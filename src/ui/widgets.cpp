// 共用控件的实现：文字与输入框的小工厂、状态条、数字框、页面基类、逐份处理的跑法。
#include "ui/widgets.h"

#include <FL/Fl.H>
#include <FL/Fl_Box.H>
#include <FL/Fl_Button.H>
#include <FL/Fl_Choice.H>
#include <FL/Fl_Hold_Browser.H>
#include <FL/Fl_Input.H>
#include <FL/Fl_PNG_Image.H>
#include <FL/Fl_Window.H>
#include <FL/fl_ask.H>
#include <FL/fl_draw.H>

#include <algorithm>
#include <cctype>
#include <cstdlib>
#include <fstream>
#include <exception>
#include <filesystem>
#include <string>
#include <system_error>
#include <utility>
#include <vector>

#include "engine/io.h"
#include "engine/schemes.h"

namespace texttool::ui {
namespace {

namespace fs = std::filesystem;

Fl_Box* g_status_bar = nullptr;
std::string g_status_text;  // 状态条上那一句，状态条还没挂上时也要留得住

// 勾选框与单选圈的边长（圆的直径）：字高 15 的正文配这么大，点起来也有准头。
constexpr int kChoiceMark = 18;
// 方块（圆盘）到文字的间隔。
constexpr int kChoiceGap = 10;
// 勾选框的小方块自己的圆角：方块边上只有 18 像素，硬撑 kButtonRadius 就成一块圆饼了。
// 走的是与按钮同一套逐行递减的画法，只是半径小：3 就是 2、1、0 三级。
constexpr int kChoiceRadius = 3;
// 选中单圈中间那个白孔的直径。
constexpr int kChoiceHole = 6;

// 圆角半径按控件自己的尺寸收一收：半径超过一半就没法收出角来。
// 勾选框那种小方块的半径本来就小，收了也不变；只有极窄的控件会碰上。
int clamped_radius(int w, int h, int radius) {
    const int limit = std::min(w, h) / 2;
    if (radius > limit) radius = limit;
    return radius < 0 ? 0 : radius;
}

// 第 dy 行（从顶边数起）左右各收进多少像素。见 widgets.h 里 stroke_round_rect 的说明。
int round_inset(int dy, int h, int radius) {
    if (radius <= 0 || h <= 0 || dy < 0 || dy >= h) return 0;
    // 上下对称：离顶边近就按顶边算，离底边近就按底边算。
    const int from_bottom = h - 1 - dy;
    const int depth = dy < from_bottom ? dy : from_bottom;
    return depth >= radius ? 0 : radius - 1 - depth;
}

}  // namespace

void fill_round_rect(int x, int y, int w, int h, int radius, Fl_Color color) {
    if (w <= 0 || h <= 0) return;
    radius = clamped_radius(w, h, radius);
    fl_color(color);
    // 一行一条横带，左右按圆角收进；相邻两行只差一像素，中间那几行收 0。
    for (int dy = 0; dy < h; ++dy) {
        const int inset = round_inset(dy, h, radius);
        fl_rectf(x + inset, y + dy, w - 2 * inset, 1);
    }
}

void stroke_round_rect(int x, int y, int w, int h, int radius, Fl_Color color) {
    if (w <= 0 || h <= 0) return;
    radius = clamped_radius(w, h, radius);
    fl_color(color);

    // 逐行画：斜着收进的那几行左边一个像素、右边一个像素，上下两行横着连起来。
    // 每一行的像素都落在 fill_round_rect 填出的那一行里，所以角上不会缺一格。
    for (int dy = 0; dy < h; ++dy) {
        const int inset = round_inset(dy, h, radius);
        const int left = x + inset;
        const int right = x + w - 1 - inset;
        if (right < left) continue;
        if (dy == 0 || dy == h - 1 || right == left)
            fl_rectf(left, y + dy, right - left + 1, 1);
        else {
            fl_rectf(left, y + dy, 1, 1);
            fl_rectf(right, y + dy, 1, 1);
        }
    }
}

namespace {

// 细折线箭头：up 为真画上箭头（尖朝上），否则画下箭头（尖朝下）。线宽一像素，
// 与边框一样细；两像素的箭头在下拉框与数字框上都显得笨。
void stroke_chevron(int cx, int cy, int half_width, int half_height, bool up,
                   Fl_Color color = kInk) {
    fl_color(color);
    fl_line_style(FL_SOLID, 1);
    if (up) {
        fl_line(cx - half_width, cy + half_height, cx, cy - half_height);
        fl_line(cx, cy - half_height, cx + half_width, cy + half_height);
    } else {
        fl_line(cx - half_width, cy - half_height, cx, cy + half_height);
        fl_line(cx, cy + half_height, cx + half_width, cy - half_height);
    }
    fl_line_style(FL_SOLID, 1);
}

// 扁平外观的框：按控件给的底色填一块圆角矩形，再沿边画一道一像素的边框。
// 界面库的凸起框与凹下框共用这一个函数，所以输入框、下拉框、列表框、报告区
// 都是同一种边框；边框色取主题里的 kBorder，控件禁用时框也还在。
// 圆角外面那四个小角用页面底色盖掉：这类框都摆在页面上，不先盖一层就会留着上一帧的像素。
// 内容让出两像素（见 reinstall_flat_boxtypes 给的占位），所以边框与里面的字之间有一道缝，
// 不会贴着字，也不会顶到圆角上。
void flat_box_draw(int x, int y, int w, int h, Fl_Color fill) {
    if (w <= 0 || h <= 0) return;
    fl_color(kPageBg);
    fl_rectf(x, y, w, h);
    fill_round_rect(x, y, w, h, kButtonRadius, fill);
    stroke_round_rect(x, y, w, h, kButtonRadius, kBorder);
}

bool g_keyboard_input = false;  // 最近一次操作是键盘

// 全局事件处理：只记一件事，这次操作是键盘还是鼠标。返回 0 表示不挡事件。
int watch_input_kind(int event) {
    bool keyboard = g_keyboard_input;
    switch (event) {
        case FL_KEYBOARD:
        case FL_SHORTCUT:
            keyboard = true;
            break;
        case FL_PUSH:
        case FL_DRAG:
        case FL_RELEASE:
        case FL_MOUSEWHEEL:
            keyboard = false;
            break;
        default:
            return 0;
    }
    if (keyboard == g_keyboard_input) return 0;
    g_keyboard_input = keyboard;
    // 界面库的虚线焦点框跟着这个开关走：鼠标点出来的一律不画，按了键才画。
    Fl::visible_focus(keyboard ? 1 : 0);
    // 切换的那一刻要把有焦点的那个重画一遍，焦点框才会出现或消失。
    if (Fl_Widget* focused = Fl::focus()) focused->redraw();
    Fl::redraw();
    return 0;
}

}  // namespace

Fl_Box* make_text(int x, int y, int w, int h, const std::string& text, int size, Fl_Color color) {
    auto* box = new Fl_Box(x, y, w, h);
    box->copy_label(text.c_str());
    box->align(FL_ALIGN_LEFT | FL_ALIGN_INSIDE);
    box->labelsize(size);
    box->labelcolor(color);
    box->box(FL_NO_BOX);
    return box;
}

Fl_Input* make_input(int x, int y, int w, int h, const std::string& value) {
    auto* input = new Fl_Input(x, y, w, h);
    if (!value.empty()) input->value(value.c_str());
    input->textsize(15);
    input->labelsize(15);
    return input;
}

void reinstall_flat_boxtypes() {
    // 四组一对一对的框：凸起对凹下、粗对细、盒对框。控件按用途取其中一个，
    // 全部指向同一种画法，页面上就不再有凸起与亮边。
    const Fl_Boxtype flatBoxes[] = {
        FL_UP_BOX,           FL_DOWN_BOX,           FL_UP_FRAME,          FL_DOWN_FRAME,
        FL_THIN_UP_BOX,      FL_THIN_DOWN_BOX,      FL_THIN_UP_FRAME,     FL_THIN_DOWN_FRAME,
        FL_BORDER_BOX,       FL_BORDER_FRAME,
    };
    // 后四个是内容区给边框让出的位置：横竖各让三像素。界面库原来那几种框让两像素，
    // 但圆角半径 6 的那几行斜边上，边框像素会落在「第 2 像素往里」的位置上：让两像素时
    // 报告区（Fl_Text_Display 会把它那块内容区整片铺成底色）正好把斜边上的两个边框像素盖掉，
    // 角上就像缺了一格。让三像素，斜边全部落在内容区外面，边框一个像素也不会被盖。
    for (const Fl_Boxtype flat : flatBoxes) Fl::set_boxtype(flat, flat_box_draw, 3, 3, 6, 6);
}

void install_flat_look() {
    reinstall_flat_boxtypes();

    // 键盘框只在键盘操作时画，鼠标点出来的那一圈是多余的；
    // 界面库自己的控件同样走这个开关，所以一开窗就按鼠标模式摆好。
    Fl::visible_focus(0);
    Fl::add_handler(watch_input_kind);
}

bool keyboard_input() { return g_keyboard_input; }

FlatButton::FlatButton(int x, int y, int w, int h, const char* label)
    : Fl_Button(x, y, w, h, label) {
    box(FL_NO_BOX);
    labelsize(15);
    labelcolor(kInk);
}

void FlatButton::draw() {
    const bool enabled = active_r() != 0;
    const bool pressed = enabled && value() != 0 && Fl::pushed() == this;
    const bool hovered = enabled && Fl::belowmouse() == this;

    // 白底圆角矩形，沿边一道细框；悬停深一档，按下再深一档，禁用时退回灰底灰字。
    // 三层底色都是平涂，不画亮边与暗边，按下去不会变成一块立体的方块。
    const Fl_Color fill = !enabled ? button_bg_disabled()
                                   : (pressed ? button_bg_pressed()
                                              : (hovered ? button_bg_hover() : button_bg()));
    fill_round_rect(x(), y(), w(), h(), kButtonRadius, fill);
    stroke_round_rect(x(), y(), w(), h(), kButtonRadius, kBorder);

    fl_font(labelfont(), labelsize());
    fl_color(enabled ? labelcolor() : kMuted);
    fl_draw(label(), x(), y(), w(), h(), FL_ALIGN_CENTER | FL_ALIGN_INSIDE | FL_ALIGN_CLIP);

    if (enabled && Fl::focus() == this && keyboard_input()) {
        // 焦点框跟着按钮的圆角走：按钮是圆的，框是方的就对不齐。
        stroke_round_rect(x() + 2, y() + 2, w() - 4, h() - 4, kButtonRadius, kAccent);
    }
}

int FlatButton::handle(int event) {
    const int result = Fl_Button::handle(event);
    // 鼠标进出要重画，否则悬停时换的那一层底色不会出来。
    if (event == FL_ENTER || event == FL_LEAVE) redraw();
    return result;
}

FlatButton* make_button(int x, int y, int w, int h, const std::string& label) {
    auto* button = new FlatButton(x, y, w, h);
    // 文案可能是拼出来的，只记指针的 label() 会变成乱码，所以走 copy_label。
    button->copy_label(label.c_str());
    return button;
}

FlatChoice::FlatChoice(int x, int y, int w, int h, const char* label) : Fl_Choice(x, y, w, h, label) {
    box(FL_NO_BOX);
    labelsize(15);
    textsize(15);
    textcolor(kInk);
}

void FlatChoice::draw() {
    const bool enabled = active_r() != 0;

    fill_round_rect(x(), y(), w(), h(), kButtonRadius, kFieldBg);
    stroke_round_rect(x(), y(), w(), h(), kButtonRadius, kBorder);

    // 右边一个细箭头，不画分隔线：分隔线是立体框的痕迹。
    stroke_chevron(x() + w() - 14, y() + h() / 2, 5, 3, false, enabled ? kInk : kMuted);

    if (mvalue() != nullptr) {
        fl_font(textfont(), textsize());
        fl_color(enabled ? textcolor() : kMuted);
        fl_draw(mvalue()->text, x() + 9, y() + 1, w() - 30, h() - 2,
                FL_ALIGN_LEFT | FL_ALIGN_INSIDE | FL_ALIGN_CLIP);
    }
    draw_label();
}

FlatChoice* make_choice(int x, int y, int w, int h, const std::vector<std::string>& items,
                        int selected) {
    auto* choice = new FlatChoice(x, y, w, h);
    for (const std::string& item : items) choice->add(item.c_str());
    if (selected >= 0 && selected < static_cast<int>(items.size())) choice->value(selected);
    return choice;
}

int choice_width(const std::string& label, int labelsize) {
    fl_font(FL_HELVETICA, labelsize);
    // 末尾两像素是余量：宽度恰好等于字宽时，最后一笔会被 CLIP 削掉一点。
    return kChoiceMark + kChoiceGap + static_cast<int>(fl_width(label.c_str())) + 2;
}

FlatCheck::FlatCheck(int x, int y, int w, int h, const char* label)
    : Fl_Check_Button(x, y, w, h, label) {
    box(FL_NO_BOX);
    labelsize(15);
}

void FlatCheck::draw() {
    const bool on = value() != 0;
    const bool enabled = active_r() != 0;
    const int top = y() + (h() - kChoiceMark) / 2;

    // 方块：没勾是白底细框，勾上了是实心强调色；边框不画在白底里面，
    // 两种状态的轮廓一样大，勾不勾不会让方块看起来大小不一。
    if (on) {
        fill_round_rect(x(), top, kChoiceMark, kChoiceMark, kChoiceRadius,
                        enabled ? kAccent : mix_color(kAccent, kPageBg, 0.55));
        fl_color(kAccentInk);
        fl_line_style(FL_SOLID, 2);
        fl_line(x() + 4, top + 9, x() + 7, top + 13);
        fl_line(x() + 7, top + 13, x() + 14, top + 5);
        fl_line_style(FL_SOLID, 1);
    } else {
        fill_round_rect(x(), top, kChoiceMark, kChoiceMark, kChoiceRadius, kFieldBg);
        stroke_round_rect(x(), top, kChoiceMark, kChoiceMark, kChoiceRadius, kBorder);
    }

    fl_font(labelfont(), labelsize());
    fl_color(enabled ? labelcolor() : kMuted);
    fl_draw(label() == nullptr ? "" : label(), x() + kChoiceMark + kChoiceGap, y(),
            w() - kChoiceMark - kChoiceGap, h(), FL_ALIGN_LEFT | FL_ALIGN_INSIDE | FL_ALIGN_CLIP);
}

FlatRadio::FlatRadio(int x, int y, int w, int h, const char* label)
    : Fl_Round_Button(x, y, w, h, label) {
    box(FL_NO_BOX);
    labelsize(15);
    type(FL_RADIO_BUTTON);
}

void FlatRadio::draw() {
    const bool on = value() != 0;
    const bool enabled = active_r() != 0;
    const int top = y() + (h() - kChoiceMark) / 2;
    const int cx = x() + kChoiceMark / 2;
    const int cy = top + kChoiceMark / 2;

    if (on) {
        // 选中的是一整块圆盘：远看就是一个深色圆点，不会与没选中的圆环认错。
        fl_color(enabled ? kAccent : mix_color(kAccent, kPageBg, 0.55));
        fl_pie(x(), top, kChoiceMark, kChoiceMark, 0, 360);
        fl_color(kAccentInk);
        fl_pie(cx - kChoiceHole / 2, cy - kChoiceHole / 2, kChoiceHole, kChoiceHole, 0, 360);
    } else {
        fl_color(kFieldBg);
        fl_pie(x(), top, kChoiceMark, kChoiceMark, 0, 360);
        fl_color(kBorder);
        fl_line_style(FL_SOLID, 1);
        fl_arc(x(), top, kChoiceMark, kChoiceMark, 0, 360);
    }

    fl_font(labelfont(), labelsize());
    fl_color(enabled ? labelcolor() : kMuted);
    fl_draw(label() == nullptr ? "" : label(), x() + kChoiceMark + kChoiceGap, y(),
            w() - kChoiceMark - kChoiceGap, h(), FL_ALIGN_LEFT | FL_ALIGN_INSIDE | FL_ALIGN_CLIP);
}

namespace {

// 滚动条的滑块：一条细灰棍，凹槽就是输入框那一层底色。
// 槽里不画箭头、斜纹与刻度，只留滑块本身。
class FlatScrollbar : public Fl_Scrollbar {
public:
    FlatScrollbar(int x, int y, int w, int h) : Fl_Scrollbar(x, y, w, h) { box(FL_NO_BOX); }

    void draw() override {
        fl_color(kFieldBg);
        fl_rectf(x(), y(), w(), h());

        const int length = h();
        int size = static_cast<int>(slider_size() * length + 0.5);
        if (size < 20) size = 20;  // 太短就不好抓
        if (size > length) size = length;
        double val = 0.0;
        if (maximum() > minimum())
            val = (value() - minimum()) / (maximum() - minimum());
        if (val < 0.0) val = 0.0;
        if (val > 1.0) val = 1.0;
        const int pos = static_cast<int>(val * (length - size) + 0.5);

        fl_color(kMuted);
        fl_rectf(x() + 3, y() + pos, w() - 6, size);
    }
};

constexpr int kScrollBarWidth = 9;
constexpr int kWheelStep = 26;  // 滚一格是一次一行

}  // namespace

ScrollList::ScrollList(int x, int y, int w, int h) : Fl_Group(x, y, w, h), content_top_(y + 1) {
    box(FL_NO_BOX);
    content_ = new Fl_Group(x + 1, y + 1, w - 2 - kScrollBarWidth, h - 2);
    content_->box(FL_NO_BOX);
    content_->end();

    bar_ = new FlatScrollbar(x + w - 1 - kScrollBarWidth, y + 1, kScrollBarWidth, h - 2);
    bar_->type(FL_VERTICAL);
    bar_->step(kWheelStep);
    bar_->linesize(kWheelStep);
    bar_->when(FL_WHEN_CHANGED);
    bar_->callback(on_bar, this);
    Fl_Group::end();
}

int ScrollList::visible_height() const { return h() - 2; }

void ScrollList::end() {
    content_->end();
    refresh_layout();
}

void ScrollList::refresh_layout() {
    // 子控件顶边对齐 content_ 的时候滚动量是 0，所以量总高时要把当前滚动量加回去。
    int bottom = content_top_;
    for (int i = 0; i < content_->children(); ++i) {
        Fl_Widget* child = content_->child(i);
        if (child == nullptr || child->visible() == 0) continue;
        bottom = std::max(bottom, child->y() + child->h());
    }
    content_height_ = bottom - content_top_;

    const int span = std::max(0, content_height_ - visible_height());
    if (span == 0) {
        // 内容装得下就不摆滑块，整个宽度都给子控件。
        bar_->hide();
        content_->resize(content_->x(), content_->y(), w() - 2, visible_height());
    } else {
        bar_->show();
        content_->resize(content_->x(), content_->y(), w() - 2 - kScrollBarWidth,
                         visible_height());
    }

    bar_->range(0, span);
    bar_->linesize(kWheelStep);
    if (content_height_ > 0) {
        double fraction = static_cast<double>(visible_height()) / static_cast<double>(content_height_);
        bar_->slider_size(fraction > 1.0 ? 1.0 : fraction);
    }
    scroll_to(offset_);
    redraw();
}

void ScrollList::scroll_to(int offset) {
    const int span = std::max(0, content_height_ - visible_height());
    if (offset < 0) offset = 0;
    if (offset > span) offset = span;
    offset_ = offset;

    content_->position(content_->x(), content_top_ - offset_);
    if (bar_->value() != offset_) bar_->value(offset_);
    if (bar_->visible()) bar_->redraw();
    redraw();
}

void ScrollList::on_bar(Fl_Widget*, void* data) {
    auto* list = static_cast<ScrollList*>(data);
    list->scroll_to(static_cast<int>(list->bar_->value() + 0.5));
}

void ScrollList::draw() {
    // 圆角外面那四个小角是页面底色：与输入框、列表框、报告区是同一副样子。
    fl_color(kPageBg);
    fl_rectf(x(), y(), w(), h());
    fill_round_rect(x(), y(), w(), h(), kButtonRadius, kFieldBg);

    // 子控件裁在框里，滚出去的那部分不露到框外。滑块摆在裁剪区右边，单独画。
    fl_push_clip(x() + 1, y() + 1, w() - 2 - kScrollBarWidth, h() - 2);
    Fl_Group::draw();
    fl_pop_clip();
    if (bar_->visible()) Fl_Group::draw_child(*bar_);

    stroke_round_rect(x(), y(), w(), h(), kButtonRadius, kBorder);
}

int ScrollList::handle(int event) {
    if (event == FL_MOUSEWHEEL && Fl::event_inside(this)) {
        scroll_to(offset_ - Fl::event_dy() * kWheelStep);
        return 1;
    }
    if ((event == FL_PUSH || event == FL_DRAG || event == FL_RELEASE) &&
        Fl::event_inside(bar_)) {
        return bar_->handle(event);
    }
    return Fl_Group::handle(event);
}

// ---------------------------------------------------------------------------
// 浏览对话框的上次目录与上次选的底色
//
// 这两个选择都记在界面配置文件里，一行一项。每次点「浏览」都从程序所在目录开始，
// 等于让用户在同一个地方重新找一遍稿子，所以把上次用过的目录记下来。
// 配置文件读不到、写不进去都不影响使用：退回程序当前目录、退回浅色底色。
// ---------------------------------------------------------------------------

namespace {

std::filesystem::path ui_config_path() {
    const auto from_env = [](const char* name) -> std::string {
        const char* value = std::getenv(name);
        return value == nullptr ? std::string() : std::string(value);
    };
#ifdef _WIN32
    const std::string base = from_env("APPDATA");
#else
    std::string base = from_env("XDG_CONFIG_HOME");
    if (base.empty()) {
        const std::string home = from_env("HOME");
        if (!home.empty()) base = home + "/.config";
    }
#endif
    if (base.empty()) return {};
    return std::filesystem::path(base) / "texttool" / "ui.conf";
}

}  // namespace

std::string last_browse_directory() {
    const std::string value = ui_config_get("last_dir");
    std::error_code ignored;
    if (!value.empty() && std::filesystem::is_directory(value, ignored)) return value;
    return {};
}

void remember_browse_directory(const std::string& directory) {
    if (directory.empty()) return;
    ui_config_set("last_dir", directory);
}

namespace {

// ---------------------------------------------------------------------------
// 选文件、选文件夹的窗
//
// 一扇窗分四段：顶上是现在在哪个文件夹，中间是这一层的东西（文件夹在前、文件在后），
// 下面一行写文件名，最下面两个按钮。点两下文件夹就进去，点两下文件就选中它。
// 要文件夹的时候，进到那一层再按「选这个文件夹」，返回的是现在这一层。
// ---------------------------------------------------------------------------

constexpr int kPickerWidth = 560;
constexpr int kPickerHeight = 440;
constexpr int kRowHeight = 24;

// 列表里的一行。文件夹行的写法与文件行不同，一眼分得开。
struct PickerEntry {
    std::string label;
    std::string path;
    bool directory = false;
};

struct PickerState {
    Fl_Window* window = nullptr;
    Fl_Hold_Browser* list = nullptr;
    Fl_Input* name = nullptr;
    Fl_Box* name_label = nullptr;
    Fl_Box* where = nullptr;
    Fl_Box* hint = nullptr;
    std::vector<PickerEntry>* entries = nullptr;
    PickerRequest request;
    std::string directory;
    std::string chosen;  // 选好的路径；空表示还没选
};

std::string lower_case(std::string text) {
    for (char& c : text) c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));
    return text;
}

// 名字后缀（不带点）在不在要列的那几种里。空表就是全列。
bool has_wanted_extension(const std::string& file_name, const std::vector<std::string>& wanted) {
    if (wanted.empty()) return true;
    const std::size_t dot = file_name.rfind('.');
    if (dot == std::string::npos || dot + 1 >= file_name.size()) return false;
    const std::string extension = lower_case(file_name.substr(dot + 1));
    for (const std::string& one : wanted)
        if (lower_case(one) == extension) return true;
    return false;
}

void picker_hint(PickerState& state, const std::string& text) {
    if (state.hint == nullptr) return;
    state.hint->copy_label(text.c_str());
    state.hint->redraw();
}

// 按现在这个文件夹重填列表。
void picker_fill(PickerState& state) {
    auto& entries = *state.entries;
    entries.clear();

    if (state.where != nullptr) {
        state.where->copy_label(("现在的位置：" + state.directory).c_str());
        state.where->redraw();
    }

    std::vector<PickerEntry> directories;
    std::vector<PickerEntry> files;

    const std::filesystem::path current(state.directory);
    const std::filesystem::path parent = current.parent_path();
    if (!parent.empty() && parent != current)
        directories.push_back({"上一级", parent.string(), true});

    std::error_code error;
    std::filesystem::directory_iterator it(current, error);
    if (error) {
        picker_hint(state, "这个文件夹读不了，换一层看看");
    } else {
        for (; !error && it != std::filesystem::directory_iterator(); it.increment(error)) {
            const std::string name = it->path().filename().string();
            if (name.empty() || name[0] == '.') continue;  // 隐藏的东西不列，免得满屏都是

            std::error_code type_error;
            const bool is_directory = it->is_directory(type_error);
            if (is_directory) {
                // 选文件夹时列表里只摆文件夹：文件点了也选不走，摆着只会碍事。
                const std::string label = state.request.directory_only ? name : name + "（文件夹）";
                directories.push_back({label, it->path().string(), true});
            } else if (!state.request.directory_only && it->is_regular_file(type_error) &&
                       has_wanted_extension(name, state.request.extensions)) {
                files.push_back({name, it->path().string(), false});
            }
        }
    }

    const auto by_label = [](const PickerEntry& left, const PickerEntry& right) {
        return left.label < right.label;
    };
    // 上一级固定排第一，其余文件夹与文件各按名字排：找东西的人按名字找。
    const std::size_t fixed = directories.empty() || directories.front().label != "上一级" ? 0 : 1;
    std::sort(directories.begin() + static_cast<std::ptrdiff_t>(fixed), directories.end(), by_label);
    std::sort(files.begin(), files.end(), by_label);

    entries = std::move(directories);
    entries.insert(entries.end(), files.begin(), files.end());

    state.list->clear();
    for (const PickerEntry& entry : entries) state.list->add(entry.label.c_str());
    state.list->scrollbar_width(0);  // 行不多，自带的那根滑块用不上
    state.list->redraw();
}

void picker_open(PickerState& state, const std::string& directory) {
    std::error_code error;
    if (!std::filesystem::is_directory(directory, error)) {
        picker_hint(state, "这个文件夹不在了，换一层看看");
        return;
    }
    state.directory = directory;
    if (state.name != nullptr) state.name->value("");
    picker_hint(state, "点两下文件夹就进去，点两下文件就选中它");
    picker_fill(state);
}

// 点一下列表里的一行：文件夹只是选上，文件填到文件名那一格；点两下就是进去或选走。
void picker_pick(PickerState& state, int row, bool double_click) {
    const auto& entries = *state.entries;
    if (row < 1 || static_cast<std::size_t>(row) > entries.size()) return;
    const PickerEntry& entry = entries[static_cast<std::size_t>(row) - 1];

    if (entry.directory) {
        if (double_click) {
            picker_open(state, entry.path);
            return;
        }
        picker_hint(state, "进「" + entry.label + "」请点两下");
        return;
    }

    if (state.name != nullptr) state.name->value(entry.label.c_str());
    if (double_click) state.chosen = entry.path;
}

// 按「选这一份」：文件夹模式返回现在这一层，文件模式看文件名那一格。
void picker_accept(PickerState& state) {
    if (state.request.directory_only) {
        state.chosen = state.directory;
        return;
    }

    std::string typed = state.name == nullptr ? std::string() : state.name->value();
    while (!typed.empty() && (typed.back() == ' ' || typed.back() == '\t')) typed.pop_back();
    if (typed.empty()) {
        picker_hint(state, "先填上文件名，或者点两下上面那一份");
        return;
    }

    const std::filesystem::path candidate(typed);
    const std::string path =
        candidate.is_absolute() ? typed : texttool::combine_path(state.directory, typed);
    std::error_code error;

    if (state.request.suggested_name.empty()) {
        // 选一份现成的：名字对不上就说一声，不把错的路径交出去。
        if (!std::filesystem::is_regular_file(path, error)) {
            picker_hint(state, "这个文件夹里没有这一份：" + typed);
            return;
        }
    } else if (std::filesystem::exists(path, error)) {
        // 要写的位置已经有东西：写下去就把原来那份盖掉了，问一句。
        if (fl_choice("这个位置已经有一份一模一样的名字了，要换掉它吗？", "取消", "换掉", nullptr) != 1) {
            picker_hint(state, "换一个名字，或者再按一次「选这一份」");
            return;
        }
    }

    state.chosen = path;
}

void on_picker_row(Fl_Widget*, void* data) {
    auto* state = static_cast<PickerState*>(data);
    picker_pick(*state, state->list->value(), Fl::event_clicks() != 0);
}

void on_picker_accept(Fl_Widget*, void* data) {
    auto* state = static_cast<PickerState*>(data);
    picker_accept(*state);
    if (!state->chosen.empty() && state->window != nullptr) state->window->hide();
}

void on_picker_cancel(Fl_Widget*, void* data) {
    auto* state = static_cast<PickerState*>(data);
    if (state != nullptr && state->window != nullptr) state->window->hide();
}

// 文件名那一格敲回车就算选中。
void on_picker_name(Fl_Widget*, void* data) {
    auto* state = static_cast<PickerState*>(data);
    if (Fl::event_key() != FL_Enter && Fl::event_key() != FL_KP_Enter) return;
    picker_accept(*state);
    if (!state->chosen.empty() && state->window != nullptr) state->window->hide();
}

}  // namespace

std::string pick_path(const PickerRequest& request) {
    // 从哪儿开始找：调用方给的目录，其次是上一回用过的，最后是现在这个目录。
    std::string start = request.start_dir;
    if (start.empty()) start = last_browse_directory();
    std::error_code error;
    if (start.empty() || !std::filesystem::is_directory(start, error)) start = ".";

    Fl_Window dialog(0, 0, kPickerWidth, kPickerHeight, request.title.c_str());
    dialog.set_modal();
    dialog.color(kPageBg);
    dialog.labelcolor(kInk);
    dialog.begin();

    std::vector<PickerEntry> entries;
    PickerState state;
    state.window = &dialog;
    state.request = request;
    state.entries = &entries;
    state.directory = std::filesystem::absolute(start, error).string();

    state.where = make_text(14, 8, kPickerWidth - 28, 22, "", 13, kMuted);

    // 列表占掉上下两段之外的全部高度：选文件夹时没有文件名那一行，列表就更高。
    const int name_row_height = request.directory_only ? 0 : 42;
    const int list_height = kPickerHeight - 34 - 62 - name_row_height;
    state.list = new Fl_Hold_Browser(14, 34, kPickerWidth - 28, list_height);
    state.list->textsize(15);
    state.list->color(kFieldBg);
    state.list->textcolor(kInk);
    state.list->selection_color(kAccent);
    state.list->when(FL_WHEN_RELEASE);
    state.list->callback(on_picker_row, &state);

    const int name_y = 34 + list_height + 10;
    state.name_label = make_text(14, name_y + 7, 62, 22, "文件名：");
    state.name = make_input(80, name_y, kPickerWidth - 28 - 66, 30);
    state.name->when(FL_WHEN_ENTER_KEY | FL_WHEN_RELEASE);
    state.name->callback(on_picker_name, &state);
    if (!request.suggested_name.empty()) state.name->value(request.suggested_name.c_str());

    if (request.directory_only) {
        state.name_label->hide();
        state.name->hide();
    }

    const int button_y = kPickerHeight - 48;
    state.hint = make_text(14, button_y, kPickerWidth - 28 - 200, 32, "", 13, kMuted);
    state.hint->align(FL_ALIGN_LEFT | FL_ALIGN_INSIDE | FL_ALIGN_CLIP);

    auto* cancel = make_button(kPickerWidth - 14 - 76, button_y, 76, 32, "取消");
    cancel->labelsize(14);
    cancel->callback(on_picker_cancel, &state);
    auto* accept = make_button(kPickerWidth - 14 - 176, button_y, 92, 32,
                              request.directory_only ? "选这个文件夹" : "选这一份");
    accept->labelsize(14);
    accept->callback(on_picker_accept, &state);

    dialog.callback(on_picker_cancel, &state);  // 点关闭框也算取消
    dialog.end();

    picker_hint(state, request.directory_only ? "进到要处理的那一层，再按「选这个文件夹」"
                                              : "点两下文件夹就进去，点两下文件就选中它");
    state.directory = std::filesystem::is_directory(state.directory, error) ? state.directory : ".";
    picker_fill(state);

    if (Fl_Window* owner = Fl::first_window()) {
        const int px = owner->x() + (owner->w() - kPickerWidth) / 2;
        const int py = owner->y() + (owner->h() - kPickerHeight) / 2;
        dialog.position(px < 0 ? 0 : px, py < 0 ? 0 : py);
    }

    dialog.show();
    apply_title_bar(&dialog);
    // 模态窗自己转事件循环：这一圈里主窗口收不到输入，退出后才回到调用它的人。
    while (dialog.shown() != 0) Fl::wait();

    if (state.chosen.empty()) return {};
    const std::filesystem::path chosen(state.chosen);
    remember_browse_directory(request.directory_only ? state.chosen
                                                     : chosen.parent_path().string());
    return state.chosen;
}

std::string ui_config_get(const std::string& key) {
    const std::filesystem::path path = ui_config_path();    if (path.empty()) return {};
    std::ifstream in(path);
    if (!in) return {};

    std::string line;
    const std::string prefix = key + "=";
    while (std::getline(in, line)) {
        if (line.rfind(prefix, 0) != 0) continue;
        return line.substr(prefix.size());
    }
    return {};
}

void ui_config_set(const std::string& key, const std::string& value) {
    const std::filesystem::path path = ui_config_path();
    if (path.empty() || key.empty()) return;

    // 先把现有的几项读回来：这个文件里不止一项，整份重写会把别的项弄丢。
    std::vector<std::pair<std::string, std::string>> items;
    std::ifstream in(path);
    std::string line;
    while (std::getline(in, line)) {
        const std::size_t equals = line.find('=');
        if (equals == std::string::npos || equals == 0) continue;
        items.emplace_back(line.substr(0, equals), line.substr(equals + 1));
    }

    bool replaced = false;
    for (auto& item : items) {
        if (item.first != key) continue;
        item.second = value;
        replaced = true;
    }
    if (!replaced) items.emplace_back(key, value);

    std::error_code ignored;
    std::filesystem::create_directories(path.parent_path(), ignored);
    std::ofstream out(path, std::ios::trunc);
    if (!out) return;
    for (const auto& item : items) out << item.first << "=" << item.second << "\n";
}

Fl_Image* bundled_image(const std::string& program_path, const std::string& file_name) {
    const std::filesystem::path dir = texttool::program_dir(program_path.c_str());
    const std::filesystem::path candidates[] = {
        dir / file_name,
        dir / "images" / file_name,
        dir / "assets" / "images" / file_name,
    };
    for (const std::filesystem::path& candidate : candidates) {
        std::error_code ignored;
        if (!std::filesystem::is_regular_file(candidate, ignored)) continue;
        // 图读不进来（文件坏了）时界面库会给一张空图，界面上就是没画，不崩。
        return new Fl_PNG_Image(candidate.string().c_str());
    }
    return nullptr;
}

std::string encoding_label(TextEncoding encoding) {
    switch (encoding) {
        case TextEncoding::Utf8: return "UTF-8";
        case TextEncoding::Utf8Bom: return "UTF-8（带 BOM）";
        case TextEncoding::Utf16Le: return "UTF-16 LE";
        case TextEncoding::Utf16Be: return "UTF-16 BE";
        case TextEncoding::Gbk: return "GBK";
    }
    return "未知";
}

// ---------------------------------------------------------------------------
// 自己管排布的控件组
// ---------------------------------------------------------------------------

namespace {

// 两个浏览按钮的宽度与间隙。宽度按文案量出来：六个字加省略号正好放下，不再留白。
// 只留一个按钮时宽度取「选文件…」那个数，与原来那一行长得一样。
constexpr int kBrowseButtonWidth = 88;
constexpr int kFolderButtonWidth = 96;
constexpr int kButtonGap = 8;

}  // namespace

SourceRow::SourceRow(int x, int y, int w, const std::string& label, bool with_file_button,
                     bool with_folder_button)
    : Fl_Group(x, y, w, kFormRowHeight) {
    box(FL_NO_BOX);
    color(kPageBg);
    begin();
    label_ = make_text(x, y, kLabelWidth, kFormRowHeight, label);
    input_ = make_input(x + kLabelWidth, y, std::max(0, w - kLabelWidth), kFormRowHeight);
    if (with_file_button) {
        file_button_ = make_button(x, y, kBrowseButtonWidth, kFormRowHeight, "选文件…");
        file_button_->labelsize(15);
        file_button_->callback(on_pick, this);
    }
    if (with_folder_button) {
        folder_button_ = make_button(x, y, kBrowseButtonWidth, kFormRowHeight, "选文件夹…");
        folder_button_->labelsize(15);
        folder_button_->callback(on_pick, this);
    }
    end();
    layout_children();
}

void SourceRow::resize(int x, int y, int w, int h) {
    Fl_Widget::resize(x, y, w, h);
    layout_children();
}

void SourceRow::layout_children() {
    const int right = x() + w();
    const int top = y();

    // 按钮贴着右边界往里排，输入框到最左边那个按钮为止。
    int field_right = right;
    if (file_button_ != nullptr && folder_button_ != nullptr) {
        folder_button_->resize(right - kFolderButtonWidth, top, kFolderButtonWidth, kFormRowHeight);
        const int file_x = right - kFolderButtonWidth - kButtonGap - kBrowseButtonWidth;
        file_button_->resize(file_x, top, kBrowseButtonWidth, kFormRowHeight);
        field_right = file_x;
    } else if (folder_button_ != nullptr) {
        folder_button_->resize(right - kBrowseButtonWidth, top, kBrowseButtonWidth, kFormRowHeight);
        field_right = right - kBrowseButtonWidth;
    } else if (file_button_ != nullptr) {
        file_button_->resize(right - kBrowseButtonWidth, top, kBrowseButtonWidth, kFormRowHeight);
        field_right = right - kBrowseButtonWidth;
    }

    label_->resize(x(), top, kLabelWidth, kFormRowHeight);
    const int field_x = x() + kLabelWidth;
    input_->resize(field_x, top, std::max(0, field_right - field_x), kFormRowHeight);
}

// 选路径与状态条那一句，整个界面只在这里写一次：五页的源文件行都是这一行控件。
void SourceRow::pick(bool directory) {
    if (input_ == nullptr) return;

    // 从哪儿开始找：输入框里已经有路径就以它所在目录为先，没有就用上次用过的目录。
    std::string start;
    const std::string typed =
        input_->value() == nullptr ? std::string() : std::string(input_->value());
    if (!typed.empty()) {
        std::error_code ignored;
        const std::filesystem::path candidate(typed);
        start = std::filesystem::is_directory(candidate, ignored)
                    ? candidate.string()
                    : candidate.parent_path().string();
        if (!start.empty() && !std::filesystem::is_directory(start, ignored)) start.clear();
    }

    PickerRequest request;
    request.title = directory ? "选择文件夹" : "选择文件";
    request.directory_only = directory;
    request.start_dir = start;
    const std::string picked = pick_path(request);
    if (picked.empty()) return;

    input_->value(picked.c_str());
    set_status("已选好：" + picked);
}

void SourceRow::on_pick(Fl_Widget* sender, void* data) {
    auto* self = static_cast<SourceRow*>(data);
    // 按的是哪一支按钮就选哪一种：文件夹按钮选目录，文件按钮选文件。
    self->pick(sender == self->folder_button_);
}

FieldRow::FieldRow(int x, int y, int w, const std::string& label, Fl_Widget* field)
    : Fl_Group(x, y, w, kFormRowHeight), field_(field) {
    box(FL_NO_BOX);
    color(kPageBg);
    begin();
    label_ = make_text(x, y, kLabelWidth, kFormRowHeight, label);
    // 控件由调用方建好，可能已经挂在页面上：add 会把它收到这一行里来。
    if (field_ != nullptr) add(field_);
    end();
    layout_children();
}

void FieldRow::resize(int x, int y, int w, int h) {
    Fl_Widget::resize(x, y, w, h);
    layout_children();
}

void FieldRow::layout_children() {
    if (label_ != nullptr) label_->resize(x(), y(), kLabelWidth, kFormRowHeight);
    if (field_ == nullptr) return;
    const int field_x = x() + kLabelWidth;
    field_->resize(field_x, y(), std::max(0, x() + w() - field_x), kFormRowHeight);
}

// 状态条自己画：一整条灰带顶到窗口两边，上面不画分隔线，字从左边 kStatusTextLeft 起。
// 界面库的 Fl_Box 画字时按边框的占位量往里让，而占位是 0，字就贴在窗口边上，
// 所以这里自己算字的起点。
namespace {

class StatusBar : public Fl_Box {
public:
    StatusBar(int x, int y, int w, int h) : Fl_Box(x, y, w, h) { box(FL_NO_BOX); }

    void draw() override {
        fl_color(kStatusBg);
        fl_rectf(x(), y(), w(), h());

        const char* text = label() == nullptr ? "" : label();
        fl_font(labelfont(), labelsize());
        fl_color(labelcolor());
        fl_draw(text, x() + kStatusTextLeft, y(), w() - kStatusTextLeft, h(),
                FL_ALIGN_LEFT | FL_ALIGN_INSIDE | FL_ALIGN_CLIP);
    }
};

}  // namespace

Fl_Box* make_status_bar(int x, int y, int w, int h) {
    auto* bar = new StatusBar(x, y, w, h);
    bar->labelsize(13);
    bar->labelcolor(kStatusInk);
    bar->align(FL_ALIGN_LEFT | FL_ALIGN_INSIDE);
    return bar;
}

void attach_status_bar(Fl_Box* bar) {
    g_status_bar = bar;
    // 页面在建状态条之前就建好了，各页露出来时写的那一句得先搬上去。
    if (g_status_bar != nullptr) g_status_bar->copy_label(g_status_text.c_str());
}

void set_status(const std::string& text) {
    g_status_text = text;
    if (g_status_bar != nullptr) g_status_bar->copy_label(text.c_str());
    if (g_status_bar != nullptr) g_status_bar->redraw();
}

std::string status_text() { return g_status_text; }

void append_status(const std::string& note) {
    if (g_status_bar == nullptr) return;
    set_status(g_status_text.empty() ? note : g_status_text + "；" + note);
}

// ---------------------------------------------------------------------------
// 数字框
// ---------------------------------------------------------------------------

NumberBox::NumberBox(int x, int y, int w, int h, int minimum, int maximum, int value,
                     int input_width)
    : Fl_Group(x, y, w, h), minimum_(minimum), maximum_(maximum), input_width_(input_width) {
    button_width_ = (w - input_width_) / 2;
    box(FL_NO_BOX);
    begin();
    // 输入框嵌在外框里面，四周留出边距，底色与页面底色分开一格，看得出这里是能改的。
    input_ = new Fl_Input(x + 10, y + 2, input_width_ - 14, h - 5);
    input_->textsize(15);
    input_->box(FL_FLAT_BOX);
    input_->color(kFieldBg);
    input_->when(FL_WHEN_ENTER_KEY | FL_WHEN_CHANGED | FL_WHEN_RELEASE);
    end();
    NumberBox::value(value);
}

int NumberBox::value() const {
    if (input_ == nullptr) return minimum_;
    // 读的时候也夹到范围内：输入框被清空时 atoi 给 0，阈值 0 会让每一行都立刻断开，
    // 看起来像「处理了但什么也没接」。显示的字面值可能还是空的，下一次编辑就会写回去。
    return std::clamp(std::atoi(input_->value()), minimum_, maximum_);
}

void NumberBox::range(int minimum, int maximum) {
    minimum_ = minimum;
    maximum_ = maximum;
    commit();
}

void NumberBox::value(int next) {
    const int clamped = std::clamp(next, minimum_, maximum_);
    if (input_ != nullptr) input_->value(std::to_string(clamped).c_str());
}

void NumberBox::commit() {
    if (input_ == nullptr) return;
    value(std::atoi(input_->value()));
}

void NumberBox::step(int delta) {
    commit();
    value(value() + delta);
    if (input_ != nullptr) input_->redraw();
    redraw();
}

void NumberBox::draw() {
    // 白底圆角外框，与按钮、输入框同一套画法。
    fill_round_rect(x(), y(), w(), h(), kButtonRadius, kFieldBg);
    stroke_round_rect(x(), y(), w(), h(), kButtonRadius, kBorder);

    // 两条竖分隔线：数字区右边一条，两个按钮中间一条。
    fl_color(kBorder);
    fl_line(x() + input_width_, y() + 1, x() + input_width_, y() + h() - 2);
    fl_line(x() + input_width_ + button_width_, y() + 1, x() + input_width_ + button_width_,
            y() + h() - 2);

    // 两个按钮：左加一、右减一，箭头是细折线。
    const int up_cx = x() + input_width_ + button_width_ / 2;
    const int down_cx = x() + input_width_ + button_width_ + button_width_ / 2;
    const int cy = y() + h() / 2;
    stroke_chevron(up_cx, cy, 5, 3, true);
    stroke_chevron(down_cx, cy, 5, 3, false);

    draw_children();
}

int NumberBox::handle(int event) {
    // 输入框里的字被删空、或者写了个越界的数，离开时就地改成合法值并写回去。
    // 只在读的时候夹住不够：框里还留着空的或 0，换页签回来看见的仍是那个样子。
    if (event == FL_UNFOCUS || event == FL_LEAVE || event == FL_ENTER) {
        if (event != FL_ENTER) {
            commit();
            if (input_ != nullptr) input_->redraw();
        }
        return Fl_Group::handle(event);
    }
    if (event == FL_KEYBOARD && Fl::event_key() == FL_Enter) {
        commit();
        if (input_ != nullptr) input_->redraw();
        redraw();
        return 1;
    }
    if (event == FL_PUSH) {
        const int mx = Fl::event_x();
        const int my = Fl::event_y();
        if (my >= y() && my < y() + h()) {
            if (mx >= x() + input_width_ && mx < x() + input_width_ + button_width_) {
                step(+1);
                return 1;
            }
            if (mx >= x() + input_width_ + button_width_ && mx < x() + w()) {
                step(-1);
                return 1;
            }
        }
    }
    if (event == FL_RELEASE) {
        if (Fl::event_inside(this)) commit();
        redraw();
        return 1;
    }
    return Fl_Group::handle(event);
}

// ---------------------------------------------------------------------------
// 拖放
// ---------------------------------------------------------------------------

std::vector<std::string> expand_dropped_paths(const std::string& dropped,
                                             std::vector<std::string>* unreadable) {
    std::vector<std::string> files;
    std::error_code ignored;

    std::size_t start = 0;
    while (start <= dropped.size()) {
        std::size_t end = dropped.find('\n', start);
        if (end == std::string::npos) end = dropped.size();
        std::string path = dropped.substr(start, end - start);
        start = end + 1;
        while (!path.empty() && (path.back() == '\r' || path.back() == ' ')) path.pop_back();
        if (path.empty()) continue;

        if (fs::is_directory(path, ignored)) {
            // 目录：递归收 .txt 与 .md，按路径排序，收法与命令行走目录时是同一份
            // （engine/io.h 的 scan_documents）。读不了的子目录不让它把拖放带崩，
            // 收得到的照收，读不了的路径交给调用方写在状态条上。
            const texttool::DirectoryScan scan = texttool::scan_documents(path, {".txt", ".md"});
            files.insert(files.end(), scan.files.begin(), scan.files.end());
            if (unreadable != nullptr)
                unreadable->insert(unreadable->end(), scan.unreadable.begin(),
                                   scan.unreadable.end());
            continue;
        }
        if (fs::is_regular_file(path, ignored)) files.push_back(path);
    }
    return files;
}

Page::Page(int x, int y, int w, int h) : Fl_Group(x, y, w, h) {
    box(FL_NO_BOX);
    color(kPageBg);
}

// 用自己的 x/y/w/h 直接改：界面库的 Fl_Group::resize 会按新旧尺寸的比例把子控件搬一遍，
// 而页面的排布是 layout 一层自己算的，让它搬一遍再算一遍是两笔账。
void Page::resize(int x, int y, int w, int h) {
    Fl_Widget::resize(x, y, w, h);
    reflow();
}

void Page::reflow() {
    layout();
    redraw();
}

int Page::handle(int event) {
    switch (event) {
        case FL_DND_ENTER:
        case FL_DND_DRAG:
        case FL_DND_LEAVE:
            return 1;
        case FL_DND_RELEASE: {
            const char* dropped = Fl::event_text();
            if (dropped == nullptr) return 0;
            std::vector<std::string> unreadable;
            on_files(expand_dropped_paths(dropped, &unreadable));
            // 页面在收下稿件时自己写过一句状态；这里把读不了的目录补在后面，两句都留着。
            for (const std::string& folder : unreadable)
                append_status("这个文件夹读不了，里面的稿子没有收进来：" + folder);
            return 1;
        }
        default:
            return Fl_Group::handle(event);
    }
}

// ---------------------------------------------------------------------------
// 逐份处理
// ---------------------------------------------------------------------------

BatchOutcome run_files(const std::vector<std::string>& files,
                       const std::function<std::string(const std::string&)>& task,
                       CancelState& cancel, const std::string& verb) {
    BatchOutcome outcome;
    cancel.requested = false;

    for (std::size_t i = 0; i < files.size(); ++i) {
        if (cancel.requested) {
            outcome.cancelled = true;
            break;
        }
        const std::string progress = "正在" + verb + "第 " + std::to_string(i + 1) + " 份（共 " +
                                     std::to_string(files.size()) + " 份）：" + files[i];
        set_status(progress);
        Fl::check();  // 让状态条与取消按钮先动起来

        try {
            outcome.outputs.push_back(task(files[i]));
        } catch (const std::exception& error) {
            outcome.failures.push_back(files[i] + "：" + error.what());
        }
        Fl::check();
    }

    if (outcome.cancelled) {
        set_status("已取消，做完 " + std::to_string(outcome.outputs.size()) + " 份");
    } else if (outcome.failures.empty()) {
        set_status("完成 " + std::to_string(outcome.outputs.size()) + " 份");
    } else {
        set_status("完成 " + std::to_string(outcome.outputs.size()) + " 份，失败 " +
                   std::to_string(outcome.failures.size()) + " 份");
    }
    return outcome;
}

}  // namespace texttool::ui
