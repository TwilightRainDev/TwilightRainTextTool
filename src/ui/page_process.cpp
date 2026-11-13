// 处理页：用户平时只盯这一页。
//
// 这一页管什么：挑一份稿子或一个文件夹、按清单上的勾选把几件事串成一条链跑完、把结果写成文件、
// 按「先看效果」在右边展开预览面板看结果开头（再按一次收起）、在面板里按「保存」把完整结果
// 存到用户挑的位置、把检查报告攒起来给报告面板看、按「直接改原文件」与
// 「改之前先留一份备份」决定写到哪儿。勾选到链的对应是：合并多份先把几份接成一个临时文件，
// 再按接回断裂的句子、换稿子的标点、排版视觉小说的次序往下串；检查不进链，在产物上另跑一遍。
//
// 这一页不管什么：各步参数本身怎么取值（接回断行、换标点、排版视觉小说三页各管自己那几格，
// 这一页只照接口取走）、处理算法、编码探测与写盘的细节。挑哪几份稿子、按什么次序接、
// 中间产物叫什么名字都在引擎里定。页签与面包屑由主窗口负责，这一页从「要处理的稿子：」开始。
#include <FL/Fl.H>
#include <FL/Fl_Box.H>
#include <FL/Fl_Button.H>
#include <FL/Fl_Group.H>
#include <FL/Fl_Input.H>
#include <FL/Fl_Text_Buffer.H>
#include <FL/Fl_Text_Display.H>
#include <FL/Fl_Window.H>
#include <FL/fl_draw.H>

#include <algorithm>
#include <cstddef>
#include <exception>
#include <filesystem>
#include <stdexcept>
#include <string>
#include <string_view>
#include <system_error>
#include <utility>
#include <vector>

#include "engine/chain.h"
#include "engine/encoding.h"
#include "engine/io.h"
#include "engine/lint_runner.h"  // 检查参数的取值类型，页面之间交接它
#include "engine/processing.h"
#include "ui/pages.h"
#include "ui/primary_button.h"
#include "ui/theme.h"
#include "ui/widgets.h"

#if defined(_WIN32)
#include <process.h>
#else
#include <unistd.h>
#endif

namespace texttool::ui {
namespace {

// 预览只读产物开头这么多字。整份结果放进面板，稿子一大就白占一份内存，而面板本来也看不下。
constexpr std::size_t kPreviewChars = 20000;

// 版式。纵向位置一律在 layout() 里从页面顶边一行行加出来，横向从左右边界、标签列与清单缩进算，
// 这里只写间隔与控件本身的尺寸，不写绝对坐标：页面拿到多大就按多大排。
constexpr int kTopGap = 12;            // 第一行离页面顶边多远
constexpr int kRowGap = 8;             // 源文件行与格式行之间
constexpr int kContentGap = 10;        // 格式行与下面清单之间
constexpr int kListIndent = 82;        // 清单里的勾选框离页面左边多远，比标签列窄一档
constexpr int kListPitch = 36;         // 清单上一行占多高
constexpr int kBeforeButtonsGap = 22;  // 清单最后一行到按钮那一排
constexpr int kInlineGap = 12;         // 同一行里前后两个控件之间
constexpr int kButtonHeight = 32;
constexpr int kButtonGapRun = 10;      // 「开始处理」与「先看一眼」之间
constexpr int kButtonGap = 8;          // 其余按钮之间
constexpr int kFormatWidth = 122;      // 「处理什么格式的文件」那一格
constexpr int kRunButtonWidth = 96;
constexpr int kPeekButtonWidth = 96;
constexpr int kStopButtonWidth = 76;
constexpr int kMoreButtonWidth = 96;
constexpr int kReportButtonWidth = 100;
constexpr int kPanelShare = 44;     // 预览面板占内容宽度的百分之多少
constexpr int kPanelMinWidth = 300; // 面板最窄到这里，再窄就读不了
constexpr int kPanelGap = 24;       // 左列与面板之间至少留这么宽
constexpr int kPagePadBottom = 18;  // 面板底边离页面底边多远
constexpr int kPanelPad = 14;       // 面板里的控件离面板边多远
constexpr int kPanelTitleHeight = 22;
constexpr int kPanelNoticeHeight = 20;
constexpr int kPanelTitleGap = 8;
constexpr int kPanelNoticeGap = 6;
constexpr int kPanelFooterGap = 6;   // 正文区到「保存」那一行
constexpr int kPanelFooterHeight = kFormRowHeight;

// 界面文案。建控件与量宽度用的是同一句话的同一份出处，两处不会对不上。
constexpr const char* kMergeLabel = "接回断裂的句子";
constexpr const char* kJoinLabel = "合并多份稿子";
constexpr const char* kReplaceLabel = "换稿子的标点";
constexpr const char* kLintLabel = "检查是不是机器写的";
constexpr const char* kVnLabel = "排版视觉小说";
constexpr const char* kOverwriteLabel = "直接改原文件（危险）";
constexpr const char* kBackupLabel = "改之前先留一份备份";
constexpr const char* kFormatLabel = "处理什么格式的文件：";
constexpr const char* kFormatDefault = "*.txt";

// 合并多份只在自检时换名字：自检要把结果写到固定的一个名字上，界面那一页那一格用户改过也不管。
constexpr const char* kSelfCheckJoinName = "joined.txt";

// 面板里那几句。第一句是还没跑过时写的，第二句是产物比面板能放的长时写在标题下面的。
constexpr const char* kPanelHint =
    "挑一份稿子，然后按「开始处理」。\n处理完的结果先显示在这里，满意再另存。";
constexpr const char* kTruncatedHint = "只显示开头，完整结果在输出文件里";
constexpr const char* kEmptyResultHint = "（结果里一个字都没有）";
constexpr const char* kNoReportHint =
    "还没有查过。勾上「检查是不是机器写的」，再按「开始处理」，查完的报告按文件排在这里。";

// 清单上的三件事与合并多份这一轮不能一起跑：合并的输入是几份稿子接起来的那一份，
// 那三项各自都要按「一份稿子」来读，串在一起分不清谁是谁。合并期间这三项置灰并清掉勾。
constexpr const char* kJoinNote =
    "合并多份与接回断裂的句子、换稿子的标点、排版视觉小说不能一起跑：这三项这一轮不勾";
constexpr const char* kNothingNote = "一件事都没勾：至少勾一项，按「开始处理」才有事可做";

// 一位进程号。临时文件带它，同一台机器上开着几份程序时互相不抢名字。
long current_process_id() {
#if defined(_WIN32)
    return static_cast<long>(_getpid());
#else
    return static_cast<long>(::getpid());
#endif
}

// 这一趟接出来的临时文件。中途抛出去、或者跑完了，都靠析构把它删掉：
// 留一份在盘上，下一次「合并多份」会把它当成一份稿子收进去。
class TempFile {
public:
    TempFile() = default;
    ~TempFile() { clear(); }

    TempFile(const TempFile&) = delete;
    TempFile& operator=(const TempFile&) = delete;

    void adopt(std::string path) {
        clear();
        path_ = std::move(path);
    }

    void clear() {
        if (path_.empty()) return;
        std::error_code ignored;
        std::filesystem::remove(path_, ignored);
        path_.clear();
    }

private:
    std::string path_;
};

// 一个路径所在的文件夹：给的是文件夹就是它自己，给的是文件就取上一层。
// 上一层取不出来时返回空串，调用方拿这个空串说一句「先把源文件夹填上」。
std::string folder_of(const std::string& path) {
    if (path.empty()) return {};
    std::error_code ignored;
    if (std::filesystem::is_directory(path, ignored)) return path;
    const std::string parent = std::filesystem::path(path).parent_path().string();
    return parent.empty() ? std::string(".") : parent;
}

// 「处理什么格式的文件」那一格写成 *.txt 这种通配。收文件夹里的稿子时只认后缀，
// 所以这里把通配换成后缀：*.txt 换成 .txt，逗号或空格隔开的写几段就收几种。
// 写 *、*.* 或者空着时按 .txt 与 .md 两种收，与把一个文件夹直接拖进来时收的那一批相同。
std::vector<std::string> extensions_from_pattern(const std::string& pattern) {
    std::vector<std::string> extensions;
    std::string token;

    const auto take_token = [&extensions, &token]() {
        const std::string value = token;
        token.clear();
        if (value.empty()) return;
        if (value == "*" || value == "*.*" || value == ".") {
            extensions.push_back(".txt");
            extensions.push_back(".md");
            return;
        }
        // 只有 *.后缀 这一种写法能变成后缀；别的写法（*abc*）没法落成后缀，按不写处理。
        if (value.size() > 2 && value[0] == '*' && value[1] == '.')
            extensions.push_back(value.substr(1));
    };

    for (const char ch : pattern) {
        if (ch == ',' || ch == ';' || ch == ' ' || ch == '\t') {
            take_token();
        } else {
            token.push_back(ch);
        }
    }
    take_token();

    if (extensions.empty()) {
        extensions.push_back(".txt");
        extensions.push_back(".md");
    }
    return extensions;
}

// 一句话在界面上占多宽。标签列装不下自己的文案时按它把那几行放宽，字不会被切掉。
int text_width(const std::string& text, int size) {
    fl_font(FL_HELVETICA, size);
    return static_cast<int>(fl_width(text.c_str()));
}

// 从一份文件里读开头最多 max_chars 个字给面板看。读够就停手，占的内存与耗时都只与
// max_chars 有关，与稿子多长无关。后面还有没有剩的由 truncated 交回。
void read_head(const std::string& path, std::size_t max_chars, std::string& text, bool& truncated) {
    text.clear();
    truncated = false;

    texttool::TextFileReader reader(path);
    std::size_t chars = 0;
    std::string_view chunk;
    while (chars < max_chars && reader.next(chunk)) {
        std::size_t used = 0;  // 这一块收到第几个字节
        while (used < chunk.size() && chars < max_chars) {
            const auto lead = static_cast<unsigned char>(chunk[used]);
            std::size_t width = 1;
            if ((lead & 0xE0) == 0xC0) width = 2;
            else if ((lead & 0xF0) == 0xE0) width = 3;
            else if ((lead & 0xF8) == 0xF0) width = 4;
            used += width;
            ++chars;
        }
        text.append(chunk.substr(0, std::min(used, chunk.size())));
    }
    if (chars >= max_chars) {
        std::string_view extra;
        truncated = reader.next(extra);
    }
}

// 一份文件有多少字：边读边数，不把整份稿装进内存。自检要的两个数用它。
std::size_t count_chars(const std::string& path) {
    texttool::TextFileReader reader(path);
    std::string_view chunk;
    while (reader.next(chunk)) {
    }
    return reader.text_chars();
}

// 逐份处理的失败记录每一行是「路径：原因」。状态条上单份只留文件名，多份留整条路径：
// 同一批里重名的稿子很常见，只留文件名分不清是哪一份。
std::string failure_line(const std::string& entry, bool many) {
    constexpr char kColon[] = "：";
    const std::size_t split = entry.find(kColon);
    if (split == std::string::npos) return "[失败] " + entry;
    const std::string path = entry.substr(0, split);
    const std::string reason = entry.substr(split + sizeof(kColon) - 1);
    return "[失败] " + (many ? path : texttool::file_name_of_any(path)) + kColon + reason;
}

// 预览面板：一块白底、一道细框、圆角的面板，标题与（要的时候）一句截断提示在上，只读正文在下。
// 面板一直显示，不随有没有结果改尺寸：窗口多大它就多大，位置由页面在 layout() 里给。
class PreviewPanel : public Fl_Group {
public:
    PreviewPanel(int x, int y, int w, int h) : Fl_Group(x, y, w, h) {
        box(FL_NO_BOX);
        color(kPageBg);
        begin();

        title_ = make_text(x, y, 80, kPanelTitleHeight, "预览", 15, kMuted);
        notice_ = make_text(x, y, 200, kPanelNoticeHeight, kTruncatedHint, 13, kMuted);

        buffer_ = new Fl_Text_Buffer();
        view_ = new Fl_Text_Display(x, y, 100, 100);
        view_->buffer(buffer_);
        view_->textsize(15);
        view_->color(kFieldBg);
        view_->textcolor(kInk);
        view_->cursor_color(kInk);
        view_->selection_color(kAccent);
        // 面板自己铺白底，正文区不画框：两块圆角框套在一起会显得脏。
        view_->box(FL_FLAT_BOX);
        view_->wrap_mode(Fl_Text_Display::WRAP_AT_BOUNDS, 0);
        buffer_->text(kPanelHint);

        // 「保存」在面板右下角：只有真的预览出一份结果以后才点得动。
        save_ = make_button(x, y, kSavePreviewWidth, kFormRowHeight, "保存");
        save_->labelsize(14);
        save_->deactivate();

        notice_->hide();
        end();

        layout_children();
    }

    // 换掉面板里的内容。truncated 为真时标题下面多一句「只显示开头」。
    // 「保存」按下去做什么由页面接：面板只管把按钮摆在右下角。
    Fl_Button* save_button() { return save_; }

    // 有一份完整结果可存时点亮；没有结果、或者算的时候出错，就灰着。
    void set_save_enabled(bool on) {
        if (save_ == nullptr) return;
        if (on) {
            save_->activate();
        } else {
            save_->deactivate();
        }
    }

    void set_text(const std::string& text, bool truncated) {
        buffer_->text(text.empty() ? kEmptyResultHint : text.c_str());
        view_->scroll(0, 0);
        notice_shown_ = truncated;
        layout_children();
        redraw();
    }

    void resize(int x, int y, int w, int h) override {
        // 不用界面库按比例搬子控件那一套：位置一律自己按新的版面算。
        Fl_Widget::resize(x, y, w, h);
        layout_children();
    }

    void draw() override {
        fill_round_rect(x(), y(), w(), h(), kButtonRadius, kFieldBg);
        stroke_round_rect(x(), y(), w(), h(), kButtonRadius, kBorder);
        Fl_Group::draw();  // box 是 FL_NO_BOX，这一句只把子控件画出来
    }

private:
    void layout_children() {
        const int inner_x = x() + kPanelPad;
        const int inner_w = std::max(0, w() - 2 * kPanelPad);

        int row = y() + kPanelPad;
        title_->resize(inner_x, row, inner_w, kPanelTitleHeight);
        row += kPanelTitleHeight + kPanelTitleGap;

        notice_->resize(inner_x, row, inner_w, kPanelNoticeHeight);
        if (notice_shown_) {
            notice_->show();
            row += kPanelNoticeHeight + kPanelNoticeGap;
        } else {
            notice_->hide();
        }

        const int footer_top = y() + h() - kPanelPad - kPanelFooterHeight;
        view_->resize(inner_x, row, inner_w,
                      std::max(0, footer_top - kPanelFooterGap - row));
        save_->resize(x() + w() - kPanelPad - kSavePreviewWidth, footer_top, kSavePreviewWidth,
                      kPanelFooterHeight);
    }

    static constexpr int kSavePreviewWidth = 76;

    Fl_Box* title_ = nullptr;
    Fl_Box* notice_ = nullptr;
    Fl_Text_Buffer* buffer_ = nullptr;
    Fl_Text_Display* view_ = nullptr;
    Fl_Button* save_ = nullptr;
    bool notice_shown_ = false;
};

// 清单上勾的那几项，读一次就是一份，后面的判断全看它，不再回头读控件。
struct RunSettings {
    bool merge = false;     // 接回断裂的句子
    bool join = false;      // 合并多份稿子
    bool replace = false;   // 换稿子的标点
    bool vn = false;        // 排版视觉小说
    bool lint = false;      // 检查是不是机器写的
    bool overwrite = false; // 直接改原文件
    bool backup = false;    // 改之前先留一份备份
};

// 一份稿这一趟要跑的东西。合并多份时 source 是接出来的临时文件，target 是源文件夹里那一份。
struct RunPlan {
    std::string source;
    std::string target;
    std::vector<texttool::ChainStep> steps;
    bool backup = false;
};

class ProcessPage : public Page {
public:
    ProcessPage(AppContext& app, int x, int y, int w, int h) : Page(x, y, w, h), app_(app) {
        box(FL_NO_BOX);
        color(kPageBg);
        begin();

        // 控件先按占位位置建好，实位一律在 layout() 里算：构造完调一次 reflow()，
        // 之后窗口尺寸一变，Page::resize 会再调一遍。
        source_row_ = new SourceRow(x, y, kLabelWidth, "要处理的稿子：", true, true);
        source_ = source_row_->input();

        format_label_ = make_text(x, y, kLabelWidth, kFormRowHeight, kFormatLabel);
        format_ = make_input(x, y, kFormatWidth, kFormRowHeight, kFormatDefault);
        format_->tooltip("收文件夹里的稿子时只收这几种；写成 *.txt 或 *.md，用逗号隔开写几段");

        list_label_ = make_text(x, y, kLabelWidth, kFormRowHeight, "处理：");

        merge_check_ = new FlatCheck(x, y, kLabelWidth, kFormRowHeight, kMergeLabel);
        merge_check_->labelsize(15);
        merge_check_->value(1);
        merge_check_->callback(on_option_changed, this);

        join_check_ = new FlatCheck(x, y, kLabelWidth, kFormRowHeight, kJoinLabel);
        join_check_->labelsize(15);
        join_check_->callback(on_option_changed, this);

        replace_check_ = new FlatCheck(x, y, kLabelWidth, kFormRowHeight, kReplaceLabel);
        replace_check_->labelsize(15);
        replace_check_->callback(on_option_changed, this);

        lint_check_ = new FlatCheck(x, y, choice_width(kLintLabel), kFormRowHeight, kLintLabel);
        lint_check_->labelsize(15);
        lint_check_->callback(on_option_changed, this);

        report_button_ = make_button(x, y, kReportButtonWidth, kFormRowHeight, "报告面板");
        report_button_->labelsize(15);
        report_button_->callback(on_show_report, this);

        more_button_ = make_button(x, y, kMoreButtonWidth, kFormRowHeight, "更多功能");
        more_button_->labelsize(15);
        more_button_->callback(on_show_more, this);

        // 这一行平时藏着，在「更多功能」里勾上排版视觉小说才出现在清单里。
        vn_check_ = new FlatCheck(x, y, kLabelWidth, kFormRowHeight, kVnLabel);
        vn_check_->labelsize(15);
        vn_check_->callback(on_option_changed, this);
        vn_check_->hide();

        overwrite_ = new FlatCheck(x, y, choice_width(kOverwriteLabel), kFormRowHeight,
                                   kOverwriteLabel);
        overwrite_->labelsize(15);
        overwrite_->labelcolor(kWarnInk);
        overwrite_->callback(on_overwrite_changed, this);

        backup_ = new FlatCheck(x, y, kLabelWidth, kFormRowHeight, kBackupLabel);
        backup_->labelsize(15);
        backup_->labelcolor(kMuted);
        backup_->deactivate();  // 不覆盖原文件时备份无从谈起

        run_ = new PrimaryButton(x, y, kRunButtonWidth, kButtonHeight, "开始处理");
        run_->labelsize(15);
        run_->callback(on_run, this);

        peek_ = make_button(x, y, kPeekButtonWidth, kButtonHeight, "先看效果");
        peek_->labelsize(15);
        peek_->callback(on_peek, this);

        cancel_button_ = make_button(x, y, kStopButtonWidth, kButtonHeight, "停止");
        cancel_button_->labelsize(15);
        cancel_button_->callback(on_stop, this);
        cancel_button_->deactivate();  // 没在跑的时候没什么可停的

        panel_ = new PreviewPanel(x, y, kLabelWidth, kFormRowHeight);
        panel_->hide();  // 默认收起：按「先看效果」才展开
        panel_->save_button()->callback(on_save_preview, this);

        end();

        reflow();  // 按页面现有尺寸摆一遍
        update_controls();
    }

    // 自检入口实际做事的这一半：把这份稿填进源文件框，按当前勾选（或指定的一项）真的跑、真的写盘。
    SelfCheckSummary run_file(const std::string& path, int single_step) {
        apply_single_step(path, single_step);
        // 自检没人看着，弹窗会把脚本挂住：确认问句一律跳过，按「继续」算。
        interactive_ = false;
        measure_ = true;
        run(false);
        measure_ = false;
        interactive_ = true;

        SelfCheckSummary summary;
        if (written_ > 0 && !stopped_ && !last_outputs_.empty()) summary.output = last_outputs_.back();
        summary.input_chars = last_input_chars_;
        summary.output_chars = last_output_chars_;
        return summary;
    }

protected:
    void layout() override {
        const int left = x() + kPagePadLeft;
        const int field_x = x() + kPageFieldLeft;
        const int right = x() + w() - kPagePadRight;
        const int content_width = std::max(0, right - left);

        int row = y() + kTopGap;

        // 源文件行与格式行横跨整页：下面才是左列加预览面板。
        source_row_->resize(left, row, content_width, kFormRowHeight);
        row += kFormRowHeight + kRowGap;

        // 「处理什么格式的文件」这一句比标签列宽，这一行的输入框跟着它往右让，字不会被切掉。
        const int format_label_width =
            std::max(kLabelWidth, text_width(kFormatLabel, 15) + kInlineGap);
        format_label_->resize(left, row, format_label_width, kFormRowHeight);
        format_->resize(left + format_label_width, row, kFormatWidth, kFormRowHeight);
        row += kFormRowHeight + kContentGap;

        // 预览面板展开时占右边一列，收起时左列用满整页。
        const int panel_width =
            std::min(std::max(kPanelMinWidth, content_width * kPanelShare / 100), content_width);
        const int panel_x = std::max(left, right - panel_width);
        const int panel_height = std::max(0, y() + h() - kPagePadBottom - row);
        if (preview_open_) {
            panel_->resize(panel_x, row, std::max(0, right - panel_x), panel_height);
            panel_->show();
        } else {
            panel_->hide();
        }

        // 左列的右边界：面板开着时与它留一道空隙，收起时一直用到页面右边界。
        const int list_right =
            preview_open_ ? std::max(0, panel_x - kPanelGap - left) : content_width;
        const int check_x = left + kListIndent;
        const int check_width = std::max(0, list_right - kListIndent);

        // 「处理：」只占清单左边那一小截：清单的勾选框比标签列窄一档，标签跟着收窄，
        // 压到勾选框上是版式检查会报的那一处重叠。
        list_label_->resize(left, row, std::max(0, kListIndent - kInlineGap), kFormRowHeight);
        merge_check_->resize(check_x, row, check_width, kFormRowHeight);
        row += kListPitch;

        join_check_->resize(check_x, row, check_width, kFormRowHeight);
        row += kListPitch;

        replace_check_->resize(check_x, row, check_width, kFormRowHeight);
        row += kListPitch;

        // 检查这一行右边并着一个按钮：先按文案量出勾选框的宽，按钮挨着它往右排。
        const int lint_width = choice_width(kLintLabel);
        lint_check_->resize(check_x, row, lint_width, kFormRowHeight);
        report_button_->resize(check_x + lint_width + kInlineGap, row, kReportButtonWidth,
                               kFormRowHeight);
        row += kListPitch;

        more_button_->resize(check_x, row, kMoreButtonWidth, kFormRowHeight);
        row += kListPitch;

        // 藏着的这一行也摆好位置：它在页面之内，只是不显示；显出来的时候正好把下面几行往下推。
        vn_check_->resize(check_x, row, check_width, kFormRowHeight);
        if (vn_row_shown_) row += kListPitch;

        const int overwrite_width = choice_width(kOverwriteLabel);
        overwrite_->resize(check_x, row, overwrite_width, kFormRowHeight);
        const int backup_x = check_x + overwrite_width + kInlineGap;
        backup_->resize(backup_x, row, std::max(0, list_right - (backup_x - left)), kFormRowHeight);
        row += kListPitch;
        row += kBeforeButtonsGap;

        run_->resize(field_x, row, kRunButtonWidth, kButtonHeight);
        const int peek_x = field_x + kRunButtonWidth + kButtonGapRun;
        peek_->resize(peek_x, row, kPeekButtonWidth, kButtonHeight);
        cancel_button_->resize(peek_x + kPeekButtonWidth + kButtonGap, row, kStopButtonWidth,
                               kButtonHeight);
    }

    // 翻到这一页时状态条说自己这一句。状态条只有一份文字，别页写上去的话不会自己消失。
    void on_shown() override {
        update_controls();
        refresh_status_note();
    }

    void on_files(const std::vector<std::string>& files) override {
        if (files.empty()) return;
        source_->value(files.front().c_str());
        dropped_ = files;
        update_controls();
        set_status("已拖进来 " + std::to_string(files.size()) + " 份稿子：上面这一格是第一份");
    }

private:
    // ---------------------------------------------------------------- 输入

    std::string input_path() const { return texttool::trim_whitespace(source_->value()); }

    std::string pattern() const {
        const std::string typed = texttool::trim_whitespace(format_->value());
        return typed.empty() ? std::string(kFormatDefault) : typed;
    }

    RunSettings read_settings() const {
        RunSettings settings;
        settings.join = join_check_->value() != 0;
        settings.merge = merge_check_->value() != 0;
        settings.replace = replace_check_->value() != 0;
        settings.vn = vn_row_shown_ && vn_check_->value() != 0;
        settings.lint = lint_check_->value() != 0;
        settings.overwrite = overwrite_->value() != 0;
        settings.backup = settings.overwrite && backup_->value() != 0;
        return settings;
    }

    // 这一批要处理哪些稿子。合并多份时整批就是源文件夹这一份。
    std::vector<std::string> batch_files(bool joining) const {
        std::vector<std::string> files;
        const std::string typed = input_path();
        if (typed.empty()) return files;

        if (joining) {
            const std::string folder = folder_of(typed);
            if (!folder.empty()) files.push_back(folder);
            return files;
        }

        std::error_code ignored;
        if (std::filesystem::is_directory(typed, ignored)) {
            // 框里是文件夹就按「处理什么格式的文件」那一格收里面的稿子，与命令行走目录同一个收法。
            const texttool::DirectoryScan scan =
                texttool::scan_documents(typed, extensions_from_pattern(pattern()));
            files = scan.files;
        } else {
            files.push_back(typed);
        }
        for (const std::string& file : dropped_) {
            if (std::find(files.begin(), files.end(), file) == files.end()) files.push_back(file);
        }
        return files;
    }

    // ------------------------------------------------------------ 参数与链

    // 勾上的那几项换成链上的步骤。次序定死：接回、换标点、排版。
    bool build_steps(const RunSettings& settings, std::vector<texttool::ChainStep>& steps,
                     std::string& error) const {
        if (settings.merge) {
            texttool::MergeOptions options;
            if (!merge_page_options(app_, options, error)) return false;
            texttool::ChainStep step;
            step.kind = texttool::ChainStepKind::MergeLines;
            step.merge = options;
            steps.push_back(step);
        }
        if (settings.replace) {
            texttool::ReplaceOptions options;
            if (!replace_page_options(app_, options, error)) return false;
            texttool::ChainStep step;
            step.kind = texttool::ChainStepKind::ReplacePunctuation;
            step.replace = options;
            steps.push_back(step);
        }
        if (settings.vn) {
            texttool::VnOptions options;
            if (!vn_page_options(app_, options, error)) return false;
            texttool::ChainStep step;
            step.kind = texttool::ChainStepKind::ReformatVn;
            step.vn = options;
            steps.push_back(step);
        }
        return true;
    }

    // 合并多份接出来的临时文件放哪儿：放系统临时目录，跑完就删。
    // 不放在源文件夹里，是因为那里多出一份带后缀的文件，下一次合并会把它当成稿子收进去。
    std::string join_temp_path(const std::string& folder) {
        std::error_code ignored;
        std::filesystem::path directory = std::filesystem::temp_directory_path(ignored);
        if (ignored || directory.empty()) directory = folder;

        ++temp_counter_;
        const std::string name = ".texttool-join-" + std::to_string(current_process_id()) + "-" +
                                 std::to_string(temp_counter_) + ".txt";
        return (directory / name).string();
    }

    // 把挑出来的几份按次序接进一个文件：一律 UTF-8 带 BOM，份与份之间补一个换行，
    // 与命令行合并多份写出来的字节相同。逐块读逐块写，不把整份稿装进内存。
    static void join_into(const std::string& target, const std::vector<std::string>& files) {
        texttool::TextWriter writer(target, texttool::TextEncoding::Utf8Bom);
        for (const std::string& file : files) {
            texttool::TextFileReader reader(file);
            bool any = false;
            bool ends_with_newline = false;
            std::string_view chunk;
            while (reader.next(chunk)) {
                writer.write(chunk);
                any = true;
                ends_with_newline = !chunk.empty() && chunk.back() == '\n';
            }
            // 本份末尾已经是换行就不再补；空稿子也单独占一行。
            if (!any || !ends_with_newline) writer.write("\n");
        }
        writer.commit();
    }

    // 一份稿这一趟的输入、目标与步骤。合并多份时先接文件，接出来的那一份当输入。
    bool make_plan(const std::string& entry, const RunSettings& settings,
                   const std::vector<texttool::ChainStep>& steps, TempFile& joined, RunPlan& plan,
                   std::string& error) {
        if (settings.join) {
            const std::string folder = folder_of(entry);
            if (folder.empty()) {
                error = "合并多份要先把源文件夹填在上面：按「选文件夹…」挑一个，或者挑文件夹里的一份稿子";
                return false;
            }
            const std::string name =
                join_name_override_.empty() ? join_page_output_name(app_) : join_name_override_;
            plan.target = texttool::combine_path(folder, name);

            const texttool::JoinSelection selection =
                texttool::select_join_files(folder, pattern(), plan.target);
            if (selection.directory_error) {
                error = "读不了这个文件夹：" + folder;
                return false;
            }
            if (selection.files.empty()) {
                error = "这个文件夹里没有对得上的稿子：" + folder;
                return false;
            }
            const std::string temporary = join_temp_path(folder);
            joined.adopt(temporary);
            join_into(temporary, selection.files);
            plan.source = temporary;
        } else {
            plan.source = entry;
            plan.target = settings.overwrite ? entry : texttool::processed_path(entry);
        }
        plan.steps = steps;
        // 接回断行按字节数算宽度时，宽度要按这份稿原来的编码量：GBK 稿里一个汉字 2 字节，
        // 换算成 UTF-8 再量就是 3 字节，断行位置会不同。命令行 merge 也是按读进来的编码算的。
        texttool::TextEncoding encoding = texttool::TextEncoding::Utf8;
        try {
            encoding = texttool::probe_file_encoding(plan.source);
        } catch (const std::exception& failure) {
            error = std::string("读不出来：") + failure.what();
            return false;
        }
        for (texttool::ChainStep& step : plan.steps) {
            if (step.kind == texttool::ChainStepKind::MergeLines)
                step.merge.source_encoding = encoding;
        }
        // 备份只对「写回原文件」有意义：合并多份写的是另一份新文件，原稿根本没动。
        plan.backup = settings.overwrite && settings.backup && !settings.join;
        return true;
    }

    // ---------------------------------------------------------------- 处理

    // 「开始处理」与「先看一眼」共用这一条：取参数、拼链、逐份跑。
    // preview 为真时只算第一份、不写盘，结果只放进右边的面板。
    void run(bool preview) {
        if (running_) return;

        // 上一轮按过「停止」以后这个标记不会自己清：每一轮开工前先松开它。
        cancel_state_.requested = false;

        const RunSettings settings = read_settings();
        std::vector<texttool::ChainStep> steps;
        std::string error;
        if (!build_steps(settings, steps, error)) {
            set_status(error);
            return;
        }
        // 合并多份自己就把几份接成一份产物，它算一件事；没有它、也没有步骤、又没勾检查时，
        // 这一趟确实没活可干。
        if (steps.empty() && !settings.lint && !settings.join) {
            set_status(kNothingNote);
            return;
        }
        if (preview && steps.empty() && !settings.join) {
            set_status("还没勾上要做的处理，先看不了效果");
            return;
        }

        // 直接改原文件又不留备份：原内容这一趟之后再也找不回来，动手前问一次。
        // 合并多份写的是文件夹里的另一份文件，原稿不动，这一问也就不问。
        if (!preview && settings.overwrite && !settings.backup && !settings.join && interactive_) {
            if (!confirm_overwrite()) {
                set_status("没有动手，原文件还是原来那一份");
                return;
            }
        }

        std::vector<std::string> files = batch_files(settings.join);
        if (files.empty()) {
            set_status("还没有选稿子：挑一份，或者把它拖进来");
            return;
        }
        if (preview) files.resize(1);  // 一次只看一份：多份挤在一个面板里看不出单份的效果

        reports_.clear();
        written_ = 0;
        stopped_ = false;
        last_outputs_.clear();
        last_input_chars_ = 0;
        last_output_chars_ = 0;

        running_ = true;
        update_controls();

        if (preview) {
            run_preview(files.front(), settings, steps);
            running_ = false;
            update_controls();
            return;
        }

        const bool many = files.size() > 1;
        // 只勾了检查：不写盘，报告攒着给报告面板。合并多份时另算，它自己要写出一份文件来。
        const bool lint_only = steps.empty() && !settings.join;

        // 逐份跑由 run_files 负责：每份之间让界面喘一口气，状态条写「第 n / 共 m 份」，
        // 「停止」才有机会点得到。处理期间界面不刷新：引擎是单线程的，一边算一边刷会把两件事搅在一起。
        const BatchOutcome outcome = run_files(
            files,
            [&](const std::string& entry) -> std::string {
                RunPlan plan;
                TempFile joined;
                std::string plan_error;
                if (!make_plan(entry, settings, steps, joined, plan, plan_error))
                    throw std::runtime_error(plan_error);

                // 没有要串的步骤时只有两条路：只合并多份，或者只检查。
                if (plan.steps.empty()) {
                    if (!settings.join) {
                        if (measure_) last_input_chars_ = count_chars(plan.source);
                        lint_one(plan.source);
                        return plan.source;
                    }
                    // 只合并多份：接出来的那一份就是产物，写到目标名字上。
                    // 临时文件在系统临时目录里，与目标常常不在一块盘上，所以按字节拷一份过去，
                    // 不指望能直接改名。
                    std::error_code copy_error;
                    std::filesystem::copy_file(plan.source, plan.target,
                                               std::filesystem::copy_options::overwrite_existing,
                                               copy_error);
                    if (copy_error) throw std::runtime_error("写入失败：" + plan.target);
                    ++written_;
                    if (measure_) {
                        last_input_chars_ = count_chars(plan.source);
                        last_output_chars_ = count_chars(plan.target);
                    }
                    if (settings.lint) lint_one(plan.target);
                    return plan.target;
                }

                texttool::ChainRequest request;
                request.source = plan.source;
                request.target = plan.target;
                request.steps = plan.steps;
                request.backup = plan.backup;
                // 停止只在两步之间看：正在写的那一步写完才收手，不会留下写了一半的结果。
                request.should_stop = [this]() { return cancel_state_.requested; };

                const texttool::ChainResult result = texttool::run_chain(request);
                if (result.stopped) {
                    stopped_ = true;
                    return plan.source;  // 这一份没写成，target 一个字没动
                }

                ++written_;
                if (measure_) {
                    last_input_chars_ = count_chars(plan.source);
                    last_output_chars_ = count_chars(plan.target);
                }
                if (settings.lint) lint_one(plan.target);
                return plan.target;
            },
            cancel_state_, "处理");

        running_ = false;
        update_controls();

        last_outputs_ = outcome.outputs;

        // 面板开着才把产物开头摆进去；收起时不动它，也不点亮「保存」。
        preview_ready_ = false;
        if (!stopped_ && written_ > 0 && !last_outputs_.empty() && preview_open_) {
            try {
                std::string head;
                bool truncated = false;
                read_head(last_outputs_.back(), kPreviewChars, head, truncated);
                panel_->set_text(head, truncated);
                preview_ready_ = true;
                panel_->set_save_enabled(true);
            } catch (const std::exception&) {
                // 读不动就不动面板：结果已经写进文件，面板留着上一回那一份。
                panel_->set_save_enabled(false);
            }
        }

        if (stopped_) {
            set_status("停下了，做完 " + std::to_string(written_) + " 份");
            return;
        }
        if (lint_only) {
            set_status("检查完 " + std::to_string(reports_.size()) +
                       " 份，没有改稿子：报告在「报告面板」里");
            return;
        }
        set_status(final_status(outcome, many));
    }

    // 这一份预览是按哪份稿、哪组参数算出来的：按「保存」时照原样再跑一遍，
    // 把完整结果写到用户挑的位置，而不是把面板里那两万字存成一份。
    void remember_preview(const std::string& entry, const RunSettings& settings,
                          const std::vector<texttool::ChainStep>& steps) {
        preview_entry_ = entry;
        preview_settings_ = settings;
        preview_steps_ = steps;
        preview_ready_ = true;
        panel_->set_save_enabled(true);
    }

    // 「先看效果」与「收起预览」是同一个按钮：开着的时候收起，收起的时候展开并算一遍。
    void toggle_preview() {
        if (running_) return;
        if (preview_open_) {
            preview_open_ = false;
            preview_ready_ = false;
            panel_->set_save_enabled(false);
            peek_->copy_label("先看效果");
            reflow();
            set_status("预览收起来了，按「先看效果」再展开");
            return;
        }
        preview_open_ = true;
        peek_->copy_label("收起预览");
        reflow();
        run(true);
    }

    // 把预览的这一份完整结果存到用户挑的位置。面板里只放开头两万字，所以这里照同一组参数
    // 再跑一遍写盘；目标已存在时先留 .bak，与「开始处理」那一套写盘规矩相同。
    void save_preview() {
        if (!preview_ready_ || running_) return;

        const std::string suggested =
            preview_settings_.join
                ? join_page_output_name(app_)
                : texttool::file_name_of_any(texttool::processed_path(preview_entry_));

        PickerRequest request;
        request.title = "保存处理结果";
        request.start_dir = folder_of(preview_entry_);
        request.suggested_name = suggested;
        const std::string target = pick_path(request);
        if (target.empty()) return;

        running_ = true;
        update_controls();
        std::string failure;
        try {
            RunPlan plan;
            TempFile joined;
            std::string error;
            if (!make_plan(preview_entry_, preview_settings_, preview_steps_, joined, plan, error))
                throw std::runtime_error(error);

            if (plan.steps.empty()) {
                if (!preview_settings_.join)
                    throw std::runtime_error("这一趟没有要保存的处理结果");
                std::error_code exists_error;
                if (std::filesystem::exists(target, exists_error))
                    texttool::backup_before_overwrite(target);
                std::error_code copy_error;
                std::filesystem::copy_file(plan.source, target,
                                           std::filesystem::copy_options::overwrite_existing,
                                           copy_error);
                if (copy_error) throw std::runtime_error("写不进去：" + target);
            } else {
                texttool::ChainRequest chain;
                chain.source = plan.source;
                chain.target = target;
                chain.steps = plan.steps;
                chain.backup = true;
                texttool::run_chain(chain);
            }
        } catch (const std::exception& error) {
            failure = error.what();
        }
        running_ = false;
        update_controls();

        if (!failure.empty()) {
            set_status("保存没做成：" + failure);
            return;
        }
        set_status("已保存到：" + target);
    }

    // 预览：跑同一条链，产物读开头那一截，不碰目标文件。合并多份时同样先接出临时文件。
    void run_preview(const std::string& entry, const RunSettings& settings,
                     const std::vector<texttool::ChainStep>& steps) {
        RunPlan plan;
        TempFile joined;
        std::string error;
        try {
            if (!make_plan(entry, settings, steps, joined, plan, error)) {
                panel_->set_text(error, false);
                panel_->set_save_enabled(false);
                preview_ready_ = false;
                set_status(error);
                return;
            }
            if (plan.steps.empty()) {
                // 只合并多份：没有要串的步骤，接出来的那一份开头就是要看的东西。
                std::string head;
                bool truncated = false;
                read_head(plan.source, kPreviewChars, head, truncated);
                panel_->set_text(head, truncated);
                remember_preview(entry, settings, steps);
                set_status(truncated
                               ? "预览算完，没有写文件；产物比面板能放的长，按「保存」写出来的是完整结果"
                               : "预览算完，没有写文件");
                return;
            }

            texttool::ChainRequest request;
            request.source = plan.source;
            request.target = plan.target;
            request.steps = plan.steps;
            request.backup = false;  // 预览不写目标文件，也就没有备份这回事
            request.should_stop = [this]() { return cancel_state_.requested; };

            const texttool::ChainPreview result = texttool::preview_chain(request, kPreviewChars);
            if (result.stopped) {
                panel_->set_text("停下了，这一眼没看完。", false);
                panel_->set_save_enabled(false);
                preview_ready_ = false;
                set_status("停下了，没有写文件");
                return;
            }
            panel_->set_text(result.text, result.truncated);
            remember_preview(entry, settings, steps);
            set_status(result.truncated
                           ? "预览算完，没有写文件；产物比面板能放的长，按「保存」写出来的是完整结果"
                           : "预览算完，没有写文件");
        } catch (const std::exception& error_text) {
            panel_->set_text(std::string("没看上：") + error_text.what(), false);
            panel_->set_save_enabled(false);
            preview_ready_ = false;
            set_status(std::string("没看上：") + error_text.what());
        }
    }

    // 在一份稿上跑一遍检查，报告攒起来给报告面板看。查不动也记一节，别只留在状态条上。
    void lint_one(const std::string& path) {
        std::string report;
        std::string error;
        if (lint_page_report(app_, path, report, error)) {
            reports_.emplace_back(path, report);
        } else {
            reports_.emplace_back(path, "查不了这一份：" + error);
        }
    }

    std::string final_status(const BatchOutcome& outcome, bool many) const {
        if (outcome.cancelled) return "停下了，做完 " + std::to_string(written_) + " 份";

        if (!outcome.failures.empty()) {
            if (written_ == 0) return failure_line(outcome.failures.front(), many);
            return "做好 " + std::to_string(written_) + " 份，有 " +
                   std::to_string(outcome.failures.size()) + " 份没做成；" +
                   failure_line(outcome.failures.front(), many);
        }
        if (written_ == 0) return "没有做成任何一份";
        if (!many && !last_outputs_.empty())
            return "做好了：" + texttool::file_name_of_any(last_outputs_.back());
        return "做好 " + std::to_string(written_) + " 份";
    }

    // ------------------------------------------------------------ 按钮状态

    // 按钮与勾选框的启用关系只从这里给，别处不零散地 activate/deactivate，
    // 否则跑完一轮以后界面上的状态与勾选框对不上。
    void update_controls() {
        const bool joining = join_check_->value() != 0;
        if (joining) {
            // 合并多份与这三项这一轮不能一起跑：清掉勾并置灰，免得勾着却跑不了。
            merge_check_->value(0);
            replace_check_->value(0);
            vn_check_->value(0);
            merge_check_->deactivate();
            replace_check_->deactivate();
            vn_check_->deactivate();
        } else {
            merge_check_->activate();
            replace_check_->activate();
            if (vn_row_shown_) {
                vn_check_->activate();
            } else {
                vn_check_->deactivate();
            }
        }

        // 备份只对覆盖有意义：不覆盖时写的是新文件，原文件根本没动。
        if (overwrite_->value() != 0) {
            backup_->activate();
        } else {
            backup_->value(0);
            backup_->deactivate();
        }

        // 「开始处理」与「先看一眼」一直点得动：点下去的时候再检查框里填了什么，
        // 状态条说清楚为什么没跑。这样选完文件、拖进来、改过路径都不必再刷新一次按钮状态。
        if (running_) {
            run_->deactivate();
            peek_->deactivate();
            cancel_button_->activate();
        } else {
            run_->activate();
            peek_->activate();
            cancel_button_->deactivate();
        }
        // 面板收起、或者算的时候出错，就不该让人按「保存」。
        if (panel_ != nullptr) panel_->set_save_enabled(preview_ready_ && !running_);
    }

    // 状态条上现在该说哪一句：合并把三项关掉时说明为什么，一项都没勾时说清楚。
    void refresh_status_note() {
        if (running_) return;
        if (join_check_->value() != 0) {
            set_status(kJoinNote);
            return;
        }
        const RunSettings settings = read_settings();
        if (!settings.merge && !settings.replace && !settings.vn && !settings.lint) {
            set_status(kNothingNote);
            return;
        }
        const std::string path = input_path();
        set_status(path.empty() ? std::string("就绪：把稿子拖进来，或者按「选文件…」挑一份")
                                : "要处理的稿子：" + path);
    }

    // 清单里多不多「排版视觉小说」这一行。多出来时清单整体往下推，藏起来时整体上移。
    void set_vn_row(bool shown) {
        if (vn_row_shown_ == shown) return;
        vn_row_shown_ = shown;
        if (shown) {
            vn_check_->show();
            vn_check_->value(0);
        } else {
            vn_check_->value(0);
            vn_check_->hide();
        }
        update_controls();
        reflow();
    }

    // ------------------------------------------------------------ 自检入口

    // 自检只跑其中的一项时，把清单勾成那一项、别的都清掉；合并多份把源文件所在的文件夹当源文件夹。
    // single_step 为 0 时按现在勾的跑，只换源文件框。
    void apply_single_step(const std::string& path, int single_step) {
        join_name_override_.clear();
        dropped_.clear();

        if (single_step != 0) {
            merge_check_->value(0);
            join_check_->value(0);
            replace_check_->value(0);
            lint_check_->value(0);
            overwrite_->value(0);
            backup_->value(0);
            set_vn_row(single_step == 4);
            vn_check_->value(single_step == 4 ? 1 : 0);
        }

        std::string source_path = path;
        switch (single_step) {
            case 1:
                merge_check_->value(1);
                break;
            case 2:
                join_check_->value(1);
                source_path = folder_of(path);
                if (source_path.empty()) source_path = path;
                join_name_override_ = kSelfCheckJoinName;
                break;
            case 3:
                replace_check_->value(1);
                break;
            case 4:
                break;  // 上面已经把这一项勾上了
            default:
                break;
        }

        source_->value(source_path.c_str());
        update_controls();
        reflow();
    }

    // -------------------------------------------------------------- 弹窗

    static void center_on(Fl_Window& dialog, Fl_Window* owner) {
        if (owner == nullptr) return;
        const int px = owner->x() + (owner->w() - dialog.w()) / 2;
        const int py = owner->y() + (owner->h() - dialog.h()) / 2;
        dialog.position(px < 0 ? 0 : px, py < 0 ? 0 : py);
    }

    // 覆盖确认。返回真就是照这么办，点「取消」或者点关闭框都算不办。
    bool confirm_overwrite() {
        constexpr int kWidth = 460;
        constexpr int kHeight = 190;

        bool proceed = false;
        Fl_Window dialog(0, 0, kWidth, kHeight, "直接改原文件");
        dialog.set_modal();
        dialog.color(kPageBg);
        dialog.labelcolor(kInk);
        dialog.begin();

        // 断行自己写死：界面库的自动折行在中文长句上不可靠，这一句又必须一次看全。
        auto* message = make_text(24, 22, kWidth - 48, 84,
                                  "勾了「直接改原文件」，又没勾「改之前先留一份备份」。\n"
                                  "这一趟会把结果写回原来那一份稿子上，\n"
                                  "原来的内容再也找不回来。",
                                  15, kWarnInk);
        message->align(FL_ALIGN_LEFT | FL_ALIGN_INSIDE);

        auto* cancel = make_button(kWidth - 24 - 76, kHeight - 54, 76, 32, "取消");
        cancel->labelsize(15);
        cancel->callback([](Fl_Widget* button, void* data) {
            *static_cast<bool*>(data) = false;
            button->window()->hide();
        }, &proceed);

        auto* accept = make_button(kWidth - 24 - 76 - 8 - 100, kHeight - 54, 100, 32, "照样改");
        accept->labelsize(15);
        accept->callback([](Fl_Widget* button, void* data) {
            *static_cast<bool*>(data) = true;
            button->window()->hide();
        }, &proceed);

        dialog.callback([](Fl_Widget* window, void*) { window->hide(); });
        dialog.end();

        center_on(dialog, app_.window);
        dialog.show();
        apply_title_bar(&dialog);
        while (dialog.shown() != 0) Fl::wait();
        return proceed;
    }

    // 报告面板：按文件分节排出人读报告，选中就能复制。没查过时说明先勾哪一项。
    void show_report_panel() {
        constexpr int kWidth = 760;
        constexpr int kHeight = 560;

        // 缓冲区要先于窗口声明：局部变量按声明的倒序销毁，缓冲区后声明就会先死，
        // 而窗口里那个只读区还指着它，收窗口时去解引用已经放掉的那块内存。
        // 先声明窗口、后声明缓冲区，收的时候正好反过来：先收窗口与控件，再放缓冲区。
        Fl_Text_Buffer buffer;

        Fl_Window dialog(0, 0, kWidth, kHeight, "报告面板");
        dialog.set_modal();
        dialog.color(kPageBg);
        dialog.labelcolor(kInk);
        dialog.begin();

        auto* view = new Fl_Text_Display(16, 16, kWidth - 32, kHeight - 76);
        view->buffer(&buffer);
        view->textsize(15);
        view->color(kFieldBg);
        view->textcolor(kInk);
        view->cursor_color(kInk);
        view->selection_color(kAccent);
        view->wrap_mode(Fl_Text_Display::WRAP_AT_BOUNDS, 0);

        auto* close = make_button(kWidth - 16 - 92, kHeight - 50, 92, 32, "关闭");
        close->labelsize(15);
        close->callback([](Fl_Widget* button, void*) { button->window()->hide(); });

        dialog.callback([](Fl_Widget* window, void*) { window->hide(); });
        dialog.end();

        buffer.text(report_text().c_str());

        center_on(dialog, app_.window);
        dialog.show();
        apply_title_bar(&dialog);
        while (dialog.shown() != 0) Fl::wait();
    }

    std::string report_text() const {
        if (reports_.empty()) return kNoReportHint;
        std::string text;
        for (const auto& entry : reports_) {
            if (!text.empty()) text += "\n";
            text += entry.first;
            text += "\n";
            text += entry.second;
            if (text.empty() || text.back() != '\n') text += "\n";
        }
        return text;
    }

    // 更多功能：这一轮只有一项「排版视觉小说」。勾上当场把那一行加进清单，点「取消」还原。
    void show_more() {
        constexpr int kWidth = 420;
        constexpr int kHeight = 190;

        struct State {
            ProcessPage* page = nullptr;
            bool before = false;
        };
        State state;
        state.page = this;
        state.before = vn_row_shown_;

        Fl_Window dialog(0, 0, kWidth, kHeight, "更多功能");
        dialog.set_modal();
        dialog.color(kPageBg);
        dialog.labelcolor(kInk);
        dialog.begin();

        make_text(24, 20, kWidth - 48, 24, "这一页还能多做一件事：", 15, kMuted);

        auto* vn = new FlatCheck(24, 52, kWidth - 48, kFormRowHeight, kVnLabel);
        vn->labelsize(15);
        vn->value(vn_row_shown_ ? 1 : 0);
        vn->callback([](Fl_Widget* widget, void* data) {
            auto* more = static_cast<State*>(data);
            const int checked = static_cast<Fl_Check_Button*>(widget)->value();
            more->page->set_vn_row(checked != 0);
            if (!more->page->vn_row_shown_) {
                set_status("「排版视觉小说」那一行收起来了：要它就在「更多功能」里再勾上");
            } else {
                set_status("清单里多了一行「排版视觉小说」，回到「处理：」里勾上才会跑");
            }
        }, &state);

        auto* cancel = make_button(kWidth - 24 - 76, kHeight - 54, 76, 32, "取消");
        cancel->labelsize(15);
        cancel->callback([](Fl_Widget* button, void* data) {
            auto* more = static_cast<State*>(data);
            more->page->set_vn_row(more->before);
            button->window()->hide();
        }, &state);

        auto* accept = make_button(kWidth - 24 - 76 - 8 - 76, kHeight - 54, 76, 32, "好");
        accept->labelsize(15);
        accept->callback([](Fl_Widget* button, void*) { button->window()->hide(); });

        dialog.callback([](Fl_Widget* window, void* data) {
            auto* more = static_cast<State*>(data);
            more->page->set_vn_row(more->before);
            window->hide();
        }, &state);
        dialog.end();

        center_on(dialog, app_.window);
        dialog.show();
        apply_title_bar(&dialog);
        while (dialog.shown() != 0) Fl::wait();
    }

    // -------------------------------------------------------------- 回调

    static void on_option_changed(Fl_Widget*, void* data) {
        auto* self = static_cast<ProcessPage*>(data);
        self->update_controls();
        self->refresh_status_note();
    }

    static void on_overwrite_changed(Fl_Widget*, void* data) {
        auto* self = static_cast<ProcessPage*>(data);
        self->update_controls();
        // 这一行不往前推进什么，只说清楚改的是哪儿。
        if (self->overwrite_->value() != 0) {
            set_status("结果会写回原来那一份稿子上");
        } else {
            set_status("结果会写成旁边的一份新文件，原来那一份不动");
        }
    }

    static void on_run(Fl_Widget*, void* data) { static_cast<ProcessPage*>(data)->run(false); }

    static void on_peek(Fl_Widget*, void* data) {
        static_cast<ProcessPage*>(data)->toggle_preview();
    }

    static void on_save_preview(Fl_Widget*, void* data) {
        static_cast<ProcessPage*>(data)->save_preview();
    }

    static void on_stop(Fl_Widget*, void* data) {
        auto* self = static_cast<ProcessPage*>(data);
        // 只置一个标记：每份开始前、链的两步之间各看一眼。正在算的那一步不打断，
        // 否则会留下写了一半的结果。
        self->cancel_state_.requested = true;
        set_status("正在收尾，这一份做完就停");
    }

    static void on_show_report(Fl_Widget*, void* data) {
        static_cast<ProcessPage*>(data)->show_report_panel();
    }

    static void on_show_more(Fl_Widget*, void* data) {
        static_cast<ProcessPage*>(data)->show_more();
    }

    AppContext& app_;
    SourceRow* source_row_ = nullptr;  // 「要处理的稿子：」那一行，自己排内部
    Fl_Input* source_ = nullptr;        // 上面那一行里的输入框，这一页直接读写它
    Fl_Box* format_label_ = nullptr;
    Fl_Input* format_ = nullptr;
    Fl_Box* list_label_ = nullptr;
    FlatCheck* merge_check_ = nullptr;
    FlatCheck* join_check_ = nullptr;
    FlatCheck* replace_check_ = nullptr;
    FlatCheck* lint_check_ = nullptr;
    Fl_Button* report_button_ = nullptr;
    Fl_Button* more_button_ = nullptr;
    FlatCheck* vn_check_ = nullptr;  // 「更多功能」里勾上才出现在清单里
    FlatCheck* overwrite_ = nullptr;
    FlatCheck* backup_ = nullptr;
    Fl_Button* run_ = nullptr;
    Fl_Button* peek_ = nullptr;
    Fl_Button* cancel_button_ = nullptr;
    PreviewPanel* panel_ = nullptr;

    std::vector<std::string> dropped_;  // 拖进来或选进来的那一批，目录已展开成文件
    std::vector<std::pair<std::string, std::string>> reports_;  // 每份产物的检查报告，前面是文件

    CancelState cancel_state_;
    bool running_ = false;
    bool vn_row_shown_ = false;  // 清单里有没有「排版视觉小说」这一行
    bool interactive_ = true;    // 自检时关掉，免得弹窗把脚本挂住
    bool measure_ = false;       // 自检时数一数源文与结果各多少字
    bool stopped_ = false;       // 这一轮是不是被「停止」打断的
    bool preview_open_ = false;  // 右边的预览面板是不是展开着
    bool preview_ready_ = false;  // 面板里那份结果能不能按「保存」写出来
    std::string preview_entry_;   // 这份预览是按哪份稿算的
    RunSettings preview_settings_;  // 算这份预览时勾的那几项
    std::vector<texttool::ChainStep> preview_steps_;  // 算这份预览用的那几步参数
    std::size_t written_ = 0;    // 这一轮真的写成了几份
    std::size_t last_input_chars_ = 0;
    std::size_t last_output_chars_ = 0;
    std::vector<std::string> last_outputs_;  // 这一轮交回的几个产物路径
    std::string join_name_override_;         // 自检合并多份时用定死的名字
    unsigned long temp_counter_ = 0;         // 接出来的临时文件本轮第几份
};

// 自检入口要的那一页从页登记表取。
ProcessPage* process_page_of(AppContext& app) {
    return static_cast<ProcessPage*>(app.page(PageId::Process));
}

}  // namespace

Page* build_process_page(AppContext& app, int x, int y, int w, int h) {
    auto* page = new ProcessPage(app, x, y, w, h);
    app.register_page(PageId::Process, page);
    return page;
}

SelfCheckSummary run_process_page_on_file(AppContext& app, const std::string& path,
                                          int single_step) {
    ProcessPage* page = process_page_of(app);
    if (page == nullptr) return {};
    return page->run_file(path, single_step);
}

}  // namespace texttool::ui
