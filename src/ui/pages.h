// 主窗口与各页之间的接口。
//
// 主窗口只负责窗口、页签条、状态条与规则表的加载；每页自己在自己的文件里建控件、
// 接引擎。页面之间的接力（处理完拿去检查）也走这里，不直接摸对方的控件。
#pragma once

#include <FL/Fl_Group.H>
#include <FL/Fl_Window.H>

#include <cstddef>
#include <string>
#include <vector>

#include "engine/lint_runner.h"
#include "engine/processing.h"
#include "engine/rules.h"
#include "engine/schemes.h"
#include "ui/tab_bar.h"
#include "ui/widgets.h"

namespace texttool::ui {

// 页的标识。页与页之间经主窗口那张登记表取对象，谁都不自己存一份全局页指针：
// 存全局量的写法在建出第二份页面时会悄悄把前一份顶掉，接力失败也看不出来。
// 这里只写页本身是什么，不管它在页签层的哪一格：页签怎么排都不牵动这张表。
enum class PageId { Process, Merge, Join, Replace, Vn, Lint, About };

// 主窗口交给各页的东西。各页只读，不改，登记表那一项除外。
struct AppContext {
    Fl_Window* window = nullptr;
    TabBar* tabs = nullptr;
    const std::vector<texttool::LintRule>* rules = nullptr;  // 规则表读到了才有
    std::string rule_error;                                  // 读不出来时的那一句
    std::string font_family;                                 // 界面用的中文字体族名
    // 程序自己的路径。配置文件（替换规则、替换方案、角色方案）按它所在的目录找，
    // 与命令行同一个找法；空串就是按当前工作目录找。
    std::string program_path;

    // 页登记表：各页的建页函数把自己登记进来，页与页之间按标识取对象。
    // 同一页被登记第二次时换成新的那个指针，不新增一条。
    void register_page(PageId id, Fl_Group* page) {
        for (auto& entry : pages) {
            if (entry.first == id) {
                entry.second = page;
                return;
            }
        }
        pages.emplace_back(id, page);
    }

    // 这一页还没建出来时给 nullptr，调用方自己决定怎么说那件事。
    Fl_Group* page(PageId id) const {
        for (const auto& entry : pages) {
            if (entry.first == id) return entry.second;
        }
        return nullptr;
    }

    std::vector<std::pair<PageId, Fl_Group*>> pages;
};

// 各页一个构造函数：建好自己的控件，返回这一页。位置尺寸由主窗口给。
// 建页顺序就是页签层里页的扁平名词序（顶项页在前，下拉里的按挂进去的次序），
// 由 app.cpp 决定；这里只按名字给出建页函数。
Page* build_process_page(AppContext& app, int x, int y, int w, int h);
Page* build_merge_page(AppContext& app, int x, int y, int w, int h);
Page* build_join_page(AppContext& app, int x, int y, int w, int h);
Page* build_replace_page(AppContext& app, int x, int y, int w, int h);
Page* build_vn_page(AppContext& app, int x, int y, int w, int h);
Page* build_lint_page(AppContext& app, int x, int y, int w, int h);
Page* build_about_page(AppContext& app, int x, int y, int w, int h);

// 自检模式跑完一份稿的结果。
struct SelfCheckSummary {
    std::string output;            // 写成的文件；空表示没写成，原因在状态条上
    std::size_t input_chars = 0;   // 源文多少字，按 UTF-8 码点算
    std::size_t output_chars = 0;  // 结果多少字，同一个算法
};

// 自检模式：把一份稿填进处理页的源文件框，按页面上的勾选真的跑一遍、真的写盘。
// 走的是用户点「开始处理」那条路（同一个函数），状态条与源文件框都会跟着变。
// single_step 为 0 时按处理页现在勾的那些跑；1 到 4 只跑其中一项（接回断行、合并多份、
// 换标点、剧本排版），给逐页量内存用。空结果表示没写成，原因在状态条上。
SelfCheckSummary run_process_page_on_file(AppContext& app, const std::string& path, int single_step);

// 各高级页把屏上的那一份参数交给处理页。读不出来（规则表坏了、角色表对不上）时返回 false，
// error 里是给人看的原因；这时不要把 options 拿去跑。
bool merge_page_options(AppContext& app, texttool::MergeOptions& options, std::string& error);
bool replace_page_options(AppContext& app, texttool::ReplaceOptions& options, std::string& error);
bool vn_page_options(AppContext& app, texttool::VnOptions& options, std::string& error);

// 合并多份页那一格「合并成哪个文件」，空着时返回默认的 joined.txt。
std::string join_page_output_name(AppContext& app);

// 检查页现在选的显示哪些问题、稿子类型、只查哪几条。处理页跑报告面板时用同一份。
texttool::LintOptions lint_page_options(AppContext& app);

// 在产物上跑一遍检查，交回人读报告（排版与命令行 lint 打出来的那一份相同）。
// 检查项读不进来、稿子读不了都返回 false，error 说原因。
bool lint_page_report(AppContext& app, const std::string& path, std::string& report,
                      std::string& error);

// 「备份设置」要打的那份换标点的规则：换标点页现在列表里的那些，这次运行里改过的也算。
// 建页面时读规则表出过错的话，error 里是那个原因，返回的表是空的。
std::vector<texttool::ReplaceRule> replace_page_rules(AppContext& app, std::string& error);

// 「恢复设置」写完之后，把包里的规则换进换标点页的列表。
void replace_page_apply_imported_rules(AppContext& app,
                                       const std::vector<texttool::ReplaceRule>& rules);

// 「恢复设置」写完之后，让剧本排版页重新读一遍角色表：那一行统计跟着变。
void vn_page_reload_schemes(AppContext& app);

}  // namespace texttool::ui
