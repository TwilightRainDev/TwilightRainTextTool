// rhythm 组五条算法规则（句长均匀、短句连出、篇幅节奏、碎句、段落长短均匀）的测试。
//
// 期望值按判据写死：同一份规则表、同一段文本，统计附注与 choppy-clauses 的命中（行列、
// 长度、命中原文、说明）都是逐个对过的值。那四条规则只出统计附注，这里就只比附注；
// 只有 choppy-clauses 报命中，比的是命中位置与命中原文。
//
// 每条规则都带正例、反例与门限两侧的样本：门限写成常量，改一个数字这里就该红。
#include <cstddef>
#include <string>
#include <string_view>
#include <vector>

#include "doctest/doctest.h"
#include "engine/rules.h"
#include "engine/scan.h"

namespace {

using texttool::AlgoContext;
using texttool::LintRule;

// 规则表只读一次：每条测试都重新读盘，测到的就不只是算法了。
const std::vector<LintRule>& rule_table() {
    static const std::vector<LintRule> rules = texttool::load_rules(TEXTTOOL_RULES_JSON);
    return rules;
}

const LintRule& rule_of(const std::string& id) {
    for (const LintRule& rule : rule_table()) {
        if (rule.id == id) return rule;
    }
    FAIL("规则表里没有这条规则：" << id);
    return rule_table().front();
}

std::string repeat(const std::string& unit, std::size_t times) {
    std::string out;
    for (std::size_t i = 0; i < times; ++i) out += unit;
    return out;
}

// 段落之间用空行隔开。
std::string join_paragraphs(const std::vector<std::string>& paragraphs) {
    std::string out;
    for (std::size_t i = 0; i < paragraphs.size(); ++i) {
        if (i > 0) out += "\n\n";
        out += paragraphs[i];
    }
    out += "\n";
    return out;
}

struct AlgoHit {
    std::size_t begin = 0;  // 字节偏移
    std::size_t end = 0;
    std::string excerpt;
};

struct AlgoResult {
    std::vector<AlgoHit> hits;
    std::vector<std::string> notes;
};

void collect_hit(std::size_t begin, std::size_t end, const std::string& excerpt, void* context) {
    static_cast<AlgoResult*>(context)->hits.push_back({begin, end, excerpt});
}

AlgoResult run(const std::string& id, const std::string& text) {
    AlgoResult result;
    const AlgoContext context{text, texttool::unit_length(text, 0, text.size())};
    texttool::run_algorithm(rule_of(id), context, &collect_hit, &result, result.notes);
    return result;
}

void check_notes(const std::vector<std::string>& actual, const std::vector<std::string>& expected) {
    REQUIRE(actual.size() == expected.size());
    for (std::size_t i = 0; i < expected.size(); ++i) {
        CHECK_MESSAGE(actual[i] == expected[i], ("第 " + std::to_string(i) + " 条附注，本地「" +
                                                 actual[i] + "」，对照基准「" + expected[i] + "」"));
    }
}

// ---------------------------------------------------------------- 样本

// 连着的短句：一句一个动作，每句都在十个字以内。
const std::string kShortRun = "他走了。她也走了。天亮了。门开着。\n";
// 十二句一模一样长：句长分布毫无参差。
const std::string kSameLength12 = repeat("他看了她一眼。", 12) + "\n";
// 五段一模一样长：段落长短毫无参差。
const std::string kSameParagraph =
    "今天他把手里的活干完了。夜里下过一场小雨，天亮时地上的水已经干了。";
const std::string kSameParagraph5 = join_paragraphs(std::vector<std::string>(5, kSameParagraph));
// 八百字以上全是九字句：整篇按节拍往下砸。
const std::string kTempo = repeat("他走进屋里看了看。", 178) + "\n";
// 分号加逗号硬接的八个短分句。
const std::string kChoppy = "他起身；穿衣，洗脸，拿包，出门，下楼，买饭，上车。\n";

}  // namespace

// ---------------------------------------------------------------- 短句连出

TEST_CASE("short-sentence-run：连着四句都在十个字以内就出附注") {
    check_notes(run("short-sentence-run", kShortRun).notes,
                {"连续 4 句都在 10 字以内——节拍化的碎片句，像在逐条填写动作与情绪"});
}

TEST_CASE("short-sentence-run：只连着三句不报") {
    check_notes(run("short-sentence-run", "他走了。她也走了。天亮了。\n").notes, {});
}

TEST_CASE("short-sentence-run：省略号每个字都断一句，句数按对照基准数") {
    // 切句按每个句末标点切，「……让我看见」是两句：三句短句 + 两句 = 连出五句。
    // 本地切句若把连续省略号收进上一句，这里只会数到四句。
    check_notes(run("short-sentence-run", "他走了。她也走了。天亮了。他说……\n").notes,
                {"连续 5 句都在 10 字以内——节拍化的碎片句，像在逐条填写动作与情绪"});
}

TEST_CASE("short-sentence-run：句子超过十个字就重新计数") {
    // 六句短句、一句十六字、再六句短句：最长的连续段是六句。
    const std::string text = repeat("他说完了吗。", 6) +
                             repeat("他站在门口看着远处那条河的亮光。", 1) + repeat("他说完了吗。", 6) +
                             "\n";
    check_notes(run("short-sentence-run", text).notes,
                {"连续 6 句都在 10 字以内——节拍化的碎片句，像在逐条填写动作与情绪"});
}

TEST_CASE("short-sentence-run：篇幅节奏报过的稿子不再重复报同一个现象") {
    // 「节拍已报过」要传下去：同一段短句在两条规则里各报一次就是刷屏。
    const AlgoResult tempo = run("sentence-tempo", kTempo);
    REQUIRE(tempo.notes.size() == 1);
    check_notes(run("short-sentence-run", kTempo).notes, {});
}

TEST_CASE("short-sentence-run：篇幅节奏没报的稿子自己照报") {
    // 不足四百字，篇幅节奏整条不判，连续短句照报。
    const std::string text = repeat("他走进屋里看了看。", 40) + "\n";
    CHECK(run("sentence-tempo", text).notes.empty());
    check_notes(run("short-sentence-run", text).notes,
                {"连续 40 句都在 10 字以内——节拍化的碎片句，像在逐条填写动作与情绪"});
}

// ---------------------------------------------------------------- 句长均匀

TEST_CASE("sentence-length-uniform：十句以上、变异系数低于 0.35 就出附注") {
    check_notes(run("sentence-length-uniform", kSameLength12).notes, {"句长分布过于整齐（变异系数 0.00，12 句）"});
}

TEST_CASE("sentence-length-uniform：变异系数 0.33 仍在门限内") {
    // 六字句与十二字句各五句：平均九字、标准差三字，变异系数 0.33。
    const std::string text = repeat("他说完了吗。", 5) + repeat("他站在门口看着远处的河。", 5) + "\n";
    check_notes(run("sentence-length-uniform", text).notes, {"句长分布过于整齐（变异系数 0.33，10 句）"});
}

TEST_CASE("sentence-length-uniform：变异系数高过 0.35 不报") {
    const std::string text = repeat("他说完了吗。", 5) + repeat("他站在门口看着远处那条河的亮光。", 5) + "\n";
    check_notes(run("sentence-length-uniform", text).notes, {});
}

TEST_CASE("sentence-length-uniform：不足十句不判") {
    check_notes(run("sentence-length-uniform", repeat("他说完了吗。", 9) + "\n").notes, {});
}

TEST_CASE("sentence-length-uniform：不足六个字的句子不计入") {
    // 十句四字句，另有一句七字句：计入的只有那一句，句数不足十句。
    CHECK(run("sentence-length-uniform", repeat("他走了。", 10) + "他说完了吗。\n").notes.empty());
}

// ---------------------------------------------------------------- 篇幅节奏

TEST_CASE("sentence-tempo：八百字一窗、步长四百字，连着成立只报一次") {
    // 全文八百字以上，窗口起点是 0、400、800，三窗都成立，只出第一条附注。
    // 行号写起点所在行的下一行（Locate 已是一起数，再加一）。
    check_notes(run("sentence-tempo", kTempo).notes,
                {"第 2 行起这一窗 89 句，平均 9 字，最长 9 字——一句都没说长，整段按节拍往下砸"});
}

TEST_CASE("sentence-tempo：平均句长保留一位小数") {
    const std::string text =
        repeat("他走进屋里看了她一眼。" + std::string("他走进屋里看了看她一眼。"), 20) + "\n";
    check_notes(run("sentence-tempo", text).notes,
                {"第 2 行起这一窗 40 句，平均 11.5 字，最长 12 字——一句都没说长，整段按节拍往下砸"});
}

TEST_CASE("sentence-tempo：首窗被长句否掉，第二窗照报并给出它自己的行号") {
    const std::string lead = "他站在门口看着远处那条河慢慢亮起来的天色和云影，一直站到太阳把屋顶晒热。";
    const std::string text = lead + "\n" + repeat("他走进屋里看了看。", 130) + "\n";
    check_notes(run("sentence-tempo", text).notes,
                {"第 3 行起这一窗 89 句，平均 9 字，最长 9 字——一句都没说长，整段按节拍往下砸"});
}

TEST_CASE("sentence-tempo：不足四百字整条不判") {
    CHECK(run("sentence-tempo", repeat("他走进屋里看了看。", 40) + "\n").notes.empty());
}

TEST_CASE("sentence-tempo：不足六字的句子不计句数") {
    // 四百字里全是一句四字，计入的句数是零。
    CHECK(run("sentence-tempo", repeat("他走了。", 100) + "\n").notes.empty());
}

TEST_CASE("sentence-tempo：平均句长到十五字不再算节拍") {
    CHECK(run("sentence-tempo", repeat("他走进屋里看了看她现在的样子呀。", 26) + "\n").notes.empty());
}

TEST_CASE("sentence-tempo：有一句超过二十八字就不算整段短句") {
    const std::string text = repeat("他走进屋里看了看。", 40) +
                             "他站在门口看着远处那条河慢慢亮起来的天色和云影，一直站到太阳把屋顶晒热。" +
                             repeat("他走进屋里看了看。", 40) + "\n";
    CHECK(run("sentence-tempo", text).notes.empty());
}

// ---------------------------------------------------------------- 段落长短均匀

TEST_CASE("paragraph-length-uniform：五段以上、变异系数低于 0.25 就出附注") {
    check_notes(run("paragraph-length-uniform", kSameParagraph5).notes,
                {"段落长度过于均一（变异系数 0.00，5 段）——人写文本段落长短通常更参差"});
}

TEST_CASE("paragraph-length-uniform：非零变异系数按两位小数给出") {
    const std::vector<std::string> paragraphs = {repeat("他今天把活干完了。", 1),
                                                 repeat("他今天把活干完了。", 1),
                                                 repeat("他今天把活干完了。", 1),
                                                 repeat("他今天把活干完了。", 1),
                                                 "他今天把活干完了，天黑了。"};
    check_notes(run("paragraph-length-uniform", join_paragraphs(paragraphs)).notes,
                {"段落长度过于均一（变异系数 0.16，5 段）——人写文本段落长短通常更参差"});
}

TEST_CASE("paragraph-length-uniform：不足五段不判") {
    check_notes(run("paragraph-length-uniform", join_paragraphs(std::vector<std::string>(4, kSameParagraph))).notes,
                {});
}

TEST_CASE("paragraph-length-uniform：段落长短参差不报") {
    const std::vector<std::string> paragraphs = {"今天他干完了活。", kSameParagraph, "天黑了。",
                                                 kSameParagraph, kSameParagraph};
    check_notes(run("paragraph-length-uniform", join_paragraphs(paragraphs)).notes, {});
}

// ---------------------------------------------------------------- 碎句

TEST_CASE("choppy-clauses：八个短分句靠分号硬接就报一处") {
    const AlgoResult result = run("choppy-clauses", kChoppy);
    CHECK(result.notes.empty());
    REQUIRE(result.hits.size() == 1);
    // 命中是段落起点到前三十字（这一段不足三十字，就是整段），不含行尾换行。
    CHECK(result.hits[0].begin == 0);
    CHECK(result.hits[0].end == 75);
    CHECK(result.hits[0].excerpt == "他起身；穿衣，洗脸，拿包，出门，下楼，买饭，上车。");
}

TEST_CASE("choppy-clauses：破折号硬接同样算") {
    const AlgoResult result = run("choppy-clauses", "他起身——穿衣，洗脸，拿包，出门，下楼，买饭，上车。\n");
    REQUIRE(result.hits.size() == 1);
    CHECK(result.hits[0].excerpt == "他起身——穿衣，洗脸，拿包，出门，下楼，买饭，上车。");
}

TEST_CASE("choppy-clauses：命中长度封顶三十字") {
    const std::string text =
        "他起身；穿衣去，洗脸去，拿包去，出门去，下楼去，买饭去，上车去。\n";
    const AlgoResult result = run("choppy-clauses", text);
    REQUIRE(result.hits.size() == 1);
    CHECK(result.hits[0].excerpt == "他起身；穿衣去，洗脸去，拿包去，出门去，下楼去，买饭去，上车");
}

TEST_CASE("choppy-clauses：不足八个分句不报") {
    CHECK(run("choppy-clauses", "他起身；穿衣，洗脸，拿包，出门，下楼，买饭。\n").hits.empty());
}

TEST_CASE("choppy-clauses：平均分句长过五字不报") {
    CHECK(run("choppy-clauses",
              "他起身走过去啊；穿衣走过去啊，洗脸走过去啊，拿包走过去啊，出门走过去啊，下楼走过去啊，"
              "买饭走过去啊，上车走过去啊。\n")
              .hits.empty());
}

TEST_CASE("choppy-clauses：没有分号也没有破折号的段落不判") {
    CHECK(run("choppy-clauses", "他起身，穿衣，洗脸，拿包，出门，下楼，买饭，上车。\n").hits.empty());
}

TEST_CASE("choppy-clauses：段内有序数词骨架的不判") {
    // 「首先、其次、一、二、三」是结构，不是把话切碎了硬接。
    CHECK(run("choppy-clauses", "首先；穿衣，洗脸，拿包，出门，下楼，买饭，上车。\n").hits.empty());
}

TEST_CASE("choppy-clauses：段落分开算，命中落在所在段落起点") {
    const std::string text = "第一段写完了。\n\n他起身；穿衣，洗脸，拿包，出门，下楼，买饭，上车。\n";
    const AlgoResult result = run("choppy-clauses", text);
    REQUIRE(result.hits.size() == 1);
    CHECK(result.hits[0].begin == std::string("第一段写完了。\n\n").size());
    CHECK(result.hits[0].excerpt == "他起身；穿衣，洗脸，拿包，出门，下楼，买饭，上车。");
}
