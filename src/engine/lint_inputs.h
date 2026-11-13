// lint 的输入整理：把命令行给的文件与目录摊平成待检稿件清单。
//
// 目录递归收 .txt 与 .md（后缀不分大小写），同一个目录内部按相对路径排序，
// 分隔符统一写成 /，换台机器也是同一个次序；软链接跟到真实目标，同一份稿只检一次，
// 指回上级的链接不会把一棵树展开两次；子目录读不了只报那一条路径，其余照常收。
#pragma once

#include <string>
#include <vector>

namespace texttool {

// 读不了的子目录交给调用方去说：命令行打到标准错误，别的调用方自己决定。
// message 是一整句可以原样写给读者的话。
using DirectoryWarning = void (*)(const std::string& message, void* context);

struct LintInputFiles {
    // 收进来的稿子。整体按输入顺序；同一个目录内部按相对路径排序。
    std::vector<std::string> files;
    // 读不了的子目录条数，具体每一句已通过 DirectoryWarning 交出去了。
    int unreadable = 0;
};

// 展开输入：目录换成它下面所有 .txt 与 .md，文件与 - 直接放进清单。
// 一个目录里一个 .txt 或 .md 都收不到时抛 std::runtime_error，
// 消息是「目录下没有 .txt 或 .md 文件：<目录>」，让「路径写错」与「目录里没有稿子」分得开。
// 传 - 表示从标准输入读，连给两次就是读两次，这里不去重。
LintInputFiles collect_lint_inputs(const std::vector<std::string>& inputs,
                                   DirectoryWarning on_unreadable = nullptr,
                                   void* context = nullptr);

}  // namespace texttool
