// 命令行入口：lint 子命令，加上帮助与未知参数的处理。
//
// 输出与 fixtures/cli 下的期望输出逐字节对齐：人读报告、--json、退出码三样都一样，
// 帮助文本与「未知命令」的提示也逐字节对得上。对照办法与样本见 fixtures/cli，
// 比对的脚本是 tests/compare_cli.sh，改了输出就重跑它。
//
// 接上的子命令是 lint、merge、replace、join、vn、calibrate、reply-check，七个都能跑。
//
// merge、replace、join、vn 四个命令的测试在 fixtures/processing/cli，
// 脚本是 tests/compare_processing.sh。表里标 known 的测试只剩两类：
// 写盘保持用户原编码（比对的那一边一律写带 BOM 的 UTF-8），以及读不到文件时
// 两边报错措辞不同（一边是 .NET 的系统措辞，一边是自己的话）。正文与退出码两边一致。
//
// lint 收目录、--baseline 差分、--log 命中日志三样都接在引擎上：
// 目录展开在 engine/lint_inputs，差分在 engine/lint_runner，日志排版在 engine/report。
#include <algorithm>
#include <cerrno>
#include <chrono>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <map>
#include <optional>
#include <exception>
#include <stdexcept>
#include <string>
#include <string_view>
#include <vector>

#if defined(__GLIBC__)
#include <malloc.h>
#endif

#include "engine/calibration.h"
#include "engine/encoding.h"
#include "engine/io.h"
#include "engine/lint_inputs.h"
#include "engine/lint_runner.h"
#include "engine/processing.h"
#include "engine/reply_shape.h"
#include "engine/report.h"
#include "engine/rule_overrides.h"
#include "engine/rules.h"
#include "engine/schemes.h"

namespace {

// ---------------------------------------------------------------------------
// 帮助与提示
// ---------------------------------------------------------------------------

// 用法文本。版本号不写在这里：由 CMake 从 project() 里那一行传进来，
// 与界面「关于」页读的是同一个常量，改版本只改那一处。
constexpr const char* kUsageBody = R"TXT(

用法：
  texttool merge <文件...> [选项]      行合并 + 后处理
  texttool replace <文件...> [选项]    标点符号替换
  texttool vn <文件...> [选项]        视觉小说排版
  texttool join <目录> [选项]         文件拼接
  texttool lint <文件|目录...> [选项]  AI 味检查（只报不改）
  texttool calibrate [选项]            规则标定：人类文本与模型文本两侧对比
  texttool reply-check [选项] [文件]    回复里读者解不开的写法（只报不改）

运行 texttool <命令> --help 查看各命令选项。
)TXT";

constexpr const char* kLintHelp = R"TXT(lint 选项：
  --json                以 JSON 输出（顶层 Version 与 Reports；命中带 Id、Line、Col、Snippet）
  --only <Id,...>       只跑指定规则，如 --only parallel-contrast,cliche-words
  --min-severity <info|warn>  只报警告及以上的问题：提示级不列，统计照出
  --genre <doc|fiction|agent> 按文本类型选规则：doc = 编程文档，fiction = 小说，
                              agent = AI 助手回复（读者要能一句句读懂，不受团队行话与代号）；
                              不给则跑全部规则
  --baseline <文件>     只报相对这份稿子多出来的命中，每条规则给一行新增处数
  --log <文件>          把命中追加写成一行一条的 JSON 日志：留档、跑全库统计时用；
                        日志里只有 ASCII 与汉字，别的字符转成 \uXXXX

输入：文件或目录，可多个。给目录时递归收 .txt 与 .md，按路径排序；
      软链接跟到真实目标，一份稿只检一次，指回上级的链接不会重复展开；
      子目录读不了会点名路径报出来，其余文件照常检查。
      目录里一个都收不到按用法错误退 2，并点名是哪个目录。
      传 - 表示从 stdin 读。带 --baseline 时只能给一个输入（目录按展开后的份数算）。

--baseline 的用法：写前那一份当基线、写后那一份当输入，行号按写后那份算。
存量命中不再列出，退出码只看新增：改写没有让任何检查项变多就是 0。

退出码（与其它子命令不同）：0 = 无命中，1 = 有命中，2 = 用法/读取错误。
单份稿读不了不打断整批：原因连同路径打到 stderr，报告只覆盖读得到的部分，最后退 2。
退 1 的条件是过滤后还有问题：加 --min-severity warn 时，只有警告级问题才算。
)TXT";

// 命令行与界面报的是同一个版本号。没传进来就当场报错，不惄声印一个空版本。
#ifndef TEXTTOOL_VERSION
#error "TEXTTOOL_VERSION 没传进来：CMakeLists.txt 里 texttool 的 target_compile_definitions 要带上它"
#endif

std::string usage_text() { return std::string("TextTool 命令行工具 v") + TEXTTOOL_VERSION + kUsageBody; }

constexpr const char* kMergeHelp = R"TXT(merge 选项：
  --threshold <N>      每行拼到多少字（默认 20）
  --chars / --bytes    按字符数 / 字节数（默认字符数）
  --overwrite          直接覆盖原文件，不留备份（危险）
  --backup             覆盖原文件，覆盖前备份为 .bak（隐含 --overwrite）
  --no-cjk             禁用中文截断修复
  --fix-punct          启用标点截断修复
  --punct-chars <s>    标点截断字符集（默认 "，"）
  --no-trim-comma      禁用行首逗号清理
  --no-merge           行尾是句末标点时不与下一行合并
  --no-merge-chars <s> 算作句末的标点（默认 "。！？"，隐含 --no-merge）
  --apply-replace      按规则表把标点换一遍（规则来自 replace_rules.json）
)TXT";

constexpr const char* kReplaceHelp = R"TXT(replace 选项：
  --scheme <名>        使用指定预设方案（默认用配置的替换规则）
)TXT";

constexpr const char* kVnHelp = R"TXT(vn 选项：
  --scheme <名>        使用指定角色方案（可多次指定）
  --max-para <N>       段落最大字数（默认 450）
  --reformat-only      仅排版
  --punct-only         仅补标点
)TXT";

constexpr const char* kJoinHelp = R"TXT(join 选项：
  --pattern <glob>     文件匹配模式（默认 *.txt）
  --output <名>        输出文件名（默认 joined.txt）
)TXT";

constexpr const char* kCalibrateHelp = R"TXT(calibrate 选项：
  --human <目录>        人类写的文本目录（递归收 .txt）
  --ai <目录>           模型写的文本目录（递归收 .txt）
  --genre <doc|fiction|agent>  只跑该档规则；不给则整张规则表都跑
  --json                以 JSON 输出（Version 1：Genre、Human、Ai、SeparableCount、Rules）

判定：人类侧零命中、模型侧有命中记「可分」；两侧都有命中且模型侧每千字命中
达到人类侧三倍记「弱」；其余记「不可分」。密度用未取整的值算，表里只取两位小数。

退出码：0 = 跑完，2 = 用法或读取错误（缺 --human/--ai、目录不存在、没有 .txt、文本类型拼错）。
)TXT";

constexpr const char* kReplyCheckHelp = R"TXT(reply-check 选项：
  --json                以 JSON 输出（Version 2：Version 与 Hits 两项，每条带 Id、Token、Line、Blocks）
  --help                显示本说明

输入：一个文件路径，或传 - 从 stdin 读；不给也走 stdin。
围栏代码块里的内容整块跳过：那是引用的原文，不是回复的措辞。

退出码与 lint 一致：0 = 没有发现，1 = 有发现，2 = 用法或读取错误。
)TXT";

// ---------------------------------------------------------------------------
// 分段计时
//
// 打开稿件、读稿与排版这几段在命令行里，扫描与排版的细分在引擎里（引擎那一侧同样由
// TEXTTOOL_TIMING 打开）。平时跑不输出一个字节。
// ---------------------------------------------------------------------------

using Clock = std::chrono::steady_clock;

bool timing_on() {
    static const bool on = std::getenv("TEXTTOOL_TIMING") != nullptr;
    return on;
}

double seconds_since(Clock::time_point started) {
    return std::chrono::duration<double>(Clock::now() - started).count();
}

void note_seconds(const char* stage, double seconds) {
    if (!timing_on()) return;
    std::fprintf(stderr, "[计时] %-18s %9.2f ms\n", stage, seconds * 1000.0);
}

// 标准输出按字节写出去：不走格式化，也不让宿主把行尾改成 CRLF。
void write_stdout(std::string_view text) {
    if (!text.empty()) std::fwrite(text.data(), 1, text.size(), stdout);
    std::fflush(stdout);
}

void write_stderr(std::string_view text) {
    if (!text.empty()) std::fwrite(text.data(), 1, text.size(), stderr);
    std::fflush(stderr);
}

// 与 lint 相同的报错写法：一行「错误：…」，退 2。
int fail(const std::string& message) {
    write_stderr("错误：" + message + "\n");
    return 2;
}

// ---------------------------------------------------------------------------
// 小工具
// ---------------------------------------------------------------------------

char ascii_lower(char c) { return (c >= 'A' && c <= 'Z') ? static_cast<char>(c - 'A' + 'a') : c; }

std::string lower(std::string_view text) {
    std::string out(text);
    for (char& c : out) c = ascii_lower(c);
    return out;
}

bool equals_ignore_case(std::string_view a, std::string_view b) {
    if (a.size() != b.size()) return false;
    for (std::size_t i = 0; i < a.size(); ++i)
        if (ascii_lower(a[i]) != ascii_lower(b[i])) return false;
    return true;
}


std::vector<std::string> split_commas(std::string_view text) {
    std::vector<std::string> out;
    std::size_t begin = 0;
    while (begin <= text.size()) {
        std::size_t comma = text.find(',', begin);
        if (comma == std::string_view::npos) comma = text.size();
        std::string item = texttool::trim_whitespace(text.substr(begin, comma - begin));
        if (!item.empty()) out.push_back(std::move(item));
        begin = comma + 1;
    }
    return out;
}

std::string join(const std::vector<std::string>& items, std::string_view separator) {
    std::string out;
    for (std::size_t i = 0; i < items.size(); ++i) {
        if (i != 0) out.append(separator);
        out.append(items[i]);
    }
    return out;
}

// ---------------------------------------------------------------------------
// 规则表的位置
//
// 命令行不像界面那样在编译期写死规则表路径：程序装在哪台机器、哪个目录都可能变。
// 查找次序：先看环境变量 TEXTTOOL_RULES_JSON，再看程序所在目录与它下面的 assets，
// 最后从这个目录逐层往上找六层；
// 当前工作目录按同样的规则也找一遍。
// 都找不到就报错，并告诉对方用环境变量指过去，不拿半张表当作检查过。
// ---------------------------------------------------------------------------

// 找规则表的次序写一次在引擎里（texttool::find_rules_file），界面与命令行共用，
// 免得两边各找各的、打包以后一边找得到一边找不到。
std::optional<std::filesystem::path> find_rules_file(const char* argv0) {
    return texttool::find_rules_file(texttool::program_dir(argv0));
}

// 检查要用的那张表：内置规则表，加上程序目录里用户改过的条目（lint_rules.json）。
// 命令行自己不接改条目的开关，但界面上改完的那一条下一次跑命令行就该生效，
// 所以这里一律按合并后的表跑。找不到规则表、或者覆盖文件坏了，都说清是哪一份。
bool load_rule_table(const char* argv0, std::vector<texttool::LintRule>& out, std::string& error) {
    const auto path = find_rules_file(argv0);
    if (!path) {
        error = "找不到规则表 default_lint_rules.json，用环境变量 TEXTTOOL_RULES_JSON 指过去";
        return false;
    }
    return texttool::load_rules_with_overrides(*path, argv0, out, error);
}

// ---------------------------------------------------------------------------
// 读稿件
// ---------------------------------------------------------------------------


struct Input {
    std::string label;  // 报告里的 File 字段：命令行给的名字，stdin 是 (stdin)
    std::string text;   // UTF-8
};

std::string read_bytes(std::istream& in, std::size_t expected = 0) {
    std::string bytes;
    if (expected != 0) bytes.reserve(expected);
    char buffer[64 * 1024];
    while (in) {
        in.read(buffer, sizeof(buffer));
        bytes.append(buffer, static_cast<std::size_t>(in.gcount()));
    }
    return bytes;
}

// 打开一份稿，只确认能不能读：文件在不在、能不能打开。读到一半出的事要真读的时候才知道。
bool open_input(const std::string& path, std::ifstream& in, std::string& error) {
    std::error_code ignored;
    if (!std::filesystem::exists(path, ignored)) {
        error = "文件不存在：" + path;
        return false;
    }

    errno = 0;
    in.open(path, std::ios::binary);
    if (!in) {
        error = std::strerror(errno == 0 ? EACCES : errno);
        return false;
    }
    return true;
}

// 失败行里出现的那份稿的名字：stdin 是 (stdin)。
std::string input_label(const std::string& path) { return path == "-" ? "(stdin)" : path; }

// 只确认这份稿读得动，内容一个字节也不读：多份时报告怎么排，要先知道有几份出得来报告。
// stdin 能不能读要读了才知道，所以一律算读得动。
bool can_read(const std::string& path, std::string& error) {
    if (path == "-") return true;
    std::ifstream in;
    return open_input(path, in, error);
}

// 手上已经攒着整份字节时的解：管道的稿子走这一处（见下面 read_input）。
// 开头的字节序标记要在这里吃掉，留着会让全篇的列号偏一、字数多一。
// 已经是合法 UTF-8 的稿子不必再解一遍：探测那一步已经把全篇验过，手上的字节就是结果。
std::string decode_bytes(std::string bytes) {
    const texttool::TextEncoding encoding = texttool::detect_encoding(bytes);
    if (encoding == texttool::TextEncoding::Utf8) return bytes;
    if (encoding == texttool::TextEncoding::Utf8Bom) {
        bytes.erase(0, 3);
        return bytes;
    }
    // 别的编码要整份换成 UTF-8，峰值就是「源 + 结果」两份。
    return texttool::to_utf8(bytes, encoding);
}

// 读一份稿：按同一条规则判编码，再解成 UTF-8。文件不存在与读不了分开说。
//
// 普通文件交给引擎的 load_text：它按块读、按块解码，源字节不整份留在内存里，
// 15MB 的 GBK 稿换成 UTF-8 时峰值只按解出来的那一份算，与界面同一个口径。
// 管道、/dev/fd、终端这类回不到开头的东西（进程替换 <(...) 就是）读不了第二遍：
// 判编码那一趟看完就过去了，只能整份留在手上再解，与以前一样。
bool read_input(const std::string& path, Input& out, std::string& error) {
    if (path == "-") {
        out.label = "(stdin)";
        out.text = decode_bytes(read_bytes(std::cin));
        return true;
    }

    std::ifstream in;
    if (!open_input(path, in, error)) return false;

    out.label = path;
    std::error_code kind_error;
    if (std::filesystem::is_regular_file(path, kind_error)) {
        try {
            out.text = texttool::load_text(path).text;
        } catch (const std::exception& ex) {
            error = ex.what();
            return false;
        }
        return true;
    }

    std::error_code size_error;
    const auto bytes_on_disk = std::filesystem::file_size(path, size_error);
    std::string bytes = read_bytes(in, size_error ? 0 : static_cast<std::size_t>(bytes_on_disk));
    if (in.bad()) {
        error = "读取中断：" + path;
        return false;
    }
    out.text = decode_bytes(std::move(bytes));
    return true;
}

// ---------------------------------------------------------------------------
// 一份报告
// ---------------------------------------------------------------------------


// 报告层的输出口子：写完一块就落到 stdout。人读报告按命中逐行写，几十兆的报告
// 不整份留在内存里。
void write_chunk(std::string_view chunk, void*) { write_stdout(chunk); }

// 多份时的 JSON：报告层只渲染一份稿（外层带 Version 与 Reports），这里把每份的正文取出来
// 拼进同一个 Reports 数组。首尾是报告层的固定输出，取正文靠这两个固定片段定位；
// 两者对不上就是形状不符，直接报错退出。
// 一份 15MB 的稿子能出上百兆 JSON，所以一份也要边算边写出去，不能整份拼在内存里。
// 做法：每份开头的 kHead 先按住，凑齐了比一遍就丢掉；结尾的 kTail 与它前面的那个字节
// 一直按到最后：中间那几份的收尾换行不发，只发一个逗号与换行，最后一份原样写出去，
// 再补外壳的收尾。
class JsonArrayWriter {
public:
    static constexpr std::string_view kHead = "{\n  \"Version\": 4,\n  \"Reports\": [\n";
    static constexpr std::string_view kTail = "  ]\n}\n";

    // 报告层的输出口子。
    static void sink(std::string_view chunk, void* context) {
        static_cast<JsonArrayWriter*>(context)->feed(chunk);
    }

    // 开始渲染一份：这一份的开头先按住 kHead 那么多字节。
    void begin_report() {
        skip_ = kHead.size();
        if (has_pending_) {  // 上一份末尾那个字节不发，这里发一个逗号与换行
            write_stdout(",\n");
            has_pending_ = false;
        }
    }

    // 一份正文渲染完了：尾部应该正好是 kTail，它前面那个字节按住，等下一份决定怎么发。
    void end_report() {
        const bool tail_matches =
            held_.size() == kTail.size() + 1 && held_.compare(1, kTail.size(), kTail) == 0;
        if (skip_ != 0 || !tail_matches) {
            ok_ = false;
            return;
        }
        pending_ = held_.front();
        has_pending_ = true;
        held_.clear();
    }

    // 整批收尾：把最后一份的结尾字节发出去，再补外壳的收尾。
    void finish() {
        if (has_pending_) write_stdout(std::string_view(&pending_, 1));
        has_pending_ = false;
        write_stdout(kTail);
    }

    bool ok() const { return ok_; }

private:
    void feed(std::string_view chunk) {
        held_.append(chunk);
        skip_head();
        if (!ok_ || skip_ != 0) return;  // 形状不符，或开头还没凑齐
        keep_tail();
    }

    // 丢掉报告层开头那几个字节；第一份的开头对上了，外壳的开头才写出去。
    void skip_head() {
        if (skip_ == 0) return;
        const std::size_t take = skip_ < held_.size() ? skip_ : held_.size();
        const std::size_t at = kHead.size() - skip_;
        if (std::string_view(held_).substr(0, take) != kHead.substr(at, take)) {
            ok_ = false;
            held_.clear();
            skip_ = 0;
            return;
        }
        held_.erase(0, take);
        skip_ -= take;
        if (skip_ == 0 && !shell_written_) {
            write_stdout(kHead);
            shell_written_ = true;
        }
    }

    // 尾部 kTail 与它前面那个字节留着不发，其余的发出去。
    void keep_tail() {
        constexpr std::size_t keep = kTail.size() + 1;
        if (held_.size() <= keep) return;
        const std::size_t send = held_.size() - keep;
        write_stdout(std::string_view(held_).substr(0, send));
        held_.erase(0, send);
    }

    std::string held_;         // 还没发出去的尾巴
    std::size_t skip_ = 0;     // 这一份的开头还要丢掉多少字节
    char pending_ = 0;         // 上一份末尾那个字节，等下一份开始时再决定发什么
    bool has_pending_ = false;
    bool shell_written_ = false;
    bool ok_ = true;
};

// ---------------------------------------------------------------------------
// 选项校验
//
// 引擎也会校验同一批条件，但报错文字不带命令行选项的名字。这里是用户在命令行上看到的
// 那一句，次序固定：min-severity、genre、only、规则表、only 的编号与档位。
// 校验全过之后引擎不会再因这几条抛错，扫描阶段的报错才原样透出去。
// ---------------------------------------------------------------------------

int validate_options(const std::optional<std::string>& min_severity,
                     const std::optional<std::string>& genre,
                     const std::optional<std::string>& only,
                     const std::vector<std::string>& only_ids,
                     const std::vector<texttool::LintRule>& rules) {
    if (min_severity && !equals_ignore_case(*min_severity, "info") &&
        !equals_ignore_case(*min_severity, "warn"))
        return fail("--min-severity 只接受 info 或 warn");

    if (genre && !equals_ignore_case(*genre, "doc") && !equals_ignore_case(*genre, "fiction") &&
        !equals_ignore_case(*genre, "agent"))
        return fail("--genre 只接受 doc、fiction、agent");

    if (only && only_ids.empty()) return fail("--only 需要至少一个规则 Id");

    if (!only) return 0;

    std::vector<std::string> unknown;
    for (const auto& id : only_ids) {
        bool found = false;
        for (const auto& rule : rules)
            if (equals_ignore_case(rule.id, id)) found = true;
        if (!found) unknown.push_back(id);
    }
    if (!unknown.empty()) return fail("未知规则 Id：" + join(unknown, ", "));

    // 被档位挡掉、又被 --only 点名的规则要响亮报出来：否则用户看到的是零命中，
    // 而真相是这条规则不在这个档。挡掉的规则一次全列出来。
    // 没给档位就没有「挡掉」这回事，整张表都跑。
    if (!genre) return 0;
    std::vector<std::string> blocked;
    std::vector<std::string> blocked_genres;
    for (const auto& rule : rules) {
        bool named = false;
        for (const auto& id : only_ids)
            if (equals_ignore_case(rule.id, id)) named = true;
        if (!named || rule.genres.empty()) continue;
        bool allowed = false;
        for (const auto& item : rule.genres)
            if (equals_ignore_case(item, *genre)) allowed = true;
        if (allowed) continue;
        blocked.push_back(rule.id);
        if (blocked_genres.empty()) blocked_genres = rule.genres;
    }
    if (!blocked.empty())
        return fail("规则 " + join(blocked, ", ") + " 不在 " + *genre + " 档生效（适用档位：" +
                    join(blocked_genres, "/") + "）");
    return 0;
}

// ---------------------------------------------------------------------------
// lint
// ---------------------------------------------------------------------------

// 目录展开时碰到读不了的子目录：命令行打成一行警告，其余照常往下查。
void warn_unreadable_directory(const std::string& message, void*) {
    write_stderr(message + "\n");
}

// 写前那一份。读不到时要说清是哪一份，不能只说「文件不存在」：
// 一次带 --baseline 的调用涉及两个路径，读者改错哪一个都能停住，但得知这是哪一个。
bool read_baseline(const std::string& path, std::string& text, std::string& error) {
    std::error_code ignored;
    if (!std::filesystem::is_regular_file(path, ignored)) {
        error = "基线文件读不到：" + path;
        return false;
    }

    std::ifstream in;
    std::string open_error;
    if (!open_input(path, in, open_error)) {
        error = "基线文件读不到：" + path + "（" + open_error + "）";
        return false;
    }
    // 与读正文那份同一个口径：按块读、按块解码，源字节不整份留着。
    // 两趟之间文件出了事，load_text 抛上来的说法就是「读取中断」，与以前一样按读取中断报。
    try {
        text = texttool::load_text(path).text;
    } catch (const std::exception&) {
        error = "基线文件读不到：" + path + "（读取中断）";
        return false;
    }
    return true;
}

// 命中日志的落点：文件不存在就新建，目录不在时报错交给调用方，不悄悄换地方。
bool open_hit_log(const std::string& path, std::ofstream& out, std::string& error) {
    std::error_code ec;
    const std::filesystem::path full = std::filesystem::absolute(std::filesystem::path(path), ec);
    const std::string directory =
        (ec ? std::filesystem::path(path) : full).parent_path().string();
    if (!directory.empty()) {
        std::error_code dir_error;
        const bool exists = std::filesystem::is_directory(directory, dir_error);
        if (dir_error || !exists) {
            error = "日志目录不存在：" + directory;
            return false;
        }
    }
    out.open(path, std::ios::binary | std::ios::app);
    if (!out) {
        error = "日志写不进去：" + path;
        return false;
    }
    return true;
}

// 日志的分块出口：报告层攒够一块交过来，这里写盘。
void write_log_chunk(std::string_view chunk, void* context) {
    auto* out = static_cast<std::ofstream*>(context);
    out->write(chunk.data(), static_cast<std::streamsize>(chunk.size()));
}

int run_lint_command(const std::vector<std::string>& args, const char* argv0) {
    bool json = false;
    std::optional<std::string> only, min_severity, genre, baseline, log;
    std::vector<std::string> inputs;

    for (std::size_t i = 0; i < args.size(); ++i) {
        const std::string& arg = args[i];
        // 取值型选项：后面那个词就是它的值，缺了就报错，不吃掉下一个选项。
        const auto value = [&args, &i]() -> std::optional<std::string> {
            if (i + 1 >= args.size()) return std::nullopt;
            return args[++i];
        };

        if (arg == "--json") {
            json = true;
        } else if (arg == "--only" || arg == "--min-severity" || arg == "--genre" ||
                   arg == "--baseline" || arg == "--log") {
            auto parsed = value();
            if (!parsed) return fail(arg + " 需要参数值");
            if (arg == "--only") only = *parsed;
            else if (arg == "--min-severity") min_severity = *parsed;
            else if (arg == "--genre") genre = *parsed;
            else if (arg == "--baseline") baseline = *parsed;
            else log = *parsed;
        } else if (arg == "--help" || arg == "-h") {
            write_stdout(kLintHelp);
            return 0;
        } else {
            // 认不出的参数按输入路径处理，
            // 于是 --nofalg 这种拼错会落成「文件不存在：--nofalg」，不会被静默吃掉。
            inputs.push_back(arg);
        }
    }

    if (inputs.empty()) return fail("lint 需要至少一个输入文件或目录（- 表示 stdin）");

    // 目录换成它下面所有 .txt 与 .md：同一个目录内部按相对路径排序，软链接跟到真实目标，
    // 读不了的子目录点名报出来、其余照收。份数按展开后的算，--baseline 那一句也按这个数。
    std::vector<std::string> expanded;
    try {
        expanded = texttool::collect_lint_inputs(inputs, warn_unreadable_directory, nullptr).files;
    } catch (const std::exception& ex) {
        return fail(ex.what());
    }

    // 一次差分只回答「这一份比那份多了什么」：先把目录摊平再数
    if (baseline && expanded.size() != 1)
        return fail("--baseline 只配一个输入文件：一次差分只回答「这一份比那份多了什么」");

    // 稿子是一份一份读、看完就放掉的，所以先只确认每份能不能读：多份时报告怎么排，
    // 要看有几份出得来报告（只有一份时不印文件名那一行）。这一步只打开看一眼，内容不读。
    // 一份读不了不拖住整批，原因带路径打到 stderr，跑完再按读取错误退 2。
    const Clock::time_point open_started = Clock::now();
    std::vector<bool> readable(expanded.size(), false);
    std::size_t readable_count = 0;
    int unreadable = 0;
    for (std::size_t i = 0; i < expanded.size(); ++i) {
        const std::string& path = expanded[i];
        std::string error;
        if (!can_read(path, error)) {
            ++unreadable;
            write_stderr("[失败] " + input_label(path) + "：" + error + "\n");
            continue;
        }
        readable[i] = true;
        ++readable_count;
    }

    if (readable_count == 0) {
        write_stderr("错误：" + std::to_string(unreadable) +
                     " 份稿都读不了，没有可以出报告的输入\n");
        return 2;
    }
    note_seconds("打开稿件", seconds_since(open_started));

    std::vector<texttool::LintRule> rules;
    {
        std::string error;
        if (!load_rule_table(argv0, rules, error)) return fail(error);
    }

    const std::vector<std::string> only_ids = only ? split_commas(*only) : std::vector<std::string>();
    if (const int code = validate_options(min_severity, genre, only, only_ids, rules); code != 0)
        return code;

    texttool::LintOptions options;
    options.only_rules = only_ids;
    const std::string severity = min_severity.value_or("info");
    options.min_severity = severity;
    options.genre = genre.value_or(std::string());

    // 要写命中日志时，这一趟的命中表留全：日志按全部命中写，写完再裁给报告与 JSON。
    if (log) options.keep_all_hits = true;

    // 差分那一趟：正文那一份按写前那一份裁，裁之前的命中全留着；只留点名的规则与警告级
    // 这两刀由 apply_baseline 在差分之后做。
    if (baseline) {
        options.role = texttool::LintScanRole::Compared;
        options.cut_to_named = false;
        options.min_severity = "info";
    }

    // 写前那一份：同一套规则扫一遍，只计数不裁，接着把正文那份的存量命中抵掉。
    // 读不到时要说是哪一份，不能只说「文件不存在」：一次调用涉及两个路径。
    std::optional<texttool::BaselineCounts> baseline_counts;
    if (baseline) {
        std::string baseline_text;
        std::string error;
        if (!read_baseline(*baseline, baseline_text, error)) return fail(error);

        texttool::LintOptions baseline_options;
        baseline_options.only_rules = only_ids;
        baseline_options.min_severity = "info";
        baseline_options.genre = options.genre;
        baseline_options.role = texttool::LintScanRole::Baseline;
        baseline_options.cut_to_named = false;
        try {
            baseline_counts = texttool::count_baseline(
                texttool::run_lint(rules, baseline_text, baseline_options));
        } catch (const std::exception& ex) {
            return fail(ex.what());
        }
    }

    const auto collapse = texttool::collapse_thresholds(rules);
    // 一批稿子共用一次规则编译：四百多个模式只编译一遍，输入越多省得越多。
    // 只有一份稿子时不建这一份：编译结果的四百多个模式要一直占着内存（实测两三兆），
    // 而只有一份时没有第二份来摊这次编译，逐条用完就放更省。
    // 规则表活到函数末尾，编译结果的生命周期不超出它。
    std::optional<texttool::CompiledRules> compiled_rules;
    if (readable_count > 1) compiled_rules.emplace(rules);

    // 一份一份跑：读一份、扫一份、把这一份的报告写出去，然后放掉再读下一份。
    // 整批攒着等于把每份稿子与它的报告各占一遍，输入越多峰值越高。
    // 人读报告之间空一行，每份前面顶格印一行文件名（只有一份时不印）。
    const Clock::time_point run_started = Clock::now();
    JsonArrayWriter json_out;
    bool any_hit = false;
    bool wrote_any = false;
    // 命中日志第一次真要写的时候才开文件：一份报告都没出的那一次不留一个空文件。
    std::ofstream hit_log;
    bool log_opened = false;
    std::string log_error;
    for (std::size_t i = 0; i < expanded.size(); ++i) {
        if (!readable[i]) continue;

        Input input;
        std::string error;
        if (!read_input(expanded[i], input, error)) {
            // 打开了却读到一半出事，只在这一刻才知道，照旧带路径打到 stderr。
            ++unreadable;
            write_stderr("[失败] " + input_label(expanded[i]) + "：" + error + "\n");
            continue;
        }

        texttool::LintOutcome outcome;
        try {
            outcome = compiled_rules ? texttool::run_lint(*compiled_rules, input.text, options)
                                     : texttool::run_lint(rules, input.text, options);
            if (baseline_counts)
                texttool::apply_baseline(outcome, *baseline_counts, only_ids, severity);
        } catch (const std::exception& ex) {
            return fail(ex.what());
        }
        texttool::ReportInput report =
            texttool::make_report(input.label, std::move(outcome), collapse, !log);
        // 命中日志另存一份：留档与脚本读的是全部位置，不受报告封顶影响。
        // 按块写出去，不每行开关一次文件，也不把整份日志攒在内存里。
        if (log && !log_opened && log_error.empty())
            log_opened = open_hit_log(*log, hit_log, log_error);
        if (log_opened) {
            texttool::render_hit_log(report, write_log_chunk, &hit_log);
            hit_log.flush();
            if (!hit_log) {
                log_error = "日志写不进去：" + *log;
                log_opened = false;
                hit_log.close();
            }
        }
        // 日志写完了，报告与 JSON 按原来那条上限裁。
        if (log) texttool::cap_report_hits(report);
        any_hit = any_hit || !report.hits.empty();

        if (json) {
            json_out.begin_report();
            texttool::render_json_report(report, JsonArrayWriter::sink, &json_out);
            json_out.end_report();
            if (!json_out.ok()) return fail("报告层输出的 JSON 形状变了，多份拼接接不上");
        } else {
            if (wrote_any) write_stdout("\n");
            if (readable_count > 1) write_stdout(report.source_name + "\n");
            texttool::render_text_report(report, write_chunk, nullptr);
        }
        wrote_any = true;
    }
    if (json) json_out.finish();
    note_seconds("读稿与排版", seconds_since(run_started));

    if (hit_log.is_open()) hit_log.close();
    // 日志开不了或写不进去：报告照常给完了，这里响亮停下，不装作记过账。
    if (!log_error.empty()) return fail(log_error);
    if (log && log_opened) write_stderr("[日志] 命中追加到 " + *log + "\n");

    // 读不了的汇总放到最后：报告照常给，退出码按读取错误退 2。
    if (unreadable > 0) {
        write_stderr("错误：" + std::to_string(unreadable) +
                     " 份稿读不了，上面的报告只覆盖读得到的那几份\n");
        return 2;
    }
    return any_hit ? 1 : 0;
}

// ---------------------------------------------------------------------------
// 处理子命令（merge / replace）
//
// 与 lint 不同的三处：
//   * 参数错误是一行「错误：…」退 1，不是 lint 的退 2；
//   * 输入一份份地跑，一份跑不动不拖住整批，最后统一退 1；
//   * 成功时每份印一行「[成功] 标签 → 输出文件」，标签在只给一份时是文件名，
//     给多份时是命令行上的原样路径（同一批里不同目录下的同名文件才分得清）。
//
// 写盘一律 UTF-8 带 BOM：不管原稿是 GBK 还是 UTF-16，都按带 BOM 的 UTF-8
// 写出，两份命令都是如此。原编码只在读进来那一步用，用完就不管了。
// ---------------------------------------------------------------------------

// 这些错一律打一行「错误：…」退 1。
int fail_processing(const std::string& message) {
    write_stderr("错误：" + message + "\n");
    return 1;
}

// 按 .NET 的 int.TryParse 收整数：允许首尾空白与一个正负号，只认十进制数字，超出范围算失败。
std::optional<int> parse_int(std::string_view text) {
    const auto is_space = [](char c) {
        return c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == '\v' || c == '\f';
    };
    std::size_t begin = 0, end = text.size();
    while (begin < end && is_space(text[begin])) ++begin;
    while (end > begin && is_space(text[end - 1])) --end;
    if (begin == end) return std::nullopt;

    bool negative = false;
    if (text[begin] == '+' || text[begin] == '-') {
        negative = text[begin] == '-';
        ++begin;
    }
    if (begin == end) return std::nullopt;

    long long value = 0;
    for (std::size_t i = begin; i < end; ++i) {
        if (text[i] < '0' || text[i] > '9') return std::nullopt;
        value = value * 10 + (text[i] - '0');
        if (value > 2147483648LL) return std::nullopt;
    }
    if (negative) value = -value;
    if (value < -2147483648LL || value > 2147483647LL) return std::nullopt;
    return static_cast<int>(value);
}

// ---------------------------------------------------------------------------
// 读稿与写盘
//
// 读：按严格标准判编码，再解成 UTF-8；BOM 在这一步去掉。
// 写：先写同目录的 .tmp 再整体替换，中途失败把 .tmp 删掉；内容一律 UTF-8 带 BOM。
// 覆盖前留备份：最新一份是 .bak，历史向后轮换成 .bak.1 / .bak.2 / .bak.3，
// 只留三份历史；原文件是空的就不备份。
// ---------------------------------------------------------------------------


enum class WriteMode { NewFile, OverwriteWithBackup, Overwrite };

// 命令行给的路径都得是可读的普通文件：目录与软链接到目录都算「文件不存在」，
// 整批在开工前就停住，一份也不处理（先查完再跑）。
int require_files(const std::vector<std::string>& files, const std::string& command) {
    if (files.empty()) return fail_processing(command + " 需要至少一个输入文件");
    for (const auto& file : files) {
        std::error_code ignored;
        if (!std::filesystem::is_regular_file(file, ignored))
            return fail_processing("文件不存在：" + file);
    }
    return 0;
}

// ---------------------------------------------------------------------------
// merge
// ---------------------------------------------------------------------------

int run_merge_command(const std::vector<std::string>& args, const char* argv0) {
    texttool::MergeOptions merge;
    bool overwrite = false, backup = false;
    std::vector<std::string> files;

    for (std::size_t i = 0; i < args.size(); ++i) {
        const std::string& arg = args[i];
        const auto value = [&args, &i]() -> std::optional<std::string> {
            if (i + 1 >= args.size()) return std::nullopt;
            return args[++i];
        };

        if (arg == "--threshold") {
            auto parsed = value();
            if (!parsed) return fail_processing("--threshold 需要参数值");
            auto number = parse_int(*parsed);
            if (!number) return fail_processing("--threshold 需要整数");
            merge.threshold = *number;
        } else if (arg == "--chars") {
            merge.by_chars = true;
        } else if (arg == "--bytes") {
            merge.by_chars = false;
        } else if (arg == "--overwrite") {
            overwrite = true;
        } else if (arg == "--backup") {
            backup = true;  // 隐含 --overwrite
        } else if (arg == "--no-cjk") {
            merge.fix_cjk = false;
        } else if (arg == "--fix-punct") {
            merge.fix_punct = true;
        } else if (arg == "--punct-chars") {
            auto parsed = value();
            if (!parsed) return fail_processing("--punct-chars 需要参数值");
            merge.punct_chars = *parsed;
        } else if (arg == "--no-trim-comma") {
            merge.trim_leading_comma = false;
        } else if (arg == "--no-merge") {
            merge.no_merge = true;
        } else if (arg == "--no-merge-chars") {
            auto parsed = value();
            if (!parsed) return fail_processing("--no-merge-chars 需要参数值");
            merge.no_merge_chars = *parsed;
            merge.no_merge = true;  // 给了字符集就等于要按它分段，与界面上勾上那个勾一致
        } else if (arg == "--apply-replace") {
            merge.apply_replace = true;
        } else if (arg == "--help" || arg == "-h") {
            write_stdout(kMergeHelp);
            return 0;
        } else {
            // 认不出的参数按输入路径处理：拼错的选项会落成「文件不存在：--xxx」，不被静默吃掉。
            files.push_back(arg);
        }
    }

    if (const int code = require_files(files, "merge"); code != 0) return code;

    // 规则表在开工前读一遍，文件损坏就整批停在这一步，报错文字与其他命令一致。
    // 只有 --apply-replace 会用到这些规则；不带它时读进来只是照旧校验一遍。
    std::vector<texttool::ReplaceRule> rules;
    std::string error;
    if (!texttool::load_replace_rules(argv0, rules, error)) return fail_processing(error);
    merge.replace_rules = rules;

    const WriteMode mode = (!overwrite && !backup) ? WriteMode::NewFile
                          : backup               ? WriteMode::OverwriteWithBackup
                                                 : WriteMode::Overwrite;

    bool failed = false;
    const bool many = files.size() > 1;
    for (const auto& file : files) {
        const std::string label = many ? file : texttool::file_name_of(file);
        try {
            // 逐块读：只把编码判一次，正文一块读完就交给管线，不在内存里留住整份。
            texttool::TextFileReader reader(file);
            // 按字节数合并时，宽度按这份稿子原来的编码量：GBK 稿里一个汉字 2 字节。
            merge.source_encoding = reader.encoding();
            const std::filesystem::path output(texttool::processed_path(file));
            const std::filesystem::path target = mode == WriteMode::NewFile ? output
                                                                            : std::filesystem::path(file);
            if (mode == WriteMode::OverwriteWithBackup) texttool::backup_before_overwrite(file);
            // 算一段写一段：源文与结果都不整份留在内存里。
            texttool::TextWriter writer(target.string(), reader.encoding());
            texttool::MergePipeline pipeline(
                merge, [&writer](std::string_view chunk) { writer.write(chunk); });
            std::string_view block;
            while (reader.next(block)) pipeline.feed(block);
            pipeline.finish();
            // 覆盖处理时目标就是这个文件：先放掉读的句柄，改名才不会被自己占着。
            reader.close();
            writer.commit();
            write_stdout("[成功] " + label + " → " + target.string() + "\n");
        } catch (const std::exception& ex) {
            failed = true;
            write_stderr("[失败] " + label + "：" + ex.what() + "\n");
        }
    }
    return failed ? 1 : 0;
}

// ---------------------------------------------------------------------------
// replace
//
// 只写新文件，没有覆盖与备份这两个选项：参数里出现 --overwrite
// 会当成输入路径，落成「文件不存在：--overwrite」。
// ---------------------------------------------------------------------------

int run_replace_command(const std::vector<std::string>& args, const char* argv0) {
    std::vector<std::string> files;
    std::optional<std::string> scheme_name;

    for (std::size_t i = 0; i < args.size(); ++i) {
        const std::string& arg = args[i];
        if (arg == "--scheme") {
            if (i + 1 >= args.size()) return fail_processing("--scheme 需要参数值");
            scheme_name = args[++i];
        } else if (arg == "--help" || arg == "-h") {
            write_stdout(kReplaceHelp);
            return 0;
        } else {
            files.push_back(arg);
        }
    }

    if (const int code = require_files(files, "replace"); code != 0) return code;

    std::vector<texttool::ReplaceRule> rules;
    std::string error;
    if (!texttool::load_replace_rules(argv0, rules, error)) return fail_processing(error);

    if (scheme_name) {
        std::vector<texttool::ReplaceScheme> schemes;
        if (!texttool::load_replace_schemes(argv0, schemes, error)) return fail_processing(error);
        const texttool::ReplaceScheme* found = texttool::find_replace_scheme(schemes, *scheme_name);
        if (found == nullptr) return fail_processing("未找到方案：" + *scheme_name);
        rules = found->rules;
    }

    if (rules.empty())
        return fail_processing("没有可用的替换规则（请先在 GUI 配置规则，或指定 --scheme）");

    const texttool::ReplaceOptions options{rules};

    bool failed = false;
    const bool many = files.size() > 1;
    for (const auto& file : files) {
        const std::string label = many ? file : texttool::file_name_of(file);
        try {
            // 逐段读、逐段换、逐段写：15MB 的稿子只有一块正文与当前这一行在内存里。
            const std::filesystem::path output(texttool::processed_path(file));
            texttool::replace_punctuation_file(file, output.string(), options);
            write_stdout("[成功] " + label + " → " + texttool::file_name_of(output.string()) + "\n");
        } catch (const std::exception& ex) {
            failed = true;
            write_stderr("[失败] " + label + "：" + ex.what() + "\n");
        }
    }
    return failed ? 1 : 0;
}

// ---------------------------------------------------------------------------
// join
//
// 输入是一个目录（不收文件列表），输出是一个新文件：默认 joined.txt，
// 也没有 --overwrite 与 --backup，这两个词在命令行上会被当成目录名。
// ---------------------------------------------------------------------------

// 拼路径的写法、通配只有 * 与 ? 两个、挑稿子的次序是全路径不分大小写升序，三样都定死。
// 这三样与界面拼接页共用，放在引擎的 io 里，两边拼出来的文件才能一样。
int run_join_command(const std::vector<std::string>& args) {
    std::string directory, pattern = "*.txt", output_name = "joined.txt";
    bool has_directory = false;

    for (std::size_t i = 0; i < args.size(); ++i) {
        const std::string& arg = args[i];
        if (arg == "--pattern" || arg == "--output") {
            if (i + 1 >= args.size()) return fail_processing(arg + " 需要参数值");
            (arg == "--pattern" ? pattern : output_name) = args[++i];
        } else if (arg == "--help" || arg == "-h") {
            write_stdout(kJoinHelp);
            return 0;
        } else {
            // 认不出的参数与目录都落在这里：目录取最后一个。
            directory = arg;
            has_directory = true;
        }
    }

    if (!has_directory || directory.empty()) return fail_processing("join 需要指定目录");

    std::error_code ec;
    if (!std::filesystem::is_directory(directory, ec))
        return fail_processing("目录不存在：" + directory);

    const std::string output = texttool::combine_path(directory, output_name);

    // 挑哪几份、排什么次序在引擎里做：界面拼接页调的是同一个函数。
    const texttool::JoinSelection selection = texttool::select_join_files(directory, pattern, output);
    if (selection.directory_error) return fail_processing("读不了目录：" + directory);
    const std::vector<std::string>& files = selection.files;

    // 输出不走原子替换：直接开文件写，中途失败会留下半份文件。
    std::ofstream out(output, std::ios::binary | std::ios::trunc);
    if (!out) return fail_processing("写入失败：" + output);

    try {
        out.write("\xEF\xBB\xBF", 3);  // 输出一律 UTF-8 带 BOM
        for (const auto& file : files) {
            // 逐块读、逐块写：一份 15MB 的稿子只有一个读缓冲在内存里，
            // 不像整份读进来那样要先把这一份装下再写出去。
            texttool::TextFileReader reader(file);
            bool any = false;
            bool ends_with_newline = false;
            std::string_view chunk;
            while (reader.next(chunk)) {
                out.write(chunk.data(), static_cast<std::streamsize>(chunk.size()));
                any = true;
                ends_with_newline = chunk.back() == '\n';
            }
            // 文件之间要有换行分隔：本份正文末尾已经是 \n 就不再补。
            // 空稿子也要单独占一行。
            if (!any || !ends_with_newline) out.put('\n');
        }
        out.close();
        if (!out) return fail_processing("写入失败：" + output);
    } catch (const std::exception& ex) {
        return fail_processing(ex.what());
    }

    write_stdout("[成功] 合并 " + std::to_string(files.size()) + " 个文件 → " + output + "\n");
    return 0;
}

// ---------------------------------------------------------------------------
// vn
//
// 角色方案来自程序目录下的 vn_schemes.json；那份不在就用默认方案
// （内容与 assets/default_vn_schemes.json 相同）。程序不带内置数据，
// 把默认方案摆成程序目录下的 default_vn_schemes.json 即可。
//
// 命令行给几个 --scheme 就把几份方案并起来：角色名取并集（引擎自己会去重），
// 路线名按给的顺序接起来，场景标记正则用 | 连成一条交给引擎。
//
// 方案里的正则写坏时不在预检那一步报：这一层不预编译这道正则（引擎在排版时才编），
// 报错落在每份的 [失败] 行上。这条路径要一份坏方案才走得进去，没列进测试表。
// ---------------------------------------------------------------------------

int run_vn_command(const std::vector<std::string>& args, const char* argv0) {
    texttool::VnOptions vn;  // 默认：排版加补标点，每段上限 450
    std::vector<std::string> files;
    std::vector<std::string> scheme_names;

    for (std::size_t i = 0; i < args.size(); ++i) {
        const std::string& arg = args[i];
        if (arg == "--scheme") {
            if (i + 1 >= args.size()) return fail_processing("--scheme 需要参数值");
            scheme_names.push_back(args[++i]);
        } else if (arg == "--max-para") {
            if (i + 1 >= args.size()) return fail_processing("--max-para 需要参数值");
            auto number = parse_int(args[++i]);
            if (!number) return fail_processing("--max-para 需要整数");
            vn.max_paragraph = *number;
        } else if (arg == "--reformat-only") {
            vn.do_fix_punct = false;
        } else if (arg == "--punct-only") {
            vn.do_reformat = false;
        } else if (arg == "--help" || arg == "-h") {
            write_stdout(kVnHelp);
            return 0;
        } else {
            // 认不出的参数按输入路径处理：拼错的选项会落成「文件不存在：--xxx」。
            files.push_back(arg);
        }
    }

    if (const int code = require_files(files, "vn"); code != 0) return code;

    // 上限小于 1 会让引擎的切削停不下来（这里同样拒绝），整批在开工前就停住。
    if (vn.max_paragraph < 1) return fail_processing("--max-para 需要大于 0 的整数");

    if (!scheme_names.empty()) {
        std::vector<texttool::VnScheme> schemes;
        std::string error;
        if (!texttool::load_vn_schemes(argv0, schemes, error)) return fail_processing(error);
        if (!texttool::combine_vn_schemes(schemes, scheme_names, vn, error))
            return fail_processing(error);
    }

    bool failed = false;
    const bool many = files.size() > 1;
    for (const auto& file : files) {
        const std::string label = many ? file : texttool::file_name_of(file);
        try {
            // 逐段读、逐段排、逐段写：15MB 的稿子只有当前这一段在内存里。
            const std::filesystem::path output(texttool::processed_path(file));
            texttool::reformat_vn_file(file, output.string(), vn);
            write_stdout("[成功] " + label + " → " + texttool::file_name_of(output.string()) + "\n");
        } catch (const std::exception& ex) {
            failed = true;
            write_stderr("[失败] " + label + "：" + ex.what() + "\n");
        }
    }
    return failed ? 1 : 0;
}

// ---------------------------------------------------------------------------
// calibrate
//
// 两侧目录各跑一遍规则，按规则汇总命中与每千字密度。退出码只有 0 与 2：
// 这张表本身不是判定，跑完就算成功；缺参数、目录不存在、档位拼错都是用法问题，用 2，
// 与 lint 对用法错误的规定一致。
// ---------------------------------------------------------------------------

int run_calibrate_command(const std::vector<std::string>& args, const char* argv0) {
    try {
        std::optional<std::string> human, ai, genre;
        bool json = false;

        for (std::size_t i = 0; i < args.size(); ++i) {
            const std::string& arg = args[i];
            const auto value = [&args, &i]() -> std::optional<std::string> {
                if (i + 1 >= args.size()) return std::nullopt;
                return args[++i];
            };

            if (arg == "--human" || arg == "--ai" || arg == "--genre") {
                auto parsed = value();
                if (!parsed) return fail(arg + " 需要参数值");
                if (arg == "--human") human = *parsed;
                else if (arg == "--ai") ai = *parsed;
                else genre = *parsed;
            } else if (arg == "--json") {
                json = true;
            } else if (arg == "--help" || arg == "-h") {
                write_stdout(kCalibrateHelp);
                return 0;
            } else {
                return fail("未知参数：" + arg + "（calibrate 只收 --human、--ai、--genre、--json）");
            }
        }

        // 缺的参数一次说全：两个都没给时逐条提示，会让人多跑一遍。
        std::string missing;
        if (!human) missing = "--human <目录>";
        if (!ai) missing += missing.empty() ? "--ai <目录>" : " 与 --ai <目录>";
        if (!missing.empty()) return fail("calibrate 缺少 " + missing);

        // 次序固定：先收两侧的稿，再读规则表，最后校验档位。
        const std::vector<std::string> human_files =
            texttool::calibration_txt_files(*human, "--human");
        const std::vector<std::string> ai_files = texttool::calibration_txt_files(*ai, "--ai");

        std::vector<texttool::LintRule> rules;
        std::string rules_error;
        if (!load_rule_table(argv0, rules, rules_error)) return fail(rules_error);

        const texttool::CalibrationResult result =
            texttool::calibrate(rules, human_files, ai_files, genre);
        write_stdout(json ? result.to_json() + "\n" : result.render());
        return 0;
    } catch (const std::exception& ex) {
        return fail(ex.what());
    }
}

// ---------------------------------------------------------------------------
// reply-check
//
// 回复里「离开文件就看不懂」的写法，一行一处。回答闸把它当工具调用，输出因此是稳定契约：
// 人读一行一处，JSON 带版本号。退出码与 lint 一致：0 = 没有发现，1 = 有发现，2 = 用法或读取错误。
// ---------------------------------------------------------------------------

int run_reply_check_command(const std::vector<std::string>& args, const char* argv0) {
    try {
        bool json = false;
        std::optional<std::string> input;

        for (const std::string& arg : args) {
            if (arg == "--json") {
                json = true;
            } else if (arg == "--help" || arg == "-h") {
                write_stdout(kReplyCheckHelp);
                return 0;
            } else if (arg == "-") {
                if (input) return fail("reply-check 只读一个输入");
                input = "-";
            } else {
                if (input) return fail("reply-check 只读一个输入");
                input = arg;
            }
        }

        Input read;
        std::string error;
        if (!read_input(input.value_or("-"), read, error)) return fail(error);

        std::vector<texttool::LintRule> rules;
        std::string rules_error;
        if (!load_rule_table(argv0, rules, rules_error)) return fail(rules_error);

        const std::vector<texttool::ReplyShapeHit> hits =
            texttool::scan_reply_shape(rules, read.text);
        write_stdout(texttool::render_reply_shape(hits, json));
        return hits.empty() ? 0 : 1;
    } catch (const std::exception& ex) {
        return fail(ex.what());
    }
}

int unknown_command(const std::string& command) {
    write_stderr("未知命令：" + command + "\n");
    write_stdout(usage_text());
    return 1;
}

}  // namespace

// Linux 上的一块内存在用完之后要还给系统，不留在分配器里当下一块的备料。
//
// 这份程序一次要几十兆的临时块（解稿的正文副本、首现表、候选表），它们大小不一：
// glibc 默认按块的大小决定「这块钱是从系统要的还是从堆里切的」，切出来的那块用完会留在堆里，
// 进程峰值因此把已经不用了的那些页也算进去——15MB 稿子上实测多出一成左右。
// 这里把「多大的块直接从系统要」与「堆尾降到多小就还给系统」压到两百多 KB，
// 大块用完当场还页，峰值按当时真活着的那一份算。glibc 才有这两个门槛，别的平台各自有等价的做法，
// 不设也不会错，只是峰值高一些。
#if defined(__GLIBC__)
constexpr int kMmapThresholdBytes = 256 * 1024;
constexpr int kTrimThresholdBytes = 512 * 1024;
#endif

int main(int argc, char** argv) {
#if defined(__GLIBC__)
    mallopt(M_MMAP_THRESHOLD, kMmapThresholdBytes);
    mallopt(M_TRIM_THRESHOLD, kTrimThresholdBytes);
#endif
    const char* argv0 = argc > 0 ? argv[0] : nullptr;
    std::vector<std::string> args;
    for (int i = 1; i < argc; ++i) args.emplace_back(argv[i]);

    // 不给命令就只印用法，退 1：调用方漏了参数，不能当成跑成功。
    if (args.empty()) {
        write_stdout(usage_text());
        return 1;
    }

    const std::string command = lower(args[0]);
    const std::vector<std::string> rest(args.begin() + 1, args.end());

    if (command == "lint") return run_lint_command(rest, argv0);
    if (command == "help" || command == "--help" || command == "-h") {
        write_stdout(usage_text());
        return 0;
    }
    if (command == "merge") return run_merge_command(rest, argv0);
    if (command == "replace") return run_replace_command(rest, argv0);
    if (command == "vn") return run_vn_command(rest, argv0);
    if (command == "join") return run_join_command(rest);
    if (command == "calibrate") return run_calibrate_command(rest, argv0);
    if (command == "reply-check") return run_reply_check_command(rest, argv0);
    return unknown_command(args[0]);
}
