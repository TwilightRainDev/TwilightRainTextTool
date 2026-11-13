// 由代码统计的规则：rhythm 组，含句长均匀、短句连出、篇幅节奏、碎句、段落长短均匀。
//
// 门限、窗口、切分标准按下面写的来，这些常数不许单独调整：
// 输出是按字节比的，差一个字就是红。
//
// 切句自己切，不用 scan.h 的 split_sentences：那一边按「每个句末标点之后」断句，连续省略号
// 各断一句、句末的引号括号落在下一句开头；split_sentences 把连续句末标点与收尾的引号括号并回
// 上一句，句数与句长都跟着变，句长均匀、短句连出、篇幅节奏三条会一起偏。
//
// 切段不自己切：分段方式（逐行与 markdown）由接线层定，切好的段随 AlgoContext 交过来；
// 上下文里没有段落时才按逐行分段现切一份（逐个调算法的测试走这条）。
//
// 命中说明的去处：AlgoWriter 的入参只有字节区间与命中原文，说明里的分句数与平均句长只有算法知道，
// 所以 choppy-clauses 在调 writer 之前把说明放进 choppy_pending_detail()，接线层在回调里取。
// 篇幅节奏只出统计附注、不进命中：短句密度分不开人与模型，它只是「这一段全在短句」的观察。
#include "engine/scan.h"

#include <algorithm>
#include <cmath>
#include <cstdint>
#include <string>
#include <string_view>
#include <vector>

namespace texttool {
namespace {

// 门限，与 fixtures/cli 下 rhythm 各份 .expected.json 里的统计附注对得上：
//
// 短句连出：连着这么多句、每句不超过这么多字。
constexpr std::size_t kShortSentenceMaxChars = 10;
constexpr std::size_t kShortSentenceMinRun = 4;
// 篇幅节奏：八百字一窗、步长四百字（相邻窗重叠一半），短于四百字的稿子整条不判。
constexpr std::size_t kTempoWindowChars = 800;
constexpr std::size_t kTempoMinTextChars = 400;
constexpr std::size_t kTempoWindowStep = 400;
// 窗内十句以上、平均句长低于十五字、最长句不超过二十八字才算整段短句。
constexpr std::size_t kTempoMinSentences = 10;
constexpr std::size_t kTempoMinSentenceChars = 6;
constexpr std::size_t kTempoMaxSentenceChars = 28;
constexpr double kTempoMaxMeanChars = 15.0;
// 句长均匀：十句以上（六字以下的句子不参与），变异系数低于 0.35。
constexpr std::size_t kSentenceUniformMinChars = 6;
constexpr std::size_t kSentenceUniformMinCount = 10;
constexpr double kSentenceUniformMaxCv = 0.35;
// 段落长短均匀：五段以上，变异系数低于 0.25。
constexpr std::size_t kParagraphUniformMinCount = 5;
constexpr double kParagraphUniformMaxCv = 0.25;
// 碎句：一段里八句以上、平均每句不超过五字，且段内有分号或破折号硬接。
constexpr std::size_t kChoppyMinClauses = 8;
constexpr double kChoppyMaxMeanChars = 5.0;
// 碎句报出去的那一截：段首三字以内的原文封顶三十字。
constexpr std::size_t kChoppyHeadChars = 30;

// 长度按 UTF-16 码元数，四字节字符算两个：报出去的行列也是码元。
std::size_t char_width(unsigned char lead) {
    if (lead < 0x80) return 1;
    if ((lead & 0xE0) == 0xC0) return 2;
    if ((lead & 0xF0) == 0xE0) return 3;
    if ((lead & 0xF8) == 0xF0) return 4;
    return 1;
}

std::uint32_t code_point(std::string_view text, std::size_t at, std::size_t width) {
    const auto byte = [&](std::size_t offset) {
        return static_cast<std::uint32_t>(static_cast<unsigned char>(text[at + offset]));
    };
    if (width == 1) return byte(0);
    if (width == 2) return ((byte(0) & 0x1Fu) << 6) | (byte(1) & 0x3Fu);
    if (width == 3) return ((byte(0) & 0x0Fu) << 12) | ((byte(1) & 0x3Fu) << 6) | (byte(2) & 0x3Fu);
    return 0;  // 四字节的都是基本平面以外，没有空白字符
}

// char.IsWhiteSpace 的那一组字符。掐首尾空白时按这一组算，否则句长会差一个字。
bool is_space(std::uint32_t cp) {
    return cp == 0x20 || (cp >= 0x09 && cp <= 0x0D) || cp == 0x85 || cp == 0xA0 || cp == 0x1680 ||
           (cp >= 0x2000 && cp <= 0x200A) || cp == 0x2028 || cp == 0x2029 || cp == 0x202F ||
           cp == 0x205F || cp == 0x3000;
}

// 句末标点：句号、叹号、问号、省略号。每个都单独断一句，连续的省略号因此是两句。
bool is_sentence_end(std::uint32_t cp) {
    return cp == 0x3002 || cp == 0xFF01 || cp == 0xFF1F || cp == 0x2026;
}

// 分句的停顿：句末、逗号、顿号、分号、冒号、换行与破折号。
bool is_clause_break(std::uint32_t cp) {
    return is_sentence_end(cp) || cp == 0xFF0C || cp == 0x3001 || cp == 0xFF1B || cp == 0xFF1A ||
           cp == '\n' || cp == '\r' || cp == 0x2014;
}

// 一句的结尾：句末标点之后的位置，字节下标与按 UTF-16 码元数的下标各存一份。
//
// 按原文的字符宽度走。空格副本（把结构区域都写成空格、与原文等长的那一份）里被遮的字
// 是好几个空格，照它数一个汉字会变成三处。
// 断句看空格副本（结构区域里写着的句号不断句），码元数按原文算。
struct Cut {
    std::size_t byte = 0;
    std::size_t unit = 0;
};

std::vector<Cut> sentence_cuts(const MeasuredText& text) {
    const std::string_view scan = text.scan;
    const std::string_view original = text.original;
    std::vector<Cut> cuts;
    std::size_t unit = 0;
    std::size_t at = 0;
    while (at < scan.size()) {
        const std::size_t width = char_width(static_cast<unsigned char>(original[at]));
        if (at + width > scan.size()) {
            ++unit;
            ++at;
            continue;
        }
        // 被遮住的字符：空格副本的这个位置是空格，原文不是
        const bool masked = scan[at] != original[at];
        const std::uint32_t cp = masked ? std::uint32_t{' '} : code_point(scan, at, width);
        unit += (width == 4) ? 2 : 1;
        at += width;
        if (is_sentence_end(cp)) cuts.push_back({at, unit});
    }
    return cuts;
}

// 每一句去首尾空白后的长度。末尾那截空着时也占一条 0。
std::vector<std::size_t> sentence_lengths(const MeasuredText& text) {
    const std::vector<Cut> cuts = sentence_cuts(text);
    std::vector<std::size_t> lengths;
    lengths.reserve(cuts.size() + 1);
    std::size_t begin = 0;
    for (const Cut& cut : cuts) {
        lengths.push_back(trimmed_units(text, begin, cut.byte));
        begin = cut.byte;
    }
    lengths.push_back(trimmed_units(text, begin, text.scan.size()));
    return lengths;
}

// 按段落切，并掐掉首尾的空白字符：段落的起点就是命中要报的位置。
// 接线层切好的段原样用：段首尾的空白它已经掐过，段界也与段落作用域的数据规则一致。
std::vector<Span> paragraphs_of(std::string_view text, const std::vector<Span>* prepared) {
    if (prepared) return *prepared;

    std::vector<Span> paragraphs;
    for (const Span& span : split_paragraphs(text)) {
        std::size_t begin = span.begin;
        std::size_t end = span.end;
        while (begin < end) {
            const std::size_t width = char_width(static_cast<unsigned char>(text[begin]));
            if (begin + width > end || !is_space(code_point(text, begin, width))) break;
            begin += width;
        }
        while (end > begin) {
            std::size_t start = end - 1;
            while (start > begin && (static_cast<unsigned char>(text[start]) & 0xC0) == 0x80) --start;
            if (!is_space(code_point(text, start, end - start))) break;
            end = start;
        }
        if (end > begin) paragraphs.push_back({span_index(begin), span_index(end)});
    }
    return paragraphs;
}

double mean_of(const std::vector<std::size_t>& values) {
    double sum = 0;
    for (const std::size_t value : values) sum += static_cast<double>(value);
    return sum / static_cast<double>(values.size());
}

// 变异系数：标准差除以平均值，标准差按总体算（除以条数）。
double variation_of(const std::vector<std::size_t>& values) {
    const double mean = mean_of(values);
    double squares = 0;
    for (const std::size_t value : values) {
        const double gap = static_cast<double>(value) - mean;
        squares += gap * gap;
    }
    return std::sqrt(squares / static_cast<double>(values.size())) / mean;
}

// 两位小数，四舍五入到远离零的那一侧，写出来固定两位、不省末尾的零。
std::string format_two_decimals(double value) {
    const long long hundredths = std::llround(value * 100.0);
    std::string out = std::to_string(hundredths / 100);
    const long long fraction = hundredths % 100;
    out.push_back('.');
    out.push_back(static_cast<char>('0' + fraction / 10));
    out.push_back(static_cast<char>('0' + fraction % 10));
    return out;
}

// 保留一位小数后按整数原样写出：对应 Math.Round(value, 1) 再交给 string.Format 的默认写法
// 2.125 给 2.1、4.75 给 4.8（取偶）、整数值不带小数点。
std::string format_one_decimal(double value) {
    const long long tenths = std::llround(std::nearbyint(value * 10.0));
    const long long whole = tenths / 10;
    const long long digit = tenths % 10;
    if (digit == 0) return std::to_string(whole);
    return std::to_string(whole) + "." + std::to_string(digit);
}

// 规则表里的说明是带 {0}、{1} 的模板，代入参数后才是报给读者的那一句。
std::string fill(std::string_view text, const std::vector<std::string>& args) {
    std::string out;
    for (std::size_t i = 0; i < text.size();) {
        if (text[i] != '{') {
            out.push_back(text[i]);
            ++i;
            continue;
        }
        const std::size_t close = text.find('}', i + 1);
        bool digits = close != std::string_view::npos && close > i + 1;
        std::size_t index = 0;
        for (std::size_t k = i + 1; digits && k < close; ++k) {
            if (text[k] < '0' || text[k] > '9') {
                digits = false;
            } else {
                index = index * 10 + static_cast<std::size_t>(text[k] - '0');
            }
        }
        if (!digits || index >= args.size()) {
            out.push_back(text[i]);
            ++i;
            continue;
        }
        out += args[index];
        i = close + 1;
    }
    return out;
}

bool contains_any(std::string_view text, const std::vector<std::string_view>& needles) {
    for (const std::string_view needle : needles) {
        if (text.find(needle) != std::string_view::npos) return true;
    }
    return false;
}

// 当前这一处命中的说明。设它的一定在调 writer 之前，取它的一定在回调里，两处在同一个线程上。
std::string& pending_detail() {
    static thread_local std::string detail;
    return detail;
}

// 篇幅节奏报没报过，要传给短句连出那条：同一次扫描里两条规则共用这一份结论。
// 算法接口里没有这个字段，这里按文本各存一份：地址、长度、内容哈希三项全对上才算同一份稿子，
// 换了稿子对不上，就不继承别处的结论。
struct TempoMark {
    const void* data = nullptr;
    std::size_t size = 0;
    std::uint64_t hash = 0;
    bool reported = false;
};

TempoMark& tempo_mark() {
    static TempoMark mark;
    return mark;
}

std::uint64_t content_hash(std::string_view text) {
    std::uint64_t hash = 1469598103934665603ULL;
    for (const char byte : text) {
        hash ^= static_cast<unsigned char>(byte);
        hash *= 1099511628211ULL;
    }
    return hash;
}

// 按 UTF-16 码元往前推的游标，顺带记行号。窗口起点与终点都只往后走，整篇因此只扫一遍。
// 码元边界按原文的字符宽度算，行号看空格副本（两份的换行位置一样）。
struct UnitWalker {
    MeasuredText text;
    std::size_t byte = 0;
    std::size_t unit = 0;
    std::size_t line = 1;

    void advance_to(std::size_t target_unit) {
        while (byte < text.scan.size() && unit < target_unit) {
            const std::size_t width =
                char_width(static_cast<unsigned char>(text.original[byte]));
            if (byte + width > text.scan.size()) {
                ++unit;
                ++byte;
                return;
            }
            const std::size_t step = (width == 4) ? 2 : 1;
            if (unit + step > target_unit) return;  // 目标落在代理对中间：停在字符边界上
            if (text.scan[byte] == '\n') ++line;
            byte += width;
            unit += step;
        }
    }
};

// ---------------------------------------------------------------- 五条规则

// paragraph-length-uniform 段落长度过于均一：五段以上、变异系数低于 0.25 才出附注。
// 段落长短参差是风格问题，不是逐条照着改的错，所以只进统计。
void run_paragraph_length_uniform(const LintRule& rule, const AlgoContext& context, AlgoWriter,
                                  void*, std::vector<std::string>& notes) {
    const std::vector<Span> paragraphs = paragraphs_of(context.text, context.paragraphs);
    if (paragraphs.size() < kParagraphUniformMinCount) return;

    std::vector<std::size_t> lengths;
    lengths.reserve(paragraphs.size());
    for (const Span& paragraph : paragraphs) {
        lengths.push_back(measured_units(context.measured(), paragraph.begin, paragraph.end));
    }
    const double cv = variation_of(lengths);
    if (cv >= kParagraphUniformMaxCv) return;

    notes.push_back(fill(rule.detail, {format_two_decimals(cv), std::to_string(paragraphs.size())}));
}

// sentence-length-uniform 句长分布过于整齐：十句以上、变异系数低于 0.35 才出附注。
// 短于六个字的句子不参与：一句「好。」的句长由标点决定，算进来只会把分布搅乱。
void run_sentence_length_uniform(const LintRule& rule, const AlgoContext& context, AlgoWriter,
                                 void*, std::vector<std::string>& notes) {
    std::vector<std::size_t> lengths;
    for (const std::size_t length : sentence_lengths(context.measured())) {
        if (length >= kSentenceUniformMinChars) lengths.push_back(length);
    }
    if (lengths.size() < kSentenceUniformMinCount) return;

    const double cv = variation_of(lengths);
    if (cv >= kSentenceUniformMaxCv) return;

    notes.push_back(fill(rule.detail, {format_two_decimals(cv), std::to_string(lengths.size())}));
}

// short-sentence-run 连续短句：一句十来字正常，连着四句以上就是节拍化，每句都在表演「克制」。
// 与篇幅节奏查的不是同一件事：那条看整窗的平均句长，这条看是否连着短；篇幅节奏已经报过的
// 稿子这条不再报，同一个现象不在报告里出现两遍。
void run_short_sentence_run(const LintRule& rule, const AlgoContext& context, AlgoWriter, void*,
                            std::vector<std::string>& notes) {
    const TempoMark& mark = tempo_mark();
    if (mark.reported && mark.data == context.text.data() && mark.size == context.text.size() &&
        mark.hash == content_hash(context.text)) {
        return;
    }

    std::size_t streak = 0;
    std::size_t longest = 0;
    for (const std::size_t length : sentence_lengths(context.measured())) {
        if (length == 0) continue;
        streak = length <= kShortSentenceMaxChars ? streak + 1 : 0;
        longest = std::max(longest, streak);
    }
    if (longest < kShortSentenceMinRun) return;

    notes.push_back(fill(rule.detail, {std::to_string(longest),
                                       std::to_string(kShortSentenceMaxChars)}));
}

// sentence-tempo 节拍化短句：按八百字一窗、四百字一步算平均句长。
//
// 三条门限同时成立才算整段短句：窗内十句以上、平均句长低于十五字、最长句不超过二十八字。
// 最后一条是这条规则的关键：真人的段落总要有一句把话说长，节拍稿一句都没有。
// 相邻窗重叠一半，同一段短句会在几窗里连续成立，所以连着成立的只报第一条，不跟着步长刷屏。
void run_sentence_tempo(const LintRule& rule, const AlgoContext& context, AlgoWriter, void*,
                        std::vector<std::string>& notes) {
    const std::string_view text = context.text;
    if (context.units < kTempoMinTextChars) {
        tempo_mark() = {text.data(), text.size(), 0, false};
        return;
    }

    const MeasuredText measured = context.measured();
    const std::vector<Cut> cuts = sentence_cuts(measured);
    const std::size_t window_chars = std::min(kTempoWindowChars, context.units);
    UnitWalker start_at{measured};
    UnitWalker end_at{measured};
    std::size_t cut_index = 0;
    std::size_t reported = 0;
    bool previous_was_tempo = false;

    for (std::size_t start = 0; start + window_chars <= context.units; start += kTempoWindowStep) {
        const std::size_t end = start + window_chars;
        start_at.advance_to(start);
        end_at.advance_to(end);
        while (cut_index < cuts.size() && cuts[cut_index].unit <= start) ++cut_index;

        std::size_t sentence_count = 0;
        std::size_t sentence_chars = 0;
        std::size_t longest = 0;
        std::size_t from = start_at.byte;
        for (std::size_t k = cut_index;; ++k) {
            const bool inside = k < cuts.size() && cuts[k].unit < end;
            const std::size_t to = inside ? cuts[k].byte : end_at.byte;
            const std::size_t length = trimmed_units(measured, from, to);
            if (length >= kTempoMinSentenceChars) {
                ++sentence_count;
                sentence_chars += length;
                longest = std::max(longest, length);
            }
            if (!inside) break;
            from = to;
        }

        const double mean =
            sentence_count > 0 ? static_cast<double>(sentence_chars) / sentence_count : 0.0;
        const bool short_window = sentence_count >= kTempoMinSentences &&
                                  mean < kTempoMaxMeanChars && longest <= kTempoMaxSentenceChars;
        if (!short_window) {
            previous_was_tempo = false;
            continue;
        }
        if (!previous_was_tempo) {
            // 行号照它取：窗口起点所在行再加一，报出来的是「第 2 行起」这种数。
            notes.push_back(fill(rule.detail,
                                 {std::to_string(start_at.line + 1), std::to_string(sentence_count),
                                  format_one_decimal(mean), std::to_string(longest)}));
            ++reported;
        }
        previous_was_tempo = true;
    }

    tempo_mark() = {text.data(), text.size(), content_hash(text), reported > 0};
}

// choppy-clauses 短句硬连：该断的地方用分号、破折号硬接，一段里七八个短促分句，
// 主语宾语全省了，读者得自己把话补全。
//
// 判的是句子形状，不是用词：分句数与平均句长两个都要够门槛，所以正常的中文并列
// （满篇顿号）不会误伤；段内出现「首先、其次」这类序数骨架时是结构，不是硬接，整段不判。
void run_choppy_clauses(const LintRule& rule, const AlgoContext& context, AlgoWriter writer,
                        void* writer_context, std::vector<std::string>&) {
    const std::string_view text = context.text;
    const MeasuredText measured = context.measured();
    for (const Span& paragraph : paragraphs_of(text, context.paragraphs)) {
        const std::string_view body = text.substr(paragraph.begin, paragraph.end - paragraph.begin);
        if (body.find("；") == std::string_view::npos && body.find("——") == std::string_view::npos) {
            continue;
        }
        if (contains_any(body, {"首先", "其次", "再次", "最后", "其一", "其二", "其三"})) continue;

        std::vector<std::size_t> clauses;
        std::size_t clause_begin = paragraph.begin;
        std::size_t at = paragraph.begin;
        while (at < paragraph.end) {
            const std::size_t width = char_width(static_cast<unsigned char>(text[at]));
            if (at + width > paragraph.end) break;
            if (is_clause_break(code_point(text, at, width))) {
                const std::size_t length = trimmed_units(measured, clause_begin, at);
                if (length > 0) clauses.push_back(length);
                clause_begin = at + width;
            }
            at += width;
        }
        const std::size_t tail = trimmed_units(measured, clause_begin, paragraph.end);
        if (tail > 0) clauses.push_back(tail);

        if (clauses.size() < kChoppyMinClauses) continue;
        double sum = 0;
        for (const std::size_t length : clauses) sum += static_cast<double>(length);
        const double mean = sum / static_cast<double>(clauses.size());
        if (mean > kChoppyMaxMeanChars) continue;

        // 报的是段首那一截原文：位置与长度都按它算，最多三十字。
        // 三十字按原文的码元数算：空格副本里被遮的多字节字成了好几个空格。
        const std::size_t head_end =
            byte_after_units(measured, paragraph.begin, paragraph.end, kChoppyHeadChars);
        // 说明里的两个数字（切了几个分句、平均几字）只有这里知道，先放进交接口再调 writer。
        // 报给读者的那一截从原文上切：空格副本里结构区域是空格。
        pending_detail() =
            fill(rule.detail, {std::to_string(clauses.size()), format_one_decimal(mean)});
        writer(paragraph.begin, head_end,
               std::string(context.original.substr(paragraph.begin, head_end - paragraph.begin)),
               writer_context);
    }
}

}  // namespace

// choppy-clauses 本次命中的说明：算好之后放进交接口，接线层在 writer 回调里取一次。
const std::string& choppy_pending_detail() { return pending_detail(); }

void register_rhythm_algorithms() {
    register_algorithm("sentence-length-uniform", &run_sentence_length_uniform);
    register_algorithm("short-sentence-run", &run_short_sentence_run);
    register_algorithm("sentence-tempo", &run_sentence_tempo);
    register_algorithm("choppy-clauses", &run_choppy_clauses);
    register_algorithm("paragraph-length-uniform", &run_paragraph_length_uniform);
}

}  // namespace texttool
