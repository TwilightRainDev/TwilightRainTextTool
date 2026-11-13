// 多步串联的测试：一步、两步、三步的产物逐字节比对；失败与按停止之后盘上留了什么；
// 目标已存在时覆盖并留 .bak；强杀留下的临时文件怎么清；预览与正式两套前缀互不干扰。
//
// 期望字节有两个来处，都不是拿被测代码自己算出来的：
//   · 单步链与 fixtures/processing 下由参考实现生成的期望输出逐字节相同；
//   · 两步与三步链的期望是命令行按同一顺序手工跑出来的（merge、replace、vn 依次跑），
//     抄成字面量放在这里。
//
// 临时目录一律建在系统临时目录下，跑完连里面的东西一起删掉。
#include "doctest/doctest.h"

#include <algorithm>
#include <cerrno>
#include <filesystem>
#include <fstream>
#include <sstream>
#include <stdexcept>
#include <string>
#include <string_view>
#include <vector>

#if defined(_WIN32)
#include <windows.h>
#else
#include <csignal>
#include <unistd.h>
#endif

#include "engine/chain.h"

namespace {

namespace fs = std::filesystem;

using texttool::ChainRequest;
using texttool::ChainResult;
using texttool::ChainStep;
using texttool::ChainStepKind;
using texttool::TextEncoding;

std::string read_bytes(const std::string& path) {
    std::ifstream in(path, std::ios::binary);
    REQUIRE_MESSAGE(in.good(), "打不开文件：" << path);
    std::ostringstream buffer;
    buffer << in.rdbuf();
    return buffer.str();
}

void write_bytes(const std::string& path, std::string_view bytes) {
    std::ofstream out(path, std::ios::binary | std::ios::trunc);
    REQUIRE_MESSAGE(out.good(), "写不进文件：" << path);
    out.write(bytes.data(), static_cast<std::streamsize>(bytes.size()));
}

bool file_exists(const std::string& path) {
    std::error_code ignored;
    return fs::is_regular_file(path, ignored);
}

// 测试自己造一个目录，跑完连里面的东西一起删掉。
class TempDir {
public:
    explicit TempDir(const std::string& name) {
        path_ = fs::temp_directory_path() / ("texttool-chain-" + name);
        std::error_code ignored;
        fs::remove_all(path_, ignored);
        fs::create_directories(path_);
    }
    ~TempDir() {
        std::error_code ignored;
        fs::remove_all(path_, ignored);
    }
    TempDir(const TempDir&) = delete;
    TempDir& operator=(const TempDir&) = delete;

    std::string file(const std::string& name) const { return (path_ / name).string(); }
    std::string dir() const { return path_.string(); }

    // 目录里所有名字，排过序，比的时候稳定。
    std::vector<std::string> names() const {
        std::vector<std::string> found;
        std::error_code ec;
        for (fs::directory_iterator it(path_, ec), end; !ec && it != end; it.increment(ec))
            found.push_back(it->path().filename().string());
        std::sort(found.begin(), found.end());
        return found;
    }

    // 本工具留下的痕迹：两套前缀之一。
    std::vector<std::string> temp_files() const {
        std::vector<std::string> found;
        for (const std::string& name : names())
            if (name.starts_with(".texttool-")) found.push_back(name);
        return found;
    }

private:
    fs::path path_;
};

// 本进程号：临时文件名里那一段。测试要拼出「正在跑的进程留下的那份」时用它。
long own_process_id() {
#if defined(_WIN32)
    return static_cast<long>(::GetCurrentProcessId());
#else
    return static_cast<long>(::getpid());
#endif
}

// 一个确实没在跑的进程号：从 pid 上限附近往下找，第一个「查不到这个进程」的就是。
// 这一带的号要全被占满才撞得上，正常机器上第一个就中。
long unused_process_id() {
#if defined(_WIN32)
    return 4'000'000;
#else
    for (long pid = 4'194'303; pid > 1; --pid)
        if (::kill(static_cast<pid_t>(pid), 0) != 0 && errno == ESRCH) return pid;
    return 0;
#endif
}

const std::string kFixtureDir =
    std::string(TEXTTOOL_FIXTURES_DIR) + "/processing/merge_join_replace/";

// 链里的一步：接回断行，参数与 fixtures 里 merge_default_mixed 那一行相同。
ChainStep merge_step() {
    ChainStep step;
    step.kind = ChainStepKind::MergeLines;
    step.merge.threshold = 20;
    step.merge.by_chars = true;
    step.merge.fix_cjk = true;
    step.merge.fix_punct = false;
    step.merge.punct_chars = "，";
    step.merge.no_merge = false;
    step.merge.apply_replace = false;
    step.merge.trim_leading_comma = true;
    return step;
}

// 链里的一步：换标点，半角逗号换成全角逗号。
ChainStep replace_step() {
    ChainStep step;
    step.kind = ChainStepKind::ReplacePunctuation;
    step.replace.rules = {{",", "，"}};
    step.replace.protect_blog = true;
    return step;
}

// 链里的一步：剧本排版，排版加补标点，每段上限取默认的 450。
ChainStep vn_step() {
    ChainStep step;
    step.kind = ChainStepKind::ReformatVn;
    step.vn.do_fix_punct = true;
    step.vn.do_reformat = true;
    step.vn.max_paragraph = 450;
    return step;
}

// 三步链的输入：一行半角逗号、一行短句，接回那一步会拼成一行。
const char* kSample = "他说完了,然后走了\n省略号收尾\n";
// 再换标点之后（命令行 replace，规则 ,=>，）。
const char* kSampleReplaced = "他说完了，然后走了省略号收尾\n";
// 再排版之后（命令行 vn 的产物：句末补了句号，末尾不带换行）。
const char* kSampleFormatted = "他说完了，然后走了省略号收尾。";

}  // namespace

TEST_CASE("单步链（接回断行）与参考实现的期望输出逐字节相同") {
    TempDir dir("merge-one");

    // 一、默认参数那一份。
    {
        const std::string input = dir.file("mixed.txt");
        const std::string target = dir.file("mixed-out.txt");
        write_bytes(input, read_bytes(kFixtureDir + "inputs/mixed.txt"));

        ChainRequest request;
        request.source = input;
        request.target = target;
        request.steps = {merge_step()};

        const ChainResult result = texttool::run_chain(request);
        CHECK(result.finished_steps == 1);
        CHECK_FALSE(result.stopped);
        CHECK(result.encoding == TextEncoding::Utf8);
        CHECK(read_bytes(target) == read_bytes(kFixtureDir + "expected/merge_default_mixed.txt"));
        // 原稿一个字没动，临时文件不剩。
        CHECK(read_bytes(input) == read_bytes(kFixtureDir + "inputs/mixed.txt"));
        CHECK(dir.temp_files().empty());
    }

    // 二、CRLF 那一份：换行按接回那一步的规矩走，链不另作处理。
    {
        const std::string input = dir.file("crlf.txt");
        const std::string target = dir.file("crlf-out.txt");
        write_bytes(input, read_bytes(kFixtureDir + "inputs/crlf.txt"));

        ChainStep step = merge_step();
        step.merge.threshold = 4;
        step.merge.fix_cjk = false;
        step.merge.trim_leading_comma = false;

        ChainRequest request;
        request.source = input;
        request.target = target;
        request.steps = {step};

        texttool::run_chain(request);
        CHECK(read_bytes(target) == read_bytes(kFixtureDir + "expected/merge_crlf.txt"));
        CHECK(dir.temp_files().empty());
    }
}

TEST_CASE("单步链（换标点）与参考实现的期望输出逐字节相同") {
    TempDir dir("replace-one");
    const std::string input = dir.file("in.txt");
    const std::string target = dir.file("out.txt");
    write_bytes(input, read_bytes(kFixtureDir + "inputs/replace_overlap.txt"));

    ChainStep step = replace_step();
    step.replace.rules = {{"。。", "；"}};

    ChainRequest request;
    request.source = input;
    request.target = target;
    request.steps = {step};

    const ChainResult result = texttool::run_chain(request);
    CHECK(result.finished_steps == 1);
    CHECK(read_bytes(target) == read_bytes(kFixtureDir + "expected/replace_prefix_overlap.txt"));
    CHECK(read_bytes(input) == read_bytes(kFixtureDir + "inputs/replace_overlap.txt"));
    CHECK(dir.temp_files().empty());
}

TEST_CASE("两步链：接回断行再换标点") {
    TempDir dir("two-steps");
    const std::string input = dir.file("in.txt");
    const std::string target = dir.file("out.txt");
    write_bytes(input, kSample);

    ChainRequest request;
    request.source = input;
    request.target = target;
    request.steps = {merge_step(), replace_step()};

    const ChainResult result = texttool::run_chain(request);
    CHECK(result.finished_steps == 2);
    CHECK_FALSE(result.stopped);
    CHECK(read_bytes(target) == kSampleReplaced);
    CHECK(read_bytes(input) == kSample);
    CHECK(dir.temp_files().empty());
}

TEST_CASE("三步链：接回断行、换标点、剧本排版") {
    TempDir dir("three-steps");
    const std::string input = dir.file("in.txt");
    const std::string target = dir.file("out.txt");
    write_bytes(input, kSample);

    ChainRequest request;
    request.source = input;
    request.target = target;
    request.steps = {merge_step(), replace_step(), vn_step()};

    const ChainResult result = texttool::run_chain(request);
    CHECK(result.finished_steps == 3);
    CHECK(read_bytes(target) == kSampleFormatted);
    CHECK(read_bytes(input) == kSample);
    CHECK(dir.temp_files().empty());
}

TEST_CASE("按原编码读、按原编码写：GBK 稿进、GBK 稿出") {
    TempDir dir("gbk");
    const std::string input = dir.file("in.txt");
    const std::string target = dir.file("out.txt");
    // 中文。 的 GBK 字节：D6 D0 CE C4 A1 A3；句号换成叹号以后是 A3 A1。
    write_bytes(input, "\xD6\xD0\xCE\xC4\xA1\xA3\n");

    ChainStep step = replace_step();
    step.replace.rules = {{"。", "！"}};

    ChainRequest request;
    request.source = input;
    request.target = target;
    request.steps = {step};

    const ChainResult result = texttool::run_chain(request);
    CHECK(result.encoding == TextEncoding::Gbk);
    CHECK(read_bytes(target) == "\xD6\xD0\xCE\xC4\xA3\xA1\n");
}

TEST_CASE("中途失败：临时文件删掉，原稿与已存在的旧结果都不动") {
    TempDir dir("failure");
    const std::string input = dir.file("in.txt");
    const std::string target = dir.file("out.txt");
    write_bytes(input, kSample);

    // 每段上限小于 1：剧本排版那一步开工前就抛，链停在第 2 步。
    ChainStep broken = vn_step();
    broken.vn.max_paragraph = 0;

    ChainRequest request;
    request.source = input;
    request.target = target;
    request.steps = {merge_step(), broken, vn_step()};

    // 目标本来不存在：失败之后也不该出现。
    std::string message;
    try {
        texttool::run_chain(request);
    } catch (const std::exception& ex) {
        message = ex.what();
    }
    CHECK(message.find("第 2 步（剧本排版）失败") != std::string::npos);
    CHECK(message.find("每段上限必须大于 0") != std::string::npos);
    CHECK_FALSE(file_exists(target));
    CHECK(read_bytes(input) == kSample);
    CHECK(dir.temp_files().empty());

    // 目标已经有一份旧结果：失败之后它还是原来那份，也不留 .bak。
    write_bytes(target, "旧结果\n");
    CHECK_THROWS_AS(texttool::run_chain(request), std::runtime_error);
    CHECK(read_bytes(target) == "旧结果\n");
    CHECK_FALSE(file_exists(target + ".bak"));
    CHECK(read_bytes(input) == kSample);
    CHECK(dir.temp_files().empty());
}

TEST_CASE("第一步就失败：消息说的是第 1 步") {
    TempDir dir("failure-first");
    const std::string target = dir.file("out.txt");

    ChainRequest request;
    request.source = dir.file("missing.txt");
    request.target = target;
    request.steps = {merge_step()};

    std::string message;
    try {
        texttool::run_chain(request);
    } catch (const std::exception& ex) {
        message = ex.what();
    }
    CHECK(message.find("第 1 步（接回断行）失败") != std::string::npos);
    CHECK(message.find("文件不存在") != std::string::npos);
    CHECK_FALSE(file_exists(target));
    CHECK(dir.temp_files().empty());
}

TEST_CASE("目标已存在：覆盖，先留一份 .bak") {
    TempDir dir("overwrite");
    const std::string input = dir.file("in.txt");
    const std::string target = dir.file("out.txt");
    write_bytes(input, kSample);
    write_bytes(target, "上一份结果\n");

    ChainRequest request;
    request.source = input;
    request.target = target;
    request.steps = {merge_step(), replace_step()};

    texttool::run_chain(request);
    CHECK(read_bytes(target) == kSampleReplaced);
    CHECK(read_bytes(target + ".bak") == "上一份结果\n");

    // 不要备份时就不留：覆盖的规矩照旧，少一份 .bak。
    write_bytes(target, "又一份结果\n");
    std::error_code ignored;
    fs::remove(target + ".bak", ignored);
    request.backup = false;
    texttool::run_chain(request);
    CHECK(read_bytes(target) == kSampleReplaced);
    CHECK_FALSE(file_exists(target + ".bak"));
}

TEST_CASE("按停止：两步之间收手，正在写的那一步写完，临时文件删掉") {
    TempDir dir("stop");
    const std::string input = dir.file("in.txt");
    const std::string target = dir.file("out.txt");
    write_bytes(input, kSample);

    ChainRequest request;
    request.source = input;
    request.target = target;
    request.steps = {merge_step(), replace_step(), vn_step()};

    int asked = 0;
    std::vector<std::string> at_stop;
    request.should_stop = [&]() {
        ++asked;
        // 问的时候第一步已经写完了：它的产物就在目标目录里，名字对得上。
        at_stop = dir.temp_files();
        return true;
    };

    const ChainResult result = texttool::run_chain(request);
    CHECK(result.stopped);
    CHECK(result.finished_steps == 1);
    CHECK(asked == 1);  // 只在两步之间问，收手以后不再问
    REQUIRE(at_stop.size() == 1);
    CHECK(at_stop.front() ==
          ".texttool-tmp-" + std::to_string(own_process_id()) + "-1");
    // 停下来以后：临时文件删干净，目标文件没出现，原稿没动。
    CHECK(dir.temp_files().empty());
    CHECK_FALSE(file_exists(target));
    CHECK(read_bytes(input) == kSample);
}

TEST_CASE("强杀留下的临时文件：名字对得上且进程不在的删掉，其余留着") {
    TempDir dir("sweep");
    const long dead = unused_process_id();
    REQUIRE(dead > 0);

    const std::string stale_formal = dir.file(".texttool-tmp-" + std::to_string(dead) + "-1");
    const std::string stale_writer =
        dir.file(".texttool-preview-" + std::to_string(dead) + "-2.tmp");
    const std::string own = dir.file(".texttool-tmp-" + std::to_string(own_process_id()) + "-1");
    const std::string bad_step = dir.file(".texttool-tmp-" + std::to_string(dead) + "-x");
    const std::string no_step = dir.file(".texttool-tmp-" + std::to_string(dead));
    const std::string other = dir.file("note.txt");

    for (const std::string& path : {stale_formal, stale_writer, own, bad_step, no_step, other})
        write_bytes(path, "留个痕迹\n");

    CHECK(texttool::sweep_stale_chain_temps(dir.dir()) == 2);
    CHECK_FALSE(file_exists(stale_formal));
    CHECK_FALSE(file_exists(stale_writer));
    CHECK(file_exists(own));        // 本进程还在跑，这是它自己的活
    CHECK(file_exists(bad_step));   // 名字对不上，不归本工具管
    CHECK(file_exists(no_step));
    CHECK(file_exists(other));

    // 再扫一遍：没有可删的了，不再报数。
    CHECK(texttool::sweep_stale_chain_temps(dir.dir()) == 0);
    // 目录读不了：当没有，返回 0，不抛。
    CHECK(texttool::sweep_stale_chain_temps(dir.file("missing")) == 0);
}

TEST_CASE("预览：只读开头，跑完删掉自己那份临时文件，目标文件不出现") {
    TempDir dir("preview");
    const std::string input = dir.file("in.txt");
    const std::string target = dir.file("out.txt");
    write_bytes(input, kSample);

    ChainRequest request;
    request.source = input;
    request.target = target;
    request.steps = {merge_step(), replace_step()};

    // 读开头五个字：刚好停在「他说完了，」。
    const texttool::ChainPreview head = texttool::preview_chain(request, 5);
    CHECK(head.text == "他说完了，");
    CHECK(head.truncated);
    CHECK_FALSE(head.stopped);
    CHECK(head.encoding == TextEncoding::Utf8);

    // 要得比产物长：整份都读回来了，不算截断。
    const texttool::ChainPreview whole = texttool::preview_chain(request, 1000);
    CHECK(whole.text == kSampleReplaced);
    CHECK_FALSE(whole.truncated);

    // 预览只落临时文件：目标没出现，临时文件也删干净了。
    CHECK_FALSE(file_exists(target));
    CHECK(dir.temp_files().empty());
    CHECK(read_bytes(input) == kSample);
}

TEST_CASE("预览按停止：不留临时文件，也不碰目标") {
    TempDir dir("preview-stop");
    const std::string input = dir.file("in.txt");
    const std::string target = dir.file("out.txt");
    write_bytes(input, kSample);

    ChainRequest request;
    request.source = input;
    request.target = target;
    request.steps = {merge_step(), replace_step()};
    request.should_stop = []() { return true; };

    const texttool::ChainPreview preview = texttool::preview_chain(request, 1000);
    CHECK(preview.stopped);
    CHECK(preview.text.empty());
    CHECK_FALSE(file_exists(target));
    CHECK(dir.temp_files().empty());
}

TEST_CASE("预览与正式跑用不同的临时名前缀：两边同时开着也不互相盖") {
    TempDir dir("prefixes");
    const std::string input = dir.file("in.txt");
    const std::string target = dir.file("out.txt");
    write_bytes(input, kSample);
    const std::string pid = std::to_string(own_process_id());

    // 一、正式跑那一份正在写（名字是本进程号与第 1 步）：预览不碰它。
    const std::string formal_live = dir.file(".texttool-tmp-" + pid + "-1");
    write_bytes(formal_live, "正式跑写到一半\n");

    ChainRequest request;
    request.source = input;
    request.target = target;
    request.steps = {replace_step()};

    const texttool::ChainPreview preview = texttool::preview_chain(request, 1000);
    CHECK_FALSE(preview.stopped);
    CHECK(read_bytes(formal_live) == "正式跑写到一半\n");

    // 二、预览那一份正在写：正式跑改名成目标，也不碰它。
    const std::string preview_live = dir.file(".texttool-preview-" + pid + "-1");
    write_bytes(preview_live, "预览写到一半\n");
    std::error_code ignored;
    fs::remove(formal_live, ignored);

    texttool::run_chain(request);
    // 链里只有换标点这一步：行还是两行，逗号换成了全角。
    CHECK(read_bytes(target) == "他说完了，然后走了\n省略号收尾\n");
    CHECK(read_bytes(preview_live) == "预览写到一半\n");
}

TEST_CASE("链里没有步骤：当场抛，盘上什么也不留") {
    TempDir dir("empty");
    ChainRequest request;
    request.source = dir.file("in.txt");
    request.target = dir.file("out.txt");
    write_bytes(request.source, kSample);

    CHECK_THROWS_AS(texttool::run_chain(request), std::invalid_argument);
    CHECK_THROWS_AS(texttool::preview_chain(request, 100), std::invalid_argument);
    CHECK_FALSE(file_exists(request.target));
    CHECK(dir.temp_files().empty());
}
