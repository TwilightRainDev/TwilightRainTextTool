// 判据执行：按规则表扫出命中。
//
// 扫描的步骤按这个顺序走，中途不能跳：
// 先建博文的空格副本（把结构区域都写成空格、与原文逐字节对齐的那一份），
// 定用哪种分段方式与「这份稿按不按博文处理」，再逐条规则过跳过的四道门，
// 然后按规则的作用域取候选、配多条模式的规则去掉重叠、过密度门限，最后整表按 (行, 列) 稳定排一次。
//
// 规则与统计读的是空格副本：博文的结构区域在那份里是空格，匹配不到词，
// 被遮的地方当空行，篇幅与密度分母因此只算正文。位置、行列、命中原文仍从原文取。
//
// 规则表里的三类规则走同一趟：词面与正则两类在这里取候选，标了 algorithm 的那 15 条交给
// engine/scan.h 登记的算法，算法写回的命中与附注跟数据规则的一起排、一起出报告。
//
// 匹配用的是 engine/regex.h，位置换算用 engine/location.h，博文判定与空格副本用
// engine/markdown.h，规则元数据全部取自规则表：
// 这一层不写第二份标题、说明、严重程度，命中也只带规则表下标，不带这些字符串。
#include "engine/lint_runner.h"

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <exception>
#include <map>
#include <memory>
#include <optional>
#include <stdexcept>
#include <string>
#include <string_view>
#include <thread>
#include <utility>
#include <vector>

// 少数几个核（见下面的 available_cores）：Linux 上要问调度器。
#if defined(__linux__)
#include <sched.h>
#endif

#include "engine/location.h"
#include "engine/markdown.h"
#include "engine/regex.h"
#include "engine/scan.h"

namespace texttool {

namespace {

using Clock = std::chrono::steady_clock;

// 分段计时：只在环境变量 TEXTTOOL_TIMING 非空时打开，把各段耗时打到 stderr。
// 改性能先看这组数字，不靠猜是哪一段慢；平时跑一个字节都不多输出。
// TEXTTOOL_TIMING_RULES 同时打开时，再按规则逐条打一行：哪一条是耗时的大头，直接看这一组。
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

// 附加串那块内存先按这个数预留：十几万条说明合起来在几兆上下。
// 预留只是占住地址，真正占页是写进去的时候。
constexpr std::size_t kArenaReserve = 8u * 1024 * 1024;

// 命中表的预留：按稿件长度估（平均每多少字节报一处），估出来的格子数上下都有个界。
// 预留只是占住地址，写进去才落页；估大了不占内存，估小了退回翻倍扩容。
// 十几万处的表在扩容那一刻要同时占着新旧两份，峰值就落在那一瞬，所以这张表先估够。
constexpr std::size_t kHitBytesPerHit = 64;
constexpr std::size_t kHitGrow = 8192;
constexpr std::size_t kHitReserveLimit = 1u << 20;

// 前这么多条算法命中的附加串记在早期表里（见 Scan::store_early_detail）。
// 早期表按「这一处自己的格号」下标，一格只属于一条命中。
enum class EarlyKind : std::uint8_t {
    position = 1,  // 说明按这个位置的行列现拼，位置在 early_value_
    text = 2,      // 描述子在 early_text_ 里
};

// ---------------------------------------------------------------- UTF-8 与空白

// 一个 UTF-8 序列占几个字节、折算成几个 UTF-16 码元。坏字节按一个字节一个码元算，
// 与 location.cpp 的换算相同：两处给出的行列与长度必须一致。
struct Unit {
    std::size_t bytes = 1;
    std::size_t units = 1;
};

Unit measure(std::string_view text, std::size_t at) {
    const auto lead = static_cast<unsigned char>(text[at]);
    if (lead < 0x80) return {1, 1};
    std::size_t bytes = 0;
    if ((lead & 0xE0) == 0xC0) {
        bytes = 2;
    } else if ((lead & 0xF0) == 0xE0) {
        bytes = 3;
    } else if ((lead & 0xF8) == 0xF0) {
        bytes = 4;
    } else {
        return {1, 1};
    }
    if (at + bytes > text.size()) return {1, 1};
    for (std::size_t i = 1; i < bytes; ++i)
        if ((static_cast<unsigned char>(text[at + i]) & 0xC0) != 0x80) return {1, 1};
    return {bytes, bytes == 4 ? std::size_t{2} : std::size_t{1}};
}

std::size_t next_char(std::string_view text, std::size_t at) {
    return std::min(text.size(), at + measure(text, at).bytes);
}


// .NET 的 char.IsWhiteSpace 认的那些码点。规则里的 Trim() 与段首尾去空白都按它算，
// 少认一个全角空格就会让段落窗口差一格。
bool is_space(uint32_t cp) {
    return (cp >= 0x09 && cp <= 0x0D) || cp == 0x20 || cp == 0x85 || cp == 0xA0 || cp == 0x1680 ||
           (cp >= 0x2000 && cp <= 0x200A) || cp == 0x2028 || cp == 0x2029 || cp == 0x202F ||
           cp == 0x205F || cp == 0x3000;
}

uint32_t codepoint_at(std::string_view text, std::size_t at) {
    const auto b0 = static_cast<unsigned char>(text[at]);
    const Unit unit = measure(text, at);
    if (unit.bytes == 1) return b0;
    if (unit.bytes == 2)
        return static_cast<uint32_t>((b0 & 0x1F) << 6) |
               (static_cast<unsigned char>(text[at + 1]) & 0x3F);
    if (unit.bytes == 3)
        return static_cast<uint32_t>((b0 & 0x0F) << 12) |
               (static_cast<uint32_t>(static_cast<unsigned char>(text[at + 1]) & 0x3F) << 6) |
               (static_cast<unsigned char>(text[at + 2]) & 0x3F);
    return static_cast<uint32_t>((b0 & 0x07) << 18) |
           (static_cast<uint32_t>(static_cast<unsigned char>(text[at + 1]) & 0x3F) << 12) |
           (static_cast<uint32_t>(static_cast<unsigned char>(text[at + 2]) & 0x3F) << 6) |
           (static_cast<unsigned char>(text[at + 3]) & 0x3F);
}

bool starts_with(std::string_view text, std::string_view prefix) {
    return text.size() >= prefix.size() && text.compare(0, prefix.size(), prefix) == 0;
}

// 去掉两端的空白，区间原地收窄。
void trim_span(std::string_view text, std::size_t& begin, std::size_t& end) {
    while (begin < end && is_space(codepoint_at(text, begin))) begin = next_char(text, begin);
    while (end > begin) {
        // 从后往前退一个码点：不能按字节退，汉字与表情符号都不是单字节
        std::size_t at = end - 1;
        while (at > begin && (static_cast<unsigned char>(text[at]) & 0xC0) == 0x80) --at;
        if (!is_space(codepoint_at(text, at))) break;
        end = at;
    }
}

// 行首行尾：行尾不含换行，也不含行尾的回车。
std::size_t line_end(std::string_view text, std::size_t start, std::size_t end) {
    while (end > start && text[end - 1] == '\r') --end;
    return end;
}

// ---------------------------------------------------------------- 行形状

// 一行在 markdown 结构上算什么。判定就是下面这一份，段落切分只认这里。
bool is_blank_line(std::string_view text, std::size_t begin, std::size_t end) {
    trim_span(text, begin, end);
    return begin == end;
}

std::string_view trim_copy(std::string_view raw) {
    std::size_t begin = 0;
    std::size_t end = raw.size();
    trim_span(raw, begin, end);
    return raw.substr(begin, end - begin);
}

bool is_list_item(std::string_view raw) {
    const std::string_view trimmed = trim_copy(raw);
    return starts_with(trimmed, "- ") || starts_with(trimmed, "* ") || starts_with(trimmed, "+ ");
}

bool is_quote(std::string_view raw) { return starts_with(trim_copy(raw), ">"); }

bool is_numbered_item(std::string_view raw) {
    const std::string_view trimmed = trim_copy(raw);
    std::size_t digits = 0;
    while (digits < trimmed.size() && trimmed[digits] >= '0' && trimmed[digits] <= '9') ++digits;
    if (digits == 0 || digits + 1 >= trimmed.size()) return false;
    // 句子里的「、」与点号都算编号的收尾：数字后面还得跟着字
    return trimmed[digits] == '.' || starts_with(trimmed.substr(digits), "\xE3\x80\x81");
}

// 列表项、引用行、编号行各自成段：并进散文段会让段内序数词跨条目误报。
bool is_block_line(std::string_view raw) {
    return is_list_item(raw) || is_quote(raw) || is_numbered_item(raw);
}

// ---------------------------------------------------------------- 段落怎么切

// 分段方式的探测：头 4096 个码元里空行占行数的比例达到一成，就按 markdown 读。
// 取样长度与阈值是定好的：判错一处，段落作用域的几十条规则会一起偏。
bool is_markdown_mode(std::string_view text) {
    constexpr std::size_t kProbeChars = 4096;
    constexpr double kBlankLineRatio = 0.10;

    std::size_t probe_end = 0;
    std::size_t units = 0;
    while (probe_end < text.size() && units < kProbeChars) {
        units += measure(text, probe_end).units;
        probe_end = next_char(text, probe_end);
    }
    std::size_t end = probe_end;
    if (end > 0 && text[end - 1] == '\n') --end;

    std::size_t lines = 0;
    std::size_t blanks = 0;
    std::size_t pos = 0;
    while (true) {
        const std::size_t newline = text.find('\n', pos);
        const std::size_t line_stop =
            (newline == std::string_view::npos || newline > end) ? end : newline;
        ++lines;
        if (is_blank_line(text, pos, line_stop)) ++blanks;
        if (line_stop >= end) break;
        pos = line_stop + 1;
    }
    // blanks < lines 兜住空串与纯空白文件："" 拆出 1 行 1 空行，否则会被判成 markdown
    return lines > 0 && blanks < lines &&
           static_cast<double>(blanks) / static_cast<double>(lines) >= kBlankLineRatio;
}

// 正文字数：按 UTF-16 码元算，被博文结构遮住的字符不算。密度门限的分母与报告里的字数都用它。
// 一个字符整个算或整个不算：空格副本按字节标，它若只盖住一个字符的一半，仍按整个字符算进去。
std::size_t body_units(std::string_view text, const BlogMask& mask) {
    std::size_t units = 0;
    for (std::size_t at = 0; at < text.size();) {
        const Unit unit = measure(text, at);
        if (!mask.is_masked(at)) units += unit.units;
        at += unit.bytes;
    }
    return units;
}

// 一段就是原文里的一段字节区间：正文按需从原文里切，段本身不必各带一份视图。
// 十几万段各多带八个字节就是几百 KB，而这份表在峰值上一直是活的。
using Paragraph = Span;

// 逐行分段：每一行自成一段。markdown 分段：空行分段，软换行并入同一段，
// 列表项、引用行、编号行各自成段。
std::vector<Paragraph> split_paragraphs(std::string_view text, bool markdown) {
    std::vector<Paragraph> paragraphs;
    if (!markdown) {
        std::size_t offset = 0;
        std::size_t line_start = 0;
        for (std::size_t i = 0; i <= text.size(); ++i) {
            if (i < text.size() && text[i] != '\n') continue;
            const std::size_t end = line_end(text, line_start, i);
            const std::size_t trimmed_begin = line_start;
            std::size_t from = line_start;
            std::size_t to = end;
            trim_span(text, from, to);
            if (to > from)
                paragraphs.push_back({span_index(offset + (from - trimmed_begin)), span_index(to)});
            offset += i - line_start + 1;
            line_start = i + 1;
            if (i == text.size()) break;
        }
        return paragraphs;
    }

    // 行的起止只存下标：段落才是要留下的东西
    std::vector<std::size_t> line_start{0};
    std::vector<std::size_t> line_stop;
    for (std::size_t pos = 0; pos < text.size(); ++pos) {
        if (text[pos] != '\n') continue;
        line_stop.push_back(line_end(text, line_start.back(), pos));
        line_start.push_back(pos + 1);
    }
    line_stop.push_back(line_end(text, line_start.back(), text.size()));

    auto add_block = [&](std::size_t start, std::size_t end) {
        std::size_t from = start;
        std::size_t to = end;
        trim_span(text, from, to);
        if (to > from) paragraphs.push_back({span_index(from), span_index(to)});
    };

    std::size_t i = 0;
    while (i < line_start.size()) {
        if (is_blank_line(text, line_start[i], line_stop[i])) {
            ++i;
            continue;
        }
        const std::string_view raw = text.substr(line_start[i], line_stop[i] - line_start[i]);
        if (is_block_line(raw)) {
            add_block(line_start[i], line_stop[i]);
            ++i;
            continue;
        }
        std::size_t end_line = i;
        while (end_line + 1 < line_start.size() &&
               !is_blank_line(text, line_start[end_line + 1], line_stop[end_line + 1]) &&
               !is_block_line(text.substr(line_start[end_line + 1],
                                           line_stop[end_line + 1] - line_start[end_line + 1])))
            ++end_line;
        add_block(line_start[i], line_stop[end_line]);
        i = end_line + 1;
    }
    return paragraphs;
}

// ---------------------------------------------------------------- 规则编译

// 词面串按正则字面把它转义：词表里写什么就找什么，不交给引擎解释。
std::string escape_literal(std::string_view literal) {
    std::string out;
    out.reserve(literal.size() + 8);
    for (char c : literal) {
        if (c == '\\' || c == '^' || c == '$' || c == '.' || c == '|' || c == '?' || c == '*' ||
            c == '+' || c == '(' || c == ')' || c == '[' || c == ']' || c == '{' || c == '}')
            out.push_back('\\');
        out.push_back(c);
    }
    return out;
}


// ---------------------------------------------------------------- 候选与去重

struct Candidate {
    std::size_t begin = 0;
    std::size_t end = 0;
    std::size_t hint = 0;
    std::size_t order = 0;  // 候选原来的先后，同长同起点时用它保序
};

// 一趟扫描收候选的地方。
//
// 报告里一条规则最多列 kMaxListedHitsPerRule 处，而判据、密度门限、报告里的总数按真实处数算，
// 与列不列得下无关。所以位置只留够报告用的那些，再多的只记处数：15MB 的稿子上越界字符一趟收
// 十三万处，整张表留着是八兆，扩容那一下新旧两张同时在，前后空出十几兆；而报告里它只列两百处。
//
// 能不能截：会去重叠的规则不能截（去掉重叠之后后面的候选可能顶上来），不封顶的那两趟
// （带 --baseline 的写前、写后）一处也不能丢。其余规则按模式各留 kMaxListedHitsPerRule 处，
// 报告的截取按收集先后进行，先攒下的那几百处正好是截取之后会留下的那些，结果与一处不留地攒完再截相同。
class CandidateList {
public:
    explicit CandidateList(bool keep_all) : keep_all_(keep_all) {}

    // 换一个模式：记下起点，这个模式凑不够次数时按它整段撤掉。
    void begin_pattern() {
        pattern_listed_base_ = listed_.size();
        pattern_total_base_ = total_;
    }

    void push(const Candidate& candidate) {
        ++total_;
        if (keep_all_ || listed_.size() - pattern_listed_base_ < kMaxListedHitsPerRule)
            listed_.push_back(candidate);
    }

    // 这个模式收下的处数（含没留位置的那些）够不够判据要的次数。
    bool pattern_reached(std::size_t min_count) const {
        return total_ - pattern_total_base_ >= min_count;
    }

    // 这个模式凑不够次数：它收的那几处连位置带处数一起撤掉，与整表收集时 resize 回去一样。
    void drop_pattern() {
        listed_.resize(pattern_listed_base_);
        total_ = pattern_total_base_;
    }

    // 快路一次补上没留位置的那几处：只记处数，不动位置表（与逐条 push 到超上限之后等价）。
    void add_total(std::size_t count) { total_ += count; }

    std::size_t total() const { return total_; }

    // 只记了处数、没留位置的处数。报告里「另有 N 处未列出」与总处数要把它们算上。
    std::size_t unlisted() const { return total_ - listed_.size(); }

    std::vector<Candidate> take() { return std::move(listed_); }

private:
    bool keep_all_;
    std::vector<Candidate> listed_;
    std::size_t total_ = 0;
    std::size_t pattern_listed_base_ = 0;
    std::size_t pattern_total_base_ = 0;
};

// 候选到这个数以下就直接两两比：索引一次要建几个数组加两次排序，小清单上不划算。
constexpr std::size_t kDropOverlapsScanLimit = 32;

std::vector<Candidate> drop_overlaps_by_scan(std::vector<Candidate> candidates) {
    std::vector<std::size_t> order(candidates.size());
    for (std::size_t i = 0; i < order.size(); ++i) order[i] = i;
    std::stable_sort(order.begin(), order.end(), [&](std::size_t a, std::size_t b) {
        const std::size_t len_a = candidates[a].end - candidates[a].begin;
        const std::size_t len_b = candidates[b].end - candidates[b].begin;
        if (len_a != len_b) return len_a > len_b;
        if (candidates[a].begin != candidates[b].begin) return candidates[a].begin < candidates[b].begin;
        return a < b;
    });

    std::vector<Candidate> kept;
    for (std::size_t i : order) {
        const Candidate& candidate = candidates[i];
        bool overlaps = false;
        for (const Candidate& other : kept) {
            if (candidate.begin < other.end && other.begin < candidate.end) {
                overlaps = true;
                break;
            }
        }
        if (!overlaps) kept.push_back(candidate);
    }
    std::stable_sort(kept.begin(), kept.end(),
                     [](const Candidate& a, const Candidate& b) { return a.begin < b.begin; });
    return kept;
}

// 同一条规则里，同一处只报一次：两张词表可能盖住同一个词，一个词也可能被两条词表
// 从不同位置咬住，两种都数两遍同一条证据。重叠时留长的，同长留起点靠前的。
//
// 候选多时走前缀最大值树：相交等于「起点早于本段终点的已留段里，有终点越过本段起点」，
// 每条只需一次二分加一次前缀查询。取舍语义与逐个比较完全相同，返回顺序仍按起点升序。
std::vector<Candidate> drop_overlaps(std::vector<Candidate> candidates) {
    if (candidates.size() <= 1) return candidates;
    if (candidates.size() <= kDropOverlapsScanLimit) return drop_overlaps_by_scan(std::move(candidates));

    const std::size_t count = candidates.size();

    // 位置序：候选按起点升序。相交判定里的「起点早于本段终点」在这条序上正好是一段前缀
    std::vector<std::size_t> by_start(count);
    for (std::size_t i = 0; i < count; ++i) by_start[i] = i;
    std::stable_sort(by_start.begin(), by_start.end(), [&](std::size_t a, std::size_t b) {
        return candidates[a].begin < candidates[b].begin;
    });
    std::vector<std::size_t> starts(count);
    std::vector<std::size_t> rank(count);
    for (std::size_t i = 0; i < count; ++i) {
        starts[i] = candidates[by_start[i]].begin;
        rank[by_start[i]] = i;
    }

    // 前缀最大值树：tree[i] 存位置序末尾是 i 的那一段里已留段终点的最大值
    constexpr std::size_t kNone = 0;
    std::vector<std::size_t> tree(count + 1, kNone);
    auto max_end_before = [&](std::size_t positions) {
        std::size_t best = kNone;
        for (std::size_t i = positions; i > 0; i -= i & (~i + 1)) best = std::max(best, tree[i]);
        return best;
    };
    auto keep = [&](std::size_t position, std::size_t end) {
        for (std::size_t i = position + 1; i <= count; i += i & (~i + 1)) tree[i] = std::max(tree[i], end);
    };

    // 优先级序：长的先看，同长按起点，同长同起点按候选原来的先后
    std::vector<std::size_t> by_priority(count);
    for (std::size_t i = 0; i < count; ++i) by_priority[i] = i;
    std::stable_sort(by_priority.begin(), by_priority.end(), [&](std::size_t a, std::size_t b) {
        const std::size_t len_a = candidates[a].end - candidates[a].begin;
        const std::size_t len_b = candidates[b].end - candidates[b].begin;
        if (len_a != len_b) return len_a > len_b;
        if (candidates[a].begin != candidates[b].begin) return candidates[a].begin < candidates[b].begin;
        return a < b;
    });

    std::vector<Candidate> kept;
    kept.reserve(count);
    for (std::size_t i : by_priority) {
        const Candidate& candidate = candidates[i];
        // 起点早于本段终点的位置有几个：位置序已按起点升序，二分找第一个起点不早于本段终点的
        std::size_t lo = 0;
        std::size_t hi = count;
        while (lo < hi) {
            const std::size_t mid = lo + (hi - lo) / 2;
            if (starts[mid] < candidate.end) {
                lo = mid + 1;
            } else {
                hi = mid;
            }
        }
        if (max_end_before(lo) > candidate.begin) continue;  // 与已留段的某一段相交，让给已留的那段
        kept.push_back(candidate);
        keep(rank[i], candidate.end);
    }
    std::stable_sort(kept.begin(), kept.end(),
                     [](const Candidate& a, const Candidate& b) { return a.begin < b.begin; });
    return kept;
}

// 这一趟去重叠要不要跑：只有一条规则的命中会互相重叠时才有必要。
//
// 命中的取法是从上一次的结束处接着找（scan_document_scope 与 scan_paragraph_scope 里那个
// search 循环）：模式的 search 只从 from 起试起点、返回的 begin 不早于 from，拿到命中后
// 下一趟从 end 起（零长度命中往后挪一个字）。所以同一个模式的前后两处命中，后一处的起点
// 一定不早于前一处的终点，同一段字不可能被它命中两次而相交。前后文（环视）只影响一处命中
// 从哪里算起，不影响这条走法。
//
// 会相交的只有多条模式：两张词表可能盖住同一个词，一个词也可能被两条词表从不同位置咬住。
// 所以判断只看编译后模式的条数，与作用域、前后文无关。
bool needs_drop_overlaps(const CompiledRule& compiled) { return compiled.patterns().size() > 1; }

// ---------------------------------------------------------------- 快路：一趟扫完一批起点候选
//
// 老路是一个模式扫一遍全文：一条规则带四五十个词就是四五十趟。这条规则里的词是死的字面量
// （或者是「命中必然以某串字面量开头」的正则），把它们的候选串收成一张前缀索引
// （见 needle_scan.h），全文只走一趟，每个候选位置上再按各条模式自己的匹配器回验一次。
//
// 结果与逐条扫逐字节相同，因为三条不变的规矩都在：
//   * 每个模式各自推进：命中之后下一次的起点不早于这次命中的终点（next_at 一模式一格）；
//   * 候选按模式先后、同类按起点先后进候选表（快路先把命中按模式归堆，再按模式先后倒出来）；
//   * 同一个模式前后两处命中不重叠，靠的也是同一个推进位置。
// 唯一省略的一步是「候选起点上的回验」：整条模式就是一串字面量的那些，命中什么由候选本身定死。
struct FastHits {
    std::vector<std::vector<Candidate>> kept;  // 每个模式留下的位置（够报告与去重叠用）
    std::vector<std::size_t> total;            // 每个模式收下的处数（含没留位置的）
    std::vector<std::size_t> next_at;          // 每个模式下一次允许的起点
    // 回验用的匹配器，一个模式一个，用到才建：一段一段反复用，不每验一次建一个。
    std::vector<std::unique_ptr<PatternVerifier>> verifiers;

    // 按模式数备好格子；之后每段（段落作用域里是一段）只清空内容，不重新要内存。
    void prepare(std::size_t patterns) {
        if (kept.size() == patterns) return;
        kept.assign(patterns, {});
        total.assign(patterns, 0);
        next_at.assign(patterns, 0);
        verifiers.resize(patterns);
    }

    // 起新的一段：所有模式的推进位置从头来。
    void start(std::size_t from) {
        for (std::size_t i = 0; i < kept.size(); ++i) {
            kept[i].clear();
            total[i] = 0;
            next_at[i] = from;
        }
    }
};

// 扫一遍 text[from..)，把每个模式收下的命中按模式归堆。
// keep_all 为假时每个模式最多留 kMaxListedHitsPerRule 处（与候选表同一道封顶），
// 多出来的只记处数：报告里的总处数与「另有 N 处未列出」按真实处数算。
template <class Tick>
void scan_fast(const CompiledRule& compiled, std::string_view text, std::size_t from,
               bool keep_all, const MatchLimits& limits, FastHits& hits, Tick&& tick) {
    const std::vector<CompiledRule::FastEntry>& entries = compiled.fast_entries();
    const std::size_t cap = keep_all ? static_cast<std::size_t>(-1) : kMaxListedHitsPerRule;
    const char* data = text.data();
    std::size_t order = 0;
    // 换了一份稿件（段落作用域里是换了一段）：留着的那几个匹配器只改指向。
    for (std::unique_ptr<PatternVerifier>& verifier : hits.verifiers)
        if (verifier) verifier->rebind(text, limits);
    compiled.needle_scanner().scan(
        text, from,
        [&](std::size_t entry_index, std::size_t begin, std::size_t end) {
            const CompiledRule::FastEntry& entry = entries[entry_index];
            if (begin < hits.next_at[entry.pattern]) return;
            // 候选串的首字节永远不是续字节（候选串都是成形的 UTF-8 片段），
            // 所以这一道对正常稿件从不拦人；留着是给坏字节兜底。
            if (begin != 0 && (static_cast<unsigned char>(data[begin]) & 0xC0) == 0x80) return;
            Candidate candidate{begin, end, entry.hint, order++};
            if (!entry.whole) {
                std::unique_ptr<PatternVerifier>& verifier = hits.verifiers[entry.pattern];
                if (!verifier)
                    verifier = std::make_unique<PatternVerifier>(
                        compiled.pattern_at(entry.pattern), text, limits);
                MatchRange out;
                if (!verifier->match_at(begin, out)) return;
                candidate.begin = out.begin;
                candidate.end = out.end;
                if (candidate.end <= candidate.begin) return;
            }
            hits.next_at[entry.pattern] = candidate.end;
            ++hits.total[entry.pattern];
            std::vector<Candidate>& slot = hits.kept[entry.pattern];
            if (slot.size() < cap) slot.push_back(candidate);
            tick();
        },
        [&](std::size_t) { tick(); });
}

// 把一个模式收下的命中按原样交给候选表：先位置的，再只计数的那几处。
void replay_fast(const FastHits& hits, std::size_t pattern, CandidateList& candidates) {
    const std::vector<Candidate>& slot = hits.kept[pattern];
    for (const Candidate& candidate : slot) candidates.push(candidate);
    candidates.add_total(hits.total[pattern] - slot.size());
}

// ---------------------------------------------------------------- 规则筛选

const char* const kGenreIds[] = {"doc", "fiction", "agent"};

std::string lower(std::string_view text) {
    std::string out(text);
    for (char& c : out)
        if (c >= 'A' && c <= 'Z') c = static_cast<char>(c - 'A' + 'a');
    return out;
}

bool equals_ignore_case(std::string_view a, std::string_view b) { return lower(a) == lower(b); }

bool genre_allowed(const LintRule& rule, std::string_view genre) {
    if (rule.genres.empty() || genre.empty()) return true;
    for (const auto& item : rule.genres)
        if (equals_ignore_case(item, genre)) return true;
    return false;
}

bool is_named_in(const std::vector<std::string>& ids, std::string_view id) {
    for (const auto& item : ids)
        if (equals_ignore_case(item, id)) return true;
    return false;
}

bool is_named(const LintOptions& options, std::string_view id) {
    return is_named_in(options.only_rules, id);
}

bool named(const LintOptions& options, const LintRule& rule) { return is_named(options, rule.id); }

// 参与扫描的规则：档位过得去，且不是「不点名不跑」的那一类（点名了就照跑）。
// 点名的规则不在档位里时响亮报错：否则读者看到的是零命中，而真相是这条规则不在这个档。
std::vector<const LintRule*> active_rules(const std::vector<LintRule>& rules,
                                          const LintOptions& options) {
    if (!options.min_severity.empty() && !equals_ignore_case(options.min_severity, "info") &&
        !equals_ignore_case(options.min_severity, "warn"))
        throw std::runtime_error("min_severity 只接受 info 或 warn");

    if (!options.genre.empty()) {
        bool known = false;
        for (const char* id : kGenreIds)
            if (equals_ignore_case(id, options.genre)) known = true;
        if (!known) throw std::runtime_error("genre 只接受 doc、fiction、agent");
    }

    if (!options.only_rules.empty()) {
        for (const auto& id : options.only_rules) {
            bool found = false;
            for (const auto& rule : rules)
                if (equals_ignore_case(rule.id, id)) found = true;
            if (!found) throw std::runtime_error("未知规则 Id：" + id);
        }
        for (const auto& rule : rules) {
            if (rule.genres.empty() || genre_allowed(rule, options.genre) || !named(options, rule))
                continue;
            std::string applies;
            for (const auto& item : rule.genres) applies += (applies.empty() ? "" : "/") + item;
            throw std::runtime_error("规则 " + rule.id + " 不在 " + options.genre +
                                     " 档生效（适用档位：" + applies + "）");
        }
    }

    std::vector<const LintRule*> active;
    active.reserve(rules.size());
    for (const auto& rule : rules) {
        if (!genre_allowed(rule, options.genre)) continue;
        if (rule.default_off && !named(options, rule)) continue;
        active.push_back(&rule);
    }
    return active;
}

// ---------------------------------------------------------------- 算法命中

// 算法把命中说明放在自己组的交接口里（见 engine/scan.h）：写命中的回调按规则编号去取一次。
// 复读组与 rhythm 组的 choppy-clauses 各一个口子，shape 组的其余各条共用一个。
const std::string& pending_detail_of(const std::string& rule_id) {
    if (rule_id == "repeated-verbatim" || rule_id == "repeated-token-run")
        return repeat_pending_detail();
    if (rule_id == "choppy-clauses") return choppy_pending_detail();
    return shape_pending_detail();
}

// 展示片段的上限：只有复读组会给出（整串报一处时命中可以很长），其余各条按命中长度取。
bool is_repeat_rule(const std::string& rule_id) {
    return rule_id == "repeated-verbatim" || rule_id == "repeated-token-run";
}

// ---------------------------------------------------------------- 一趟扫描

// 登记这份正文的作用域：扫完（包括抛错停下）就把登记放掉，免得下一份稿子
// 用上这份的结论。
class DocumentScopeGuard {
public:
    explicit DocumentScopeGuard(std::string_view text) { begin_document(text); }
    ~DocumentScopeGuard() { end_document(); }
    DocumentScopeGuard(const DocumentScopeGuard&) = delete;
    DocumentScopeGuard& operator=(const DocumentScopeGuard&) = delete;
};

// 一份稿子上只能建一次的那几样：行索引、博文遮罩与空格副本、分段方式、正文字数。
//
// 规则分给几条线程扫（见 Scan::run），各条线程只借这一份、谁也不写它。
// 为什么不能每条线程各建一份：一份十五兆的博文稿，光是空格副本就是十六兆，
// 几条线程各来一份，四十兆的内存线立刻就穿了。
//
// 段落切分也放在这里（用到才切）：段落作用域的数据规则与算法规则共用同一份切法，
// 切一次就够。切的时间在派活之前（见 Scan::run），切完之后这一份就只有读的。
//
// 这一份只在构造它的地方就地存在（std::make_shared），不拷不搬：
// scan_text 指向自己那一份 masked_text，搬一次就悬空了。
struct ScanPrep {
    explicit ScanPrep(std::string_view text)
        : text(text),
          index(text),
          mask(BlogMask::create(text)),
          // 不是博文时一个字都不遮，规则读的就是原文：行内代码里的文件名对文档与回复是有用的证据
          masked_text(mask.is_blog_post() ? mask.apply(text) : std::string()),
          scan_text(mask.is_blog_post() ? std::string_view(masked_text) : text),
          units(index.text_units()),
          markdown(is_markdown_mode(text)),
          blog_post(mask.is_blog_post()) {
        body_units = texttool::body_units(text, mask);
    }

    std::string_view text;      // 原文
    LineIndex index;            // 原文的行索引：行列与长度都从这里换算
    BlogMask mask;              // 博文结构遮罩（判完就只剩这一份判定结果）
    std::string masked_text;    // 空格副本全文：只有博文稿才有内容，其余为空串
    std::string_view scan_text;  // 规则与统计读的那一份；不是博文时就是原文
    std::size_t units = 0;       // 原文全长，按 UTF-16 码元
    std::size_t body_units = 0;  // 正文字数：原文全长减去博文结构
    bool markdown = false;       // 分段方式是不是 markdown
    bool blog_post = false;      // 这份稿子算不算博文
    std::optional<std::vector<Paragraph>> paragraphs;  // 用到才切，切在派活之前
};

// 这一趟要跑的一条规则。
struct RuleJob {
    const LintRule* rule = nullptr;
    std::size_t index = 0;  // 规则表下标：命中只带下标，排版时按它回查规则表
    // 共用的编译结果。只有这一份稿子时为空，扫到它的那条线程自己编、扫完就放掉。
    const CompiledRule* compiled = nullptr;
};

// 一条规则跑完的落点。
//
// 多线程扫的时候每条规则先写进自己这一份，合并那一趟（见 Scan::run）再按规则表顺序收进
// LintOutcome：命中的先后（LintHit::order）与复读串剔命中都按那个顺序定，所以各条线程
// 各写各的格子、谁也不碰别人的一份，收拢之前不必加锁。
//
// 命中表收进结果之后就放掉（见 Scan::merge_rule）：带 --baseline 的一趟要留全命中，
// 两份同时在内存里会顶破内存线。
struct RuleResult {
    std::vector<LintHit> hits;
    std::vector<LintNote> notes;
    std::size_t rule_hits = 0;  // 这条规则的真实处数：封顶之后它仍是全的
    std::size_t patterns = 0;   // 几个模式，只给分段计时那一行看
    double compile_seconds = 0;
    double data_seconds = 0;
    // 这条规则抛出来的错（单条规则超时、按净增算的密度门限没有基线）：
    // 合并到它的时候在原来的次序上重新抛（见 Scan::merge_rule）。
    std::exception_ptr error;
};

// 一次检查开几条线程。
//
// 规则之间互不相干，各扫各的，核心多就多开几条。上限取 8：十五兆的稿子上 8 条线程
// 已经把数据规则那一大块压到原来的几分之一，再往上加收益只剩零头，而命中表与附加串的
// 预留一路涨。
// 短稿、只点了一两条规则的运行仍走一条：开一条线程要几十微秒，比扫那份稿子还贵。
constexpr unsigned kMaxScanThreads = 8;
constexpr std::size_t kParallelMinBytes = 128 * 1024;

// 这一趟真能用几个核。
//
// 不直接问 hardware_concurrency：它说的是这台机器有几个核，不是本进程准用几个。
// 被绑到两个核上（taskset、容器配额、调度器限流）却开八条线程时，每条线程分到的时间
// 只有八分之一，单条规则的时限（默认 2 秒，按墙钟算）就会被自身抢核顶爆——最慢的那条
// 按三百多毫秒算，八条挤一个核就是两秒往上。所以先问亲和性：
// Linux 读 sched_getaffinity（taskset -c 8 这种就只剩一个核），
// 其余平台退回 hardware_concurrency（Windows 上不读进程亲和性，那里很少有人手动绑核）。
unsigned available_cores() {
#if defined(__linux__)
    cpu_set_t allowed;
    CPU_ZERO(&allowed);
    if (sched_getaffinity(0, sizeof(allowed), &allowed) == 0) {
        const int count = CPU_COUNT(&allowed);
        if (count > 0) return static_cast<unsigned>(count);
    }
#endif
    const unsigned fallback = std::thread::hardware_concurrency();
    return fallback == 0 ? 2 : fallback;
}

unsigned scan_threads(std::size_t text_bytes, std::size_t data_jobs) {
    if (text_bytes < kParallelMinBytes || data_jobs < 2) return 1;
    unsigned count = std::min<unsigned>(available_cores(), kMaxScanThreads);
    count = static_cast<unsigned>(std::min<std::size_t>(count, data_jobs));
    return count == 0 ? 1 : count;
}

class Scan {
public:
    // 这一份稿子的第一条：共享准备在这里建（见 ScanPrep）。
    Scan(std::string_view text, const LintOptions& options)
        : Scan(std::make_shared<ScanPrep>(text), options) {}

    // 分派出去的那几条线程走这一个：只借准备，不另建一份。
    Scan(std::shared_ptr<ScanPrep> prep, const LintOptions& options)
        : prep_(std::move(prep)),
          text_(prep_->text),
          options_(options),
          index_(prep_->index),
          masked_text_(prep_->masked_text),
          scan_text_(prep_->scan_text),
          units_(prep_->units),
          body_units_(prep_->body_units),
          budget_(options.rule_time_budget),
          markdown_(prep_->markdown),
          // 博文稿只有显式指定了档位、也没有点名规则时才吃那几条标了 SkipInBlog 的判据
          blog_default_(prep_->blog_post && options.genre.empty() && options.only_rules.empty()),
          // 扫描时就封顶。带 --baseline 的一趟不行：得先留全命中才算得出净增位置（见 cap_listed_hits）；
          // 要写命中日志的一趟也不行：日志按全部命中写（见 LintOptions::keep_all_hits）。
          cap_now_(options.role == LintScanRole::Standalone && !options.keep_all_hits),
          paragraphs_(prep_->paragraphs) {}

    // compiled 为空表示这一份稿子只有自己：每条规则编译完、扫完就放掉，不把编译结果留到下一份。
    // 一份稿子的编译结果要占两三兆，只有一份时没有第二份来摊，留着只是白占内存。
    LintOutcome run(const std::vector<LintRule>& rules, const CompiledRules* compiled_rules) {
        // 规则在空格副本上扫，所以登记给匹配层的也是那一份：按「同一份稿件」判的
        // 重复查询结论才用得对。
        DocumentScopeGuard guard(scan_text_);
        const Clock::time_point run_started = Clock::now();
        outcome_.units = body_units_;
        outcome_.paragraph_mode = markdown_ ? "markdown" : "line";
        outcome_.rules = &rules;
        outcome_.text = text_;
        outcome_.rule_hits.assign(rules.size(), 0);
        // 命中表的预留行数按稿件长度估，封顶之后还要再收一道底：一条规则最多列那么多处。
        std::size_t hit_slots =
            std::clamp<std::size_t>(text_.size() / kHitBytesPerHit, kHitGrow, kHitReserveLimit);
        if (cap_now_) hit_slots = std::min(hit_slots, rules.size() * kMaxListedHitsPerRule);
        outcome_.hits.reserve(hit_slots);

        // 先过跳过的四道门（Markdown、博文、DefaultOff 与 Genres，后两道在 active_rules 里过），
        // 把这一趟要跑的规则排成一张表：数据规则分给几条线程，算法规则留到合并那一趟按次序跑。
        std::vector<RuleJob> jobs;
        jobs.reserve(rules.size());
        std::size_t data_jobs = 0;
        bool needs_paragraphs = false;
        for (const LintRule* rule : active_rules(rules, options_)) {
            if (rule->skip_in_markdown && markdown_) continue;
            if (rule->skip_in_blog && blog_default_) continue;
            RuleJob job;
            job.rule = rule;
            job.index = static_cast<std::size_t>(rule - rules.data());
            if (rule->kind != RuleKind::Algorithm) {
                if (rule->scope == "paragraph") needs_paragraphs = true;
                // 同一条规则在多份稿子上只编译一次：这一批有共用的 CompiledRules 就在这里取。
                // 编译表只许一个线程碰，所以这一趟要用到的规则全在派活之前编译完。
                // 只有这一份稿子时（compiled_rules 为空）交给扫到它的那条线程编译、扫完就放掉：
                // 一份稿子的编译结果占两三兆，没有第二份来摊，留着只是白占内存。
                if (compiled_rules != nullptr) {
                    const Clock::time_point compile_started = Clock::now();
                    job.compiled = compiled_rules->compiled_at(job.index);
                    compile_seconds_ += seconds_since(compile_started);
                    if (job.compiled == nullptr) continue;  // 规则表下标对不上，不该发生
                }
                ++data_jobs;
            }
            jobs.push_back(job);
        }
        std::vector<RuleResult> results(jobs.size());

        // 段落切分只做一次，切在各线程开工之前：段落作用域的数据规则与算法规则共用这一份，
        // 开工之后再切就有两条线程同时写同一份。
        if (needs_paragraphs) cut_paragraphs();

        // 数据规则那一趟：按表里的先后排队，谁空谁取下一条。规则的长短差得远（最慢的一条
        // 三百多毫秒，快的不到一毫秒），按份数硬分给几条线程，会有人先扫完干等着。
        const unsigned threads = scan_threads(scan_text_.size(), data_jobs);
        std::atomic<std::size_t> next_job{0};
        auto drain = [&](bool on_main_thread) {
            // 每条线程各自跟匹配层登记这份正文：重复查询的结论按线程留（见 regex.h 的
            // begin_document）。主线程在 run() 开头已经登记过，不必再登记一次。
            std::optional<DocumentScopeGuard> scope;
            if (!on_main_thread) scope.emplace(scan_text_);
            Scan worker(prep_, options_);
            // 命中只带规则下标，回查规则表用（见 LintHit）；接口留着，万一哪天回调要读
            worker.outcome_.rules = &rules;
            worker.outcome_.text = text_;
            // 真实处数按规则表下标存，与主 Scan 那一份同一套（见 push_hit）
            worker.outcome_.rule_hits.assign(rules.size(), 0);
            while (true) {
                const std::size_t at = next_job.fetch_add(1, std::memory_order_relaxed);
                if (at >= jobs.size()) return;
                const RuleJob& job = jobs[at];
                // 算法规则留到合并那一趟：它们按次序替别的规则剔命中（见下面的合并那一段）
                if (job.rule->kind == RuleKind::Algorithm) continue;
                try {
                    worker.scan_data_rule(job, results[at]);
                } catch (...) {
                    // 这一条抛出来的错（超时、按净增算的密度门限没有基线）：别的线程照旧
                    // 往下扫，合并到这一条的时候再在原来的次序上重新抛。
                    results[at].error = std::current_exception();
                }
            }
        };
        if (threads > 1) {
            std::vector<std::thread> pool;
            pool.reserve(threads - 1);
            for (unsigned i = 1; i < threads; ++i) pool.emplace_back(drain, false);
            drain(true);
            for (std::thread& worker : pool) worker.join();
        } else {
            drain(true);
        }

        // 合并：按规则表顺序把各条规则的命中收进结果，算法规则就在这一步按次序跑。
        // 两条越过规则的动作用（复读串替别的规则剔命中、篇幅节奏先于短句连出）都落在这一步：
        // 它们按「表里排在它前面的规则已经出完」发生，与逐条扫描时的先后一样。
        for (std::size_t at = 0; at < jobs.size(); ++at) {
            const RuleJob& job = jobs[at];
            RuleResult& result = results[at];
            if (job.rule->kind == RuleKind::Algorithm) {
                const Clock::time_point algo_started = Clock::now();
                try {
                    scan_algorithm_scope(job.index, *job.rule);
                } catch (...) {
                    result.error = std::current_exception();
                }
                const double seconds = seconds_since(algo_started);
                algo_seconds_ += seconds;
                if (std::getenv("TEXTTOOL_TIMING_RULES") != nullptr)
                    std::fprintf(stderr, "[算法] %-34s %8.2f ms\n", job.rule->id.c_str(),
                                 seconds * 1000.0);
            } else {
                compile_seconds_ += result.compile_seconds;
                data_seconds_ += result.data_seconds;
                if (std::getenv("TEXTTOOL_TIMING_RULES") != nullptr)
                    std::fprintf(stderr, "[规则] %-34s %8.2f ms  %zu 模式\n", job.rule->id.c_str(),
                                 result.data_seconds * 1000.0, result.patterns);
            }
            merge_rule(result, job.index);  // 出错的那一条在这里按次序重新抛
        }
        // 点名的规则才进报告：这一刀切在结果上，不动「哪些规则跑」
        // （标了 DefaultOff 的规则点名前并不跑，但档位通用、没标 DefaultOff 的规则会跑完再裁）。
        // 命中的与附注同一刀：附注没有 severity，但它同样属于某条规则，未点名的不得漏进统计段。
        //
        // 切在结果上而不是跑之前，是因为规则之间有动作要传：复读串替其他规则剔命中，
        // 篇幅节奏报过之后短句连出不再报。未点名的规则先不跑，这两个动作就不发生，
        // 报告与「不写 --only」时看到的那份不一致。
        if (!options_.only_rules.empty() && options_.cut_to_named) {
            outcome_.hits.erase(std::remove_if(outcome_.hits.begin(), outcome_.hits.end(),
                                               [&](const LintHit& hit) {
                                                   return !is_named(options_, rules_of(hit).id);
                                               }),
                                outcome_.hits.end());
            outcome_.notes.erase(std::remove_if(outcome_.notes.begin(), outcome_.notes.end(),
                                                [&](const LintNote& note) {
                                                    return !is_named(options_, note.rule_id);
                                                }),
                                 outcome_.notes.end());
            // 没点名的规则在这里一条不剩：真实处数也得跟着归零，否则报告会给一条
            // 列表里一处也没有的规则写「另有 N 处未列出」。
            for (std::size_t i = 0; i < rules.size(); ++i)
                if (!is_named(options_, rules[i].id)) outcome_.rule_hits[i] = 0;
        }
        // 命中按起点排一次：同一规则的多条模式各扫一遍会让位置来回跳。
        // (行, 列) 是起点的单调函数，按起点排与按行列排是同一个次序。
        // 起点相同的按出现先后排（order），整张表因此是全序：用 std::sort 就够，
        // 它不留临时缓冲；stable_sort 要多占半张表，十几万处就是几兆。
        const Clock::time_point sort_started = Clock::now();
        std::sort(outcome_.hits.begin(), outcome_.hits.end(),
                  [](const LintHit& a, const LintHit& b) {
                      if (a.begin != b.begin) return a.begin < b.begin;
                      return a.order < b.order;
                  });

        materialize_extras();

        if (timing_on())
            std::fprintf(stderr, "[计时] %-18s %9u 条\n", "数据规则线程", threads);
        note_seconds("建行索引与段落", prep_seconds_ + slices_seconds_);
        note_seconds("规则编译", compile_seconds_);
        // 开了几条线程时这一项是「各条线程各扫各的」加起来的时间，会比「引擎合计」大：
        // 几条线程是同时跑的，逐条的耗时依然是各条自己的墙钟（看 TEXTTOOL_TIMING_RULES 那一行）。
        note_seconds("数据规则扫描", data_seconds_);
        note_seconds("算法规则", algo_seconds_);
        note_seconds("命中排序", seconds_since(sort_started));
        note_seconds("引擎合计", seconds_since(run_started));
        return std::move(outcome_);
    }

    // 构造函数里那一份（行索引、字数、分段方式、博文探测）由 run_lint 量：
    // 构造函数体写在初始化列表后面，量不到自己。
    void add_prep_seconds(double seconds) { prep_seconds_ += seconds; }

private:
    // 扫一条数据规则，结果摘到这一条自己的格子里（见 RuleResult）。
    //
    // 编译结果在这里就地建、扫完就放掉：只有这一份稿子时没有第二份来摊那两三兆。
    // 提取结果放在最后一步：提取之后这一份工作区就是空的，下一条规则从零开始，
    // 各条之间不会串。
    void scan_data_rule(const RuleJob& job, RuleResult& result) {
        const LintRule& rule = *job.rule;
        std::optional<CompiledRule> own;
        const CompiledRule* compiled = job.compiled;
        if (compiled == nullptr) {
            const Clock::time_point compile_started = Clock::now();
            own.emplace(rule);
            result.compile_seconds = seconds_since(compile_started);
            compiled = &*own;
        }
        start_rule(job.index, rule);
        const Clock::time_point scan_started = Clock::now();
        if (rule.scope == "paragraph") {
            scan_paragraph_scope(*compiled);
        } else {
            scan_document_scope(*compiled);
        }
        result.data_seconds = seconds_since(scan_started);
        result.patterns = compiled->patterns().size();
        take_rule_result(rule, job.index, result);
    }

    // 这一条规则的结果从运行状态里摘出来：命中、处数、附注搬到它自己的格子里，
    // 命中表就地换回一张空表（不把原来那一张留在工作区占着）。
    void take_rule_result(const LintRule& rule, std::size_t rule_index, RuleResult& result) {
        result.rule_hits = outcome_.rule_hits[rule_index];
        outcome_.rule_hits[rule_index] = 0;
        result.hits = std::move(outcome_.hits);
        std::vector<LintHit>().swap(outcome_.hits);
        result.notes = std::move(outcome_.notes);
        std::vector<LintNote>().swap(outcome_.notes);
        // 早期表与附加串只有算法规则会建（见 store_early_detail），而算法规则跑在总表上、
        // 在合并那一趟（scan_algorithm_scope），不经过这里。真有人日后给数据规则也建了，
        // 这里必须响亮报错：静默丢掉的话，排版读到的说明是空的，报告就少了一整段。
        if (!early_kind_.empty() || !early_text_.empty())
            throw std::logic_error("数据规则不该有附加串：" + rule.id);
    }

    // 一条规则的结果收进总表：命中的次序（LintHit::order）在这里统一编，
    // 真实处数与附注一起搬过去；出错的那一条就在这里按原来的次序重新抛。
    //
    // 次序为什么这样编就与逐条扫描时一样：原先全表共用一个计数器，按规则表顺序逐条扫、
    // 每条规则内按出现先后报，那个计数器就是「规则表顺序 × 规则内先后」的单调函数；
    // 这里按同一个顺序逐条收，编出来的先后关系与之等价。
    // 排序键是 (起点, 次序)，同起点的几处分属哪些规则、各自第几处，两边一致。
    void merge_rule(RuleResult& result, std::size_t rule_index) {
        if (result.error) std::rethrow_exception(result.error);
        outcome_.rule_hits[rule_index] += result.rule_hits;
        if (result.hits.size() > outcome_.hits.capacity() - outcome_.hits.size()) {
            // 预估的格子不够了：一次扩到位，别让每处都去要一次（与 push_hit 同一套）
            const std::size_t want =
                std::max(outcome_.hits.capacity() * 2 + kHitGrow,
                         outcome_.hits.size() + result.hits.size());
            outcome_.hits.reserve(want);
        }
        for (const LintHit& hit : result.hits) {
            LintHit merged = hit;
            merged.order = next_order_++;
            outcome_.hits.push_back(merged);
        }
        // 命中与附注都搬过去了，这一条的格子就地放掉：带 --baseline 的一趟要留全命中，
        // 两张表同时在内存里会顶破内存线。
        std::vector<LintHit>().swap(result.hits);
        for (LintNote& note : result.notes) outcome_.notes.push_back(std::move(note));
        std::vector<LintNote>().swap(result.notes);
    }

    // 算法规则：与数据规则同一趟、同一个顺序。切好的段、全文字数随上下文交过去，
    // 写回的命中与附注由这一层收下，与数据规则的一起排序。
    //
    // 算法在空格副本上找（结构区域是空格，匹配不上任何词），报出来的命中原文
    // 由算法从 context.original 上切，读者看到的仍是自己写的那些字。
    void scan_algorithm_scope(std::size_t rule_index, const LintRule& rule) {
        start_rule(rule_index, rule);

        AlgoContext context;
        context.text = scan_text_;
        context.units = units_;
        context.original = text_;
        context.paragraphs = &algo_paragraphs();
        // 行索引接一份过去：这条规则要算行列时不必按原文再建一份（见 AlgoContext::locations）。
        context.locations = &index_;

        std::vector<std::string> notes;
        run_algorithm(rule, context, &Scan::write_algorithm_hit, this, notes);
        for (std::string& note : notes) outcome_.notes.push_back({rule.id, std::move(note)});

        // 复读串已经整串报过一处：串里其他规则的命中不再各报一遍
        const std::string algorithm = rule.algorithm_name.value_or(std::string());
        if (algorithm == "repeated-token-run")
            drop_hits_inside_token_runs(static_cast<std::uint16_t>(rule_index));
    }

    // 算法写命中的口子：算法只给字节区间与命中原文，其余字段照旧从规则表与位置表取。
    // 区间定位置、行号、长度，命中原文是报给读者的那几个字，两者不必等长（复读串就是整串报一处）。
    static void write_algorithm_hit(std::size_t begin, std::size_t end, const std::string& excerpt,
                                    void* context) {
        static_cast<Scan*>(context)->report_algorithm(begin, end, excerpt);
    }

    void report_algorithm(std::size_t begin, std::size_t end, const std::string& excerpt) {
        const LintRule& rule = *current_rule_;
        // 交接口每一条都取一次：下一条命中不会捡到这一条留下的位置。
        // 被严重程度门挡掉的命中也要取，不然那个位置会留到后面别的规则的命中上。
        const DetailPosition position = take_pending_detail_position();
        // 与数据规则同一道严重程度门：min_severity=warn 时只留警告级
        if (!equals_ignore_case(options_.min_severity, "info") && rule.severity != "warn") return;
        // 附加串只给进得了命中表的那几处留：超出一条规则上限的命中只计数、不入表（见 push_hit），
        // 没人读它的说明。留下的这一格记的是这一处自己的下标，命中只读自己那一格；
        // 下标按 32 位放，同一份稿子里的每一处都放得下，不会绕回到别人那一格。
        LintHit hit;
        if (!cap_now_ || outcome_.rule_hits[current_rule_index_] < kMaxListedHitsPerRule) {
            const std::uint32_t slot = static_cast<std::uint32_t>(early_kind_.size());
            if (store_early_detail(begin, end, excerpt, position)) hit.extra = slot;
        }
        const Location location = index_.locate(begin, end);
        hit.rule = static_cast<std::uint16_t>(current_rule_index_);
        hit.begin = begin;
        hit.end = end;
        hit.line = static_cast<std::uint32_t>(location.line);
        hit.column = static_cast<std::uint32_t>(location.column);
        hit.length = static_cast<std::uint32_t>(location.length);
        hit.order = next_order_++;
        hit.snippet_length =
            is_repeat_rule(rule.id) ? static_cast<std::uint16_t>(repeat_pending_snippet_chars()) : 0;
        push_hit(hit);
    }

    // 给这一处留一格附加串，留下就返回真（格号是 early_kind_ 的下标，一格只属于一条命中）。
    //
    // 说明的两种写法分开存：
    //   position —— 说明是「规则表 Detail 填两个数」，那两个数是原文某一处的行列，
    //               存那一处的下标（四字节），排版时现算行列、现拼说明；
    //   text     —— 说明是现成一句，或者命中原文不是原文区间那一段，整份描述子存在这里。
    // 复读类规则的说明都是第一种：一处整句三十多字节，八万处就是两兆半，存下标只要一颗零头。
    //
    // 两样都留不下时不留格（返回假）：命中原文按区间从稿子里切，说明回规则表取。
    bool store_early_detail(std::size_t begin, std::size_t end, const std::string& excerpt,
                            DetailPosition position) {
        const LintRule& rule = *current_rule_;
        const std::size_t slot = early_kind_.size();
        // 命中原文常常就是命中区间里那段字节（整段、整句、整串重复报一处）：
        // 这一段不抄第二遍，排版时按区间从稿子里切；抄的话光这一项就是几十兆。
        const bool has_excerpt = excerpt != text_.substr(begin, end - begin);
        if (!has_excerpt && position.present) {
            note_early_rule(slot);
            early_kind_.push_back(static_cast<std::uint8_t>(EarlyKind::position));
            early_value_.push_back(static_cast<std::uint32_t>(position.at));
            return true;
        }
        const std::string_view detail = pending_detail_of(rule.id);
        if (!has_excerpt && detail.empty()) return false;
        LintHitExtra extra;
        if (has_excerpt) extra.match_offset = add_arena_string(excerpt, extra.match_length);
        extra.detail_offset = add_arena_string(detail, extra.detail_length);
        note_early_rule(slot);
        early_kind_.push_back(static_cast<std::uint8_t>(EarlyKind::text));
        early_value_.push_back(0);
        early_text_.push_back({static_cast<std::uint32_t>(slot), extra});
        return true;
    }

    // 这一格属于哪条规则：说明的模板跟着格子走。只在真留下一格时记，格号因此严格递增。
    void note_early_rule(std::size_t slot) {
        if (!early_rule_starts_.empty() &&
            early_rule_starts_.back().second == current_rule_index_)
            return;
        early_rule_starts_.push_back(
            {static_cast<std::uint32_t>(slot), static_cast<std::uint16_t>(current_rule_index_)});
    }

    // 这一格属于哪条规则：早先按规则记下的起点表里找最后一个不大于它的。
    std::size_t rule_of_slot(std::size_t slot) const {
        const auto found = std::upper_bound(
            early_rule_starts_.begin(), early_rule_starts_.end(), slot,
            [](std::size_t want, const std::pair<std::uint32_t, std::uint16_t>& row) {
                return want < row.first;
            });
        return found == early_rule_starts_.begin() ? 0 : (found - 1)->second;
    }

    // 命中表定下来之后，把留下那几条命中的附加串按格取一遍，建成紧凑的一份。
    // 命中里的下标就是它自己那一格，一处只会取到自己那条说明。
    //
    // 只给留在表里的命中建：没列出去的命中不排版，建了也没人读。
    void materialize_extras() {
        if (early_kind_.empty()) return;
        std::sort(early_text_.begin(), early_text_.end(),
                  [](const std::pair<std::uint32_t, LintHitExtra>& a,
                     const std::pair<std::uint32_t, LintHitExtra>& b) { return a.first < b.first; });
        // 格号换成 extras 里的位置。格号是这一处自己的下标，一格只属于一条命中，
        // 所以不需要按下标去重：两处命中读到同一格这种事不再发生。
        const std::vector<LintRule>& rules = *outcome_.rules;
        for (LintHit& hit : outcome_.hits) {
            if (hit.rule >= rules.size() || rules[hit.rule].kind != RuleKind::Algorithm) continue;
            if (hit.extra == kNoExtra || hit.extra >= early_kind_.size()) {
                hit.extra = kNoExtra;
                continue;
            }
            if (!append_early_extra(hit.extra)) {
                hit.extra = kNoExtra;
                continue;
            }
            hit.extra = static_cast<std::uint32_t>(outcome_.extras.size() - 1);
        }
    }

    // 把一格变成一条附加串排在表尾；这一格里什么都没有就返回假。
    bool append_early_extra(std::size_t slot) {
        const auto kind = static_cast<EarlyKind>(early_kind_[slot]);
        if (kind == EarlyKind::position) {
            const std::size_t at = early_value_[slot];
            if (at > text_.size()) return false;
            const Location where = index_.locate(at, at);
            const LintRule& rule = (*outcome_.rules)[rule_of_slot(slot)];
            const std::string detail = fill_detail_args(
                rule.detail, {std::to_string(where.line), std::to_string(where.column)});
            LintHitExtra extra;
            extra.detail_offset = add_arena_string(detail, extra.detail_length);
            outcome_.extras.push_back(extra);
            return true;
        }
        if (kind == EarlyKind::text) {
            const auto found = std::lower_bound(
                early_text_.begin(), early_text_.end(), slot,
                [](const std::pair<std::uint32_t, LintHitExtra>& row, std::size_t want) {
                    return row.first < want;
                });
            if (found == early_text_.end() || found->first != slot) return false;
            outcome_.extras.push_back(found->second);
            return true;
        }
        return false;
    }

    // 落在复读串里（起点在串内）的其他规则的命中别再各报一遍：它跟串是同一件事。
    // 比的是本条规则自己的下标，不是写死的字符串。
    void drop_hits_inside_token_runs(std::uint16_t rule_index) {
        const std::vector<Span> runs = repeat_token_run_spans();
        if (runs.empty()) return;
        const auto inside_run = [&](const LintHit& hit) {
            if (hit.rule == rule_index) return false;
            for (const Span& run : runs)
                if (hit.begin >= run.begin && hit.begin < run.end) return true;
            return false;
        };
        // 剔掉的命中要从真实处数里减掉。已经因为封顶没攒下来的那几处减不到：
        // 它们是同一条规则里落在复读串内的命中，复读串是段内四遍以上的短单元，量很小。
        for (const LintHit& hit : outcome_.hits)
            if (inside_run(hit)) --outcome_.rule_hits[hit.rule];
        outcome_.hits.erase(std::remove_if(outcome_.hits.begin(), outcome_.hits.end(), inside_run),
                           outcome_.hits.end());
    }

    // 算法要的段落：与段落作用域的数据规则共用同一份切法，段界不会两处各说各的。
    // 用到才切，切完就留着：十五条算法规则共用这一份。
    const std::vector<Span>& algo_paragraphs() {
        if (!paragraphs_) cut_paragraphs();
        return *paragraphs_;
    }

    // 段落切分只做一次：段落作用域的数据规则与算法规则共用这一份。
    // 切的是空格副本：被遮的整行成了空白行，段落不会跳进代码块里。
    void cut_paragraphs() {
        const Clock::time_point started = Clock::now();
        paragraphs_ = split_paragraphs(scan_text_, markdown_);
        slices_seconds_ += seconds_since(started);
    }

    void scan_document_scope(const CompiledRule& compiled) {
        const bool drop_overlaps_needed = needs_drop_overlaps(compiled);
        // 封顶只在单跑的这趟成立：报告一条规则最多列 kMaxListedHitsPerRule 处。
        // 会去重叠的规则照旧全攒（去重叠之后后面的候选可能顶上来）。
        const bool keep_all = !cap_now_ || drop_overlaps_needed;
        CandidateList candidates(keep_all);
        // 快路：起点候选能证明的那些模式合成一趟全文扫描（见 scan_fast）。
        // 其余模式仍逐个扫全文，两种候选按同一个模式先后进表。
        FastHits fast;
        if (compiled.has_fast()) {
            fast.prepare(compiled.patterns().size());
            fast.start(0);
            scan_fast(compiled, scan_text_, 0, keep_all, limits_, fast, [this] { tick(); });
        }
        std::size_t order = 0;
        for (std::size_t index = 0; index < compiled.patterns().size(); ++index) {
            const Pattern& pattern = compiled.patterns()[index].first;
            const std::size_t hint = compiled.patterns()[index].second;
            // 先收再摸：同一段文本只扫一遍。这条模式凑不够次数就把它那几条从末尾撤掉，
            // 顺序仍是模式的先后，与逐条收到齐再判一致。
            candidates.begin_pattern();
            if (compiled.has_fast() && compiled.fast_pattern(index)) {
                replay_fast(fast, index, candidates);
            } else {
                std::size_t at = 0;
                while (true) {
                    const MatchRange match = pattern.search(scan_text_, at, limits_);
                    if (match.begin == kNoMatch) break;
                    candidates.push({match.begin, match.end, hint, order++});
                    tick();
                    if (match.end > match.begin) {
                        at = match.end;
                    } else {
                        if (match.begin >= scan_text_.size()) break;
                        at = next_char(scan_text_, match.begin);
                    }
                }
            }
            if (!candidates.pattern_reached(
                    static_cast<std::size_t>(compiled.rule().min_count)))
                candidates.drop_pattern();
            check_deadline();
        }

        // 判据与密度看真实处数：没留位置的那些也算一处。
        const std::size_t hit_count = candidates.total();
        const std::size_t unlisted = candidates.unlisted();
        // 单模式规则跳过去重叠：命中天然互不相交（见 needs_drop_overlaps），
        // 候选表不必再建五个与它同长的下标数组。
        std::vector<Candidate> kept =
            drop_overlaps_needed ? drop_overlaps(candidates.take()) : candidates.take();
        if (!meets_density(compiled.rule(), hit_count)) return;
        for (const Candidate& candidate : kept)
            report(compiled, candidate.begin, candidate.end, candidate.hint);
        // 没留位置的那几处只记处数：报告里的总处数与「另有 N 处未列出」按真实处数算，
        // 与列不列得下无关。截断只发生在封顶的这趟，全攒的那两趟这里恒为 0。
        count_unlisted(compiled.rule(), unlisted);
    }

    void scan_paragraph_scope(const CompiledRule& compiled) {
        if (!paragraphs_) cut_paragraphs();
        const LintRule& rule = compiled.rule();
        const bool drop_overlaps_needed = needs_drop_overlaps(compiled);
        FastHits fast;
        if (compiled.has_fast()) fast.prepare(compiled.patterns().size());
        for (const Paragraph& paragraph : *paragraphs_) {
            const std::string_view body =
                scan_text_.substr(paragraph.begin, paragraph.end - paragraph.begin);
            // 窗口只与段有关，与模式无关：提到模式循环外。
            // 窗口位置按原文的码元算：空格副本里被遮的多字节字成了好几个空格，
            // 拿它数码元会把段算长，窗口就跟着移错。
            std::size_t window = 0;
            if (rule.tail_chars) {
                const std::size_t tail = static_cast<std::size_t>(*rule.tail_chars);
                const MeasuredText whole(text_);
                const std::size_t units = measured_units(whole, paragraph.begin, paragraph.end);
                if (units > tail)
                    window =
                        byte_after_units(whole, paragraph.begin, paragraph.end, units - tail) -
                        paragraph.begin;
            }

            const bool keep_all = !cap_now_ || drop_overlaps_needed;
            CandidateList candidates(keep_all);
            // 快路：本段的候选起点一趟扫完（窗口是段内偏移，与老路的起点对齐）。
            if (compiled.has_fast()) {
                fast.start(window);
                scan_fast(compiled, body, window, keep_all, limits_, fast, [this] { tick(); });
            }
            std::size_t order = 0;
            for (std::size_t index = 0; index < compiled.patterns().size(); ++index) {
                const Pattern& pattern = compiled.patterns()[index].first;
                const std::size_t hint = compiled.patterns()[index].second;
                candidates.begin_pattern();
                if (compiled.has_fast() && compiled.fast_pattern(index)) {
                    replay_fast(fast, index, candidates);
                } else {
                    std::size_t at = window;
                    while (true) {
                        const MatchRange match = pattern.search(body, at, limits_);
                        if (match.begin == kNoMatch) break;
                        candidates.push({match.begin, match.end, hint, order++});
                        tick();
                        if (match.end > match.begin) {
                            at = match.end;
                        } else {
                            if (match.begin >= body.size()) break;
                            at = next_char(body, match.begin);
                        }
                    }
                }
                if (!candidates.pattern_reached(static_cast<std::size_t>(rule.min_count)))
                    candidates.drop_pattern();
                check_deadline();
            }

            const std::size_t unlisted = candidates.unlisted();
            std::vector<Candidate> kept =
                drop_overlaps_needed ? drop_overlaps(candidates.take()) : candidates.take();
            for (const Candidate& candidate : kept)
                report(compiled, paragraph.begin + candidate.begin,
                       paragraph.begin + candidate.end, candidate.hint);
            count_unlisted(rule, unlisted);
        }
    }

    // 密度门限：词表规则按千字处数判，不按绝对次数。算的是去重后的处数：
    // 同一处被两条模式各咬一口不是两处证据。分母是正文字数，按码元算，博文结构不计。
    bool meets_density(const LintRule& rule, std::size_t hits) const {
        std::string basis = "document";
        double minimum = 0;
        bool present = false;
        if (rule.density) {
            basis = rule.density->basis;
            minimum = rule.density->min;
            present = true;
        } else if (rule.min_density_per_kchar) {
            minimum = static_cast<double>(*rule.min_density_per_kchar);
            present = true;
        }
        if (!present) return true;

        if (basis == "document") {
            const double units = static_cast<double>(std::max<std::size_t>(body_units_, 1));
            return static_cast<double>(hits) * 1000.0 / units >= minimum;
        }
        if (basis == "delta") {
            // 单扫一份稿子算不出净增，报错而不退回按全篇判；写前那一份与写后那一份都不判，
            // 正文那一份留到差分之后按净增再筛一次（见 apply_baseline）。
            if (options_.role == LintScanRole::Standalone)
                throw std::runtime_error("规则 " + rule.id +
                                         " 的触发密度按净增算（Basis=delta），这次扫描没有基线");
            return true;
        }
        // 其余基准在编译时就已报错，走到这里只能是分派漏了一条
        throw std::runtime_error("规则 " + rule.id + " 的密度基准 " + basis + " 尚未实现");
    }

    // 只记了处数、没留位置的候选：报告里的「另有 N 处未列出」与规则总处数按它们算。
    // 严重程度那道门与 report 相同：没留位置的与留位置的按同一把尺子，被这道门挡掉的规则一处也不记。
    void count_unlisted(const LintRule& rule, std::size_t count) {
        if (count == 0) return;
        if (!equals_ignore_case(options_.min_severity, "info") && rule.severity != "warn") return;
        outcome_.rule_hits[current_rule_index_] += count;
    }

    void report(const CompiledRule& compiled, std::size_t begin, std::size_t end, std::size_t hint) {
        const LintRule& rule = compiled.rule();
        // min_severity=warn 时只留警告级，报告过滤就这一条
        if (!equals_ignore_case(options_.min_severity, "info") && rule.severity != "warn") return;
        // 原文、说明都留在原处：原文按字节区间从稿子里切，说明按规则下标从规则表取。
        // 命中里只留区间、行列、规则下标与建议下标，命中长度按区间现算。
        const Location location = index_.locate(begin, end);
        LintHit hit;
        hit.rule = static_cast<std::uint16_t>(current_rule_index_);
        hit.hint = static_cast<std::uint16_t>(hint);
        hit.begin = begin;
        hit.end = end;
        hit.line = static_cast<std::uint32_t>(location.line);
        hit.column = static_cast<std::uint32_t>(location.column);
        hit.length = static_cast<std::uint32_t>(location.length);
        hit.order = next_order_++;
        push_hit(hit);
    }

    // 单条规则的时限。按整条规则累计，累计的量里算着每一处正则试错的花费：
    // 超了 budget_ 就停下并报错，消息里带规则编号与毫秒数，不静默跳过。
    void start_rule(std::size_t rule_index, const LintRule& rule) {
        rule_started_ = Clock::now();
        current_rule_ = &rule;
        current_rule_index_ = rule_index;
        ticks_ = 0;
    }

    // 命中只带规则下标，回查规则表取标题、说明、严重程度
    const LintRule& rules_of(const LintHit& hit) const { return (*outcome_.rules)[hit.rule]; }

    // 走到扩容这一步说明按稿件长度估的那张表估小了：按翻倍再长一格，别让每处都去要一次。
    void push_hit(const LintHit& hit) {
        // 真实处数先记下：判据、密度、报告里的总数都按它算，与这条规则列不列得下无关。
        std::size_t& total = outcome_.rule_hits[hit.rule];
        ++total;
        // 超出一条规则的上限就不入表：处数照记，条目丢掉。
        if (cap_now_ && total > kMaxListedHitsPerRule) return;
        if (outcome_.hits.size() == outcome_.hits.capacity())
            outcome_.hits.reserve(outcome_.hits.capacity() * 2 + kHitGrow);
        outcome_.hits.push_back(hit);
    }

    // 附加串进 arena，返回偏移，长度写回 length。
    // 一开始就把常见规模预留好：偏移不受搬家影响，但搬一次要白拷一遍。
    std::uint32_t add_arena_string(std::string_view text, std::uint32_t& length) {
        if (outcome_.arena.capacity() == 0) outcome_.arena.reserve(kArenaReserve);
        const std::uint32_t offset = static_cast<std::uint32_t>(outcome_.arena.size());
        outcome_.arena.append(text);
        length = static_cast<std::uint32_t>(text.size());
        return offset;
    }

    void check_deadline() {
        if (Clock::now() - rule_started_ < budget_) return;
        throw std::runtime_error("规则 " + current_rule_->id + " 执行超过时限（" +
                                 std::to_string(budget_.count()) + " 毫秒），已停下");
    }

    // 每 1024 处看一眼表：每次命中都读时钟的话，处数一多这层自己就成了开销。
    void tick() {
        if ((++ticks_ & 1023u) != 0) return;
        check_deadline();
    }

    // 共享准备（见 ScanPrep）：主 Scan 建它，分派出去的线程只借。这一份必须比本对象活得久。
    std::shared_ptr<ScanPrep> prep_;
    std::string_view text_;
    const LintOptions& options_;
    const LineIndex& index_;
    const std::string& masked_text_;    // 空格副本的全文：只有博文稿才有内容，其余为空串
    const std::string_view scan_text_;  // 规则与统计读的那一份；不是博文时就是原文
    const std::size_t units_;           // 原文全长，按 UTF-16 码元；算法按它算窗口与千字处数
    const std::size_t body_units_;      // 正文字数：原文全长减去博文结构，密度分母与报告的
                                        // 「全文 N 字」都用它
    const std::chrono::milliseconds budget_;
    const bool markdown_;
    const bool blog_default_;
    // 扫描时就把每条规则超上限的命中挡在表外（见构造函数）。
    const bool cap_now_;
    // 留给命中表的附加串，一处命中一格：这一格装的是什么（见 EarlyKind）、位置是多少、
    // 整份描述子在哪。格号就在 LintHit::extra 里，一格只属于一条命中。
    // 为什么存位置不存整句：见 store_early_detail，存位置四字节，存整句三十多字节。
    std::vector<std::uint8_t> early_kind_;
    std::vector<std::uint32_t> early_value_;
    std::vector<std::pair<std::uint32_t, LintHitExtra>> early_text_;
    // 一条规则的命中占一段连续的格子：记下每段的起点与规则下标，按格查模板用（见 rule_of_slot）。
    std::vector<std::pair<std::uint32_t, std::uint16_t>> early_rule_starts_;

    std::optional<std::vector<Paragraph>>& paragraphs_;  // 只借 ScanPrep 里那一份：用到才切，
                                                         // 切在派活之前（见 run）
    const MatchLimits limits_{};

    // 分段计时的累计值，正常跑不读也不写（note_seconds 自己会判）
    double prep_seconds_ = 0;
    double slices_seconds_ = 0;
    double compile_seconds_ = 0;
    double data_seconds_ = 0;
    double algo_seconds_ = 0;

    LintOutcome outcome_;
    Clock::time_point rule_started_{};
    const LintRule* current_rule_ = nullptr;
    std::size_t current_rule_index_ = 0;
    std::uint32_t next_order_ = 0;  // 命中的次序：合并那一趟统一编（见 merge_rule），
                                    // 算法规则在总表上直接写时也用它
    std::size_t ticks_ = 0;
};

}  // namespace

// ---------------------------------------------------------------- 规则编译

CompiledRule::CompiledRule(const LintRule& rule) : rule_(rule) {
    std::size_t hint_index = 0;
    if (rule.literal_patterns) {
        for (const auto& literal : *rule.literal_patterns) {
            patterns_.emplace_back(Pattern(escape_literal(literal)), hint_index++);
        }
    }
    for (const auto& pattern : rule.patterns) {
        const bool literal = rule.kind == RuleKind::Literal;
        patterns_.emplace_back(Pattern(literal ? escape_literal(pattern) : pattern), hint_index++);
    }

    build_fast_path();

    // 尚未实现的密度基准在编译时就报：扫到命中才报会被读成「这份稿子没问题」
    if (rule.density) {
        const std::string& basis = rule.density->basis;
        if (basis != "document" && basis != "delta") {
            throw std::runtime_error("规则 " + rule.id + " 的密度基准 " + basis +
                                     " 尚未实现：先用 document 或 delta");
        }
    }
}

// 快路的候选串与索引。能否走快路由每条模式的 Pattern::start_plan 说了算，
// 这里只做两件事：把能走的那些模式的候选串收成一张前缀索引，记下每个模式在表里的位置。
//
// 能走快路的模式少于三条时不建索引：快路那一趟全文扫描比「逐条扫一趟」贵（三字节前缀表
// 每格要做一次哈希，逐条扫那条路的首字节筛是摊成比较指令的），实测一趟快路约等于
// 三趟逐条扫。两条模式的规则按老路走反而快，别为了统一把它拖慢。
void CompiledRule::build_fast_path() {
    fast_first_.assign(patterns_.size(), kNoFastEntry);
    std::vector<std::string> needles;
    std::vector<FastEntry> entries;
    std::size_t usable = 0;
    for (std::size_t index = 0; index < patterns_.size(); ++index) {
        const StartPlan plan = patterns_[index].first.start_plan();
        if (plan.kind == StartKind::none || plan.needles.empty()) continue;
        if (plan.whole && plan.needles.size() != 1) continue;  // 整串命中的必然只有一串
        const std::size_t first = entries.size();
        bool ok = true;
        for (const std::string& needle : plan.needles) {
            if (needle.empty()) {
                ok = false;
                break;
            }
            FastEntry entry;
            entry.pattern = index;
            entry.hint = patterns_[index].second;
            entry.whole = plan.whole;
            entries.push_back(entry);
            needles.push_back(needle);
        }
        if (!ok) {
            // 这一条给不出可用的候选串：它退回老路，已经塞进去的也一并撤掉
            entries.resize(first);
            needles.resize(first);
            continue;
        }
        fast_first_[index] = first;
        ++usable;
    }
    if (usable < 3) return;  // 见上面那段：一两模式的规则走老路更快
    scanner_.build(needles);
    // 建不起来、或者编号与候选串对不上（不该发生），就全体退回老路：宁可慢，不能悄悄少扫
    if (scanner_.empty() || scanner_.size() != entries.size()) return;
    fast_entries_ = std::move(entries);
}

std::optional<std::string> CompiledRule::hint_at(std::size_t index) const {
    if (!rule_.hints || index >= rule_.hints->size()) return std::nullopt;
    return (*rule_.hints)[index];
}

CompiledRules::CompiledRules(const std::vector<LintRule>& rules)
    : rules_(&rules), cache_(rules.size()) {}

const CompiledRule* CompiledRules::compiled_at(std::size_t index) const {
    if (index >= cache_.size()) return nullptr;
    const LintRule& rule = (*rules_)[index];
    if (rule.kind == RuleKind::Algorithm) return nullptr;
    if (!cache_[index]) cache_[index].emplace(rule);
    return &*cache_[index];
}

// ---------------------------------------------------------------- 下标换成字符串

const LintRule& HitFields::rule_of(const LintHit& hit) const { return rules[hit.rule]; }

const LintHitExtra* HitFields::extra_of(const LintHit& hit) const {
    if (hit.extra == kNoExtra || hit.extra >= extras.size()) return nullptr;
    return &extras[hit.extra];
}

std::string_view HitFields::arena_part(std::uint32_t offset, std::uint32_t length) const {
    if (length == 0) return {};
    if (static_cast<std::size_t>(offset) + length > arena.size()) return {};
    return arena.substr(offset, length);
}

std::string_view HitFields::match_of(const LintHit& hit) const {
    if (const LintHitExtra* extra = extra_of(hit); extra != nullptr) {
        const std::string_view stored = arena_part(extra->match_offset, extra->match_length);
        if (!stored.empty()) return stored;
    }
    // 数据规则的原文就是区间里那段字节；区间越界按空串算，不去猜
    if (hit.begin > hit.end || hit.end > text.size()) return {};
    return text.substr(hit.begin, hit.end - hit.begin);
}

std::string_view HitFields::detail_of(const LintHit& hit) const {
    if (const LintHitExtra* extra = extra_of(hit); extra != nullptr) {
        const std::string_view stored = arena_part(extra->detail_offset, extra->detail_length);
        if (!stored.empty()) return stored;
    }
    return rule_of(hit).detail;
}

std::optional<std::string_view> HitFields::hint_of(const LintHit& hit) const {
    const LintRule& rule = rule_of(hit);
    if (!rule.hints || hit.hint >= rule.hints->size()) return std::nullopt;
    const std::string& hint = (*rule.hints)[hit.hint];
    if (hint.empty()) return std::nullopt;
    return hint;
}

std::optional<std::string_view> HitFields::scheme_of(const LintHit& hit) const {
    const LintRule& rule = rule_of(hit);
    if (!rule.suggest_scheme || rule.suggest_scheme->empty()) return std::nullopt;
    return *rule.suggest_scheme;
}

std::string_view HitFields::ready_snippet_of(const LintHit& hit) const {
    const LintHitExtra* extra = extra_of(hit);
    if (extra == nullptr) return {};
    return arena_part(extra->snippet_offset, extra->snippet_length);
}

namespace {

// 命中的字节区间按 32 位存（每处省八个字节），所以稿件要能装进 4GB。
// 超过就响亮报错：宁可报「接不了」，也不能悄悄把偏移截断成错的。
constexpr std::size_t kMaxTextBytes = 0xFFFFFFFFull;

}  // namespace

LintOutcome run_lint(const CompiledRules& compiled, std::string_view text,
                     const LintOptions& options) {
    if (text.size() > kMaxTextBytes)
        throw std::runtime_error("稿件超过 4GB，接不了：命中区间按 32 位存");
    // 建索引那一份（行表、字数、分段方式、博文探测）在构造函数里做，耗时记在 prep 上
    const Clock::time_point started = Clock::now();
    Scan scan(text, options);
    scan.add_prep_seconds(seconds_since(started));
    return scan.run(compiled.rules(), &compiled);
}

LintOutcome run_lint(const std::vector<LintRule>& rules, std::string_view text,
                     const LintOptions& options) {
    if (text.size() > kMaxTextBytes)
        throw std::runtime_error("稿件超过 4GB，接不了：命中区间按 32 位存");
    const Clock::time_point started = Clock::now();
    Scan scan(text, options);
    scan.add_prep_seconds(seconds_since(started));
    // 只有这一份稿子：每条规则编译完就放掉，不为下一份留着。
    return scan.run(rules, nullptr);
}

// ---------------------------------------------------------------- 与写前那一份对差

BaselineCounts count_baseline(const LintOutcome& baseline) {
    BaselineCounts counts;
    const HitFields fields = baseline.fields();
    counts.total_hits = static_cast<int>(baseline.hits.size());
    for (const LintHit& hit : baseline.hits) {
        const std::string id = fields.rule_of(hit).id;
        counts.by_rule_and_match[lower(id)][std::string(fields.match_of(hit))] += 1;
    }
    // 报告里写新增处数时按规则编号的原样与字节序排，写这一段时就按这个次序
    for (const LintRule& rule : *baseline.rules) {
        if (counts.by_rule_and_match.find(lower(rule.id)) != counts.by_rule_and_match.end())
            counts.rule_ids.push_back(rule.id);
    }
    std::sort(counts.rule_ids.begin(), counts.rule_ids.end());
    return counts;
}

void apply_baseline(LintOutcome& body, const BaselineCounts& baseline,
                    const std::vector<std::string>& only_rules, const std::string& min_severity) {
    const HitFields fields = body.fields();

    // 参与比较的规则先记下来：删掉一段之后一条不剩的规则，报告里仍要给「新增 0 处」。
    // 「这次没变多」与「这条没查」是两件事。
    std::vector<std::string> involved = baseline.rule_ids;
    for (const LintHit& hit : body.hits) {
        const std::string& id = fields.rule_of(hit).id;
        if (std::find(involved.begin(), involved.end(), id) == involved.end())
            involved.push_back(id);
    }
    std::sort(involved.begin(), involved.end());

    // 同一个词在写前那一份里出现过几次，正文里同名词面的前几处就当作存量丢掉，从第几处开始算新增。
    // 位置没有对应关系，只能按出现次序认账。
    std::map<std::pair<std::string, std::string>, int> seen;
    std::vector<LintHit> added;
    added.reserve(body.hits.size());
    for (const LintHit& hit : body.hits) {
        const std::string id = lower(fields.rule_of(hit).id);
        const std::string match(fields.match_of(hit));
        const int already = seen[{id, match}]++;
        int in_baseline = 0;
        const auto rule_at = baseline.by_rule_and_match.find(id);
        if (rule_at != baseline.by_rule_and_match.end()) {
            const auto match_at = rule_at->second.find(match);
            if (match_at != rule_at->second.end()) in_baseline = match_at->second;
        }
        if (already < in_baseline) continue;
        added.push_back(hit);
    }
    body.hits = std::move(added);

    // 按净增算密度门限的规则，差分之后才判：净增处数除以写后千字数，不到下限就整条撤掉。
    // 分母取写后那份：要回答的是「现在这份稿子有多密」，已经删掉的那部分不该再参与分母。
    for (const LintRule& rule : *body.rules) {
        if (!rule.density || rule.density->basis != "delta") continue;
        std::size_t count = 0;
        for (const LintHit& hit : body.hits)
            if (equals_ignore_case(fields.rule_of(hit).id, rule.id)) ++count;
        const double units = static_cast<double>(std::max<std::size_t>(body.units, 1));
        if (static_cast<double>(count) * 1000.0 / units >= rule.density->min) continue;
        body.hits.erase(std::remove_if(body.hits.begin(), body.hits.end(),
                                       [&](const LintHit& hit) {
                                           return equals_ignore_case(fields.rule_of(hit).id, rule.id);
                                       }),
                        body.hits.end());
    }

    body.baseline_hits = baseline.total_hits;

    // 每条参与比较的规则给一行新增处数。护栏按「第一个整数是该规则的新增处数」读这一行，
    // 措辞可以改，数字必须在最前。
    for (const std::string& id : involved) {
        int count = 0;
        for (const LintHit& hit : body.hits)
            if (equals_ignore_case(fields.rule_of(hit).id, id)) ++count;
        body.notes.push_back({id, "相对基线新增 " + std::to_string(count) + " 处"});
    }

    // 收尾的两刀：命中只留点名的规则与警告级，统计附注只留点名的规则。
    // 新增处数按这两刀之前的命中数写，两刀不改它。
    const bool want_warn_only = !equals_ignore_case(min_severity, "info");
    body.hits.erase(std::remove_if(body.hits.begin(), body.hits.end(),
                                   [&](const LintHit& hit) {
                                       if (!only_rules.empty() &&
                                           !is_named_in(only_rules, fields.rule_of(hit).id))
                                           return true;
                                       return want_warn_only && fields.rule_of(hit).severity != "warn";
                                   }),
                    body.hits.end());
    if (!only_rules.empty()) {
        body.notes.erase(std::remove_if(body.notes.begin(), body.notes.end(),
                                        [&](const LintNote& note) {
                                            return !is_named_in(only_rules, note.rule_id);
                                        }),
                         body.notes.end());
    }

    // 差分之后命中定稿：真实处数按裁完的表重数一遍，封顶与报告都读这一份。
    body.rule_hits.assign(body.rules->size(), 0);
    for (const LintHit& hit : body.hits) ++body.rule_hits[hit.rule];
}

void cap_listed_hits(LintOutcome& outcome) {
    if (outcome.rules == nullptr) return;
    const std::size_t rule_count = outcome.rules->size();
    if (outcome.rule_hits.size() != rule_count) {
        // 没数过真实处数（手上只有一张命中表的那条路）：先把每条规则的处数点出来。
        outcome.rule_hits.assign(rule_count, 0);
        for (const LintHit& hit : outcome.hits)
            if (hit.rule < rule_count) ++outcome.rule_hits[hit.rule];
    }

    // 命中已按起点排过，留下的就是各条规则最早的那几处；不为超出的部分另占内存，原地收。
    std::vector<std::size_t> listed(rule_count, 0);
    std::size_t keep = 0;
    for (std::size_t i = 0; i < outcome.hits.size(); ++i) {
        const LintHit& hit = outcome.hits[i];
        if (hit.rule < rule_count) {
            if (listed[hit.rule] >= kMaxListedHitsPerRule) continue;
            ++listed[hit.rule];
        }
        if (keep != i) outcome.hits[keep] = hit;
        ++keep;
    }
    outcome.hits.resize(keep);
}

}  // namespace texttool
