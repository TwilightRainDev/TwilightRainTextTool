// 图形界面入口：只建窗口、页签条、状态条、分隔线，各页各在自己的文件里建。
//
// 界面只做三件事：收集参数、把参数交给引擎、把结果显示出来。文本处理逻辑一律不写在这里，
// 否则命令行与界面会各长出一套行为，两边输出就再也对不上。
//
// 窗口能拉：尺寸变了由主窗口（下面的 MainWindow）重算页签条、页面区、分隔线与状态条，
// 再让每页按新尺寸重排一次。页面上不假定窗口是某一个固定尺寸。
// 外观是白底、无边框页签，当前项字底下一道蓝色短线；标签一列对齐、控件一列对齐。
// 页签层顶端三项（处理、关于、高级），其中「高级」自己画成一个下拉，细节见 ui/tab_bar.h。
//
// 下面这些环境变量只给截图、自检与版式检查用，界面上没有对应控件，也不写用户配置：
//
//   TEXTTOOL_UI_SIZE=宽x高      启动尺寸；给了以后最小尺寸也放成同一个数（不能拉）
//   TEXTTOOL_UI_SCALE=倍数      0.75 到 3，不给就按屏幕自己估
//   TEXTTOOL_LAYOUT_CHECK=1     三档尺寸逐页查越界与重叠，全过退 0，有一处不合格退 1
//   TEXTTOOL_SELF_CHECK=稿件    自检：翻到处理页，按页面上的勾选真跑一遍、真写盘，打印字数与耗时后退出
//   TEXTTOOL_SELF_CHECK_PAGE=n  自检只跑处理页上的一项：1 接回断行、2 合并多份、3 换标点、4 剧本排版；
//                               不给就按处理页现在勾的那些跑。验收脚本里的 1 到 4 照旧好使。
//   TEXTTOOL_START_TAB=n        启动时翻到第 n 页，截图用。这是页签层的扁平序号：
//                               1 处理、2 关于、3 接回断行、4 合并多份、5 换标点、6 剧本排版、7 文字体检。
//                               3 到 7 就是「高级」下拉里那五项。重排页签时这张表跟着改。
//
// 界面同时最多开四份，第五份不开（判定方式见 ui/run_guard.h）：弹一句「已经开着四份文本工具
// 了，先关掉一份再开」，退出码 3。自检与版式检查那一份也算在四份里头，它们本来是一份一份跑的。
#include <FL/Fl.H>
#include <FL/Fl_Box.H>
#include <FL/Fl_Button.H>
#include <FL/Fl_Window.H>

#include <algorithm>
#include <chrono>
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <exception>
#include <memory>
#include <string>
#include <vector>

#include "engine/io.h"
#include "engine/rules.h"
#include "ui/cjk_font.h"
#include "ui/layout_check.h"
#include "ui/pages.h"
#include "ui/run_guard.h"
#include "ui/tab_bar.h"
#include "ui/widgets.h"

namespace {

using texttool::ui::AppContext;
using texttool::ui::TabBar;

Fl_Window* g_window = nullptr;

// 自检模式跑失败时的退出码。正常启动不碰它。版式检查也用这一个。
int g_exit_code = 0;

// 已经有四份开着时，第五份的退出码。它要是一个非零值，脚本才看得出这一份不是正常开的。
constexpr int kTooManyInstancesExitCode = 3;

void on_window_close(Fl_Widget*, void*) { g_window->hide(); }

// 窗口里那几块共用的摆法：页签条与状态条左右留出的边、页签条顶端的位置与高度。
// 页面区多大由这几处算出来（见 TabBar::page_area），页面自己不碰这些数。
constexpr int kChromeInset = 12;
constexpr int kTabBarTop = 8;
constexpr int kTabBarHeight = 40;

// 主窗口。窗口尺寸一变就这样走一遍：先改自己的尺寸，再把页签条、页面区、分隔线与状态条重算，
// 最后让每页按新尺寸重排一次（Page::resize 会调它自己的 layout）。
//
// 子控件全由 layout_chrome 摆，不设 resizable()：设了界面库会按新旧尺寸的比例把子控件搬一遍，
// 搬完再摆一遍是两笔账。Fl_Window::resize 在没设 resizable() 的时候只改窗口自己的尺寸、不搬子控件
// （界面库 Fl_Group::resize 的第一支），所以这一条路是干净的；
// 反过来在这里只调 Fl_Widget::resize 的话，屏幕上那个窗口不会跟着变（程序自己改尺寸时）。
class MainWindow : public Fl_Window {
public:
    MainWindow(int x, int y, int w, int h, const char* title) : Fl_Window(x, y, w, h, title) {}

    TabBar* tabs = nullptr;
    Fl_Box* status = nullptr;

    // 页面区到状态条顶边为止；状态条压在底下，灰带顶到窗口两边。
    int content_bottom() const { return h() - texttool::ui::kStatusHeight; }

    void layout_chrome() {
        if (tabs != nullptr) {
            tabs->resize(kChromeInset, kTabBarTop, std::max(0, w() - 2 * kChromeInset),
                         kTabBarHeight);
            int page_x = 0;
            int page_y = 0;
            int page_w = 0;
            int page_h = 0;
            tabs->page_area(page_x, page_y, page_w, page_h, content_bottom());
            for (int i = 0; i < tabs->page_count(); ++i) {
                if (Fl_Group* page = tabs->page(i)) page->resize(page_x, page_y, page_w, page_h);
            }
        }
        if (status != nullptr) {
            status->resize(0, content_bottom(), w(), texttool::ui::kStatusHeight);
        }
    }

    void resize(int x, int y, int w, int h) override {
        Fl_Window::resize(x, y, w, h);
        layout_chrome();
        redraw();
    }
};

// 启动尺寸：默认是 kWindowWidth x kWindowHeight，最小是 kWindowMinWidth x kWindowMinHeight。
// 截图与冒烟测试要用别的尺寸时给环境变量 TEXTTOOL_UI_SIZE=宽x高：这时最小尺寸也放成同一个数
// （最小等于启动尺寸就是不能拉）。写法读不出来就按默认尺寸走，并在标准错误说一句：
// 这种写法一旦写错，悄悄生效与悄悄不生效都不能看出是哪个，干脆两样都说。
struct StartSize {
    int w = texttool::ui::kWindowWidth;
    int h = texttool::ui::kWindowHeight;
    int min_w = texttool::ui::kWindowMinWidth;
    int min_h = texttool::ui::kWindowMinHeight;
};

StartSize detect_start_size() {
    StartSize size;
    const char* env = std::getenv("TEXTTOOL_UI_SIZE");
    if (env == nullptr || *env == '\0') return size;

    std::string text(env);
    for (char& ch : text) {
        if (ch == 'X' || ch == '*') ch = 'x';
    }
    int width = 0;
    int height = 0;
    if (std::sscanf(text.c_str(), "%dx%d", &width, &height) == 2 && width >= 200 &&
        height >= 200) {
        size.w = width;
        size.h = height;
        size.min_w = width;
        size.min_h = height;
        return size;
    }

    std::fprintf(stderr, "[界面] TEXTTOOL_UI_SIZE 读不出来：%s（要「宽x高」，例如 900x560），按默认尺寸启动\n",
                 env);
    return size;
}

// 自检模式要用的几样：主窗口交给各页的东西、要处理的稿件路径、只跑哪一项。
struct SelfCheck {
    AppContext* app = nullptr;
    std::string doc;
    int step = 0;  // 0 按处理页现在勾的跑；1 接回断行、2 合并多份、3 换标点、4 剧本排版
};

// 自检模式：只给测量内存与耗时用，用户平时碰不到。
//
// 设了 TEXTTOOL_SELF_CHECK=<稿件路径> 就按下面的顺序走：窗口画出来并翻到处理页，把路径填进
// 源文件框，按页面上的勾选真的跑一遍处理、真的写盘、真的刷新界面，把字数与耗时打到标准输出，
// 然后自己退出。处理跑的是点「开始处理」那条路（同一个函数），所以量到的峰值就是用户自己
// 处理这份稿时的峰值。界面上没有对应的控件，也不写任何用户配置。
//
// TEXTTOOL_SELF_CHECK_PAGE 可以只跑其中一项（1 接回断行、2 合并多份、3 换标点、4 剧本排版），
// 量哪一项就在命令行上连这一项一起给 measure_peak.py：
//     python3 tools/measure_peak.py --limit 40 --env TEXTTOOL_SELF_CHECK=稿件
//         --env TEXTTOOL_SELF_CHECK_PAGE=3 build-linux/texttool-gui
void run_self_check(void* data) {
    std::unique_ptr<SelfCheck> task(static_cast<SelfCheck*>(data));
    Fl::flush();  // 启动那一帧先画出来，起步的内存与正常打开时一样

    task->app->tabs->select_page(task->app->page(texttool::ui::PageId::Process));
    Fl::check();

    const auto started = std::chrono::steady_clock::now();
    const texttool::ui::SelfCheckSummary summary =
        texttool::ui::run_process_page_on_file(*task->app, task->doc, task->step);
    const auto elapsed = std::chrono::duration_cast<std::chrono::milliseconds>(
        std::chrono::steady_clock::now() - started);

    // 真的刷新一遍：状态条、源文件框、编码行都变了，画出来的这一帧是处理完的样子。
    task->app->window->redraw();
    Fl::flush();

    std::printf("[自检] 只跑：%d 稿子：%s\n", task->step, task->doc.c_str());
    std::printf("[自检] 字数：源文 %zu，结果 %zu（都按 UTF-8 码点算）\n", summary.input_chars,
                summary.output_chars);
    std::printf("[自检] 耗时：%lld 毫秒\n", static_cast<long long>(elapsed.count()));
    std::printf("[自检] 结果：%s\n", summary.output.empty() ? "没写成" : summary.output.c_str());
    std::printf("[自检] 状态条：%s\n", texttool::ui::status_text().c_str());
    if (summary.output.empty()) {
        g_exit_code = 1;
        std::fprintf(stderr, "[自检] 这一轮没写成，退出码 1\n");
    }
    std::fflush(stdout);
    std::fflush(stderr);
    g_window->hide();  // 最后一个窗口藏起来，Fl::run 就返回了
}

// 界面缩放。整份界面按默认尺寸那一档排，屏幕像素比这多的时候要放大，
// 否则字在 2560 宽的屏上小到要看不清。
//
// 先看环境变量 TEXTTOOL_UI_SCALE（0.75 到 3 之间，写 1 就是不缩放，截图对照时用得上），
// 没写就按屏幕自己估：系统报的 DPI 与屏幕可用区域，哪个更要求放大就用哪个，
// 取到四分之一档（1、1.25、1.5、1.75、2），上限两倍。
// 1920x1080 与更小的屏幕算出来是 1，界面与桌面版逐像素同大。
float detect_ui_scale() {
    if (const char* env = std::getenv("TEXTTOOL_UI_SCALE")) {
        const double value = std::atof(env);
        if (value >= 0.75 && value <= 3.0) return static_cast<float>(value);
    }

    float horizontal_dpi = 0.0f;
    float vertical_dpi = 0.0f;
    Fl::screen_dpi(horizontal_dpi, vertical_dpi, 0);

    int area_x = 0;
    int area_y = 0;
    int area_w = 0;
    int area_h = 0;
    Fl::screen_work_area(area_x, area_y, area_w, area_h, 0);

    const float by_dpi = vertical_dpi > 110.0f ? vertical_dpi / 96.0f : 1.0f;
    const float by_pixels =
        area_w > 0 && area_h > 0 ? std::max(area_w / 1920.0f, area_h / 1080.0f) : 1.0f;

    float scale = std::max(by_dpi, by_pixels);
    scale = std::round(scale * 4.0f) / 4.0f;
    return std::clamp(scale, 1.0f, 2.0f);
}

// 第五份被拒时给用户看的那一句话。一句说清是已经开着四份，不是程序坏了或者漏了什么东西。
//
// 不直接用界面库的 fl_alert：那个要有人点一下才返回。没人看着的时候（脚本连着起五份、
// 冒烟测试）会一直挂在那里。所以自己搭一个小窗：点「知道了」就关，没人点 10 秒以后自己关。
void show_instance_limit_notice() {
    Fl_Window window(430, 132, "文本工具");
    Fl_Box message(24, 22, 382, 44, "已经开着四份文本工具了，先关掉一份再开。");
    message.align(FL_ALIGN_INSIDE | FL_ALIGN_LEFT | FL_ALIGN_WRAP);
    Fl_Button ok(175, 84, 80, 30, "知道了");
    ok.callback([](Fl_Widget* button, void*) { button->window()->hide(); });
    window.end();
    window.set_modal();
    window.show();

    // 最多等 10 秒：有人点就早关，没人点也不会把这一份挂成又一个进程。
    // 用时间戳算，不用次数算：界面上有风吹草动时 Fl::wait 会提前返回，按次数算就等不满。
    Fl_Timestamp started = Fl::now();
    while (window.shown() && Fl::seconds_since(started) < 10.0) Fl::wait(0.1);
    window.hide();
}

}  // namespace

int main(int argc, char** argv) {
    // 外观先定：控件走 gtk 风格（勾选框、单选圈用界面库自己那一套），
    // 几种立体框换成扁平外观，底色按用户选的那一套上。
    // 这些必须在建任何控件之前设，之后设只对新控件生效。
    Fl::scheme("gtk+");
    texttool::ui::install_flat_look();
    texttool::ui::apply_appearance(texttool::ui::saved_appearance());

    // 缩放要在建任何控件之前设：之后就只对新窗口生效。
    Fl::screen_scale(0, detect_ui_scale());

    // 程序所在的目录要算出来给两处用：自带字体按它找，配置文件也按它找，与命令行同一个找法。
    const std::string program_path = argc > 0 && argv[0] != nullptr ? argv[0] : "";

    // 字体要在建任何控件之前定下来：控件建好以后只对新建的控件生效。
    const std::string font_family = texttool::ui::install_cjk_font(program_path);

    // 份数限制：这一份先占位置，占不上（已经有四份开着）就提示一句退出。
    // 排在这里，是因为拒绝时那句话要用上面刚装好的界面字体显示中文；排在建任何窗口之前，
    // 是因为第五份不该先闪一下界面再消失。
    texttool::ui::RunningInstance instance;
    int running = 0;
    if (!texttool::ui::claim_running_instance(&instance, &running)) {
        std::fprintf(stderr, "[界面] 已经开着 %d 份，最多 %d 份，这一份不开（退出码 %d）\n", running,
                     texttool::ui::kMaxRunningInstances, kTooManyInstancesExitCode);
        show_instance_limit_notice();
        return kTooManyInstancesExitCode;
    }

    std::vector<texttool::LintRule> rules;
    std::string rule_error;
    try {
        // 先按程序所在目录找规则表，找不到才退回编译时那条路径。
        // 打包发出去的机器上没有源码树，只有前者可能命中。
        const auto found = texttool::find_rules_file(texttool::program_dir(program_path.c_str()));
        rules = texttool::load_rules(found ? found->string() : std::string(TEXTTOOL_RULES_JSON));
    } catch (const std::exception& error) {
        rule_error = error.what();
    }

    const StartSize start = detect_start_size();

    // 产品名只写这一处：窗口标题栏与页签条右端的面包屑都用它。
    constexpr const char* kProductName = "TwilightRain 的文本工具";

    MainWindow window(120, 90, start.w, start.h, kProductName);
    window.color(texttool::ui::kPageBg);
    window.labelcolor(texttool::ui::kInk);
    window.begin();

    AppContext app;
    app.window = &window;
    app.rules = &rules;
    app.rule_error = rule_error;
    app.font_family = font_family;
    app.program_path = program_path;

    TabBar bar(kChromeInset, kTabBarTop, start.w - 2 * kChromeInset, kTabBarHeight);
    app.tabs = &bar;
    bar.set_product_name(kProductName);
    bar.set_title_window(&window);

    // 各页共用一个页面区，各页自己往里排。建一页就在这里登记一次：
    // 页签条、版式检查与自检用同一份名单。
    int page_x = 0, page_y = 0, page_w = 0, page_h = 0;
    bar.page_area(page_x, page_y, page_w, page_h, window.content_bottom());

    std::vector<texttool::ui::LayoutCheckPage> pages;
    auto build_page = [&](texttool::ui::Page* (*builder)(texttool::ui::AppContext&, int, int, int,
                                                       int),
                          const std::string& title) {
        texttool::ui::Page* page = builder(app, page_x, page_y, page_w, page_h);
        pages.push_back({title, page});
        return page;
    };

    // 顶端三项：处理、关于各是一页，高级是一个下拉。
    bar.add_top_page("处理", build_page(texttool::ui::build_process_page, "处理"));
    bar.add_top_page("关于", build_page(texttool::ui::build_about_page, "关于"));

    // 高级下拉五项，次序就是页的扁平序号里的 3 到 7（见文件头那张表）。
    const int advanced = bar.add_menu_tab("高级");
    auto add_advanced = [&](const char* title,
                            texttool::ui::Page* (*builder)(texttool::ui::AppContext&, int, int,
                                                          int, int)) {
        bar.add_menu_item(advanced, title, build_page(builder, title));
    };
    add_advanced("接回断行", texttool::ui::build_merge_page);
    add_advanced("合并多份", texttool::ui::build_join_page);
    add_advanced("换标点", texttool::ui::build_replace_page);
    add_advanced("剧本排版", texttool::ui::build_vn_page);
    add_advanced("文字体检", texttool::ui::build_lint_page);

    // 状态条：窗口最底下这一条浅灰，灰带顶到窗口左右两边，上面不画分隔线。
    auto* status = texttool::ui::make_status_bar(0, window.content_bottom(), start.w,
                                                 texttool::ui::kStatusHeight);
    texttool::ui::attach_status_bar(status);
    // 规则表读不到是启动时就要说的事，写在状态条上；读得到时状态条那句话由
    // 第一页露出来时自己写（见 Page::on_shown）。
    if (!rule_error.empty()) texttool::ui::set_status("检查项读不出来：" + rule_error);

    // 只给截图与冒烟测试用：环境变量 TEXTTOOL_START_TAB 是页的扁平序号（1 处理、2 关于、
    // 3 到 7 是高级下拉里那五项），启动时直接翻到那一页。不用命令行参数，
    // 是因为 FLTK 自己会去解析命令行，它不认识的写法会打一串用法提示出来。
    if (const char* tab = std::getenv("TEXTTOOL_START_TAB")) {
        const int index = std::atoi(tab);
        if (index >= 1 && index <= bar.page_count()) bar.value(index - 1);
    }

    window.end();
    window.callback(on_window_close);

    // chrome 交给主窗口摆一遍：之后尺寸变化走的也是同一条路。
    window.tabs = &bar;
    window.status = status;
    window.layout_chrome();

    // 窗口图标：任务栏与窗口左上角那个小图标，用程序自带的那个。
    // 传图要给 Fl_RGB_Image* 那一支重载：同一族里还有一个收 void* 的老口子，走错了图标就空着。
    if (Fl_Image* icon = texttool::ui::bundled_image(program_path, "icon.png"))
        window.icon(dynamic_cast<const Fl_RGB_Image*>(icon));

    // 只给下限，上限交给屏幕：拉不大、缩不小、最大化按钮用不了，都是因为上下限被锁成了同一个数。
    window.size_range(start.min_w, start.min_h, 0, 0);

    window.show(argc, argv);
    g_window = &window;

    // 开窗这一步会把当前 scheme 的框表重铺一遍（见 reinstall_flat_boxtypes 的说明），
    // 十种框得在开完窗以后再指回扁平那一套，否则输入框与列表框还是立体框。
    texttool::ui::reinstall_flat_boxtypes();

    // 底色再上一次：第一次开窗时界面库会自己取一遍颜色，选中色被盖回它自带的深蓝，
    // 勾选框里的勾、列表选中行、输入框选中文字都跟着变。开窗之后再上一次，
    // 第一次打开就是对的，不必等用户切一次底色。
    texttool::ui::apply_appearance(texttool::ui::appearance());

    // 自检模式见上面 run_self_check 的说明。等 0.2 秒是为了让第一帧真的画出来，
    // 与用户打开界面以后喘一口气再点按钮是一个样子。
    if (const char* doc = std::getenv("TEXTTOOL_SELF_CHECK"); doc != nullptr && *doc != '\0') {
        auto* task = new SelfCheck;
        task->app = &app;
        task->doc = doc;
        if (const char* page = std::getenv("TEXTTOOL_SELF_CHECK_PAGE"); page != nullptr) {
            const int index = std::atoi(page);
            task->step = index >= 1 && index <= 4 ? index : 0;
        }
        Fl::add_timeout(0.2, run_self_check, task);
    }

    // 版式检查见 ui/layout_check.h：环境变量开着才排一次，跑完自己把窗口藏起来。
    texttool::ui::start_layout_check_if_requested(&window, &bar, pages, &g_exit_code);

    const int code = Fl::run();
    // 自己那一份记录跟着走：界面关掉，临时目录里不再占位置。被强杀的清不掉，下一次启动
    // 按进程号探活，人已经不在的记录就删了。
    texttool::ui::release_running_instance(&instance);
    return g_exit_code != 0 ? g_exit_code : code;
}
