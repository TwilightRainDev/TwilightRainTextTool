// 稿件编码的检测与按原编码写回。
//
// 四个处理功能与检查共用这一处：读进来时记下原编码，写出去时保持原样，
// 否则用户用 GBK 存的稿子处理完会变成 UTF-8，回不去原来的编辑器。
//
// 检测标准：BOM 一出现就定案；
// 没有 BOM 时先看头 4KB，头 4KB 是合法 UTF-8 才再把全篇复验一遍，全篇也合法才认 UTF-8，
// 否则按 GBK；头 4KB 就不是合法 UTF-8 的直接按 GBK。没有「认不出来」这一档，
// 每个文件都会归到五种编码里的一种。判头 4KB 时末尾被切断的多字节字符先剪掉不算，
// 复验全篇时它照旧算不合法，两类判定逐条分开。
//
// 坏字节按解码器补，不抛错：UTF-8 补 U+FFFD；GBK 走 cp936 的最佳匹配回退，
// 次字节不合法的双字节字补 '?'，单字节 0xFF 补私用区 U+F8F5，0x80 是「€」。
//
// BOM 与换行都按原样保留：UTF-8 有 BOM 就写一个、没有就不加，
// UTF-16 一律带 BOM（只有带 BOM 的 UTF-16 才认得出是 UTF-16），
// 换行是字节，转换不碰它，CRLF 与 LF 原样进出。
#pragma once

#include <cstddef>
#include <string>
#include <string_view>

namespace texttool {

enum class TextEncoding { Utf8, Utf8Bom, Utf16Le, Utf16Be, Gbk };

// 检测的完整结果。现在没有「认不出来」这一档，recognized 恒为 true，
// 留着这个字段是为了不改调用方已有的写法；判断编码只看 encoding。
struct EncodingProbe {
    TextEncoding encoding;
    bool recognized;
};

// 判编码的先后：先看 BOM，再看头 4KB 是不是合法 UTF-8，再复验全篇，不是 UTF-8 就按 GBK。
EncodingProbe probe_encoding(std::string_view bytes);

// 只要一个编码时用它；与 probe_encoding 的判断完全相同，只少一个 recognized。
TextEncoding detect_encoding(std::string_view bytes);

// 只看一段文件开头的编码，不做全篇复验：界面显示「文件编码：」用它，不必把整份稿读进来。
// BOM 一出现就定案；这一段是合法 UTF-8 就按 UTF-8，否则按 GBK。段尾被切断的多字节字符不算错，
// 因为文件比这一段长时切点本来就可能落在半个字上。
// 判法与 probe_encoding 的头 4KB 那一步相同，差别只在这里不往下复验：
// 开头是 UTF-8、后面有坏字节的稿子，这里给 Utf8，全篇判一遍给 Gbk。写回仍按 load_text 判的编码。
TextEncoding detect_encoding_of_head(std::string_view head);

// 对外显示的名字，与报告里出现的一致。给人看的中文名在本地化表里，这里只给标识。
std::string_view encoding_name(TextEncoding encoding);

// 这一块字节是不是合法 UTF-8。truncated_tail_ok 为真时，末尾那个还没凑齐整个字的序列
// 不算错（按块读文件时切点本来就可能落在半个字上）；为假时那样的一截算不合法。
// probe_encoding 判文件头与复验全篇用的是同一个规则。
bool is_valid_utf8_bytes(std::string_view bytes, bool truncated_tail_ok);

// 这块字节末尾有几个字节属于一个还没凑齐的字（0 到 3）。按块读一份稿时把它留到下一块，
// 块与块之间就都切在一个字的边界上：按块数出来的字数与按整份数出来的相同，
// 交给下面的管线也不会切在半个字上。只往末尾看，不看前面那几个字节在最前面。
std::size_t incomplete_utf8_tail(std::string_view bytes);

// 把字节按检测到的编码解成 UTF-8 文本。
// 合法 UTF-8 原样返回（末尾被截断的序列不算合法，补 U+FFFD）；带 BOM 的去 BOM；
// 坏字节按各自的解码器补替换字符，不抛错。结果一定是合法 UTF-8。
std::string to_utf8(std::string_view bytes, TextEncoding encoding);

// 这段 UTF-8 正文有多少个字：一个码点算一个字，汉字、字母、标点各算一个。
// 走在坏字节上按一个字节算一个字，与解码器补一个替换字符的做法对得上。
// 自检模式把它打在标准输出上，与报告里的字数不是同一个数法，别混用。
std::size_t utf8_char_count(std::string_view text);

// 这段 UTF-8 文本写成目标编码要占多少字节。行合并的「按字节数」用它在原稿编码上量宽度，
// 与 from_utf8 写出时的字节数相同（表示不了的字写出去是一个 '?'，算 1 字节）。
// Utf8 与 Utf8Bom 都是文本本身的字节数，不含 BOM；UTF-16 按码元算，一个基本平面的字 2 字节。
std::size_t encoded_byte_count(std::string_view text, TextEncoding encoding);

// 把 UTF-8 文本按目标编码写回字节。
// 目标是 Utf8Bom 或 UTF-16 时补一个 BOM，文本开头已有的 BOM 先去掉再补，保证只有一个；
// 目标是 Utf8 时不加 BOM。目标编码表示不了的字符换成 '?'，不抛错。
std::string from_utf8(std::string_view text, TextEncoding encoding);

// ================================================================
//  分块转换：一次只吃一块字节，块尾半个字留到下一块
//
//  整份读进来再解，峰值是「源 + 结果」两份；15MB 的 GBK 稿换成 UTF-8 就是 37MB 往上。
//  用这两个类按块喂，源字节只有一块在内存里，结果只有一份。用法：
//
//      std::string text;
//      texttool::ChunkedDecoder decoder(text, encoding, 文件字节数);
//      while (读到一块) decoder.append(块);
//      decoder.finish();
//
//  给的文件字节数只用来一次预留够容量，省得边转边扩容翻倍；不给也不会错。
//  两个类的构造函数都把结果追加到传入的字符串上，调用方在同一块里接着追加别的内容也行。
// ================================================================

class ChunkedDecoder {
public:
    // source_bytes 是预计要喂进来的源字节总数，用来一次预留结果容量。
    ChunkedDecoder(std::string& out, TextEncoding encoding, std::size_t source_bytes = 0);

    // 喂一块源字节：能转的部分立刻转成 UTF-8 追加到 out，块尾半个字留着等下一块。
    void append(std::string_view chunk);

    // 收尾：把留着的半个字按坏字节处理掉，结果就完整了。之后不再接输入。
    void finish();

private:
    std::string& out_;
    TextEncoding encoding_;
    std::string pending_;  // 上一块末尾没切干净的前缀，最多几个字节
    bool started_ = false;  // BOM 已经处理过
    bool finished_ = false;
};

class ChunkedEncoder {
public:
    // text_bytes 是预计要喂进来的 UTF-8 文本总字节数，用来一次预留容量。
    ChunkedEncoder(std::string& out, TextEncoding encoding, std::size_t text_bytes = 0);

    // 喂一块 UTF-8 文本：按目标编码追加到 out。调用方可以把 out 里的内容随时写出去再 clear()，
    // 这样输出也只有一块在内存里。
    void append(std::string_view chunk);

    // 收尾：把留着的半个字按表示不了的字符处理掉。
    void finish();

private:
    std::string& out_;
    TextEncoding encoding_;
    std::string pending_;
    bool started_ = false;
    bool finished_ = false;
};

}  // namespace texttool
