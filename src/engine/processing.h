// 四个处理功能的入口。四个功能都只做「文本进、文本出」，读写文件、判编码、备份
// 这些事由命令行与界面负责，引擎不碰盘。
//
// 与 fixtures/processing 下期望输出的关系：同一份输入、同一组参数，输出必须逐字节相同。
#pragma once

#include <cstddef>
#include <cstdint>
#include <functional>
#include <memory>
#include <string>
#include <string_view>
#include <unordered_set>
#include <utility>
#include <vector>

#include "engine/encoding.h"
#include "engine/markdown.h"

namespace texttool {

// 行合并：把被硬换行截断的句子接回去。
struct MergeOptions {
    int threshold = 20;              // 阈值：按字数或字节数
    bool by_chars = true;            // true 按字数，false 按字节数
    // 按字节数时，宽度按原稿编码算（读文件时用的那个编码）。
    // 一份 GBK 稿里一个汉字是 2 字节，换算成 UTF-8 再量就是 3 字节，断行位置会不同。
    TextEncoding source_encoding = TextEncoding::Utf8;
    bool fix_cjk = true;             // 修复中文截断断段
    bool fix_punct = false;          // 修复标点截断断段
    std::string punct_chars = "，";  // 标点截断修复用的标点
    bool no_merge = false;           // 行尾部标点不合并
    std::string no_merge_chars = "。！？";
    bool apply_replace = false;      // 应用标点替换规则
    bool trim_leading_comma = true;  // 去除位于行首逗号
    std::vector<std::pair<std::string, std::string>> replace_rules;
};

// 合并结果逐段交出去：每算出一段就交一次，末行也带换行，算过的那段不在内存里留着。
// 15MB 的稿子因此只占「源文一份 + 当前这一段」，不像整份返回那样要再攒一份结果。
using TextChunkSink = std::function<void(std::string_view)>;

void merge_lines_into(std::string_view text, const MergeOptions& options, const TextChunkSink& sink);

// 行合并的逐段管线：正文一块一块喂进去，算够一段就交给 sink。
// 内存里只有「上一块末尾没走完的那一行、正在攒的这一段、三行待拼的行」，不攒整份稿子。
// 宽度也是边拼边累加的：每拼进一行加一次，不回头重扫已经拼过的字。用法：
//
//     texttool::MergePipeline pipeline(options, sink);
//     pipeline.feed(一块正文);   // 可以喂很多次，块边界落在哪个字节上都行
//     pipeline.finish();          // 收尾，最后那几行在这里交出去
//
// 与一次喂完整份等价：切块不改变结果。要从盘上逐段读、结果逐段写回去，
// 用法是 TextFileReader 读一块喂一块，sink 里直接写 TextWriter。
class MergePipeline {
public:
    MergePipeline(const MergeOptions& options, TextChunkSink sink);

    void feed(std::string_view utf8);
    void finish();

private:
    void handle_line(std::string_view line);  // 交进来一行（不带行尾）
    void after_cjk(std::string line);
    void after_punct(std::string line);
    void finish_line(std::string line);

    MergeOptions options_;
    TextChunkSink sink_;
    bool has_no_merge_ = false;
    std::unordered_set<std::uint32_t> no_merge_;
    bool has_punct_ = false;
    std::unordered_set<std::uint32_t> punct_;
    std::string line_buffer_;  // 上一块末尾没走完的那一行，加上刚喂进来的字节
    std::size_t scanned_ = 0;  // line_buffer_ 里已经扫到哪，没走完的那一行不用重扫
    std::string accumulated_;  // 正在攒的这一段
    std::size_t width_ = 0;    // accumulated_ 的宽度，边拼边加
    std::string cjk_pending_;  // 等下一行才能定拼不拼的那一行
    std::string punct_pending_;
};
// 整份结果的写法，内部就是 merge_lines_into 攒进一个字符串。
// 要控峰值就把结果直接写出去（写盘用 merge_lines_into 配 TextWriter），别走这一条。
std::string merge_lines(std::string_view text, const MergeOptions& options);

// 文件拼接：把多份文本按顺序接成一份。
struct JoinOptions {
    std::string pattern = "*.txt";
};

std::string join_texts(const std::vector<std::string>& parts, const JoinOptions& options);

// 标点替换：按规则表把一种标点换成另一种。
struct ReplaceOptions {
    std::vector<std::pair<std::string, std::string>> rules;
    // 博文结构原样保留：头部字段、三冒号指令行、代码块、公式、链接地址不替换。
    // 命令行 replace 开着它；行合并流水线里按行套规则，看不到整份稿子的头尾，那一档关着。
    bool protect_blog = true;
};

std::string replace_punctuation(std::string_view text, const ReplaceOptions& options);

// 把一串替换规则套在一段一段的正文上。规则按次序一条条套，前一条的结果是后一条的输入，
// 每一条都是从左往右不重叠地换（与 .NET 的 string.Replace 一致）。
//
// 一条规则一档，一档只压着自己这条规则还没凑齐的尾巴（不超过 find 的长度），
// 所以整条串从不落内存：边喂边出，喂多少留下多少。对整份串喂一次与分几块喂，
// 结果逐字节相同。find 为空的规则跳过。
class RuleChain {
public:
    RuleChain(const std::vector<std::pair<std::string, std::string>>& rules, TextChunkSink sink);

    // 交一段正文：能判定的部分立刻换掉交出去，不够判的那一截留到下一块。
    void feed(std::string_view utf8);

    // 把各档压着的尾巴全交出去，这一段正文到此结束。之后还能接着 feed。
    void flush();

private:
    struct Stage {
        std::string find;
        std::string replace;
        std::string pending;
    };

    void push(std::size_t index, std::string_view bytes);

    std::vector<Stage> stages_;
    TextChunkSink sink_;
};

// 换标点的逐段管线：正文一块一块喂进去，边判博文结构边套规则，算出来的结果交给 sink。
// 内存里只有一块源字节、当前这一行、规则链各档压着的尾巴，不攒整份稿子。
//
// 空格副本（把结构区域都写成空格、与原文等长的那一份）由逐行状态机（`BlogMaskScanner`）给出：
// 与整份建表用的是同一套判定，一行一行算出来的边界与整份算出来的相同，
// 一个字节占一比特的那份表因此不必再建。
// blog_post 要把稿子看一遍才知道，由调用方用同一个逐行状态机判好再传进来。用法：
//
//     texttool::ReplacePipeline pipeline(options, blog_post, sink);
//     pipeline.feed(一块正文);   // 块边界落在哪个字节上都行
//     pipeline.finish();
//
// 与一次喂完整份等价：切块不改变结果。从盘上逐段读、结果逐段写回去的用法见
// replace_punctuation_file。
class ReplacePipeline {
public:
    ReplacePipeline(const ReplaceOptions& options, bool blog_post, TextChunkSink sink);

    void feed(std::string_view utf8);
    void finish();

private:
    void handle_line(const BlogMaskScanner::LineMask& line);
    void write_masked(std::string_view bytes);  // 被遮住的那一段：原样接在规则算出来的后面

    bool masking_ = false;  // 这份稿子按博文遮
    TextChunkSink sink_;
    RuleChain chain_;
    BlogMaskScanner scanner_;
};

// 换标点写盘：从 source 逐段读、边判结构边换、逐段写到 target，内存里只有一块正文。
// 返回原稿的编码；写盘用的就是它。读不了、写不进去都抛 std::runtime_error。
TextEncoding replace_punctuation_file(const std::string& source, const std::string& target,
                                     const ReplaceOptions& options);

// 视觉小说排版：一句一行、补标点、限制每段字数。
struct VnOptions {
    bool do_fix_punct = true;     // 补标点
    bool do_reformat = true;      // 排版
    int max_paragraph = 450;      // 每段最多多少字
    std::vector<std::string> characters;   // 角色名，用于识别台词
    std::vector<std::string> route_names;  // 路线名，用于识别场景分隔
    // 场景标记的匹配模式，空表示用通用模式 `── 场景 ──`。
    // 方案包里的模式可能不止一条，由调用方用 | 连成一条再传进来。
    std::string scene_pattern;
};

// 整份结果的写法，内部就是 VnPipeline 攒进一个字符串。
// 要控峰值就用 VnPipeline 或 reformat_vn_file，别走这一条。
std::string reformat_vn(std::string_view text, const VnOptions& options);

// 剧本排版的逐段管线：正文一块一块喂进去，一行判完并进当前这一段，一段排完就交给 sink。
// 内存里只有当前这一行、正在攒的这一段、正在切出来的那几片，以及「上一段是什么」这一格记忆；
// 整份稿子转成宽字符那一份不再是必须的。用法与 ReplacePipeline 相同：
//
//     texttool::VnPipeline pipeline(options, sink);
//     pipeline.feed(一块正文);
//     pipeline.finish();
//
// 与一次喂完整份等价：切块不改变结果。切点、长度、换行怎么定都在实现里，与整份那条路同一份代码。
class VnPipeline {
public:
    VnPipeline(const VnOptions& options, TextChunkSink sink);
    ~VnPipeline();

    VnPipeline(const VnPipeline&) = delete;
    VnPipeline& operator=(const VnPipeline&) = delete;

    void feed(std::string_view utf8);
    void finish();

    struct Impl;  // 实现细节，不在这份头文件里露出来

private:
    std::unique_ptr<Impl> impl_;
};

// 剧本排版写盘：从 source 逐段读、逐段排、逐段写到 target，内存里只有当前这一段。
// 返回原稿的编码；写盘用的就是它。每段上限小于 1、读不了、场景标记正则非法、
// 写不进去都抛，抛之前目标文件一个字没动。
TextEncoding reformat_vn_file(const std::string& source, const std::string& target,
                              const VnOptions& options);

}  // namespace texttool
