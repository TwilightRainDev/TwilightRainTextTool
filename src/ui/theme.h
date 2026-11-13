// 界面的底色：每个颜色只在这一处给值，两套（浅色、深色）在运行时换。
//
// 控件保存的是下面这些名字（颜色槽位），不是某个具体颜色。换底色时改槽位背后的值，
// 已经建好的控件下次重画就用新值，不必重建界面。
//
// 用哪一套由用户定：关掉程序再打开仍是同一套。这一项与「上次浏览的目录」
// 记在同一个文件里（界面配置文件 ui.conf 的 appearance 一项）。读不到配置时用浅色。
#pragma once

#include <FL/Enumerations.H>
#include <FL/Fl_Window.H>

#include <string>

namespace texttool::ui {

// 两套底色。
enum class Appearance { Light, Dark };

// 正文文字、次要文字、边框与分隔线。浅色下边框是桌面版实测的那个深灰（118,118,118）：
// 按钮、输入框、下拉框、列表框、报告框都用它，比正文浅、比底色深，一眼看得出轮廓。
extern const Fl_Color kInk;
extern const Fl_Color kMuted;
extern const Fl_Color kBorder;

// 页面与窗口的底色、输入框与报告框的底色。
extern const Fl_Color kPageBg;
extern const Fl_Color kFieldBg;

// 状态条：底色与字色。
extern const Fl_Color kStatusBg;
extern const Fl_Color kStatusInk;
// 最浅的那一档灰线。用在点不动的主按钮轮廓上：画得再深一点就会被当成能点的按钮。
extern const Fl_Color kSeparator;

// 主按钮：平时的底色、鼠标停在上面、按下、按钮上的字。
extern const Fl_Color kAccent;
extern const Fl_Color kAccentHover;
extern const Fl_Color kAccentPressed;
extern const Fl_Color kAccentInk;

// 页签条：当前页的字与横线、其他页的字、页签条下面那道细线。
extern const Fl_Color kTabActive;
extern const Fl_Color kTabIdle;
extern const Fl_Color kTabLine;

// 提醒类的字色，用在「直接改原文件（危险）」那一行。
extern const Fl_Color kWarnInk;

// 普通按钮的底色：平时、鼠标停在上面、按下，以及禁用时那一档。平时与输入框同一块白底，
// 悬停与按下由输入框底色与正文色按比例混出来（见下面的 mix_color），禁用退回原来那层浅灰。
// 这几档不占色号：色号一共只有十六个，已经用满了。
Fl_Color button_bg();
Fl_Color button_bg_hover();
Fl_Color button_bg_pressed();
Fl_Color button_bg_disabled();

// 两色按比例混：weight 为 0 给 color，为 1 给 other。返回的是直接带 RGB 的颜色，
// 不占用色号。禁用态与按钮底色都靠它算。
Fl_Color mix_color(Fl_Color color, Fl_Color other, double weight);

// 现在用的是哪一套。
Appearance appearance();

// 配置文件里存的那一套。没存过、或者存的值认不出来，就是浅色。
Appearance saved_appearance();

// 换成这一套。界面库自己的控件（输入框、下拉、列表、勾选、按钮）也跟着变，
// 换完把整个窗口重画一遍。
void apply_appearance(Appearance which);

// 让窗口标题栏跟着界面底色走。Windows 上标题栏由系统画，不设这一项就是白底，
// 深色界面顶上永远亮着一条白带；浅色界面则要把它设回浅色，免得跟着系统深色模式走。
// 别的平台由窗口管理器决定，这个函数什么也不做。窗口还没显示、或者拿不到系统句柄时也什么也不做。
void apply_title_bar(Fl_Window* window);

// 换成另一套并记住这一选择，返回换成的这一套。
Appearance switch_appearance();

// 这一套在界面上的叫法：深色背景、浅色背景。按钮上写的就是这个。
std::string appearance_label(Appearance which);

}  // namespace texttool::ui
