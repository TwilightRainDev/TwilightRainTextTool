// 版式检查的实现：三档尺寸，逐页量直接子控件。
//
// 数字与判断都在这一个文件里，页面上不改一个字。不合格的项打印出来是为了让人知道
// 该去改哪一页的 layout；检查本身不替页面想办法。
#include "ui/layout_check.h"

#include <FL/Fl.H>
#include <FL/Fl_Box.H>
#include <FL/Fl_Button.H>
#include <FL/Fl_Check_Button.H>
#include <FL/Fl_Choice.H>
#include <FL/Fl_Input.H>
#include <FL/Fl_Round_Button.H>

#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>
#include <vector>

#include "ui/tab_bar.h"
#include "ui/widgets.h"

namespace texttool::ui {
namespace {

// 三档尺寸：最小、默认、以及比默认再大一点的那一档（常见屏上最大化之后的宽度）。
struct WindowSize {
    int w = 0;
    int h = 0;
};
constexpr WindowSize kSizes[] = {
    {kWindowMinWidth, kWindowMinHeight},
    {kWindowWidth, kWindowHeight},
    {1366, 768},
};

struct Job {
    Fl_Window* window = nullptr;
    TabBar* tabs = nullptr;
    const std::vector<LayoutCheckPage>* pages = nullptr;
    int* exit_code = nullptr;
};

std::string rect_text(const Fl_Widget* widget) {
    char buffer[96];
    std::snprintf(buffer, sizeof(buffer), "(%d, %d, %d, %d)", widget->x(), widget->y(), widget->w(),
                  widget->h());
    return buffer;
}

// 控件在报错里怎么称呼：有文案就用文案，没有就按类型给个中文名。
// 两种都对不上时写「控件」：这里只是让人找得到那个控件，认得准不准不影响判据。
std::string widget_name(const Fl_Widget* widget) {
    const char* kind = "控件";
    if (dynamic_cast<const Fl_Input*>(widget) != nullptr) {
        kind = "输入框";
    } else if (dynamic_cast<const Fl_Choice*>(widget) != nullptr) {
        kind = "下拉框";
    } else if (dynamic_cast<const Fl_Check_Button*>(widget) != nullptr) {
        kind = "勾选框";
    } else if (dynamic_cast<const Fl_Round_Button*>(widget) != nullptr) {
        kind = "单选";
    } else if (dynamic_cast<const Fl_Button*>(widget) != nullptr) {
        kind = "按钮";
    } else if (dynamic_cast<const Fl_Box*>(widget) != nullptr) {
        kind = "文字";
    } else if (dynamic_cast<const Fl_Group*>(widget) != nullptr) {
        kind = "控件组";
    }

    std::string name(kind);
    const char* label = widget->label();
    if (label != nullptr && *label != '\0') {
        name += "「";
        name += label;
        name += "」";
    }
    return name;
}

// 这一页的直接子控件有没有越界、有没有互相重叠。毛病写进 details，返回不合格的条数。
// 宽或高不是正数的子控件两样都不算：它们本来就不占地方。
int check_page(const Fl_Group* page, std::vector<std::string>& details) {
    const int left = page->x();
    const int top = page->y();
    const int right = left + page->w();
    const int bottom = top + page->h();

    int problems = 0;
    std::vector<const Fl_Widget*> visible;

    for (int i = 0; i < page->children(); ++i) {
        const Fl_Widget* child = page->child(i);
        if (child == nullptr || child->w() <= 0 || child->h() <= 0) continue;

        const bool inside = child->x() >= left && child->y() >= top &&
                            child->x() + child->w() <= right &&
                            child->y() + child->h() <= bottom;
        if (!inside) {
            details.push_back("越界：" + widget_name(child) + " 在 " + rect_text(child) +
                              "，不在版面 " + rect_text(page) + " 之内");
            ++problems;
        }
        if (child->visible()) visible.push_back(child);
    }

    for (std::size_t i = 0; i < visible.size(); ++i) {
        for (std::size_t j = i + 1; j < visible.size(); ++j) {
            const Fl_Widget* first = visible[i];
            const Fl_Widget* second = visible[j];
            // 贴边不算重叠：一边的右边界等于另一边的左边界时，两块是并排的。
            if (first->x() >= second->x() + second->w() ||
                second->x() >= first->x() + first->w())
                continue;
            if (first->y() >= second->y() + second->h() ||
                second->y() >= first->y() + first->h())
                continue;
            details.push_back("重叠：" + widget_name(first) + " " + rect_text(first) + " 与 " +
                              widget_name(second) + " " + rect_text(second));
            ++problems;
        }
    }

    return problems;
}

void run_check(void* data) {
    auto* job = static_cast<Job*>(data);

    int sizes_ok = 0;
    int sizes_bad = 0;
    std::vector<std::string> details;

    for (const WindowSize& size : kSizes) {
        // 与用户拖动窗口尺寸走同一条路：主窗口那一段自己会重算 chrome 与各页。
        job->window->resize(job->window->x(), job->window->y(), size.w, size.h);
        Fl::flush();

        int page_x = 0;
        int page_y = 0;
        int page_w = 0;
        int page_h = 0;
        job->tabs->page_area(page_x, page_y, page_w, page_h,
                             job->window->h() - kStatusHeight);
        std::printf("[版式] 尺寸 %dx%d，页面区 %dx%d（左上角 %d, %d）\n", job->window->w(),
                    job->window->h(), page_w, page_h, page_x, page_y);

        int bad_pages = 0;
        for (const LayoutCheckPage& item : *job->pages) {
            if (item.page == nullptr) continue;
            details.clear();
            const int problems = check_page(item.page, details);
            if (problems == 0) {
                std::printf("[版式]   %s：通过\n", item.name.c_str());
                continue;
            }
            ++bad_pages;
            std::printf("[版式]   %s：%d 处不合格\n", item.name.c_str(), problems);
            for (const std::string& line : details) std::printf("[版式]     %s\n", line.c_str());
        }
        if (bad_pages == 0) {
            ++sizes_ok;
        } else {
            ++sizes_bad;
        }
    }

    const int total = static_cast<int>(sizeof(kSizes) / sizeof(kSizes[0]));
    std::printf("[版式] 三档尺寸：%d 档通过，%d 档有不合格项（共 %d 档）\n", sizes_ok, sizes_bad,
                total);
    if (job->exit_code != nullptr) *job->exit_code = sizes_bad == 0 ? 0 : 1;
    std::printf("[版式] %s\n", sizes_bad == 0 ? "看完，退出码 0" : "看不完，退出码 1");
    std::fflush(stdout);

    job->window->hide();  // 最后一个窗口藏起来，Fl::run 就返回了
    delete job;
}

}  // namespace

void start_layout_check_if_requested(Fl_Window* window, TabBar* tabs,
                                     const std::vector<LayoutCheckPage>& pages, int* exit_code) {
    const char* env = std::getenv("TEXTTOOL_LAYOUT_CHECK");
    if (env == nullptr || *env == '\0' || std::strcmp(env, "0") == 0) return;
    if (window == nullptr || tabs == nullptr || pages.empty()) return;

    auto* job = new Job;
    job->window = window;
    job->tabs = tabs;
    job->pages = &pages;
    job->exit_code = exit_code;
    // 等第一帧画出来再动手，与自检同一个理由：先让界面正常打开一次。
    Fl::add_timeout(0.1, run_check, job);
}

}  // namespace texttool::ui
