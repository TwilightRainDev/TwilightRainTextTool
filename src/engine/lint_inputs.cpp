// 目录展开的实现：按相对路径排序、软链接跟到真实目标、读不了的子目录报出去。
//
// 排序键相对每个目录各自算：直接按原路径排会分平台走两套次序，同一个目录里既有子目录
// 又有大写开头的文件时两边顺序相反。分隔符在这里统一写成 /。
#include "engine/lint_inputs.h"

#include <algorithm>
#include <filesystem>
#include <set>
#include <stdexcept>
#include <string>
#include <system_error>
#include <vector>

namespace texttool {
namespace {

namespace fs = std::filesystem;

// 展开出来的一个文件：原路径、真实路径、排序键。
struct Entry {
    std::string file;
    std::string real;
    std::string sort_key;
};

bool is_directory(const std::string& path) {
    std::error_code ec;
    const bool folder = fs::is_directory(path, ec);
    return folder && !ec;
}

// 解析到真实目标：路径中间哪一段是软链接都跟过去，链上串了几层也跟到底。
// 解析不了（链接断掉、没权限）就按原路径返回：宁可多检一遍，也不能把稿漏掉。
std::string real_path(const std::string& path) {
    std::error_code ec;
    const fs::path resolved = fs::weakly_canonical(fs::path(path), ec);
    if (ec) return path;
    return resolved.lexically_normal().generic_string();
}

std::string lower_ascii(std::string text) {
    for (char& c : text)
        if (c >= 'A' && c <= 'Z') c = static_cast<char>(c - 'A' + 'a');
    return text;
}

// 目录展开认的后缀：.txt 与 .md，不分大小写。
bool document_extension(const fs::path& path) {
    const std::string extension = lower_ascii(path.extension().string());
    return extension == ".txt" || extension == ".md";
}

// 相对 root 的路径，分隔符统一写成 /。
std::string sort_key_of(const std::string& root, const std::string& path) {
    std::error_code ec;
    const fs::path relative = fs::relative(fs::path(path), fs::path(root), ec);
    if (ec) return path;
    return relative.generic_string();
}

// 读不了的原因：权限不足、目录不存在这两种用中文说。命令行其余输出都是中文，
// 这里不自成一段外文；别的原因直接把系统给的那句话带出来。
std::string reason_of(const std::error_code& ec) {
    if (ec == std::errc::permission_denied) return "没有访问权限";
    if (ec == std::errc::no_such_file_or_directory) return "目录不存在";
    return ec.message();
}

void report_unreadable(const std::string& directory, const std::error_code& ec,
                       DirectoryWarning on_unreadable, void* context) {
    if (on_unreadable == nullptr) return;
    on_unreadable("读不了目录，跳过：" + directory + "（" + reason_of(ec) + "）", context);
}

void walk(const std::string& root, const std::string& directory, std::vector<Entry>& found,
          std::set<std::string>& walked, DirectoryWarning on_unreadable, void* context,
          int& unreadable) {
    // 走过的真实目录不再走：软链接指回上级时，一棵树会被展开成无限层
    if (!walked.insert(real_path(directory)).second) return;

    std::error_code ec;
    fs::directory_iterator it(directory, ec);
    if (ec) {
        ++unreadable;
        report_unreadable(directory, ec, on_unreadable, context);
        return;
    }

    std::vector<std::string> subdirectories;
    for (; it != fs::directory_iterator(); it.increment(ec)) {
        if (ec) break;
        const fs::directory_entry& entry = *it;
        std::error_code kind_error;
        if (entry.is_directory(kind_error)) {
            subdirectories.push_back(entry.path().string());
            continue;
        }
        // 不是目录就当稿件看。断掉的软链接也走这一路（查不出它指向什么），
        // 收到之后在读取那一步报「文件不存在」。
        if (!document_extension(entry.path())) continue;
        found.push_back({entry.path().string(), real_path(entry.path().string()),
                         sort_key_of(root, entry.path().string())});
    }
    if (ec) {
        // 读到一半出错：已经收下的照留，这个目录记一笔，里头的子目录照常往下走。
        ++unreadable;
        report_unreadable(directory, ec, on_unreadable, context);
    }

    for (const std::string& subdirectory : subdirectories)
        walk(root, subdirectory, found, walked, on_unreadable, context, unreadable);
}

}  // namespace

LintInputFiles collect_lint_inputs(const std::vector<std::string>& inputs,
                                   DirectoryWarning on_unreadable, void* context) {
    LintInputFiles out;
    // 去重按真实路径：同一个目录给两次、软链接与真身各给一次，同一份稿都只检一遍
    std::set<std::string> seen;

    for (const std::string& input : inputs) {
        // 标准输入原样透传：连给两次就是读两次，这里不去重
        if (input == "-") {
            out.files.push_back(input);
            continue;
        }

        // 不是目录的东西都当文件处理：真不存在时由读取那一步报「文件不存在」
        if (!is_directory(input)) {
            if (seen.insert(real_path(input)).second) out.files.push_back(input);
            continue;
        }

        std::vector<Entry> found;
        std::set<std::string> walked;
        walk(input, input, found, walked, on_unreadable, context, out.unreadable);
        if (found.empty())
            throw std::runtime_error("目录下没有 .txt 或 .md 文件：" + input);

        // 同一个目录内部按相对路径的字节序排好，换台机器跑次序也一样
        std::sort(found.begin(), found.end(),
                  [](const Entry& a, const Entry& b) { return a.sort_key < b.sort_key; });
        for (const Entry& entry : found) {
            if (!seen.insert(entry.real).second) continue;
            out.files.push_back(entry.file);
        }
    }
    return out;
}

}  // namespace texttool
