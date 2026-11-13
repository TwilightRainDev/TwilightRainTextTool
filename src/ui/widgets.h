// 六个页面共用的控件与工具。
//
// 版式按 tools/compare_layout.py 的比对截图来：标签一列对齐、控件一列对齐、按钮在左下角。
// 这里只放两个以上页面都要用的东西：文字与输入框的小工厂、状态条、数字框（自己画的）、
// 页面基类（收拖放）、逐份处理的跑法。某一页自己的控件留在那一页的文件里。
#pragma once

#include <FL/Fl_Box.H>
#include <FL/Fl_Button.H>
#include <FL/Fl_Check_Button.H>
#include <FL/Fl_Choice.H>
#include <FL/Fl_Group.H>
#include <FL/Fl_Image.H>
#include <FL/Fl_Input.H>
#include <FL/Fl_Round_Button.H>
#include <FL/Fl_Scrollbar.H>
#include <FL/Fl_Widget.H>
#include <FL/fl_draw.H>

#include <functional>

#include "engine/encoding.h"
#include <string>
#include <vector>

#include "ui/theme.h"

namespace texttool::ui {

// 窗口与各页共用的尺寸。
//
// 窗口开出来是 kWindowWidth x kWindowHeight，缩不到 kWindowMinWidth x kWindowMinHeight 以下，
// 上限由屏幕与窗口管理器定（最大化按钮要用这条路）。各页不再假定窗口是某一个固定尺寸：
// 摆子控件的那一层按当时拿到的宽度与高度算（见 Page::layout）。
inline constexpr int kWindowWidth = 1000;
inline constexpr int kWindowHeight = 620;
inline constexpr int kWindowMinWidth = 900;
inline constexpr int kWindowMinHeight = 560;
inline constexpr int kStatusHeight = 30;
inline constexpr int kStatusTextLeft = 12;  // 状态条上的字离窗口左边多少
inline constexpr int kLabelWidth = 136;  // 标签这一列的宽度（最长的标签到字段列还空 13 像素）
inline constexpr int kLeft = 30;
inline constexpr int kFieldX = kLeft + kLabelWidth;

// 页面内部的排版基准：页面的左上角带一点窗内边距（与页签条左边对齐），
// 所以页内排布要从下面这两个偏移算起，而不是直接用上面那几个窗口级数字。
inline constexpr int kPagePadLeft = kLeft - 12;                         // 标签列相对页面左上角
inline constexpr int kPageFieldLeft = kPagePadLeft + kLabelWidth;       // 字段列
inline constexpr int kPagePadRight = kLeft - 12;                        // 右边界离页面右边多少
// 标签、输入框、按钮摆在同一行时都用这个高度，行与行之间按各自的间距排。
// 名字不叫 kRowHeight：几页里已经有同名的局部量（列表行高），再起一个会撞上。
// 行高 32 加行距 10，一行一行往下就是 42 的间距。
inline constexpr int kFormRowHeight = 32;

// 界面自己的小配置：一行一项，写成「键=值」。
// Linux 放在 ~/.config/texttool/ui.conf，Windows 放在 %APPDATA%\texttool\ui.conf。
// 记两项：上次浏览的目录、上次选的底色。读不到、写不进去都不影响使用。
std::string ui_config_get(const std::string& key);
void ui_config_set(const std::string& key, const std::string& value);

// 扁平外观：把界面库的几种立体框换成一样的样子：白底（深色下是深灰底）的圆角矩形，
// 沿边一道一像素的边框（颜色取主题里的 kBorder），圆角与按钮同一档。输入框、下拉框、
// 列表框、报告区、菜单都跟着变，深色底色下才看得见轮廓；禁用时框也还在。
// 建窗与建控件之前调一次（见 app.cpp）；开完窗还要再调一次 reinstall_flat_boxtypes，
// 原因见那一条。
void install_flat_look();

// 只把那十种框换成扁平画法，别的什么都不动。
//
// 为什么开窗之后还要再调一次：程序用 Fl_Window::show(argc, argv) 开窗，界面库第一次开窗时
// 会把当前 scheme 的框表整张重铺一遍（FLTK 1.4.5 的 Fl_arg.cxx 里那句“Fl::scheme(Fl::scheme())”），
// 那一句把十种框重新指回 scheme 自带的画法。开窗之后不补这一次，输入框与列表框就是圆角、
// 两层亮边的立体框，而自己画的按钮、下拉框、数字框是 1 像素细框，同一页上两套边框。
void reinstall_flat_boxtypes();

// 最近一次操作是不是键盘：按下鼠标、拖动鼠标就变成否，按键就变成是。
// 键盘焦点框只在是的时候画：鼠标点出来的焦点框是多余的，还会盖住按钮上的字。
bool keyboard_input();

// 普通按钮：白底、一道细框、圆角，悬停与按下时底色深一档，禁用时灰底灰字但框还在。
// 页面上的按钮一律用它，按下与悬停的变化只在这里写一次。
class FlatButton : public Fl_Button {
public:
    FlatButton(int x, int y, int w, int h, const char* label = nullptr);
    void draw() override;
    int handle(int event) override;
};

FlatButton* make_button(int x, int y, int w, int h, const std::string& label);

// 下拉框：白底、一像素边框、圆角、右边一个细箭头，没有立体边，也没有分隔线。
// 点开以后弹出的那一列还是界面库自己的，跟着底色走。
class FlatChoice : public Fl_Choice {
public:
    FlatChoice(int x, int y, int w, int h, const char* label = nullptr);
    void draw() override;
};

// 下拉框里的一项。取值只有定死的几个时用它，手打容易拼出引擎不认的写法。
FlatChoice* make_choice(int x, int y, int w, int h, const std::vector<std::string>& items,
                        int selected = 0);

// 按钮、数字框、输入框、下拉框的圆角半径。直角方块看起来与标签分不开，圆角只把四个角磨掉一点。
inline constexpr int kButtonRadius = 6;

// 一像素圆角矩形的轮廓与实心块。颜色由调用方给：边框、圆角轮廓、实心底都用得到同一笔。
//
// 圆角是逐行收进的，不是一段圆弧：第 dy 行（从顶边数起）左右各收进 round_inset(dy) 像素，
// 相邻两行只差一像素——半径 6 时从上往下是 5、4、3、2、1、0，再往下就是 0；靠底边的几行
// 倒着来，上下对称。四个角上每一行都恰有一个像素，既不跳两格（缺口）也不走平台（碎步）。
// 半径超过控件的一半时自动收到一半，宽度或高度只有几像素的控件也不会画歪。
void stroke_round_rect(int x, int y, int w, int h, int radius, Fl_Color color);
void fill_round_rect(int x, int y, int w, int h, int radius, Fl_Color color);

// 勾选框：白底一像素边框的小方块；勾上以后是一块实心强调色的方块，里面一道白勾。
//
// 不用界面库自带的那个：它勾上以后只在白框里画一道细勾，一眼看不出勾了没有。
// 方块的边长、方块到文字的间隔都是固定的，所以控件该占多宽由 choice_width() 量出来。
class FlatCheck : public Fl_Check_Button {
public:
    FlatCheck(int x, int y, int w, int h, const char* label = nullptr);
    void draw() override;
};

// 单选圈：白底一像素圆环；选中的是一块实心强调色的圆盘，中间留一个白色的孔。
// 同一组（同一个父控件里、类型都是 FL_RADIO_BUTTON）的几项仍然互斥，那一段是界面库做的。
class FlatRadio : public Fl_Round_Button {
public:
    FlatRadio(int x, int y, int w, int h, const char* label = nullptr);
    void draw() override;
};

// 勾选框或单选在给定文案下该占多宽：方块（圆盘）边长 + 间隔 + 文字宽度 + 两像素余量。
// 页面按它算同一行里下一个控件的位置，不必再按字数估；labelsize 要与控件的字号一致。
int choice_width(const std::string& label, int labelsize = 15);

// 带滚动条的竖列容器。界面库的滚动区自带滑块上的斜纹与亮边，与这一套不合。
//
// 用法与 Fl_Scroll 一样：先 begin()，把子控件按绝对坐标摆进去（顶排先写当前可见位置），
// 再 end()；容器自己按子控件的总高算滚动范围。子控件摆好以后改过尺寸或行数得再调
// refresh_layout()。
class ScrollList : public Fl_Group {
public:
    ScrollList(int x, int y, int w, int h);

    void begin() { content_->begin(); }
    void end();

    // 重新量一遍子控件的总高，把滑块的范围与位置对齐。
    void refresh_layout();

    void draw() override;
    int handle(int event) override;

private:
    Fl_Group* content_ = nullptr;
    Fl_Scrollbar* bar_ = nullptr;
    int content_top_ = 0;     // 滚动量为 0 时子控件区顶边的位置
    int content_height_ = 0;  // 子控件加起来有多高
    int offset_ = 0;          // 现在滚到哪儿了

    void scroll_to(int offset);
    int visible_height() const;
    static void on_bar(Fl_Widget* bar, void* data);  // 滑块被拖动、被点的时候跟着走
};

// 选文件、选文件夹的那一扇窗。自己搭：界面库自带的那扇把栏目与按钮都写成英文，
// 还带一块预览区与「显示隐藏文件」的勾，与这一套界面不是一种东西。
//
// 三处都用它：页面上的「选文件…」「选文件夹…」、关于页的备份与恢复。
struct PickerRequest {
    std::string title = "选择文件";
    bool directory_only = false;      // 只选文件夹
    std::string start_dir;            // 从哪个文件夹开始；空表示上一回用过的那个
    std::string suggested_name;       // 保存模式预填的文件名；空表示选一份现成的
    std::vector<std::string> extensions;  // 只列这些后缀（小写，不带点）；空表示全列
};

// 选好的完整路径；取消返回空串。选好以后上一回用过的目录跟着记下。
std::string pick_path(const PickerRequest& request);

// 程序旁边的那份图片（程序图标、作者头像）。找不到就是空图，界面上不画。
// 先找程序所在目录，再找它下面的 images 与 assets/images。
Fl_Image* bundled_image(const std::string& program_path, const std::string& file_name);

// FLTK 的 label(const char*) 只记指针、不复制字符串；文案是动态拼的就必须走 copy_label，
// 否则屏幕上只剩乱码。这两个工厂都按这个规矩来。
Fl_Box* make_text(int x, int y, int w, int h, const std::string& text, int size = 15,
                  Fl_Color color = kInk);
Fl_Input* make_input(int x, int y, int w, int h, const std::string& value = "");

// 「标签 + 输入框 + 浏览按钮」这一行，排布自己管。
//
// 页面在 layout() 里建好它、把整行的位置与宽度交给它（resize）；宽度一变，里面的按钮与输入框
// 自己重算，页面里不再算一遍按钮宽。
// x 是标签左边界，y 是行顶；w 是整行（含标签那一列）的宽度；行高固定为 kFormRowHeight。
// 输入框从标签右边界起，到按钮左边界为止；按钮贴着右边界往里排。
//
// with_file_button 对应「选文件…」，with_folder_button 对应「选文件夹…」：
// 两个都给就是两个按钮，只给文件夹那个就是「选文件夹…」一个按钮。
// 选中之后把路径填进输入框，并在状态条报一句；对话框的起始目录跟着输入框里现有的路径走，
// 选过的目录记在界面配置文件里。
class SourceRow : public Fl_Group {
public:
    SourceRow(int x, int y, int w, const std::string& label, bool with_file_button,
              bool with_folder_button);

    // 宽度变了就重算：按钮宽固定，输入框吃剩下的宽度。
    void resize(int x, int y, int w, int h) override;

    // 这一行填的路径。外部读写都走它。
    Fl_Input* input() { return input_; }

private:
    Fl_Box* label_ = nullptr;
    Fl_Input* input_ = nullptr;
    FlatButton* file_button_ = nullptr;    // 「选文件…」；不要这一支就是空
    FlatButton* folder_button_ = nullptr;  // 「选文件夹…」；不要这一支就是空

    void layout_children();
    void pick(bool directory);  // 弹出选文件或选文件夹，选好了填进输入框

    static void on_pick(Fl_Widget* sender, void* data);
};

// 「标签 + 一个控件」这一行，排布自己管。
//
// 标签占 kLabelWidth，控件由调用方建好后传进来（通常是输入框或下拉框），吃剩下的宽度。
// x 是标签左边界，y 是行顶，w 是整行（含标签那一列）的宽度；行高固定为 kFormRowHeight。
// 控件本身不归这一行管：什么时候建、什么时候删由调用方定，这一行只摆位置。
class FieldRow : public Fl_Group {
public:
    FieldRow(int x, int y, int w, const std::string& label, Fl_Widget* field);

    // 宽度变了就重算：标签宽固定，控件吃剩下的宽度。
    void resize(int x, int y, int w, int h) override;

    Fl_Widget* field() { return field_; }

private:
    Fl_Box* label_ = nullptr;
    Fl_Widget* field_ = nullptr;

    void layout_children();
};

// 状态条：窗口底部那一条，由主窗口建好后挂上。
// 灰带顶到窗口左右两边，上面不画分隔线，字从左边 kStatusTextLeft 起。
Fl_Box* make_status_bar(int x, int y, int w, int h);
void attach_status_bar(Fl_Box* bar);
void set_status(const std::string& text);
// 状态条现在写的那一句。自检模式把它打到标准输出，好与界面上看到的一致。
std::string status_text();
// 在状态条现有那一句后面补一句，用分号隔开。状态条还没挂上时什么也不做。
void append_status(const std::string& note);

// 数字框。样子照桌面版：一个圆角外框内分三格，左边是数字，右边两个按钮左右并排，
// 按钮里画细折线箭头（左加一、右减一）。界面库自带的那个是上下叠放、实心三角，
// 与桌面版不一样，所以自己画。
//
// 宽度按「数字区 + 两个按钮」给：阈值那处 122 宽（数字 46），段落字数那处 110 宽（数字 42）。
class NumberBox : public Fl_Group {
public:
    NumberBox(int x, int y, int w, int h, int minimum, int maximum, int value, int input_width);

    int value() const;
    void value(int next);
    void range(int minimum, int maximum);

    Fl_Input* input() { return input_; }

    void draw() override;
    int handle(int event) override;

private:
    Fl_Input* input_ = nullptr;
    int minimum_ = 0;
    int maximum_ = 0;
    int input_width_ = 0;
    int button_width_ = 0;

    void step(int delta);
    void commit();  // 把输入框里的数读回来，越界就夹到范围内
};

// 编码名在界面上的写法。引擎给的 encoding_name 是报告与 JSON 里用的标识（全小写、带连字符），
// 界面上写标准专名：UTF-8、UTF-8（带 BOM）、UTF-16 LE、UTF-16 BE、GBK。
std::string encoding_label(TextEncoding encoding);

// 「浏览」对话框的起始目录：上次用过的那个。存在界面配置文件里（见 ui_config_get），
// 读不到或写不进去都不影响使用。记不存在的目录等于没记。
std::string last_browse_directory();
void remember_browse_directory(const std::string& directory);

// 一份稿的原始路径全部展开成稿件列表：目录递归收 .txt 与 .md，按路径排序；
// 不是目录就原样收。拖进来一个文件夹也能直接开工，就是这个函数在做。
// 目录展开在引擎里（engine/io.h 的 scan_documents），与命令行走目录是同一份收法。
// unreadable 非空时，读不了的目录路径收进去，调用方写在状态条上。
std::vector<std::string> expand_dropped_paths(const std::string& dropped,
                                             std::vector<std::string>* unreadable = nullptr);

// 取消标记。放在页面里（页面是长命对象），取消按钮的回调把 requested 置上，
// 逐份处理的循环每份开始前看一眼。
struct CancelState {
    bool requested = false;
};

// 逐份处理的跑法：每份之间让界面喘一口气（处理挂起的事件），状态条写「正在<动词> 第 n / 共 m 份：名字」。
// 单份处理期间界面不刷新：引擎是单线程的，一边算一边刷会把界面和引擎搅在一起。
// task 返回输出文件路径；抛异常即这一份失败，不影响后面的。
struct BatchOutcome {
    std::vector<std::string> outputs;
    std::vector<std::string> failures;
    bool cancelled = false;
};

BatchOutcome run_files(const std::vector<std::string>& files,
                       const std::function<std::string(const std::string&)>& task,
                       CancelState& cancel, const std::string& verb);

// 页面基类：白底、无框，接收拖进来的稿件，并且自己按当时的尺寸排子控件。
//
// 子类把收到稿件之后要做的事写在 on_files 里（目录在这里已经展开成文件了），
// 把子控件怎么摆写在 layout 里。
class Page : public Fl_Group {
public:
    Page(int x, int y, int w, int h);
    int handle(int event) override;

    // 位置或尺寸变了：先改自己的（不走界面库按比例搬子控件那一套），再重排一次。
    // 主窗口在窗口尺寸变化时逐页调它，一页只调一次。
    void resize(int x, int y, int w, int h) override;

    // 按现在的 x()、y()、w()、h() 重排一遍：调 layout，然后重画。
    // 子类构造完自己的控件以后要调一次；之后窗口尺寸变了由 resize 自动再调。
    void reflow();

    // 这一页被翻到前面了（页签切换、或者启动时露出来的那一页）。
    // 子类在这里把状态条改成描述这一页现状的那一句：状态条只有一份文字，
    // 上一步在别的页上写的那些话不会自己消失，翻过来不重写就留着别页的话。
    virtual void on_shown() {}

protected:
    // 子类在这里按自己当时的 x()、y()、w()、h() 摆直接子控件。
    // 一个子控件都不摆的时候什么也不用做（默认就是空的），页面照旧用构造时给的位置。
    virtual void layout() {}
    virtual void on_files(const std::vector<std::string>& files) { (void)files; }
};

}  // namespace texttool::ui
