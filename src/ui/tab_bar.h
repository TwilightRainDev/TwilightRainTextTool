// 页签层：顶端三项（处理、关于、高级），其中「高级」是自己画的下拉。
//
// 不用界面库自带的页签控件，也不用它自带的弹出菜单：页签它画的是带边框的凸起块；
// 菜单按系统那一套画，字号、行高、边框都与页签条对不上。两样都自己画，才有一副样子。
//
// 页签条：白底、无边框。当前页的字加重，字底下一道蓝色短线，长度就是那几个字的宽度。
// 下拉：一扇无边框小窗，圆角与按钮同一档，字号与颜色与页签条一致。
//       点「高级」展开；再点一次、点别处、按 Esc 都收起；上下键挪、回车或空格定下来。
// 面包屑：翻进下拉里某一页时页签条右端写「高级 > 换标点」，翻回顶项时写回产品名。
//       写在页签条右端、而不是只写窗口标题栏：标题栏由窗口管理器画，截图与录屏里不一定带着它。
//       窗口标题栏同时写同一句（见 set_title_window），两处一致。
//
// 数据结构：顶端每一项要么直接是一页（处理、关于），要么是一个下拉（高级），下拉里挂几页。
// 所有页另外按「先顶项页、再按挂进来的次序」排成一份扁平名单：页的扁平序号给环境变量
// TEXTTOOL_START_TAB 与版式检查用（序号表见 app.cpp 头注释），页与页之间不用它。
//
// 用法：先建 TabBar，再建各页的 Fl_Group（位置由 page_area 给出），
// 然后 add_top_page 挂顶项、add_menu_tab 加一个下拉、add_menu_item 往下拉里挂页。
#pragma once

#include <FL/Fl.H>
#include <FL/Fl_Group.H>
#include <FL/Fl_Window.H>
#include <FL/fl_draw.H>

#include <algorithm>
#include <functional>
#include <string>
#include <utility>
#include <vector>

#include "ui/theme.h"
#include "ui/widgets.h"

namespace texttool::ui {

// 页签条里字与线的那几个数。字号与页面上的正文一样大，页签看起来不会比正文小一档。
constexpr int kTabFontSize = 15;
constexpr int kTabPadX = 12;        // 一格页签里，字左边到格子左边多少（右边也这么多）
constexpr int kTabStart = 4;        // 第一格离页签条左边多少
constexpr int kTabCaretWidth = 16;  // 下拉那一项的尾巴上那个小尖角占的宽（含它与字的间隙）
constexpr int kTabUnderline = 3;    // 当前项下面那道短线的粗细

// 下拉小窗里的那几个数：行高、上下留白、字到框边的距离。
constexpr int kMenuRowHeight = 30;
constexpr int kMenuPadY = 6;
constexpr int kMenuPadX = 14;
constexpr int kMenuRowInset = 4;  // 指到某一行时那块底色离框边多少，看起来才像一行字

// 「高级」下拉弹出来的那一扇小窗。
//
// 不用界面库自带的弹出菜单：它按系统那一套画。这里只做下拉要用的那几件事：
// 一行一项、指到哪一行就衬哪一行、上下键挪、回车或空格定下来、Esc 或点外面收起。
// 收起与挑中是两件事：dismiss 只收起，pick 收起之后再调 on_pick。
class TabMenu : public Fl_Window {
public:
    TabMenu();

    // 弹出来。x、y 是屏幕坐标（窗口左上角），width 是最窄能有多宽；
    // selected 是现在翻到的那一项（-1 表示不在这一项上），挑中时调 on_pick(第几项)。
    void popup(int x, int y, int width, std::vector<std::string> items, int selected,
               std::function<void(int)> on_pick);

    void dismiss();
    // visible() 是 const 的，shown() 不是：这里只问「露没露着」，不问开在哪儿。
    bool opened() const { return visible() != 0; }

    void draw() override;
    int handle(int event) override;

private:
    std::vector<std::string> items_;
    std::function<void(int)> on_pick_;
    int focused_ = -1;   // 键盘或鼠标现在指着第几项
    int selected_ = -1;  // 现在翻到的是第几项（那几页里的哪一页）

    void pick(int index);
    int item_at(int mouse_y) const;
};

// 页签条。
//
// 子页与顶项页都是主窗口的直接子控件，这里只管谁露着：同一时刻只有一页可见。
class TabBar : public Fl_Group {
public:
    TabBar(int x, int y, int w, int h);
    ~TabBar() override;

    // 顶端一项直接是一页（处理、关于这样）。
    void add_top_page(const std::string& title, Fl_Group* page);

    // 顶端一项是一个下拉，返回它的序号（往下拉里挂页要用）。下拉一开始是空的。
    int add_menu_tab(const std::string& title);

    // 往下拉里挂一页。
    void add_menu_item(int top_index, const std::string& title, Fl_Group* page);

    // 面包屑上写的那一条产品名：翻在顶项上时写它。
    void set_product_name(std::string name);

    // 让窗口标题栏跟着面包屑走。不给的话标题栏不动，页签条右端那块照写。
    void set_title_window(Fl_Window* window);

    // 一页的内容区域，页签条下面那一块。content_bottom 是页面区到哪儿为止。
    void page_area(int& x, int& y, int& w, int& h, int content_bottom) const {
        x = this->x();
        y = this->y() + strip_height();
        w = this->w();
        h = content_bottom - y;
    }

    int page_height() const { return strip_height(); }

    // 页的扁平名单：顶项页在前，下拉里的按挂进来的次序。版式检查与自检按它认页。
    int page_count() const { return static_cast<int>(pages_.size()); }

    // 第 index 页；越界给 nullptr。页面之间的接力用它找目标页。
    Fl_Group* page(int index) const {
        if (index < 0 || index >= static_cast<int>(pages_.size())) return nullptr;
        return pages_[index];
    }

    // 按扁平序号翻页：收起下拉、换面包屑、通知那一页。
    void value(int index);

    // 现在露着的那一页的扁平序号。
    int value() const { return active_page_; }

    // 按页面对象翻页。自检按页标识找到页，再用它翻：页签怎么排都不牵动自检那一头。
    void select_page(Fl_Group* page);

    // 页签条位置一变，下拉那一块就对不上「高级」那一格了，先收起来。
    void resize(int x, int y, int w, int h) override {
        if (menu_ != nullptr) menu_->dismiss();
        Fl_Group::resize(x, y, w, h);
    }

    void draw() override;
    int handle(int event) override;

private:
    // 顶端一项。page 非空是「顶项自己就是一页」；items 非空是「下拉」。两种不会同时有。
    // selected 是下拉里现在翻到的那一项，-1 表示不在这一项上。
    struct Top {
        std::string title;
        Fl_Group* page = nullptr;
        std::vector<std::pair<std::string, Fl_Group*>> items;
        int selected = -1;
    };

    // 画东西与判鼠标点在哪都用这一段高：页签条比它高出来的那一截压在页面区上，
    // 被页面盖住，所以页签里只用到这么多。
    int strip_height() const;

    int slot_width(const Top& top) const;  // 一格顶项占多宽
    int hit_test(int mouse_x) const;       // 鼠标点在哪个顶项上，没点着给 -1
    int index_of(Fl_Group* page) const;

    void update_title();  // 面包屑那一条字的两种出口：页签条右端与窗口标题栏
    std::string title_text() const;
    void draw_caret(int cx, int cy, Fl_Color color) const;

    // 下拉那扇窗按需建：界面库建窗口时会把「当前组」指到它自己身上，
    // 那之后新建的控件就会挂进那扇窗里，所以建的时候把「当前组」记下来、建完放回去。
    TabMenu& menu();

    void open_menu(int top_index);
    void on_menu_pick(int top_index, int item);

    std::vector<Top> tops_;
    std::vector<Fl_Group*> pages_;
    int active_top_ = -1;
    int active_page_ = -1;
    int hover_ = -1;
    std::string product_name_;
    Fl_Window* title_window_ = nullptr;
    TabMenu* menu_ = nullptr;
};

// ---------------------------------------------------------------------------
// 下拉小窗
// ---------------------------------------------------------------------------

inline TabMenu::TabMenu() : Fl_Window(0, 0, 100, 100) {
    // 无边框、不受窗口管理器管：它是页签条拉出来的一块，不该有标题栏，也不该被摆到别处。
    border(0);
    set_override();
    color(kPageBg);
    end();
}

inline void TabMenu::popup(int x, int y, int width, std::vector<std::string> items, int selected,
                           std::function<void(int)> on_pick) {
    items_ = std::move(items);
    if (items_.empty()) return;
    on_pick_ = std::move(on_pick);
    selected_ = selected;
    focused_ = selected >= 0 ? selected : 0;  // 一拉开就有一行是亮着的，方向键接着往下走

    // 宽度：最长那一项加两边留白，但不少于页签那一格自己的宽度，看起来像从那一格里长出来的。
    fl_font(FL_HELVETICA, kTabFontSize);
    int text_width = 0;
    for (const std::string& item : items_)
        text_width = std::max(text_width, static_cast<int>(fl_width(item.c_str())));
    const int w = std::max(width, text_width + 2 * kMenuPadX);
    const int h = static_cast<int>(items_.size()) * kMenuRowHeight + 2 * kMenuPadY;

    resize(x, y, w, h);
    show();
    // 拉开以后事件先到这一扇窗：点在下拉外面那一下才收得起来（与系统菜单一样，
    // 那一下只用来收起，不传给底下的控件）。
    Fl::grab(this);
    redraw();
}

inline void TabMenu::dismiss() {
    if (!shown()) return;
    if (Fl::grab() == this) Fl::grab(nullptr);
    hide();
    on_pick_ = nullptr;
}

// 挑中：先收起，再调回调。回调里可能又翻页、又改标题，那都是收起之后的事。
inline void TabMenu::pick(int index) {
    if (index < 0 || index >= static_cast<int>(items_.size())) return;
    std::function<void(int)> callback = on_pick_;  // dismiss 会把 on_pick_ 清掉
    dismiss();
    if (callback) callback(index);
}

// 鼠标在这一扇窗的哪一行上。落在上下留白里给 -1（那一下只当点在外面）。
inline int TabMenu::item_at(int mouse_y) const {
    if (mouse_y < kMenuPadY) return -1;
    const int row = (mouse_y - kMenuPadY) / kMenuRowHeight;
    if (row < 0 || row >= static_cast<int>(items_.size())) return -1;
    return row;
}

inline void TabMenu::draw() {
    // 底先铺页面底色：圆角外面那四个小角露出来的就是它，与下拉后面那一页同色，
    // 远看是一块圆角的板，不是一扇方窗。
    fl_color(kPageBg);
    fl_rectf(0, 0, w(), h());

    // 圆角框：填输入框底色，沿边一道一像素，圆角与按钮同一档、同一套画法。
    fill_round_rect(0, 0, w(), h(), kButtonRadius, kFieldBg);
    stroke_round_rect(0, 0, w(), h(), kButtonRadius, kBorder);

    fl_font(FL_HELVETICA, kTabFontSize);
    for (int i = 0; i < static_cast<int>(items_.size()); ++i) {
        const int row_y = kMenuPadY + i * kMenuRowHeight;

        if (i == focused_) {
            // 指到哪一行就衬一块浅色，比整条通栏窄一点，看起来是一行字而不是一横条。
            fill_round_rect(kMenuRowInset, row_y + 1, w() - 2 * kMenuRowInset, kMenuRowHeight - 2,
                            kButtonRadius, mix_color(kFieldBg, kAccent, 0.16));
        }

        // 现在翻到的那一页用强调色写字，与「我正在哪儿」对得上。
        fl_color(i == selected_ ? kAccent : kInk);
        fl_draw(items_[i].c_str(), kMenuPadX, row_y, w() - 2 * kMenuPadX, kMenuRowHeight,
                FL_ALIGN_LEFT);
    }
}

inline int TabMenu::handle(int event) {
    switch (event) {
        case FL_PUSH: {
            const int hit = item_at(Fl::event_y());
            if (hit < 0) {
                dismiss();  // 点在下拉外面：收起
                return 1;
            }
            pick(hit);
            return 1;
        }
        case FL_MOVE: {
            const int hit = item_at(Fl::event_y());
            if (hit != focused_) {
                focused_ = hit;
                redraw();
            }
            return 1;
        }
        case FL_LEAVE:
            if (focused_ >= 0) {
                focused_ = -1;
                redraw();
            }
            return 1;
        case FL_KEYDOWN: {
            const int key = Fl::event_key();
            const int count = static_cast<int>(items_.size());
            if (key == FL_Up) {
                focused_ = focused_ <= 0 ? count - 1 : focused_ - 1;
                redraw();
                return 1;
            }
            if (key == FL_Down) {
                focused_ = focused_ >= count - 1 ? 0 : focused_ + 1;
                redraw();
                return 1;
            }
            if (key == FL_Enter || key == FL_KP_Enter || key == ' ') {
                pick(focused_);
                return 1;
            }
            if (key == FL_Escape) {
                dismiss();
                return 1;
            }
            return 1;  // 别的键不往下传：下拉开着的时候，按键不该落到页面上
        }
        default:
            return Fl_Window::handle(event);
    }
}

// ---------------------------------------------------------------------------
// 页签条
// ---------------------------------------------------------------------------

inline TabBar::TabBar(int x, int y, int w, int h) : Fl_Group(x, y, w, h) {
    box(FL_NO_BOX);
    end();
}

inline TabBar::~TabBar() {
    delete menu_;  // 下拉窗不是这一组的子控件，得自己删
}

inline int TabBar::strip_height() const {
    // 页签条比这个数高一点，高出来的那一截压在页面区上，被页面盖住。
    // 页签里的字与线只用到这么多，page_area 也从这里往下量。
    return 36;
}

inline void TabBar::add_top_page(const std::string& title, Fl_Group* page) {
    Top top;
    top.title = title;
    top.page = page;
    tops_.push_back(top);
    pages_.push_back(page);
    page->hide();
    if (active_page_ < 0) value(0);  // 第一页默认露出来
}

inline int TabBar::add_menu_tab(const std::string& title) {
    Top top;
    top.title = title;
    tops_.push_back(top);
    return static_cast<int>(tops_.size()) - 1;
}

inline void TabBar::add_menu_item(int top_index, const std::string& title, Fl_Group* page) {
    if (top_index < 0 || top_index >= static_cast<int>(tops_.size())) return;
    tops_[top_index].items.emplace_back(title, page);
    pages_.push_back(page);
    page->hide();
    if (active_page_ < 0) value(0);
}

inline void TabBar::set_product_name(std::string name) {
    product_name_ = std::move(name);
    update_title();
    redraw();
}

inline void TabBar::set_title_window(Fl_Window* window) {
    title_window_ = window;
    update_title();
}

inline int TabBar::index_of(Fl_Group* page) const {
    for (int i = 0; i < static_cast<int>(pages_.size()); ++i) {
        if (pages_[i] == page) return i;
    }
    return -1;
}

inline void TabBar::select_page(Fl_Group* page) {
    const int index = index_of(page);
    if (index >= 0) value(index);
}

inline void TabBar::value(int index) {
    if (index < 0 || index >= static_cast<int>(pages_.size())) return;

    // 下拉开着的时候翻页（快捷键、自检、截图那几路）先把它收起来。
    if (menu_ != nullptr) menu_->dismiss();

    for (int t = 0; t < static_cast<int>(tops_.size()); ++t) {
        Top& top = tops_[t];
        if (top.page == pages_[index]) {
            top.selected = -1;
            active_top_ = t;
        }
        for (int i = 0; i < static_cast<int>(top.items.size()); ++i) {
            if (top.items[i].second == pages_[index]) {
                top.selected = i;
                active_top_ = t;
            }
        }
    }

    for (Fl_Group* page : pages_) page->hide();
    pages_[index]->show();
    active_page_ = index;
    update_title();
    redraw();

    // 刚露出来的那一页把状态条改成描述自己现状的那一句。状态条只有一份文字，
    // 别的页写上去的话不会自己消失，所以每翻一次就重写一次。
    if (auto* page = dynamic_cast<Page*>(pages_[index])) page->on_shown();
}

inline std::string TabBar::title_text() const {
    if (active_top_ < 0 || active_top_ >= static_cast<int>(tops_.size())) return product_name_;
    const Top& top = tops_[active_top_];
    if (top.selected >= 0 && top.selected < static_cast<int>(top.items.size()))
        return top.title + " > " + top.items[top.selected].first;
    return product_name_;
}

inline void TabBar::update_title() {
    const std::string text = title_text();
    if (title_window_ == nullptr) return;
    const char* current = title_window_->label();
    if (current != nullptr && text == current) return;
    // 界面库的 label(const char*) 只记指针，拼出来的字必须走 copy_label。
    title_window_->copy_label(text.c_str());
}

inline int TabBar::slot_width(const Top& top) const {
    fl_font(FL_HELVETICA, kTabFontSize);
    int width = static_cast<int>(fl_width(top.title.c_str())) + 2 * kTabPadX;
    if (!top.items.empty()) width += kTabCaretWidth;
    return width;
}

inline int TabBar::hit_test(int mouse_x) const {
    int pen = x() + kTabStart;
    for (int i = 0; i < static_cast<int>(tops_.size()); ++i) {
        const int width = slot_width(tops_[i]);
        if (mouse_x >= pen && mouse_x < pen + width) return i;
        pen += width;
    }
    return -1;
}

inline void TabBar::draw_caret(int cx, int cy, Fl_Color color) const {
    // 下拉那一项尾巴上一个小尖角，让人看得出它点得开。线宽一像素，与边框一样细。
    fl_color(color);
    fl_line_style(FL_SOLID, 1);
    fl_line(cx, cy - 2, cx + 4, cy + 2);
    fl_line(cx + 4, cy + 2, cx + 8, cy - 2);
    fl_line_style(0);
}

inline void TabBar::draw() {
    fl_color(kPageBg);
    fl_rectf(x(), y(), w(), strip_height());

    fl_font(FL_HELVETICA, kTabFontSize);
    // 字在格子里上下居中，按字体高度算出字底那一行：短线贴在字底下，不按格子底边画
    // （那样会离字很远，看起来是两条不相干的线）。
    const int text_bottom = y() + strip_height() / 2 + fl_height() / 2;

    int pen = x() + kTabStart;
    for (int i = 0; i < static_cast<int>(tops_.size()); ++i) {
        const Top& top = tops_[i];
        const int width = slot_width(top);
        const int text_width = static_cast<int>(fl_width(top.title.c_str()));
        const int text_x = pen + kTabPadX;
        const bool current = (i == active_top_);

        fl_color(current ? kTabActive : kTabIdle);
        fl_draw(top.title.c_str(), text_x, y(), text_width, strip_height(), FL_ALIGN_LEFT);

        if (current) {
            // 当前项：字底下一道蓝色短线，长度就是这几个字的宽度。
            fl_color(kAccent);
            fl_rectf(text_x, text_bottom + 1, text_width, kTabUnderline);
        } else if (i == hover_) {
            // 鼠标扫过的那些：一道灰线垫在同一处，表示点得动，但不是「现在在这儿」。
            fl_color(kBorder);
            fl_rectf(text_x, text_bottom + 1, text_width, 1);
        }

        // 下拉那一项尾巴上的尖角：翻在下拉里某一页时也留着，用户要能看出「我现在在高级里面」。
        if (!top.items.empty())
            draw_caret(text_x + text_width + 8, y() + strip_height() / 2,
                       current ? kTabActive : kTabIdle);

        pen += width;
    }

    if (!product_name_.empty()) {
        // 面包屑：右端一条淡色的字。翻在顶项上写产品名，翻进下拉里写「高级 > 换标点」。
        fl_font(FL_HELVETICA, kTabFontSize);
        fl_color(kMuted);
        fl_draw(title_text().c_str(), x(), y(), w() - kTabPadX, strip_height(),
                FL_ALIGN_RIGHT | FL_ALIGN_INSIDE);
    }

    // 页签条下面那道通栏细线：留着，页签与页面之间有一道浅线分得开。
    fl_color(kTabLine);
    fl_rectf(x(), y() + strip_height() - 1, w(), 1);
}

inline int TabBar::handle(int event) {
    switch (event) {
        case FL_PUSH: {
            const int hit = hit_test(Fl::event_x());
            if (hit < 0) {
                if (menu_ != nullptr && menu_->opened()) menu_->dismiss();
                return 0;
            }
            const Top& top = tops_[hit];
            if (top.items.empty()) {
                value(index_of(top.page));
                return 1;
            }
            // 下拉那一项：开着就收起，没开就拉开（再点一次收起来）。
            if (menu_ != nullptr && menu_->opened()) {
                menu_->dismiss();
                return 1;
            }
            open_menu(hit);
            return 1;
        }
        case FL_MOVE: {
            const int hit = hit_test(Fl::event_x());
            if (hit != hover_) {
                hover_ = hit;
                redraw();
            }
            return 0;
        }
        case FL_LEAVE:
            if (hover_ != -1) {
                hover_ = -1;
                redraw();
            }
            return 0;
        default:
            return Fl_Group::handle(event);
    }
}

inline TabMenu& TabBar::menu() {
    if (menu_ == nullptr) {
        Fl_Group* previous = Fl_Group::current();
        menu_ = new TabMenu();
        Fl_Group::current(previous);  // 建窗会把「当前组」指过去，建完放回来
    }
    return *menu_;
}

inline void TabBar::open_menu(int top_index) {
    const Top& top = tops_[top_index];

    // 下拉的左上角对着这一项文字的左边，上沿贴着页签条底边：看起来像从这一项里拉出来的。
    fl_font(FL_HELVETICA, kTabFontSize);
    int pen = x() + kTabStart;
    for (int i = 0; i < top_index; ++i) pen += slot_width(tops_[i]);
    const int text_x = pen + kTabPadX;
    const int text_width = static_cast<int>(fl_width(top.title.c_str()));

    Fl_Window* owner = window();
    const int root_x = owner != nullptr ? owner->x_root() : 0;
    const int root_y = owner != nullptr ? owner->y_root() : 0;

    std::vector<std::string> titles;
    titles.reserve(top.items.size());
    for (const auto& item : top.items) titles.push_back(item.first);

    menu().popup(root_x + text_x, root_y + y() + strip_height(), text_width + 2 * kTabPadX,
                 std::move(titles), top.selected,
                 [this, top_index](int item) { on_menu_pick(top_index, item); });
}

inline void TabBar::on_menu_pick(int top_index, int item) {
    if (top_index < 0 || top_index >= static_cast<int>(tops_.size())) return;
    const Top& top = tops_[top_index];
    if (item < 0 || item >= static_cast<int>(top.items.size())) return;
    value(index_of(top.items[item].second));
}

}  // namespace texttool::ui
