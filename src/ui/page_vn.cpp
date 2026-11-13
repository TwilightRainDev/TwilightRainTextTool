// 剧本排版页：只放这一项的参数。
//
// 这一页管什么：怎么排（补标点也排段落、只排段落、只补标点，默认第一个）、每段最多几个字
// （默认 450）、用哪几份角色表里的哪几个名字。屏上的取值由处理页在跑之前取走
// （见 vn_page_options），换成引擎的 VnOptions。
//
// 「挑角色表…」打开两层清单：每份角色表一行，展开以后每个角色名、每条路线名各一行，
// 都能单独勾。勾中的名字按次序并起来交给引擎，各份的场景标记正则用竖线连成一条。
// 清单里还能新建、编辑、删除角色表；按「确定」以后整份存回盘上（程序旁边那份，或者
// 环境变量指的那份），命令行下一次用 --scheme 读到的就是这一份。
//
// 这一页不管什么：不读稿子、不碰文件、不跑排版、不显示结果。挑稿子、开始处理、看预览
// 都在「处理」那一页上；怎么断句、怎么分段全在引擎里。
#include <FL/Fl.H>
#include <FL/Fl_Box.H>
#include <FL/Fl_Button.H>
#include <FL/Fl_Group.H>
#include <FL/Fl_Input.H>
#include <FL/Fl_Scroll.H>
#include <FL/Fl_Text_Buffer.H>
#include <FL/Fl_Text_Display.H>
#include <FL/Fl_Text_Editor.H>
#include <FL/Fl_Window.H>
#include <FL/fl_draw.H>

#include <algorithm>
#include <cstddef>
#include <cstdlib>
#include <exception>
#include <functional>
#include <string>
#include <vector>

#include "engine/io.h"
#include "engine/processing.h"
#include "engine/schemes.h"
#include "ui/pages.h"
#include "ui/widgets.h"

namespace texttool::ui {
namespace {

// ---------------------------------------------------------------------------
// 挑角色表那扇窗
//
// 两层清单自己画，不借界面库的树：两层、三态、自绘的勾选框都只在这一处，配色跟界面
// 别处一套（theme.h 的槽位）。行高固定，点哪一行都按行高换算。
// ---------------------------------------------------------------------------

// 清单里一行的高度。
constexpr int kRowHeight = 26;

// 没有选中任何一份方案时 current 的值。
constexpr std::size_t kNoScheme = static_cast<std::size_t>(-1);

// 窗里正在改的那份表与勾选状态。勾选框按「这一份里的第几行」记，行号先角色名后路线名，
// 与 engine 的 VnSchemePick 同一个口径；改一份方案的行数以后整份重算。
struct SchemeChooser {
    std::vector<texttool::VnScheme> schemes;
    std::vector<std::vector<char>> checked;  // 与每份的行数同长
    std::vector<char> open;                  // 展开状态：打开窗时全展开
    std::vector<char> self_checked;          // 一份里一行都没有时，它自己那一勾
    std::size_t current = kNoScheme;         // 选中的那一份：编辑、删除的对象
    bool delete_pending = false;             // 已经问过「删不删」，再按一次就删
    std::string warning;                     // 清单上方那一行提示
};

// 一份里有几行名字：先角色名，后路线名。
std::size_t line_count(const texttool::VnScheme& scheme) {
    return scheme.characters.size() + scheme.route_names.size();
}

// 一份里勾了几行。
std::size_t checked_count(const SchemeChooser& state, std::size_t index) {
    std::size_t count = 0;
    for (const char on : state.checked[index])
        if (on != 0) ++count;
    return count;
}

// 这一份算不算用上了：名字行勾了任何一行，或者根本没有名字行的方案自己那一勾。
bool scheme_picked(const SchemeChooser& state, std::size_t index) {
    if (checked_count(state, index) > 0) return true;
    return line_count(state.schemes[index]) == 0 && state.self_checked[index] != 0;
}

// 方案那一行按一下：全勾着的就全不勾，半选或者一个没勾的全勾上。
// 一份里一行都没有时，这一勾只表示「用上它的场景标记」。
void toggle_scheme(SchemeChooser& state, std::size_t index) {
    if (state.checked[index].empty()) {
        state.self_checked[index] = state.self_checked[index] == 0 ? 1 : 0;
        return;
    }
    const bool fill = checked_count(state, index) != state.checked[index].size();
    for (char& on : state.checked[index]) on = fill ? 1 : 0;
}

// 勾选框：空着只有框，全勾是实心带对勾，只勾了一部分是实心带横杠。
void draw_check_box(int x, int y, bool on, bool partial) {
    constexpr int kSize = 14;
    if (on || partial) {
        fl_color(on ? kInk : kMuted);
        fl_rectf(x, y, kSize, kSize);
    }
    fl_color(kBorder);
    fl_line_style(FL_SOLID, 1);
    fl_rect(x, y, kSize, kSize);

    if (on || partial) {
        fl_color(FL_WHITE);
        fl_line_style(FL_SOLID, 2);
        if (on) {
            fl_line(x + 3, y + 7, x + 6, y + 10);
            fl_line(x + 6, y + 10, x + 11, y + 3);
        } else {
            fl_line(x + 3, y + 7, x + 11, y + 7);
        }
    }
    fl_line_style(FL_SOLID, 1);
}

// 展开与收起的小三角：收起时朝右，展开时朝下。
void draw_triangle(int x, int y, bool open) {
    fl_color(kMuted);
    fl_line_style(FL_SOLID, 2);
    if (open) {
        fl_line(x - 4, y - 2, x, y + 2);
        fl_line(x, y + 2, x + 4, y - 2);
    } else {
        fl_line(x - 2, y - 4, x + 2, y);
        fl_line(x + 2, y, x - 2, y + 4);
    }
    fl_line_style(FL_SOLID, 1);
}

// 一行字太长时截到栏里，末尾写一个省略号：右边缘不会留下半截括号或半个字。
void draw_fitted(const std::string& text, int x, int baseline, int limit_x) {
    if (x >= limit_x) return;
    const int available = limit_x - x;
    if (fl_width(text.c_str()) <= available) {
        fl_draw(text.c_str(), x, baseline);
        return;
    }

    // 按字切：一个字的字节数由首字节定，只整字地截。
    std::size_t bytes = 0;
    std::string cut;
    while (bytes < text.size()) {
        const auto lead = static_cast<unsigned char>(text[bytes]);
        const std::size_t width = lead < 0x80 ? 1 : (lead < 0xE0 ? 2 : (lead < 0xF0 ? 3 : 4));
        const std::string next = text.substr(0, bytes + width);
        if (fl_width((next + "…").c_str()) > available) break;
        cut = next;
        bytes += width;
    }
    fl_draw((cut + "…").c_str(), x, baseline);
}

// 方案那一行末尾的辅助信息：几个角色名、几条路线名。
std::string scheme_counts(const texttool::VnScheme& scheme) {
    std::string counts = std::to_string(scheme.characters.size()) + " 个角色名";
    if (!scheme.route_names.empty())
        counts += "、" + std::to_string(scheme.route_names.size()) + " 条路线名";
    return counts;
}

// 方案那一行右半边写的话：有说明先写说明，后面跟上条数。
std::string scheme_side_text(const texttool::VnScheme& scheme) {
    const std::string counts = scheme_counts(scheme);
    return scheme.description.empty() ? "  （" + counts + "）"
                                      : "  " + scheme.description + "（" + counts + "）";
}

// 两层清单。放在滚动区里，高度随行数长；滚轮交给外面的滚动区，这里只认左键。
class SchemeList : public Fl_Widget {
public:
    SchemeList(int x, int y, int w, int h, SchemeChooser& state)
        : Fl_Widget(x, y, w, h), state_(state) {
        box(FL_NO_BOX);
    }

    // 勾选、展开、增删以后叫一声：摘要、按钮的可用状态、滚动范围都跟着变。
    std::function<void()> on_change;

    // 外面那个滚动区。重排以后按新的高度把滚动位置夹回来。
    Fl_Scroll* scroll = nullptr;

    // 按现在这份表与展开状态重排行。行数变了高度跟着变，滚动区自己重算滚动条。
    void rebuild() {
        rows_.clear();
        for (std::size_t index = 0; index < state_.schemes.size(); ++index) {
            rows_.push_back(Row{false, index, 0});
            if (state_.open[index] == 0) continue;
            for (std::size_t line = 0; line < state_.checked[index].size(); ++line)
                rows_.push_back(Row{true, index, line});
        }

        const int wanted = static_cast<int>(rows_.size()) * kRowHeight;
        size(w(), wanted < kRowHeight ? kRowHeight : wanted);

        if (scroll != nullptr) {
            const int limit = std::max(0, h() - scroll->h());
            const int offset = std::clamp(scroll->yposition(), 0, limit);
            scroll->scroll_to(scroll->xposition(), offset);
            scroll->redraw();
        }
    }

    void draw() override {
        fl_color(kPageBg);
        fl_rectf(x(), y(), w(), h());
        fl_font(FL_HELVETICA, 15);

        if (rows_.empty()) {
            fl_color(kMuted);
            fl_font(FL_HELVETICA, 14);
            fl_draw("还没有角色表：按下面的「新建方案」先建一份。", x() + 10, y() + 22);
            return;
        }

        for (std::size_t index = 0; index < rows_.size(); ++index)
            draw_row(rows_[index], y() + static_cast<int>(index) * kRowHeight);
    }

    int handle(int event) override {
        if (event != FL_PUSH) return 0;  // 滚轮、滚条都交给外面的滚动区
        if (Fl::event_button() != FL_LEFT_MOUSE) return 0;

        const int index = (Fl::event_y() - y()) / kRowHeight;
        if (index < 0 || index >= static_cast<int>(rows_.size())) return 0;

        const Row row = rows_[static_cast<std::size_t>(index)];
        // 一动手就把上回那句「再按一次就删」收起来：换了对象再按，不该删掉别的。
        state_.delete_pending = false;
        state_.warning.clear();
        state_.current = row.scheme;

        if (!row.line) {
            // 行首那一小段是展开、收起；接着的方框是「这份全勾上或者全不勾」；
            // 按在名字上只是选中这一份（编辑、删除的对象），不碰勾选。
            const int offset = Fl::event_x() - x();
            if (offset < 22) {
                state_.open[row.scheme] = state_.open[row.scheme] == 0 ? 1 : 0;
            } else if (offset < 46) {
                toggle_scheme(state_, row.scheme);
            }
        } else {
            char& on = state_.checked[row.scheme][row.at];
            on = on == 0 ? 1 : 0;
        }

        if (on_change) on_change();
        return 1;
    }

private:
    // 一行：line 为假是方案那一行（at 不用），为真是这一份里的第 at 行名字。
    struct Row {
        bool line = false;
        std::size_t scheme = 0;
        std::size_t at = 0;
    };

    void draw_row(const Row& row, int row_y) {
        const int baseline = row_y + 18;
        fl_push_clip(x(), row_y, w(), kRowHeight);

        const texttool::VnScheme& scheme = state_.schemes[row.scheme];
        if (!row.line) {
            if (row.scheme == state_.current) {
                fl_color(kFieldBg);
                fl_rectf(x(), row_y, w(), kRowHeight);
            }
            draw_triangle(x() + 12, row_y + 13, state_.open[row.scheme] != 0);

            const std::size_t lines = state_.checked[row.scheme].size();
            const std::size_t count = checked_count(state_, row.scheme);
            const bool on = lines == 0 ? state_.self_checked[row.scheme] != 0 : count == lines;
            const bool partial = lines != 0 && count != 0 && count != lines;
            draw_check_box(x() + 26, row_y + 6, on, partial);

            int text_x = x() + 48;
            fl_color(kInk);
            fl_draw(scheme.name.c_str(), text_x, baseline);
            text_x += static_cast<int>(fl_width(scheme.name.c_str()));

            if (scheme.is_built_in) {
                const char* tag = "（内置）";
                fl_color(kMuted);
                fl_draw(tag, text_x, baseline);
                text_x += static_cast<int>(fl_width(tag));
            }

            fl_color(kMuted);
            draw_fitted(scheme_side_text(scheme), text_x, baseline, x() + w() - 6);
        } else {
            draw_check_box(x() + 42, row_y + 6, state_.checked[row.scheme][row.at] != 0, false);

            int text_x = x() + 66;
            const std::size_t characters = scheme.characters.size();
            if (row.at < characters) {
                fl_color(kInk);
                draw_fitted(scheme.characters[row.at], text_x, baseline, x() + w() - 6);
            } else {
                const char* prefix = "路线：";
                fl_color(kMuted);
                fl_draw(prefix, text_x, baseline);
                text_x += static_cast<int>(fl_width(prefix));
                fl_color(kInk);
                draw_fitted(scheme.route_names[row.at - characters], text_x, baseline,
                            x() + w() - 6);
            }
        }

        fl_pop_clip();
    }

    SchemeChooser& state_;
    std::vector<Row> rows_;
};

// 窗里那几个要随着勾选与选中项变化的东西。
struct ChooserWindow {
    Fl_Window* window = nullptr;
    SchemeChooser* state = nullptr;
    SchemeList* list = nullptr;
    Fl_Box* summary = nullptr;
    Fl_Box* warning = nullptr;
    Fl_Button* edit = nullptr;
    Fl_Button* remove = nullptr;
    bool accepted = false;
};

// 窗里那一句统计：勾了几份、几个角色名、几条路线名。与桌面版同一个口径。
std::string chooser_summary(const SchemeChooser& state) {
    std::size_t schemes = 0;
    std::size_t characters = 0;
    std::size_t routes = 0;
    for (std::size_t index = 0; index < state.schemes.size(); ++index) {
        if (!scheme_picked(state, index)) continue;
        ++schemes;
        const std::size_t characters_here = state.schemes[index].characters.size();
        for (std::size_t line = 0; line < state.checked[index].size(); ++line) {
            if (state.checked[index][line] == 0) continue;
            (line < characters_here ? characters : routes) += 1;
        }
    }

    if (schemes == 0) return "还没勾：一份都不勾就按通用写法认场景";
    return "勾了 " + std::to_string(schemes) + " 份角色表：" + std::to_string(characters) +
           " 个角色名、" + std::to_string(routes) + " 条路线名";
}

// 勾选状态、选中项变了以后刷新整扇窗：清单、统计、提示、三个按钮的可用状态。
void refresh_chooser(ChooserWindow& win) {
    SchemeChooser& state = *win.state;
    win.list->rebuild();
    win.summary->copy_label(chooser_summary(state).c_str());

    const bool chosen = state.current < state.schemes.size();
    const bool built_in = chosen && state.schemes[state.current].is_built_in;
    if (chosen) {
        win.edit->activate();
    } else {
        win.edit->deactivate();
    }
    // 内置的那几份只能改，不能删。
    if (chosen && !built_in) {
        win.remove->activate();
    } else {
        win.remove->deactivate();
    }
    win.remove->copy_label(state.delete_pending ? "确认删除" : "删除方案");

    // 提示那一行：先说正在问的「删不删」，再说内置不能删，最后才是别的提示。
    std::string note = state.warning;
    if (note.empty() && built_in) note = "内置的角色表不能删，可以由「编辑方案」改。";
    win.warning->copy_label(note.c_str());
    win.warning->labelcolor(state.warning.empty() ? kMuted : kWarnInk);

    win.window->redraw();
}

// 把一份方案里勾中的行收成引擎要的形状（engine 的 VnSchemePick）。
texttool::VnSchemePick make_pick(const SchemeChooser& state, std::size_t index) {
    texttool::VnSchemePick pick;
    pick.scheme = index;
    const std::size_t characters = state.schemes[index].characters.size();
    for (std::size_t line = 0; line < state.checked[index].size(); ++line) {
        if (state.checked[index][line] == 0) continue;
        if (line < characters) {
            pick.characters.push_back(line);
        } else {
            pick.route_names.push_back(line - characters);
        }
    }
    return pick;
}

// 除了这一份以外，别的方案都叫什么：新建与改名时用来拦住重名。
std::vector<std::string> other_names(const SchemeChooser& state, std::size_t skip) {
    std::vector<std::string> names;
    for (std::size_t index = 0; index < state.schemes.size(); ++index) {
        if (index == skip) continue;
        names.push_back(state.schemes[index].name);
    }
    return names;
}

// ---------------------------------------------------------------- 编辑那扇窗

// 多行文本按行拆：去掉空行，每行去掉首尾空白（与桌面版同一个口径）。
std::vector<std::string> split_lines(const std::string& text) {
    std::vector<std::string> lines;
    std::size_t start = 0;
    while (start <= text.size()) {
        std::size_t end = text.find('\n', start);
        if (end == std::string::npos) end = text.size();
        std::string line = text.substr(start, end - start);
        if (!line.empty() && line.back() == '\r') line.pop_back();
        line = texttool::trim_whitespace(line);
        if (!line.empty()) lines.push_back(line);
        start = end + 1;
    }
    return lines;
}

// 一行一个写成一份文本：最后一行后面不留空行。
std::string join_lines(const std::vector<std::string>& lines) {
    std::string text;
    for (const std::string& line : lines) {
        text += line;
        text += '\n';
    }
    if (!text.empty()) text.pop_back();
    return text;
}

// 编辑窗里那几样控件与正在改的副本。
struct EditorWindow {
    Fl_Window* window = nullptr;
    Fl_Input* name = nullptr;
    Fl_Input* description = nullptr;
    Fl_Input* scene_pattern = nullptr;
    Fl_Text_Buffer* characters_buffer = nullptr;
    Fl_Text_Buffer* routes_buffer = nullptr;
    Fl_Box* warning = nullptr;
    const std::vector<std::string>* taken = nullptr;  // 别的方案的名字，重名要拦
    texttool::VnScheme scheme;                        // 在副本上改，取消时调用方那份不动
    bool accepted = false;
};

// 编辑窗里那一行提示。改对了再按一次「确定」就关窗。
void editor_warn(EditorWindow& win, const std::string& text) {
    win.warning->copy_label(text.c_str());
    win.window->redraw();
}

// 编辑一扇角色表：名字、说明、场景标记正则、角色名一行一个、路线名一行一个。
// 返回 true 表示按了「确定」，改好的内容在 scheme 里；名字空着或者与别人重名
// 就在窗内提示，不关窗。
bool open_scheme_editor(Fl_Window* owner_window, texttool::VnScheme& scheme,
                        const std::vector<std::string>& taken_names) {
    constexpr int kWidth = 560;
    constexpr int kHeight = 502;
    constexpr int kLeft = 14;
    constexpr int kLabelWidth = 100;
    constexpr int kFieldX = kLeft + kLabelWidth + 10;
    constexpr int kFieldWidth = kWidth - kLeft - kFieldX;

    EditorWindow win;
    win.scheme = scheme;
    win.taken = &taken_names;

    {
        Fl_Window window(0, 0, kWidth, kHeight, "编辑角色表");
        window.set_modal();
        window.color(kPageBg);
        window.begin();
        win.window = &window;

        make_text(kLeft, 14, kLabelWidth, 30, "名字：");
        win.name = make_input(kFieldX, 14, kFieldWidth, 30, scheme.name);
        // 名字一改，下面那句提示就收起：那句只在按「确定」时才算数。
        win.name->when(FL_WHEN_CHANGED);
        win.name->callback([](Fl_Widget*, void* data) {
            static_cast<EditorWindow*>(data)->warning->copy_label("");
        }, &win);

        make_text(kLeft, 52, kLabelWidth, 30, "说明：");
        win.description = make_input(kFieldX, 52, kFieldWidth, 30, scheme.description);

        make_text(kLeft, 90, kLabelWidth, 30, "场景标记：");
        win.scene_pattern = make_input(kFieldX, 90, kFieldWidth, 30, scheme.scene_pattern);

        make_text(kLeft, 126, kWidth - 2 * kLeft, 20, "角色名，一行一个：", 14);
        win.characters_buffer = new Fl_Text_Buffer();
        win.characters_buffer->text(join_lines(scheme.characters).c_str());
        auto* characters = new Fl_Text_Editor(kLeft, 148, kWidth - 2 * kLeft, 148);
        characters->buffer(win.characters_buffer);
        characters->textfont(FL_HELVETICA);
        characters->textsize(15);
        characters->textcolor(kInk);
        characters->color(kFieldBg);
        characters->box(FL_DOWN_BOX);
        characters->wrap_mode(Fl_Text_Display::WRAP_AT_BOUNDS, 0);

        make_text(kLeft, 306, kWidth - 2 * kLeft, 20, "路线名，一行一个：", 14);
        win.routes_buffer = new Fl_Text_Buffer();
        win.routes_buffer->text(join_lines(scheme.route_names).c_str());
        auto* routes = new Fl_Text_Editor(kLeft, 328, kWidth - 2 * kLeft, 90);
        routes->buffer(win.routes_buffer);
        routes->textfont(FL_HELVETICA);
        routes->textsize(15);
        routes->textcolor(kInk);
        routes->color(kFieldBg);
        routes->box(FL_DOWN_BOX);
        routes->wrap_mode(Fl_Text_Display::WRAP_AT_BOUNDS, 0);

        win.warning = make_text(kLeft, 426, kWidth - 2 * kLeft, 22, "", 13, kWarnInk);

        auto* cancel = make_button(kWidth - kLeft - 76, 456, 76, 32, "取消");
        cancel->labelsize(14);
        cancel->callback([](Fl_Widget*, void* data) {
            static_cast<EditorWindow*>(data)->window->hide();
        }, &win);
        auto* ok = make_button(kWidth - kLeft - 76 - 8 - 76, 456, 76, 32, "确定");
        ok->labelsize(14);
        ok->callback([](Fl_Widget*, void* data) {
            auto* editor = static_cast<EditorWindow*>(data);

            texttool::VnScheme edited = editor->scheme;
            edited.name = texttool::trim_whitespace(editor->name->value());
            if (edited.name.empty()) {
                editor_warn(*editor, "名字空着：写上这份角色表叫什么，比如作品名");
                return;
            }
            if (editor->taken != nullptr &&
                std::find(editor->taken->begin(), editor->taken->end(), edited.name) !=
                    editor->taken->end()) {
                editor_warn(*editor, "已经有一份叫「" + edited.name + "」的了：换个名字");
                return;
            }

            edited.description = texttool::trim_whitespace(editor->description->value());
            edited.scene_pattern = texttool::trim_whitespace(editor->scene_pattern->value());

            char* characters = editor->characters_buffer->text();
            edited.characters = split_lines(characters == nullptr ? "" : characters);
            std::free(characters);
            char* routes = editor->routes_buffer->text();
            edited.route_names = split_lines(routes == nullptr ? "" : routes);
            std::free(routes);

            editor->scheme = std::move(edited);
            editor->accepted = true;
            editor->window->hide();
        }, &win);

        window.callback([](Fl_Widget*, void* data) {
            static_cast<EditorWindow*>(data)->window->hide();
        }, &win);
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
    }  // 窗口与里面的控件在这里收掉，之后才能放开那两份文本

    delete win.characters_buffer;
    delete win.routes_buffer;

    if (!win.accepted) return false;
    scheme = std::move(win.scheme);
    return true;
}

// ---------------------------------------------------------------- 三个按钮

void chooser_new(ChooserWindow& win) {
    SchemeChooser& state = *win.state;
    texttool::VnScheme added;  // 名字、说明、正则都空着，等填
    if (!open_scheme_editor(win.window, added, other_names(state, kNoScheme))) return;

    state.schemes.push_back(added);
    state.checked.emplace_back(line_count(added), 0);
    state.open.push_back(1);
    state.self_checked.push_back(0);
    state.current = state.schemes.size() - 1;
    state.delete_pending = false;
    state.warning.clear();
    refresh_chooser(win);
}

void chooser_edit(ChooserWindow& win) {
    SchemeChooser& state = *win.state;
    if (state.current >= state.schemes.size()) return;

    const std::size_t index = state.current;
    texttool::VnScheme edited = state.schemes[index];  // 在副本上改，取消时原样不动
    if (!open_scheme_editor(win.window, edited, other_names(state, index))) return;

    // 改之前这一份算用上了，改完就整份勾上：只改个名字不该让勾选凭空没了。
    const bool was_picked = scheme_picked(state, index);

    state.schemes[index] = edited;
    state.checked[index].assign(line_count(edited), 0);
    if (was_picked) {
        if (line_count(edited) == 0) {
            state.self_checked[index] = 1;
        } else {
            for (char& on : state.checked[index]) on = 1;
        }
    }
    state.delete_pending = false;
    state.warning.clear();
    refresh_chooser(win);
}

void chooser_remove(ChooserWindow& win) {
    SchemeChooser& state = *win.state;
    if (state.current >= state.schemes.size()) return;
    if (state.schemes[state.current].is_built_in) return;  // 按钮是灰的，这里再挡一次

    if (!state.delete_pending) {
        state.delete_pending = true;
        state.warning = "再按一次「确认删除」，「" + state.schemes[state.current].name +
                        "」这一份就删掉了";
        refresh_chooser(win);
        return;
    }

    const std::size_t index = state.current;
    state.schemes.erase(state.schemes.begin() + static_cast<std::ptrdiff_t>(index));
    state.checked.erase(state.checked.begin() + static_cast<std::ptrdiff_t>(index));
    state.open.erase(state.open.begin() + static_cast<std::ptrdiff_t>(index));
    state.self_checked.erase(state.self_checked.begin() + static_cast<std::ptrdiff_t>(index));
    state.current = kNoScheme;
    state.delete_pending = false;
    state.warning.clear();
    refresh_chooser(win);
}

// 挑角色表那扇窗。返回 true 表示按了「确定」，这时 schemes 与 picks 是新的；
// 返回 false 表示取消：调用方手上的东西一个字段都不动，盘上也不写。
bool open_scheme_chooser(Fl_Window* owner_window, std::vector<texttool::VnScheme>& schemes,
                         std::vector<texttool::VnSchemePick>& picks) {
    constexpr int kWidth = 660;
    constexpr int kHeight = 538;
    constexpr int kLeft = 14;
    constexpr int kListHeight = 380;
    constexpr int kListTop = 52;

    SchemeChooser state;
    state.schemes = schemes;
    state.checked.assign(state.schemes.size(), {});
    state.open.assign(state.schemes.size(), 1);  // 打开时全展开
    state.self_checked.assign(state.schemes.size(), 0);
    for (std::size_t index = 0; index < state.schemes.size(); ++index)
        state.checked[index].assign(line_count(state.schemes[index]), 0);

    // 上次挑的样子找回来：哪一份勾了哪几行。行数变了（文件被改过）就只认还在的那几行。
    for (const texttool::VnSchemePick& pick : picks) {
        if (pick.scheme >= state.schemes.size()) continue;
        const texttool::VnScheme& scheme = state.schemes[pick.scheme];
        for (const std::size_t line : pick.characters)
            if (line < scheme.characters.size()) state.checked[pick.scheme][line] = 1;
        for (const std::size_t line : pick.route_names)
            if (line < scheme.route_names.size())
                state.checked[pick.scheme][scheme.characters.size() + line] = 1;
        if (line_count(scheme) == 0) state.self_checked[pick.scheme] = 1;
    }

    ChooserWindow win;
    win.state = &state;

    {
        Fl_Window window(0, 0, kWidth, kHeight, "挑角色表");
        window.set_modal();
        window.color(kPageBg);
        window.begin();
        win.window = &window;

        make_text(kLeft, 6, kWidth - 2 * kLeft, 20,
                  "每份角色表一行：按那一行的框，这份里的名字全勾上或者全不勾。", 13, kMuted);
        make_text(kLeft, 26, kWidth - 2 * kLeft, 20,
                  "按最左边的小三角展开，每个角色名、每条路线名都能单独勾。", 13, kMuted);

        Fl_Scroll scroll(kLeft, kListTop, kWidth - 2 * kLeft, kListHeight);
        scroll.type(Fl_Scroll::VERTICAL_ALWAYS);
        scroll.box(FL_NO_BOX);
        scroll.color(kPageBg);
        scroll.begin();
        SchemeList list(kLeft, kListTop, kWidth - 2 * kLeft - Fl::scrollbar_size(), 100, state);
        scroll.end();
        list.scroll = &scroll;
        win.list = &list;

        win.summary = make_text(kLeft, kListTop + kListHeight + 6, kWidth - 2 * kLeft, 22, "", 14);
        win.warning = make_text(kLeft, kListTop + kListHeight + 30, kWidth - 2 * kLeft, 22, "", 13,
                                kWarnInk);

        constexpr int kFooterTop = kHeight - 46;
        auto* add = make_button(kLeft, kFooterTop, 100, 32, "新建方案");
        add->labelsize(14);
        add->callback([](Fl_Widget*, void* data) {
            chooser_new(*static_cast<ChooserWindow*>(data));
        }, &win);
        win.edit = make_button(kLeft + 108, kFooterTop, 100, 32, "编辑方案");
        win.edit->labelsize(14);
        win.edit->callback([](Fl_Widget*, void* data) {
            chooser_edit(*static_cast<ChooserWindow*>(data));
        }, &win);
        win.remove = make_button(kLeft + 216, kFooterTop, 100, 32, "删除方案");
        win.remove->labelsize(14);
        win.remove->callback([](Fl_Widget*, void* data) {
            chooser_remove(*static_cast<ChooserWindow*>(data));
        }, &win);

        auto* cancel = make_button(kWidth - kLeft - 76, kFooterTop, 76, 32, "取消");
        cancel->labelsize(14);
        cancel->callback([](Fl_Widget*, void* data) {
            static_cast<ChooserWindow*>(data)->window->hide();
        }, &win);
        auto* ok = make_button(kWidth - kLeft - 76 - 8 - 76, kFooterTop, 76, 32, "确定");
        ok->labelsize(14);
        ok->callback([](Fl_Widget*, void* data) {
            auto* chooser = static_cast<ChooserWindow*>(data);
            chooser->accepted = true;
            chooser->window->hide();
        }, &win);

        list.on_change = [&win]() { refresh_chooser(win); };
        refresh_chooser(win);

        window.callback([](Fl_Widget*, void* data) {
            static_cast<ChooserWindow*>(data)->window->hide();  // 点关闭框也算取消
        }, &win);
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
    }

    if (!win.accepted) return false;

    schemes = state.schemes;
    picks.clear();
    for (std::size_t index = 0; index < state.schemes.size(); ++index)
        if (scheme_picked(state, index)) picks.push_back(make_pick(state, index));
    return true;
}

// ---------------------------------------------------------------- 这一页

// 这一页的行距与控件宽度。纵向位置由 layout() 从页面顶边一行一行累加出来，横向从左边界、
// 字段列、右边界算，这里不写任何绝对坐标：页面拿到多大就按多大排，窗口尺寸一变重排一次就够。
//
// 这几行控件从左往右都短，页面再窄也放得下：三个单选按文案量宽（choice_width 量的），
// 数字框与「挑角色表…」是定宽，摘要那一句给满字段列，太长就在右边截掉。
constexpr int kTopGap = 12;           // 第一行离页面顶边多远
constexpr int kRowGap = 8;            // 相邻两行之间
constexpr int kRadioGap = 20;         // 三个单选之间留宽些，文字与下一个圆环才分得开
constexpr int kNumberBoxWidth = 110;  // 数字区 42 + 两个加减按钮
constexpr const char* kBothLabel = "补标点，也排段落";
constexpr const char* kLayoutOnlyLabel = "只排段落";
constexpr const char* kPunctOnlyLabel = "只补标点";
constexpr int kPickButtonWidth = 120;  // 「挑角色表…」
constexpr int kSummaryHeight = 26;     // 角色表那一句摘要

class VnPage : public Page {
public:
    VnPage(AppContext& app, int x, int y, int w, int h) : Page(x, y, w, h), app_(app) {
        box(FL_NO_BOX);
        color(kPageBg);
        begin();

        // 控件先按占位位置建好，实位一律在 layout() 里算：构造完调一次 reflow()，
        // 之后窗口尺寸一变，Page::resize 会再调一遍，所以这里写死哪个数都没有意义。
        mode_caption_ = make_text(x, y, kLabelWidth, kFormRowHeight, "怎么排：");
        // 次序照设计稿：都做、只排段落、只补标点。
        both_ = new FlatRadio(x, y, choice_width(kBothLabel), kFormRowHeight, kBothLabel);
        layout_only_ = new FlatRadio(x, y, choice_width(kLayoutOnlyLabel), kFormRowHeight,
                                     kLayoutOnlyLabel);
        punct_only_ = new FlatRadio(x, y, choice_width(kPunctOnlyLabel), kFormRowHeight,
                                    kPunctOnlyLabel);
        // 三个是一组单选：类型都设成 FL_RADIO_BUTTON，它们才互斥；不加这一行，
        // 三个能同时亮，看着像三件事都在选。
        both_->type(FL_RADIO_BUTTON);
        punct_only_->type(FL_RADIO_BUTTON);
        layout_only_->type(FL_RADIO_BUTTON);
        both_->labelsize(15);
        punct_only_->labelsize(15);
        layout_only_->labelsize(15);
        both_->value(1);

        max_para_caption_ = make_text(x, y, kLabelWidth, kFormRowHeight, "每段最多几个字：");
        max_para_ = new NumberBox(x, y, kNumberBoxWidth, kFormRowHeight, 100, 2000, 450, 42);

        scheme_caption_ = make_text(x, y, kLabelWidth, kFormRowHeight, "角色表：");
        pick_schemes_ = make_button(x, y, kPickButtonWidth, kFormRowHeight, "挑角色表…");
        pick_schemes_->labelsize(15);
        pick_schemes_->callback(on_pick_schemes, this);
        scheme_summary_ = make_text(x, y, kLabelWidth, kSummaryHeight, "", 14, kMuted);

        end();

        reflow();  // 按页面现有尺寸摆一遍
        update_scheme_summary();
    }

    // 恢复设置以后重新读一遍角色表：读进来的那份与屏上勾的那几份对不上，勾选一并清掉。
    void reload_schemes() {
        schemes_loaded_ = false;
        picks_.clear();
        schemes_.clear();
        schemes_error_.clear();
        update_scheme_summary();
    }

    // 把屏上的取值填进 options。挑过角色表就把勾中的名字并进去；并的时候发现文件被改过、
    // 勾的行对不上，就停下来报原因，不拿半份表去排版。
    bool read_options(texttool::VnOptions& options, std::string& error) {
        options = texttool::VnOptions{};
        options.do_fix_punct = punct_only_->value() != 0 || both_->value() != 0;
        options.do_reformat = layout_only_->value() != 0 || both_->value() != 0;
        options.max_paragraph = max_para_->value();

        if (picks_.empty()) return true;
        // 挑过角色表就先确认那份表还读得出来：文件被删掉、改坏以后不拿半份表去排版。
        if (!ensure_schemes(error)) return false;
        return texttool::combine_vn_scheme_picks(schemes_, picks_, options, error);
    }

protected:
    // 按页面现在的左上角与宽高摆一遍直接子控件：标签列从左边距起，控件从字段列起，
    // 右边到右边界为止，纵向从顶边一行一行往下累加。窗口最小尺寸下这些宽度都还是正的。
    void layout() override {
        const int left = x() + kPagePadLeft;
        const int field_x = x() + kPageFieldLeft;
        const int right = x() + w() - kPagePadRight;
        const int field_width = std::max(0, right - field_x);

        int row = y() + kTopGap;

        // 三个单选从左往右排，宽度按文案量好，剩下的空白留在右边。
        mode_caption_->resize(left, row, kLabelWidth, kFormRowHeight);
        const int both_w = choice_width(kBothLabel);
        both_->resize(field_x, row, both_w, kFormRowHeight);
        const int layout_only_x = field_x + both_w + kRadioGap;
        const int layout_only_w = choice_width(kLayoutOnlyLabel);
        layout_only_->resize(layout_only_x, row, layout_only_w, kFormRowHeight);
        punct_only_->resize(layout_only_x + layout_only_w + kRadioGap, row,
                            choice_width(kPunctOnlyLabel), kFormRowHeight);
        row += kFormRowHeight + kRowGap;

        // 数字框宽度固定（里面按数字区算分隔线），所以它跨尺寸时只换位置、不改宽度。
        max_para_caption_->resize(left, row, kLabelWidth, kFormRowHeight);
        max_para_->resize(field_x, row, kNumberBoxWidth, kFormRowHeight);
        row += kFormRowHeight + kRowGap;

        scheme_caption_->resize(left, row, kLabelWidth, kFormRowHeight);
        pick_schemes_->resize(field_x, row, kPickButtonWidth, kFormRowHeight);
        row += kFormRowHeight;

        // 摘要那一句能长到一整行（用了几份、哪几份），给满字段列，不折行时靠右边截。
        scheme_summary_->resize(field_x, row, field_width, kSummaryHeight);
    }

    // 翻到这一页时状态条说自己这一句。参数在这里设，真的排版要到处理页上开。
    // 状态条只有一份文字，不重写就留着别页写的话。
    void on_shown() override {
        set_status("怎么排、每段最多几个字、用哪几份角色表都在这里设，设好就到「处理」页勾上排版视觉小说");
    }

private:
    // ------------------------------------------------------------ 角色表

    // 读一次角色表并记在屏上。挑角色表、交参数都从这里起头；读不出来时把原因留下，
    // 别处再问就直接说这一句，不反复读盘。
    bool ensure_schemes(std::string& error) {
        if (schemes_loaded_) {
            if (!schemes_error_.empty()) {
                error = schemes_error_;
                return false;
            }
            return true;
        }
        schemes_loaded_ = true;
        schemes_.clear();
        schemes_error_.clear();

        std::vector<texttool::VnScheme> loaded;
        std::string reason;
        if (!texttool::load_vn_schemes(app_.program_path.c_str(), loaded, reason)) {
            schemes_error_ = reason;
            error = reason;
            return false;
        }
        schemes_ = std::move(loaded);
        return true;
    }

    // 屏上那一行摘要：用了几份、哪几份。读不出来时把原因写在这儿。
    void update_scheme_summary() {
        std::string note;
        std::string error;
        if (!ensure_schemes(error)) {
            note = "（角色表读不出来：" + error + "）";
        } else if (!picks_.empty()) {
            note = picked_note();
        } else if (schemes_.empty()) {
            note = "（还没有角色表：按「挑角色表…」自己建一份，或者按通用写法认场景）";
        } else {
            note = "（没挑角色表：按「挑角色表…」选一份，或者按通用写法认场景）";
        }
        scheme_summary_->copy_label(note.c_str());
        scheme_summary_->labelcolor(picks_.empty() ? kMuted : kInk);
        scheme_summary_->redraw();
    }

    // 用了几份、哪几份。状态条上那一句也是它。
    std::string picked_note() const {
        if (picks_.empty()) return "没挑角色表：按通用写法认场景";

        std::string names;
        for (const texttool::VnSchemePick& pick : picks_) {
            if (pick.scheme >= schemes_.size()) continue;
            if (!names.empty()) names += "、";
            names += schemes_[pick.scheme].name;
        }
        return "用了 " + std::to_string(picks_.size()) + " 份角色表：" + names;
    }

    // 挑角色表：打开两层清单，按「确定」以后把这份表存回盘上，屏上换成挑好的那几份。
    void open_schemes() {
        std::string error;
        if (!ensure_schemes(error)) {
            set_status("角色表读不出来：" + error);
            return;
        }

        std::vector<texttool::VnScheme> schemes = schemes_;
        std::vector<texttool::VnSchemePick> picks = picks_;
        if (!open_scheme_chooser(app_.window, schemes, picks)) return;

        schemes_ = std::move(schemes);
        picks_ = std::move(picks);
        schemes_error_.clear();

        update_scheme_summary();

        // 存回盘上：命令行下一次用 --scheme 读到的就是这一份。存不下来时说清楚，
        // 这一次运行照旧按屏上这份排。
        try {
            texttool::save_vn_schemes(app_.program_path.c_str(), schemes_);
        } catch (const std::exception& failure) {
            set_status(std::string("角色表存不下来：") + failure.what() +
                       "（这个位置写不进去，看看是不是只读的、有没有权限）");
            return;
        }
        set_status(picked_note());
    }

    static void on_pick_schemes(Fl_Widget*, void* data) {
        static_cast<VnPage*>(data)->open_schemes();
    }

    AppContext& app_;

    Fl_Box* mode_caption_ = nullptr;
    Fl_Box* max_para_caption_ = nullptr;
    Fl_Box* scheme_caption_ = nullptr;
    FlatRadio* both_ = nullptr;
    FlatRadio* punct_only_ = nullptr;
    FlatRadio* layout_only_ = nullptr;
    NumberBox* max_para_ = nullptr;
    Fl_Button* pick_schemes_ = nullptr;
    Fl_Box* scheme_summary_ = nullptr;

    std::vector<texttool::VnScheme> schemes_;    // 屏上这份角色表：读盘读来的，或者窗里改过的
    std::vector<texttool::VnSchemePick> picks_;  // 挑中的那几份与那几行
    std::string schemes_error_;                  // 角色表读不出来时的原因
    bool schemes_loaded_ = false;                // 读没读过（读过就不再读盘）
};

// 按页登记表找这一页：恢复设置与处理页取参数都从这里走。
VnPage* vn_page_of(AppContext& app) {
    return static_cast<VnPage*>(app.page(PageId::Vn));
}

}  // namespace

Page* build_vn_page(AppContext& app, int x, int y, int w, int h) {
    auto* page = new VnPage(app, x, y, w, h);
    app.register_page(PageId::Vn, page);
    return page;
}

void vn_page_reload_schemes(AppContext& app) {
    if (VnPage* page = vn_page_of(app)) page->reload_schemes();
}

bool vn_page_options(AppContext& app, texttool::VnOptions& options, std::string& error) {
    VnPage* page = vn_page_of(app);
    if (page == nullptr) {
        error = "剧本排版那一页还没建出来，取不到参数";
        return false;
    }
    return page->read_options(options, error);
}

}  // namespace texttool::ui
