// 字节偏移换算成行列与长度。
//
// 这一层按 UTF-16 码元算行列：一个字符占一个下标，基本平面以外的字符占两个，
// 与报告里的列号、字数同一个数法。所以中文稿的列号不是字节数，表情符号还要再差一位。
// 算错这一处，报告里每个行列都会偏，逐字节对照必挂。
#pragma once

#include <cstddef>
#include <cstdint>
#include <string_view>
#include <vector>

namespace texttool {

struct Location {
    std::size_t line = 0;    // 从 1 开始
    std::size_t column = 0;  // 从 1 开始，按 UTF-16 码元数
    std::size_t length = 0;  // 命中长度，也按 UTF-16 码元数
};

// 预先扫一遍文本里每一行的起始字节偏移，重复换算时不必每次从头数。
//
// 行首偏移按 32 位存：一份十五兆的稿子有二十四万行，按 64 位存就是一点九兆，
// 按 32 位存省下一半，而稿件本来限在 4GB 以内（run_lint 遇到更大的直接报错，不静默截断）。
class LineIndex {
public:
    explicit LineIndex(std::string_view text);

    // 把字节区间 [begin, end) 换算成行列与长度。
    Location locate(std::size_t begin, std::size_t end) const;

    // 全文长度，按 UTF-16 码元数，与报告里的 Chars 一致。
    std::size_t text_units() const;

private:
    std::string_view text_;
    std::vector<std::uint32_t> line_starts_;
};

}  // namespace texttool
