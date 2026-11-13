// 由代码计算的规则共用的文本切分与登记表。
//
// 15 条这类规则都要按段落、按句子、按字数去统计，切分规则必须只有一套，
// 否则同一条稿子在两条规则里会被切成不同的句子，数字互相打架。
#pragma once

#include <cstddef>
#include <cstdint>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

#include "engine/lint_runner.h"
#include "engine/location.h"
#include "engine/rules.h"

namespace texttool {

// 一段或一句在文本里的字节范围。
// 下标按 32 位存：一份十五兆的稿子有十八万段，按 64 位存是二点九兆，按 32 位存省一半，
// 而稿件本来限在 4GB 以内（run_lint 遇到更大的直接报错，不静默截断）。
struct Span {
    std::uint32_t begin = 0;
    std::uint32_t end = 0;
};

// 把字节下标收成 32 位放进 Span：稿件限在 4GB 以内（见 run_lint 的那道门），收的时候不会截断。
// 所有切出来装进 Span 的地方都走它（按行、按 markdown、算法里的段落与串）。
inline std::uint32_t span_index(std::size_t value) { return static_cast<std::uint32_t>(value); }

// 算法规则写命中的口子。写进去的字节范围由调用方再换算行列。
using AlgoWriter = void (*)(std::size_t begin, std::size_t end, const std::string& excerpt,
                            void* context);

// 一份待找文本的两面：认形状、比内容看 scan（博文稿里结构区域是空格），数码元看 original。
//
// 两份逐字节对齐，所以字节下标通用。两份指的是原文与空格副本（把结构区域都写成空格、
// 与原文等长的那一份，博文稿才建）。数码元必须看 original：空格副本把一个多字节字写成
// 好几个空格，照它数一个汉字会被数成三处，而一个字符只算一处。
struct MeasuredText {
    std::string_view scan;
    std::string_view original;

    // 只有一份文本时两面相同：算法单跑、稿子不是博文都走这条。
    explicit MeasuredText(std::string_view text) : scan(text), original(text) {}
    MeasuredText(std::string_view scan, std::string_view original)
        : scan(scan), original(original) {}
};

struct AlgoContext {
    AlgoContext() = default;
    AlgoContext(std::string_view text, std::size_t units)
        : text(text), original(text), units(units) {}

    // 待找的那一份：博文稿上它是空格副本（结构区域是空格），不是博文时就是原文。
    // 找、比、切段都只看这一份。
    std::string_view text;

    // 原文全文。数「字」与取报给读者的命中原文用这一份：被遮住的字符在这里仍是作者写的字。
    // 不给就是与 text 同一份（测试单跑一个算法时）。
    std::string_view original;

    std::size_t units = 0;  // 原文全长，按 UTF-16 码元；千字处数的分母按它算

    // 数「字」用的两面。算法里由 context 现取，不另存。
    MeasuredText measured() const { return MeasuredText(text, original); }

    // 接线层按稿件给的分段方式（逐行或 markdown）切好并持有的段落，段界与段落作用域的数据规则同一份。
    // 空指针表示调用方没切：算法按逐行分段自己切一份（逐个调算法的测试走这条）。
    const std::vector<Span>* paragraphs = nullptr;

    // 原文的行索引。接线层手上已经有一份（按 original 建的），交过来就不用算法自己再建：
    // 一份 15MB 的稿子行索引一兆多，每条要算行列的规则再建一份就是白占。
    // 空指针表示调用方没建：算法自己建一份（逐个调算法的测试走这条）。
    const LineIndex* locations = nullptr;
};

using AlgoFn = void (*)(const LintRule& rule, const AlgoContext& context, AlgoWriter writer,
                        void* writer_context, std::vector<std::string>& notes);

// 按段落切：一行一段，空行跳过。段首的空白（含制表符）与段尾的换行、空格都不算在段内。
std::vector<Span> split_paragraphs(std::string_view text);

// 按句子切：句末标点、问号、叹号、省略号收尾，引号与括号收进上一句。
std::vector<Span> split_sentences(std::string_view text);

// 某段字节的「字数」，按 UTF-16 码元算，基本平面以外的字算两个。
std::size_t unit_length(std::string_view text, std::size_t begin, std::size_t end);

// [begin, end) 的码元数，按 original 数：博文结构那部分在空格副本里是一个字好几个空格，
// 照它数会偏大。
std::size_t measured_units(const MeasuredText& text, std::size_t begin, std::size_t end);

// 掐掉首尾空白之后的码元数：空白按 scan 认（掐的就是空格副本那份），码元按 original 数。
std::size_t trimmed_units(const MeasuredText& text, std::size_t begin, std::size_t end);

// 从 begin 起往后数 count 个字（UTF-16 码元）落在哪个字节上，到头就停在 end。
// 按 original 的字符宽度走：被遮的多字节字在空格副本里是好几个空格。
std::size_t byte_after_units(const MeasuredText& text, std::size_t begin, std::size_t end,
                             std::size_t count);

// 每千字多少处，保留一位小数。
double per_thousand(std::size_t count, std::size_t units);

// 各分组的源文件在加载时把自己登记进来，名字与规则表的 AlgorithmName 一致。
void register_algorithm(const std::string& name, AlgoFn fn);

// 这个名字登记过没有。规则表里写了一个没人实现的算法名，这条规则扫到就抛；
// 用户改条目时先用它问一句，写进去之前就挡下来。
bool algorithm_is_registered(std::string_view name);

// 命中说明的交接。AlgoWriter 只带字节区间与命中原文，说明里的数字只有算法自己知道，
// 所以算法在调 writer 之前把本次说明放进本组的交接口，接线层在写命中的回调里按规则编号取一次。
// 复读组（repeated-verbatim、repeated-token-run）取第一个，rhythm 组的 choppy-clauses 取第二个，
// shape 组的其余各条取第三个。
const std::string& repeat_pending_detail();
const std::string& choppy_pending_detail();
const std::string& shape_pending_detail();

// 本次命中的展示片段最多取多少字，0 表示按命中长度取。整串报一处时命中可以很长，
// 片段只要那一串的样子，复读组把上限一并交出来。
std::size_t repeat_pending_snippet_chars();

// 本轮复读串在原文里的字节范围：串里其他规则的命中由接线层按这批区间剔掉。
std::vector<Span> repeat_token_run_spans();

// 说明里那两个数的来处。复读类规则的说明是「与第 N 行第 M 列逐字相同」，
// 那两个数是原文某一处的行列，由那处的字节下标现算。算法把下标交出来，
// 接线层只存下标（四字节）：存整句要三十多字节，十几万处就是两兆多。
//
// 交出来的下标只对「说明就是规则表 Detail 填 {0}{1} 两个数」这一种写法成立，
// 所以只在规则表里的那条 Detail 真用了两个数的地方设置它（见 algo_repeat 的两处）。
struct DetailPosition {
    bool present = false;
    std::size_t at = 0;  // 说明里那两个数由这个字节位置的行列算
};

// 取一次就清空：没设置过的算法拿到的是「没有」，不会捡到上一条规则留下的位置。
DetailPosition take_pending_detail_position();

// 设置本次的说明位置。每一条命中只对最靠近它的那一次设置生效（接线层取一次就清空）。
void set_pending_detail_position(DetailPosition position);

// 规则表里的说明模板用 {0}、{1} 填参数：第几号参数就填进那个花括号里。
// 花括号写坏了就原样给出，不让一条配置把整份报告打崩。
// 算法组拼说明、接线层按存的数拼说明，两边都走这一份，拼法不会两处各说各的。
std::string fill_detail_args(std::string_view pattern, const std::vector<std::string>& args);

// 按规则表里的算法名调用；名字没登记时抛 std::runtime_error。
void run_algorithm(const LintRule& rule, const AlgoContext& context, AlgoWriter writer,
                   void* writer_context, std::vector<std::string>& notes);

}  // namespace texttool
