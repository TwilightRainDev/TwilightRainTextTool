// 换标点页：管这一份换标点的规则表，以及盘上的几份现成方案。
//
// 左边是这次要用的规则列表；右边两格编辑规则：把什么换成什么，按「确定」改掉选中的那一条。
// 列表下面两排小按钮：上移、下移、添加、删除；从预设里挑、保存。
//
// 「从预设里挑」那扇窗同时是方案管理：列出盘上的每一份方案，能按「用这份」把它的规则整批搬进
// 左边的列表，也能在这里新建、编辑、删除。新建与编辑在另一扇小窗里写方案名与整份规则，
// 按「确定」就整份写回盘上；删除在窗里确认一次再删，同样整份写回。
//
// 这一页不管换标点怎么换（那在引擎里），也不管稿子怎么读写、怎么批量跑（那是处理页的事）：
// 这一页只交出「现在这份规则」，处理页按 replace_page_options 来取。
//
// 列表里的增删改先只留在这次运行里，按「保存」才写回规则文件（与命令行读的是同一份）。
// 不自动写：删掉一条、改错一条都立刻变成盘上的事实，回头就找不回来了。所以每次改完
// 状态条都写着还没存下来，按钮也只有在有改动时才点得动。要把这一套搬到别的机器，
// 在「关于」页备份设置；恢复设置会把整份规则换掉，这一页的列表也跟着换。
#include <FL/Fl.H>
#include <FL/Fl_Box.H>
#include <FL/Fl_Button.H>
#include <FL/Fl_Hold_Browser.H>
#include <FL/Fl_Input.H>
#include <FL/Fl_Text_Buffer.H>
#include <FL/Fl_Text_Display.H>
#include <FL/Fl_Text_Editor.H>
#include <FL/Fl_Window.H>

#include <algorithm>
#include <cstddef>
#include <cstdlib>
#include <exception>
#include <string>
#include <utility>
#include <vector>

#include "engine/io.h"
#include "engine/processing.h"
#include "engine/schemes.h"
#include "ui/pages.h"
#include "ui/widgets.h"

namespace texttool::ui {
namespace {

// 这一页的行距与控件宽度。纵向位置由 layout() 从页面顶边往下、从页面底边往上两头算出来，
// 横向从左边界、字段列、右边界算，这里只写每段间隔与控件本身多宽，不写任何绝对坐标：
// 页面拿到多大就按多大排，窗口尺寸一变重排一次就够。
//
// 页面分三段：上面一行（标签与列表的标题）高度定死，中间一段吃掉剩下的高度（左边列表、
// 右边编辑列），下面两排小按钮贴着页面底边往上排。
constexpr int kTopGap = 12;              // 第一行离页面顶边多远
constexpr int kListWidth = 320;          // 左边规则列表定宽
constexpr int kListHeight = 168;         // 列表只占上半截，按钮紧跟着它，下面留白
constexpr int kColumnGap = 22;           // 列表与右边编辑列之间
constexpr int kFindCaptionWidth = 36;    // 「把：」
constexpr int kReplaceCaptionWidth = 60; // 「换成：」
constexpr int kInlineGap = 4;            // 同一行里标题与格子之间
constexpr int kEditorRowGap = 8;         // 编辑列里相邻两行之间
constexpr int kEditFieldWidth = 154;     // 编辑列两格按设计稿的宽度，不拉满
constexpr int kEditButtonWidth = 70;     // 「确定」
constexpr int kSmallButtonGap = 7;       // 同一排小按钮之间
constexpr int kSmallButtonWidth = 70;    // 「上移」「下移」「添加」「删除」
constexpr int kSmallRowGap = 8;          // 两排小按钮之间
constexpr int kListToButtonsGap = 12;    // 列表底边到第一排小按钮
constexpr int kPresetButtonWidth = 205;  // 「从预设里挑」
constexpr int kSaveButtonWidth = 68;     // 「保存」

// 编辑列一行的步长：行高 kFormRowHeight，行间 kEditorRowGap。
constexpr int kEditorRowStep = kFormRowHeight + kEditorRowGap;

// ---------------------------------------------------------------- 规则与文本

// 一条规则在列表里怎么写：换成空的那一条写成「删掉」。
std::string rule_label(const texttool::ReplaceRule& rule) {
    if (rule.second.empty()) return "把「" + rule.first + "」删掉";
    return "把「" + rule.first + "」换成「" + rule.second + "」";
}

// 一整份规则写成多行文本：一行一条「找=换」，右边空着就只剩一个等号。
std::string rules_to_text(const std::vector<texttool::ReplaceRule>& rules) {
    std::string text;
    for (const texttool::ReplaceRule& rule : rules) {
        text += rule.first;
        text += '=';
        text += rule.second;
        text += '\n';
    }
    if (!text.empty()) text.pop_back();  // 最后一行后面不留空行
    return text;
}

// 多行文本读成一整份规则：一行一条，第一处等号分开两半，两边去掉首尾空白。
// 没有等号的行按「换成空」算；空行与左边空着的行都不收。
std::vector<texttool::ReplaceRule> text_to_rules(const std::string& text) {
    std::vector<texttool::ReplaceRule> rules;
    std::size_t start = 0;
    while (start <= text.size()) {
        std::size_t end = text.find('\n', start);
        if (end == std::string::npos) end = text.size();
        std::string line = text.substr(start, end - start);
        if (!line.empty() && line.back() == '\r') line.pop_back();
        start = end + 1;

        line = texttool::trim_whitespace(line);
        if (line.empty()) continue;
        const std::size_t split = line.find('=');
        if (split == std::string::npos) {
            rules.emplace_back(line, std::string());
            continue;
        }
        std::string find = texttool::trim_whitespace(line.substr(0, split));
        if (find.empty()) continue;
        rules.emplace_back(std::move(find), texttool::trim_whitespace(line.substr(split + 1)));
    }
    return rules;
}

// ---------------------------------------------------------------- 编辑那扇小窗

// 编辑一扇方案的窗：方案名、整份规则（多行文本）、按「确定」时校验不过的那一句提示。
struct SchemeEditor {
    Fl_Window* window = nullptr;
    Fl_Input* name = nullptr;
    Fl_Text_Buffer* rules = nullptr;
    Fl_Box* warning = nullptr;
    const std::vector<std::string>* taken = nullptr;  // 别的方案的名字，重名要拦
    texttool::ReplaceScheme scheme;                   // 在副本上改，取消时调用方那份不动
    bool accepted = false;
};

// 编辑窗里那一行提示：校验没过时写一句，再按一次「确定」重试。
void editor_warn(SchemeEditor& editor, const std::string& text) {
    editor.warning->copy_label(text.c_str());
    editor.warning->redraw();
}

// 改一扇方案。返回 true 表示按了「确定」，改好的内容在 scheme 里；名字空着、与别人重名
// 或者规则一条都写不出来，就在窗里说一句，不关窗，也不写盘。
bool open_scheme_editor(Fl_Window* owner_window, const std::string& title,
                        texttool::ReplaceScheme& scheme,
                        const std::vector<std::string>& taken_names) {
    constexpr int kWidth = 520;
    constexpr int kHeight = 460;
    constexpr int kLeft = 14;
    constexpr int kLabelWidth = 70;
    constexpr int kFieldX = kLeft + kLabelWidth + 10;
    constexpr int kFieldWidth = kWidth - kLeft - kFieldX;
    constexpr int kFooterTop = kHeight - 46;

    SchemeEditor editor;
    editor.scheme = scheme;
    editor.taken = &taken_names;

    {
        Fl_Window window(0, 0, kWidth, kHeight, title.c_str());
        window.set_modal();
        window.color(kPageBg);
        window.begin();
        editor.window = &window;

        make_text(kLeft, 14, kLabelWidth, 30, "方案名：");
        editor.name = make_input(kFieldX, 14, kFieldWidth, 30, scheme.name);
        // 名字一改，下面那句提示就收起：那句只在按「确定」时才算数。
        editor.name->when(FL_WHEN_CHANGED);
        editor.name->callback([](Fl_Widget*, void* data) {
            static_cast<SchemeEditor*>(data)->warning->copy_label("");
        }, &editor);

        make_text(kLeft, 52, kWidth - 2 * kLeft, 20,
                  "规则一行一条，写成「找=换」：等号左边写要换掉的标点，右边写换成什么。", 13,
                  kMuted);
        make_text(kLeft, 72, kWidth - 2 * kLeft, 20,
                  "等号右边空着，就是把这个标点删掉；空行、没写「找」的行都不算一条。", 13,
                  kMuted);

        make_text(kLeft, 98, kLabelWidth, 22, "规则：", 14);
        editor.rules = new Fl_Text_Buffer();
        editor.rules->text(rules_to_text(scheme.rules).c_str());
        auto* rules = new Fl_Text_Editor(kLeft, 122, kWidth - 2 * kLeft, 236);
        rules->buffer(editor.rules);
        rules->textfont(FL_HELVETICA);
        rules->textsize(15);
        rules->textcolor(kInk);
        rules->color(kFieldBg);
        rules->box(FL_DOWN_BOX);
        rules->wrap_mode(Fl_Text_Display::WRAP_AT_BOUNDS, 0);

        editor.warning = make_text(kLeft, kFooterTop - 26, kWidth - 2 * kLeft, 22, "", 13,
                                   kWarnInk);

        auto* cancel = make_button(kWidth - kLeft - 76, kFooterTop, 76, 32, "取消");
        cancel->labelsize(14);
        cancel->callback([](Fl_Widget*, void* data) {
            static_cast<SchemeEditor*>(data)->window->hide();
        }, &editor);

        auto* ok = make_button(kWidth - kLeft - 76 - 8 - 76, kFooterTop, 76, 32, "确定");
        ok->labelsize(14);
        ok->callback([](Fl_Widget*, void* data) {
            auto* state = static_cast<SchemeEditor*>(data);

            texttool::ReplaceScheme edited = state->scheme;
            edited.name = texttool::trim_whitespace(state->name->value());
            if (edited.name.empty()) {
                editor_warn(*state, "方案名空着：写上这份方案叫什么，比如「中文标点换英文」");
                return;
            }
            if (state->taken != nullptr &&
                std::find(state->taken->begin(), state->taken->end(), edited.name) !=
                    state->taken->end()) {
                editor_warn(*state, "已经有一份叫「" + edited.name + "」的了：换个名字");
                return;
            }

            char* text = state->rules->text();
            edited.rules = text_to_rules(text == nullptr ? "" : text);
            std::free(text);
            if (edited.rules.empty()) {
                editor_warn(*state, "规则一条都没有：至少写一行，比如「，=,」");
                return;
            }

            state->scheme = std::move(edited);
            state->accepted = true;
            state->window->hide();
        }, &editor);

        window.callback([](Fl_Widget*, void* data) {
            static_cast<SchemeEditor*>(data)->window->hide();  // 点关闭框也算取消
        }, &editor);
        window.end();

        if (owner_window != nullptr) {
            const int px = owner_window->x() + (owner_window->w() - kWidth) / 2;
            const int py = owner_window->y() + (owner_window->h() - kHeight) / 2;
            window.position(px < 0 ? 0 : px, py < 0 ? 0 : py);
        }

        window.show();
        apply_title_bar(&window);
        // 模态窗口自己转事件循环：这一圈里主窗口收不到输入，退出后才回到本页。
        while (window.shown() != 0) Fl::wait();
    }  // 窗口与里面的控件在这里收掉，之后才能放开那份文本

    delete editor.rules;

    if (!editor.accepted) return false;
    scheme = std::move(editor.scheme);
    return true;
}

// ---------------------------------------------------------------- 方案管理那扇窗

// 「从预设里挑」那扇窗的全部东西。schemes 是窗里那份方案表：新建、编辑、删除都先改它，
// 写盘成功才算数，写不进去就整份退回改之前的样子。
struct SchemeChooser {
    Fl_Window* window = nullptr;
    Fl_Hold_Browser* list = nullptr;
    Fl_Box* note = nullptr;
    Fl_Button* use = nullptr;
    Fl_Button* edit = nullptr;
    Fl_Button* remove = nullptr;
    std::vector<texttool::ReplaceScheme> schemes;
    std::string program_path;  // 写盘按它找程序旁边的方案文件
    std::size_t current = 0;   // 选中的那一份，从 1 数；0 表示没选
    bool delete_pending = false;  // 「删除」已经按过一次，再按一次才真删
    bool used = false;            // 按了「用这份」：关窗后调用方把规则搬进列表
    std::size_t used_index = 0;
};

void chooser_note(SchemeChooser& state, const std::string& text) {
    state.note->copy_label(text.c_str());
    state.note->redraw();
}

// 按现在的选中状态把列表、按钮与「用这份」的启用关系摆一遍。不碰提示那一行：
// 提示是谁写的谁负责清，这里一刷就把它抹掉会让写它的人白写。
void refresh_chooser(SchemeChooser& state) {
    state.list->clear();
    for (const texttool::ReplaceScheme& scheme : state.schemes)
        state.list->add((scheme.name + "（" + std::to_string(scheme.rules.size()) + " 条规则）")
                            .c_str());
    if (state.current >= 1 && state.current <= state.schemes.size())
        state.list->select(static_cast<int>(state.current));
    state.list->redraw();

    const bool picked = state.current >= 1 && state.current <= state.schemes.size();
    if (picked) {
        state.use->activate();
        state.edit->activate();
    } else {
        state.use->deactivate();
        state.edit->deactivate();
    }
    // 删除要点两次：第一次把按钮上的字换成「确认删除」，同一个位置再按一次才删。
    state.remove->label(state.delete_pending ? "确认删除" : "删除…");
    if (picked) {
        state.remove->activate();
    } else {
        state.remove->deactivate();
    }
    if (state.schemes.empty()) chooser_note(state, "还没有方案：按「新建…」写一份。");
}

// 别的方案都叫什么：新建与改名时用来拦住重名。
std::vector<std::string> other_names(const SchemeChooser& state, std::size_t skip) {
    std::vector<std::string> names;
    for (std::size_t index = 0; index < state.schemes.size(); ++index) {
        if (index + 1 == skip) continue;
        names.push_back(state.schemes[index].name);
    }
    return names;
}

// 整份方案写回盘上。写不进去时屏上的表与盘上那份保持一致，原因写在窗里那一行：
// 屏幕上看到的就是盘上的那一份，不留「看着存好了、其实没存」的余地。
bool chooser_commit(SchemeChooser& state, std::vector<texttool::ReplaceScheme> next,
                    const std::string& done_note) {
    try {
        texttool::save_replace_schemes(state.program_path.c_str(), next);
    } catch (const std::exception& failure) {
        // 引擎能给的是「哪个文件没写进去」，再补一句该查什么，读者才动得了手。
        chooser_note(state, std::string("存不下来：") + failure.what() +
                                "（这个位置写不进去，看看是不是只读的、有没有权限）");
        return false;
    }
    state.schemes = std::move(next);
    state.delete_pending = false;
    refresh_chooser(state);
    chooser_note(state, done_note);
    return true;
}

void chooser_new(SchemeChooser& state) {
    texttool::ReplaceScheme added;  // 名字与规则都空着，等填
    if (!open_scheme_editor(state.window, "新建方案", added,
                            other_names(state, 0)))
        return;

    std::vector<texttool::ReplaceScheme> next = state.schemes;
    next.push_back(added);
    const std::string name = added.name;
    if (chooser_commit(state, std::move(next), "「" + name + "」存好了，盘上有这一份了")) {
        state.current = state.schemes.size();
        refresh_chooser(state);
    }
}

void chooser_edit(SchemeChooser& state) {
    if (state.current < 1 || state.current > state.schemes.size()) return;

    const std::size_t index = state.current - 1;
    texttool::ReplaceScheme edited = state.schemes[index];  // 在副本上改，取消时原样不动
    if (!open_scheme_editor(state.window, "编辑方案", edited, other_names(state, state.current)))
        return;

    std::vector<texttool::ReplaceScheme> next = state.schemes;
    next[index] = edited;
    const std::string name = edited.name;
    chooser_commit(state, std::move(next), "「" + name + "」存好了，盘上是改过的这一份");
}

void chooser_remove(SchemeChooser& state) {
    if (state.current < 1 || state.current > state.schemes.size()) return;

    if (!state.delete_pending) {
        state.delete_pending = true;
        refresh_chooser(state);
        chooser_note(state, "再按一次「确认删除」，「" + state.schemes[state.current - 1].name +
                                "」这一份就删掉了");
        return;
    }

    const std::string name = state.schemes[state.current - 1].name;
    std::vector<texttool::ReplaceScheme> next = state.schemes;
    next.erase(next.begin() + static_cast<std::ptrdiff_t>(state.current - 1));
    state.delete_pending = false;
    state.current = 0;
    chooser_commit(state, std::move(next), "「" + name + "」从盘上删掉了");
}

// 「从预设里挑」那扇窗。返回 true 表示按了「用这份」，这时 picked 里是那一份；
// 返回 false 表示取消，调用方手上的规则一条都不动。窗里新建、编辑、删除的方案
// 在按下去的时候就写回盘上了，取消也收不回来（删错的话再建一份）。
bool open_scheme_chooser(Fl_Window* owner_window, const std::string& program_path,
                         std::vector<texttool::ReplaceScheme>& schemes,
                         texttool::ReplaceScheme& picked) {
    constexpr int kWidth = 520;
    constexpr int kHeight = 396;
    constexpr int kLeft = 14;
    constexpr int kListTop = 52;
    constexpr int kListHeight = 268;
    constexpr int kFooterTop = kHeight - 46;

    SchemeChooser state;
    state.schemes = schemes;
    state.program_path = program_path;

    {
        Fl_Window window(0, 0, kWidth, kHeight, "从预设里挑");
        window.set_modal();
        window.color(kPageBg);
        window.begin();
        state.window = &window;

        make_text(kLeft, 6, kWidth - 2 * kLeft, 20,
                  "一份方案就是一整套换标点的规则。按「用这份」，它的规则整批换成左边那份列表。",
                  13, kMuted);
        make_text(kLeft, 26, kWidth - 2 * kLeft, 20,
                  "在这里新建、编辑、删除的方案会存回盘上，下次打开还在。", 13, kMuted);

        state.list = new Fl_Hold_Browser(kLeft, kListTop, kWidth - 2 * kLeft, kListHeight);
        state.list->textsize(15);
        state.list->callback([](Fl_Widget*, void* data) {
            auto* chooser = static_cast<SchemeChooser*>(data);
            const int chosen = chooser->list->value();
            chooser->current =
                chosen >= 1 ? static_cast<std::size_t>(chosen) : std::size_t{0};
            if (chooser->delete_pending) chooser->delete_pending = false;
            refresh_chooser(*chooser);
        }, &state);

        state.note = make_text(kLeft, kListTop + kListHeight + 4, kWidth - 2 * kLeft, 22, "", 13,
                               kWarnInk);

        state.use = make_button(kLeft, kFooterTop, 86, 32, "用这份");
        state.use->labelsize(14);
        state.use->callback([](Fl_Widget*, void* data) {
            auto* chooser = static_cast<SchemeChooser*>(data);
            if (chooser->current < 1 || chooser->current > chooser->schemes.size()) {
                chooser_note(*chooser, "先在清单里点一份方案");
                return;
            }
            chooser->used = true;
            chooser->used_index = chooser->current - 1;
            chooser->window->hide();
        }, &state);

        auto* create = make_button(kLeft + 94, kFooterTop, 86, 32, "新建…");
        create->labelsize(14);
        create->callback([](Fl_Widget*, void* data) {
            auto* chooser = static_cast<SchemeChooser*>(data);
            chooser->delete_pending = false;
            refresh_chooser(*chooser);
            chooser_note(*chooser, "");
            chooser_new(*chooser);
        }, &state);

        state.edit = make_button(kLeft + 188, kFooterTop, 86, 32, "编辑…");
        state.edit->labelsize(14);
        state.edit->callback([](Fl_Widget*, void* data) {
            auto* chooser = static_cast<SchemeChooser*>(data);
            if (chooser->current < 1) {
                chooser_note(*chooser, "先在清单里点一份方案，再按「编辑…」");
                return;
            }
            chooser->delete_pending = false;
            refresh_chooser(*chooser);
            chooser_note(*chooser, "");
            chooser_edit(*chooser);
        }, &state);

        state.remove = make_button(kLeft + 282, kFooterTop, 86, 32, "删除…");
        state.remove->labelsize(14);
        state.remove->callback([](Fl_Widget*, void* data) {
            chooser_remove(*static_cast<SchemeChooser*>(data));
        }, &state);

        auto* cancel = make_button(kWidth - kLeft - 76, kFooterTop, 76, 32, "取消");
        cancel->labelsize(14);
        cancel->callback([](Fl_Widget*, void* data) {
            static_cast<SchemeChooser*>(data)->window->hide();
        }, &state);

        window.callback([](Fl_Widget*, void* data) {
            static_cast<SchemeChooser*>(data)->window->hide();  // 点关闭框也算取消
        }, &state);
        window.end();

        // 开窗就选中第一份：进来多半就是为了挑一份。
        state.current = state.schemes.empty() ? 0 : 1;
        refresh_chooser(state);

        if (owner_window != nullptr) {
            const int px = owner_window->x() + (owner_window->w() - kWidth) / 2;
            const int py = owner_window->y() + (owner_window->h() - kHeight) / 2;
            window.position(px < 0 ? 0 : px, py < 0 ? 0 : py);
        }

        window.show();
        apply_title_bar(&window);
        // 模态窗口自己转事件循环：这一圈里主窗口收不到输入，退出后才回到本页。
        while (window.shown() != 0) Fl::wait();
    }

    schemes = state.schemes;  // 窗里存过的方案也跟着交回去，下一次打开是新的那份
    if (!state.used || state.used_index >= schemes.size()) return false;
    picked = schemes[state.used_index];
    return true;
}

// ---------------------------------------------------------------- 这一页

class ReplacePage : public Page {
public:
    ReplacePage(AppContext& app, int x, int y, int w, int h) : Page(x, y, w, h), app_(app) {
        box(FL_NO_BOX);
        color(kPageBg);
        begin();

        // 控件先按占位位置建好，实位一律在 layout() 里算：构造完调一次 reflow()，
        // 之后窗口尺寸一变，Page::resize 会再调一遍，所以这里写死哪个数都没有意义。
        rules_caption_ = make_text(x, y, kLabelWidth, kFormRowHeight, "要换的标点：");
        rules_ = new Fl_Hold_Browser(x, y, kListWidth, kFormRowHeight);
        rules_->textsize(15);
        rules_->callback(on_pick, this);

        // 右边编辑列：把、换成、确定。两格的左边对齐到同一条竖线。
        find_caption_ = make_text(x, y, kFindCaptionWidth, kFormRowHeight, "把：");
        find_ = make_input(x, y, kEditFieldWidth, kFormRowHeight);
        replace_caption_ = make_text(x, y, kReplaceCaptionWidth, kFormRowHeight, "换成：");
        replace_ = make_input(x, y, kEditFieldWidth, kFormRowHeight);
        confirm_ = make_button(x, y, kEditButtonWidth, kFormRowHeight, "确定");
        confirm_->labelsize(14);
        confirm_->callback(on_confirm, this);

        // 列表下面两排小按钮。
        up_ = make_button(x, y, kEditButtonWidth, kFormRowHeight, "上移");
        up_->labelsize(14);
        up_->callback(on_move_up, this);
        down_ = make_button(x, y, kEditButtonWidth, kFormRowHeight, "下移");
        down_->labelsize(14);
        down_->callback(on_move_down, this);
        add_ = make_button(x, y, kEditButtonWidth, kFormRowHeight, "添加");
        add_->labelsize(14);
        add_->callback(on_add, this);
        remove_ = make_button(x, y, kEditButtonWidth, kFormRowHeight, "删除");
        remove_->labelsize(14);
        remove_->callback(on_delete_selected, this);

        presets_ = make_button(x, y, kPresetButtonWidth, kFormRowHeight, "从预设里挑");
        presets_->labelsize(14);
        presets_->callback(on_presets, this);
        save_ = make_button(x, y, kSaveButtonWidth, kFormRowHeight, "保存");
        save_->labelsize(14);
        save_->callback(on_save, this);

        end();

        reflow();  // 按页面现有尺寸摆一遍

        load_rules_from_config();
        update_controls();
    }

    // 备份设置时要用：现在列表里的规则，以及建页面时读规则出的那个错。
    const std::vector<texttool::ReplaceRule>& rules_snapshot() const { return rules_data_; }
    const std::string& load_error() const { return load_error_; }

    // 恢复设置以后换掉列表。方案名与读错误一并清掉：现在这份表来自恢复的那一份。
    // 恢复时已经写进规则文件，所以列表与盘上的那份一致，没有「还没存下来」的改动。
    void apply_imported_rules(const std::vector<texttool::ReplaceRule>& rules) {
        rules_data_ = rules;
        saved_rules_ = rules;
        scheme_name_.clear();
        load_error_.clear();
        refresh_list();
    }

protected:
    // 这一页露出来时状态条说自己这一句：规则读不出来、还有没存下的改动、一条规则都没有，
    // 这三件事都要先说；都不是就说现在有几条规则、用的是哪一份方案。
    // 状态条只有一份文字，别的页写上去的话不会自己消失，所以每次露出来都重写一遍。
    void on_shown() override {
        if (!load_error_.empty()) {
            set_status("现在用的这几条换标点的规则读不出来：" + load_error_);
            return;
        }
        if (dirty()) {
            set_status("还有改动没存下来：按「保存」，下次打开还能用");
            return;
        }
        if (rules_data_.empty()) {
            set_status("还没有要换的标点：右边填一条按「添加」，或者「从预设里挑」一份方案");
            return;
        }
        set_status("现在有 " + std::to_string(rules_data_.size()) + " 条换标点的规则" +
                   scheme_note());
    }

    // 按页面现在的左上角与宽高摆一遍直接子控件：标签列从左边距起，字段列从 kPageFieldLeft
    // 起，右边到右边界为止。左边是列表，右边是编辑列；两排小按钮贴着页面底边往上排，
    // 中间剩下的高度全给列表。
    void layout() override {
        const int left = x() + kPagePadLeft;
        const int field_x = x() + kPageFieldLeft;
        const int right = x() + w() - kPagePadRight;

        const int row = y() + kTopGap;
        rules_caption_->resize(left, row, kLabelWidth, kFormRowHeight);

        // 列表只占上半截，按钮紧跟着列表往下排，下面空着。设计稿就是这样：
        // 列表拉满整页会让按钮离得太远，整页看着挤。
        rules_->resize(field_x, row, kListWidth, kListHeight);

        const int first_row = row + kListHeight + kListToButtonsGap;
        const int small_step = kSmallButtonWidth + kSmallButtonGap;
        up_->resize(field_x, first_row, kSmallButtonWidth, kFormRowHeight);
        down_->resize(field_x + small_step, first_row, kSmallButtonWidth, kFormRowHeight);
        add_->resize(field_x + 2 * small_step, first_row, kSmallButtonWidth, kFormRowHeight);
        remove_->resize(field_x + 3 * small_step, first_row, kSmallButtonWidth, kFormRowHeight);

        // 第二排：「从预设里挑」与「保存」按设计稿的宽度排，不把列表拉满。
        const int second_row = first_row + kFormRowHeight + kSmallRowGap;
        presets_->resize(field_x, second_row, kPresetButtonWidth, kFormRowHeight);
        save_->resize(field_x + kPresetButtonWidth + kSmallButtonGap, second_row, kSaveButtonWidth,
                      kFormRowHeight);

        // 右边编辑列：标题按各自的字数占宽，两格的右边界对齐在同一条竖线上。
        const int editor_x = field_x + kListWidth + kColumnGap;
        const int find_input_x = editor_x + kFindCaptionWidth + kInlineGap;
        const int replace_input_x = editor_x + kReplaceCaptionWidth + kInlineGap;
        const int find_width = std::max(0, std::min(kEditFieldWidth, right - find_input_x));
        const int replace_width = std::max(0, std::min(kEditFieldWidth, right - replace_input_x));

        find_caption_->resize(editor_x, row, kFindCaptionWidth, kFormRowHeight);
        find_->resize(find_input_x, row, find_width, kFormRowHeight);

        const int replace_row = row + kEditorRowStep;
        replace_caption_->resize(editor_x, replace_row, kReplaceCaptionWidth, kFormRowHeight);
        replace_->resize(replace_input_x, replace_row, replace_width, kFormRowHeight);

        // 「确定」压在「把：」那一列下面，与设计稿同一条竖线。
        confirm_->resize(editor_x, replace_row + kEditorRowStep, kEditButtonWidth, kFormRowHeight);
    }

private:
    // ------------------------------------------------------------ 规则列表

    // 建页面时读一次界面上配的那张规则表。读不出来就记下原因，列表留空。
    void load_rules_from_config() {
        std::string error;
        if (!texttool::load_replace_rules(app_.program_path.c_str(), rules_data_, error))
            load_error_ = error;
        saved_rules_ = rules_data_;  // 刚从盘上读来的，与盘上那份一样
        refresh_list();
    }

    // 列表与上次读进来（或存下去）的那份不一样，就是还有没存下来的改动。
    bool dirty() const { return rules_data_ != saved_rules_; }

    // 改动以后那句状态：存之前明说还没存下来，免得关了程序才发现白改。
    void report_edit(const std::string& what) {
        set_status(dirty() ? what + "，还没存下来：按「保存」" : what);
    }

    // 把 rules_data_ 写进列表。keep 是刷新后要选中的那一行（从 1 数），0 表示沿用当前选中的。
    void refresh_list(std::size_t keep = 0) {
        if (keep == 0) {
            const int current = rules_->value();
            if (current >= 1) keep = static_cast<std::size_t>(current);
        }
        rules_->clear();
        for (const texttool::ReplaceRule& rule : rules_data_)
            rules_->add(rule_label(rule).c_str());
        if (keep >= 1 && keep <= rules_data_.size()) rules_->select(static_cast<int>(keep));
        rules_->redraw();
        update_controls();
    }

    // 选中的那一行（从 1 数）；没选是 0。
    std::size_t selected() const {
        const int index = rules_->value();
        if (index < 1 || static_cast<std::size_t>(index) > rules_data_.size()) return 0;
        return static_cast<std::size_t>(index);
    }

    // 把选中的那条规则填进右边的两个格子，改之前先看见它现在长什么样。
    void fill_editor_from_selection() {
        const std::size_t index = selected();
        if (index == 0) return;
        const texttool::ReplaceRule& rule = rules_data_[index - 1];
        find_->value(rule.first.c_str());
        replace_->value(rule.second.c_str());
    }

    // 右边两个格子里的规则。左格空着就不算一条，调用方按需报那一句。
    bool read_editor(std::string& find, std::string& replace) const {
        find = texttool::trim_whitespace(find_->value());
        replace = texttool::trim_whitespace(replace_->value());
        return !find.empty();
    }

    // 状态条上那条「现在列表里是哪儿来的」的说明：一次运行只管一次搬进来的方案。
    std::string scheme_note() const {
        return scheme_name_.empty() ? std::string() : "（用的是「" + scheme_name_ + "」这份方案）";
    }

    // ---------------------------------------------------------------- 存下来

    // 把列表整份写回规则文件。写盘走引擎那一套：先写临时文件再整体改名，
    // 写不进去时原来的文件一个字没动，原因写在状态条上。
    void save_rules() {
        try {
            texttool::save_replace_rules(app_.program_path.c_str(), rules_data_);
        } catch (const std::exception& failure) {
            // 引擎能给的是「哪个文件没写进去」，再补一句该查什么，读者才动得了手。
            set_status(std::string("存不下来：") + failure.what() +
                       "（这个位置写不进去，看看是不是只读的、有没有权限）");
            return;
        }
        saved_rules_ = rules_data_;
        update_controls();
        set_status("已存下 " + std::to_string(rules_data_.size()) +
                   " 条换标点的规则，下次打开还在");
    }

    // ------------------------------------------------------- 「从预设里挑」

    void open_presets() {
        std::vector<texttool::ReplaceScheme> schemes;
        std::string error;
        if (!texttool::load_replace_schemes(app_.program_path.c_str(), schemes, error)) {
            set_status("方案读不出来：" + error);
            return;
        }
        // 一份方案都没有也照开：窗里能按「新建…」写一份，这个流程只是更短。

        texttool::ReplaceScheme picked;
        if (!open_scheme_chooser(app_.window, app_.program_path, schemes, picked)) return;

        rules_data_ = picked.rules;
        scheme_name_ = picked.name;
        refresh_list(1);
        report_edit("已换上「" + scheme_name_ + "」的 " + std::to_string(rules_data_.size()) +
                    " 条规则");
    }

    // -------------------------------------------------------------- 回调

    static void on_pick(Fl_Widget*, void* data) {
        auto* self = static_cast<ReplacePage*>(data);
        self->fill_editor_from_selection();
        // 选中一条以后，确定、删除、上移、下移才有对象：这几个按钮的启用关系跟着立刻刷新。
        self->update_controls();
    }

    // 按「确定」：选中了就改这一条，没选中就添一条新的。
    static void on_confirm(Fl_Widget*, void* data) {
        auto* self = static_cast<ReplacePage*>(data);
        std::string find;
        std::string replace;
        if (!self->read_editor(find, replace)) {
            set_status("「把」这一格空着，换不成东西：写上要换掉的标点");
            return;
        }
        const std::size_t index = self->selected();
        if (index == 0) {
            self->rules_data_.emplace_back(find, replace);
            self->scheme_name_.clear();
            self->refresh_list(self->rules_data_.size());
            self->report_edit("已添加第 " + std::to_string(self->rules_data_.size()) + " 条规则");
            return;
        }
        self->rules_data_[index - 1] = {find, replace};
        self->scheme_name_.clear();
        self->refresh_list(index);
        self->report_edit("已更新第 " + std::to_string(index) + " 条规则");
    }

    // 按「添加」：不管选没选中，都按右边那两格添一条新的。
    static void on_add(Fl_Widget*, void* data) {
        auto* self = static_cast<ReplacePage*>(data);
        std::string find;
        std::string replace;
        if (!self->read_editor(find, replace)) {
            set_status("「把」这一格空着，换不成东西：写上要换掉的标点");
            return;
        }
        self->rules_data_.emplace_back(find, replace);
        self->scheme_name_.clear();
        self->refresh_list(self->rules_data_.size());
        self->report_edit("已添加第 " + std::to_string(self->rules_data_.size()) + " 条规则");
    }

    static void on_delete_selected(Fl_Widget*, void* data) {
        auto* self = static_cast<ReplacePage*>(data);
        const std::size_t index = self->selected();
        if (index == 0) {
            set_status("先在左边点一条规则，再按「删除」");
            return;
        }
        self->rules_data_.erase(self->rules_data_.begin() + static_cast<std::ptrdiff_t>(index - 1));
        self->scheme_name_.clear();
        self->refresh_list(std::min(index, self->rules_data_.size()));
        self->report_edit("已删除第 " + std::to_string(index) + " 条规则");
    }

    // 上移下移：换的是相邻两条的次序。规则按次序一条一条套下去，次序变了结果可能不同，
    // 所以这两步不是摆设。
    static void move(ReplacePage* self, int delta) {
        const std::size_t index = self->selected();
        if (index == 0) {
            set_status("先在左边点一条规则，再按「上移」或「下移」");
            return;
        }
        const std::size_t target = delta < 0 ? index - 1 : index + 1;
        if (target < 1 || target > self->rules_data_.size()) return;
        std::swap(self->rules_data_[index - 1], self->rules_data_[target - 1]);
        self->scheme_name_.clear();
        self->refresh_list(target);
        self->report_edit("已挪到第 " + std::to_string(target) + " 条");
    }

    static void on_move_up(Fl_Widget*, void* data) {
        move(static_cast<ReplacePage*>(data), -1);
    }
    static void on_move_down(Fl_Widget*, void* data) {
        move(static_cast<ReplacePage*>(data), 1);
    }

    static void on_presets(Fl_Widget*, void* data) {
        static_cast<ReplacePage*>(data)->open_presets();
    }

    static void on_save(Fl_Widget*, void* data) {
        static_cast<ReplacePage*>(data)->save_rules();
    }

    // ------------------------------------------------------------ 按钮状态

    // 按钮的启用关系只从这里给，别处不要零散地 activate/deactivate。
    void update_controls() {
        // 「保存」只在有改动时点得动：没改的时候按它，屏幕上看不出发生了什么。
        if (dirty()) {
            save_->activate();
        } else {
            save_->deactivate();
        }

        // 名单里一条都没选时，改、删、挪都没有对象；到头了那一边的箭头就灰掉。
        const std::size_t index = selected();
        if (index == 0) {
            confirm_->deactivate();
            remove_->deactivate();
            up_->deactivate();
            down_->deactivate();
            return;
        }
        confirm_->activate();
        remove_->activate();
        if (index > 1) {
            up_->activate();
        } else {
            up_->deactivate();
        }
        if (index < rules_data_.size()) {
            down_->activate();
        } else {
            down_->deactivate();
        }
    }

    AppContext& app_;
    Fl_Box* rules_caption_ = nullptr;
    Fl_Hold_Browser* rules_ = nullptr;
    Fl_Box* find_caption_ = nullptr;
    Fl_Input* find_ = nullptr;
    Fl_Box* replace_caption_ = nullptr;
    Fl_Input* replace_ = nullptr;
    Fl_Button* confirm_ = nullptr;
    Fl_Button* up_ = nullptr;
    Fl_Button* down_ = nullptr;
    Fl_Button* add_ = nullptr;
    Fl_Button* remove_ = nullptr;
    Fl_Button* presets_ = nullptr;
    Fl_Button* save_ = nullptr;

    std::vector<texttool::ReplaceRule> rules_data_;  // 这次要用的规则，次序就是套用的次序
    std::vector<texttool::ReplaceRule> saved_rules_;  // 上次读进来或存下去的那份，用来认出还没存的改动
    std::string scheme_name_;                        // 从哪份方案搬来的；自己改过就清空
    std::string load_error_;                         // 规则表读不出来时的原因
};

// 备份设置、恢复设置都按页登记表找这一页。
ReplacePage* replace_page_of(AppContext& app) {
    return static_cast<ReplacePage*>(app.page(PageId::Replace));
}

}  // namespace

Page* build_replace_page(AppContext& app, int x, int y, int w, int h) {
    auto* page = new ReplacePage(app, x, y, w, h);
    app.register_page(PageId::Replace, page);
    return page;
}

std::vector<texttool::ReplaceRule> replace_page_rules(AppContext& app, std::string& error) {
    error.clear();
    ReplacePage* page = replace_page_of(app);
    if (page == nullptr) return {};
    error = page->load_error();
    return page->rules_snapshot();
}

void replace_page_apply_imported_rules(AppContext& app,
                                       const std::vector<texttool::ReplaceRule>& rules) {
    if (ReplacePage* page = replace_page_of(app)) page->apply_imported_rules(rules);
}

bool replace_page_options(AppContext& app, texttool::ReplaceOptions& options,
                          std::string& error) {
    error.clear();
    ReplacePage* page = replace_page_of(app);
    if (page == nullptr) {
        error = "换标点这一页还没建出来";
        return false;
    }
    const std::vector<texttool::ReplaceRule>& rules = page->rules_snapshot();
    if (rules.empty()) {
        error = "还没有要换的标点";
        return false;
    }
    options.rules = rules;
    // 一整份稿都在眼前，博文结构（头部字段、代码块、链接地址）要原样留住，
    // 与命令行 replace 的取法一样。
    options.protect_blog = true;
    return true;
}

}  // namespace texttool::ui
