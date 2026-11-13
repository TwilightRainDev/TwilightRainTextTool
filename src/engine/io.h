// 命令行的读稿、写盘、备份、输出文件名、目录展开，以及文件拼接时从目录里挑稿子。
// 界面也用这一份，两边不会各写一套。
//
// 读写都按用户原编码：读进来时探测一次，写回去时用同一个编码，BOM 与换行原样保留。
// 写盘先写临时文件再整体改名，中途崩掉不会留下写了一半的稿子。
#pragma once

#include <fstream>
#include <memory>
#include <string>
#include <string_view>
#include <vector>

#include "engine/encoding.h"

namespace texttool {

struct LoadedText {
    std::string path;
    std::string text;                        // UTF-8，不含 BOM
    TextEncoding encoding = TextEncoding::Utf8;  // 原文件的编码，写回时用它
};

// 读一份稿：探测编码、解成 UTF-8。文件不存在、读不了都抛 std::runtime_error。
// 消息与命令行里出现过的一致（「文件不存在：…」「读取中断：…」）。
LoadedText load_text(const std::string& path);

// 只看文件开头判编码，不把整份稿读进内存：界面显示「文件编码：」用它。
// 只读前 4096 字节，判法与 load_text 相同（BOM 一出现就定案；这一段合法 UTF-8 就按 UTF-8，
// 否则按 GBK），段尾被切断的多字节字符不算错。差别只在不再往下复验：
// 开头是 UTF-8、后面有坏字节的稿子这里给 Utf8，load_text 给 Gbk。界面只显示，写回仍用 load_text 的结果。
// 文件不存在、打不开、读不了都抛 std::runtime_error，消息与 load_text 一致。
TextEncoding probe_file_encoding(const std::string& path);

// 按给定编码写回。先写 <路径>.tmp 再改名；失败抛 std::runtime_error。
// 整份结果已经在内存里、又不怕多一份拷贝用这个；逐段算出来的结果用下面的 TextWriter。
void write_text(const std::string& path, const std::string& text, TextEncoding encoding);

// 逐块读一份稿：只在开头判一次编码，之后按那个编码解成 UTF-8，一块一块交出去。
// 内存里只有一块源字节与一块正文，不像 load_text 那样整份留着。用法：
//
//     texttool::TextFileReader reader(path);
//     std::string_view 一块;
//     while (reader.next(一块)) 处理(一块);
//     reader.encoding()   // 写回时用这个编码
//
// 判编码的规则与 load_text 相同（BOM 定案；开头 4KB 合法 UTF-8 再复验全篇），
// 交出来的正文与 load_text 读到的逐字节相同，TextWriter 配它写回的字节与 write_text 一样。
// 块尾不会切在一个字的中间；开头的 BOM 已经去掉。
class TextFileReader {
public:
    // 打开文件并判编码。文件不存在、打不开、读不了都抛 std::runtime_error，消息与 load_text 一致。
    explicit TextFileReader(const std::string& path);
    ~TextFileReader();

    TextFileReader(const TextFileReader&) = delete;
    TextFileReader& operator=(const TextFileReader&) = delete;

    TextEncoding encoding() const { return encoding_; }

    // 取下一块正文，返回假表示读完了；传给它的那一块在下次取之前有效。
    // 读到一半出错抛 std::runtime_error（消息与 load_text 一致）。
    bool next(std::string_view& chunk);

    // 从头再读一遍：编码已经判过，不再重判，与把整份稿子重新读一次等价。
    // 要读两遍的管线用它（换标点先看一遍结构再换），省下重新判一次编码那一趟。
    void rewind();

    // 提前放掉文件句柄，之后 next 一律返回假。覆盖原稿（写回同一个文件名）时要用：
    // 读完先把自己这个句柄放掉，接下来改名才不会被自家人占用。Windows 上被占用时改名会失败。
    void close();

    // 已经交出去的正文字数，按 UTF-8 码点算，与 utf8_char_count 同一个数法。
    std::size_t text_chars() const { return text_chars_; }

private:
    void detect();              // 开头判一次编码，判完把读位置拨回文件开头
    bool whole_file_is_utf8();  // 把全篇按块验一遍是不是合法 UTF-8（验完读位置不确定）
    bool read_block();          // 读一块源字节到 source_，读到结尾返回假

    std::string path_;
    std::ifstream in_;
    TextEncoding encoding_ = TextEncoding::Utf8;
    std::string source_;        // 这一块的源字节
    std::string text_;          // 这一块解出来的正文，复用它的容量
    std::string carry_;         // 上一块末尾还没凑齐整个字的那几个字节
    std::unique_ptr<ChunkedDecoder> decoder_;  // 要换编码时用它
    std::size_t text_chars_ = 0;
    std::size_t source_bytes_ = 0;  // 文件字节数，重新开解码器时用它一次预留够容量
    bool first_block_ = true;
    bool finished_ = false;
};
// 逐段写回：先写 <路径>.tmp，每交来一段 UTF-8 正文就按目标编码写进临时文件，最后整体改名。
// 内存里只有当前这一段，不像 write_text 那样要把整份结果再攒一份。用法：
//
//     texttool::TextWriter writer(target, encoding);
//     writer.write(一段);
//     writer.write(另一段);
//     writer.commit();
//
// 没有 commit 就析构时临时文件删掉，目标文件一个字没动。写出的字节与 write_text 一样：
// BOM 只在文件开头写一个，文本自己带的 BOM 去掉，分段处不会多出 BOM。
class TextWriter {
public:
    TextWriter(const std::string& path, TextEncoding encoding);
    ~TextWriter();

    TextWriter(const TextWriter&) = delete;
    TextWriter& operator=(const TextWriter&) = delete;

    // 交一段 UTF-8 正文，可以是任意字节边界（半个字由编码器自己留到下一段）。
    void write(std::string_view utf8);

    // 收尾：关文件、把临时文件改成目标文件名。失败抛 std::runtime_error。
    void commit();

private:
    void flush();

    std::string path_;
    std::string temp_;
    std::ofstream out_;
    std::string encoded_;        // 这一段换算出来的字节，写完就清掉，容量留着复用
    ChunkedEncoder encoder_;     // 把 UTF-8 按原编码换算成字节，块尾半个字自己留着
    bool committed_ = false;
};

// 把 from 改名成 to，to 已存在就替换掉。写盘的收尾与多步链的收尾都走这一处：
// Windows 的 std::filesystem::rename 在目标已存在时会失败，不换这一手，「存回原文件」在
// Windows 上就永远失败。两个路径都按 UTF-8 解释。失败时 error 里有系统给的原因。
bool replace_file(const std::string& from, const std::string& to, std::error_code& error);

// 覆盖前留一份 .bak，历史最多三份（.bak、.bak.1、.bak.2），第四份起挤掉最老的。
// 原文件不存在或是空文件时什么也不做。
void backup_before_overwrite(const std::string& path);

// 去掉首尾的空白。认的空白是空格、制表符、回车、换行四种：
// 界面读输入框（手输路径常在前后多一个空格）与命令行拆逗号项都用它。
std::string trim_whitespace(std::string_view text);

// 路径的最后一段，只按 / 切。命令行印在报告文件名、成功行里的就是这一段：
// Linux 上反斜杠是普通字符，切法不能把目录带进名字里。
std::string file_name_of(std::string_view path);

// 路径的最后一段，/ 与反斜杠都当分隔符。界面里可能收到 Windows 写法的路径。
std::string file_name_of_any(std::string_view path);

// 一次目录展开的结果。
struct DirectoryScan {
    // 收进来的稿子，按路径排序（字节序）。软链接指到同一份稿的只收一条。
    std::vector<std::string> files;
    // 读不了的目录：打不开，或者读到一半出错。files 里是读得到的那部分，
    // 要不要连这份名单一起报出去由调用方定。
    std::vector<std::string> unreadable;
};

// 递归收一个目录里后缀对得上的稿子。extensions 写带点的后缀（".txt"、".md"），
// 按字节比，区分大小写。软链接跟到真实目标：展开过的目录按真实路径只展开一次，
// 指回上级的链接因此不会绕圈；同一个真实文件被几条链接指着只收路径最小的一条。
// directory 不是目录时 files 为空，unreadable 里有它。
DirectoryScan scan_documents(const std::string& directory,
                             const std::vector<std::string>& extensions);

// 同目录下的输出文件名：<主名>_Processed<扩展名>。
// 主名与扩展名按最后一个点拆（.hidden 这类整名都算扩展名，主名为空）。
std::string processed_path(const std::string& path);

// 拼路径：后一段是绝对路径就整段换成它，前一段以 / 结尾就不再多加一个斜杠。
std::string combine_path(const std::string& directory, const std::string& name);

// 文件拼接要拼的那几份稿，以及挑的时候有没有出错。
struct JoinSelection {
    std::vector<std::string> files;
    // 目录读到一半出错（没权限、盘掉线）：files 是出错前收到的那些，不能用，调用方按失败处理。
    bool directory_error = false;
};

// 从目录里挑出要拼的稿子：只收这一层（子目录跳过），文件名按通配挑（只有 * 与 ? 两个
// 通配符，大小写分不分按平台），输出文件自己不算稿子，最后按全路径不分大小写升序排好。
// 收哪些、排成什么次序与命令行 join 一样，界面按同一份名单拼。
JoinSelection select_join_files(const std::string& directory, const std::string& pattern,
                               const std::string& output_path);

}  // namespace texttool
