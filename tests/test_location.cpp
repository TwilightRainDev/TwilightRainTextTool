// 行列表的测试。行列就这么数：
// 行首是每个 '\n' 的下一个位置，列号是「距本行行首多少」加一，长度同理，
// 三者都按 .NET 字符串下标算，也就是 UTF-16 码元：一个汉字占一列，表情占两列。
//
// 字节全部写死，不靠实现算出来，否则实现错了测试也跟着错。
// 中 = U+4E2D（E4 B8 AD），😀 = U+1F600（F0 9F 98 80，两个码元）。
#include <string>
#include <string_view>

#include "doctest/doctest.h"
#include "engine/location.h"

namespace {

// 「中文」两个字，六个字节，两个码元。
const std::string kHan = "\xE4\xB8\xAD\xE6\x96\x87";
// 一个表情，四个字节，两个码元。
const std::string kEmoji = "\xF0\x9F\x98\x80";

texttool::Location at(std::string_view text, std::size_t begin, std::size_t end) {
    return texttool::LineIndex(text).locate(begin, end);
}

}  // namespace

TEST_CASE("纯 ASCII：一行与多行") {
    const std::string text = "hello\nworld";
    const texttool::LineIndex index(text);

    const auto first = index.locate(0, 5);
    CHECK(first.line == 1);
    CHECK(first.column == 1);
    CHECK(first.length == 5);

    const auto second = index.locate(6, 11);
    CHECK(second.line == 2);
    CHECK(second.column == 1);
    CHECK(second.length == 5);

    // 第 1 行里靠中间的一段
    const auto middle = index.locate(2, 4);
    CHECK(middle.line == 1);
    CHECK(middle.column == 3);
    CHECK(middle.length == 2);

    CHECK(index.text_units() == 11);
}

TEST_CASE("中文：每个字三字节，但只算一列") {
    const std::string text = kHan + "\n" + kHan;  // 六个字节 + 换行 + 六个字节
    const texttool::LineIndex index(text);

    const auto first = index.locate(0, 6);
    CHECK(first.line == 1);
    CHECK(first.column == 1);
    CHECK(first.length == 2);

    // 第 2 行的第一个字：7 字节处，列号是 1 而不是 7
    const auto second = index.locate(7, 9);
    CHECK(second.line == 2);
    CHECK(second.column == 1);
    CHECK(second.length == 1);

    // 第 2 行的第二个字：10 字节处，列号 2
    const auto tail = index.locate(10, 13);
    CHECK(tail.line == 2);
    CHECK(tail.column == 2);
    CHECK(tail.length == 1);

    CHECK(index.text_units() == 5);
}

TEST_CASE("表情符号：占两个码元，列号跟着走") {
    const std::string text = kEmoji + kEmoji;  // 八个字节，四个码元
    const texttool::LineIndex index(text);

    const auto first = index.locate(0, 4);
    CHECK(first.line == 1);
    CHECK(first.column == 1);
    CHECK(first.length == 2);

    // 第二个表情从第 5 个码元开始：字节偏移 4，列号 3
    const auto second = index.locate(4, 8);
    CHECK(second.line == 1);
    CHECK(second.column == 3);
    CHECK(second.length == 2);

    CHECK(index.text_units() == 4);
}

TEST_CASE("中文与表情混排：列号按码元累加") {
    const std::string text = kHan + kEmoji + kHan;  // 2 + 2 + 2 个码元，16 字节
    const texttool::LineIndex index(text);

    const auto emoji = index.locate(6, 10);
    CHECK(emoji.line == 1);
    CHECK(emoji.column == 3);
    CHECK(emoji.length == 2);

    const auto tail = index.locate(10, 16);
    CHECK(tail.column == 5);
    CHECK(tail.length == 2);

    CHECK(index.text_units() == 6);
}

TEST_CASE("CRLF 行尾：行首在 LF 之后，CR 算第 1 行的一列") {
    // "abc\r\ndef"：第 1 行 3 个字，第 2 行从字节 5 开始
    const std::string text = "abc\r\ndef";
    const texttool::LineIndex index(text);

    const auto cr = index.locate(3, 4);
    CHECK(cr.line == 1);
    CHECK(cr.column == 4);
    CHECK(cr.length == 1);

    const auto second = index.locate(5, 8);
    CHECK(second.line == 2);
    CHECK(second.column == 1);
    CHECK(second.length == 3);

    CHECK(index.text_units() == 8);
}

TEST_CASE("空行：连续换行各算一行") {
    // "a\n\nb"：第 2 行是空的，第 3 行是 b
    const std::string text = "a\n\nb";
    const texttool::LineIndex index(text);

    const auto first = index.locate(0, 1);
    CHECK(first.line == 1);
    CHECK(first.column == 1);

    // 空行的行首：字节 2，列号 1
    const auto empty_line = index.locate(2, 2);
    CHECK(empty_line.line == 2);
    CHECK(empty_line.column == 1);
    CHECK(empty_line.length == 0);

    const auto third = index.locate(3, 4);
    CHECK(third.line == 3);
    CHECK(third.column == 1);

    // 开头就是空行也按第 1 行算
    CHECK(at("\nb", 1, 2).line == 2);

    CHECK(index.text_units() == 4);
}

TEST_CASE("行尾处命中：命中停在行末，列号是行宽减一") {
    // "接上。\n下一行"：第 1 行三个字，命中「接上。」从 6 字节处起
    const std::string text = "\xE6\x8E\xA5\xE4\xB8\x8A\xE3\x80\x82\nabc";
    const texttool::LineIndex index(text);

    const auto hit = index.locate(0, 9);
    CHECK(hit.line == 1);
    CHECK(hit.column == 1);
    CHECK(hit.length == 3);

    // 只抓行尾最后一个字：第 3 列
    const auto last = index.locate(6, 9);
    CHECK(last.line == 1);
    CHECK(last.column == 3);
    CHECK(last.length == 1);

    // 空命中停在行末（换行符之前）：第 4 列，长度 0
    const auto end = index.locate(9, 9);
    CHECK(end.line == 1);
    CHECK(end.column == 4);
    CHECK(end.length == 0);

    // 换行符本身也算第 1 行的第 4 列
    const auto newline = index.locate(9, 10);
    CHECK(newline.line == 1);
    CHECK(newline.column == 4);
    CHECK(newline.length == 1);

    CHECK(index.text_units() == 7);
}

TEST_CASE("全文字数：按 UTF-16 码元算，空稿是 0") {
    CHECK(texttool::LineIndex("").text_units() == 0);
    CHECK(texttool::LineIndex("abc").text_units() == 3);
    CHECK(texttool::LineIndex(kHan).text_units() == 2);
    CHECK(texttool::LineIndex(kEmoji).text_units() == 2);
    CHECK(texttool::LineIndex(kEmoji + kEmoji).text_units() == 4);
    CHECK(texttool::LineIndex("a\nb").text_units() == 3);
}
