#include "engine/location.h"

namespace texttool {

namespace {

// 一个 UTF-8 序列占几个字节、折算成几个 UTF-16 码元。
// 非法或截断的起始字节按一个字节、一个码元算：宁可偏一格，也绝不吞掉后面的字。
struct Unit {
    std::size_t bytes = 1;
    std::size_t units = 1;
};

Unit measure(std::string_view text, std::size_t at) {
    const auto lead = static_cast<unsigned char>(text[at]);
    std::size_t bytes = 0;
    if (lead < 0x80) return {1, 1};
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
    // 基本平面以外（四字节序列）在 .NET 字符串里是两个码元
    return {bytes, bytes == 4 ? std::size_t{2} : std::size_t{1}};
}

// 一段字节折合成 UTF-16 码元数：一个字符占一个码元，基本平面以外的占两个。
std::size_t units_in(std::string_view text, std::size_t begin, std::size_t end) {
    std::size_t units = 0;
    for (std::size_t at = begin; at < end;) {
        const Unit unit = measure(text, at);
        units += unit.units;
        at += unit.bytes;
    }
    return units;
}

}  // namespace

LineIndex::LineIndex(std::string_view text) : text_(text) {
    // 行首是文本开头与每个 '\n' 的下一个位置：CRLF 的 CR 仍算上一行的一列
    line_starts_.push_back(0);
    for (std::size_t at = 0; at < text_.size(); ++at)
        if (text_[at] == '\n') line_starts_.push_back(static_cast<std::uint32_t>(at + 1));
}

Location LineIndex::locate(std::size_t begin, std::size_t end) const {
    if (begin > text_.size()) begin = text_.size();
    if (end > text_.size()) end = text_.size();
    if (end < begin) end = begin;

    // 二分找最后一个不大于 begin 的行首：行号从 1 起，列号是距行首的码元数加一
    std::size_t lo = 0;
    std::size_t hi = line_starts_.size() - 1;
    while (lo < hi) {
        const std::size_t mid = lo + (hi - lo + 1) / 2;
        if (line_starts_[mid] <= begin) {
            lo = mid;
        } else {
            hi = mid - 1;
        }
    }

    Location location;
    location.line = lo + 1;
    location.column = units_in(text_, line_starts_[lo], begin) + 1;    location.length = units_in(text_, begin, end);
    return location;
}

std::size_t LineIndex::text_units() const { return units_in(text_, 0, text_.size()); }

}  // namespace texttool
