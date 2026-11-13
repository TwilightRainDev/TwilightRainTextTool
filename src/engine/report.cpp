#include "engine/report.h"

#include <algorithm>
#include <cmath>
#include <cstdint>
#include <string>
#include <string_view>
#include <unordered_map>
#include <vector>

namespace texttool {

namespace {

// ---------------------------------------------------------------------------
// UTF-8 与逐个码位的读写
//
// 这里按码位处理，报告里的行列与长度则按 UTF-16 码元算：需要转义的字符都在 BMP 内，
// 两种切法得到同样的结果；BMP 之外的字符原样写出去，也不会被切开。
// ---------------------------------------------------------------------------

struct CodePoint {
    char32_t value = 0;
    std::size_t bytes = 1;
};

CodePoint decode_utf8(std::string_view text, std::size_t at) {
    const auto lead = static_cast<unsigned char>(text[at]);
    if (lead < 0x80) return {lead, 1};
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
        // 不是合法的 UTF-8 起始字节：当成一个字节原样带过去，绝不吞掉
        return {lead, 1};
    }
    if (at + length > text.size()) return {lead, 1};
    for (std::size_t i = 1; i < length; ++i) {
        const auto ch = static_cast<unsigned char>(text[at + i]);
        if ((ch & 0xC0) != 0x80) return {lead, 1};
        value = (value << 6) | (ch & 0x3F);
    }
    return {value, length};
}

void append_utf8(std::string& out, char32_t value) {
    if (value < 0x80) {
        out.push_back(static_cast<char>(value));
    } else if (value < 0x800) {
        out.push_back(static_cast<char>(0xC0 | (value >> 6)));
        out.push_back(static_cast<char>(0x80 | (value & 0x3F)));
    } else if (value < 0x10000) {
        out.push_back(static_cast<char>(0xE0 | (value >> 12)));
        out.push_back(static_cast<char>(0x80 | ((value >> 6) & 0x3F)));
        out.push_back(static_cast<char>(0x80 | (value & 0x3F)));
    } else {
        out.push_back(static_cast<char>(0xF0 | (value >> 18)));
        out.push_back(static_cast<char>(0x80 | ((value >> 12) & 0x3F)));
        out.push_back(static_cast<char>(0x80 | ((value >> 6) & 0x3F)));
        out.push_back(static_cast<char>(0x80 | (value & 0x3F)));
    }
}

// ---------------------------------------------------------------------------
// 要转义的字符
// ---------------------------------------------------------------------------

// 控制台编不出来的隐形字符。人读报告必须把它们写成 \uXXXX 字面量：
// Windows 默认码页里编不出 U+200B，直接打印会抛异常，报隐形字符等于不报。
bool is_invisible(char32_t value) {
    switch (value) {
        case 0x00A0:  // 不换行空格
        case 0x00AD:  // 软连字符
        case 0x200B:  // 零宽空格
        case 0x200C:  // 零宽不连字
        case 0x200D:  // 零宽连字
        case 0x200E:  // 左至右标记
        case 0x200F:  // 右至左标记
        case 0x2060:  // 词连接符
        case 0xFEFF:  // 零宽不换行空格（BOM）
            return true;
        default:
            return false;
    }
}

// 空白分隔符（Zs）里除普通空格以外的那些，加上行分隔符（Zl）与段分隔符（Zp）。
// JSON 里这些一律写成 \uXXXX，人读报告的片段里则原样保留。
bool is_exotic_space(char32_t value) {
    if (value == 0x00A0) return true;
    if (value == 0x1680) return true;
    if (value >= 0x2000 && value <= 0x200A) return true;
    if (value == 0x2028 || value == 0x2029) return true;
    if (value == 0x202F || value == 0x205F || value == 0x3000) return true;
    return false;
}

// 自用区：Unicode 把它们留给各语言私用，编码器不认为它们是可读的字。
// 只管 BMP 里的那一段；补充自用区（U+0F0000 起）与别的补充区字符一样按码元成对写。
bool is_private_use(char32_t value) { return value >= 0xE000 && value <= 0xF8FF; }

// 未分配码位与非字符（U+FFFE、U+FFFF、U+FDD0–U+FDEF 这些）：只放行 Unicode 已分配的码位，
// 其余一律写成大写十六进制。这张表就是那段判定：
// 每个区间都是「编码器不放行、上面几条又没管到」的码位，拿同一套编码器选项把
// U+0000–U+FFFF 逐个码位过一遍探出来的。Unicode 升版后要重探，否则会差几个码位。
struct CodePointRange {
    char32_t lo;
    char32_t hi;
};

constexpr CodePointRange kNotAllowedRanges[] = {
    {0x0378, 0x0379}, {0x0380, 0x0383}, {0x038B, 0x038B}, {0x038D, 0x038D},
    {0x03A2, 0x03A2}, {0x0530, 0x0530}, {0x0557, 0x0558}, {0x058B, 0x058C},
    {0x0590, 0x0590}, {0x05C8, 0x05CF}, {0x05EB, 0x05EE}, {0x05F5, 0x05FF},
    {0x070E, 0x070E}, {0x074B, 0x074C}, {0x07B2, 0x07BF}, {0x07FB, 0x07FC},
    {0x082E, 0x082F}, {0x083F, 0x083F}, {0x085C, 0x085D}, {0x085F, 0x085F},
    {0x086B, 0x086F}, {0x088F, 0x088F}, {0x0892, 0x0897}, {0x0984, 0x0984},
    {0x098D, 0x098E}, {0x0991, 0x0992}, {0x09A9, 0x09A9}, {0x09B1, 0x09B1},
    {0x09B3, 0x09B5}, {0x09BA, 0x09BB}, {0x09C5, 0x09C6}, {0x09C9, 0x09CA},
    {0x09CF, 0x09D6}, {0x09D8, 0x09DB}, {0x09DE, 0x09DE}, {0x09E4, 0x09E5},
    {0x09FF, 0x0A00}, {0x0A04, 0x0A04}, {0x0A0B, 0x0A0E}, {0x0A11, 0x0A12},
    {0x0A29, 0x0A29}, {0x0A31, 0x0A31}, {0x0A34, 0x0A34}, {0x0A37, 0x0A37},
    {0x0A3A, 0x0A3B}, {0x0A3D, 0x0A3D}, {0x0A43, 0x0A46}, {0x0A49, 0x0A4A},
    {0x0A4E, 0x0A50}, {0x0A52, 0x0A58}, {0x0A5D, 0x0A5D}, {0x0A5F, 0x0A65},
    {0x0A77, 0x0A80}, {0x0A84, 0x0A84}, {0x0A8E, 0x0A8E}, {0x0A92, 0x0A92},
    {0x0AA9, 0x0AA9}, {0x0AB1, 0x0AB1}, {0x0AB4, 0x0AB4}, {0x0ABA, 0x0ABB},
    {0x0AC6, 0x0AC6}, {0x0ACA, 0x0ACA}, {0x0ACE, 0x0ACF}, {0x0AD1, 0x0ADF},
    {0x0AE4, 0x0AE5}, {0x0AF2, 0x0AF8}, {0x0B00, 0x0B00}, {0x0B04, 0x0B04},
    {0x0B0D, 0x0B0E}, {0x0B11, 0x0B12}, {0x0B29, 0x0B29}, {0x0B31, 0x0B31},
    {0x0B34, 0x0B34}, {0x0B3A, 0x0B3B}, {0x0B45, 0x0B46}, {0x0B49, 0x0B4A},
    {0x0B4E, 0x0B54}, {0x0B58, 0x0B5B}, {0x0B5E, 0x0B5E}, {0x0B64, 0x0B65},
    {0x0B78, 0x0B81}, {0x0B84, 0x0B84}, {0x0B8B, 0x0B8D}, {0x0B91, 0x0B91},
    {0x0B96, 0x0B98}, {0x0B9B, 0x0B9B}, {0x0B9D, 0x0B9D}, {0x0BA0, 0x0BA2},
    {0x0BA5, 0x0BA7}, {0x0BAB, 0x0BAD}, {0x0BBA, 0x0BBD}, {0x0BC3, 0x0BC5},
    {0x0BC9, 0x0BC9}, {0x0BCE, 0x0BCF}, {0x0BD1, 0x0BD6}, {0x0BD8, 0x0BE5},
    {0x0BFB, 0x0BFF}, {0x0C0D, 0x0C0D}, {0x0C11, 0x0C11}, {0x0C29, 0x0C29},
    {0x0C3A, 0x0C3B}, {0x0C45, 0x0C45}, {0x0C49, 0x0C49}, {0x0C4E, 0x0C54},
    {0x0C57, 0x0C57}, {0x0C5B, 0x0C5C}, {0x0C5E, 0x0C5F}, {0x0C64, 0x0C65},
    {0x0C70, 0x0C76}, {0x0C8D, 0x0C8D}, {0x0C91, 0x0C91}, {0x0CA9, 0x0CA9},
    {0x0CB4, 0x0CB4}, {0x0CBA, 0x0CBB}, {0x0CC5, 0x0CC5}, {0x0CC9, 0x0CC9},
    {0x0CCE, 0x0CD4}, {0x0CD7, 0x0CDC}, {0x0CDF, 0x0CDF}, {0x0CE4, 0x0CE5},
    {0x0CF0, 0x0CF0}, {0x0CF4, 0x0CFF}, {0x0D0D, 0x0D0D}, {0x0D11, 0x0D11},
    {0x0D45, 0x0D45}, {0x0D49, 0x0D49}, {0x0D50, 0x0D53}, {0x0D64, 0x0D65},
    {0x0D80, 0x0D80}, {0x0D84, 0x0D84}, {0x0D97, 0x0D99}, {0x0DB2, 0x0DB2},
    {0x0DBC, 0x0DBC}, {0x0DBE, 0x0DBF}, {0x0DC7, 0x0DC9}, {0x0DCB, 0x0DCE},
    {0x0DD5, 0x0DD5}, {0x0DD7, 0x0DD7}, {0x0DE0, 0x0DE5}, {0x0DF0, 0x0DF1},
    {0x0DF5, 0x0E00}, {0x0E3B, 0x0E3E}, {0x0E5C, 0x0E80}, {0x0E83, 0x0E83},
    {0x0E85, 0x0E85}, {0x0E8B, 0x0E8B}, {0x0EA4, 0x0EA4}, {0x0EA6, 0x0EA6},
    {0x0EBE, 0x0EBF}, {0x0EC5, 0x0EC5}, {0x0EC7, 0x0EC7}, {0x0ECF, 0x0ECF},
    {0x0EDA, 0x0EDB}, {0x0EE0, 0x0EFF}, {0x0F48, 0x0F48}, {0x0F6D, 0x0F70},
    {0x0F98, 0x0F98}, {0x0FBD, 0x0FBD}, {0x0FCD, 0x0FCD}, {0x0FDB, 0x0FFF},
    {0x10C6, 0x10C6}, {0x10C8, 0x10CC}, {0x10CE, 0x10CF}, {0x1249, 0x1249},
    {0x124E, 0x124F}, {0x1257, 0x1257}, {0x1259, 0x1259}, {0x125E, 0x125F},
    {0x1289, 0x1289}, {0x128E, 0x128F}, {0x12B1, 0x12B1}, {0x12B6, 0x12B7},
    {0x12BF, 0x12BF}, {0x12C1, 0x12C1}, {0x12C6, 0x12C7}, {0x12D7, 0x12D7},
    {0x1311, 0x1311}, {0x1316, 0x1317}, {0x135B, 0x135C}, {0x137D, 0x137F},
    {0x139A, 0x139F}, {0x13F6, 0x13F7}, {0x13FE, 0x13FF}, {0x169D, 0x169F},
    {0x16F9, 0x16FF}, {0x1716, 0x171E}, {0x1737, 0x173F}, {0x1754, 0x175F},
    {0x176D, 0x176D}, {0x1771, 0x1771}, {0x1774, 0x177F}, {0x17DE, 0x17DF},
    {0x17EA, 0x17EF}, {0x17FA, 0x17FF}, {0x181A, 0x181F}, {0x1879, 0x187F},
    {0x18AB, 0x18AF}, {0x18F6, 0x18FF}, {0x191F, 0x191F}, {0x192C, 0x192F},
    {0x193C, 0x193F}, {0x1941, 0x1943}, {0x196E, 0x196F}, {0x1975, 0x197F},
    {0x19AC, 0x19AF}, {0x19CA, 0x19CF}, {0x19DB, 0x19DD}, {0x1A1C, 0x1A1D},
    {0x1A5F, 0x1A5F}, {0x1A7D, 0x1A7E}, {0x1A8A, 0x1A8F}, {0x1A9A, 0x1A9F},
    {0x1AAE, 0x1AAF}, {0x1ACF, 0x1AFF}, {0x1B4D, 0x1B4F}, {0x1B7F, 0x1B7F},
    {0x1BF4, 0x1BFB}, {0x1C38, 0x1C3A}, {0x1C4A, 0x1C4C}, {0x1C89, 0x1C8F},
    {0x1CBB, 0x1CBC}, {0x1CC8, 0x1CCF}, {0x1CFB, 0x1CFF}, {0x1F16, 0x1F17},
    {0x1F1E, 0x1F1F}, {0x1F46, 0x1F47}, {0x1F4E, 0x1F4F}, {0x1F58, 0x1F58},
    {0x1F5A, 0x1F5A}, {0x1F5C, 0x1F5C}, {0x1F5E, 0x1F5E}, {0x1F7E, 0x1F7F},
    {0x1FB5, 0x1FB5}, {0x1FC5, 0x1FC5}, {0x1FD4, 0x1FD5}, {0x1FDC, 0x1FDC},
    {0x1FF0, 0x1FF1}, {0x1FF5, 0x1FF5}, {0x1FFF, 0x1FFF}, {0x2065, 0x2065},
    {0x2072, 0x2073}, {0x208F, 0x208F}, {0x209D, 0x209F}, {0x20C1, 0x20CF},
    {0x20F1, 0x20FF}, {0x218C, 0x218F}, {0x2427, 0x243F}, {0x244B, 0x245F},
    {0x2B74, 0x2B75}, {0x2B96, 0x2B96}, {0x2CF4, 0x2CF8}, {0x2D26, 0x2D26},
    {0x2D28, 0x2D2C}, {0x2D2E, 0x2D2F}, {0x2D68, 0x2D6E}, {0x2D71, 0x2D7E},
    {0x2D97, 0x2D9F}, {0x2DA7, 0x2DA7}, {0x2DAF, 0x2DAF}, {0x2DB7, 0x2DB7},
    {0x2DBF, 0x2DBF}, {0x2DC7, 0x2DC7}, {0x2DCF, 0x2DCF}, {0x2DD7, 0x2DD7},
    {0x2DDF, 0x2DDF}, {0x2E5E, 0x2E7F}, {0x2E9A, 0x2E9A}, {0x2EF4, 0x2EFF},
    {0x2FD6, 0x2FEF}, {0x2FFC, 0x2FFF}, {0x3040, 0x3040}, {0x3097, 0x3098},
    {0x3100, 0x3104}, {0x3130, 0x3130}, {0x318F, 0x318F}, {0x31E4, 0x31EF},
    {0x321F, 0x321F}, {0xA48D, 0xA48F}, {0xA4C7, 0xA4CF}, {0xA62C, 0xA63F},
    {0xA6F8, 0xA6FF}, {0xA7CB, 0xA7CF}, {0xA7D2, 0xA7D2}, {0xA7D4, 0xA7D4},
    {0xA7DA, 0xA7F1}, {0xA82D, 0xA82F}, {0xA83A, 0xA83F}, {0xA878, 0xA87F},
    {0xA8C6, 0xA8CD}, {0xA8DA, 0xA8DF}, {0xA954, 0xA95E}, {0xA97D, 0xA97F},
    {0xA9CE, 0xA9CE}, {0xA9DA, 0xA9DD}, {0xA9FF, 0xA9FF}, {0xAA37, 0xAA3F},
    {0xAA4E, 0xAA4F}, {0xAA5A, 0xAA5B}, {0xAAC3, 0xAADA}, {0xAAF7, 0xAB00},
    {0xAB07, 0xAB08}, {0xAB0F, 0xAB10}, {0xAB17, 0xAB1F}, {0xAB27, 0xAB27},
    {0xAB2F, 0xAB2F}, {0xAB6C, 0xAB6F}, {0xABEE, 0xABEF}, {0xABFA, 0xABFF},
    {0xD7A4, 0xD7AF}, {0xD7C7, 0xD7CA}, {0xD7FC, 0xDFFF}, {0xFA6E, 0xFA6F},
    {0xFADA, 0xFAFF}, {0xFB07, 0xFB12}, {0xFB18, 0xFB1C}, {0xFB37, 0xFB37},
    {0xFB3D, 0xFB3D}, {0xFB3F, 0xFB3F}, {0xFB42, 0xFB42}, {0xFB45, 0xFB45},
    {0xFBC3, 0xFBD2}, {0xFD90, 0xFD91}, {0xFDC8, 0xFDCE}, {0xFDD0, 0xFDEF},
    {0xFE1A, 0xFE1F}, {0xFE53, 0xFE53}, {0xFE67, 0xFE67}, {0xFE6C, 0xFE6F},
    {0xFE75, 0xFE75}, {0xFEFD, 0xFEFE}, {0xFF00, 0xFF00}, {0xFFBF, 0xFFC1},
    {0xFFC8, 0xFFC9}, {0xFFD0, 0xFFD1}, {0xFFD8, 0xFFD9}, {0xFFDD, 0xFFDF},
    {0xFFE7, 0xFFE7}, {0xFFEF, 0xFFF8}, {0xFFFE, 0xFFFF},
};

bool is_not_allowed(char32_t value) {
    std::size_t lo = 0;
    std::size_t hi = sizeof(kNotAllowedRanges) / sizeof(kNotAllowedRanges[0]);
    while (lo < hi) {
        const std::size_t mid = lo + (hi - lo) / 2;
        if (value < kNotAllowedRanges[mid].lo) {
            hi = mid;
        } else if (value > kNotAllowedRanges[mid].hi) {
            lo = mid + 1;
        } else {
            return true;
        }
    }
    return false;
}

void append_hex4(std::string& out, char32_t value, bool upper) {
    static const char* const kLower = "0123456789abcdef";
    static const char* const kUpper = "0123456789ABCDEF";
    const char* const digits = upper ? kUpper : kLower;
    out.append("\\u");
    out.push_back(digits[(value >> 12) & 0xF]);
    out.push_back(digits[(value >> 8) & 0xF]);
    out.push_back(digits[(value >> 4) & 0xF]);
    out.push_back(digits[value & 0xF]);
}

// ---------------------------------------------------------------------------
// 人读报告里的片段
// ---------------------------------------------------------------------------

// 片段里给人看的样子：隐形字符写成码位，换行折成一个空格，连着几个空白只留一个。
// 一个片段里跨了段落时，读者仍要能读成一句。
std::string visible(std::string_view text) {
    std::string out;
    out.reserve(text.size());
    for (std::size_t at = 0; at < text.size();) {
        const CodePoint cp = decode_utf8(text, at);
        at += cp.bytes;
        if (cp.value == '\r' || cp.value == '\n' || cp.value == '\t') {
            if (out.empty() || out.back() != ' ') out.push_back(' ');
            continue;
        }
        if (is_invisible(cp.value) || cp.value < 0x20)
            append_hex4(out, cp.value, false);
        else
            append_utf8(out, cp.value);
    }
    return out;
}

// 行尾收成 LF：命中说明与规则表里的文案可能自带 CRLF，那些换行也要跟着本工作区一律 LF。
std::string to_lf(std::string_view text) {
    if (text.find('\r') == std::string::npos) return std::string(text);
    std::string out;
    out.reserve(text.size());
    for (std::size_t i = 0; i < text.size(); ++i) {
        if (text[i] == '\r' && i + 1 < text.size() && text[i + 1] == '\n') continue;
        out.push_back(text[i]);
    }
    return out;
}

// ---------------------------------------------------------------------------
// JSON 的字符串转义
//
// JSON 的转义就这四条，逐字节一致靠它：
//   * HTML 敏感字符与 + 写成大写十六进制（" 也走这条，不写成 \"）；
//   * 控制符、DEL 与 C1、普通空格以外的空白、行与段分隔符、自用区、未分配码位与
//     非字符（U+FFFE、U+FFFF 这些）都写成大写十六进制；
//   * BMP 之外的字一律写成一对代理项，补充自用区也在其中：写成截到低位的 \u0000
//     等于把一个字换成空字符，JSON 解回去就丢了；
//   * 隐形字符那 9 个写小写十六进制：它们连 CRLF 一起由输出收尾那一步改写。
// ---------------------------------------------------------------------------

void append_json_escaped(std::string& out, std::string_view text) {
    for (std::size_t at = 0; at < text.size();) {
        const CodePoint cp = decode_utf8(text, at);
        at += cp.bytes;
        const char32_t c = cp.value;

        if (c == '\\') {
            out.append("\\\\");
            continue;
        }
        if (c < 0x20) {
            switch (c) {
                case 0x08: out.append("\\b"); continue;
                case 0x09: out.append("\\t"); continue;
                case 0x0A: out.append("\\n"); continue;
                case 0x0C: out.append("\\f"); continue;
                case 0x0D: out.append("\\r"); continue;
                default: append_hex4(out, c, true); continue;
            }
        }
        // 补充区先判：下面几条只认 BMP 内的取值范围，让它们先跑过一遍，
        // 补充私有区的字会被当成 BMP 私有区截掉高位
        if (c >= 0x10000) {
            const char32_t rest = c - 0x10000;
            append_hex4(out, 0xD800 + (rest >> 10), true);
            append_hex4(out, 0xDC00 + (rest & 0x3FF), true);
            continue;
        }
        const bool html_sensitive = c == '"' || c == '&' || c == '\'' || c == '+' ||
                                    c == '<' || c == '>' || c == '`';
        const bool narrow_control = (c >= 0x7F && c <= 0x9F);
        if (html_sensitive || narrow_control || is_exotic_space(c) || c == 0xFEFF ||
            is_private_use(c) || is_not_allowed(c)) {
            append_hex4(out, c, true);
            continue;
        }
        if (is_invisible(c)) {
            append_hex4(out, c, false);
            continue;
        }
        append_utf8(out, c);
    }
}

// ---------------------------------------------------------------------------
// 命中里的下标与区间换成字符串
//
// 命中只带规则下标与字节区间。标题、说明、建议这些从规则表取，命中原文按区间从稿子里切，
// 展示片段现切：一处只切一处用的那几十字节，不预先给十几万处各存一份。
// ---------------------------------------------------------------------------

std::string_view id_of(const HitFields& fields, const LintHit& hit) {
    return fields.rule_of(hit).id;
}

std::string_view title_of(const HitFields& fields, const LintHit& hit) {
    return fields.rule_of(hit).title;
}

std::string_view group_of(const HitFields& fields, const LintHit& hit) {
    return fields.rule_of(hit).group;
}

std::string_view severity_of(const HitFields& fields, const LintHit& hit) {
    return fields.rule_of(hit).severity;
}

// 展示片段：命中前后各 8 个码元，两端被截断时各补一个省略号。
// 窗口按 UTF-16 码元数定，走的时候按码点算字节，两边数的是同一段：
// 增补平面的字算两个码元，窗口正好切在它中间时把这个字整个带上，不劈开代理对。
constexpr std::size_t kSnippetPad = 8;

// 往前退 units 个码元，返回落点；不够退就停在 0。
// 最后一个字跨过窗口边界时整个带上：落在低代理上就往前退一格，回到这个字的开头。
std::size_t bytes_before(std::string_view text, std::size_t at, std::size_t units) {
    std::size_t taken = 0;
    while (at > 0 && taken < units) {
        std::size_t back = at - 1;
        while (back > 0 && (static_cast<unsigned char>(text[back]) & 0xC0) == 0x80) --back;
        const CodePoint cp = decode_utf8(text, back);
        if (back + cp.bytes != at) return at;  // 起点不落在字上：不动，免得切出半个字
        taken += cp.bytes == 4 ? 2 : 1;
        at = back;
    }
    return at;
}

// 往后走 units 个码元，返回落点；走到末尾就停在末尾。
std::size_t bytes_after(std::string_view text, std::size_t at, std::size_t units) {
    std::size_t taken = 0;
    while (at < text.size() && taken < units) {
        const CodePoint cp = decode_utf8(text, at);
        taken += cp.bytes == 4 ? 2 : 1;
        at += cp.bytes;
    }
    return at;
}

// 命中起点落在字中间时按该字的起点算：字节区间是引擎给的，不该切出半个字。
std::size_t align_to_char_start(std::string_view text, std::size_t at) {
    while (at > 0 && at < text.size() && (static_cast<unsigned char>(text[at]) & 0xC0) == 0x80) --at;
    return at;
}

std::string snippet_of(const ReportInput& input, const LintHit& hit) {
    const HitFields fields = input.fields();
    const std::string_view ready = fields.ready_snippet_of(hit);
    if (!ready.empty()) return std::string(ready);

    const std::string_view text = input.text;
    if (hit.begin >= text.size() || hit.begin > hit.end) return std::string();
    const std::size_t begin_at = align_to_char_start(text, hit.begin);
    const std::size_t length = hit.length;
    const std::size_t window = hit.snippet_length == 0 ? length : std::min(length, std::size_t{hit.snippet_length});

    const std::size_t begin = bytes_before(text, begin_at, kSnippetPad);
    const std::size_t end = bytes_after(text, begin_at, window + kSnippetPad);

    std::string out;
    if (begin > 0) out.append("\xE2\x80\xA6");  // …
    out.append(text.substr(begin, end - begin));
    if (end < text.size()) out.append("\xE2\x80\xA6");
    return out;
}

// ---------------------------------------------------------------------------
// 人读报告
// ---------------------------------------------------------------------------

// 折叠只在处数够多时才有意义：两条命中也折叠，读者连样例都看不出来。
constexpr std::size_t kCollapseMinHits = 10;
constexpr std::size_t kCollapseSamples = 3;

double density(std::size_t hits, int chars) {
    return chars <= 0 ? static_cast<double>(hits) : static_cast<double>(hits) * 1000.0 / chars;
}

// 每千字处数，取一位小数只是为了显示。取整按「四舍五入、半数离开零」。
std::string format_density(double value) {
    const double scaled = std::round(value * 10.0);
    const auto tenths = static_cast<long long>(scaled);
    return std::to_string(tenths / 10) + "." + std::to_string(tenths % 10);
}

// 该不该折叠：规则表里开了折叠密度、处数不少于门槛、密度达到阈值。
// 只看处数不看密度会让长篇必然折叠，只看密度不看处数会让短文本的两处也折叠。
bool should_collapse(const ReportInput& input, std::string_view rule_id, std::size_t hits) {
    const auto at = input.collapse_at_density.find(std::string(rule_id));
    if (at == input.collapse_at_density.end()) return false;
    return hits >= kCollapseMinHits && density(hits, input.total_scanned) >= at->second;
}

// 一条规则的真实处数：封顶之后 hits 里只留了前几处，组头与密度分母不能用条数。
std::size_t rule_hits_of(const ReportInput& input, std::uint16_t rule) {
    if (rule < input.rule_hits.size()) return input.rule_hits[rule];
    std::size_t count = 0;
    for (const LintHit& hit : input.hits)
        if (hit.rule == rule) ++count;
    return count;
}

// 整份稿子的真实处数：人读报告开头那句与日志汇总行都写它，不是列出来的条数。
std::size_t total_hits_of(const ReportInput& input) {
    if (input.rule_hits.empty()) return input.hits.size();
    std::size_t total = 0;
    for (const std::size_t count : input.rule_hits) total += count;
    return total;
}

// 截断说明：某条规则的真实处数比列出来的多，就在它的附注里补一句。
// 补在最后：附注的次序对外有约定。一条规则的新增处数写在「相对基线新增 N 处」这一句里，
// 下游按「这条规则的第一条附注」取那个整数，所以那一句必须排在这句前面；
// 截断这句里的第一个整数是「没列出来的处数」，排后一位，不会被当成新增处数。
void append_truncation_notes(ReportInput& report) {
    if (report.rules == nullptr || report.rule_hits.empty()) return;
    std::vector<std::size_t> listed(report.rules->size(), 0);
    for (const LintHit& hit : report.hits)
        if (hit.rule < listed.size()) ++listed[hit.rule];
    for (std::size_t i = 0; i < report.rules->size(); ++i) {
        const std::size_t total = report.rule_hits[i];
        if (total <= listed[i]) continue;
        report.notes.push_back({(*report.rules)[i].id,
                                "另有 " + std::to_string(total - listed[i]) +
                                    " 处未列出（同一条规则每份稿最多列 " +
                                    std::to_string(kMaxListedHitsPerRule) + " 处）"});
    }
}

// 一条命中一行，七个空格缩进；片段、提示、说明、方案依次挂在后面，没有的就不写。
template <class Out>
void append_hit_line(Out& out, const ReportInput& input, const LintHit& hit) {
    const HitFields fields = input.fields();
    out.append("       ");
    out.append("第 ");
    out.append(std::to_string(hit.line));
    out.append(" 行 第 ");
    out.append(std::to_string(hit.column));
    out.append(" 列");
    out.append("  ");
    out.append(visible(snippet_of(input, hit)));
    if (const auto hint = fields.hint_of(hit)) {
        out.append("  <- ");
        out.append(to_lf(*hint));
    }
    const std::string_view detail = fields.detail_of(hit);
    if (!detail.empty()) {
        out.append("  <- ");
        out.append(to_lf(detail));
    }
    if (const auto scheme = fields.scheme_of(hit)) {
        out.append("  <- 可套用方案：");
        out.append(to_lf(*scheme));
    }
    out.push_back('\n');
}

struct Group {
    std::size_t rule = 0;
    std::vector<const LintHit*> hits;
};

// 同一规则算一组，组的先后按第一次出现的顺序；组内按行列升序。
// 全表排序不在这里：调用方先按行列排好（见 report.h），组序因此是「各组首条命中的位置顺序」。
// 一条规则只有一种标题，按规则下标分组与按「编号 + 标题」分组是同一件事。
std::vector<Group> group_hits(const std::vector<LintHit>& hits) {
    std::vector<Group> groups;
    std::unordered_map<std::uint16_t, std::size_t> index;
    for (const auto& hit : hits) {
        auto at = index.find(hit.rule);
        if (at == index.end()) {
            index.emplace(hit.rule, groups.size());
            groups.push_back({hit.rule, {}});
            groups.back().hits.push_back(&hit);
        } else {
            groups[at->second].hits.push_back(&hit);
        }
    }
    // 组内再排一次：同一规则的多条 Pattern 各扫一遍时，行号会来回跳
    for (auto& group : groups) {
        std::stable_sort(group.hits.begin(), group.hits.end(), [](const LintHit* a, const LintHit* b) {
            if (a->line != b->line) return a->line < b->line;
            return a->column < b->column;
        });
    }
    return groups;
}

// 输出缓冲：攒够一块就交给口子。报告按命中逐行写，长稿的报告不整份留在内存里。
// 整份返回的那个版本直接写 std::string，两条路共用下面这份排版逻辑。
class ChunkWriter {
public:
    ChunkWriter(ReportSink sink, void* context) : sink_(sink), context_(context) {}

    void append(std::string_view text) {
        buffer_.append(text);
        trim();
    }
    void push_back(char c) {
        buffer_.push_back(c);
        trim();
    }
    void flush() {
        if (buffer_.empty()) return;
        sink_(buffer_, context_);
        buffer_.clear();
    }

private:
    void trim() {
        if (buffer_.size() >= kChunkBytes) flush();
    }

    static constexpr std::size_t kChunkBytes = 128 * 1024;

    ReportSink sink_;
    void* context_;
    std::string buffer_;
};

template <class Out>
void render_text(const ReportInput& input, Out& out) {
    const HitFields fields = input.fields();
    const std::size_t found = total_hits_of(input);

    // 给了写前那一份时先说清下面这些数是跟谁比的：只列新增位置时，读者第一眼会以为命中变少了
    if (input.baseline_hits) {
        out.append("相对基线新增 ");
        out.append(std::to_string(found));
        out.append(" 处，基线 ");
        out.append(std::to_string(*input.baseline_hits));
        out.append(" 处\n");
    }

    if (!input.hits.empty()) {
        for (const auto& group : group_hits(input.hits)) {
            const std::string_view rule_id = id_of(fields, *group.hits.front());
            // 组头与密度写真实处数：封顶只管列几处，不管数了几处
            const std::size_t total = rule_hits_of(input, group.rule);
            const bool collapsed = should_collapse(input, rule_id, total);
            // 组头只写标题：规则编号是内部编号，只在 --json 与 --only 里用
            out.append("[发现] ");
            out.append(title_of(fields, *group.hits.front()));
            out.append("（");
            out.append(std::to_string(total));
            out.append(" 处");
            if (collapsed) {
                out.append("，每千字 ");
                out.append(format_density(density(total, input.total_scanned)));
                out.append(" 处）：同一句式反复出现，只列几处样例");
            } else {
                out.append("）");
            }
            out.push_back('\n');

            const std::size_t listed = collapsed ? std::min(kCollapseSamples, group.hits.size())
                                                 : group.hits.size();
            for (std::size_t i = 0; i < listed; ++i) append_hit_line(out, input, *group.hits[i]);

            if (collapsed) {
                out.append("       其余 ");
                out.append(std::to_string(total - listed));
                out.append(" 处未逐条列出");
                // 封顶之后手上不是全部位置，这一句就说不起了（截断说明在统计段）
                if (total <= group.hits.size()) out.append("，位置一条不少");
                out.push_back('\n');
            }
        }
    } else {
        out.append("[通过] 没有发现可疑的写法\n");
    }

    if (!input.notes.empty()) {
        out.append("[统计]\n");
        for (const auto& note : input.notes) {
            out.append("       - ");
            out.append(note.text);
            out.push_back('\n');
        }
    }

    // 分段方式按人话说：line / markdown 是程序里的取值，读者不需要看到
    const std::string mode_label =
        input.paragraph_mode == "markdown" ? "按段落分段" : "按行分段";
    out.append("[说明] 全文 ");
    out.append(std::to_string(input.total_scanned));
    out.append(" 字，发现 ");
    out.append(std::to_string(found));
    out.append(" 处，另有 ");
    out.append(std::to_string(input.notes.size()));
    out.append(" 条统计；");
    out.append(mode_label);
    out.append("。这个工具只指出位置，不改你的字。\n");
}

void append_indent(std::string& out, int level) { out.append(static_cast<std::size_t>(level) * 2, ' '); }

void append_json_string(std::string& out, std::string_view value) {
    out.push_back('"');
    append_json_escaped(out, value);
    out.push_back('"');
}

// "键": 值。逗号与缩进由调用方给，这样字段次序一眼能对上契约。
template <class Out>
void append_field(Out& out, int level, const char* key, const std::string& rendered, bool comma) {
    std::string indent;
    append_indent(indent, level);
    out.append(indent);
    out.push_back('"');
    out.append(key);
    out.append("\": ");
    out.append(rendered);
    if (comma) out.push_back(',');
    out.push_back('\n');
}

std::string json_string(std::string_view value) {
    std::string out;
    append_json_string(out, value);
    return out;
}

// 没写这一项时 JSON 里写 null，写成本这里的空串在 JSON 里是两样。
std::string json_optional_string(const std::optional<std::string_view>& value) {
    return value ? json_string(*value) : std::string("null");
}

template <class Out>
void render_json(const ReportInput& input, Out& out) {
    const HitFields fields = input.fields();
    out.append("{\n");
    append_field(out, 1, "Version", "4", true);
    out.append("  \"Reports\": [\n");
    out.append("    {\n");

    append_field(out, 3, "File", json_string(input.source_name), true);
    append_field(out, 3, "Chars", std::to_string(input.total_scanned), true);
    append_field(out, 3, "ParagraphMode", json_string(input.paragraph_mode), true);

    if (input.hits.empty()) {
        append_field(out, 3, "Hits", "[]", true);
    } else {
        out.append("      \"Hits\": [\n");
        for (std::size_t i = 0; i < input.hits.size(); ++i) {
            const LintHit& hit = input.hits[i];
            out.append("        {\n");
            append_field(out, 5, "Id", json_string(id_of(fields, hit)), true);
            append_field(out, 5, "Group", json_string(group_of(fields, hit)), true);
            append_field(out, 5, "Title", json_string(title_of(fields, hit)), true);
            append_field(out, 5, "Severity", json_string(severity_of(fields, hit)), true);
            append_field(out, 5, "Line", std::to_string(hit.line), true);
            append_field(out, 5, "Col", std::to_string(hit.column), true);
            append_field(out, 5, "Length", std::to_string(hit.length), true);
            append_field(out, 5, "Match", json_string(fields.match_of(hit)), true);
            append_field(out, 5, "Snippet", json_string(snippet_of(input, hit)), true);
            append_field(out, 5, "Detail", json_string(fields.detail_of(hit)), true);
            append_field(out, 5, "Hint", json_optional_string(fields.hint_of(hit)), true);
            append_field(out, 5, "SuggestScheme", json_optional_string(fields.scheme_of(hit)), false);
            out.append(i + 1 < input.hits.size() ? "        },\n" : "        }\n");
        }
        out.append("      ],\n");
    }

    if (input.notes.empty()) {
        append_field(out, 3, "Notes", "[]", input.baseline_hits.has_value());
    } else {
        out.append("      \"Notes\": [\n");
        for (std::size_t i = 0; i < input.notes.size(); ++i) {
            const LintNote& note = input.notes[i];
            out.append("        {\n");
            append_field(out, 5, "Id", json_string(note.rule_id), true);
            append_field(out, 5, "Text", json_string(note.text), false);
            out.append(i + 1 < input.notes.size() ? "        },\n" : "        }\n");
        }
        out.append(input.baseline_hits.has_value() ? "      ],\n" : "      ]\n");
    }

    // 没给写前那一份时整个字段不出现：不带 --baseline 的调用输出与升版前一致
    if (input.baseline_hits)
        append_field(out, 3, "BaselineHits", std::to_string(*input.baseline_hits), false);

    out.append("    }\n");
    out.append("  ]\n");
    out.append("}\n");
}

// ---------------------------------------------------------------------------
// 命中日志

// 日志里的一个码元写成 \uXXXX，小写十六进制。增补平面的字拆成两个代理码元。
void append_log_hex(std::string& out, char32_t value) {
    static constexpr char kDigits[] = "0123456789abcdef";
    out.append("\\u");
    out.push_back(kDigits[(value >> 12) & 0xF]);
    out.push_back(kDigits[(value >> 8) & 0xF]);
    out.push_back(kDigits[(value >> 4) & 0xF]);
    out.push_back(kDigits[value & 0xF]);
}

// 老控制台画得出来的字：ASCII 可打印区、汉字与全角标点。别的字符一律转义。
bool log_drawable(char32_t value) {
    return (value >= 0x20 && value <= 0x7E) || (value >= 0x3000 && value <= 0x303F) ||
           (value >= 0x3400 && value <= 0x4DBF) || (value >= 0x4E00 && value <= 0x9FFF) ||
           (value >= 0xF900 && value <= 0xFAFF) || (value >= 0xFF01 && value <= 0xFF5E) ||
           (value >= 0xFFE0 && value <= 0xFFE6);
}

void append_log_string(std::string& out, std::string_view value) {
    out.push_back('"');
    for (std::size_t at = 0; at < value.size();) {
        const CodePoint cp = decode_utf8(value, at);
        at += cp.bytes;
        switch (cp.value) {
            case '"': out.append("\\\""); continue;
            case '\\': out.append("\\\\"); continue;
            // 命中可以跨行，原文里的换行要转义，否则一条命中会被拆成两行 JSON
            case '\n': out.append("\\n"); continue;
            case '\r': out.append("\\r"); continue;
            case '\t': out.append("\\t"); continue;
            default: break;
        }
        if (log_drawable(cp.value)) {
            append_utf8(out, cp.value);
            continue;
        }
        if (cp.value < 0x10000) {
            append_log_hex(out, cp.value);
            continue;
        }
        const char32_t rest = cp.value - 0x10000;
        append_log_hex(out, 0xD800 + (rest >> 10));
        append_log_hex(out, 0xDC00 + (rest & 0x3FF));
    }
    out.push_back('"');
}

template <class Out>
void render_log(const ReportInput& input, Out& out) {
    const HitFields fields = input.fields();
    std::string line;

    line.append("{\"Kind\":\"Report\",\"File\":");
    append_log_string(line, input.source_name);
    line.append(",\"Chars\":");
    line.append(std::to_string(input.total_scanned));
    line.append(",\"ParagraphMode\":");
    append_log_string(line, input.paragraph_mode);
    line.append(",\"Hits\":");
    line.append(std::to_string(total_hits_of(input)));
    line.append("}\n");
    out.append(line);

    for (const LintHit& hit : input.hits) {
        line.clear();
        line.append("{\"Kind\":\"Hit\",\"File\":");
        append_log_string(line, input.source_name);
        line.append(",\"Line\":");
        line.append(std::to_string(hit.line));
        line.append(",\"Col\":");
        line.append(std::to_string(hit.column));
        line.append(",\"Length\":");
        line.append(std::to_string(hit.length));
        line.append(",\"Id\":");
        append_log_string(line, id_of(fields, hit));
        line.append(",\"Severity\":");
        append_log_string(line, severity_of(fields, hit));
        line.append(",\"Match\":");
        append_log_string(line, fields.match_of(hit));
        line.append(",\"Snippet\":");
        append_log_string(line, snippet_of(input, hit));
        line.append("}\n");
        out.append(line);
    }
}

}  // namespace

void cap_report_hits(ReportInput& report) {
    if (report.rules == nullptr) return;
    const std::size_t rule_count = report.rules->size();
    if (report.rule_hits.size() != rule_count) {
        report.rule_hits.assign(rule_count, 0);
        for (const LintHit& hit : report.hits)
            if (hit.rule < rule_count) ++report.rule_hits[hit.rule];
    }

    std::vector<std::size_t> listed(rule_count, 0);
    std::size_t keep = 0;
    for (std::size_t i = 0; i < report.hits.size(); ++i) {
        const LintHit& hit = report.hits[i];
        if (hit.rule < rule_count) {
            if (listed[hit.rule] >= kMaxListedHitsPerRule) continue;
            ++listed[hit.rule];
        }
        if (keep != i) report.hits[keep] = hit;
        ++keep;
    }
    report.hits.resize(keep);
}

std::map<std::string, int> collapse_thresholds(const std::vector<LintRule>& rules) {
    std::map<std::string, int> out;
    for (const auto& rule : rules)
        if (rule.collapse_at_density) out[rule.id] = *rule.collapse_at_density;
    return out;
}

ReportInput make_report(std::string source_name, LintOutcome&& outcome,
                        const std::map<std::string, int>& collapse, bool cap_hits) {
    // 封顶：单扫一份稿子时扫描那一层已经做过，带 --baseline 的那一趟到这里才做
    // （净增位置先裁出来，封顶排在裁之后）。做过一次再调一次什么也不改。
    // 要写全量日志的那一趟先不封顶，日志写完再调 cap_report_hits。
    if (cap_hits) cap_listed_hits(outcome);

    ReportInput report;
    report.source_name = std::move(source_name);
    report.total_scanned = static_cast<int>(outcome.units);
    report.paragraph_mode = std::move(outcome.paragraph_mode);
    report.collapse_at_density = collapse;
    report.rules = outcome.rules;
    report.text = outcome.text;

    // 统计项由算法类规则算：每条附注带自己那条规则的编号，JSON 里按它写出去。
    report.notes = std::move(outcome.notes);
    report.extras = std::move(outcome.extras);
    report.arena = std::move(outcome.arena);
    report.hits = std::move(outcome.hits);
    report.rule_hits = std::move(outcome.rule_hits);
    report.baseline_hits = outcome.baseline_hits;
    append_truncation_notes(report);
    return report;
}

std::string render_text_report(const ReportInput& input) {
    std::string out;
    render_text(input, out);
    return out;
}

void render_text_report(const ReportInput& input, ReportSink sink, void* context) {
    ChunkWriter out(sink, context);
    render_text(input, out);
    out.flush();
}

std::string render_json_report(const ReportInput& input) {
    std::string out;
    render_json(input, out);
    return out;
}

void render_json_report(const ReportInput& input, ReportSink sink, void* context) {
    ChunkWriter out(sink, context);
    render_json(input, out);
    out.flush();
}

std::string render_hit_log(const ReportInput& input) {
    std::string out;
    render_log(input, out);
    return out;
}

void render_hit_log(const ReportInput& input, ReportSink sink, void* context) {
    ChunkWriter out(sink, context);
    render_log(input, out);
    out.flush();
}

int exit_code_for(const ReportInput& input) { return input.hits.empty() ? 0 : 1; }

std::size_t total_hits(const ReportInput& input) { return total_hits_of(input); }

std::string json_escape(std::string_view value) {
    std::string out;
    out.reserve(value.size() + 8);
    append_json_escaped(out, value);
    return out;
}

}  // namespace texttool
