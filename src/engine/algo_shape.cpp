// 由代码统计的规则：shape 组。七条都在这里实现并登记。
//
// 每条的判据、门限与说明文字的取法都在下面写清，期望输出见 fixtures/cli 下七份稿件各自的
// .expected.json：
//   * 感叹号密度、序号骨架、显式编号只出统计附注，不出命中；
//   * 括号全半角混用、引号样式混用整篇至多一条命中，报最早出现的那个标点；
//   * 术语堆叠逐处报，比喻堆叠逐句报，两条都先过一道全篇密度门限，不到门限一处都不报。
//
// 说明文字（Detail）在 AlgoWriter 的入参里没有位置：它只带命中的字节区间与命中原文。
// 七条里有四条的报告说明要代入当前数字（密度、字数、个数），只能现算，
// 所以每次写命中之前先把这一段放进 shape_pending_detail()，接线层在 writer 回调里取走。
// 接线层按规则编号选口子：本组七条取 shape_pending_detail()，复读组取 repeat_pending_detail()。
#include "engine/scan.h"

#include <algorithm>
#include <charconv>
#include <cstddef>
#include <cstdint>
#include <iterator>
#include <string>
#include <string_view>
#include <vector>

#include "engine/regex.h"

namespace texttool {
namespace {

// ---------------------------------------------------------------- 交接与格式化

// 当前这一处命中该带的说明。设它的一定在调用 writer 之前，取它的一定在回调里，
// 两处在同一个线程上（一份稿一次扫描）。并行扫多份时每份各扫各的，这份变量各写各的用。
std::string g_pending_detail;

// 规则表里的说明带 {0} {1} 这类占位符，代入参数后才是报告里那一句。
// 占位符写坏了原样给出：一条配置写错不该把整份报告打崩（格式化出错也照原样退）。
std::string fill(std::string_view pattern, const std::vector<std::string>& args) {
    if (args.empty()) return std::string(pattern);
    std::string out;
    for (std::size_t i = 0; i < pattern.size();) {
        if (pattern[i] == '{' && i + 2 < pattern.size() && pattern[i + 2] == '}' &&
            pattern[i + 1] >= '0' && pattern[i + 1] <= '9') {
            const std::size_t index = static_cast<std::size_t>(pattern[i + 1] - '0');
            if (index >= args.size()) return std::string(pattern);
            out.append(args[index]);
            i += 3;
            continue;
        }
        out.push_back(pattern[i]);
        ++i;
    }
    return out;
}

std::string number(std::size_t value) { return std::to_string(value); }

// 双精度按最短能还原的写法给：40.4 写 40.4，5 写 5，不补「.0」；先取到一位小数。
std::string number(double value) {
    char buffer[64];
    const std::to_chars_result result = std::to_chars(buffer, buffer + sizeof buffer, value);
    return std::string(buffer, result.ptr);
}

// ---------------------------------------------------------------- 切字与空白

std::size_t char_bytes(unsigned char lead) {
    if (lead < 0x80) return 1;
    if ((lead & 0xE0) == 0xC0) return 2;
    if ((lead & 0xF0) == 0xE0) return 3;
    if ((lead & 0xF8) == 0xF0) return 4;
    return 1;
}

// UTF-8 的一个字：坏字节按一个字节处理，不往后吞（与 scan.cpp 相同）。
std::uint32_t next_codepoint(std::string_view text, std::size_t& at) {
    const unsigned char lead = static_cast<unsigned char>(text[at]);
    const std::size_t width = char_bytes(lead);
    if (width == 1 || at + width > text.size()) {
        ++at;
        return lead;
    }
    std::uint32_t value = lead & (0xFFu >> (width + 1));
    for (std::size_t k = 1; k < width; ++k)
        value = (value << 6) | (static_cast<unsigned char>(text[at + k]) & 0x3Fu);
    at += width;
    return value;
}

// 空白就是这一组：制表与换行、半角空格、全角空格都算，
// 不间断空格与各种排版空格也算。切句子之后两端的空白不参与字数与报位。
bool is_space(std::uint32_t cp) {
    return (cp >= 0x09 && cp <= 0x0D) || cp == 0x20 || cp == 0x85 || cp == 0xA0 || cp == 0x1680 ||
           (cp >= 0x2000 && cp <= 0x200A) || cp == 0x2028 || cp == 0x2029 || cp == 0x202F ||
           cp == 0x205F || cp == 0x3000;
}

// 去掉两端的空白之后的那一段：字节范围与字数（UTF-16 码元）。
struct Trimmed {
    std::size_t begin = 0;
    std::size_t end = 0;
    std::size_t units = 0;
};

Trimmed trim(const MeasuredText& text, std::size_t begin, std::size_t end) {
    const std::string_view scan = text.scan;
    std::size_t head = begin;
    while (head < end) {
        std::size_t at = head;
        if (!is_space(next_codepoint(scan, at))) break;
        head = at;
    }
    std::size_t tail = end;
    while (tail > head) {
        // 从末尾往回退一个字：UTF-8 的续字节是 10xxxxxx，退到首字节为止
        std::size_t previous = tail - 1;
        while (previous > head && (static_cast<unsigned char>(scan[previous]) & 0xC0) == 0x80)
            --previous;
        std::size_t at = previous;
        if (!is_space(next_codepoint(scan, at))) break;
        tail = previous;
    }
    return {head, tail, measured_units(text, head, tail)};
}

// ---------------------------------------------------------------- 找与数

// 一处命中的字节范围。
struct Found {
    std::size_t begin = 0;
    std::size_t end = 0;
};

// 从头往后找，不重叠：找到一处就从它后面接着找，找到一处交一处。
// 要边走边判的地方用它：整篇的句子范围有几十万处，先攒成表再走就是几兆白占。
template <typename Sink>
void for_each_match(std::string_view text, const Pattern& pattern, Sink&& sink) {
    const MatchLimits limits{};
    std::size_t from = 0;
    while (from <= text.size()) {
        const MatchRange match = pattern.search(text, from, limits);
        if (match.begin == kNoMatch) break;
        sink(match.begin, match.end);
        if (match.end > match.begin) {
            from = match.end;
        } else {
            if (match.begin >= text.size()) break;
            std::size_t at = match.begin;
            next_codepoint(text, at);
            from = at;
        }
    }
}

// 同一批命中攒成一张表：要两遍走、或要数总数的地方用它。
std::vector<Found> find_all(std::string_view text, const Pattern& pattern) {
    std::vector<Found> out;
    for_each_match(text, pattern,
                   [&](std::size_t begin, std::size_t end) {
                       out.push_back({span_index(begin), span_index(end)});
                   });
    return out;
}

// [begin, end) 里能把整条模式装下的命中数，用来数「这一段里有几个某个词」。
std::size_t count_within(std::string_view text, const Pattern& pattern, std::size_t begin,
                         std::size_t end) {
    std::size_t count = 0;
    const MatchLimits limits{};
    std::size_t from = begin;
    while (from <= end) {
        const MatchRange match = pattern.search(text, from, limits);
        if (match.begin == kNoMatch) break;
        if (match.begin >= end) break;
        // 跨出这一段的不算：模式是在切下来的那一段上找的，装不下的不算命中
        if (match.end > end) {
            std::size_t at = match.begin;
            next_codepoint(text, at);
            from = at;
            continue;
        }
        ++count;
        from = match.end;
    }
    return count;
}

// 一处命中：说明由调用方通过 shape_pending_detail() 取，区间与原文走这里。
// 区间定位置与展示片段，原文定 Match，两者可以不是同一段（比喻堆叠的句子以空白起头就是这种情况）。
// 原文一律从 context.original 上切。空格副本（把结构区域都写成空格、与原文等长的那一份）
// 里那些位置是空格，报给读者的得是作者写的字。
void emit(AlgoWriter writer, void* writer_context, std::size_t begin, std::size_t end,
          const std::string& excerpt) {
    writer(begin, end, excerpt, writer_context);
}

void emit(AlgoWriter writer, void* writer_context, std::string_view text, std::size_t begin,
          std::size_t end) {
    emit(writer, writer_context, begin, end, std::string(text.substr(begin, end - begin)));
}

// 每千字多少处，一位小数。分母是正文字数，按 UTF-16 码元算。
double density(std::size_t count, std::size_t units) {
    return static_cast<double>(count) * 1000.0 / static_cast<double>(std::max<std::size_t>(units, 1));
}

// ---------------------------------------------------------------- 各条判据要的字表

// 术语与意象的词表：这批词在散文里替掉了具体的观察。
const Pattern& terminology_terms() {
    static const Pattern pattern(
        "褶皱|熵增|递归|拓扑|量子纠缠|晶格|超新星|镜像|琥珀|星轨|坍缩|希格斯|麦克斯韦妖|生物硬盘|"
        "暗涌|坐标定位|解码的密钥|文明的辉光|十四行诗|降维|密码学|二级编码|建构|架构|数字孪生|"
        "全息|区块链|物联网|无人机|二维码|算法|大数据");
    return pattern;
}

// 引号内的文字：人在念这批词给人看，不是在用，数之前先挖掉。
const Pattern& quoted_spans() {
    static const Pattern pattern("[「“][^」”]*[」”]");
    return pattern;
}

// 比况词：一句里串了几个喻体，就数这几个词。
const Pattern& simile_markers() {
    static const Pattern pattern("好像|像|如同|仿佛|犹如|好似|宛若|宛如|好比");
    return pattern;
}

// 一句的范围：句末标点与换行都断开，模式是在这一段里找（[^。！？…\n]+ 这种写法）。
const Pattern& sentence_spans() {
    static const Pattern pattern("[^。！？…\n]+");
    return pattern;
}

const Pattern& ordinal_words() {
    static const Pattern pattern("首先|其次|再次|最后|其一|其二|其三");
    return pattern;
}

// 行首的硬编号：1. / 1、 / 1) / （1）/ (1) / 一、 这几类。
const Pattern& numbering_markers() {
    static const Pattern pattern(
        "(?m)^[ \t]*(?:[0-9]{1,2}[.、)]|[（(][0-9]{1,2}[)）]|[一二三四五六七八九十]+[.、])");
    return pattern;
}

// 一条命中落在某一个引号区间里没有：区间按起点升序且互不重叠。
bool inside(const std::vector<Found>& ranges, std::size_t offset) {
    const auto it = std::upper_bound(ranges.begin(), ranges.end(), offset,
                                     [](std::size_t value, const Found& range) {
                                         return value < range.begin;
                                     });
    if (it == ranges.begin()) return false;
    return offset < std::prev(it)->end;
}

// ---------------------------------------------------------------- 七条判据

// 感叹号密度偏高：四百字以上、每千字超过三个全角叹号。只出统计附注。
void exclamation_density(const LintRule& rule, const AlgoContext& context, AlgoWriter, void*,
                         std::vector<std::string>& notes) {
    std::size_t count = 0;
    for (std::size_t at = 0; at < context.text.size();) {
        if (next_codepoint(context.text, at) == 0xFF01) ++count;
    }
    // 短稿不判：四百字以下的稿子，几个叹号就能把密度顶上去，那不是密度问题
    if (context.units < 400) return;
    if (density(count, context.units) <= 3.0) return;
    notes.push_back(fill(rule.detail, {number(count), number(context.units)}));
}

// 序数词骨架：全篇「首先/其次/再次/最后/其一/其二/其三」凑够两处。只出统计附注。
void ordinal_skeleton(const LintRule& rule, const AlgoContext& context, AlgoWriter, void*,
                      std::vector<std::string>& notes) {
    const std::size_t count = find_all(context.text, ordinal_words()).size();
    // 一处是正常行文，两处起才是「首先……其次……」那套骨架
    if (count < 2) return;
    notes.push_back(fill(rule.detail, {number(count)}));
}

// 显式序号标记：行首硬编号三处以上。手写文本很少通篇硬编号，这是排版痕迹。只出统计附注。
void explicit_numbering(const LintRule& rule, const AlgoContext& context, AlgoWriter, void*,
                        std::vector<std::string>& notes) {
    const std::size_t count = find_all(context.text, numbering_markers()).size();
    if (count < 3) return;
    notes.push_back(fill(rule.detail, {number(count)}));
}

// 术语与意象堆砌：挖掉引号内的文字之后，这批词每千字五处以上，逐处报。
void terminology_pile(const LintRule& rule, const AlgoContext& context, AlgoWriter writer,
                      void* writer_context) {
    const std::vector<Found> quotes = find_all(context.text, quoted_spans());
    std::vector<Found> hits;
    for (const Found& found : find_all(context.text, terminology_terms()))
        if (!inside(quotes, found.begin)) hits.push_back(found);
    if (hits.empty()) return;
    // 密度不到门限一条都不报：人写的稿子会把被批评的词念一遍，门限管的是这种
    if (density(hits.size(), context.units) < 5.0) return;

    g_pending_detail = fill(rule.detail, {number(per_thousand(hits.size(), context.units))});
    for (const Found& found : hits)
        emit(writer, writer_context, context.original, found.begin, found.end);
}

// 一句里叠着比喻：一句话六十字以上、串了三个比况词引出的喻体，且全篇密度每千字一处以上。
void fiction_simile_pile(const LintRule& rule, const AlgoContext& context, AlgoWriter writer,
                         void* writer_context) {
    struct Candidate {
        Trimmed sentence;   // 去掉两端空白之后的整句：字数与比喻数按它算
        Found span;         // 句子在稿里的原样范围：位置与展示片段按它算
        std::size_t similes = 0;
    };
    std::vector<Candidate> candidates;
    const MeasuredText measured = context.measured();
    // 一处一处走过全篇的句子：要留下的只有候选那几处，句子表本身不必攒。
    for_each_match(context.text, sentence_spans(),
                   [&](std::size_t begin, std::size_t end) {
                       const Found span{begin, end};
                       const Trimmed sentence = trim(measured, span.begin, span.end);
                       // 短句里连用比喻是修辞，不是堆砌
                       if (sentence.units < 60) return;
                       const std::size_t similes = count_within(
                           context.text, simile_markers(), sentence.begin, sentence.end);
                       if (similes < 3) return;
                       candidates.push_back({sentence, span, similes});
                   });
    if (candidates.empty()) return;
    // 全篇密度不到门限就整条不报：真人写长句时也会带几个比喻，凑够密度才是堆砌
    if (density(candidates.size(), context.units) < 1.0) return;

    for (const Candidate& candidate : candidates) {
        const std::size_t head_units = std::min<std::size_t>(candidate.sentence.units, 30);
        // 报出去的长度截到三十字：位置与展示片段从整句起点算（句首空白也算进窗口），
        // Match 从第一个非空白字算起：命中原文与展示片段分开记。
        const std::size_t head_end =
            byte_after_units(measured, candidate.span.begin, candidate.span.end, head_units);
        const std::size_t match_end =
            byte_after_units(measured, candidate.sentence.begin, candidate.sentence.end, head_units);
        g_pending_detail =
            fill(rule.detail, {number(candidate.sentence.units), number(candidate.similes)});
        emit(writer, writer_context, candidate.span.begin, head_end,
             std::string(context.original.substr(candidate.sentence.begin,
                                                 match_end - candidate.sentence.begin)));
    }
}

// 括号全半角混用：圆括号两侧都出现过才报，报全文最先出现的那个括号。
void mixed_bracket_width(const LintRule& rule, const AlgoContext& context, AlgoWriter writer,
                         void* writer_context) {
    static const Pattern half_width("[()]");
    static const Pattern full_width("[（）]");
    static const Pattern any_width("[()（）]");
    const std::size_t half = find_all(context.text, half_width).size();
    const std::size_t full = find_all(context.text, full_width).size();
    if (half == 0 || full == 0) return;

    const std::vector<Found> first = find_all(context.text, any_width);
    if (first.empty()) return;
    g_pending_detail = fill(rule.detail, {number(half), number(full)});
    emit(writer, writer_context, context.original, first.front().begin, first.front().end);
}

// 引号样式混用：按「套」判。双引号里嵌单引号是规范写法，同一套里的两种不算混用。
void mixed_quote_style(const AlgoContext& context, AlgoWriter writer, void* writer_context) {
    // 六种引号写法分三套：同一套里的双引号与单引号不算混用。
    struct Style {
        const char* family;
        const char* name;
        const Pattern* pattern;
    };
    static const Pattern curly_double("[“”]");
    static const Pattern curly_single("[‘’]");
    static const Pattern ascii_double("\"");
    static const Pattern ascii_single("'");
    static const Pattern corner("[「」]");
    static const Pattern double_corner("[『』]");
    static const Style styles[] = {
        {"curly", "弯引号", &curly_double},        {"curly", "弯单引号", &curly_single},
        {"ascii", "ASCII 直引号", &ascii_double},  {"ascii", "ASCII 直单引号", &ascii_single},
        {"corner", "直角引号", &corner},           {"corner", "双直角引号", &double_corner},
    };

    std::vector<std::string> families;
    std::string detail;
    std::size_t first = std::string_view::npos;
    for (const Style& style : styles) {
        const std::vector<Found> found = find_all(context.text, *style.pattern);
        if (found.empty()) continue;
        detail.append(detail.empty() ? "" : " / ")
            .append(std::string(style.name) + " " + number(found.size()) + " 个");
        first = std::min(first, found.front().begin);
        if (std::find(families.begin(), families.end(), style.family) == families.end())
            families.push_back(style.family);
    }
    // 只有一套引号在用时不是混用
    if (families.size() <= 1) return;

    g_pending_detail = detail;
    emit(writer, writer_context, context.original, first,
         first + char_bytes(static_cast<unsigned char>(context.original[first])));
}

}  // namespace

// 命中说明的交接（见文件头的说明）：调用方在 writer 回调里取当前这一处该带的字。
const std::string& shape_pending_detail() { return g_pending_detail; }

void register_shape_algorithms() {
    register_algorithm("exclamation-density",
                       [](const LintRule& rule, const AlgoContext& context, AlgoWriter writer,
                          void* writer_context, std::vector<std::string>& notes) {
                           exclamation_density(rule, context, writer, writer_context, notes);
                       });
    register_algorithm("ordinal-skeleton",
                       [](const LintRule& rule, const AlgoContext& context, AlgoWriter writer,
                          void* writer_context, std::vector<std::string>& notes) {
                           ordinal_skeleton(rule, context, writer, writer_context, notes);
                       });
    register_algorithm("explicit-numbering",
                       [](const LintRule& rule, const AlgoContext& context, AlgoWriter writer,
                          void* writer_context, std::vector<std::string>& notes) {
                           explicit_numbering(rule, context, writer, writer_context, notes);
                       });
    register_algorithm("terminology-pile",
                       [](const LintRule& rule, const AlgoContext& context, AlgoWriter writer,
                          void* writer_context, std::vector<std::string>&) {
                           terminology_pile(rule, context, writer, writer_context);
                       });
    register_algorithm("fiction-simile-pile",
                       [](const LintRule& rule, const AlgoContext& context, AlgoWriter writer,
                          void* writer_context, std::vector<std::string>&) {
                           fiction_simile_pile(rule, context, writer, writer_context);
                       });
    register_algorithm("mixed-bracket-width",
                       [](const LintRule& rule, const AlgoContext& context, AlgoWriter writer,
                          void* writer_context, std::vector<std::string>&) {
                           mixed_bracket_width(rule, context, writer, writer_context);
                       });
    register_algorithm("mixed-quote-style",
                       [](const LintRule&, const AlgoContext& context, AlgoWriter writer,
                          void* writer_context, std::vector<std::string>&) {
                           mixed_quote_style(context, writer, writer_context);
                       });
}

}  // namespace texttool
