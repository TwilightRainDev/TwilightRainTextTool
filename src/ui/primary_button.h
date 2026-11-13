// 主按钮：每一页上那个真正干活的按钮（开始处理、开始合并、开始换标点、开始排版、开始检查）。
//
// 一页里只有它一个用强调色实心底，其余按钮都是白底细框，一眼就能分出主次。
// 底色永远是强调色，字永远是白字；点不动的时候换成灰底灰字加一道浅框：
// 灰底灰字一眼就看出现在点不动，而那块实心形状还在，不会与「这一页没有主按钮」混起来。
//
// 只用到一个事实：FLTK 的按钮在鼠标悬停、按下时会把状态存在 value() 与 Fl::pushed() 里，
// 照着这两个值换颜色就行，不需要额外的事件处理。
#pragma once

#include <FL/Fl.H>
#include <FL/Fl_Button.H>
#include <FL/fl_draw.H>

#include "ui/theme.h"
#include "ui/widgets.h"

namespace texttool::ui {

class PrimaryButton : public Fl_Button {
public:
    PrimaryButton(int x, int y, int w, int h, const char* label = nullptr)
        : Fl_Button(x, y, w, h, label) {
        box(FL_NO_BOX);
        labelsize(15);
    }

    void draw() override {
        const bool enabled = active_r() != 0;
        const bool pressed = enabled && (value() != 0 || Fl::pushed() == this);
        const bool hovered = enabled && Fl::belowmouse() == this;

        Fl_Color fill = pressed ? kAccentPressed : (hovered ? kAccentHover : kAccent);
        Fl_Color ink = kAccentInk;
        if (!enabled) {
            // 点不动：整块退成普通按钮那一档灰，字变灰，轮廓用最浅的那档灰。
            fill = button_bg_disabled();
            ink = kMuted;
        }

        // 圆角与普通按钮同一套画法：逐行递减，角上不缺像素。
        fill_round_rect(x(), y(), w(), h(), kButtonRadius, fill);
        if (!enabled) stroke_round_rect(x(), y(), w(), h(), kButtonRadius, kSeparator);

        fl_color(ink);
        fl_font(labelfont(), labelsize());
        fl_draw(label(), x(), y(), w(), h(), FL_ALIGN_CENTER | FL_ALIGN_CLIP);

        if (enabled && Fl::focus() == this && keyboard_input()) {
            stroke_round_rect(x() + 3, y() + 3, w() - 6, h() - 6, kButtonRadius, kAccentInk);
        }
    }

    int handle(int event) override {
        const int result = Fl_Button::handle(event);
        // 鼠标进出要重画，否则悬停变色不会出现。
        if (event == FL_ENTER || event == FL_LEAVE) redraw();
        return result;
    }
};

}  // namespace texttool::ui
