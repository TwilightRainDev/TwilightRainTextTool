// markdown 逐行判定与博文遮罩的实现。判定按下面这份约定，不要按 CommonMark 全文重写：
// 注释与报告是按字节比的，差一个边界就是一处不一致。被遮住的结构见 markdown.h。
#include "engine/markdown.h"

#include <algorithm>
#include <cstddef>
#include <cstdint>
#include <string>
#include <string_view>
#include <vector>

namespace texttool {
namespace {

// 取一个 UTF-8 字符，返回吃掉的字节数。非法序列按单字节返回，不抛也不丢。
std::size_t decode_utf8(std::string_view text, std::size_t pos, char32_t& code_point) {
    auto lead = static_cast<unsigned char>(text[pos]);
    if (lead < 0x80) {
        code_point = lead;
        return 1;
    }

    std::size_t length = 0;
    char32_t value = 0;
    if ((lead & 0xE0) == 0xC0) {
        length = 2;
        value = lead & 0x1F;
    } else if ((lead & 0xF0) == 0xE0) {
        length = 3;
        value = lead & 0x0F;
    } else if ((lead & 0xF8) == 0xF0) {
        length = 4;
        value = lead & 0x07;
    } else {
        code_point = lead;
        return 1;
    }

    if (pos + length > text.size()) {
        code_point = lead;
        return 1;
    }
    for (std::size_t offset = 1; offset < length; ++offset) {
        auto next = static_cast<unsigned char>(text[pos + offset]);
        if ((next & 0xC0) != 0x80) {
            code_point = lead;
            return 1;
        }
        value = (value << 6) | (next & 0x3F);
    }
    code_point = value;
    return length;
}

// 空白的判定与 .NET 的 char.IsWhiteSpace 同一组：换行、制表、各档间隔符都算。
bool is_space(char32_t code_point) {
    return (code_point >= 0x09 && code_point <= 0x0D) || code_point == 0x20 ||
           code_point == 0x85 || code_point == 0xA0 || code_point == 0x1680 ||
           (code_point >= 0x2000 && code_point <= 0x200A) || code_point == 0x2028 ||
           code_point == 0x2029 || code_point == 0x202F || code_point == 0x205F ||
           code_point == 0x3000;
}

// .NET 的 string.Trim()：两头去掉 char.IsWhiteSpace 的字符，中间不动。
std::string_view trim(std::string_view text) {
    std::size_t begin = 0;
    while (begin < text.size()) {
        char32_t code_point = 0;
        const std::size_t length = decode_utf8(text, begin, code_point);
        if (!is_space(code_point)) break;
        begin += length;
    }
    std::size_t end = begin;
    std::size_t pos = begin;
    while (pos < text.size()) {
        char32_t code_point = 0;
        pos += decode_utf8(text, pos, code_point);
        if (!is_space(code_point)) end = pos;
    }
    return text.substr(begin, end - begin);
}

std::string_view trim_start(std::string_view text) {
    std::size_t begin = 0;
    while (begin < text.size()) {
        char32_t code_point = 0;
        const std::size_t length = decode_utf8(text, begin, code_point);
        if (!is_space(code_point)) break;
        begin += length;
    }
    return text.substr(begin);
}

bool starts_with(std::string_view text, std::string_view value) {
    return text.size() >= value.size() && text.compare(0, value.size(), value) == 0;
}

bool contains(std::string_view text, std::string_view value) {
    return text.find(value) != std::string_view::npos;
}

bool is_ascii_digit(char32_t code_point) { return code_point >= '0' && code_point <= '9'; }

char ascii_lower(char c) { return (c >= 'A' && c <= 'Z') ? static_cast<char>(c - 'A' + 'a') : c; }

// .NET 的 StartsWith(..., OrdinalIgnoreCase)。
bool starts_with_ignore_case(std::string_view text, std::string_view value) {
    if (text.size() < value.size()) return false;
    for (std::size_t i = 0; i < value.size(); ++i)
        if (ascii_lower(text[i]) != ascii_lower(value[i])) return false;
    return true;
}

// 键值行的形状：行首是英文键名，紧跟一个冒号。汉字键名不算，那是正文里的「注意：」。
bool has_yaml_key(std::string_view trimmed) {
    std::size_t i = 0;
    if (i >= trimmed.size() ||
        !((trimmed[i] >= 'a' && trimmed[i] <= 'z') || (trimmed[i] >= 'A' && trimmed[i] <= 'Z')))
        return false;
    while (i < trimmed.size()) {
        const char c = trimmed[i];
        const bool word = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
                          (c >= '0' && c <= '9') || c == '_' || c == '-';
        if (!word) break;
        ++i;
    }
    return i < trimmed.size() && trimmed[i] == ':';
}

// 引用式链接定义：[名字]: 地址。整行没有正文，全遮。
bool is_reference_definition(std::string_view trimmed) {
    if (trimmed.empty() || trimmed.front() != '[') return false;
    const std::size_t close = trimmed.find("]:");
    return close != std::string_view::npos && close > 1;
}

// 从 from 起找 value，找不到返回 npos。就是 .NET 的 IndexOf(value, from, count)：
// 只在这一行（不含换行）里找，命中位置与长度都不越出这一行。
std::size_t find_in(std::string_view text, std::size_t from, std::size_t end,
                    std::string_view value) {
    if (from >= end || value.empty()) return std::string_view::npos;
    const std::size_t limit = end;  // 命中的末字节不能越过 end
    const std::size_t area = limit - from;
    const std::size_t at = text.substr(from, area).find(value);
    if (at == std::string_view::npos) return std::string_view::npos;
    if (from + at + value.size() > limit) return std::string_view::npos;
    return from + at;
}

// 一串同一个字符有多长。
std::size_t run_length(std::string_view text, std::size_t start, std::size_t end, char c) {
    std::size_t i = start;
    while (i < end && text[i] == c) ++i;
    return i - start;
}

// 找长度正好是 length 的那一串反引号，返回它的起点；没有返回 npos。
std::size_t find_backtick_run(std::string_view text, std::size_t from, std::size_t end,
                              std::size_t length) {
    for (std::size_t i = from; i < end;) {
        if (text[i] != '`') {
            ++i;
            continue;
        }
        const std::size_t run = run_length(text, i, end, '`');
        if (run == length) return i;
        i += run;
    }
    return std::string_view::npos;
}

// 自动链接：<http://…>、<https://…>、<mailto:…>。tag_end 是 '>' 后面的那个字节。
bool looks_like_autolink(std::string_view text, std::size_t start, std::size_t end,
                         std::size_t& tag_end) {
    tag_end = start;
    if (!starts_with(text.substr(start, end - start), "<http://") &&
        !starts_with(text.substr(start, end - start), "<https://") &&
        !starts_with(text.substr(start, end - start), "<mailto:"))
        return false;
    const std::size_t close = text.find('>', start);
    if (close == std::string_view::npos || close >= end) return false;
    tag_end = close + 1;
    return true;
}

// 行内公式：$...$ 与 $$...$$。开记号后面紧跟空白的不算，收记号前面是空白的也不算，
// 单 $ 还要求两边不是数字，免得把「一件 $5 一件 $10」当成公式遮掉。
bool try_mask_formula(std::string_view text, std::size_t start, std::size_t end,
                      std::size_t& after) {
    after = start;
    const std::size_t marker = (start + 1 < end && text[start + 1] == '$') ? 2 : 1;
    const std::size_t open = start + marker;
    if (open >= end) return false;

    char32_t first = 0;
    decode_utf8(text, open, first);
    if (is_space(first)) return false;
    if (marker == 1 && is_ascii_digit(first)) return false;

    const std::string_view fence = marker == 2 ? "$$" : "$";
    const std::size_t close = find_in(text, open, end, fence);
    if (close == std::string_view::npos) return false;

    // 收记号前面那个字：回退到它的首字节再解。
    std::size_t previous = close - 1;
    while (previous > 0 && (static_cast<unsigned char>(text[previous]) & 0xC0) == 0x80) --previous;
    char32_t last = 0;
    decode_utf8(text, previous, last);
    if (is_space(last)) return false;
    if (marker == 1 && close + 1 < end) {
        char32_t next = 0;
        decode_utf8(text, close + 1, next);
        if (is_ascii_digit(next)) return false;
    }

    after = close + marker;
    return true;
}

}  // namespace

bool FenceTracker::advance(std::string_view raw_line) {
    // 按 trim 后的行首认：全角空格缩进的围栏行照样算。
    std::string_view line = raw_line;
    if (!line.empty() && line.back() == '\r') line.remove_suffix(1);
    const std::string_view trimmed = trim(line);

    if (in_fence_) {
        if (!markdown::closes_fence(trimmed, fence_char_, fence_length_)) return false;
        in_fence_ = false;
        return true;
    }

    char fence_char = '\0';
    std::size_t fence_length = 0;
    if (!markdown::opens_fence(trimmed, fence_char, fence_length)) return false;
    in_fence_ = true;
    fence_char_ = fence_char;
    fence_length_ = fence_length;
    return true;
}

namespace markdown {

bool opens_fence(std::string_view trimmed, char& fence_char, std::size_t& fence_length) {
    fence_char = trimmed.empty() ? '\0' : trimmed.front();
    fence_length = 0;
    if (fence_char != '`' && fence_char != '~') return false;
    while (fence_length < trimmed.size() && trimmed[fence_length] == fence_char) ++fence_length;
    return fence_length >= 3;
}

bool closes_fence(std::string_view trimmed, char fence_char, std::size_t fence_length) {
    if (trimmed.size() < fence_length) return false;
    for (char c : trimmed)
        if (c != fence_char) return false;
    return true;
}

bool is_front_matter_opening(std::string_view trimmed) {
    // .NET 那边先 TrimStart('\uFEFF') 再 Trim()：字节序标记不算字。
    constexpr std::string_view kByteOrderMark = "\xEF\xBB\xBF";  // U+FEFF 的 UTF-8 写法
    std::string_view text = trimmed;
    if (starts_with(text, kByteOrderMark)) text.remove_prefix(kByteOrderMark.size());
    return trim(text) == "---";
}

bool is_front_matter_closing(std::string_view trimmed) {
    return trimmed == "---" || trimmed == "...";
}

bool is_front_matter_key(std::string_view trimmed, std::string_view key) {
    if (!starts_with_ignore_case(trimmed, key)) return false;
    const std::string_view rest = trimmed.substr(key.size());
    if (starts_with(rest, ":")) return true;
    return starts_with(trim_start(rest), ":");
}

bool looks_like_front_matter(std::string_view text, std::size_t after_opening_line) {
    std::size_t pos = std::min(after_opening_line + 1, text.size());
    for (int lines = 0; lines < 200 && pos < text.size(); ++lines) {
        const std::size_t newline = text.find('\n', pos);
        const std::size_t line_end = newline == std::string_view::npos ? text.size() : newline;
        const std::string_view trimmed = trim(text.substr(pos, line_end - pos));
        if (is_front_matter_closing(trimmed)) return false;
        if (has_yaml_key(trimmed)) return true;
        if (newline == std::string_view::npos) return false;
        pos = newline + 1;
    }
    return false;
}

bool is_directive(std::string_view trimmed) { return starts_with(trimmed, "::"); }

}  // namespace markdown

bool BlogMask::is_masked(std::size_t index) const {
    if (!is_blog_post_ || index >= length_) return false;
    return (words_[index >> 6] & (std::uint64_t{1} << (index & 63))) != 0;
}

std::string BlogMask::apply(std::string_view text) const {
    // 不是博文时没有遮罩，长度对不上：原样交回，调用方不必先问 is_blog_post。
    if (!is_blog_post_ || length_ != text.size()) return std::string(text);

    std::string result(text);
    for (std::size_t i = 0; i < result.size(); ++i) {
        if (result[i] == '\n' || result[i] == '\r') continue;
        if (is_masked(i)) result[i] = ' ';
    }
    return result;
}

void BlogMask::mark(std::size_t from, std::size_t to) {
    const std::size_t start = from;
    const std::size_t stop = std::min(to, length_);
    if (start >= stop) return;

    const std::size_t first = start >> 6;
    const std::size_t last = (stop - 1) >> 6;
    const std::uint64_t head = ~std::uint64_t{0} << (start & 63);
    const std::uint64_t tail = ~std::uint64_t{0} >> (63 - ((stop - 1) & 63));

    const auto fill = [this](std::size_t word, std::uint64_t bits) {
        const std::uint64_t fresh = bits & ~words_[word];
        for (std::uint64_t rest = fresh; rest != 0; rest &= rest - 1) ++masked_chars_;
        words_[word] |= bits;
    };

    if (first == last) {
        fill(first, head & tail);
        return;
    }

    fill(first, head);
    for (std::size_t word = first + 1; word < last; ++word) fill(word, ~std::uint64_t{0});
    fill(last, tail);
}

namespace {

// 行内那几样：行内代码、公式、链接地址、自动链接、HTML 注释。整行的范围是 [start, end)。
// 遮哪一段交给 mark 处理：整份遮罩与逐行状态机用的是同一套扫法，只是收的地方不同。
template <typename Mark>
void mask_inline(std::string_view text, std::size_t start, std::size_t end, bool& in_comment,
                 Mark&& mark) {
    std::size_t i = start;
    while (i < end) {
        const char c = text[i];

        if (c == '`') {
            const std::size_t open = run_length(text, i, end, '`');
            const std::size_t close = find_backtick_run(text, i + open, end, open);
            if (close == std::string_view::npos) {
                i += open;
                continue;
            }
            mark(i, close + open);
            i = close + open;
            continue;
        }

        if (c == '<') {
            if (starts_with(text.substr(i, end - i), "<!--")) {
                const std::size_t close = find_in(text, i + 4, end, "-->");
                if (close == std::string_view::npos) {
                    // 注释跨行：这一行的尾巴也遮掉，后面的行由 in_comment 接着遮。
                    mark(i, end);
                    in_comment = true;
                    return;
                }
                mark(i, close + 3);
                i = close + 3;
                continue;
            }
            std::size_t tag_end = 0;
            if (looks_like_autolink(text, i, end, tag_end)) {
                mark(i, tag_end);
                i = tag_end;
                continue;
            }
        }

        if (c == '$') {
            std::size_t after = 0;
            if (try_mask_formula(text, i, end, after)) {
                mark(i, after);
                i = after;
                continue;
            }
        }

        // 链接与图片只遮地址：方括号里那截是给人看的字，照常检查。
        if (c == ']' && i + 1 < end && text[i + 1] == '(') {
            const std::size_t close = text.find(')', i + 2);
            if (close != std::string_view::npos && close < end) {
                mark(i + 1, close + 1);
                i = close + 1;
                continue;
            }
        }

        ++i;
    }
}

// 开头那几行算不算头部字段，要往后看这么多行。与原先看全文时扫的行数一样，
// 只是现在压着这几行等结论，不再回头去看整份稿子。
constexpr std::size_t kFrontMatterLookaheadLines = 200;

// 判定用的那一截：去掉行尾那一个 \r 再去空白。\r 本身仍在遮罩范围里。
std::string_view trimmed_for_judging(std::string_view line) {
    if (!line.empty() && line.back() == '\r') line.remove_suffix(1);
    return trim(line);
}

}  // namespace

// 一行一行交：头部字段里有没有 title 与 date、见过 :: 指令行没有，
// 以及每一行该不该遮、遮哪几段。
void BlogMaskScanner::feed(std::string_view utf8, const MaskSink& sink) {
    // 上一块末尾那一行没凑齐时才进这一段：补进 buffer_ 的只有这一行。
    // 整份正文不进来，调用方那份正文与这里这份不会是同一个时刻的两份二十几兆。
    if (!buffer_.empty()) {
        const std::size_t newline = utf8.find('\n');
        if (newline == std::string_view::npos) {
            buffer_.append(utf8);  // 这一块里仍然没有换行：这一行继续攒着
            return;
        }
        buffer_.append(utf8.substr(0, newline));
        take_line(std::string_view(buffer_), true, sink);
        buffer_.clear();
        utf8.remove_prefix(newline + 1);
    }

    // 剩下的从一行的开头算起：整行直接在调用方交来的这一块上判，不再抄一份。
    // 交出去的那一行只在 sink 返回之前有效，与 buffer_ 里那一行一样。
    std::size_t start = 0;
    while (true) {
        const std::size_t newline = utf8.find('\n', start);
        if (newline == std::string_view::npos) break;
        take_line(utf8.substr(start, newline - start), true, sink);
        start = newline + 1;
    }

    // 末尾没有换行的那一截留到下一块。整份稿子一行到底时这一截就是全篇，
    // 那是唯一还会整篇拷一份的情形（按行走的稿子只留着行尾这一截）。
    if (start < utf8.size()) buffer_.assign(utf8.substr(start));
}

// 一行交给判定。行数在这里加：first（稿子的第一行）由 lines_seen_ 现判。
void BlogMaskScanner::take_line(std::string_view line, bool has_newline, const MaskSink& sink) {
    const bool first = lines_seen_ == 0;
    ++lines_seen_;
    advance(line, has_newline, first, sink);
}

void BlogMaskScanner::finish(const MaskSink& sink) {
    // 最后那一行没有换行也要交；末尾孤零零一个 \n 后面不再多算一行。
    if (!buffer_.empty()) {
        take_line(std::string_view(buffer_), false, sink);
        buffer_.clear();
    }
    // 全文看完了还没判出头部字段，压着的那几行按正文交出去。
    if (deciding_) decide_front_matter(false, sink);
}

void BlogMaskScanner::advance(std::string_view line, bool has_newline, bool first,
                              const MaskSink& sink) {
    if (deciding_) {
        deferred_.push_back({std::string(line), has_newline});
        const std::string_view trimmed = trimmed_for_judging(line);
        if (markdown::is_front_matter_closing(trimmed)) {
            decide_front_matter(false, sink);
            return;
        }
        if (has_yaml_key(trimmed)) {
            decide_front_matter(true, sink);
            return;
        }
        // deferred_ 里第一项是开门那行，后面的才是往后看的行。
        if (deferred_.size() > kFrontMatterLookaheadLines) decide_front_matter(false, sink);
        return;
    }

    if (first && markdown::is_front_matter_opening(trimmed_for_judging(line))) {
        // 开头的 --- 算不算头部字段要看后面几行：这几行先压着，判出来再按次序交出去。
        deciding_ = true;
        deferred_.push_back({std::string(line), has_newline});
        return;
    }

    plain_line(line, has_newline, sink);
}

void BlogMaskScanner::plain_line(std::string_view line, bool has_newline, const MaskSink& sink) {
    const std::string_view trimmed = trimmed_for_judging(line);

    if (in_front_matter_) {
        has_title_ = has_title_ || markdown::is_front_matter_key(trimmed, "title");
        has_date_ = has_date_ || markdown::is_front_matter_key(trimmed, "date");
        if (markdown::is_front_matter_closing(trimmed)) in_front_matter_ = false;
        emit_whole_line(line, has_newline, sink);
        return;
    }
    if (fence_.in_fence()) {
        fence_.advance(line);
        emit_whole_line(line, has_newline, sink);
        return;
    }
    if (in_comment_) {
        if (contains(trimmed, "-->")) in_comment_ = false;
        emit_whole_line(line, has_newline, sink);
        return;
    }
    if (in_math_block_) {
        if (contains(trimmed, "$$")) in_math_block_ = false;
        emit_whole_line(line, has_newline, sink);
        return;
    }
    if (fence_.advance(line)) {
        emit_whole_line(line, has_newline, sink);
        return;
    }
    if (starts_with(trimmed, "$$") && !contains(trimmed.substr(2), "$$")) {
        in_math_block_ = true;
        emit_whole_line(line, has_newline, sink);
        return;
    }
    if (markdown::is_directive(trimmed)) {
        saw_directive_ = true;
        emit_whole_line(line, has_newline, sink);
        return;
    }
    if (is_reference_definition(trimmed)) {
        emit_whole_line(line, has_newline, sink);
        return;
    }
    emit_inline(line, has_newline, sink);
}

void BlogMaskScanner::decide_front_matter(bool accepted, const MaskSink& sink) {
    deciding_ = false;
    std::vector<DeferredLine> pending = std::move(deferred_);
    deferred_.clear();

    if (accepted) {
        in_front_matter_ = true;
        for (std::size_t index = 0; index < pending.size(); ++index) {
            const std::string_view line(pending[index].bytes);
            // 开门那行只遮不认键，后面的行才认 title 与 date。
            if (index > 0) {
                const std::string_view trimmed = trimmed_for_judging(line);
                has_title_ = has_title_ || markdown::is_front_matter_key(trimmed, "title");
                has_date_ = has_date_ || markdown::is_front_matter_key(trimmed, "date");
            }
            emit_whole_line(line, pending[index].has_newline, sink);
        }
        return;
    }

    // 不是头部字段：这几行按正文重放。开门那行那次已经判过、判不过，不再认一次。
    for (const DeferredLine& entry : pending) plain_line(entry.bytes, entry.has_newline, sink);
}

void BlogMaskScanner::emit_whole_line(std::string_view line, bool has_newline,
                                      const MaskSink& sink) {
    spans_.clear();
    sink(LineMask{line, has_newline, true, spans_});
}

void BlogMaskScanner::emit_inline(std::string_view line, bool has_newline, const MaskSink& sink) {
    spans_.clear();
    mask_inline(line, 0, line.size(), in_comment_, [this](std::size_t from, std::size_t to) {
        spans_.push_back({from, to});
    });
    sink(LineMask{line, has_newline, false, spans_});
}

BlogMask BlogMask::create(std::string_view text) {
    BlogMask mask;
    if (text.empty()) return mask;

    mask.length_ = text.size();
    mask.words_.assign((text.size() + 63) / 64, 0);

    // 判定与遮罩都由逐行状态机给出：整份喂进去，一行一行盖到这份遮罩上。
    BlogMaskScanner scanner;
    std::size_t pos = 0;
    const auto mark_line = [&mask, &pos](const BlogMaskScanner::LineMask& line) {
        const std::size_t width = line.line.size() + (line.has_newline ? 1 : 0);
        if (line.whole_line) {
            // 整行被遮时连它的换行一起遮：这一行不参与，换行也不该算进正文字数。
            mask.mark(pos, pos + width);
        } else {
            for (const BlogMaskScanner::Span& span : line.spans) mask.mark(pos + span.begin, pos + span.end);
        }
        pos += width;
    };
    scanner.feed(text, mark_line);
    scanner.finish(mark_line);

    mask.is_blog_post_ = scanner.is_blog_post();
    if (!mask.is_blog_post_) {
        mask.words_.clear();
        mask.words_.shrink_to_fit();
        mask.length_ = 0;
        mask.masked_chars_ = 0;
    }
    return mask;
}

}  // namespace texttool
