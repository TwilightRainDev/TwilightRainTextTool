// 接回断行页：只放这一项的参数。
//
// 这一页管什么：分段长度、按字节算还是按字数算、四个附加处理的开关，以及两格标点。
// 屏上的取值由处理页在跑之前取走（见 merge_page_options），换成引擎的 MergeOptions。
//
// 这一页不管什么：不读稿子、不碰文件、不跑处理、不显示结果。挑稿子、开始处理、看预览都在
// 「处理」那一页上；换标点由处理页另起一步做，所以这里既不打开那一步，也不留规则表。
#include <FL/Fl_Box.H>
#include <FL/Fl_Input.H>

#include <algorithm>
#include <string>

#include "engine/io.h"
#include "engine/processing.h"
#include "ui/pages.h"
#include "ui/widgets.h"

namespace texttool::ui {
namespace {

// 版式：标签一列从页面左边起，控件从字段列起，纵向一行一行往下累加。
// 这里只写行距与控件本身的宽，位置一律在 layout() 里按页面当时拿到的 x() y() w() h() 算：
// 窗口尺寸一变重排一次就够，不写任何绝对坐标。
constexpr int kTopGap = 12;          // 第一行离页面顶边多远
constexpr int kRowGap = 8;           // 相邻两行之间
constexpr int kInlineGap = 12;       // 同一行里前后两个控件之间
constexpr int kRadioGap = 28;        // 两个单选之间留宽些，文字与下一个圆环才分得开
constexpr int kThresholdWidth = 124; // 数字框：数字区 50 + 两个加减按钮各 37
constexpr int kPunctInputWidth = 98;
constexpr int kNoMergeInputWidth = 94;

// 勾选框的宽度由 choice_width() 按文案量出来，不按字数估：方块边长与方块到文字的间隔
// 是控件自己定的。建控件与量宽度用同一句文案，下面这几份就是那句话的出处。
constexpr const char* kByteLabel = "按字节算";
constexpr const char* kCharLabel = "按字数算";
constexpr const char* kFixCjkLabel = "把行末是汉字的中文句子接回去";
constexpr const char* kPunctLabel = "把行末是不正常标点：";
constexpr const char* kTrimLabel = "去掉行首多余的标点";
constexpr const char* kNoMergeLabel = "遇到句末标点：";

// 两行标点后面那半句。这两块后面不再接控件，宽度按字数留够就行（一个汉字 15 像素）。
constexpr int kPunctTailWidth = 100;    // 「的句子接回去」六个字
constexpr int kNoMergeTailWidth = 115;  // 「就别急着接回去」七个字

class MergePage : public Page {
public:
    MergePage(AppContext& app, int x, int y, int w, int h) : Page(x, y, w, h) {
        (void)app;  // 这一页只收参数，用不到主窗口给的东西
        box(FL_NO_BOX);
        color(kPageBg);
        begin();

        // 控件先按占位位置建好，实位一律在 layout() 里算：构造完调一次 reflow()，
        // 之后窗口尺寸一变，Page::resize 会再调一遍。
        threshold_caption_ = make_text(x, y, kLabelWidth, kFormRowHeight, "分段长度：");
        threshold_ = new NumberBox(x, y, kThresholdWidth, kFormRowHeight, 1, 1000, 20, 50);
        threshold_->tooltip("一段拼到这么长就断开；按字节算是给老编辑器的稿子用的");
        by_byte_ = new FlatRadio(x, y, choice_width(kByteLabel), kFormRowHeight, kByteLabel);
        by_char_ = new FlatRadio(x, y, choice_width(kCharLabel), kFormRowHeight, kCharLabel);
        by_byte_->type(FL_RADIO_BUTTON);
        by_char_->type(FL_RADIO_BUTTON);
        by_byte_->labelsize(15);
        by_char_->labelsize(15);
        by_byte_->value(0);
        by_char_->value(1);

        extra_caption_ = make_text(x, y, kLabelWidth, kFormRowHeight, "附加处理：");

        fix_cjk_ = new FlatCheck(x, y, choice_width(kFixCjkLabel), kFormRowHeight, kFixCjkLabel);
        fix_cjk_->labelsize(15);
        fix_cjk_->value(1);

        fix_punct_ = new FlatCheck(x, y, choice_width(kPunctLabel), kFormRowHeight, kPunctLabel);
        fix_punct_->labelsize(15);
        punct_chars_ = make_input(x, y, kPunctInputWidth, kFormRowHeight, "，");
        punct_tail_ = make_text(x, y, kPunctTailWidth, kFormRowHeight, "的句子接回去");

        trim_comma_ = new FlatCheck(x, y, choice_width(kTrimLabel), kFormRowHeight, kTrimLabel);
        trim_comma_->labelsize(15);
        trim_comma_->value(1);

        no_merge_ = new FlatCheck(x, y, choice_width(kNoMergeLabel), kFormRowHeight, kNoMergeLabel);
        no_merge_->labelsize(15);
        no_merge_chars_ = make_input(x, y, kNoMergeInputWidth, kFormRowHeight, "。！？");
        no_merge_tail_ = make_text(x, y, kNoMergeTailWidth, kFormRowHeight, "就别急着接回去");

        end();

        reflow();  // 按页面现有尺寸摆一遍
    }

    // 把屏上的取值填进 options。换标点由处理页另起一步做，这一步恒不打开。
    void fill_options(texttool::MergeOptions& options) const {
        options.threshold = threshold_->value();
        options.by_chars = by_char_->value() != 0;
        options.fix_cjk = fix_cjk_->value() != 0;
        options.fix_punct = fix_punct_->value() != 0;
        // 格子空着就用引擎自己那一档：空串会被当成一条标点都没有，接了等于没接。
        const std::string punct = texttool::trim_whitespace(punct_chars_->value());
        if (!punct.empty()) options.punct_chars = punct;
        options.no_merge = no_merge_->value() != 0;
        const std::string no_merge_chars = texttool::trim_whitespace(no_merge_chars_->value());
        if (!no_merge_chars.empty()) options.no_merge_chars = no_merge_chars;
        options.trim_leading_comma = trim_comma_->value() != 0;
        options.apply_replace = false;
        options.replace_rules.clear();
    }

protected:
    // 按页面现在的左上角与宽高摆一遍直接子控件：标签列从左边距起，控件从字段列起。
    // 四行附加处理里，第一行与「附加处理：」标签同一行，后三行各占一行。
    void layout() override {
        const int left = x() + kPagePadLeft;
        const int field_x = x() + kPageFieldLeft;
        const int right = x() + w() - kPagePadRight;
        const int field_width = std::max(0, right - field_x);

        int row = y() + kTopGap;

        // 数字框宽度固定（里面按数字区算分隔线），换尺寸时只换位置、不改宽度。
        threshold_caption_->resize(left, row, kLabelWidth, kFormRowHeight);
        threshold_->resize(field_x, row, kThresholdWidth, kFormRowHeight);
        // 两个单选各按自己的文案占宽，中间留 kRadioGap，免得和前一个的文字连成一句。
        const int byte_x = field_x + kThresholdWidth + kInlineGap;
        const int byte_w = choice_width(kByteLabel);
        by_byte_->resize(byte_x, row, byte_w, kFormRowHeight);
        by_char_->resize(byte_x + byte_w + kRadioGap, row, choice_width(kCharLabel), kFormRowHeight);
        row += kFormRowHeight + kRowGap;

        extra_caption_->resize(left, row, kLabelWidth, kFormRowHeight);
        fix_cjk_->resize(field_x, row, field_width, kFormRowHeight);
        row += kFormRowHeight + kRowGap;

        // 这一行四件：勾选框、标点格子、后面那半句。前两件按量好的宽度排，
        // 剩下的空白留在右边。下面的「遇到句末标点」那一行同样排法。
        const int punct_w = choice_width(kPunctLabel);
        fix_punct_->resize(field_x, row, punct_w, kFormRowHeight);
        const int punct_input_x = field_x + punct_w + kInlineGap;
        punct_chars_->resize(punct_input_x, row, kPunctInputWidth, kFormRowHeight);
        punct_tail_->resize(punct_input_x + kPunctInputWidth + kInlineGap, row, kPunctTailWidth,
                            kFormRowHeight);
        row += kFormRowHeight + kRowGap;

        trim_comma_->resize(field_x, row, choice_width(kTrimLabel), kFormRowHeight);
        row += kFormRowHeight + kRowGap;

        const int no_merge_w = choice_width(kNoMergeLabel);
        no_merge_->resize(field_x, row, no_merge_w, kFormRowHeight);
        const int no_merge_input_x = field_x + no_merge_w + kInlineGap;
        no_merge_chars_->resize(no_merge_input_x, row, kNoMergeInputWidth, kFormRowHeight);
        no_merge_tail_->resize(no_merge_input_x + kNoMergeInputWidth + kInlineGap, row,
                               kNoMergeTailWidth, kFormRowHeight);
    }

    // 翻到这一页时状态条说自己这一句：参数在这里设，处理要到处理页上开。
    // 状态条只有一份文字，不重写就留着别页写的话。
    void on_shown() override {
        set_status("分段长度与附加处理在这里设，设好就到「处理」页勾上接回断裂的句子再开始处理");
    }

private:
    Fl_Box* threshold_caption_ = nullptr;
    Fl_Box* extra_caption_ = nullptr;
    NumberBox* threshold_ = nullptr;
    FlatRadio* by_byte_ = nullptr;
    FlatRadio* by_char_ = nullptr;
    FlatCheck* fix_cjk_ = nullptr;
    FlatCheck* fix_punct_ = nullptr;
    Fl_Input* punct_chars_ = nullptr;
    Fl_Box* punct_tail_ = nullptr;
    FlatCheck* trim_comma_ = nullptr;
    FlatCheck* no_merge_ = nullptr;
    Fl_Input* no_merge_chars_ = nullptr;
    Fl_Box* no_merge_tail_ = nullptr;
};

}  // namespace

Page* build_merge_page(AppContext& app, int x, int y, int w, int h) {
    auto* page = new MergePage(app, x, y, w, h);
    app.register_page(PageId::Merge, page);
    return page;
}

bool merge_page_options(AppContext& app, texttool::MergeOptions& options, std::string& error) {
    auto* page = static_cast<MergePage*>(app.page(PageId::Merge));
    if (page == nullptr) {
        error = "接回断行那一页还没建出来，取不到参数";
        return false;
    }
    page->fill_options(options);
    return true;
}

}  // namespace texttool::ui
