// 编码检测与写回的测试。
// 判编码的先后：先看 BOM，再看头 4KB 是不是合法 UTF-8，再复验全篇，不是 UTF-8 就按 GBK；没有认不出来这一档。
// 坏字节照各自的解码器补（UTF-8 补 U+FFFD，GBK 按 cp936 的最佳匹配回退补）；
// 写回保持原编码，BOM 有就留、没有就不加。
//
// 字节序列全部写死成十六进制，不靠实现算出来，否则实现错了测试也跟着错。
// 中 = U+4E2D，文 = U+6587；UTF-8 是 E4 B8 AD E6 96 87，GBK 是 D6 D0 CE C4。
#include <initializer_list>
#include <ostream>
#include <string>
#include <string_view>

#include "doctest/doctest.h"
#include "engine/encoding.h"

namespace {

using texttool::TextEncoding;

// 「中文」两个字，按各编码写出来的字节。
const std::string kUtf8Han = "\xE4\xB8\xAD\xE6\x96\x87";
const std::string kGbkHan = "\xD6\xD0\xCE\xC4";
const std::string kUtf16LeBomHan = "\xFF\xFE\x2D\x4E\x87\x65";  // U+FEFF + 中文
const std::string kUtf16BeBomHan = "\xFE\xFF\x4E\x2D\x65\x87";
const std::string kUtf8BomHan = "\xEF\xBB\xBF\xE4\xB8\xAD\xE6\x96\x87";

std::string bytes_of(std::initializer_list<int> values) {
    std::string out;
    for (int value : values) out.push_back(static_cast<char>(value));
    return out;
}

// U+FFFD：UTF-8 解码器补的替换字符。
const std::string kReplacement = "\xEF\xBF\xBD";
// cp936 的坏字节规则与 UTF-8 不同：单字节 0xFF 解成私用区 U+F8F5，
// 不合法的双字节字补一个 '?'。这里是 U+F8F5 的 UTF-8 写法。
const std::string kGbkInvalidByte = "\xEF\xA3\xB5";
// cp936 的 0x80 是欧元符号 U+20AC。
const std::string kEuroSign = "\xE2\x82\xAC";
// A2 E3：cp936 解成私用区 U+E76C，GB18030 码表里是 Euro 号，两边不一样。
const std::string kCp936PrivateE76C = "\xEE\x9D\xAC";

}  // namespace

// ================================================================
//  检测：BOM 优先
// ================================================================

TEST_CASE("带 BOM 的 UTF-8 认成 Utf8Bom") {
    const std::string bytes = kUtf8BomHan;
    CHECK(texttool::detect_encoding(bytes) == TextEncoding::Utf8Bom);
    CHECK(texttool::probe_encoding(bytes).recognized);
}

TEST_CASE("UTF-16 带 BOM 分别认成小端与大端") {
    const std::string le = kUtf16LeBomHan;
    const std::string be = kUtf16BeBomHan;
    CHECK(texttool::detect_encoding(le) == TextEncoding::Utf16Le);
    CHECK(texttool::detect_encoding(be) == TextEncoding::Utf16Be);
    CHECK(texttool::probe_encoding(le).recognized);
    CHECK(texttool::probe_encoding(be).recognized);
}

// ================================================================
//  检测：没有 BOM 时的顺序（UTF-8 合法优先，否则 GBK）
// ================================================================

TEST_CASE("不带 BOM 的 UTF-8 认成 Utf8") {
    const std::string bytes = kUtf8Han;
    CHECK(texttool::detect_encoding(bytes) == TextEncoding::Utf8);
    CHECK(texttool::probe_encoding(bytes).recognized);
}

TEST_CASE("空文件与纯 ASCII 都认成 Utf8") {
    CHECK(texttool::detect_encoding("") == TextEncoding::Utf8);
    CHECK(texttool::detect_encoding("abc\r\n") == TextEncoding::Utf8);
    CHECK(texttool::probe_encoding("").recognized);
}

TEST_CASE("GBK 中文认成 Gbk") {
    const std::string bytes = kGbkHan;
    CHECK(texttool::detect_encoding(bytes) == TextEncoding::Gbk);
    CHECK(texttool::probe_encoding(bytes).recognized);
}

// 严格标准：头 4KB 判成合法 UTF-8 之后还要复验全篇，全篇不合法就回退 GBK。
// 整份文件末尾落在半个字上时，复验不算它合法，于是按 GBK 读。
TEST_CASE("末尾截断在多字节字符中间的 UTF-8 按 GBK 处理") {
    const std::string bytes = std::string("abc") + bytes_of({0xE4, 0xB8});
    CHECK(texttool::detect_encoding(bytes) == TextEncoding::Gbk);
}

// 同上：前 4KB 全是 ASCII（合法 UTF-8），后面才出现 GBK 的双字节字。
TEST_CASE("头 4KB 合法但全篇不是合法 UTF-8 的字节按 GBK 处理") {
    std::string bytes(5000, 'a');
    bytes += bytes_of({0x81, 0x40});  // GBK 的「丂」，0x81 在 UTF-8 里是非法首字节
    CHECK(texttool::detect_encoding(bytes) == TextEncoding::Gbk);
}

TEST_CASE("非法 UTF-8 但结构上是 GBK 的字节认成 Gbk") {
    // E4 41：UTF-8 里 E4 后面必须是续字节，这里是 ASCII 'A'，不合法；
    // GBK 里 E4 是首字节、41 是次字节，合法。
    const std::string bytes = bytes_of({0xE4, 0x41});
    CHECK(texttool::detect_encoding(bytes) == TextEncoding::Gbk);
    CHECK(texttool::probe_encoding(bytes).recognized);
}

// 没有「认不出来」这一档：不是合法 UTF-8 就一律按 GBK，坏字节交给 cp936 的解码器补。
TEST_CASE("既不是 UTF-8 也不是 GBK 的字节照样按 GBK 处理") {
    const std::string bytes = bytes_of({0xFF, 0xFF});
    const auto probe = texttool::probe_encoding(bytes);
    CHECK(probe.recognized);
    CHECK(probe.encoding == TextEncoding::Gbk);
    CHECK(texttool::detect_encoding(bytes) == TextEncoding::Gbk);
    CHECK(texttool::to_utf8(bytes, TextEncoding::Gbk) ==
          kGbkInvalidByte + kGbkInvalidByte);
}

// 十五万字节的 GBK 稿，中间插一个 0xFF（既不是合法 UTF-8，也不是 GBK 的合法字节）。
// 头 4KB 认成 GBK，坏字节由 cp936 补成私用区字，前后的中文一个不丢；
// 按 UTF-8 硬解则整篇都成了替换字符。
TEST_CASE("十五万字节的 GBK 稿中间插一个无效字节，仍按 GBK 读出中文") {
    std::string bytes;
    std::string expected;
    for (int i = 0; i < 18750; ++i) {
        bytes += kGbkHan;
        expected += kUtf8Han;
    }
    bytes += bytes_of({0xFF});
    expected += kGbkInvalidByte;
    for (int i = 0; i < 18750; ++i) {
        bytes += kGbkHan;
        expected += kUtf8Han;
    }
    REQUIRE(bytes.size() == 150001);

    REQUIRE(texttool::detect_encoding(bytes) == TextEncoding::Gbk);
    CHECK(texttool::to_utf8(bytes, TextEncoding::Gbk) == expected);
}

// ================================================================
//  解码：to_utf8
// ================================================================

TEST_CASE("UTF-8 文本原样返回，带 BOM 的去掉 BOM") {
    CHECK(texttool::to_utf8(kUtf8Han, TextEncoding::Utf8) == kUtf8Han);
    CHECK(texttool::to_utf8(kUtf8BomHan, TextEncoding::Utf8Bom) == kUtf8Han);
}

TEST_CASE("UTF-16 两个方向都解成同一份 UTF-8 文本") {
    CHECK(texttool::to_utf8(kUtf16LeBomHan, TextEncoding::Utf16Le) == kUtf8Han);
    CHECK(texttool::to_utf8(kUtf16BeBomHan, TextEncoding::Utf16Be) == kUtf8Han);
}

TEST_CASE("GBK 解成 UTF-8 文本") {
    CHECK(texttool::to_utf8(kGbkHan, TextEncoding::Gbk) == kUtf8Han);
}

// Windows 侧按 4KB 切块交给系统码表。首字节正好落在切点前最后一格时，
// 不能把这一格单独切出去：系统会把半个字补成 '?'，次字节又被当成 ASCII，整段错位。
TEST_CASE("GBK 双字节字跨 4KB 切块时整字解出") {
    std::string bytes(4095, 'a');
    bytes += "\xD6\xD0";  // 「中」：首字节在 4095，次字节在 4096
    bytes += "A";
    CHECK(texttool::to_utf8(bytes, TextEncoding::Gbk) ==
          std::string(4095, 'a') + "\xE4\xB8\xAD" + "A");
}

// 解码器带替换回退：坏序列只出一个替换字符，后面的字节重新对齐，
// E4 B8 是不完整的「中」，41 是 ASCII 'A'。
TEST_CASE("UTF-8 里的坏序列按替换字符补，后面的字节继续解") {
    const std::string bytes = bytes_of({0xE4, 0xB8, 0x41});
    CHECK(texttool::to_utf8(bytes, TextEncoding::Utf8) == kReplacement + std::string("A"));
}

// 末尾被截断的多字节字符不能原样留着：那不是合法 UTF-8，写回去下游会当坏字节。
// 按「最大合法子部分」补一个替换字符，四字节序列截断也只补一个。
TEST_CASE("末尾被截断的多字节字符解成替换字符") {
    const std::string three = std::string("abc") + bytes_of({0xE4, 0xB8});
    CHECK(texttool::to_utf8(three, TextEncoding::Utf8) == std::string("abc") + kReplacement);

    const std::string four = std::string("a") + bytes_of({0xF0, 0x9F, 0x98});
    CHECK(texttool::to_utf8(four, TextEncoding::Utf8) == std::string("a") + kReplacement);
}

// GBK 的坏字节也照 cp936 的解码器补，与 UTF-8 的 U+FFFD 不是一回事。
TEST_CASE("GBK 的坏字节按 cp936 的最佳匹配回退补") {
    // 次字节 0x20 不合法：补一个 '?'，并把这个次字节一起吃掉，'A' 还在。
    CHECK(texttool::to_utf8(bytes_of({0x81, 0x20, 0x41}), TextEncoding::Gbk) ==
          std::string("?A"));
    // 次字节 0x7F 也不合法，同样吃掉两个字节。
    CHECK(texttool::to_utf8(bytes_of({0x81, 0x7F, 0x41}), TextEncoding::Gbk) ==
          std::string("?A"));
    // 单字节 0xFF 解成私用区 U+F8F5，只吃一个字节。
    CHECK(texttool::to_utf8(bytes_of({0xFF, 0x41}), TextEncoding::Gbk) ==
          kGbkInvalidByte + std::string("A"));
    // 末尾只剩一个首字节：补一个 '?'。
    CHECK(texttool::to_utf8(bytes_of({0x81}), TextEncoding::Gbk) == std::string("?"));
    // 0x80 在 cp936 里是欧元符号，不是坏字节。
    CHECK(texttool::to_utf8(bytes_of({0x80}), TextEncoding::Gbk) == kEuroSign);
    // 结构合法、但 cp936 与 GB18030 码表不一致的私用区行，按 cp936 解。
    CHECK(texttool::to_utf8(bytes_of({0xA2, 0xE3}), TextEncoding::Gbk) == kCp936PrivateE76C);
    // 这些写回去要能原样回来。
    CHECK(texttool::from_utf8(kCp936PrivateE76C, TextEncoding::Gbk) == bytes_of({0xA2, 0xE3}));
    CHECK(texttool::from_utf8(kEuroSign, TextEncoding::Gbk) == bytes_of({0x80}));
    CHECK(texttool::from_utf8(kGbkInvalidByte, TextEncoding::Gbk) == bytes_of({0xFF}));
}

// ================================================================
//  写回：from_utf8 保持原编码
// ================================================================

TEST_CASE("五种编码往返后字节完全一致") {
    struct Case {
        const char* name;
        TextEncoding encoding;
        std::string bytes;
    };
    const Case cases[] = {
        {"utf-8", TextEncoding::Utf8, kUtf8Han + std::string("\r\n")},
        {"utf-8 bom", TextEncoding::Utf8Bom, kUtf8BomHan},
        {"utf-16le", TextEncoding::Utf16Le, kUtf16LeBomHan},
        {"utf-16be", TextEncoding::Utf16Be, kUtf16BeBomHan},
        {"gbk", TextEncoding::Gbk, kGbkHan},
    };

    for (const auto& item : cases) {
        CAPTURE(item.name);
        const TextEncoding detected = texttool::detect_encoding(item.bytes);
        REQUIRE(detected == item.encoding);
        const std::string text = texttool::to_utf8(item.bytes, detected);
        CHECK(texttool::from_utf8(text, detected) == item.bytes);
    }
}

TEST_CASE("没写 BOM 的 UTF-8 写回后仍然不带 BOM") {
    const std::string text = kUtf8Han + std::string("\n");
    const std::string out = texttool::from_utf8(text, TextEncoding::Utf8);
    REQUIRE(out.size() == text.size());
    CHECK(out == text);
    CHECK(out.compare(0, 3, "\xEF\xBB\xBF") != 0);
}

TEST_CASE("原先带 BOM 的 UTF-8 写回后只有一个 BOM") {
    const std::string text = texttool::to_utf8(kUtf8BomHan, TextEncoding::Utf8Bom);
    const std::string out = texttool::from_utf8(text, TextEncoding::Utf8Bom);
    CHECK(out == kUtf8BomHan);

    // 文本自己又带了 BOM 也不能多出一个。
    const std::string twice = "\xEF\xBB\xBF" + kUtf8Han;
    CHECK(texttool::from_utf8(twice, TextEncoding::Utf8Bom) == kUtf8BomHan);
}

TEST_CASE("UTF-16 写回时带上原编码的 BOM") {
    const std::string text = kUtf8Han;
    const std::string le = texttool::from_utf8(text, TextEncoding::Utf16Le);
    const std::string be = texttool::from_utf8(text, TextEncoding::Utf16Be);
    CHECK(le == kUtf16LeBomHan);
    CHECK(be == kUtf16BeBomHan);
}

TEST_CASE("换行原样保留，不被改写") {
    const std::string text = std::string("a\r\nb\nc");
    CHECK(texttool::from_utf8(text, TextEncoding::Utf8) == text);
    CHECK(texttool::to_utf8(texttool::from_utf8(text, TextEncoding::Gbk), TextEncoding::Gbk) ==
          text);
}

// GBK 表示不了的字符换成 '?'：cp936 编码器对无法映射的字符用 '?' 顶替，
// 写回不抛错，整份稿子才处理得下去。
TEST_CASE("GBK 写回时表示不了的字符换成问号") {
    const std::string text = "\xF0\x9F\x98\x80";  // emoji，GBK 里没有
    CHECK(texttool::from_utf8(text, TextEncoding::Gbk) == "?");
}

// ================================================================
//  跨块边界：转换按固定大小的块搬字节，边界落在半个字上不能出错
// ================================================================

TEST_CASE("三百份「中文」的 GBK 往返，跨过所有内部块边界") {
    std::string gbk;
    std::string utf8;
    for (int i = 0; i < 300 * 4096; ++i) {
        gbk += kGbkHan;
        utf8 += kUtf8Han;
    }
    REQUIRE(texttool::to_utf8(gbk, TextEncoding::Gbk) == utf8);
    REQUIRE(texttool::from_utf8(utf8, TextEncoding::Gbk) == gbk);
}

TEST_CASE("UTF-16 长短不一的文本往返，含增补平面字符") {
    // 中文 + U+1F600（UTF-16 里是一对代理，UTF-8 里是四字节）
    const std::string text = kUtf8Han + "\xF0\x9F\x98\x80" + kUtf8Han;
    const std::string le = texttool::from_utf8(text, TextEncoding::Utf16Le);
    const std::string be = texttool::from_utf8(text, TextEncoding::Utf16Be);
    CHECK(texttool::to_utf8(le, TextEncoding::Utf16Le) == text);
    CHECK(texttool::to_utf8(be, TextEncoding::Utf16Be) == text);
    CHECK(le.size() == 2 + 4 * 2 + 4);  // BOM + 四个汉字各 2 字节 + 一对代理 4 字节
}

// 预留要一次到位：换出来的字节不该配着翻倍的余量，
// 否则 15MB 稿子会在“输入 + 输出”之外再多占一份。
TEST_CASE("四种高开销换码的容量不翻倍") {
    std::string gbk;
    std::string utf8;
    for (int i = 0; i < 20000; ++i) {
        gbk += kGbkHan;
        utf8 += kUtf8Han;
    }

    // 允许预留到「再多一半」：微软的标准库按 1.5 倍长，不像 glibc 那样贴着 size 长。
    // 这条要挡的是「翻倍」，不是某个具体比例。
    const auto spare = [](const std::string& value) { return value.size() + value.size() / 2 + 1; };

    const std::string text = texttool::to_utf8(gbk, TextEncoding::Gbk);
    CHECK(text.capacity() <= spare(text));

    const std::string back = texttool::from_utf8(utf8, TextEncoding::Gbk);
    CHECK(back.capacity() <= spare(back));

    const std::string le = texttool::from_utf8(utf8, TextEncoding::Utf16Le);
    CHECK(le.capacity() <= spare(le));

    const std::string leText = texttool::to_utf8(le, TextEncoding::Utf16Le);
    CHECK(leText.capacity() <= spare(leText));
}

// ================================================================
//  分块接口：按块读入、转换、追加，源字节与结果不同时整份留在内存里
// ================================================================

TEST_CASE("分块解码与整篇解码逐字节一致，块切在任何位置都行") {
    const std::string text = kUtf8Han + "a" + std::string("\xF0\x9F\x98\x80") + kUtf8Han;
    struct Case {
        const char* name;
        TextEncoding encoding;
        std::string bytes;
    };
    const Case cases[] = {
        {"utf-8", TextEncoding::Utf8, text},
        {"utf-8 末尾截断", TextEncoding::Utf8, std::string("abc") + bytes_of({0xE4, 0xB8})},
        {"utf-8 带 BOM", TextEncoding::Utf8Bom, std::string("\xEF\xBB\xBF") + text},
        {"utf-16le", TextEncoding::Utf16Le, texttool::from_utf8(text, TextEncoding::Utf16Le)},
        {"utf-16be", TextEncoding::Utf16Be, texttool::from_utf8(text, TextEncoding::Utf16Be)},
        {"gbk", TextEncoding::Gbk, texttool::from_utf8(text, TextEncoding::Gbk)},
        {"gbk 带坏字节", TextEncoding::Gbk,
         kGbkHan + bytes_of({0x81, 0x20, 0xFF, 0xA2, 0xE3}) + kGbkHan},
    };

    for (const auto& item : cases) {
        CAPTURE(item.name);
        const std::string whole = texttool::to_utf8(item.bytes, item.encoding);
        const std::string_view view(item.bytes);
        for (std::size_t chunk = 1; chunk <= view.size(); ++chunk) {
            std::string out;
            texttool::ChunkedDecoder decoder(out, item.encoding, view.size());
            for (std::size_t at = 0; at < view.size(); at += chunk)
                decoder.append(view.substr(at, chunk));
            decoder.finish();
            CHECK(out == whole);
        }
    }
}

TEST_CASE("分块写回与整篇写回逐字节一致，块切在任何位置都行") {
    const std::string text =
        kUtf8Han + "a\r\nb" + std::string("\xF0\x9F\x98\x80") + kUtf8Han + "c";
    const TextEncoding cases[] = {TextEncoding::Utf8, TextEncoding::Utf8Bom, TextEncoding::Utf16Le,
                                  TextEncoding::Utf16Be, TextEncoding::Gbk};

    for (TextEncoding encoding : cases) {
        CAPTURE(texttool::encoding_name(encoding));
        const std::string whole = texttool::from_utf8(text, encoding);
        const std::string_view view(text);
        for (std::size_t chunk = 1; chunk <= view.size(); ++chunk) {
            std::string out;
            texttool::ChunkedEncoder encoder(out, encoding, view.size());
            for (std::size_t at = 0; at < view.size(); at += chunk)
                encoder.append(view.substr(at, chunk));
            encoder.finish();
            CHECK(out == whole);
        }
    }
}

// 文本自己带的 BOM 不能被写成两个：BOM 还没凑齐的那一块要先留到下一块再判。
TEST_CASE("分块写回时文本自带的 BOM 不会写两遍") {
    const std::string text = kUtf8BomHan;  // 文本开头就是一个 BOM
    const std::string whole = texttool::from_utf8(text, TextEncoding::Utf8Bom);
    REQUIRE(whole == kUtf8BomHan);
    for (std::size_t chunk = 1; chunk <= text.size(); ++chunk) {
        std::string out;
        texttool::ChunkedEncoder encoder(out, TextEncoding::Utf8Bom, text.size());
        for (std::size_t at = 0; at < text.size(); at += chunk)
            encoder.append(std::string_view(text).substr(at, chunk));
        encoder.finish();
        CHECK(out == kUtf8BomHan);
    }
}

// 分块路径要按源字节数一次预留够，否则 15MB 的稿子会在结果之外再多占半份。
TEST_CASE("分块解码按源字节数一次预留够，不边转边翻倍") {
    std::string gbk;
    for (int i = 0; i < 20000; ++i) gbk += kGbkHan;

    std::string out;
    texttool::ChunkedDecoder decoder(out, TextEncoding::Gbk, gbk.size());
    for (std::size_t at = 0; at < gbk.size(); at += 4096)
        decoder.append(std::string_view(gbk).substr(at, 4096));
    decoder.finish();

    CHECK(out.size() == 20000 * kUtf8Han.size());
    CHECK(out.capacity() <= out.size() + out.size() / 20);
}

// 一条完整的处理路径：读完、解成 UTF-8、按原编码写回，块大小与文件大小无关。
TEST_CASE("分块读一整篇 GBK 稿再写回，字节完全一致") {
    std::string gbk;
    for (int i = 0; i < 20000; ++i) gbk += kGbkHan;
    const std::string_view source(gbk);

    std::string text;
    texttool::ChunkedDecoder decoder(text, TextEncoding::Gbk, source.size());
    for (std::size_t at = 0; at < source.size(); at += 4096)
        decoder.append(source.substr(at, 4096));
    decoder.finish();
    REQUIRE(text.size() == 20000 * kUtf8Han.size());

    std::string back;
    texttool::ChunkedEncoder encoder(back, TextEncoding::Gbk, text.size());
    for (std::size_t at = 0; at < text.size(); at += 4096)
        encoder.append(std::string_view(text).substr(at, 4096));
    encoder.finish();
    CHECK(back == gbk);
}

// ================================================================
//  名字：报告里出现的那几个
// ================================================================

TEST_CASE("编码名字与报告里的一致") {
    CHECK(texttool::encoding_name(TextEncoding::Utf8) == "utf-8");
    CHECK(texttool::encoding_name(TextEncoding::Utf8Bom) == "utf-8-bom");
    CHECK(texttool::encoding_name(TextEncoding::Utf16Le) == "utf-16le");
    CHECK(texttool::encoding_name(TextEncoding::Utf16Be) == "utf-16be");
    CHECK(texttool::encoding_name(TextEncoding::Gbk) == "gbk");
}

// ================================================================
//  按目标编码数字节：行合并的「按字节数」用它量宽度
// ================================================================

TEST_CASE("按编码数字节：UTF-8 是文本字节数，UTF-16 一个汉字两字节，GBK 也是两字节") {
    CHECK(texttool::encoded_byte_count(kUtf8Han, TextEncoding::Utf8) == 6);
    CHECK(texttool::encoded_byte_count(kUtf8Han, TextEncoding::Utf8Bom) == 6);  // 不含 BOM
    CHECK(texttool::encoded_byte_count(kUtf8Han, TextEncoding::Utf16Le) == 4);
    CHECK(texttool::encoded_byte_count(kUtf8Han, TextEncoding::Utf16Be) == 4);
    CHECK(texttool::encoded_byte_count(kUtf8Han, TextEncoding::Gbk) == 4);
    CHECK(texttool::encoded_byte_count("", TextEncoding::Gbk) == 0);
}

TEST_CASE("按编码数字节：GBK 的两个单字节特例只算一个字节") {
    CHECK(texttool::encoded_byte_count(kEuroSign, TextEncoding::Gbk) == 1);
    CHECK(texttool::encoded_byte_count(kGbkInvalidByte, TextEncoding::Gbk) == 1);
}

TEST_CASE("按编码数字节：国标码表示不了的字算一个问号") {
    // U+1F600 表情符号：F0 9F 98 80。国标码没有它的码位，写出去是一个 '?'。
    const std::string emoji = "\xF0\x9F\x98\x80";
    CHECK(texttool::encoded_byte_count(emoji, TextEncoding::Gbk) == 1);
    CHECK(texttool::encoded_byte_count(emoji, TextEncoding::Utf8) == 4);
    CHECK(texttool::from_utf8(emoji, TextEncoding::Gbk) == "?");
    // 前面那两个字四字节，加一个表情符号写出时的一个 '?'。
    CHECK(texttool::encoded_byte_count(kUtf8Han + emoji, TextEncoding::Gbk) == 5);
}

TEST_CASE("按编码数字节的结果与写回时写出的字节数一致，UTF-16 除外（写回补 BOM）") {
    const std::string emoji = "\xF0\x9F\x98\x80";
    const std::string samples[] = {kUtf8Han, "abc", kUtf8Han + " mixed 123 " + kUtf8Han,
                                   kEuroSign + kGbkInvalidByte + "x", emoji + "a" + emoji, ""};
    const TextEncoding encodings[] = {TextEncoding::Utf8, TextEncoding::Gbk};
    for (const auto& sample : samples) {
        for (TextEncoding encoding : encodings) {
            CHECK(texttool::encoded_byte_count(sample, encoding) ==
                  texttool::from_utf8(sample, encoding).size());
        }
    }
    // 带 BOM 的两种写回时补 BOM：UTF-8 三字节，UTF-16 两字节。数字节时都不算它
    for (const auto& sample : samples) {
        CHECK(texttool::encoded_byte_count(sample, TextEncoding::Utf8Bom) ==
              texttool::from_utf8(sample, TextEncoding::Utf8Bom).size() - 3);
        for (TextEncoding encoding : {TextEncoding::Utf16Le, TextEncoding::Utf16Be}) {
            CHECK(texttool::encoded_byte_count(sample, encoding) ==
                  texttool::from_utf8(sample, encoding).size() - 2);
        }
    }
}
