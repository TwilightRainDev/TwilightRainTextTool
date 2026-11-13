// 合并多份页：只定合并出来的那份文件叫什么名字。
//
// 页面上只有一行：输出文件名。处理页勾上「合并多份稿子」以后，按这一格的名字把源文件所在
// 文件夹里的稿子接成一份；这一格空着就用默认的名字。
//
// 这一页不管挑哪些稿子、按什么次序接、写到哪个文件夹，那些都在处理页与引擎里。
#include <FL/Fl_Box.H>
#include <FL/Fl_Input.H>

#include <algorithm>
#include <string>

#include "engine/io.h"
#include "ui/pages.h"
#include "ui/widgets.h"

namespace texttool::ui {
namespace {

// 框里空着时用的输出文件名，与命令行 join 的默认值一样。
constexpr const char* kDefaultOutputName = "joined.txt";

// 这一页只有一行，所以间距只有一项：这一行离页面顶边多远。
// 行的位置由 layout() 从页面顶边算出来，不写绝对坐标：页面拿到多大就按多大排。
constexpr int kTopGap = 12;

class JoinPage : public Page {
public:
    JoinPage(int x, int y, int w, int h) : Page(x, y, w, h) {
        box(FL_NO_BOX);
        color(kPageBg);
        begin();

        // 控件先按占位位置建好，实位一律在 layout() 里算：构造完调一次 reflow()，
        // 之后窗口尺寸一变，Page::resize 会再调一遍。
        output_label_ = make_text(x, y, kLabelWidth, kFormRowHeight, "合并成哪个文件：");
        output_ = make_input(x, y, kLabelWidth, kFormRowHeight, kDefaultOutputName);
        output_->tooltip("写在源文件所在的文件夹里，也可以写绝对路径指到别处；空着就用默认的名字");

        end();

        reflow();  // 按页面现有尺寸摆一遍
    }

    // 输出文件名：框里空着就用默认的名字。处理页按这一格拼出写盘的目标路径。
    std::string output_name() const {
        const std::string typed = texttool::trim_whitespace(output_->value());
        return typed.empty() ? std::string(kDefaultOutputName) : typed;
    }

protected:
    // 一行：标签从左边距起，输入框从字段列起到右边界为止。
    void layout() override {
        const int left = x() + kPagePadLeft;
        const int field_x = x() + kPageFieldLeft;
        const int right = x() + w() - kPagePadRight;
        const int field_width = std::max(0, right - field_x);
        const int row = y() + kTopGap;

        output_label_->resize(left, row, kLabelWidth, kFormRowHeight);
        output_->resize(field_x, row, field_width, kFormRowHeight);
    }

    // 这一页露出来时状态条说自己这一句。状态条只有一份文字，不重写就留着别的页的话。
    void on_shown() override {
        set_status("这一页只定合并出来的那份文件叫什么；勾上「合并多份稿子」以后在处理页里合并");
    }

private:
    Fl_Box* output_label_ = nullptr;
    Fl_Input* output_ = nullptr;  // 「合并成哪个文件」那一格
};

}  // namespace

namespace {

// 处理页要的那一页从页登记表取。
JoinPage* join_page_of(AppContext& app) {
    return static_cast<JoinPage*>(app.page(PageId::Join));
}

}  // namespace

Page* build_join_page(AppContext& app, int x, int y, int w, int h) {
    auto* page = new JoinPage(x, y, w, h);
    app.register_page(PageId::Join, page);
    return page;
}

std::string join_page_output_name(AppContext& app) {
    JoinPage* page = join_page_of(app);
    // 页面还没建出来时也给默认的名字：处理页拿到空串就没法拼目标路径。
    if (page == nullptr) return std::string(kDefaultOutputName);
    return page->output_name();
}

}  // namespace texttool::ui
