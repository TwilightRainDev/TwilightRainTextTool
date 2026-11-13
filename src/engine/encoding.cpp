// 编码的检测与转换。码表交给系统：Linux 用 glibc 的 iconv（GBK 走 GB18030，
// 再按补丁表把 cp936 与 GB18030 不一致的私用区字改回来），Windows 用
// MultiByteToWideChar / WideCharToMultiByte，两边各写一份，接口在 encoding.h。
//
// 检测规则、坏字节的补法、分块的切点两个平台共用同一份代码，两边只差「查码表」这一步。
//
// 内存：分块接口（ChunkedDecoder / ChunkedEncoder）一次只转一块，源字节不会整份留在内存里；
// 整份的 to_utf8 / from_utf8 仍要「源 + 结果」两份，调用方要控峰值就用分块接口。
#include "engine/encoding.h"

#include <cstddef>
#include <string>

#if defined(_WIN32)
#include <windows.h>
#else
#include <cerrno>
#include <iconv.h>
#include <stdexcept>
#endif

namespace texttool {
namespace {

// 各种 BOM 与替换字符。
constexpr std::string_view kUtf8Bom = "\xEF\xBB\xBF";
constexpr std::string_view kUtf16LeBom = "\xFF\xFE";
constexpr std::string_view kUtf16BeBom = "\xFE\xFF";
constexpr std::string_view kReplacementChar = "\xEF\xBF\xBD";  // U+FFFD

// cp936 不带 BOM，有两个字节要单独认：0x80 是欧元符号，
// 0xFF 单字节无效，解成私用区 U+F8F5（.NET 的最佳匹配回退就给这个码位）。
constexpr std::string_view kGbkEuroSign = "\xE2\x82\xAC";    // U+20AC
constexpr std::string_view kGbkInvalidByte = "\xEF\xA3\xB5";  // U+F8F5

// 次字节不合法、末尾只剩半个字、目标编码写不出来时补的字符。
constexpr std::string_view kGbkFallback = "?";

// 检测只看这么长的头部。
constexpr std::size_t kProbeBytes = 4096;

bool starts_with(std::string_view bytes, std::string_view prefix) {
    return bytes.size() >= prefix.size() && bytes.compare(0, prefix.size(), prefix) == 0;
}

std::string_view strip_prefix(std::string_view bytes, std::string_view prefix) {
    return starts_with(bytes, prefix) ? bytes.substr(prefix.size()) : bytes;
}

std::string_view bom_of(TextEncoding encoding) {
    switch (encoding) {
        case TextEncoding::Utf8Bom:
            return kUtf8Bom;
        case TextEncoding::Utf16Le:
            return kUtf16LeBom;
        case TextEncoding::Utf16Be:
            return kUtf16BeBom;
        case TextEncoding::Utf8:
        case TextEncoding::Gbk:
            break;
    }
    return {};
}

// 去掉 BOM 之后真正的编码：解 GBK 与 UTF-8 的正文时按这个走。
TextEncoding base_encoding(TextEncoding encoding) {
    return encoding == TextEncoding::Utf8Bom ? TextEncoding::Utf8 : encoding;
}

// ================================================================
//  UTF-8 的逐字扫描：检测合法性与补替换字符共用这一份规则
// ================================================================

// 从 p 起解一个 UTF-8 字符。
//   length   这次要跳过几个字节。合法字符是整字长度；坏字节是「最大合法子部分」的长度，
//            与 .NET 的 UTF-8 解码器补替换字符的做法一致（E4 B8 后面接非续字节算一个坏字，
//            只补一个 U+FFFD，然后从那个非续字节接着解）。
//   complete 这个字需要的字节都在缓冲里；false 表示缓冲到头了，序列被截断。
struct Utf8Char {
    std::size_t length = 1;
    bool complete = false;
    bool valid = false;
};

Utf8Char scan_utf8(const unsigned char* p, std::size_t avail) {
    const unsigned char lead = p[0];
    if (lead < 0x80) return {1, true, true};

    // need 是后面还要几个续字节；lo/hi 是第一个续字节的允许范围，
    // 这三个窄范围把过长编码、代理区、超出 U+10FFFF 的序列挡在外面。
    int need = 0;
    unsigned char lo = 0x80;
    unsigned char hi = 0xBF;

    if (lead >= 0xC2 && lead <= 0xDF) {
        need = 1;
    } else if (lead == 0xE0) {
        need = 2;
        lo = 0xA0;
    } else if (lead >= 0xE1 && lead <= 0xEC) {
        need = 2;
    } else if (lead == 0xED) {
        need = 2;
        hi = 0x9F;
    } else if (lead >= 0xEE && lead <= 0xEF) {
        need = 2;
    } else if (lead == 0xF0) {
        need = 3;
        lo = 0x90;
    } else if (lead >= 0xF1 && lead <= 0xF3) {
        need = 3;
    } else if (lead == 0xF4) {
        need = 3;
        hi = 0x8F;
    } else {
        // 0x80..0xC1（孤立的续字节、过长编码的首字节）与 0xF5..0xFF。
        return {1, true, false};
    }

    std::size_t used = 1;
    for (int i = 0; i < need; ++i) {
        if (used >= avail) return {used, false, false};
        const unsigned char next = p[used];
        const unsigned char min = i == 0 ? lo : static_cast<unsigned char>(0x80);
        const unsigned char max = i == 0 ? hi : static_cast<unsigned char>(0xBF);
        if (next < min || next > max) return {used, true, false};
        ++used;
    }
    return {used, true, true};
}

// 整份字节是不是合法 UTF-8。truncated_tail_ok 为真时，末尾那个被切断的字不算错：
// 判文件头时截断是切在 4KB 边界上造成的，这一截先剪掉再判。
bool is_valid_utf8(std::string_view bytes, bool truncated_tail_ok) {
    const auto* p = reinterpret_cast<const unsigned char*>(bytes.data());
    std::size_t at = 0;
    while (at < bytes.size()) {
        const Utf8Char ch = scan_utf8(p + at, bytes.size() - at);
        if (!ch.complete) return truncated_tail_ok;  // 截断只可能出现在末尾
        if (!ch.valid) return false;
        at += ch.length;
    }
    return true;
}

// 把坏字节换成 U+FFFD，合法部分原样留下。截断的尾巴也在这里补成替换字符，
// 所以解出来的结果一定合法。先算长度再填，不靠扩容。
std::string with_replacement_chars(std::string_view bytes) {
    const auto* p = reinterpret_cast<const unsigned char*>(bytes.data());

    std::size_t reserve = 0;
    for (std::size_t at = 0; at < bytes.size();) {
        const Utf8Char ch = scan_utf8(p + at, bytes.size() - at);
        reserve += ch.valid ? ch.length : kReplacementChar.size();
        at += ch.length;
    }

    std::string out;
    out.reserve(reserve);
    for (std::size_t at = 0; at < bytes.size();) {
        const Utf8Char ch = scan_utf8(p + at, bytes.size() - at);
        if (ch.valid)
            out.append(bytes.substr(at, ch.length));
        else
            out.append(kReplacementChar);
        at += ch.length;
    }
    return out;
}

// 一个 UTF-8 字符的码位。调用前 scan_utf8 已经确认过合法性。
unsigned int codepoint_at(const unsigned char* p, std::size_t length) {
    switch (length) {
        case 1:
            return p[0];
        case 2:
            return ((p[0] & 0x1Fu) << 6) | (p[1] & 0x3Fu);
        case 3:
            return ((p[0] & 0x0Fu) << 12) | ((p[1] & 0x3Fu) << 6) | (p[2] & 0x3Fu);
        default:
            return ((p[0] & 0x07u) << 18) | ((p[1] & 0x3Fu) << 12) | ((p[2] & 0x3Fu) << 6) |
                   (p[3] & 0x3Fu);
    }
}

void append_utf8_codepoint(std::string& out, unsigned int codepoint) {
    if (codepoint < 0x80u) {
        out.push_back(static_cast<char>(codepoint));
    } else if (codepoint < 0x800u) {
        out.push_back(static_cast<char>(0xC0u | (codepoint >> 6)));
        out.push_back(static_cast<char>(0x80u | (codepoint & 0x3Fu)));
    } else if (codepoint < 0x10000u) {
        out.push_back(static_cast<char>(0xE0u | (codepoint >> 12)));
        out.push_back(static_cast<char>(0x80u | ((codepoint >> 6) & 0x3Fu)));
        out.push_back(static_cast<char>(0x80u | (codepoint & 0x3Fu)));
    } else {
        out.push_back(static_cast<char>(0xF0u | (codepoint >> 18)));
        out.push_back(static_cast<char>(0x80u | ((codepoint >> 12) & 0x3Fu)));
        out.push_back(static_cast<char>(0x80u | ((codepoint >> 6) & 0x3Fu)));
        out.push_back(static_cast<char>(0x80u | (codepoint & 0x3Fu)));
    }
}

// ================================================================
//  GBK 的字节结构
// ================================================================

// cp936 的次字节：0x40..0xFE，0x7F 不用。0x20 之类落在这里面就是坏字节。
bool is_gbk_trail(unsigned char trail) { return trail >= 0x40 && trail <= 0xFE && trail != 0x7F; }

// 一串字节是不是合法 GBK：ASCII、0x80（欧元）与成对的双字节字。
// 用来拦住 GB18030 写出来的 4 字节序列，那几个不在 GBK 里。
bool is_gbk_bytes(std::string_view bytes) {
    const auto* p = reinterpret_cast<const unsigned char*>(bytes.data());
    std::size_t at = 0;
    while (at < bytes.size()) {
        const unsigned char lead = p[at];
        if (lead < 0x81) {  // ASCII 与 0x80
            ++at;
            continue;
        }
        if (lead == 0xFF) return false;
        if (at + 1 >= bytes.size()) return false;
        if (!is_gbk_trail(p[at + 1])) return false;
        at += 2;
    }
    return true;
}

// ================================================================
//  cp936 补丁表
//
//  cp936（.NET 与 Windows 用的码表）和 iconv 的 GB18030 有 100 组双字节字解出来不一样：
//  cp936 给私用区码位，GB18030 给了别的字。两张表把这两个方向都掰回来，
//  表优先于系统码表，两个平台的结果才逐字一致。其余 23840 组两边相同。
// ================================================================

struct Cp936Pair {
    unsigned char lead;
    unsigned char trail;
    unsigned int codepoint;
};

// 按 (lead, trail) 升序。
constexpr Cp936Pair kCp936Pairs[] = {
    {0xA2, 0xE3, 0xE76C},
    {0xA6, 0xD9, 0xE78D},
    {0xA6, 0xDA, 0xE78E},
    {0xA6, 0xDB, 0xE78F},
    {0xA6, 0xDC, 0xE790},
    {0xA6, 0xDD, 0xE791},
    {0xA6, 0xDE, 0xE792},
    {0xA6, 0xDF, 0xE793},
    {0xA6, 0xEC, 0xE794},
    {0xA6, 0xED, 0xE795},
    {0xA6, 0xF3, 0xE796},
    {0xA8, 0xBC, 0xE7C7},
    {0xA8, 0xBF, 0xE7C8},
    {0xA9, 0x89, 0xE7E7},
    {0xA9, 0x8A, 0xE7E8},
    {0xA9, 0x8B, 0xE7E9},
    {0xA9, 0x8C, 0xE7EA},
    {0xA9, 0x8D, 0xE7EB},
    {0xA9, 0x8E, 0xE7EC},
    {0xA9, 0x8F, 0xE7ED},
    {0xA9, 0x90, 0xE7EE},
    {0xA9, 0x91, 0xE7EF},
    {0xA9, 0x92, 0xE7F0},
    {0xA9, 0x93, 0xE7F1},
    {0xA9, 0x94, 0xE7F2},
    {0xA9, 0x95, 0xE7F3},
    {0xFE, 0x50, 0xE815},
    {0xFE, 0x54, 0xE819},
    {0xFE, 0x55, 0xE81A},
    {0xFE, 0x56, 0xE81B},
    {0xFE, 0x57, 0xE81C},
    {0xFE, 0x58, 0xE81D},
    {0xFE, 0x59, 0xE81E},
    {0xFE, 0x5A, 0xE81F},
    {0xFE, 0x5B, 0xE820},
    {0xFE, 0x5C, 0xE821},
    {0xFE, 0x5D, 0xE822},
    {0xFE, 0x5E, 0xE823},
    {0xFE, 0x5F, 0xE824},
    {0xFE, 0x60, 0xE825},
    {0xFE, 0x61, 0xE826},
    {0xFE, 0x62, 0xE827},
    {0xFE, 0x63, 0xE828},
    {0xFE, 0x64, 0xE829},
    {0xFE, 0x65, 0xE82A},
    {0xFE, 0x66, 0xE82B},
    {0xFE, 0x67, 0xE82C},
    {0xFE, 0x68, 0xE82D},
    {0xFE, 0x69, 0xE82E},
    {0xFE, 0x6A, 0xE82F},
    {0xFE, 0x6B, 0xE830},
    {0xFE, 0x6D, 0xE832},
    {0xFE, 0x6E, 0xE833},
    {0xFE, 0x6F, 0xE834},
    {0xFE, 0x70, 0xE835},
    {0xFE, 0x71, 0xE836},
    {0xFE, 0x72, 0xE837},
    {0xFE, 0x73, 0xE838},
    {0xFE, 0x74, 0xE839},
    {0xFE, 0x75, 0xE83A},
    {0xFE, 0x77, 0xE83C},
    {0xFE, 0x78, 0xE83D},
    {0xFE, 0x79, 0xE83E},
    {0xFE, 0x7A, 0xE83F},
    {0xFE, 0x7B, 0xE840},
    {0xFE, 0x7C, 0xE841},
    {0xFE, 0x7D, 0xE842},
    {0xFE, 0x7E, 0xE843},
    {0xFE, 0x80, 0xE844},
    {0xFE, 0x81, 0xE845},
    {0xFE, 0x82, 0xE846},
    {0xFE, 0x83, 0xE847},
    {0xFE, 0x84, 0xE848},
    {0xFE, 0x85, 0xE849},
    {0xFE, 0x86, 0xE84A},
    {0xFE, 0x87, 0xE84B},
    {0xFE, 0x88, 0xE84C},
    {0xFE, 0x89, 0xE84D},
    {0xFE, 0x8A, 0xE84E},
    {0xFE, 0x8B, 0xE84F},
    {0xFE, 0x8C, 0xE850},
    {0xFE, 0x8D, 0xE851},
    {0xFE, 0x8E, 0xE852},
    {0xFE, 0x8F, 0xE853},
    {0xFE, 0x90, 0xE854},
    {0xFE, 0x92, 0xE856},
    {0xFE, 0x93, 0xE857},
    {0xFE, 0x94, 0xE858},
    {0xFE, 0x95, 0xE859},
    {0xFE, 0x96, 0xE85A},
    {0xFE, 0x97, 0xE85B},
    {0xFE, 0x98, 0xE85C},
    {0xFE, 0x99, 0xE85D},
    {0xFE, 0x9A, 0xE85E},
    {0xFE, 0x9B, 0xE85F},
    {0xFE, 0x9C, 0xE860},
    {0xFE, 0x9D, 0xE861},
    {0xFE, 0x9E, 0xE862},
    {0xFE, 0x9F, 0xE863},
    {0xFE, 0xA0, 0xE864},
};

struct Cp936Bytes {
    unsigned int codepoint;
    unsigned char length;  // 1 或 2：0x80 与 0xFF 只占一个字节
    unsigned char bytes[2];
};

// 按码位升序。除了 100 组私用区字，还有 cp936 的两个特殊字节：
// U+20AC 写成 0x80、U+F8F5 写成 0xFF。
constexpr Cp936Bytes kCp936Bytes[] = {
    {0x20AC, 1, {0x80, 0x00}},
    {0xE76C, 2, {0xA2, 0xE3}},
    {0xE78D, 2, {0xA6, 0xD9}},
    {0xE78E, 2, {0xA6, 0xDA}},
    {0xE78F, 2, {0xA6, 0xDB}},
    {0xE790, 2, {0xA6, 0xDC}},
    {0xE791, 2, {0xA6, 0xDD}},
    {0xE792, 2, {0xA6, 0xDE}},
    {0xE793, 2, {0xA6, 0xDF}},
    {0xE794, 2, {0xA6, 0xEC}},
    {0xE795, 2, {0xA6, 0xED}},
    {0xE796, 2, {0xA6, 0xF3}},
    {0xE7C7, 2, {0xA8, 0xBC}},
    {0xE7C8, 2, {0xA8, 0xBF}},
    {0xE7E7, 2, {0xA9, 0x89}},
    {0xE7E8, 2, {0xA9, 0x8A}},
    {0xE7E9, 2, {0xA9, 0x8B}},
    {0xE7EA, 2, {0xA9, 0x8C}},
    {0xE7EB, 2, {0xA9, 0x8D}},
    {0xE7EC, 2, {0xA9, 0x8E}},
    {0xE7ED, 2, {0xA9, 0x8F}},
    {0xE7EE, 2, {0xA9, 0x90}},
    {0xE7EF, 2, {0xA9, 0x91}},
    {0xE7F0, 2, {0xA9, 0x92}},
    {0xE7F1, 2, {0xA9, 0x93}},
    {0xE7F2, 2, {0xA9, 0x94}},
    {0xE7F3, 2, {0xA9, 0x95}},
    {0xE815, 2, {0xFE, 0x50}},
    {0xE819, 2, {0xFE, 0x54}},
    {0xE81A, 2, {0xFE, 0x55}},
    {0xE81B, 2, {0xFE, 0x56}},
    {0xE81C, 2, {0xFE, 0x57}},
    {0xE81D, 2, {0xFE, 0x58}},
    {0xE81E, 2, {0xFE, 0x59}},
    {0xE81F, 2, {0xFE, 0x5A}},
    {0xE820, 2, {0xFE, 0x5B}},
    {0xE821, 2, {0xFE, 0x5C}},
    {0xE822, 2, {0xFE, 0x5D}},
    {0xE823, 2, {0xFE, 0x5E}},
    {0xE824, 2, {0xFE, 0x5F}},
    {0xE825, 2, {0xFE, 0x60}},
    {0xE826, 2, {0xFE, 0x61}},
    {0xE827, 2, {0xFE, 0x62}},
    {0xE828, 2, {0xFE, 0x63}},
    {0xE829, 2, {0xFE, 0x64}},
    {0xE82A, 2, {0xFE, 0x65}},
    {0xE82B, 2, {0xFE, 0x66}},
    {0xE82C, 2, {0xFE, 0x67}},
    {0xE82D, 2, {0xFE, 0x68}},
    {0xE82E, 2, {0xFE, 0x69}},
    {0xE82F, 2, {0xFE, 0x6A}},
    {0xE830, 2, {0xFE, 0x6B}},
    {0xE832, 2, {0xFE, 0x6D}},
    {0xE833, 2, {0xFE, 0x6E}},
    {0xE834, 2, {0xFE, 0x6F}},
    {0xE835, 2, {0xFE, 0x70}},
    {0xE836, 2, {0xFE, 0x71}},
    {0xE837, 2, {0xFE, 0x72}},
    {0xE838, 2, {0xFE, 0x73}},
    {0xE839, 2, {0xFE, 0x74}},
    {0xE83A, 2, {0xFE, 0x75}},
    {0xE83C, 2, {0xFE, 0x77}},
    {0xE83D, 2, {0xFE, 0x78}},
    {0xE83E, 2, {0xFE, 0x79}},
    {0xE83F, 2, {0xFE, 0x7A}},
    {0xE840, 2, {0xFE, 0x7B}},
    {0xE841, 2, {0xFE, 0x7C}},
    {0xE842, 2, {0xFE, 0x7D}},
    {0xE843, 2, {0xFE, 0x7E}},
    {0xE844, 2, {0xFE, 0x80}},
    {0xE845, 2, {0xFE, 0x81}},
    {0xE846, 2, {0xFE, 0x82}},
    {0xE847, 2, {0xFE, 0x83}},
    {0xE848, 2, {0xFE, 0x84}},
    {0xE849, 2, {0xFE, 0x85}},
    {0xE84A, 2, {0xFE, 0x86}},
    {0xE84B, 2, {0xFE, 0x87}},
    {0xE84C, 2, {0xFE, 0x88}},
    {0xE84D, 2, {0xFE, 0x89}},
    {0xE84E, 2, {0xFE, 0x8A}},
    {0xE84F, 2, {0xFE, 0x8B}},
    {0xE850, 2, {0xFE, 0x8C}},
    {0xE851, 2, {0xFE, 0x8D}},
    {0xE852, 2, {0xFE, 0x8E}},
    {0xE853, 2, {0xFE, 0x8F}},
    {0xE854, 2, {0xFE, 0x90}},
    {0xE856, 2, {0xFE, 0x92}},
    {0xE857, 2, {0xFE, 0x93}},
    {0xE858, 2, {0xFE, 0x94}},
    {0xE859, 2, {0xFE, 0x95}},
    {0xE85A, 2, {0xFE, 0x96}},
    {0xE85B, 2, {0xFE, 0x97}},
    {0xE85C, 2, {0xFE, 0x98}},
    {0xE85D, 2, {0xFE, 0x99}},
    {0xE85E, 2, {0xFE, 0x9A}},
    {0xE85F, 2, {0xFE, 0x9B}},
    {0xE860, 2, {0xFE, 0x9C}},
    {0xE861, 2, {0xFE, 0x9D}},
    {0xE862, 2, {0xFE, 0x9E}},
    {0xE863, 2, {0xFE, 0x9F}},
    {0xE864, 2, {0xFE, 0xA0}},
    {0xF8F5, 1, {0xFF, 0x00}},
};

// 这一对双字节字在 cp936 里解成哪个码位；表里没有就返回 0，交给系统码表。
unsigned int cp936_decoded_char(unsigned char lead, unsigned char trail) {
    constexpr std::size_t count = sizeof(kCp936Pairs) / sizeof(kCp936Pairs[0]);
    const unsigned int key = (static_cast<unsigned int>(lead) << 8) | trail;

    std::size_t lo = 0;
    std::size_t hi = count;
    while (lo < hi) {
        const std::size_t mid = (lo + hi) / 2;
        const unsigned int row =
            (static_cast<unsigned int>(kCp936Pairs[mid].lead) << 8) | kCp936Pairs[mid].trail;
        if (row < key)
            lo = mid + 1;
        else
            hi = mid;
    }
    if (lo < count && kCp936Pairs[lo].lead == lead && kCp936Pairs[lo].trail == trail)
        return kCp936Pairs[lo].codepoint;
    return 0;
}

// 这个码位在 cp936 里写成哪两个字节；表里没有就返回空指针，交给系统码表。
const Cp936Bytes* cp936_bytes_of(unsigned int codepoint) {
    constexpr std::size_t count = sizeof(kCp936Bytes) / sizeof(kCp936Bytes[0]);
    std::size_t lo = 0;
    std::size_t hi = count;
    while (lo < hi) {
        const std::size_t mid = (lo + hi) / 2;
        if (kCp936Bytes[mid].codepoint < codepoint)
            lo = mid + 1;
        else
            hi = mid;
    }
    if (lo < count && kCp936Bytes[lo].codepoint == codepoint) return &kCp936Bytes[lo];
    return nullptr;
}

// ================================================================
//  输出长度：转换前先按结构算一遍，预留恰好够用
//
//  只算字节数，不查码表。GBK 的双字节字与 UTF-16 的一个码元都对应一个 BMP 字符，
//  所以算出来的就是准确值；「目标编码表示不了就换成 '?'」那几处算的是上限，
//  多算的只有那几个字节。预留算少了会让字符串自己扩容，扩容时要多一份旧缓冲，
//  所以宁可算准，也不要算小。
// ================================================================

// GBK 转 UTF-8：ASCII 1 字节，0x80 与 0xFF 都是 3 字节，
// 双字节字 3 字节（解不出来时补的 '?' 只有 1 字节，算的是上限）。
std::size_t utf8_size_of_gbk(std::string_view bytes) {
    const auto* p = reinterpret_cast<const unsigned char*>(bytes.data());
    std::size_t out = 0;
    std::size_t at = 0;
    while (at < bytes.size()) {
        const unsigned char lead = p[at];
        if (lead < 0x80) {
            out += 1;
            at += 1;
            continue;
        }
        if (lead == 0x80 || lead == 0xFF) {
            out += 3;
            at += 1;
            continue;
        }
        if (at + 1 >= bytes.size() || !is_gbk_trail(p[at + 1])) {
            out += 1;
            at += at + 1 >= bytes.size() ? 1 : 2;
            continue;
        }
        out += 3;
        at += 2;
    }
    return out;
}

// UTF-16 转 UTF-8：代理对 4 字节，其余按码位算；孤立代理补替换字符，也是 3 字节。
std::size_t utf8_size_of_utf16(std::string_view bytes, bool big_endian) {
    const auto* p = reinterpret_cast<const unsigned char*>(bytes.data());
    const auto unit_at = [&](std::size_t offset) {
        const unsigned int first = p[offset];
        const unsigned int second = p[offset + 1];
        return big_endian ? first * 256u + second : second * 256u + first;
    };

    std::size_t out = 0;
    std::size_t at = 0;
    while (at + 1 < bytes.size()) {
        const unsigned int unit = unit_at(at);
        if (unit >= 0xD800u && unit <= 0xDBFFu && at + 3 < bytes.size()) {
            const unsigned int low = unit_at(at + 2);
            if (low >= 0xDC00u && low <= 0xDFFFu) {
                out += 4;
                at += 4;
                continue;
            }
        }
        if (unit < 0x80u)
            out += 1;
        else if (unit < 0x800u)
            out += 2;
        else
            out += 3;
        at += 2;
    }
    return out;
}

// UTF-8 转 GBK：ASCII 1 字节，其余按 2 字节算（表示不了时换成的 '?' 是 1 字节，所以这是上限）。
std::size_t gbk_size_of_utf8(std::string_view text) {
    const auto* p = reinterpret_cast<const unsigned char*>(text.data());
    std::size_t out = 0;
    std::size_t at = 0;
    while (at < text.size()) {
        const Utf8Char ch = scan_utf8(p + at, text.size() - at);
        out += ch.length == 1 && ch.valid ? 1 : 2;
        at += ch.length;
    }
    return out;
}

// UTF-8 转 UTF-16：增补平面字 4 字节，其余 2 字节。
std::size_t utf16_size_of_utf8(std::string_view text) {
    const auto* p = reinterpret_cast<const unsigned char*>(text.data());
    std::size_t out = 0;
    std::size_t at = 0;
    while (at < text.size()) {
        const Utf8Char ch = scan_utf8(p + at, text.size() - at);
        out += ch.length == 4 && ch.valid ? 4 : 2;
        at += ch.length;
    }
    return out;
}

// ================================================================
//  转换：两套系统各一份，都是把结果追加到调用方已预留好的字符串上
// ================================================================

#if defined(_WIN32)

// Windows 侧按 4KB 输入一块切开，切点落在字符边界上，半个字不带过去：
// 多字节页按首字节算长度，UTF-16 遇到高位代理时把这一对留给下一块。
constexpr std::size_t kChunkBytes = 4096;

// 多字节码页里从 p 起一个字符占几个字节。
std::size_t unit_bytes(UINT code_page, const unsigned char* p, std::size_t avail) {
    if (code_page == CP_UTF8) return scan_utf8(p, avail).length;
    return p[0] < 0x81 ? 1 : (avail > 1 ? 2 : 1);
}

// 不超过 limit 的最远字符边界。
std::size_t safe_cut(UINT code_page, const unsigned char* p, std::size_t limit) {
    std::size_t used = 0;
    while (used < limit) {
        // 多字节码页的首字节要连同次字节一起切：只剩首字节时宁可留到下一块，
        // 不然最后的半截字交给系统会解成替换字符，次字节又被当成 ASCII，整段错位。
        if (code_page != CP_UTF8 && p[used] >= 0x81) {
            if (limit - used < 2) break;
            used += 2;
            continue;
        }
        const std::size_t one = unit_bytes(code_page, p + used, limit - used);
        if (used + one > limit) break;
        used += one;
    }
    return used;
}

// 多字节码页转 UTF-8，追加到 out。
// 系统不认某一段时退到一个字重试，一个字也不认就补替换字符跳过这个字：
// 整块跳过会把输入丢掉，不能这么干。与 Linux 侧的 iconv 路径行为一致。
void append_mb_to_utf8(std::string& out, std::string_view in, UINT code_page) {
    wchar_t wide[kChunkBytes];
    char narrow[kChunkBytes * 3];
    const auto* p = reinterpret_cast<const unsigned char*>(in.data());

    std::size_t offset = 0;
    while (offset < in.size()) {
        const std::size_t room = in.size() - offset;
        std::size_t cut =
            safe_cut(code_page, p + offset, room < kChunkBytes ? room : kChunkBytes);
        int wide_count =
            MultiByteToWideChar(code_page, 0, reinterpret_cast<const char*>(p + offset),
                                static_cast<int>(cut), wide, static_cast<int>(kChunkBytes));
        if (wide_count <= 0) {
            cut = unit_bytes(code_page, p + offset, room);
            wide_count = MultiByteToWideChar(code_page, 0, reinterpret_cast<const char*>(p + offset),
                                             static_cast<int>(cut), wide,
                                             static_cast<int>(kChunkBytes));
            if (wide_count <= 0) {
                out.append(kReplacementChar);
                offset += cut;
                continue;
            }
        }
        offset += cut;
        const int bytes = WideCharToMultiByte(CP_UTF8, 0, wide, wide_count, narrow,
                                             static_cast<int>(sizeof(narrow)), nullptr, nullptr);
        if (bytes > 0) out.append(narrow, static_cast<std::size_t>(bytes));
    }
}

// UTF-8 转多字节码页（只用于 GBK），追加到 out。
//
// 一个码位一个码位地换，不用整块 WideCharToMultiByte：整块换时增补平面字符在 Windows 上
// 会由一对代理各换成一个问号，与 Linux 侧「一个字符一个问号」不同，写出来的字节数也对不上。
// 另外关掉系统的近似替换（WC_NO_BEST_FIT_CHARS）：它会把某些标点换成外形相近的另一个，
// Linux 的 iconv 是严格换，两边必须同一个结果。
void append_utf8_to_mb(std::string& out, std::string_view in, UINT code_page) {
    wchar_t wide[1];
    char narrow[8];
    const auto* p = reinterpret_cast<const unsigned char*>(in.data());

    for (std::size_t offset = 0; offset < in.size();) {
        const Utf8Char ch = scan_utf8(p + offset, in.size() - offset);
        if (!ch.valid) {
            out.append(kGbkFallback);
            offset += ch.length;
            continue;
        }
        const unsigned int codepoint = codepoint_at(p + offset, ch.length);
        offset += ch.length;

        // GBK 只能表示基本平面：增补平面一个码位换一个问号。
        if (codepoint > 0xFFFFu) {
            out.append(kGbkFallback);
            continue;
        }

        wide[0] = static_cast<wchar_t>(codepoint);
        const int bytes = WideCharToMultiByte(code_page, WC_NO_BEST_FIT_CHARS, wide, 1, narrow,
                                              sizeof(narrow), "?", nullptr);
        if (bytes <= 0) {
            out.append(kGbkFallback);
            continue;
        }
        out.append(narrow, static_cast<std::size_t>(bytes));
    }
}

// 按两个字节一组存下来的 UTF-16 转 UTF-8，追加到 out。big_endian 为真时先换字节序再交给系统。
void append_utf16_to_utf8(std::string& out, std::string_view in, bool big_endian) {
    wchar_t wide[kChunkBytes / 2];
    char narrow[kChunkBytes * 2];
    const auto* p = reinterpret_cast<const unsigned char*>(in.data());

    const auto unit_at = [&](std::size_t offset) {
        const unsigned int first = p[offset];
        const unsigned int second = p[offset + 1];
        return big_endian ? first * 256u + second : second * 256u + first;
    };

    std::size_t offset = 0;  // 字节偏移
    while (offset + 1 < in.size()) {
        std::size_t units = (in.size() - offset) / 2;
        if (units > kChunkBytes / 2) units = kChunkBytes / 2;
        // 切点落在代理对中间时，把这一对留给下一块。
        if (units > 1 && offset + units * 2 < in.size()) {
            const unsigned int last = unit_at(offset + (units - 1) * 2);
            if (last >= 0xD800u && last <= 0xDBFFu) --units;
        }
        for (std::size_t i = 0; i < units; ++i)
            wide[i] = static_cast<wchar_t>(unit_at(offset + i * 2));

        int bytes = WideCharToMultiByte(CP_UTF8, 0, wide, static_cast<int>(units), narrow,
                                       static_cast<int>(sizeof(narrow)), nullptr, nullptr);
        if (bytes <= 0) {
            wide[0] = static_cast<wchar_t>(unit_at(offset));
            units = 1;
            bytes = WideCharToMultiByte(CP_UTF8, 0, wide, 1, narrow,
                                       static_cast<int>(sizeof(narrow)), nullptr, nullptr);
            if (bytes <= 0) {
                out.append(kReplacementChar);
                offset += 2;
                continue;
            }
        }
        offset += units * 2;
        out.append(narrow, static_cast<std::size_t>(bytes));
    }
}

// UTF-8 转按两个字节一组存下来的 UTF-16，追加到 out；big_endian 决定每组的先后。
void append_utf8_to_utf16(std::string& out, std::string_view in, bool big_endian) {
    wchar_t wide[kChunkBytes / 2];
    const auto* p = reinterpret_cast<const unsigned char*>(in.data());

    std::size_t offset = 0;
    while (offset < in.size()) {
        const std::size_t room = in.size() - offset;
        std::size_t cut = safe_cut(CP_UTF8, p + offset, room < kChunkBytes ? room : kChunkBytes);
        int units = MultiByteToWideChar(CP_UTF8, 0, reinterpret_cast<const char*>(p + offset),
                                        static_cast<int>(cut), wide,
                                        static_cast<int>(kChunkBytes / 2));
        if (units <= 0) {
            cut = unit_bytes(CP_UTF8, p + offset, room);
            units = MultiByteToWideChar(CP_UTF8, 0, reinterpret_cast<const char*>(p + offset),
                                        static_cast<int>(cut), wide,
                                        static_cast<int>(kChunkBytes / 2));
            if (units <= 0) {
                out.append(kGbkFallback);
                offset += cut;
                continue;
            }
        }
        offset += cut;
        for (int i = 0; i < units; ++i) {
            const unsigned int unit = static_cast<unsigned int>(wide[i]) & 0xFFFFu;
            out.push_back(static_cast<char>(big_endian ? (unit >> 8) : (unit & 0xFF)));
            out.push_back(static_cast<char>(big_endian ? (unit & 0xFF) : (unit >> 8)));
        }
    }
}

// 一段结构上合法的 GBK 字节转成 UTF-8。Windows 的 936 就是 cp936 码表，
// 与补丁表覆盖的那些行本来一致；特殊字节与补丁行在结构的循环里已经处理掉了。
void append_gbk_run(std::string& out, std::string_view run) {
    append_mb_to_utf8(out, run, 936);
}

// 一段 UTF-8 文本转成 GBK。表示不了的字符由系统换成 '?'，与 Linux 侧一致。
void append_utf8_as_gbk(std::string& out, std::string_view text) {
    append_utf8_to_mb(out, text, 936);
}

#else

class Iconv {
public:
    Iconv(const char* to, const char* from) : cd_(iconv_open(to, from)) {
        if (cd_ == reinterpret_cast<iconv_t>(-1)) {
            throw std::runtime_error(std::string("iconv 打不开转换：") + from + " -> " + to);
        }
    }
    ~Iconv() {
        if (cd_ != reinterpret_cast<iconv_t>(-1)) iconv_close(cd_);
    }
    Iconv(const Iconv&) = delete;
    Iconv& operator=(const Iconv&) = delete;

    iconv_t get() const { return cd_; }

private:
    iconv_t cd_;
};

// 转换的源是什么，决定坏字节要跳过几个才能重新对齐。
enum class Source { Utf8, Gbk, Utf16 };

std::size_t skip_length(Source source, std::string_view rest) {
    switch (source) {
        case Source::Utf8:
            // 坏字按最大合法子部分跳，一个坏字只补一个替换字符。
            return scan_utf8(reinterpret_cast<const unsigned char*>(rest.data()), rest.size())
                .length;
        case Source::Utf16:
            return rest.size() >= 2 ? 2 : rest.size();
        case Source::Gbk:
            break;
    }
    return 1;
}

// 把 in 从 from 码转到 to 码，追加到 out 末尾。out 已按估算长度预留过，
// 所以正常情况下不扩容、不另造副本；一块 32KB 暂存被填满就整块追加出去。
// 输入坏掉时补 replacement 并跳过坏字继续，不抛错。
void append_transcoded(std::string& out, std::string_view in, const char* from, const char* to,
                       Source source, std::string_view replacement) {
    Iconv conv(to, from);

    char buffer[32 * 1024];
    char* src = const_cast<char*>(in.data());
    std::size_t src_left = in.size();
    while (src_left > 0) {
        char* dst = buffer;
        std::size_t dst_left = sizeof(buffer);
        const std::size_t ret = iconv(conv.get(), &src, &src_left, &dst, &dst_left);
        out.append(buffer, sizeof(buffer) - dst_left);
        if (ret != static_cast<std::size_t>(-1)) continue;
        if (errno == E2BIG) continue;  // 暂存写满，下一圈接着写

        if (errno == EILSEQ || errno == EINVAL) {
            const std::size_t skip =
                errno == EINVAL ? src_left : skip_length(source, std::string_view(src, src_left));
            out.append(replacement);
            src += skip;
            src_left -= skip;
            continue;
        }
        throw std::runtime_error(std::string("iconv 转换失败：") + from + " -> " + to);
    }
}

// 一段结构上合法的 GBK 字节（ASCII 与成对的双字节字）转成 UTF-8，追加到 out。
// GBK 的码表走 GB18030：它认得多，剩下那 100 组不一致的由补丁表先拦掉了。
void append_gbk_run(std::string& out, std::string_view run) {
    append_transcoded(out, run, "GB18030", "UTF-8", Source::Gbk, kReplacementChar);
}

// 一段 UTF-8 文本转成 GBK，追加到 out。表示不了的字符补 '?'。
// GB18030 能把 cp936 没有的字写成 4 字节序列，那不是合法 GBK，转换完要验一遍；
// 验不过就退回逐字转，写不成的字补 '?'。
void append_utf8_as_gbk(std::string& out, std::string_view text) {
    std::string converted;
    converted.reserve(text.size());
    append_transcoded(converted, text, "UTF-8", "GB18030", Source::Utf8, kGbkFallback);
    if (is_gbk_bytes(converted)) {
        out.append(converted);
        return;
    }

    const auto* p = reinterpret_cast<const unsigned char*>(text.data());
    for (std::size_t at = 0; at < text.size();) {
        const Utf8Char ch = scan_utf8(p + at, text.size() - at);
        std::string one;
        append_transcoded(one, text.substr(at, ch.length), "UTF-8", "GB18030", Source::Utf8,
                          kGbkFallback);
        if (!one.empty() && is_gbk_bytes(one))
            out.append(one);
        else
            out.append(kGbkFallback);
        at += ch.length;
    }
}

#endif  // _WIN32

// ================================================================
//  GBK 的两个方向：结构自己判，码表交给系统，坏字节照 cp936 的最佳匹配回退补
// ================================================================

// cp936 解码，逐字对得上：
//   0x00..0x7F 是 ASCII；0x80 是欧元符号；0xFF 是私用区 U+F8F5，只吃一个字节；
//   0x81..0xFE 配 0x40..0xFE（不含 0x7F）的双字节字查码表；
//   次字节不合法：补一个 '?' 并连次字节一起吃掉；末尾只剩一个首字节：补一个 '?'。
// 连续的正常字节攒成一段交给系统，一段只调一次转换。
void append_gbk_decoded(std::string& out, std::string_view bytes) {
    const auto* p = reinterpret_cast<const unsigned char*>(bytes.data());
    std::size_t run = 0;  // [run, at) 这段可以整段交给系统
    std::size_t at = 0;

    const auto flush = [&](std::size_t end) {
        if (end > run) append_gbk_run(out, bytes.substr(run, end - run));
        run = end;
    };

    while (at < bytes.size()) {
        const unsigned char lead = p[at];
        if (lead < 0x80) {  // ASCII 跟着正文字一起交出去
            ++at;
            continue;
        }
        if (lead == 0x80) {
            flush(at);
            out.append(kGbkEuroSign);
            ++at;
            run = at;
            continue;
        }
        if (lead == 0xFF) {
            flush(at);
            out.append(kGbkInvalidByte);
            ++at;
            run = at;
            continue;
        }
        if (at + 1 >= bytes.size()) {
            flush(at);
            out.append(kGbkFallback);
            ++at;
            run = at;
            continue;
        }
        if (!is_gbk_trail(p[at + 1])) {
            flush(at);
            out.append(kGbkFallback);
            at += 2;
            run = at;
            continue;
        }
        if (const unsigned int codepoint = cp936_decoded_char(p[at], p[at + 1])) {
            flush(at);
            append_utf8_codepoint(out, codepoint);
            at += 2;
            run = at;
            continue;
        }
        at += 2;
    }
    flush(bytes.size());
}

// cp936 编码：目标编码表示不了的字符补 '?'。
// cp936 的两个特殊字节与 100 组私用区字查补丁表，其余交系统。
void append_gbk_encoded(std::string& out, std::string_view text) {
    const auto* p = reinterpret_cast<const unsigned char*>(text.data());
    std::size_t run = 0;
    std::size_t at = 0;

    const auto flush = [&](std::size_t end) {
        if (end > run) append_utf8_as_gbk(out, text.substr(run, end - run));
        run = end;
    };

    while (at < text.size()) {
        const Utf8Char ch = scan_utf8(p + at, text.size() - at);
        if (!ch.valid) {  // 坏字节在这里就按 '?' 处理，不交给系统
            flush(at);
            out.append(kGbkFallback);
            at += ch.length;
            run = at;
            continue;
        }
        if (ch.length == 1) {  // ASCII 跟着正文字一起交出去
            ++at;
            continue;
        }
        if (const Cp936Bytes* row = cp936_bytes_of(codepoint_at(p + at, ch.length))) {
            flush(at);
            for (unsigned char i = 0; i < row->length; ++i)
                out.push_back(static_cast<char>(row->bytes[i]));
            at += ch.length;
            run = at;
            continue;
        }
        at += ch.length;
    }
    flush(text.size());
}

// ================================================================
//  两套系统之上的统一入口
// ================================================================

std::string decode_to_utf8(std::string_view bytes, TextEncoding encoding) {
    switch (encoding) {
        case TextEncoding::Utf8:
            // 末尾截断的序列原样留着不是合法 UTF-8，补成替换字符。
            return is_valid_utf8(bytes, false) ? std::string(bytes) : with_replacement_chars(bytes);
        case TextEncoding::Utf8Bom:
            return decode_to_utf8(strip_prefix(bytes, kUtf8Bom), TextEncoding::Utf8);
        case TextEncoding::Utf16Le:
        case TextEncoding::Utf16Be: {
            const bool big_endian = encoding == TextEncoding::Utf16Be;
            const std::string_view payload = strip_prefix(bytes, bom_of(encoding));
            std::string out;
            out.reserve(utf8_size_of_utf16(payload, big_endian));
#if defined(_WIN32)
            append_utf16_to_utf8(out, payload, big_endian);
#else
            append_transcoded(out, payload, big_endian ? "UTF-16BE" : "UTF-16LE", "UTF-8",
                              Source::Utf16, kReplacementChar);
#endif
            return out;
        }
        case TextEncoding::Gbk: {
            std::string out;
            out.reserve(utf8_size_of_gbk(bytes));
            append_gbk_decoded(out, bytes);
            return out;
        }
    }
    return std::string(bytes);
}

// 按目标编码把 UTF-8 正文追加到 out（BOM 由调用方处理）。
void append_encoded_body(std::string& out, std::string_view body, TextEncoding encoding) {
    switch (encoding) {
        case TextEncoding::Utf8:
        case TextEncoding::Utf8Bom:
            out.reserve(out.size() + body.size());
            out.append(body);
            return;
        case TextEncoding::Utf16Le:
        case TextEncoding::Utf16Be: {
            const bool big_endian = encoding == TextEncoding::Utf16Be;
            out.reserve(out.size() + utf16_size_of_utf8(body));
#if defined(_WIN32)
            append_utf8_to_utf16(out, body, big_endian);
#else
            append_transcoded(out, body, "UTF-8", big_endian ? "UTF-16BE" : "UTF-16LE",
                              Source::Utf8, kGbkFallback);
#endif
            return;
        }
        case TextEncoding::Gbk:
            out.reserve(out.size() + gbk_size_of_utf8(body));
            append_gbk_encoded(out, body);
            return;
    }
}

std::string encode_from_utf8(std::string_view text, TextEncoding encoding) {
    if (encoding == TextEncoding::Utf8) return std::string(text);

    // 文本开头若也带着 BOM，先去掉：BOM 只由这里补，保证文件开头只有一个。
    const std::string_view body = strip_prefix(text, kUtf8Bom);

    std::string out(bom_of(encoding));
    append_encoded_body(out, body, encoding);
    return out;
}

// ================================================================
//  分块：一块里最后一个字符被切断时，把尾巴留给下一块
// ================================================================

// 块尾有几个字节要留到下一块。多字节码看首字节，UTF-8 看序列长度，UTF-16 留一对代理。
std::size_t keep_tail_length(TextEncoding encoding, std::string_view bytes) {
    const auto* p = reinterpret_cast<const unsigned char*>(bytes.data());
    switch (encoding) {
        case TextEncoding::Utf8:
        case TextEncoding::Utf8Bom: {
            std::size_t at = 0;
            while (at < bytes.size()) {
                const Utf8Char ch = scan_utf8(p + at, bytes.size() - at);
                if (!ch.complete) return bytes.size() - at;
                at += ch.length;
            }
            return 0;
        }
        case TextEncoding::Gbk: {
            std::size_t at = 0;
            while (at < bytes.size()) {
                if (p[at] < 0x81 || p[at] == 0xFF) {  // ASCII、0x80、0xFF 都是一个字节
                    ++at;
                    continue;
                }
                if (at + 1 >= bytes.size()) return 1;
                at += 2;
            }
            return 0;
        }
        case TextEncoding::Utf16Le:
        case TextEncoding::Utf16Be: {
            std::size_t keep = bytes.size() % 2;  // 末尾落单的字节
            const std::size_t usable = bytes.size() - keep;
            if (usable >= 2) {
                const std::size_t last = usable - 2;
                const unsigned int unit = encoding == TextEncoding::Utf16Be
                    ? static_cast<unsigned int>(p[last]) * 256u + p[last + 1]
                    : static_cast<unsigned int>(p[last + 1]) * 256u + p[last];
                if (unit >= 0xD800u && unit <= 0xDBFFu) keep += 2;  // 代理对留给下一块
            }
            return keep;
        }
    }
    return 0;
}

// 结果大致要多大：GBK 与 UTF-16 的一个字在 UTF-8 里最多 3 字节，按 1.5 倍预留。
std::size_t decoded_reserve(TextEncoding encoding, std::size_t source_bytes) {
    switch (encoding) {
        case TextEncoding::Utf8:
        case TextEncoding::Utf8Bom:
            return source_bytes + 4;  // 去掉 BOM 只会更短
        case TextEncoding::Gbk:
        case TextEncoding::Utf16Le:
        case TextEncoding::Utf16Be:
            return source_bytes / 2 * 3 + 4;
    }
    return source_bytes + 4;
}

// 写回结果的预留：GBK 比 UTF-8 短，UTF-16 每个 ASCII 字符要两个字节，按两倍预留。
std::size_t encoded_reserve(TextEncoding encoding, std::size_t text_bytes) {
    switch (encoding) {
        case TextEncoding::Utf8:
        case TextEncoding::Utf8Bom:
        case TextEncoding::Gbk:
            return text_bytes + 4;
        case TextEncoding::Utf16Le:
        case TextEncoding::Utf16Be:
            return text_bytes * 2 + 4;
    }
    return text_bytes + 4;
}

}  // namespace

// ================================================================
//  对外接口
// ================================================================

EncodingProbe probe_encoding(std::string_view bytes) {
    if (starts_with(bytes, kUtf8Bom)) return {TextEncoding::Utf8Bom, true};
    if (starts_with(bytes, kUtf16LeBom)) return {TextEncoding::Utf16Le, true};
    if (starts_with(bytes, kUtf16BeBom)) return {TextEncoding::Utf16Be, true};

    // 头 4KB 先判一次：末尾切在半个字上不算错（这一截先剪掉再判）。
    const std::size_t header_size = bytes.size() < kProbeBytes ? bytes.size() : kProbeBytes;
    if (is_valid_utf8(bytes.substr(0, header_size), true) && is_valid_utf8(bytes, false))
        return {TextEncoding::Utf8, true};

    // 不是合法 UTF-8 就一律按 GBK，没有第三种状态。
    return {TextEncoding::Gbk, true};
}

TextEncoding detect_encoding(std::string_view bytes) { return probe_encoding(bytes).encoding; }

TextEncoding detect_encoding_of_head(std::string_view head) {
    if (starts_with(head, kUtf8Bom)) return TextEncoding::Utf8Bom;
    if (starts_with(head, kUtf16LeBom)) return TextEncoding::Utf16Le;
    if (starts_with(head, kUtf16BeBom)) return TextEncoding::Utf16Be;
    // 段尾切在半个字上不算错，所以末尾不完整的那一截直接不管。
    return is_valid_utf8(head, true) ? TextEncoding::Utf8 : TextEncoding::Gbk;
}

bool is_valid_utf8_bytes(std::string_view bytes, bool truncated_tail_ok) {
    return is_valid_utf8(bytes, truncated_tail_ok);
}

std::size_t incomplete_utf8_tail(std::string_view bytes) {
    // 从末尾往前最多看四个字节：一个字的最后一个续字节到它的首字节。
    // 只看得出「这个字还差几块」，凑齐的、坏字节（scan_utf8 按一个字节跳过的）都不留。
    const std::size_t limit = bytes.size() < 4 ? bytes.size() : 4;
    for (std::size_t take = 1; take <= limit; ++take) {
        const auto lead = static_cast<unsigned char>(bytes[bytes.size() - take]);
        if ((lead & 0xC0) == 0x80) continue;  // 续字节，首字节还要往前找

        std::size_t need = 0;
        if (lead >= 0xC2 && lead <= 0xDF) {
            need = 2;
        } else if (lead >= 0xE0 && lead <= 0xEF) {
            need = 3;
        } else if (lead >= 0xF0 && lead <= 0xF4) {
            need = 4;
        } else {
            return 0;  // ASCII、坏首字节：都不是「还没凑齐的字」
        }
        return need > take ? take : 0;
    }
    return 0;
}

std::string_view encoding_name(TextEncoding encoding) {
    switch (encoding) {
        case TextEncoding::Utf8:
            return "utf-8";
        case TextEncoding::Utf8Bom:
            return "utf-8-bom";
        case TextEncoding::Utf16Le:
            return "utf-16le";
        case TextEncoding::Utf16Be:
            return "utf-16be";
        case TextEncoding::Gbk:
            return "gbk";
    }
    return "utf-8";
}

std::string to_utf8(std::string_view bytes, TextEncoding encoding) {
    return decode_to_utf8(bytes, encoding);
}

// 已验过合法的 UTF-8 序列解成码点。只给上面按编码数字节用。
char32_t code_point_of(const unsigned char* p, std::size_t length) {
    if (length == 1) return p[0];
    char32_t value = p[0] & (0xFFu >> (length + 1));
    for (std::size_t i = 1; i < length; ++i) value = (value << 6) | (p[i] & 0x3Fu);
    return value;
}

std::size_t utf8_char_count(std::string_view text) {
    const auto* p = reinterpret_cast<const unsigned char*>(text.data());
    std::size_t count = 0;
    for (std::size_t at = 0; at < text.size(); ++count) {
        const Utf8Char ch = scan_utf8(p + at, text.size() - at);
        at += ch.length;
    }
    return count;
}

std::size_t encoded_byte_count(std::string_view text, TextEncoding encoding) {
    switch (encoding) {
        case TextEncoding::Utf8:
        case TextEncoding::Utf8Bom:
            return text.size();
        case TextEncoding::Utf16Le:
        case TextEncoding::Utf16Be:
            return utf16_size_of_utf8(text);
        case TextEncoding::Gbk: {
            // cp936 是 ASCII 加双字节的两段式：ASCII 与它的两个单字节特例各占一个字节，
            // 其余基本平面里的字都是 2 字节。两个特例是 0x80（€）与 0xFF（私用区 U+F8F5），
            // 解码与写回都按这一条算（见这个文件里 GBK 的补字规则）。
            // 基本平面以外的字（表情符号等）在 cp936 里没有码位，写出去是一个 '?'，算 1 字节；
            // 坏字节写出去也是 '?'，同样算 1 字节。
            const auto* p = reinterpret_cast<const unsigned char*>(text.data());
            std::size_t out = 0;
            std::size_t at = 0;
            while (at < text.size()) {
                const Utf8Char ch = scan_utf8(p + at, text.size() - at);
                if (!ch.valid || ch.length == 1 || ch.length == 4) {
                    out += 1;
                    at += ch.length;
                    continue;
                }
                const char32_t code_point = code_point_of(p + at, ch.length);
                out += (code_point == 0xF8F5 || code_point == 0x20AC) ? 1 : 2;
                at += ch.length;
            }
            return out;
        }
    }
    return text.size();
}

std::string from_utf8(std::string_view text, TextEncoding encoding) {
    return encode_from_utf8(text, encoding);
}

ChunkedDecoder::ChunkedDecoder(std::string& out, TextEncoding encoding, std::size_t source_bytes)
    : out_(out), encoding_(encoding) {
    out_.reserve(out_.size() + decoded_reserve(encoding_, source_bytes));
}

void ChunkedDecoder::append(std::string_view chunk) {
    if (finished_) return;
    pending_.append(chunk);

    std::size_t offset = 0;
    if (!started_) {
        const std::string_view bom = bom_of(encoding_);
        const std::string_view head(pending_);
        if (!bom.empty()) {
            // 第一块可能连 BOM 都没凑齐，先留着等下一块；凑齐了不是 BOM 就当正文。
            if (head.size() < bom.size()) {
                if (bom.compare(0, head.size(), head) == 0) return;
            } else if (starts_with(head, bom)) {
                offset = bom.size();
            }
        }
        started_ = true;
    }

    const std::string_view rest(pending_.data() + offset, pending_.size() - offset);
    const std::size_t keep = keep_tail_length(encoding_, rest);
    const std::size_t cut = rest.size() - keep;
    if (cut > 0) out_.append(decode_to_utf8(rest.substr(0, cut), base_encoding(encoding_)));
    pending_.erase(0, offset + cut);
}

void ChunkedDecoder::finish() {
    if (finished_) return;
    finished_ = true;
    if (pending_.empty()) return;

    // 留下来的半字按坏字节补替换字符：截断的序列原样写出去不是合法 UTF-8。
    out_.append(decode_to_utf8(pending_, base_encoding(encoding_)));
    pending_.clear();
}

ChunkedEncoder::ChunkedEncoder(std::string& out, TextEncoding encoding, std::size_t text_bytes)
    : out_(out), encoding_(encoding) {
    out_.reserve(out_.size() + encoded_reserve(encoding_, text_bytes));
    out_.append(bom_of(encoding_));  // 文件的 BOM 只在最前面写一次
}

void ChunkedEncoder::append(std::string_view chunk) {
    if (finished_) return;
    pending_.append(chunk);

    if (!started_) {
        // 文本自己带的 BOM 不能写两遍：第一块可能连 BOM 都没凑齐，先留着等下一块。
        const std::string_view head(pending_);
        if (head.size() < kUtf8Bom.size() && kUtf8Bom.compare(0, head.size(), head) == 0) return;
        if (starts_with(head, kUtf8Bom)) pending_.erase(0, kUtf8Bom.size());
        started_ = true;
    }

    const std::string_view rest(pending_);
    const std::size_t keep = keep_tail_length(TextEncoding::Utf8, rest);
    const std::size_t cut = rest.size() - keep;
    if (cut > 0) append_encoded_body(out_, rest.substr(0, cut), encoding_);
    pending_.erase(0, cut);
}

void ChunkedEncoder::finish() {
    if (finished_) return;
    finished_ = true;
    if (pending_.empty()) return;

    append_encoded_body(out_, pending_, encoding_);
    pending_.clear();
}

}  // namespace texttool
