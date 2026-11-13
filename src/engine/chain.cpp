// 多步串联的实现。步骤本身不在这里：接回断行走 MergePipeline，换标点与剧本排版直接用
// processing.h 上那两个写盘入口（replace_punctuation_file、reformat_vn_file）。
// 链的产物与单步处理逐字节相同，靠的就是「同一份参数、同一份读进来又写出去的真文件」。
//
// 一趟链在磁盘上的痕迹只有两种：目标文件与临时文件。临时文件在同一目录里，
// 名字带本进程号与步序号；任何一步抛出来时这一趟留下的临时文件都删掉，
// 原稿与目标文件在最后一步改名之前一个字没动。
#include "engine/chain.h"

#include <cerrno>
#include <filesystem>
#include <stdexcept>
#include <string>
#include <string_view>
#include <system_error>
#include <utility>
#include <vector>

#if defined(_WIN32)
#ifndef NOMINMAX
#define NOMINMAX
#endif
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#else
#include <csignal>
#include <unistd.h>
#endif

#include "engine/io.h"

namespace texttool {
namespace {

namespace fs = std::filesystem;

// 两套前缀：正式跑一套，预览一套。两套都只带本工具的名字，目录里别的文件碰不到。
constexpr std::string_view kFormalPrefix = ".texttool-tmp-";
constexpr std::string_view kPreviewPrefix = ".texttool-preview-";

// TextWriter 自己在目标名后面接一层 .tmp，改名前崩掉时留下的就是 <临时名>.tmp。
constexpr std::string_view kWriterSuffix = ".tmp";

long current_process_id() {
#if defined(_WIN32)
    return static_cast<long>(GetCurrentProcessId());
#else
    return static_cast<long>(::getpid());
#endif
}

// 写临时文件的那个进程还在不在。
// pid 被系统复用（老进程死了、新进程拿到同一个号）时会误判成「还在」，代价只是那一份
// 临时文件这一趟不删、下次启动再删；反过来把正在写的文件删掉才是不能接受的，
// 所以判断往保守的那一边倒。
bool process_alive(long pid) {
    if (pid <= 0) return false;
#if defined(_WIN32)
    HANDLE handle = ::OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, static_cast<DWORD>(pid));
    if (handle == nullptr) return false;
    DWORD code = 0;
    const bool alive = ::GetExitCodeProcess(handle, &code) != 0 && code == STILL_ACTIVE;
    ::CloseHandle(handle);
    return alive;
#else
    // 信号 0 什么也不发，只做「这个进程在不在、许不许我动它」这一次检查。
    // EPERM 说明进程在、只是不属于自己；ESRCH 才是真的没了。
    if (::kill(static_cast<pid_t>(pid), 0) == 0) return true;
    return errno == EPERM;
#endif
}

// 从一段文字里吃掉开头的数字。没数字、或者数字长得不像进程号与步序号，都返回假。
bool take_digits(std::string_view& text, long& value) {
    std::size_t used = 0;
    long number = 0;
    while (used < text.size() && text[used] >= '0' && text[used] <= '9') {
        if (number > 1'000'000'000L) return false;
        number = number * 10 + (text[used] - '0');
        ++used;
    }
    if (used == 0) return false;
    text.remove_prefix(used);
    value = number;
    return true;
}

// 目录项的名字是不是本工具留下的临时文件；是的话把进程号交出去。
bool parse_temp_name(std::string_view name, long& pid) {
    std::string_view rest;
    if (name.starts_with(kFormalPrefix)) {
        rest = name.substr(kFormalPrefix.size());
    } else if (name.starts_with(kPreviewPrefix)) {
        rest = name.substr(kPreviewPrefix.size());
    } else {
        return false;
    }
    if (!take_digits(rest, pid)) return false;
    if (rest.empty() || rest.front() != '-') return false;
    rest.remove_prefix(1);
    long step = 0;
    if (!take_digits(rest, step)) return false;
    return rest.empty() || rest == kWriterSuffix;
}

// 这一趟第 index 步（从 0 数）的临时文件：<目标所在目录>/<前缀><进程号>-<步序号>。
std::string temp_path(const std::string& target, std::string_view prefix, std::size_t index,
                      long pid) {
    const std::string name = std::string(prefix) + std::to_string(pid) + "-" +
                             std::to_string(index + 1);
    const std::string directory = fs::path(target).parent_path().string();
    return directory.empty() ? name : (fs::path(directory) / name).string();
}

// 接回断行写盘：逐块读、算一段写一段、按原编码写回，与命令行 merge 那一路同一套。
// processing.h 上另外两个步骤有写盘入口，接回这一路没有；差别只在十几行读循环，
// 这里就地写一遍，不去动那个头文件对外的说法。
TextEncoding merge_file(const std::string& source, const std::string& target,
                        const MergeOptions& options) {
    TextFileReader reader(source);
    MergeOptions settings = options;
    // 按字节数合并时宽度按原稿编码量：GBK 稿里一个汉字 2 字节。这一项由链按读完的编码填，
    // 调用方那格填了什么不看。
    settings.source_encoding = reader.encoding();

    TextWriter writer(target, reader.encoding());
    MergePipeline pipeline(settings, [&writer](std::string_view chunk) { writer.write(chunk); });
    std::string_view block;
    while (reader.next(block)) pipeline.feed(block);
    pipeline.finish();
    // 目标与原稿同名时先放掉读的句柄，改名才不会被自己占着（单步处理也是这么做的）。
    reader.close();
    writer.commit();
    return reader.encoding();
}

// 跑一步：把 source 读进来，按这一步的参数算一遍，写到 target。返回产物用的编码。
TextEncoding run_step(const ChainStep& step, const std::string& source, const std::string& target) {
    switch (step.kind) {
        case ChainStepKind::MergeLines:
            return merge_file(source, target, step.merge);
        case ChainStepKind::ReplacePunctuation:
            return replace_punctuation_file(source, target, step.replace);
        case ChainStepKind::ReformatVn:
            return reformat_vn_file(source, target, step.vn);
    }
    throw std::invalid_argument("认不出的步骤");
}

// 这一趟还没删的临时文件。抛出时析构把它删干净；成功的那一份由调用方接手
// （forget：只忘不删），被下一步吃掉的那一份当场删掉（remove）。
class TempFiles {
public:
    ~TempFiles() { remove_all(); }

    TempFiles(const TempFiles&) = delete;
    TempFiles& operator=(const TempFiles&) = delete;
    TempFiles() = default;

    void add(const std::string& path) { paths_.push_back(path); }

    void remove(const std::string& path) {
        erase_file(path);
        for (auto it = paths_.begin(); it != paths_.end(); ++it) {
            if (*it == path) {
                paths_.erase(it);
                return;
            }
        }
    }

    void remove_all() {
        for (const std::string& path : paths_) erase_file(path);
        paths_.clear();
    }

    void forget() { paths_.clear(); }

private:
    static void erase_file(const std::string& path) {
        std::error_code ignored;
        fs::remove(path, ignored);
    }

    std::vector<std::string> paths_;
};

// 一步都没跑时是 Utf8，与 ChainResult 的默认值一致。
struct Product {
    std::string path;
    TextEncoding encoding = TextEncoding::Utf8;
    std::size_t finished_steps = 0;
    bool stopped = false;
};

// 按次序把每一步跑完，产物留在最后一个临时文件里，交给调用方（由它改名或读开头）。
// 抛出时这一趟的临时文件已经删干净；按停止时产物与中间产物一并删掉。
Product produce(const ChainRequest& request, std::string_view prefix) {
    if (request.steps.empty()) throw std::invalid_argument("链里没有步骤");

    TempFiles temps;
    const long pid = current_process_id();
    std::string input = request.source;
    Product product;
    for (std::size_t index = 0; index < request.steps.size(); ++index) {
        // 停止只在两步之间看：上一步写完了再收手，正在写的那一步不被打断。
        if (index > 0 && request.should_stop && request.should_stop()) {
            product.stopped = true;
            temps.remove_all();
            return product;
        }

        const std::string output = temp_path(request.target, prefix, index, pid);
        temps.add(output);
        try {
            product.encoding = run_step(request.steps[index], input, output);
        } catch (const std::exception& ex) {
            throw std::runtime_error("第 " + std::to_string(index + 1) + " 步（" +
                                     std::string(chain_step_name(request.steps[index].kind)) +
                                     "）失败：" + ex.what());
        }
        // 上一步的产物被这一步吃掉了，删掉它：任何时刻盘上最多两份临时文件。
        if (index > 0) temps.remove(input);
        input = output;
        product.finished_steps = index + 1;
    }

    temps.forget();
    product.path = std::move(input);
    return product;
}

// 从末尾往回丢 drop 个字，返回留下那一段的长度。切点落在字边界上：
// 续字节（0x80..0xBF）不可能是字头，往回走时越过它们就停在一个字的开头。
// 读出来的正文一定是合法 UTF-8（解出来的那份，与验过全篇才按 UTF-8 交出来的那份），
// 所以这条判法就是字边界本身。
std::size_t length_after_dropping(std::string_view text, std::size_t drop) {
    std::size_t end = text.size();
    for (std::size_t dropped = 0; dropped < drop && end > 0; ++dropped) {
        --end;
        while (end > 0 && (static_cast<unsigned char>(text[end]) & 0xC0) == 0x80) --end;
    }
    return end;
}

// 产物开头最多 max_chars 个字。读够就停手，不再往下读。
ChainPreview read_head(const std::string& path, std::size_t max_chars) {
    ChainPreview preview;
    TextFileReader reader(path);
    preview.encoding = reader.encoding();

    std::size_t chars = 0;
    std::string_view chunk;
    while (chars < max_chars && reader.next(chunk)) {
        preview.text.append(chunk);
        chars += utf8_char_count(chunk);
    }
    if (chars > max_chars) {
        preview.text.resize(length_after_dropping(preview.text, chars - max_chars));
        preview.truncated = true;
    } else if (chars == max_chars) {
        // 正好读够：多要一块看看后面还有没有，不然「只显示开头」是真是假说不准。
        std::string_view extra;
        preview.truncated = reader.next(extra);
    }
    return preview;
}

}  // namespace

std::string_view chain_step_name(ChainStepKind kind) {
    switch (kind) {
        case ChainStepKind::MergeLines:
            return "接回断行";
        case ChainStepKind::ReplacePunctuation:
            return "换标点";
        case ChainStepKind::ReformatVn:
            return "剧本排版";
    }
    return "未知步骤";
}

ChainResult run_chain(const ChainRequest& request) {
    Product product = produce(request, kFormalPrefix);

    ChainResult result;
    result.encoding = product.encoding;
    result.finished_steps = product.finished_steps;
    result.stopped = product.stopped;
    if (product.stopped) return result;

    // 最后一步：改名成目标。目标已存在就覆盖，先留一份 .bak——与单步处理写盘是同一套，
    // 备份在改名之前做，备份下来的才是原来那份。
    if (request.backup) backup_before_overwrite(request.target);
    std::error_code ec;
    if (!replace_file(product.path, request.target, ec)) {
        std::error_code ignored;
        fs::remove(product.path, ignored);
        throw std::runtime_error("写入失败：" + request.target);
    }
    return result;
}

ChainPreview preview_chain(const ChainRequest& request, std::size_t max_chars) {
    // 预览一律用预览前缀：与正式跑同时开着也不会互相盖掉对方的名字。
    Product product = produce(request, kPreviewPrefix);

    ChainPreview preview;
    preview.encoding = product.encoding;
    preview.stopped = product.stopped;
    if (product.stopped) return preview;

    const auto discard = [&product]() {
        std::error_code ignored;
        fs::remove(product.path, ignored);
    };
    try {
        preview = read_head(product.path, max_chars);
    } catch (...) {
        discard();
        throw;
    }
    discard();
    return preview;
}

std::size_t sweep_stale_chain_temps(const std::string& directory) {
    std::error_code ec;
    fs::directory_iterator it(directory, ec);
    if (ec) return 0;

    std::size_t removed = 0;
    for (; it != fs::directory_iterator(); it.increment(ec)) {
        // 读到一半出错：已经删的照算，剩下的下次启动再说。
        if (ec) return removed;

        long pid = 0;
        if (!parse_temp_name(it->path().filename().string(), pid)) continue;
        std::error_code kind_error;
        if (!it->is_regular_file(kind_error) || kind_error) continue;
        // 另一份正在跑：它自己还要用这个文件。
        if (process_alive(pid)) continue;
        std::error_code remove_error;
        if (fs::remove(it->path(), remove_error)) ++removed;
    }
    return removed;
}

}  // namespace texttool
