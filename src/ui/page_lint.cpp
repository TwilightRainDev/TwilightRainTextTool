// 文字体检页：定下这一趟查哪些问题，以及改检查项自己。
//
// 这一页不出报告、也不读稿：稿子在「处理」页跑，报告也在那一页的「报告面板」里看。
// 页上只留两样东西：
//   一是这一趟怎么查（显示哪些问题、稿子类型、只查指定的几条）；
//   二是「改这一条…」：规则清单窗，改完写进用户自己的规则覆盖文件，内置那一份不动。
//
// 改过的规则要下次打开程序才读进来，所以存好以后只在状态条说一句「重新打开程序后生效」：
// 这一页与它出的报告都用启动时读进来的那份规则表，界面这边不另造一套刷新。
//
// 报告仍然由这一页出（见 lint_page_report）：排版与命令行 lint 打出来的那一份相同，
// 界面这里不拼任何报告字符串。
#include <FL/Fl_Box.H>
#include <FL/Fl_Button.H>
#include <FL/Fl_Check_Button.H>
#include <FL/Fl_Choice.H>
#include <FL/Fl_Hold_Browser.H>
#include <FL/Fl_Input.H>
#include <FL/Fl_Multiline_Input.H>
#include <FL/Fl_Window.H>

#include <algorithm>
#include <cctype>
#include <cstddef>
#include <cstdlib>
#include <exception>
#include <filesystem>
#include <fstream>
#include <map>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

#include "engine/io.h"
#include "engine/lint_runner.h"
#include "engine/report.h"
#include "engine/rule_overrides.h"
#include "engine/rules.h"
#include "ui/pages.h"
#include "ui/widgets.h"

namespace texttool::ui {
namespace {

constexpr const char* kHitLogLabel = "记下检查命中（排查用）";

// 界面自己写命中日志的落点：用户状态目录，不放在程序旁边（程序可能装在只读目录）。
// Linux 认 XDG_STATE_HOME，没有就 ~/.local/state；Windows 用 LOCALAPPDATA。
std::string default_hit_log_path() {
#ifdef _WIN32
    const char* base = std::getenv("LOCALAPPDATA");
    return std::string(base != nullptr && *base != '\0' ? base : ".") + "/texttool/hits.jsonl";
#else
    const char* state = std::getenv("XDG_STATE_HOME");
    if (state != nullptr && *state != '\0') return std::string(state) + "/texttool/hits.jsonl";
    const char* home = std::getenv("HOME");
    return std::string(home != nullptr && *home != '\0' ? home : ".") +
           "/.local/state/texttool/hits.jsonl";
#endif
}

// 把一份报告的全部命中追加进日志。写不进去时返回 false，原因写进 error；
// 报告本身照旧出，日志失败不让用户丢掉这一趟的结果。
bool append_hit_log(const std::string& path, const texttool::ReportInput& report,
                    std::string& error) {
    std::error_code ignored;
    const std::filesystem::path file(path);
    if (file.has_parent_path()) std::filesystem::create_directories(file.parent_path(), ignored);

    std::ofstream out(path, std::ios::binary | std::ios::app);
    if (!out) {
        error = "日志写不进去：" + path;
        return false;
    }
    texttool::render_hit_log(report, [](std::string_view chunk, void* context) {
        auto* stream = static_cast<std::ofstream*>(context);
        stream->write(chunk.data(), static_cast<std::streamsize>(chunk.size()));
    }, &out);
    out.flush();
    if (!out) {
        error = "日志写不进去：" + path;
        return false;
    }
    return true;
}

// 一份稿跑一遍并排成人读报告。填的字段就是命令行出报告时填的那几样，两边少填一个，
// 界面上看到的报告就和命令行不是同一份了。
// 规则表与原文由调用方管着，生命周期要长过这次排版：命中里的下标与原文区间都指回它们。
// hit_log_path 非空时命中表留全、先写日志，再裁给报告。
std::string render_one(const texttool::CompiledRules& compiled, std::string_view text,
                       const std::string& source_name, const texttool::LintOptions& options,
                       const std::map<std::string, int>& collapse,
                       const std::string& hit_log_path) {
    texttool::LintOptions effective = options;
    if (!hit_log_path.empty()) effective.keep_all_hits = true;
    texttool::LintOutcome outcome = texttool::run_lint(compiled, text, effective);
    texttool::ReportInput input = texttool::make_report(source_name, std::move(outcome), collapse,
                                                        hit_log_path.empty());
    if (!hit_log_path.empty()) {
        std::string log_error;
        if (!append_hit_log(hit_log_path, input, log_error)) set_status(log_error);
        texttool::cap_report_hits(input);
    }
    return texttool::render_text_report(input);
}

// 规则表读不进来时那两句。读不到与被读坏是两件事，说法分开。
std::string rule_table_problem(const AppContext& app) {
    if (app.rules != nullptr && !app.rules->empty()) return {};
    return app.rule_error.empty() ? std::string("检查项还没读进来，做不了")
                                  : "检查项读不出来：" + app.rule_error;
}

// 把一扇窗摆到父窗口中间；父窗口空着（还没建出来）就留在它自己的位置上。
void center_on(Fl_Window& dialog, Fl_Window* parent, int width, int height) {
    if (parent == nullptr) return;
    const int px = parent->x() + (parent->w() - width) / 2;
    const int py = parent->y() + (parent->h() - height) / 2;
    dialog.position(px < 0 ? 0 : px, py < 0 ? 0 : py);
}

// ---------------------------------------------------------------------------
// 「只查指定的几条」勾选窗
//
// 界面库没有现成的多选列表，这里自己搭：一个模态窗口，里面一条规则一个勾选框，
// 只写规则标题（编号是内部编号，界面不显示），顺序就是规则表顺序。
// 标了默认不查的规则也列出来：勾上它等于按编号点名，引擎那边就会跑。
// ---------------------------------------------------------------------------

struct OnlyRulesState {
    Fl_Window* window = nullptr;
    std::vector<Fl_Check_Button*>* boxes = nullptr;
    const std::vector<texttool::LintRule>* rules = nullptr;
    std::vector<std::string> picked;  // 确定时把勾中的编号收到这里
    bool accepted = false;
};

void on_check_all(Fl_Widget*, void* data) {
    auto* state = static_cast<OnlyRulesState*>(data);
    for (auto* box : *state->boxes)
        if (box != nullptr) box->value(1);
}

void on_check_none(Fl_Widget*, void* data) {
    auto* state = static_cast<OnlyRulesState*>(data);
    for (auto* box : *state->boxes)
        if (box != nullptr) box->value(0);
}

void on_only_rules_ok(Fl_Widget*, void* data) {
    auto* state = static_cast<OnlyRulesState*>(data);
    state->picked.clear();
    const auto& boxes = *state->boxes;
    const auto& rules = *state->rules;
    for (std::size_t i = 0; i < boxes.size() && i < rules.size(); ++i)
        if (boxes[i] != nullptr && boxes[i]->value() != 0) state->picked.push_back(rules[i].id);
    state->accepted = true;
    if (state->window != nullptr) state->window->hide();
}

// 取消（包括点窗口的关闭框）：什么都不改，退出后沿用上一次的勾选。
void on_only_rules_cancel(Fl_Widget*, void* data) {
    auto* state = static_cast<OnlyRulesState*>(data);
    if (state != nullptr && state->window != nullptr) state->window->hide();
}

// ---------------------------------------------------------------------------
// 「改这一条…」规则清单窗
//
// 左边一列是全部检查项，右边改选中那一条的几项。存下去写的是用户自己的规则覆盖文件
// （程序旁边那一份），内置规则表一个字不动；窗里的「恢复这一条为内置」与「全部恢复内置」
// 就是把这一个编号的覆盖去掉、把整份覆盖清空。
//
// 保存前用 rule_problem 校验：不通过在窗里说清是哪一项不行，盘上原来那份不动。
// ---------------------------------------------------------------------------

// 编辑中的那一份。切换选择时先把屏上的取值收回 draft，来回点几下不会丢掉改到一半的。
struct RuleEditor {
    const std::vector<texttool::LintRule>* built_in = nullptr;  // 启动时读进来的那张表
    std::vector<texttool::LintRule> draft;
    int loaded = -1;      // 屏上是第几条，-1 表示还没挑
    bool changed = false; // 这一次窗里至少存过一次
    std::string argv0;    // 规则覆盖文件按程序所在目录找

    Fl_Window* window = nullptr;
    Fl_Hold_Browser* list = nullptr;
    Fl_Input* title = nullptr;
    Fl_Choice* severity = nullptr;
    Fl_Check_Button* default_off = nullptr;
    Fl_Check_Button* doc = nullptr;
    Fl_Check_Button* fiction = nullptr;
    Fl_Check_Button* agent = nullptr;
    NumberBox* min_count = nullptr;
    Fl_Multiline_Input* patterns = nullptr;
    Fl_Box* patterns_note = nullptr;
    Fl_Multiline_Input* hints = nullptr;
    Fl_Multiline_Input* detail = nullptr;
    Fl_Box* message = nullptr;
};

// 多行输入框里的每一行收成一条，空行跳过：空行是排版习惯，不是要一条空模式。
// 每行两头的空白也去掉：手粘进来的词表常在行尾多一个空格，留着匹配不上。
std::vector<std::string> lines_of(const char* text) {
    std::vector<std::string> out;
    std::string current;
    for (const char* at = text; at != nullptr && *at != '\0'; ++at) {
        if (*at == '\n') {
            const std::string line = texttool::trim_whitespace(current);
            if (!line.empty()) out.push_back(line);
            current.clear();
            continue;
        }
        if (*at != '\r') current.push_back(*at);
    }
    const std::string line = texttool::trim_whitespace(current);
    if (!line.empty()) out.push_back(line);
    return out;
}

std::string join_lines(const std::vector<std::string>& lines) {
    std::string out;
    for (const std::string& line : lines) {
        if (!out.empty()) out.push_back('\n');
        out += line;
    }
    return out;
}

bool has_genre(const std::vector<std::string>& genres, const char* name) {
    return std::find(genres.begin(), genres.end(), name) != genres.end();
}

// 规则编号不分大小写比，与引擎、报告、--only 的口径一致。
bool same_id(std::string_view left, std::string_view right) {
    if (left.size() != right.size()) return false;
    for (std::size_t i = 0; i < left.size(); ++i) {
        const auto a = static_cast<unsigned char>(left[i]);
        const auto b = static_cast<unsigned char>(right[i]);
        if (std::tolower(a) != std::tolower(b)) return false;
    }
    return true;
}

// 词表这一格按什么匹配，写在框底下一行：词面与正则两套写法在这儿是同一格，
// 分界就在前几条，不写清读者会把整词当成正则改。
std::string pattern_note(const texttool::LintRule& rule) {
    if (rule.kind == texttool::RuleKind::Algorithm)
        return "这一条的命不命中由程序算，没有词表可改。";
    const std::size_t literal = rule.literal_patterns ? rule.literal_patterns->size() : 0;
    if (literal == 0) {
        return rule.kind == texttool::RuleKind::Literal ? "一行一条，按整词匹配。"
                                                        : "一行一条，按正则匹配。";
    }
    return "一行一条；前 " + std::to_string(literal) + " 条按整词匹配，后面的按正则匹配。";
}

void say(RuleEditor& state, const std::string& text, bool warn) {
    if (state.message == nullptr) return;
    state.message->copy_label(text.c_str());
    state.message->labelcolor(warn ? kWarnInk : kMuted);
    state.message->redraw();
}

// 把第 index 条摆到屏上。
void fill_form(RuleEditor& state, int index) {
    if (index < 0 || index >= static_cast<int>(state.draft.size())) return;
    state.loaded = index;
    const texttool::LintRule& rule = state.draft[static_cast<std::size_t>(index)];

    state.title->value(rule.title.c_str());
    // 下拉第一项「提示」对应引擎的 info，第二项「要紧」对应 warn。
    state.severity->value(rule.severity == "warn" ? 1 : 0);
    state.default_off->value(rule.default_off ? 1 : 0);
    state.doc->value(has_genre(rule.genres, "doc") ? 1 : 0);
    state.fiction->value(has_genre(rule.genres, "fiction") ? 1 : 0);
    state.agent->value(has_genre(rule.genres, "agent") ? 1 : 0);
    state.min_count->value(rule.min_count);

    // 词表把两套写法摆在一个框里，次序与引擎合并的次序一致：整词的在前，正则的在后。
    std::vector<std::string> patterns;
    if (rule.literal_patterns) patterns = *rule.literal_patterns;
    patterns.insert(patterns.end(), rule.patterns.begin(), rule.patterns.end());
    state.patterns->value(join_lines(patterns).c_str());
    state.hints->value(rule.hints ? join_lines(*rule.hints).c_str() : "");
    state.detail->value(rule.detail.c_str());
    state.patterns_note->copy_label(pattern_note(rule).c_str());

    // 算法规则没有词表也没有建议：命不命中由代码算，写了也不生效。
    const bool has_patterns = rule.kind != texttool::RuleKind::Algorithm;
    if (has_patterns) {
        state.patterns->activate();
        state.hints->activate();
    } else {
        state.patterns->deactivate();
        state.hints->deactivate();
    }
    state.window->redraw();
}

// 把屏上的取值收回一条规则。够不着的项（编号、组、种类、算法名等）原样留着：
// 一律从 draft 那份拷贝里带过来，不在这里重填。
texttool::LintRule read_form(const RuleEditor& state) {
    texttool::LintRule rule = state.draft[static_cast<std::size_t>(state.loaded)];

    rule.title = texttool::trim_whitespace(state.title->value());
    rule.severity = state.severity->value() == 1 ? "warn" : "info";
    rule.default_off = state.default_off->value() != 0;

    rule.genres.clear();
    if (state.doc->value() != 0) rule.genres.push_back("doc");
    if (state.fiction->value() != 0) rule.genres.push_back("fiction");
    if (state.agent->value() != 0) rule.genres.push_back("agent");

    rule.min_count = state.min_count->value();

    if (rule.kind != texttool::RuleKind::Algorithm) {
        // 整词那几条留在前头，与摆出来的次序一致；条数变少时整词那一段跟着缩短，
        // 多出来的按正则算，与框底下那一行说明对得上。
        const std::size_t literal =
            rule.literal_patterns ? rule.literal_patterns->size() : std::size_t{0};
        const std::vector<std::string> lines = lines_of(state.patterns->value());
        const std::size_t split = std::min(literal, lines.size());
        if (split > 0) {
            rule.literal_patterns =
                std::vector<std::string>(lines.begin(), lines.begin() + static_cast<long>(split));
        } else {
            rule.literal_patterns.reset();
        }
        rule.patterns.assign(lines.begin() + static_cast<long>(split), lines.end());

        // 建议改法一条对一条；留空就是不给建议。
        const std::vector<std::string> hint_lines = lines_of(state.hints->value());
        if (hint_lines.empty()) {
            rule.hints.reset();
        } else {
            rule.hints = hint_lines;
        }
    }

    rule.detail = texttool::trim_whitespace(state.detail->value());
    return rule;
}

// 换一条改之前，先把屏上的取值收回正在编辑的那一条。
void stash_form(RuleEditor& state) {
    if (state.loaded < 0) return;
    state.draft[static_cast<std::size_t>(state.loaded)] = read_form(state);
}

// 全部恢复之前问一次：这一下把改过的每一条都清掉，退不回来。
struct ConfirmState {
    Fl_Window* window = nullptr;
    bool accepted = false;
};

void on_confirm_yes(Fl_Widget*, void* data) {
    auto* state = static_cast<ConfirmState*>(data);
    state->accepted = true;
    if (state->window != nullptr) state->window->hide();
}

void on_confirm_no(Fl_Widget*, void* data) {
    auto* state = static_cast<ConfirmState*>(data);
    if (state->window != nullptr) state->window->hide();
}

bool ask_restore_all(Fl_Window* parent) {
    constexpr int kWidth = 430;
    constexpr int kHeight = 150;

    Fl_Window window(0, 0, kWidth, kHeight, "全部恢复内置");
    window.set_modal();
    window.color(kPageBg);
    window.begin();

    Fl_Box question(20, 18, kWidth - 40, 62,
                    "改过的每一条都会退回内置那一份，退不回来。\n要全部恢复吗？");
    question.box(FL_NO_BOX);
    question.align(FL_ALIGN_LEFT | FL_ALIGN_INSIDE | FL_ALIGN_WRAP);
    question.labelsize(15);
    question.labelcolor(kInk);

    ConfirmState state;
    state.window = &window;

    const int buttons_y = kHeight - 14 - 30;
    auto* cancel = make_button(kWidth - 14 - 76, buttons_y, 76, 30, "取消");
    cancel->labelsize(14);
    cancel->callback(on_confirm_no, &state);
    auto* yes = make_button(kWidth - 14 - 76 - 8 - 96, buttons_y, 96, 30, "全部恢复");
    yes->labelsize(14);
    yes->callback(on_confirm_yes, &state);

    window.callback(on_confirm_no, &state);  // 点关闭框也算取消
    window.end();

    center_on(window, parent, kWidth, kHeight);
    window.show();
    apply_title_bar(&window);
    // 模态窗口自己转事件循环：这一圈里下面那扇窗收不到输入，退出后才回到它。
    while (window.shown() != 0) Fl::wait();
    return state.accepted;
}

void on_editor_pick(Fl_Widget* sender, void* data) {
    auto* state = static_cast<RuleEditor*>(data);
    const int picked = static_cast<Fl_Hold_Browser*>(sender)->value();  // 从 1 数
    if (picked <= 0) return;
    const int index = picked - 1;
    if (index == state->loaded) return;
    stash_form(*state);
    fill_form(*state, index);
}

void on_editor_save(Fl_Widget*, void* data) {
    auto* state = static_cast<RuleEditor*>(data);
    if (state->loaded < 0) {
        say(*state, "先在左边点一条，再改。", true);
        return;
    }
    texttool::LintRule rule = read_form(*state);
    // 标题空着的话，报告里这一组就没有名字，读者分不出是哪一条报的。
    if (rule.title.empty()) {
        say(*state, "标题不能空着：报告里每组问题都印这个标题。", true);
        return;
    }
    const std::string problem = texttool::rule_problem(rule);
    if (!problem.empty()) {
        say(*state, problem, true);
        return;
    }
    try {
        texttool::save_rule_override(state->argv0.c_str(), rule);
    } catch (const std::exception& failure) {
        say(*state, failure.what(), true);
        return;
    }

    const int index = state->loaded;
    state->draft[static_cast<std::size_t>(index)] = rule;
    state->list->text(index + 1, rule.title.c_str());  // 标题可能改了，左边跟着换
    state->changed = true;
    say(*state, "存好了。重新打开程序后生效。", false);
}

void on_editor_restore_one(Fl_Widget*, void* data) {
    auto* state = static_cast<RuleEditor*>(data);
    if (state->loaded < 0) {
        say(*state, "先在左边点一条，再恢复。", true);
        return;
    }
    const std::string id = state->draft[static_cast<std::size_t>(state->loaded)].id;
    try {
        texttool::restore_builtin_rule(state->argv0.c_str(), id);
    } catch (const std::exception& failure) {
        say(*state, failure.what(), true);
        return;
    }

    // 启动时读进来的那张表就是内置那一份：按编号找回原来那条，屏上跟着变回去。
    if (state->built_in != nullptr) {
        for (const texttool::LintRule& rule : *state->built_in) {
            if (!same_id(rule.id, id)) continue;
            state->draft[static_cast<std::size_t>(state->loaded)] = rule;
            break;
        }
    }
    fill_form(*state, state->loaded);
    state->changed = true;
    say(*state, "已恢复成内置那条。重新打开程序后生效。", false);
}

void on_editor_restore_all(Fl_Widget*, void* data) {
    auto* state = static_cast<RuleEditor*>(data);
    if (!ask_restore_all(state->window)) {
        say(*state, "没有恢复：改过的还在。", false);
        return;
    }
    try {
        texttool::restore_all_builtin_rules(state->argv0.c_str());
    } catch (const std::exception& failure) {
        say(*state, failure.what(), true);
        return;
    }
    if (state->built_in != nullptr) {
        state->draft = *state->built_in;
        state->list->clear();
        for (const texttool::LintRule& rule : state->draft) state->list->add(rule.title.c_str());
    }
    fill_form(*state, state->loaded);
    state->changed = true;
    say(*state, "全部恢复成内置的了。重新打开程序后生效。", false);
}

void on_editor_close(Fl_Widget*, void* data) {
    auto* state = static_cast<RuleEditor*>(data);
    if (state != nullptr && state->window != nullptr) state->window->hide();
}

// 这一页的行距与控件宽度。纵向位置由 layout() 从页面顶边一行行累加出来，
// 横向从左边界、字段列、右边界算。这里只写每段间隔与控件本身多宽，不写任何绝对坐标：
// 页面拿到多大就按多大排，窗口尺寸一变重排一次就够。
constexpr int kTopGap = 12;            // 第一行离页面顶边多远
constexpr int kRowGap = 10;            // 相邻两行之间
constexpr int kInlineGap = 8;          // 同一行里前后两个控件之间
constexpr int kColumnGap = 28;         // 「稿子类型：」那一列离左边下拉多远
constexpr int kButtonHeight = 32;      // 按钮都比输入行高一点
constexpr int kChoiceWidth = 160;      // 两个下拉框各占这么宽
constexpr int kGenreLabelWidth = 96;   // 「稿子类型：」这一格
constexpr int kCheckedWidth = 168;     // 「已选 N 条，共 M 条」
constexpr int kOnlyRulesWidth = 132;   // 「只查指定的几条」
constexpr int kEditRuleWidth = 108;    // 「改这一条…」
constexpr int kNoteHeight = 22;        // 那一行淡色说明

class LintPage : public Page {
public:
    LintPage(AppContext& app, int x, int y, int w, int h) : Page(x, y, w, h), app_(app) {
        box(FL_NO_BOX);
        color(kPageBg);
        begin();

        // 控件先按占位位置建好，实位一律在 layout() 里算：构造完调一次 reflow()，
        // 之后窗口尺寸一变，Page::resize 会再调一遍，所以这里写死哪个数都没有意义。
        // 两个选项都做成下拉：取值只有定死的几个，手打容易拼出引擎不认的写法。
        severity_label_ = make_text(x, y, kLabelWidth, kFormRowHeight, "显示哪些问题：");
        severity_ = make_choice(x, y, kChoiceWidth, kFormRowHeight, {"全部问题", "只看要紧的"});

        genre_label_ = make_text(x, y, kGenreLabelWidth, kFormRowHeight, "稿子类型：");
        genre_ = make_choice(x, y, kChoiceWidth, kFormRowHeight,
                             {"不限", "技术文档", "小说", "机器写的回复"});

        note_ = make_text(x, y, kLabelWidth, kNoteHeight, "只指出问题，不改字，也不动文件。", 14,
                          kMuted);

        hit_log_file_ = default_hit_log_path();
        hit_log_ = new FlatCheck(x, y, choice_width(kHitLogLabel), kFormRowHeight, kHitLogLabel);
        hit_log_->labelsize(14);
        hit_log_->value(ui_config_get("hit_log") == "1" ? 1 : 0);
        hit_log_enabled_ = hit_log_->value() != 0;
        hit_log_->tooltip(("检查命中追加写到：" + hit_log_file_).c_str());
        hit_log_->callback(on_toggle_hit_log, this);

        checked_ = make_text(x, y, kCheckedWidth, kButtonHeight, "已选 0 条，共 0 条", 14, kMuted);
        // 字靠右贴住后面的按钮：这一行是「选了几条」加两个动作，右对齐成一列好读。
        checked_->align(FL_ALIGN_RIGHT | FL_ALIGN_INSIDE);

        only_rules_ = make_button(x, y, kOnlyRulesWidth, kButtonHeight, "只查指定的几条");
        only_rules_->labelsize(14);
        only_rules_->callback(on_only_rules_clicked, this);

        edit_rule_ = make_button(x, y, kEditRuleWidth, kButtonHeight, "改这一条…");
        edit_rule_->labelsize(14);
        edit_rule_->callback(on_edit_rule_clicked, this);

        end();

        reflow();  // 按页面现有尺寸摆一遍
        update_checked();
    }

    // 这一趟怎么查：显示哪些问题、稿子类型、只查哪几条。
    // 处理页跑「报告面板」时按这一份来（见 pages.h 的 lint_page_options）。
    // 处理页跑报告时按这一项决定要不要把全部命中写进日志。
    bool hit_log_enabled() const { return hit_log_enabled_; }
    const std::string& hit_log_file() const { return hit_log_file_; }

    texttool::LintOptions options() const {
        texttool::LintOptions options;
        options.only_rules = only_rules_ids_;  // 空表等于按默认集合跑
        // 下拉第一项「全部问题」对应引擎的 info（info 就是最低一档），第二项对应 warn。
        options.min_severity = severity_->value() == 1 ? "warn" : "info";
        static const char* const kGenres[] = {"", "doc", "fiction", "agent"};
        const int genre = genre_->value();
        options.genre = (genre >= 1 && genre <= 3) ? kGenres[genre] : "";
        return options;
    }

protected:
    // 这一页露出来时状态条说自己这一句。状态条只有一份文字，不重写就留着别的页的话。
    void on_shown() override {
        set_status("这一页定这一趟查哪些问题；稿子和报告都在「处理」页那边");
    }

    // 这一页没有源文件这一行，推进来的稿子哪儿也不去。说一句，免得用户以为是没收下。
    void on_files(const std::vector<std::string>& files) override {
        if (files.empty()) return;
        set_status("这一页不收稿子：要查的稿子在「处理」页挑");
    }

    // 按页面现在的左上角与宽高摆一遍直接子控件：标签列从左边距起，字段列从 kPageFieldLeft 起，
    // 右边到右边界为止。几行都从顶边往下累加，高度都是定值，剩下的空白留在下面。
    void layout() override {
        const int left = x() + kPagePadLeft;
        const int field_x = x() + kPageFieldLeft;
        const int right = x() + w() - kPagePadRight;

        int row = y() + kTopGap;

        // 两个下拉并排：左边一问「显示哪些问题」，右边一问「稿子类型」。
        severity_label_->resize(left, row, kLabelWidth, kFormRowHeight);
        severity_->resize(field_x, row, kChoiceWidth, kFormRowHeight);
        const int genre_x = field_x + kChoiceWidth + kColumnGap;
        genre_label_->resize(genre_x, row, kGenreLabelWidth, kFormRowHeight);
        genre_->resize(genre_x + kGenreLabelWidth, row, kChoiceWidth, kFormRowHeight);
        row += kFormRowHeight + kRowGap;

        note_->resize(left, row, std::max(0, right - left), kNoteHeight);
        row += kNoteHeight + kRowGap;

        hit_log_->resize(left, row, choice_width(kHitLogLabel), kFormRowHeight);
        row += kFormRowHeight + kRowGap;

        // 已勾几条与两个按钮排成一行，贴着右边界往里排：三个东西都是「这一趟查哪几条」，
        // 从右往左连成一串，左边空出来的地方留给以后的内容。
        const int edit_rule_x = right - kEditRuleWidth;
        const int only_rules_x = edit_rule_x - kInlineGap - kOnlyRulesWidth;
        const int checked_x = only_rules_x - kInlineGap - kCheckedWidth;
        checked_->resize(checked_x, row, kCheckedWidth, kButtonHeight);
        only_rules_->resize(only_rules_x, row, kOnlyRulesWidth, kButtonHeight);
        edit_rule_->resize(edit_rule_x, row, kEditRuleWidth, kButtonHeight);
    }

private:
    void update_checked() {
        const std::size_t total = app_.rules == nullptr ? 0 : app_.rules->size();
        checked_->copy_label(("已选 " + std::to_string(only_rules_ids_.size()) + " 条，共 " +
                              std::to_string(total) + " 条")
                                 .c_str());
        checked_->redraw();
    }

    // -----------------------------------------------------------------------
    // 只查指定的几条
    // -----------------------------------------------------------------------

    void open_only_rules() {
        const std::string problem = rule_table_problem(app_);
        if (!problem.empty()) {
            set_status(problem);
            return;
        }
        const std::vector<texttool::LintRule>* rules = app_.rules;

        constexpr int kRowHeight = 26;
        constexpr int kDialogWidth = 480;
        constexpr int kHeader = 56;
        constexpr int kFooter = 48;
        constexpr int kMaxRows = 15;  // 一屏最多露 15 整行，再多就滚动
        constexpr int kListPad = 6;

        const int rules_count = static_cast<int>(rules->size());
        // 列高取整行的倍数：不取整的话最后一行只露出一半，看着像漏了一条。
        const int list_height = std::min(rules_count, kMaxRows) * kRowHeight + kListPad;
        const int height = kHeader + list_height + kFooter;

        Fl_Window dialog(0, 0, kDialogWidth, height, "只查这几条");
        dialog.set_modal();
        dialog.color(kPageBg);
        dialog.begin();

        make_text(16, 6, kDialogWidth - 32, 20,
                  "只列每条检查的名字，按原本次序排；一条都不选就按默认的那几条查。", 13, kMuted);
        make_text(16, 26, kDialogWidth - 32, 20, "勾上平时不查的检查，就是点名让它查。", 13,
                  kMuted);

        ScrollList scroll(14, kHeader, kDialogWidth - 28, list_height);
        scroll.begin();

        std::vector<Fl_Check_Button*> boxes;
        boxes.reserve(static_cast<std::size_t>(rules_count));
        for (int i = 0; i < rules_count; ++i) {
            const texttool::LintRule& rule = (*rules)[static_cast<std::size_t>(i)];
            const bool picked = std::find(only_rules_ids_.begin(), only_rules_ids_.end(), rule.id) !=
                                only_rules_ids_.end();
            auto* box = new FlatCheck(22, kHeader + kListPad / 2 + i * kRowHeight,
                                      kDialogWidth - 80, kRowHeight, rule.title.c_str());
            box->labelsize(14);
            box->value(picked ? 1 : 0);
            boxes.push_back(box);
        }
        scroll.end();

        OnlyRulesState state;
        state.window = &dialog;
        state.boxes = &boxes;
        state.rules = rules;

        const int footer_y = kHeader + list_height + 8;
        auto* all = make_button(14, footer_y, 76, 30, "全选");
        all->labelsize(14);
        all->callback(on_check_all, &state);
        auto* none = make_button(98, footer_y, 86, 30, "全不选");
        none->labelsize(14);
        none->callback(on_check_none, &state);
        auto* cancel = make_button(kDialogWidth - 14 - 76, footer_y, 76, 30, "取消");
        cancel->labelsize(14);
        cancel->callback(on_only_rules_cancel, &state);
        auto* ok = make_button(kDialogWidth - 14 - 76 - 8 - 76, footer_y, 76, 30, "确定");
        ok->labelsize(14);
        ok->callback(on_only_rules_ok, &state);

        dialog.callback(on_only_rules_cancel, &state);  // 点关闭框也算取消
        dialog.end();

        center_on(dialog, app_.window, kDialogWidth, height);

        dialog.show();
        apply_title_bar(&dialog);
        // 模态窗口自己转事件循环：这一圈里主窗口收不到输入，退出后才回到本页。
        while (dialog.shown() != 0) Fl::wait();

        if (!state.accepted) return;  // 取消：不动原来的选择
        only_rules_ids_ = std::move(state.picked);
        update_checked();
        set_status(only_rules_ids_.empty()
                       ? "一条都没点名，按默认的那几条查"
                       : "只查点名的 " + std::to_string(only_rules_ids_.size()) + " 条");
    }

    // -----------------------------------------------------------------------
    // 改这一条
    // -----------------------------------------------------------------------

    void open_rule_editor() {
        const std::string problem = rule_table_problem(app_);
        if (!problem.empty()) {
            set_status(problem);
            return;
        }

        constexpr int kDialogWidth = 720;
        constexpr int kDialogHeight = 540;
        constexpr int kListLeft = 14;
        constexpr int kListWidth = 200;
        constexpr int kLabelLeft = kListLeft + kListWidth + 14;
        constexpr int kNameWidth = 112;  // 「适用文本类型：」这一格
        constexpr int kFieldLeft = kLabelLeft + kNameWidth;
        constexpr int kFieldRight = kDialogWidth - 14;
        constexpr int kFieldWidth = kFieldRight - kFieldLeft;
        constexpr int kWideLeft = kLabelLeft;                      // 说明那几行铺到标签列
        constexpr int kWideWidth = kFieldRight - kLabelLeft;
        constexpr int kHeader = 30;
        constexpr int kFooter = 44;
        constexpr int kRowHeight = 26;
        constexpr int kRowGap = 6;
        constexpr int kFlat = 14;  // 窗里的小字与按钮字号

        Fl_Window dialog(0, 0, kDialogWidth, kDialogHeight, "改检查项");
        dialog.set_modal();
        dialog.color(kPageBg);
        dialog.begin();

        make_text(kListLeft, 6, kDialogWidth - 28, 20,
                  "左边挑一条，右边改。改的是你自己的那份规则文件，内置那一份不动。", 13, kMuted);

        RuleEditor state;
        state.built_in = app_.rules;
        state.draft = *app_.rules;
        state.argv0 = app_.program_path;
        state.window = &dialog;

        state.list = new Fl_Hold_Browser(kListLeft, kHeader, kListWidth,
                                        kDialogHeight - kHeader - kFooter);
        state.list->textsize(15);
        for (const texttool::LintRule& rule : state.draft) state.list->add(rule.title.c_str());
        state.list->callback(on_editor_pick, &state);

        int row = kHeader;

        make_text(kLabelLeft, row, kNameWidth, kRowHeight, "标题：", 15, kInk);
        state.title = make_input(kFieldLeft, row, kFieldWidth, kRowHeight);
        row += kRowHeight + kRowGap;

        // 严重度与「默认不查」并排：两者都是「这一条要不要报」，放一起好对照。
        make_text(kLabelLeft, row, kNameWidth, kRowHeight, "严重度：", 15, kInk);
        state.severity = make_choice(kFieldLeft, row, 120, kRowHeight, {"提示", "要紧"});
        state.default_off = new FlatCheck(kFieldLeft + 120 + kInlineGap * 2, row,
                                         choice_width("默认不查这一条", kFlat), kRowHeight,
                                         "默认不查这一条");
        state.default_off->labelsize(kFlat);
        row += kRowHeight + kRowGap;

        // 稿子类型：三条都不勾就是不限，任何稿子都查这一条。
        make_text(kLabelLeft, row, kNameWidth, kRowHeight, "适用文本类型：", 15, kInk);
        int genre_x = kFieldLeft;
        state.doc = new FlatCheck(genre_x, row, choice_width("技术文档", kFlat), kRowHeight, "技术文档");
        state.doc->labelsize(kFlat);
        genre_x += state.doc->w() + kInlineGap;
        state.fiction =
            new FlatCheck(genre_x, row, choice_width("小说", kFlat), kRowHeight, "小说");
        state.fiction->labelsize(kFlat);
        genre_x += state.fiction->w() + kInlineGap;
        state.agent = new FlatCheck(genre_x, row, choice_width("机器写的回复", kFlat), kRowHeight,
                                    "机器写的回复");
        state.agent->labelsize(kFlat);
        row += kRowHeight + kRowGap;

        make_text(kLabelLeft, row, kNameWidth, kRowHeight, "至少出现几次：", 15, kInk);
        state.min_count = new NumberBox(kFieldLeft, row, 122, kRowHeight, 1, 999, 1, 46);
        make_text(kFieldLeft + 122 + kInlineGap, row, kWideWidth - 122 - kInlineGap, kRowHeight,
                  "同一个写法凑够这么多次才报，一次不算。", 13, kMuted);
        row += kRowHeight + kRowGap;

        make_text(kLabelLeft, row, kNameWidth, kRowHeight, "词表：", 15, kInk);
        state.patterns = new Fl_Multiline_Input(kFieldLeft, row, kFieldWidth, 88);
        state.patterns->textsize(15);
        row += 88 + 2;
        state.patterns_note = make_text(kWideLeft, row, kWideWidth, 18, "", 13, kMuted);
        row += 18 + kRowGap;

        make_text(kLabelLeft, row, kNameWidth, kRowHeight, "建议改法：", 15, kInk);
        state.hints = new Fl_Multiline_Input(kFieldLeft, row, kFieldWidth, 60);
        state.hints->textsize(15);
        row += 60 + 2;
        // 建议按「词面在前、正则在后的合并次序」一条对一条，条数对不上引擎会拒绝，
        // 校验那一句会直接点出来，所以这里只写清怎么填。
        make_text(kWideLeft, row, kWideWidth, 18, "一行一条，与词表一条对一条；留空就不给建议。",
                  13, kMuted);
        row += 18 + kRowGap;

        make_text(kLabelLeft, row, kNameWidth, kRowHeight, "说明：", 15, kInk);
        state.detail = new Fl_Multiline_Input(kFieldLeft, row, kFieldWidth, 76);
        state.detail->textsize(15);
        row += 76 + kRowGap;

        state.message = make_text(kWideLeft, row, kWideWidth, kDialogHeight - row - kFooter,
                                  "先挑一条，改完按「保存」。", 13, kMuted);
        state.message->align(FL_ALIGN_LEFT | FL_ALIGN_INSIDE | FL_ALIGN_WRAP);

        const int footer_y = kDialogHeight - 14 - 30;
        auto* save = make_button(kListLeft, footer_y, 76, 30, "保存");
        save->labelsize(kFlat);
        save->callback(on_editor_save, &state);
        auto* restore_one =
            make_button(kListLeft + 76 + kInlineGap, footer_y, 172, 30, "恢复这一条为内置");
        restore_one->labelsize(kFlat);
        restore_one->callback(on_editor_restore_one, &state);
        auto* restore_all =
            make_button(kListLeft + 76 + kInlineGap + 172 + kInlineGap, footer_y, 132, 30,
                        "全部恢复内置");
        restore_all->labelsize(kFlat);
        restore_all->callback(on_editor_restore_all, &state);
        auto* close = make_button(kDialogWidth - 14 - 76, footer_y, 76, 30, "关闭");
        close->labelsize(kFlat);
        close->callback(on_editor_close, &state);

        dialog.callback(on_editor_close, &state);  // 点关闭框也算关闭
        dialog.end();

        // 一建起来就挑中第一条：右边摆着空格子，用户不知道该点哪儿。
        if (!state.draft.empty()) {
            state.list->select(1);
            fill_form(state, 0);
        }

        center_on(dialog, app_.window, kDialogWidth, kDialogHeight);
        dialog.show();
        apply_title_bar(&dialog);
        // 模态窗口自己转事件循环：这一圈里主窗口收不到输入，退出后才回到本页。
        while (dialog.shown() != 0) Fl::wait();

        if (state.changed) set_status("规则改好了：重新打开程序后生效");
    }

    static void on_toggle_hit_log(Fl_Widget* widget, void* data) {
        auto* self = static_cast<LintPage*>(data);
        self->hit_log_enabled_ = static_cast<Fl_Check_Button*>(widget)->value() != 0;
        ui_config_set("hit_log", self->hit_log_enabled_ ? "1" : "0");
        set_status(self->hit_log_enabled_ ? "检查命中会追加写到：" + self->hit_log_file_
                                          : "不再写检查命中日志");
    }

    static void on_only_rules_clicked(Fl_Widget*, void* data) {
        static_cast<LintPage*>(data)->open_only_rules();
    }

    static void on_edit_rule_clicked(Fl_Widget*, void* data) {
        static_cast<LintPage*>(data)->open_rule_editor();
    }

    AppContext& app_;

    Fl_Box* severity_label_ = nullptr;
    Fl_Choice* severity_ = nullptr;
    Fl_Box* genre_label_ = nullptr;
    Fl_Choice* genre_ = nullptr;
    Fl_Box* note_ = nullptr;
    FlatCheck* hit_log_ = nullptr;
    std::string hit_log_file_;
    bool hit_log_enabled_ = false;
    Fl_Box* checked_ = nullptr;
    Fl_Button* only_rules_ = nullptr;
    Fl_Button* edit_rule_ = nullptr;

    std::vector<std::string> only_rules_ids_;  // 空表示按默认集合
};

}  // namespace

Page* build_lint_page(AppContext& app, int x, int y, int w, int h) {
    auto* page = new LintPage(app, x, y, w, h);
    app.register_page(PageId::Lint, page);
    return page;
}

texttool::LintOptions lint_page_options(AppContext& app) {
    auto* page = static_cast<LintPage*>(app.page(PageId::Lint));
    if (page == nullptr) return texttool::LintOptions{};  // 页还没建出来：按默认那几条查
    return page->options();
}

bool lint_page_report(AppContext& app, const std::string& path, std::string& report,
                      std::string& error) {
    report.clear();
    error.clear();

    const std::string problem = rule_table_problem(app);
    if (!problem.empty()) {
        error = problem;
        return false;
    }

    // 读稿走引擎那一份（探测编码、解成 UTF-8），界面不另写一套。
    std::string text;
    try {
        text = texttool::load_text(path).text;
    } catch (const std::exception& failure) {
        error = failure.what();
        return false;
    }

    try {
        const std::vector<texttool::LintRule>& rules = *app.rules;
        // 一份报告里只编译一次规则：四百多个模式不必每条规则重来一遍。
        const texttool::CompiledRules compiled(rules);
        auto* page = static_cast<LintPage*>(app.page(PageId::Lint));
        const std::string log_path =
            page != nullptr && page->hit_log_enabled() ? page->hit_log_file() : std::string();
        report = render_one(compiled, text, path, lint_page_options(app),
                            texttool::collapse_thresholds(rules), log_path);
    } catch (const std::exception& failure) {
        error = failure.what();
        return false;
    }
    return true;
}

}  // namespace texttool::ui
