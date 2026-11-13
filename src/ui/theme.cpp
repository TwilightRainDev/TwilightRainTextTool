// 两套底色的值，以及换底色时要做的那几件事。
//
// 颜色槽位是界面库留给程序自己用的那十六个色号。控件只记槽位，改槽位背后的值
// 就等于给整套界面换了颜色；换完把窗口标成待重画，下一帧就是新的样子。
#include "ui/theme.h"

#include <FL/Fl.H>
#include <FL/Fl_Window.H>

#include <string>

#ifdef _WIN32
#include <FL/platform.H>  // fl_xid：拿窗口的 Windows 句柄
#include <windows.h>
#include <dwmapi.h>
#endif

#include "ui/widgets.h"

namespace texttool::ui {
namespace {

// 一个颜色的三项分量。
struct Rgb {
    unsigned char r;
    unsigned char g;
    unsigned char b;
};

// 界面上用到的全部颜色，一个色号一项。
struct Palette {
    Rgb ink;
    Rgb muted;
    Rgb border;
    Rgb page_bg;
    Rgb field_bg;
    Rgb status_bg;
    Rgb status_ink;
    Rgb separator;
    Rgb accent;
    Rgb accent_hover;
    Rgb accent_pressed;
    Rgb accent_ink;
    Rgb tab_active;
    Rgb tab_idle;
    Rgb tab_line;
    Rgb warn_ink;
};

// 浅色：白底、近黑的字、浅灰的线，与桌面版一直以来的样子一致。
constexpr Palette kLight = {
    {0x1a, 0x1a, 0x1a},  // 正文文字
    {0x8a, 0x8a, 0x8a},  // 次要文字
    {0x76, 0x76, 0x76},  // 边框（桌面版实测的 118,118,118）
    {0xff, 0xff, 0xff},  // 页面底色
    {0xff, 0xff, 0xff},  // 输入框底色
    {0xf3, 0xf3, 0xf3},  // 状态条底色
    {0x44, 0x44, 0x44},  // 状态条字色
    {0xd0, 0xd0, 0xd4},  // 最浅的那一档灰线
    {0x2f, 0x6f, 0xeb},  // 主按钮
    {0x3d, 0x7b, 0xf0},  // 主按钮，鼠标停在上面
    {0x00, 0x00, 0xff},  // 主按钮，按下
    {0xff, 0xff, 0xff},  // 主按钮上的字
    {0x1a, 0x1a, 0x1a},  // 当前页签的字与横线
    {0x5a, 0x5a, 0x5a},  // 其他页签的字
    {0xe4, 0xe4, 0xe7},  // 页签条下面那道细线
    {0xb4, 0x6a, 0x1e},  // 提醒类的字
};

// 深色：底色压到近黑，字提到浅灰。主按钮的蓝与浅色那一档相同：白字压在它上面够亮，
// 它压在深底上也看得出来。正文与页面底色的明暗差拉得够开，不会出现深底深字。
constexpr Palette kDark = {
    {0xe8, 0xe8, 0xea},  // 正文文字
    {0xa0, 0xa0, 0xa8},  // 次要文字
    {0x5a, 0x5a, 0x62},  // 边框
    {0x1e, 0x1e, 0x22},  // 页面底色
    {0x2a, 0x2a, 0x30},  // 输入框底色
    {0x26, 0x26, 0x2b},  // 状态条底色
    {0xd0, 0xd0, 0xd6},  // 状态条字色
    {0x3a, 0x3a, 0x42},  // 最浅的那一档灰线
    {0x2f, 0x6f, 0xeb},  // 主按钮
    {0x3d, 0x7b, 0xf0},  // 主按钮，鼠标停在上面
    {0x1f, 0x56, 0xc9},  // 主按钮，按下
    {0xff, 0xff, 0xff},  // 主按钮上的字
    {0xf0, 0xf0, 0xf2},  // 当前页签的字与横线
    {0xa8, 0xa8, 0xb0},  // 其他页签的字
    {0x3a, 0x3a, 0x42},  // 页签条下面那道细线
    {0xd9, 0xa0, 0x5b},  // 提醒类的字
};

Appearance g_appearance = Appearance::Light;

void put(Fl_Color slot, const Rgb& color) {
    Fl::set_color(slot, color.r, color.g, color.b);
}

}  // namespace

// 每个色号一项，一个都不重复。
const Fl_Color kInk = FL_FREE_COLOR + 0;
const Fl_Color kMuted = FL_FREE_COLOR + 1;
const Fl_Color kBorder = FL_FREE_COLOR + 2;
const Fl_Color kPageBg = FL_FREE_COLOR + 3;
const Fl_Color kFieldBg = FL_FREE_COLOR + 4;
const Fl_Color kStatusBg = FL_FREE_COLOR + 5;
const Fl_Color kStatusInk = FL_FREE_COLOR + 6;
const Fl_Color kSeparator = FL_FREE_COLOR + 7;
const Fl_Color kAccent = FL_FREE_COLOR + 8;
const Fl_Color kAccentHover = FL_FREE_COLOR + 9;
const Fl_Color kAccentPressed = FL_FREE_COLOR + 10;
const Fl_Color kAccentInk = FL_FREE_COLOR + 11;
const Fl_Color kTabActive = FL_FREE_COLOR + 12;
const Fl_Color kTabIdle = FL_FREE_COLOR + 13;
const Fl_Color kTabLine = FL_FREE_COLOR + 14;
const Fl_Color kWarnInk = FL_FREE_COLOR + 15;

void apply_title_bar(Fl_Window* window) {
#ifdef _WIN32
    if (window == nullptr || window->shown() == 0) return;
    HWND handle = reinterpret_cast<HWND>(fl_xid(window));
    if (handle == nullptr) return;

    const BOOL dark = g_appearance == Appearance::Dark ? TRUE : FALSE;
    // 20 是 Windows 10 2004 以后的属性号；更老的系统认 19。前一个设不进去就试后一个。
    if (FAILED(DwmSetWindowAttribute(handle, 20, &dark, sizeof(dark))))
        DwmSetWindowAttribute(handle, 19, &dark, sizeof(dark));

    // 只设属性时有些版本要等到下次改窗口大小才重画标题栏，这一句让它当场换。
    SetWindowPos(handle, nullptr, 0, 0, 0, 0,
                 SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
#else
    (void)window;
#endif
}

Appearance appearance() { return g_appearance; }

Appearance saved_appearance() {
    return ui_config_get("appearance") == "dark" ? Appearance::Dark : Appearance::Light;
}

std::string appearance_label(Appearance which) {
    return which == Appearance::Dark ? "深色背景" : "浅色背景";
}

void apply_appearance(Appearance which) {
    const Palette& p = which == Appearance::Dark ? kDark : kLight;

    put(kInk, p.ink);
    put(kMuted, p.muted);
    put(kBorder, p.border);
    put(kPageBg, p.page_bg);
    put(kFieldBg, p.field_bg);
    put(kStatusBg, p.status_bg);
    put(kStatusInk, p.status_ink);
    put(kSeparator, p.separator);
    put(kAccent, p.accent);
    put(kAccentHover, p.accent_hover);
    put(kAccentPressed, p.accent_pressed);
    put(kAccentInk, p.accent_ink);
    put(kTabActive, p.tab_active);
    put(kTabIdle, p.tab_idle);
    put(kTabLine, p.tab_line);
    put(kWarnInk, p.warn_ink);

    // 界面库自己的控件按这五项取色：窗口与边框的底色、输入框与列表的底色、
    // 正文与标签的字色、置灰时的字色、选中一段文字时反白的底色。
    // 顺序有讲究：底色两项会把字色一并按明暗重算，字色放在最后设，以这里给的为准。
    //
    // 选中色要在建窗之后再提一次：第一次 Fl_Window::show 会走一遍界面库的取色，
    // 选中色被重新盖回它自带的深蓝，勾选框里的勾、列表选中的行、输入框选中文字都跟着变蓝。
    // 主窗口建好窗后调一次本函数（见 app.cpp），第一次打开就与切过一次底色一样。
    Fl::background(p.page_bg.r, p.page_bg.g, p.page_bg.b);
    Fl::background2(p.field_bg.r, p.field_bg.g, p.field_bg.b);
    Fl::foreground(p.ink.r, p.ink.g, p.ink.b);
    Fl::set_color(FL_INACTIVE_COLOR, p.muted.r, p.muted.g, p.muted.b);
    Fl::set_color(FL_SELECTION_COLOR, p.accent.r, p.accent.g, p.accent.b);

    g_appearance = which;

    // Windows 的标题栏不跟界面底色走，得给每个已经开着的窗口单独设一次。
    for (Fl_Window* window = Fl::first_window(); window != nullptr;
         window = Fl::next_window(window)) {
        apply_title_bar(window);
    }

    // 整个窗口重画一遍：这套界面处处按槽位取色，标成待重画就够了。
    Fl::redraw();
    Fl::flush();
}

Fl_Color mix_color(Fl_Color color, Fl_Color other, double weight) {
    uchar r1 = 0, g1 = 0, b1 = 0, r2 = 0, g2 = 0, b2 = 0;
    Fl::get_color(color, r1, g1, b1);
    Fl::get_color(other, r2, g2, b2);
    const auto blend = [weight](uchar from, uchar to) {
        return static_cast<uchar>(from * (1.0 - weight) + to * weight + 0.5);
    };
    return fl_rgb_color(blend(r1, r2), blend(g1, g2), blend(b1, b2));
}

// 普通按钮的三层底色：平时就是输入框那块白，悬停与按下是白底上叠一层正文色。
// 悬停一层比一层深，但都是平涂，不画亮边。
Fl_Color button_bg() { return kFieldBg; }
Fl_Color button_bg_hover() { return mix_color(kFieldBg, kInk, 0.11); }
Fl_Color button_bg_pressed() { return mix_color(kFieldBg, kInk, 0.18); }
// 点不动的时候退回原来那层灰（浅色下是白底上压 5% 的浅灰，深色下是比输入框底色亮一点的一层）：
// 字同时变灰，一眼就看出按不下去；框还在，位置也还在。
Fl_Color button_bg_disabled() { return mix_color(kFieldBg, kInk, 0.05); }

Appearance switch_appearance() {
    const Appearance next = g_appearance == Appearance::Dark ? Appearance::Light : Appearance::Dark;
    apply_appearance(next);
    ui_config_set("appearance", next == Appearance::Dark ? "dark" : "light");
    return next;
}

}  // namespace texttool::ui
