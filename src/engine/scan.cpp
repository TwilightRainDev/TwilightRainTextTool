#include "engine/scan.h"

#include <algorithm>
#include <cmath>
#include <map>
#include <stdexcept>

namespace texttool {

// 各组的源文件只靠静态初始化登记算法，静态库里的这类目标文件会被链接器丢掉，
// 所以这里显式引用一次，保证三组都被链进来。
void register_repeat_algorithms();
void register_rhythm_algorithms();
void register_shape_algorithms();

namespace {

const bool g_algorithms_linked = []() {
    register_repeat_algorithms();
    register_rhythm_algorithms();
    register_shape_algorithms();
    return true;
}();

std::map<std::string, AlgoFn>& registry() {
    static std::map<std::string, AlgoFn> table;
    return table;
}

// UTF-8 的首字节决定这个字符占几个字节；遇到坏字节按一个字节处理，不往后吞。
std::size_t char_bytes(unsigned char lead) {
    if (lead < 0x80) return 1;
    if ((lead & 0xE0) == 0xC0) return 2;
    if ((lead & 0xF0) == 0xE0) return 3;
    if ((lead & 0xF8) == 0xF0) return 4;
    return 1;
}

bool is_sentence_end(std::string_view text, std::size_t at, std::size_t width) {
    // 句末：句号、问号、叹号、省略号（六个点）与它们的中文写法。
    static const char* const marks[] = {"。", "？", "！", "…", ".", "?", "!"};
    for (const char* mark : marks) {
        const std::string_view m(mark);
        if (m.size() == width && at + width <= text.size() && text.substr(at, width) == m) return true;
    }
    return false;
}

// 收在句尾的引号与括号，属于上一句。
bool is_trailing_closer(std::string_view piece) {
    static const char* const closers[] = {"”", "’", "」", "』", "）", "》", "】", ")", "\"", "'"};
    for (const char* closer : closers) {
        if (piece == closer) return true;
    }
    return false;
}

}  // namespace

std::vector<Span> split_paragraphs(std::string_view text) {
    std::vector<Span> spans;
    std::size_t start = 0;
    std::size_t i = 0;
    while (i < text.size()) {
        if (text[i] == '\n') {
            // 一行一段：换行就分段，换行后面的空白（空行、行首缩进）留给下一段去掐。
            std::size_t j = i + 1;
            while (j < text.size() &&
                   (text[j] == '\n' || text[j] == '\r' || text[j] == ' ' || text[j] == '\t'))
                ++j;
            spans.push_back({span_index(start), span_index(i)});
            start = j;
            i = j;
            continue;
        }
        ++i;
    }
    if (start < text.size()) spans.push_back({span_index(start), span_index(text.size())});

    // 去掉首尾空白的空段
    std::vector<Span> kept;
    for (const Span& span : spans) {
        std::size_t begin = span.begin;
        std::size_t end = span.end;
        while (begin < end && (text[begin] == '\n' || text[begin] == '\r' || text[begin] == ' ')) ++begin;
        while (end > begin && (text[end - 1] == '\n' || text[end - 1] == '\r' || text[end - 1] == ' ')) --end;
        if (end > begin) kept.push_back({span_index(begin), span_index(end)});
    }
    return kept;
}

std::vector<Span> split_sentences(std::string_view text) {
    std::vector<Span> spans;
    std::size_t start = 0;
    std::size_t i = 0;
    while (i < text.size()) {
        const std::size_t width = char_bytes(static_cast<unsigned char>(text[i]));
        if (i + width > text.size()) break;
        if (is_sentence_end(text, i, width)) {
            std::size_t end = i + width;
            // 连续的句末标点算同一句的结尾
            while (end < text.size()) {
                const std::size_t w = char_bytes(static_cast<unsigned char>(text[end]));
                if (end + w > text.size()) break;
                if (is_sentence_end(text, end, w)) {
                    end += w;
                    continue;
                }
                if (is_trailing_closer(text.substr(end, w))) {
                    end += w;
                    continue;
                }
                break;
            }
            spans.push_back({span_index(start), span_index(end)});
            start = end;
            i = end;
            continue;
        }
        i += width;
    }
    if (start < text.size()) spans.push_back({span_index(start), span_index(text.size())});
    return spans;
}

std::size_t unit_length(std::string_view text, std::size_t begin, std::size_t end) {
    std::size_t units = 0;
    std::size_t i = begin;
    while (i < end) {
        const unsigned char lead = static_cast<unsigned char>(text[i]);
        const std::size_t width = char_bytes(lead);
        if (i + width > end) {
            ++units;
            ++i;
            continue;
        }
        // 基本平面以外（四字节序列）在 UTF-16 里占两个码元
        units += (width == 4) ? 2 : 1;
        i += width;
    }
    return units;
}

std::size_t measured_units(const MeasuredText& text, std::size_t begin, std::size_t end) {
    return unit_length(text.original, begin, end);
}

namespace {

// char.IsWhiteSpace 的那一组码点，与各个算法里掐空白用的那一组相同。
bool is_space(std::uint32_t cp) {
    return (cp >= 0x09 && cp <= 0x0D) || cp == 0x20 || cp == 0x85 || cp == 0xA0 || cp == 0x1680 ||
           (cp >= 0x2000 && cp <= 0x200A) || cp == 0x2028 || cp == 0x2029 || cp == 0x202F ||
           cp == 0x205F || cp == 0x3000;
}

// 取一个字符的码点，只用来判空白：宽度由字节数决定，这里不再重新数。
std::uint32_t code_point_at(std::string_view text, std::size_t at, std::size_t width) {
    const auto byte = [&](std::size_t offset) {
        return static_cast<std::uint32_t>(static_cast<unsigned char>(text[at + offset]));
    };
    if (width <= 1) return byte(0);
    if (width == 2) return ((byte(0) & 0x1Fu) << 6) | (byte(1) & 0x3Fu);
    if (width == 3) return ((byte(0) & 0x0Fu) << 12) | ((byte(1) & 0x3Fu) << 6) | (byte(2) & 0x3Fu);
    return 0;  // 四字节的都是基本平面以外，没有空白字符
}

}  // namespace

std::size_t trimmed_units(const MeasuredText& text, std::size_t begin, std::size_t end) {
    const std::string_view scan = text.scan;
    const std::size_t limit = std::min(end, scan.size());
    std::size_t head = begin;
    while (head < limit) {
        const std::size_t width = char_bytes(static_cast<unsigned char>(scan[head]));
        if (head + width > limit || !is_space(code_point_at(scan, head, width))) break;
        head += width;
    }
    std::size_t tail = limit;
    while (tail > head) {
        // 从末尾往回退一个字：UTF-8 的续字节是 10xxxxxx，退到首字节为止
        std::size_t previous = tail - 1;
        while (previous > head && (static_cast<unsigned char>(scan[previous]) & 0xC0) == 0x80)
            --previous;
        const std::size_t width = char_bytes(static_cast<unsigned char>(scan[previous]));
        if (!is_space(code_point_at(scan, previous, width))) break;
        tail = previous;
    }
    return unit_length(text.original, head, tail);
}

std::size_t byte_after_units(const MeasuredText& text, std::size_t begin, std::size_t end,
                             std::size_t count) {
    const std::size_t limit = std::min(end, text.scan.size());
    std::size_t at = begin;
    std::size_t units = 0;
    while (at < limit && units < count) {
        const std::size_t width = char_bytes(static_cast<unsigned char>(text.original[at]));
        const std::size_t step = (at + width > limit) ? 1 : width;
        units += (step == 4) ? 2 : 1;
        at += step;
    }
    return std::min(at, limit);
}

double per_thousand(std::size_t count, std::size_t units) {
    if (units == 0) return 0.0;
    const double value = static_cast<double>(count) * 1000.0 / static_cast<double>(units);
    return std::round(value * 10.0) / 10.0;
}

void register_algorithm(const std::string& name, AlgoFn fn) { registry()[name] = fn; }

bool algorithm_is_registered(std::string_view name) {
    // 登记表按 std::string 建，这里临时转一份：名字都是短的，改一次表不划算。
    return registry().find(std::string(name)) != registry().end();
}

namespace {

// 交接口每次只看当前这一条命中：上一条设置过的位置与数不能留到这一条。
thread_local DetailPosition g_pending_detail_position;

}  // namespace

DetailPosition take_pending_detail_position() {
    const DetailPosition taken = g_pending_detail_position;
    g_pending_detail_position = DetailPosition{};
    return taken;
}

void set_pending_detail_position(DetailPosition position) { g_pending_detail_position = position; }

std::string fill_detail_args(std::string_view pattern, const std::vector<std::string>& args) {
    std::string out;
    out.reserve(pattern.size());
    for (std::size_t i = 0; i < pattern.size();) {
        if (pattern[i] == '{') {
            std::size_t at = i + 1;
            std::size_t index = 0;
            bool digits = false;
            while (at < pattern.size() && pattern[at] >= '0' && pattern[at] <= '9') {
                index = index * 10 + static_cast<std::size_t>(pattern[at] - '0');
                digits = true;
                ++at;
            }
            if (digits && at < pattern.size() && pattern[at] == '}' && index < args.size()) {
                out += args[index];
                i = at + 1;
                continue;
            }
        }
        out.push_back(pattern[i]);
        ++i;
    }
    return out;
}

void run_algorithm(const LintRule& rule, const AlgoContext& context, AlgoWriter writer,
                   void* writer_context, std::vector<std::string>& notes) {
    const std::string& name = rule.algorithm_name.value_or(std::string());
    const auto it = registry().find(name);
    if (it == registry().end()) {
        throw std::runtime_error("规则 " + rule.id + " 的算法名没有实现：" + name);
    }
    it->second(rule, context, writer, writer_context, notes);
}

}  // namespace texttool
