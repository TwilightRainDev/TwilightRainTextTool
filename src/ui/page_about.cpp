// 关于页：这是什么工具、谁做的、去哪儿找新版本，以及把这份工具的设置搬走或搬回来。
//
// 这一页给用户看，不给开发看：不写界面用了哪个界面库、规则有多少条、字体叫什么、
// 资源占用怎么验收。这些是内部合同，写在这里等于让读者去查词。
//
// 备份把四样打成一个包：换标点的规则、预设方案、角色表、底色。换标点的规则取「换标点」页
// 现在列表里那些（这次运行里改过但还没存下的也算），其余三样按文件与当前选择取。
// 恢复先读通整份包，再问一句会换掉哪几样，答「恢复」才写文件；写的位置与命令行读的是同一处。
// 恢复以后，换标点页的规则列表与剧本排版页的角色表跟着换，界面上看到的就是包里的内容。
#include <FL/Fl.H>
#include <FL/Fl_Box.H>
#include <FL/Fl_Button.H>
#include <FL/Fl_Group.H>
#include <FL/fl_ask.H>

#include <algorithm>
#include <exception>
#include <string>

#include "engine/config_pack.h"
#include "engine/io.h"
#include "ui/pages.h"
#include "ui/theme.h"
#include "ui/widgets.h"

// 版本号由 CMake 从 project(... VERSION ...) 传进来，界面里不另写一份，两边不会对不上。
#ifndef TEXTTOOL_VERSION
#error "TEXTTOOL_VERSION 没传进来：CMakeLists.txt 里 texttool-gui 的 target_compile_definitions 要带上它"
#endif

namespace texttool::ui {
namespace {

// 备份的包默认叫什么。用户想改名就在保存对话框里改。
constexpr const char* kPackageFileName = "文本工具设置备份.json";

// 程序图标与作者头像的文件名，与 CMakeLists.txt 复制到程序旁边的那两份一致。
constexpr const char* kIconFile = "icon.png";
constexpr const char* kAvatarFile = "avatar.png";

// 这一页的纵向排布。横向上只用页面的左边界、右边界与中心，见 layout()。
//
// 两张图固定 64 见方：图片放大会糊，所以尺寸不跟窗口走，只有位置按页面中心摆。
constexpr int kTopGap = 8;          // 页面顶边到两张图
constexpr int kFaceSize = 64;
constexpr int kFaceGap = 12;        // 两张图之间
constexpr int kFacesToTitleGap = 14;

// 标题是 22 号字，比普通行高一档，压回 kFormRowHeight 会切掉字头字尾。
constexpr int kTitleHeight = 34;

// 行与行之间的三种间距：普通的隔一点，同一段里的两行几乎贴住，换段时多空一点。
constexpr int kRowGap = 6;
constexpr int kTightGap = 2;
constexpr int kBlockGap = 20;

// 「备份设置」「恢复设置」那两个按钮的宽度与它们之间的空隙；底色那一排同理。
constexpr int kButtonWidth = 108;
constexpr int kButtonGap = 12;
constexpr int kAppearanceButtonWidth = 128;
constexpr int kAppearanceHintWidth = 168;
constexpr int kAppearanceGap = 10;

// 选一份现有的文件。取消时返回空串。
std::string choose_open_file(const std::string& title) {
    PickerRequest request;
    request.title = title;
    request.start_dir = last_browse_directory();
    request.extensions = {"json"};
    return pick_path(request);
}

// 选一个要写的位置。先填好名字，取消时返回空串。
std::string choose_save_file(const std::string& title, const std::string& name) {
    PickerRequest request;
    request.title = title;
    request.start_dir = last_browse_directory();
    request.suggested_name = name;
    request.extensions = {"json"};
    return pick_path(request);
}

// 确认框里那段话：会换掉哪几样、各有多少、换掉以后退不回来。
std::string import_question(const texttool::ConfigPackage& package) {
    std::string question = "恢复以后，现在这几样会被整个换掉：\n";
    question += "换标点的规则（包里 " + std::to_string(package.replace_rules.size()) + " 条）\n";
    question += "预设方案（包里 " + std::to_string(package.replace_schemes.size()) + " 份）\n";
    question += "角色表（包里 " + std::to_string(package.vn_schemes.size()) + " 份）\n";
    if (!package.appearance.empty()) {
        const Appearance which =
            package.appearance == "dark" ? Appearance::Dark : Appearance::Light;
        question += "底色（包里是" + appearance_label(which) + "）\n";
    }
    question += "换掉就退不回来了，想留着现在这一份，先备份一份。\n要恢复吗？";
    return question;
}

class AboutPage : public Page {
public:
    AboutPage(AppContext& app, int x, int y, int w, int h) : Page(x, y, w, h), app_(app) {
        box(FL_NO_BOX);
        color(kPageBg);
        begin();

        // 控件一律先给个零尺寸的空位，位置与尺寸全部交给 layout()，
        // 构造完调一次 reflow() 才真正摆上去。
        icon_ = new Fl_Box(x, y, 0, 0);
        icon_->box(FL_NO_BOX);
        if (Fl_Image* image = bundled_image(app.program_path, kIconFile))
            icon_->image(image->copy(kFaceSize, kFaceSize));
        avatar_ = new Fl_Box(x, y, 0, 0);
        avatar_->box(FL_NO_BOX);
        if (Fl_Image* image = bundled_image(app.program_path, kAvatarFile))
            avatar_->image(image->copy(kFaceSize, kFaceSize));

        // 标题与两行说明占满页面的整幅宽，字在中间对齐。
        title_ = new Fl_Box(x, y, 0, 0, "TwilightRain 的文本工具");
        title_->labelsize(22);
        title_->labelcolor(kInk);
        title_->box(FL_NO_BOX);

        const std::string version_text = std::string("版本 ") + TEXTTOOL_VERSION;
        version_ = new Fl_Box(x, y, 0, 0);
        version_->copy_label(version_text.c_str());
        version_->labelsize(14);
        version_->labelcolor(kMuted);
        version_->box(FL_NO_BOX);

        intro_ = new Fl_Box(
            x, y, 0, 0,
            "把散成短行的稿子接回整句，把几份稿子合并成一份，把标点换成中文的，把剧本排成一句一行。");
        intro_->labelsize(15);
        intro_->labelcolor(kInk);
        intro_->box(FL_NO_BOX);

        intro2_ = new Fl_Box(x, y, 0, 0, "写好之后，它还能把稿子里读起来别扭的地方一条条指给你看。");
        intro2_->labelsize(15);
        intro2_->labelcolor(kInk);
        intro2_->box(FL_NO_BOX);

        author_ = new Fl_Box(x, y, 0, 0, "作者：TwilightRain");
        author_->labelsize(14);
        author_->labelcolor(kMuted);
        author_->box(FL_NO_BOX);

        repo_ = new Fl_Box(x, y, 0, 0, "新版本与说明：github.com/TwilightRainDev/TwilightRainTextTool");
        repo_->labelsize(14);
        repo_->labelcolor(kMuted);
        repo_->box(FL_NO_BOX);

        export_button_ = make_button(x, y, 0, 0, "备份设置");
        export_button_->labelsize(14);
        export_button_->callback(on_export, this);
        import_button_ = make_button(x, y, 0, 0, "恢复设置");
        import_button_->labelsize(14);
        import_button_->callback(on_import, this);

        // 底色这一项：按钮上写现在用的是哪一套，点一下换另一套。
        appearance_button_ = make_button(x, y, 0, 0, appearance_label(appearance()));
        appearance_button_->labelsize(14);
        appearance_button_->callback(on_switch_appearance, this);
        appearance_hint_ = make_text(x, y, 0, 0, "点一下换成另一种底色", 14, kMuted);

        end();

        // 页面拿到的大小可能已经是窗口这一次的尺寸，构造完立刻按它摆一遍。
        reflow();
    }

protected:
    // 这一页露出来时状态条说自己这一句。状态条只有一份文字，不重写就留着别的页的话；
    // 这一页没有什么要等着用户填的，只说它自己是什么。
    void on_shown() override { set_status("这一页是版本、作者，以及把设置搬走或搬回来"); }

    // 按页面当时的左右边界与中心摆直接子控件。
    //
    // 横向：文字行从页内左边到页内右边；两张图、两排按钮按页面宽度取中，
    // 窗口变宽时它们跟着往中间走。纵向从页面顶边往下排，行高取 kFormRowHeight，
    // 两张图与标题各有自己的高度（见文件头那几个常量）。
    void layout() override {
        const int left = x() + kPagePadLeft;
        const int right = x() + w() - kPagePadRight;
        const int row_width = std::max(0, right - left);
        const int center = x() + w() / 2;

        int row = y() + kTopGap;

        const int faces_left = center - (kFaceSize * 2 + kFaceGap) / 2;
        icon_->resize(faces_left, row, kFaceSize, kFaceSize);
        avatar_->resize(faces_left + kFaceSize + kFaceGap, row, kFaceSize, kFaceSize);
        row += kFaceSize + kFacesToTitleGap;

        title_->resize(left, row, row_width, kTitleHeight);
        row += kTitleHeight + kRowGap;

        version_->resize(left, row, row_width, kFormRowHeight);
        row += kFormRowHeight + kRowGap;

        intro_->resize(left, row, row_width, kFormRowHeight);
        row += kFormRowHeight + kTightGap;

        intro2_->resize(left, row, row_width, kFormRowHeight);
        row += kFormRowHeight + kBlockGap;

        author_->resize(left, row, row_width, kFormRowHeight);
        row += kFormRowHeight;

        repo_->resize(left, row, row_width, kFormRowHeight);
        row += kFormRowHeight + kBlockGap;

        const int button_x = center - (kButtonWidth * 2 + kButtonGap) / 2;
        export_button_->resize(button_x, row, kButtonWidth, kFormRowHeight);
        import_button_->resize(button_x + kButtonWidth + kButtonGap, row, kButtonWidth,
                               kFormRowHeight);
        row += kFormRowHeight + kBlockGap;

        const int appearance_x =
            center - (kAppearanceButtonWidth + kAppearanceGap + kAppearanceHintWidth) / 2;
        appearance_button_->resize(appearance_x, row, kAppearanceButtonWidth, kFormRowHeight);
        appearance_hint_->resize(appearance_x + kAppearanceButtonWidth + kAppearanceGap, row,
                                 kAppearanceHintWidth, kFormRowHeight);
    }

private:
    // ------------------------------------------------------------ 备份设置

    void export_config() {
        // 先把包凑齐：凑不齐就直说，不必让人先白挑一次位置。
        texttool::ConfigPackage package;
        std::string error;
        package.replace_rules = replace_page_rules(app_, error);
        if (!error.empty()) {
            set_status("备份不了：" + error);
            return;
        }
        if (!texttool::load_replace_schemes(app_.program_path.c_str(), package.replace_schemes,
                                            error) ||
            !texttool::load_vn_schemes(app_.program_path.c_str(), package.vn_schemes, error)) {
            set_status("备份不了：" + error);
            return;
        }
        // 底色按现在用的是哪一套装进包里；恢复到另一台机器时跟着换成这一套。
        package.appearance = appearance() == Appearance::Dark ? "dark" : "light";

        const std::string path = choose_save_file("备份设置", kPackageFileName);
        if (path.empty()) {
            set_status("没有备份，什么都没写");
            return;
        }

        try {
            texttool::save_config_package(path, package);
        } catch (const std::exception& failure) {
            set_status(std::string("备份不了：") + failure.what());
            return;
        }
        set_status("设置已备份到：" + path);
    }

    // ------------------------------------------------------------ 恢复设置

    void import_config() {
        const std::string path = choose_open_file("恢复设置");
        if (path.empty()) {
            set_status("没有恢复，设置没动");
            return;
        }

        // 先整份读通，再问一句。读不通的那一份不再往下走，盘上一个字不动。
        texttool::ConfigPackage package;
        std::string error;
        if (!texttool::load_config_package(path, package, error)) {
            set_status("恢复不了：" + error);
            return;
        }

        const std::string question = import_question(package);
        // 右边的按钮是「取消」，按窗口的关闭框与 Esc 也落回它；要让用户真按一下才恢复。
        if (fl_choice("%s", "取消", "恢复", nullptr, question.c_str()) != 1) {
            set_status("没有恢复，设置没动");
            return;
        }

        try {
            texttool::save_config_parts(app_.program_path.c_str(), package);
        } catch (const std::exception& failure) {
            set_status(std::string("恢复不了：") + failure.what());
            return;
        }

        // 盘上换好了，界面跟着换：换标点页的列表、剧本排版页的角色表、这一页的底色。
        replace_page_apply_imported_rules(app_, package.replace_rules);
        vn_page_reload_schemes(app_);

        std::string message = "设置已恢复：换标点的规则 " +
                              std::to_string(package.replace_rules.size()) + " 条、预设方案 " +
                              std::to_string(package.replace_schemes.size()) + " 份、角色表 " +
                              std::to_string(package.vn_schemes.size()) + " 份";
        if (!package.appearance.empty()) {
            const Appearance wanted =
                package.appearance == "dark" ? Appearance::Dark : Appearance::Light;
            apply_appearance(wanted);
            ui_config_set("appearance", wanted == Appearance::Dark ? "dark" : "light");
            appearance_button_->copy_label(appearance_label(wanted).c_str());
            appearance_button_->redraw();
            message += "，底色换成" + appearance_label(wanted);
        }
        set_status(message);
    }

    // -------------------------------------------------------------- 回调

    static void on_export(Fl_Widget*, void* data) {
        static_cast<AboutPage*>(data)->export_config();
    }

    static void on_import(Fl_Widget*, void* data) {
        static_cast<AboutPage*>(data)->import_config();
    }

    static void on_switch_appearance(Fl_Widget* widget, void* data) {
        auto* page = static_cast<AboutPage*>(data);
        const Appearance next = switch_appearance();
        page->appearance_button_->copy_label(appearance_label(next).c_str());
        static_cast<Fl_Button*>(widget)->redraw();
        set_status("底色已换成" + appearance_label(next) + "，下次打开还是这一套");
    }

    AppContext& app_;
    Fl_Box* icon_ = nullptr;
    Fl_Box* avatar_ = nullptr;
    Fl_Box* title_ = nullptr;
    Fl_Box* version_ = nullptr;
    Fl_Box* intro_ = nullptr;
    Fl_Box* intro2_ = nullptr;
    Fl_Box* author_ = nullptr;
    Fl_Box* repo_ = nullptr;
    Fl_Button* export_button_ = nullptr;
    Fl_Button* import_button_ = nullptr;
    Fl_Button* appearance_button_ = nullptr;
    Fl_Box* appearance_hint_ = nullptr;
};

}  // namespace

Page* build_about_page(AppContext& app, int x, int y, int w, int h) {
    auto* page = new AboutPage(app, x, y, w, h);
    app.register_page(PageId::About, page);
    return page;
}

}  // namespace texttool::ui
