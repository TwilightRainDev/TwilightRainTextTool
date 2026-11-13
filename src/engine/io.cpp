// 读稿、写文件、备份、输出文件名的实现。编码按严格标准检测；写文件先写临时文件再改名；
// 覆盖之前留备份；处理结果的文件名带 _Processed。输出与 fixtures 下的期望输出逐字节相同。
#include "engine/io.h"

#include <algorithm>
#include <cerrno>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <set>
#include <stdexcept>
#include <string>
#include <string_view>
#include <system_error>
#include <utility>
#include <vector>

#include "engine/encoding.h"

#ifdef _WIN32
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#endif

namespace texttool {
namespace {

namespace fs = std::filesystem;

// 只看文件开头判编码时读到哪为止。
constexpr std::size_t kHeadBytes = 4096;

// 按块读一份稿时一块收多少字节。一块要能装下几十行，又不能占掉看得出来的内存：
// 64KB 一块，读缓冲与解出来的正文各一份，加起来一两百 KB。
constexpr std::size_t kReadBlockBytes = 64 * 1024;

std::string name_of(const std::string& path) { return file_name_of(path); }

// 按块读一份稿、边读边解成 UTF-8：源字节只有一块在内存里，结果只有一份。
// 解出来的字节与 to_utf8 整份解的一致（分块解码就是这个等价，测试钉着）。
std::string decode_file_in_chunks(const std::string& path, TextEncoding encoding,
                                  std::size_t source_bytes) {
    errno = 0;
    std::ifstream in(path, std::ios::binary);
    if (!in) throw std::runtime_error(std::strerror(errno == 0 ? EACCES : errno));

    std::string text;
    ChunkedDecoder decoder(text, encoding, source_bytes);
    std::vector<char> buffer(256 * 1024);
    while (in) {
        in.read(buffer.data(), static_cast<std::streamsize>(buffer.size()));
        const std::streamsize got = in.gcount();
        if (got > 0)
            decoder.append(std::string_view(buffer.data(), static_cast<std::size_t>(got)));
    }
    if (in.bad()) throw std::runtime_error("读取中断：" + path);
    decoder.finish();
    return text;
}

std::string read_all_bytes(const std::string& path) {
    errno = 0;
    std::ifstream in(path, std::ios::binary);
    if (!in) throw std::runtime_error(std::strerror(errno == 0 ? EACCES : errno));

    std::string bytes;
    std::error_code size_error;
    const auto on_disk = fs::file_size(path, size_error);
    if (!size_error) bytes.reserve(static_cast<std::size_t>(on_disk));

    char buffer[64 * 1024];
    while (in) {
        in.read(buffer, sizeof buffer);
        bytes.append(buffer, static_cast<std::size_t>(in.gcount()));
    }
    if (in.bad()) throw std::runtime_error("读取中断：" + path);
    return bytes;
}

// 文件名的通配：只有 * 与 ? 两个通配符，大小写分不分按平台。
// `*.*`、`.` 与空模式按 Win32 的规矩是「全部文件」。
bool match_file_pattern(std::string_view pattern, std::string_view name) {
    if (pattern.empty() || pattern == "." || pattern == "*.*") return true;

    std::size_t p = 0, n = 0;
    std::size_t star = std::string_view::npos, star_name = 0;
    while (n < name.size()) {
        if (p < pattern.size() && (pattern[p] == '?' || pattern[p] == name[n])) {
            ++p;
            ++n;
        } else if (p < pattern.size() && pattern[p] == '*') {
            star = p++;
            star_name = n;
        } else if (star != std::string_view::npos) {
            p = star + 1;
            n = ++star_name;
        } else {
            return false;
        }
    }
    while (p < pattern.size() && pattern[p] == '*') ++p;
    return p == pattern.size();
}

// 排队与比「是不是输出文件自己」时折大小写：折的只有 ASCII 字母，
// 非 ASCII 字母（é、Ä）按字节比。
std::string fold_ascii_case(std::string_view text) {
    std::string folded(text);
    for (char& c : folded)
        if (c >= 'a' && c <= 'z') c = static_cast<char>(c - 'a' + 'A');
    return folded;
}

// 比「是不是输出文件自己」时用：折掉 . 与 ..，不解析软链接。
std::string absolute_path(const std::string& path) {
    std::error_code ec;
    const std::filesystem::path absolute = std::filesystem::absolute(path, ec);
    if (ec) return path;
    return absolute.lexically_normal().generic_string();
}

}  // namespace

TextEncoding probe_file_encoding(const std::string& path) {
    std::error_code ignored;
    if (!fs::is_regular_file(path, ignored)) throw std::runtime_error("文件不存在：" + path);

    errno = 0;
    std::ifstream in(path, std::ios::binary);
    if (!in) throw std::runtime_error(std::strerror(errno == 0 ? EACCES : errno));

    // 只读开头这一段：判定要的就是它。文件比这短时只读到实际字节数。
    std::string head(kHeadBytes, '\0');
    in.read(head.data(), static_cast<std::streamsize>(head.size()));
    head.resize(static_cast<std::size_t>(in.gcount()));
    if (in.bad()) throw std::runtime_error("读取中断：" + path);
    return detect_encoding_of_head(head);
}

LoadedText load_text(const std::string& path) {
    std::error_code ignored;
    if (!fs::is_regular_file(path, ignored)) throw std::runtime_error("文件不存在：" + path);

    LoadedText out;
    out.path = path;
    std::string bytes = read_all_bytes(path);
    out.encoding = detect_encoding(bytes);
    // 已经是合法 UTF-8 的稿子不必再解一遍：探测那一步已经把全篇验过，读进来的那一份直接当正文。
    if (out.encoding == TextEncoding::Utf8) {
        out.text = std::move(bytes);
    } else if (out.encoding == TextEncoding::Utf8Bom) {
        bytes.erase(0, 3);
        out.text = std::move(bytes);
    } else {
        // 要换编码的稿子：源字节与解出来的正文要是同时留在内存里，15MB 的稿子就是两份。
        // 探测那一步已经用过源字节，这里先把它放开，再从盘上按块读一遍（页缓存里，几十毫秒）。
        // 两次读之间文件被别人改掉的话，会按第一次判的编码解第二次读到的字节。
        const std::size_t source_bytes = bytes.size();
        std::string().swap(bytes);
        out.text = decode_file_in_chunks(path, out.encoding, source_bytes);
    }
    return out;
}

TextFileReader::TextFileReader(const std::string& path) : path_(path) {
    std::error_code ignored;
    if (!fs::is_regular_file(path, ignored)) throw std::runtime_error("文件不存在：" + path);

    errno = 0;
    in_.open(path, std::ios::binary);
    if (!in_) throw std::runtime_error(std::strerror(errno == 0 ? EACCES : errno));

    detect();
    // 要换编码的稿子（GBK、UTF-16）边走边解：解码器自己留着块尾那半个字。
    std::error_code size_error;
    const auto on_disk = fs::file_size(path, size_error);
    source_bytes_ = size_error ? 0 : static_cast<std::size_t>(on_disk);
    if (encoding_ != TextEncoding::Utf8 && encoding_ != TextEncoding::Utf8Bom)
        decoder_ = std::make_unique<ChunkedDecoder>(text_, encoding_, source_bytes_);
}

TextFileReader::~TextFileReader() = default;

void TextFileReader::detect() {
    // 一、开头这一段先判：BOM 一出现就定案，这一段不是合法 UTF-8 就按 GBK。
    // 规则与 load_text 里那次 detect_encoding 相同，只是不再把全篇装进内存。
    std::string head(kHeadBytes, '\0');
    in_.read(head.data(), static_cast<std::streamsize>(head.size()));
    head.resize(static_cast<std::size_t>(in_.gcount()));
    if (in_.bad()) throw std::runtime_error("读取中断：" + path_);
    encoding_ = detect_encoding_of_head(head);

    // 二、开头是 UTF-8 才要把全篇复验一遍（全篇也合法才认 UTF-8）。
    // 这一步只验字节，一块一块看过就丢，不在内存里攒。
    if (encoding_ == TextEncoding::Utf8 && !whole_file_is_utf8()) encoding_ = TextEncoding::Gbk;

    in_.clear();
    in_.seekg(0);
    if (!in_) throw std::runtime_error("读取中断：" + path_);
}

bool TextFileReader::whole_file_is_utf8() {
    in_.clear();
    in_.seekg(0);
    if (!in_) throw std::runtime_error("读取中断：" + path_);

    std::string carry;
    std::string block(kReadBlockBytes, '\0');
    while (true) {
        in_.read(block.data(), static_cast<std::streamsize>(block.size()));
        const std::size_t got = static_cast<std::size_t>(in_.gcount());
        if (in_.bad()) throw std::runtime_error("读取中断：" + path_);
        if (got == 0) break;

        // 上一块末尾那几个字节与这一块拼起来再验，免得把一个字切成两半当成坏字节。
        carry.append(block.data(), got);
        const std::size_t keep = incomplete_utf8_tail(carry);
        if (!is_valid_utf8_bytes(std::string_view(carry).substr(0, carry.size() - keep), false))
            return false;
        carry.erase(0, carry.size() - keep);
    }
    // 收尾时还留着没凑齐的一截，就是全篇最后一个字被切断了，不算合法 UTF-8。
    return carry.empty();
}

void TextFileReader::close() {
    finished_ = true;
    in_.close();
}

void TextFileReader::rewind() {
    // 编码不再重判：判编码那一步要把全篇验一遍，再来一趟只为了多省一遍读盘。
    in_.clear();
    in_.seekg(0);
    if (!in_) throw std::runtime_error("读取中断：" + path_);

    text_.clear();
    source_.clear();
    carry_.clear();
    if (encoding_ != TextEncoding::Utf8 && encoding_ != TextEncoding::Utf8Bom)
        decoder_ = std::make_unique<ChunkedDecoder>(text_, encoding_, source_bytes_);
    text_chars_ = 0;
    first_block_ = true;
    finished_ = false;
}

bool TextFileReader::read_block() {
    source_.resize(kReadBlockBytes);
    in_.read(source_.data(), static_cast<std::streamsize>(source_.size()));
    source_.resize(static_cast<std::size_t>(in_.gcount()));
    if (in_.bad()) throw std::runtime_error("读取中断：" + path_);
    return !source_.empty();
}

bool TextFileReader::next(std::string_view& chunk) {
    chunk = {};
    if (finished_) return false;

    text_.clear();
    const bool got = read_block();
    if (decoder_) {
        if (got) {
            decoder_->append(source_);
        } else {
            decoder_->finish();
            finished_ = true;
        }
    } else if (got) {
        // 已经是 UTF-8 的稿子不重解：读进来的字节就是正文，与 load_text 一样原样交出去。
        // 开头的 BOM 要去掉，它不是正文。
        std::string_view bytes(source_);
        if (first_block_ && encoding_ == TextEncoding::Utf8Bom && bytes.starts_with("\xEF\xBB\xBF"))
            bytes.remove_prefix(3);
        text_ = carry_;
        text_.append(bytes);
        carry_.clear();
        // 末尾还没凑齐整个字的那几个字节留到下一块，交出去的块因此都切在字的边界上。
        const std::size_t keep = incomplete_utf8_tail(text_);
        if (keep > 0) carry_.assign(text_, text_.size() - keep, keep);
        text_.resize(text_.size() - keep);
    } else {
        // 文件读完了：留着的那几个字节凑不齐一整字，原样当正文交出去（它们是坏字节）。
        finished_ = true;
        text_ = std::move(carry_);
        carry_.clear();
    }
    first_block_ = false;

    text_chars_ += utf8_char_count(text_);
    if (text_.empty()) return false;
    chunk = text_;
    return true;
}

TextWriter::TextWriter(const std::string& path, TextEncoding encoding)
    : path_(path), temp_(path + ".tmp"), encoded_(), encoder_(encoded_, encoding) {
    out_.open(temp_, std::ios::binary | std::ios::trunc);
    if (!out_) throw std::runtime_error("写入失败：" + path_);
    flush();  // 编码器在构造时已经排好了 BOM，这里把它先写出去
}

TextWriter::~TextWriter() {
    if (committed_) return;
    out_.close();
    std::error_code ignored;
    fs::remove(temp_, ignored);
}

void TextWriter::write(std::string_view utf8) {
    encoder_.append(utf8);
    flush();
}

void TextWriter::flush() {
    if (encoded_.empty()) return;
    out_.write(encoded_.data(), static_cast<std::streamsize>(encoded_.size()));
    encoded_.clear();
    if (!out_) throw std::runtime_error("写入失败：" + path_);
}

void TextWriter::commit() {
    encoder_.finish();
    flush();
    out_.close();
    if (!out_) {
        std::error_code ignored;
        fs::remove(temp_, ignored);
        throw std::runtime_error("写入失败：" + path_);
    }
    std::error_code ec;
    if (!replace_file(temp_, path_, ec)) {
        std::error_code ignored;
        fs::remove(temp_, ignored);
        throw std::runtime_error("写入失败：" + path_);
    }
    committed_ = true;
}

bool replace_file(const std::string& from, const std::string& to, std::error_code& error) {
#ifdef _WIN32
    // 路径按 UTF-8 解释。u8path 在 C++20 里已标为过时，直接用 char8_t 构造。
    const auto to_path = [](const std::string& text) {
        return fs::path(std::u8string(reinterpret_cast<const char8_t*>(text.data()), text.size()));
    };
    const fs::path source = to_path(from);
    const fs::path target = to_path(to);
    if (MoveFileExW(source.c_str(), target.c_str(), MOVEFILE_REPLACE_EXISTING)) {
        error.clear();
        return true;
    }
    error = std::error_code(static_cast<int>(GetLastError()), std::system_category());
    return false;
#else
    fs::rename(from, to, error);
    return !error;
#endif
}

void write_text(const std::string& path, const std::string& text, TextEncoding encoding) {
    TextWriter writer(path, encoding);
    writer.write(text);
    writer.commit();
}

void backup_before_overwrite(const std::string& path) {
    constexpr int kMaxHistory = 3;
    std::error_code ec;
    if (!fs::is_regular_file(path, ec) || ec) return;
    if (fs::file_size(path, ec) == 0 || ec) return;

    const std::string backup = path + ".bak";
    const auto history = [&path](int index) { return path + ".bak." + std::to_string(index); };

    if (fs::exists(history(kMaxHistory), ec)) fs::remove(history(kMaxHistory), ec);
    for (int index = kMaxHistory - 1; index >= 1; --index)
        if (fs::exists(history(index), ec)) fs::rename(history(index), history(index + 1), ec);
    if (fs::exists(backup, ec)) fs::rename(backup, history(1), ec);
    fs::copy_file(path, backup, fs::copy_options::overwrite_existing, ec);
}

std::string trim_whitespace(std::string_view text) {
    const auto is_blank = [](char c) {
        return c == ' ' || c == '\t' || c == '\r' || c == '\n';
    };
    std::size_t begin = 0;
    std::size_t end = text.size();
    while (begin < end && is_blank(text[begin])) ++begin;
    while (end > begin && is_blank(text[end - 1])) --end;
    return std::string(text.substr(begin, end - begin));
}

std::string file_name_of(std::string_view path) {
    const std::size_t slash = path.find_last_of('/');
    return std::string(slash == std::string_view::npos ? path : path.substr(slash + 1));
}

std::string file_name_of_any(std::string_view path) {
    const std::size_t slash = path.find_last_of("/\\");
    return std::string(slash == std::string_view::npos ? path : path.substr(slash + 1));
}

DirectoryScan scan_documents(const std::string& directory,
                             const std::vector<std::string>& extensions) {
    DirectoryScan scan;
    std::error_code ec;
    if (!fs::is_directory(directory, ec)) {
        scan.unreadable.push_back(directory);
        return scan;
    }

    // 走的是自己的显式栈，不用 recursive_directory_iterator：每一步出错都能落在手里，
    // 哪个目录读不了也就报得出来。软链接跟到真实目标，展开过的目录按真实路径去重，
    // 指回上级的链接在这里停住，不会绕圈。
    std::set<std::string> visited_directories;
    std::vector<std::string> pending{directory};
    std::vector<std::pair<std::string, std::string>> found;  // 路径、真实路径

    while (!pending.empty()) {
        const std::string folder = std::move(pending.back());
        pending.pop_back();

        std::error_code real_error;
        const std::string real_folder = fs::canonical(folder, real_error).generic_string();
        const std::string identity = real_error ? folder : real_folder;
        if (!visited_directories.insert(identity).second) continue;

        fs::directory_iterator it(folder, ec);
        if (ec) {
            scan.unreadable.push_back(folder);
            continue;
        }
        for (; it != fs::directory_iterator(); it.increment(ec)) {
            if (ec) {
                // 读到一半出错：已经收下的照留，这个目录记一笔。
                scan.unreadable.push_back(folder);
                break;
            }
            const fs::directory_entry& entry = *it;
            std::error_code kind_error;
            if (entry.is_directory(kind_error)) {
                if (kind_error)
                    scan.unreadable.push_back(entry.path().generic_string());
                else
                    pending.push_back(entry.path().generic_string());
                continue;
            }
            if (!entry.is_regular_file(kind_error) || kind_error) continue;

            const std::string name = entry.path().filename().string();
            bool wanted = false;
            for (const std::string& extension : extensions) {
                if (name.size() >= extension.size() &&
                    name.compare(name.size() - extension.size(), extension.size(), extension) == 0) {
                    wanted = true;
                    break;
                }
            }
            if (!wanted) continue;

            std::error_code file_error;
            const std::string real_file = fs::canonical(entry.path(), file_error).generic_string();
            const std::string shown = entry.path().generic_string();
            found.emplace_back(shown, file_error ? shown : real_file);
        }
    }

    std::sort(found.begin(), found.end(), [](const auto& a, const auto& b) { return a.first < b.first; });
    std::set<std::string> seen_files;
    for (const auto& item : found) {
        if (!seen_files.insert(item.second).second) continue;
        scan.files.push_back(item.first);
    }
    return scan;
}

std::string processed_path(const std::string& path) {
    // 用哪一份切法按平台定：Windows 上反斜杠是分隔符（与 .NET 的 Path 一致），
    // Linux 上反斜杠只是普通字符。切错了名字里就带着目录，拼出来的输出路径会出现两层目录。
#if defined(_WIN32)
    const std::string name = file_name_of_any(path);
#else
    const std::string name = name_of(path);
#endif
    const std::size_t dot = name.find_last_of('.');
    const std::string stem = dot == std::string::npos ? name : name.substr(0, dot);
    const std::string ext = dot == std::string::npos ? std::string() : name.substr(dot);
    const std::string output = stem + "_Processed" + ext;

    const std::string dir = fs::path(path).parent_path().string();
    if (dir.empty()) return output;
    return (fs::path(dir) / output).string();
}

std::string combine_path(const std::string& directory, const std::string& name) {
    if (name.empty()) return directory;
    if (name.front() == '/') return name;
    if (directory.empty()) return name;
    return directory.back() == '/' ? directory + name : directory + "/" + name;
}

JoinSelection select_join_files(const std::string& directory, const std::string& pattern,
                               const std::string& output_path) {
    JoinSelection selection;
    const std::string output_identity = fold_ascii_case(absolute_path(output_path));

    // 只收这一层（不递归）：子目录跳过，别的都算稿子。软链接按名字收，
    // 断了的那条也收（不是目录就收，读的时候才报错），与命令行 join 一样。
    std::error_code ec;
    for (std::filesystem::directory_iterator it(directory, ec), end; !ec && it != end;
         it.increment(ec)) {
        std::error_code entry_error;
        if (it->is_directory(entry_error) && !entry_error) continue;
        const std::string name = it->path().filename().string();
        if (!match_file_pattern(pattern, name)) continue;
        const std::string path = combine_path(directory, name);
        if (fold_ascii_case(absolute_path(path)) == output_identity) continue;
        selection.files.push_back(path);
    }
    if (ec) {
        selection.directory_error = true;
        return selection;
    }

    std::stable_sort(selection.files.begin(), selection.files.end(),
                     [](const std::string& left, const std::string& right) {
                         return fold_ascii_case(left) < fold_ascii_case(right);
                     });
    return selection;
}

}  // namespace texttool
