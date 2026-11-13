// 版式检查：给几档窗口尺寸，逐页量直接子控件有没有越出页面、有没有互相重叠。
//
// 它只回答两件事：控件有没有被切断、两个控件有没有叠在一起。好看不好看它不管，
// 也不改任何坐标：页面按自己的 layout 摆，这里只读摆完的结果。窗口拖到别的尺寸时
// 用户看到的问题，与跑一次这个检查报出来的问题是同一批。
//
// 触发方式用环境变量 TEXTTOOL_LAYOUT_CHECK=1，不用命令行参数：界面库会去解析命令行，
// 它不认识的写法会打一串用法提示出来（与自检、页签序号那几个开关同一个考虑）。
#pragma once

#include <FL/Fl_Group.H>
#include <FL/Fl_Window.H>

#include <string>
#include <vector>

namespace texttool::ui {

class TabBar;

// 一页在检查里的名字与页面对象。名字只用来指认打印出来是哪一页，不参与判断。
struct LayoutCheckPage {
    std::string name;
    Fl_Group* page = nullptr;
};

// 环境变量开了就排一次检查，没开什么都不做（窗口照常跑）。
//
// 开了的时候：等 0.1 秒让第一帧画出来，然后把窗口依次设成最小、默认、再大一点三档尺寸。
// 每档尺寸下让窗口走与用户拖动尺寸同一条路（window->resize，也就是主窗口自己那一段排布），
// 刷新一遍，然后逐页看两件事：
//   1. 每个直接子控件整个落在这一页的版面之内；
//   2. 看得见的直接子控件两两不重叠（宽或高不是正数的不算）。
// 结果打到标准输出：一档一行头，每页一行结论，不合格的接着写出是哪个控件、在哪里、什么毛病。
//
// 跑完把窗口藏起来（界面库因此返回），并给 *exit_code 定值：全部通过是 0，有不合格是 1。
// exit_code 指主程序里那个全局退出码，与自检共用。
//
// pages 要活到检查跑完（主程序里用局部量传给它的阶段之内就够，它在 Fl::run 之前排定时器）。
void start_layout_check_if_requested(Fl_Window* window, TabBar* tabs,
                                     const std::vector<LayoutCheckPage>& pages, int* exit_code);

}  // namespace texttool::ui
