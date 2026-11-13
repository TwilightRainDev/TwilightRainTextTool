// shape 组七条算法规则的测试：感叹号密度、显式编号、序号骨架、术语堆叠、比喻堆叠、
// 括号全半角混用、引号样式混用。
//
// 两条线：
//   * 与 fixtures/cli 里七份稿件的 .expected.json 逐字段对照：
//     取本组七条规则的命中与统计附注，比 Id、Line、Col、Length、Match、Detail 与附注原文；
//   * 每条规则自己的门限：正好够与差一点的两侧各一例，钉住「什么时候不报」。
//
// 命中说明不在 AlgoWriter 的入参里，由 algo_shape.cpp 的 shape_pending_detail() 交接，
// 所以取说明这一步必须放在 writer 回调里。
#include <cstddef>
#include <fstream>
#include <sstream>
#include <string>
#include <string_view>
#include <vector>

#include "doctest/doctest.h"
#include "engine/location.h"
#include "engine/rules.h"
#include "engine/scan.h"
#include "json/json.hpp"

namespace texttool {

// algo_shape.cpp 交出来的口子：刚才写的那一处命中该带的说明。
const std::string& shape_pending_detail();

}  // namespace texttool

namespace {

using texttool::AlgoContext;
using texttool::LintRule;

const std::vector<LintRule>& rule_table() {
    static const std::vector<LintRule> rules = texttool::load_rules(TEXTTOOL_RULES_JSON);
    return rules;
}

const LintRule& rule_of(std::string_view id) {
    for (const LintRule& rule : rule_table())
        if (rule.id == id) return rule;
    FAIL("规则表里没有这条规则：", id);
    return rule_table().front();
}

std::string read_file(const std::string& path) {
    std::ifstream in(path, std::ios::binary);
    REQUIRE_MESSAGE(in.good(), "读不到文件：", path);
    std::ostringstream buffer;
    buffer << in.rdbuf();
    return buffer.str();
}

// 稿件与期望输出都在 fixtures/cli 里：同一份样本，命令行那边逐字节比，这里逐字段比。
std::string fixture(const std::string& name) {
    return read_file(std::string(TEXTTOOL_FIXTURES_DIR) + "/cli/" + name);
}

// 一条命中的可对照字段，取值与 .expected.json 里的字段一一对应。
struct Hit {
    std::string id;
    std::size_t line = 0;
    std::size_t column = 0;
    std::size_t length = 0;
    std::string match;   // 命中原文
    std::string detail;  // 命中说明
};

std::string describe(const Hit& hit) {
    std::ostringstream out;
    out << hit.id << " " << hit.line << ":" << hit.column << " 长 " << hit.length << " 「"
        << hit.match << "」「" << hit.detail << "」";
    return out.str();
}

// 一趟运行的收集器：上下文里放收集器与行列换算表，说明文字从交接口子取。
struct Collected {
    std::vector<Hit> hits;
    std::vector<std::string> notes;
};

void collect(std::size_t begin, std::size_t end, const std::string& excerpt, void* context) {
    auto* state = static_cast<std::pair<Collected*, const texttool::LineIndex*>*>(context);
    const texttool::Location location = state->second->locate(begin, end);
    Hit hit;
    hit.line = location.line;
    hit.column = location.column;
    hit.length = location.length;
    hit.match = excerpt;
    hit.detail = texttool::shape_pending_detail();
    state->first->hits.push_back(std::move(hit));
}

Collected run(std::string_view id, std::string_view text) {
    const LintRule& rule = rule_of(id);
    const texttool::LineIndex index(text);
    Collected out;
    std::pair<Collected*, const texttool::LineIndex*> context{&out, &index};
    const AlgoContext algo{text, index.text_units()};
    texttool::run_algorithm(rule, algo, &collect, &context, out.notes);
    for (Hit& hit : out.hits) hit.id = rule.id;
    return out;
}

// 期望输出里属于这七条规则的命中与附注。别的组的规则不在本文件比。
const std::vector<std::string>& shape_ids() {
    static const std::vector<std::string> ids = {
        "exclamation-density", "explicit-numbering", "ordinal-skeleton", "terminology-pile",
        "fiction-simile-pile", "mixed-bracket-width",  "mixed-quote-style",
    };
    return ids;
}

bool is_shape_id(const std::string& id) {
    for (const std::string& item : shape_ids())
        if (item == id) return true;
    return false;
}

struct Golden {
    std::vector<Hit> hits;
    std::vector<std::string> notes;
};

Golden golden(const std::string& name) {
    const std::string text = fixture(name + ".expected.json");
    REQUIRE_MESSAGE(!text.empty(), "基准是空的：", name);
    const nlohmann::json document = nlohmann::json::parse(text);
    const nlohmann::json& report = document.at("Reports").at(0);

    Golden out;
    for (const nlohmann::json& item : report.at("Hits")) {
        const std::string id = item.at("Id").get<std::string>();
        if (!is_shape_id(id)) continue;
        Hit hit;
        hit.id = id;
        hit.line = item.at("Line").get<std::size_t>();
        hit.column = item.at("Col").get<std::size_t>();
        hit.length = item.at("Length").get<std::size_t>();
        hit.match = item.at("Match").get<std::string>();
        hit.detail = item.at("Detail").get<std::string>();
        out.hits.push_back(std::move(hit));
    }
    for (const nlohmann::json& item : report.at("Notes")) {
        const std::string id = item.at("Id").get<std::string>();
        if (!is_shape_id(id)) continue;
        out.notes.push_back(item.at("Text").get<std::string>());
    }
    return out;
}

// 与期望输出逐字段对照：命中按顺序一一对应，附注原文一一对应。
void check_against_golden(const std::string& name) {
    const std::string draft = fixture(name + ".txt");
    const Golden want = golden(name);

    // 一条稿子可能命中本组多条规则；按规则分开跑，再按期望输出里的先后拼起来。
    std::vector<Hit> got;
    std::vector<std::string> got_notes;
    for (const std::string& id : shape_ids()) {
        const Collected one = run(id, draft);
        for (const Hit& hit : one.hits) got.push_back(hit);
        for (const std::string& note : one.notes) got_notes.push_back(note);
    }

    REQUIRE_MESSAGE(got.size() == want.hits.size(),
                    name << " 命中数 " << got.size() << "，基准 " << want.hits.size());
    for (std::size_t i = 0; i < got.size(); ++i)
        CHECK_MESSAGE(describe(got[i]) == describe(want.hits[i]),
                      name << " 第 " << (i + 1) << " 处：拿到 " << describe(got[i]) << "，基准 "
                           << describe(want.hits[i]));
    REQUIRE_MESSAGE(got_notes.size() == want.notes.size(),
                    name << " 附注数 " << got_notes.size() << "，基准 " << want.notes.size());
    for (std::size_t i = 0; i < got_notes.size(); ++i)
        CHECK_MESSAGE(got_notes[i] == want.notes[i],
                      name << " 第 " << (i + 1) << " 条附注：拿到「" << got_notes[i] << "」，基准「"
                           << want.notes[i] << "」");
}

std::string repeat(const std::string& unit, std::size_t times) {
    std::string out;
    for (std::size_t i = 0; i < times; ++i) out += unit;
    return out;
}

}  // namespace

// ---------------------------------------------------------------------------
// 与期望输出逐字段对照
// ---------------------------------------------------------------------------

TEST_CASE("本组七份稿件与对照基准逐字段一致") {
    for (const char* name : {"shape_exclaim", "shape_ordinal", "shape_numbering",
                             "shape_terminology", "shape_simile", "shape_bracket",
                             "shape_quote"})
        check_against_golden(name);
}

TEST_CASE("样本确实命中了本组每一条规则") {
    // 七份稿件各管一条，缺一份就有一条规则没人对照
    const std::vector<std::pair<std::string, std::string>> cases = {
        {"shape_exclaim", "exclamation-density"},
        {"shape_ordinal", "ordinal-skeleton"},
        {"shape_numbering", "explicit-numbering"},
        {"shape_terminology", "terminology-pile"},
        {"shape_simile", "fiction-simile-pile"},
        {"shape_bracket", "mixed-bracket-width"},
        {"shape_quote", "mixed-quote-style"},
    };
    for (const auto& [name, id] : cases) {
        const Collected out = run(id, fixture(name + ".txt"));
        const std::size_t found = out.hits.size() + out.notes.size();
        CHECK_MESSAGE(found > 0, name << " 没有命中 " << id);
    }
}

// ---------------------------------------------------------------------------
// exclamation-density：全文四百字以上、每千字超过三个叹号，报一条统计附注
// ---------------------------------------------------------------------------

TEST_CASE("感叹号密度：四百字以上、每千字超三个才报") {
    const Collected out = run("exclamation-density", fixture("shape_exclaim.txt"));
    CHECK(out.hits.empty());
    REQUIRE(out.notes.size() == 1);
    CHECK(out.notes[0] == "叹号密度偏高：6 个 / 434 字");
}

TEST_CASE("感叹号密度：不到四百字不报") {
    // 399 字、399 个叹号：密度再高也不报，判的是整篇的量
    CHECK(run("exclamation-density", repeat("！", 399)).notes.empty());
}

TEST_CASE("感叹号密度：正好每千字三个不报，多一个才报") {
    const Collected quiet = run("exclamation-density", repeat("他来了。", 250) + repeat("！", 3));
    CHECK(quiet.notes.empty());
    const Collected loud = run("exclamation-density", repeat("他来了。", 250) + repeat("！", 4));
    REQUIRE(loud.notes.size() == 1);
    CHECK(loud.notes[0] == "叹号密度偏高：4 个 / 1004 字");
}

TEST_CASE("感叹号密度：半角叹号不算") {
    CHECK(run("exclamation-density", repeat("他来了。", 250) + repeat("!", 4)).notes.empty());
}

// ---------------------------------------------------------------------------
// ordinal-skeleton：全篇「首先/其次/再次/最后/其一/其二/其三」凑够两处
// ---------------------------------------------------------------------------

TEST_CASE("序号骨架：一处不报，两处才报") {
    CHECK(run("ordinal-skeleton", "首先，把门关上。然后再说话。").notes.empty());
    const Collected two = run("ordinal-skeleton", "首先，把门关上。其次，把窗关上。");
    REQUIRE(two.notes.size() == 1);
    CHECK(two.notes[0] == "序数词骨架「首先/其次/最后」出现 2 次——结构化排版的典型痕迹");
}

TEST_CASE("序号骨架：词表里的七个词都算，别的序数说法不算") {
    const Collected out = run("ordinal-skeleton", "其一甲，其二乙，其三丙，再次丁，最后戊，再次己。");
    REQUIRE(out.notes.size() == 1);
    CHECK(out.notes[0].find("6 次") != std::string::npos);
    // 「第一」「然后」「接着」不在词表里
    CHECK(run("ordinal-skeleton", "第一，甲。然后乙。接着丙。最后丁。").notes.empty());
}

// ---------------------------------------------------------------------------
// explicit-numbering：行首硬编号凑够三处
// ---------------------------------------------------------------------------

TEST_CASE("显式序号：三处才报，两处不报") {
    CHECK(run("explicit-numbering", "1. 甲\n2. 乙\n").notes.empty());
    const Collected three = run("explicit-numbering", "1. 甲\n2. 乙\n3. 丙\n");
    REQUIRE(three.notes.size() == 1);
    CHECK(three.notes[0] == "显式序号标记 3 处——结构化排版的典型痕迹");
}

TEST_CASE("显式序号：认哪些行首写法") {
    // 认：1. / 1、 / 1) / （1）/ (1) / 一、 / 前面带空格
    const Collected punctuation =
        run("explicit-numbering", "1. 甲\n2、乙\n3) 丙\n");
    REQUIRE(punctuation.notes.size() == 1);
    CHECK(punctuation.notes[0].find("3 处") != std::string::npos);
    const Collected brackets = run("explicit-numbering", "（1）甲\n(2)乙\n三、丙\n");
    REQUIRE(brackets.notes.size() == 1);
    CHECK(brackets.notes[0].find("3 处") != std::string::npos);
    const Collected indented = run("explicit-numbering", "  1. 甲\n\t2. 乙\n    3. 丙\n");
    REQUIRE(indented.notes.size() == 1);
    CHECK(indented.notes[0].find("3 处") != std::string::npos);

    // 不认：行中的编号、三位数编号、带圈数字
    CHECK(run("explicit-numbering", "见 1. 甲\n见 2. 乙\n见 3. 丙\n").notes.empty());
    CHECK(run("explicit-numbering", "123. 甲\n456. 乙\n789. 丙\n").notes.empty());
    CHECK(run("explicit-numbering", "①甲\n②乙\n③丙\n").notes.empty());
}

// ---------------------------------------------------------------------------
// terminology-pile：挖掉引号内的文字之后，每千字五处
// ---------------------------------------------------------------------------

TEST_CASE("术语堆叠：引号里的词不算") {
    // 引号里的「降维」「密码学」被挖掉，剩下的「褶皱」十二处照报
    const std::string draft = "他说「降维之后，密码学还有什么用」，说了两遍。"
                              + repeat("褶皱在布上，布就还是一块布。", 12);
    const Collected out = run("terminology-pile", draft);
    REQUIRE(out.hits.size() == 12);
    for (const Hit& hit : out.hits) CHECK(hit.match == "褶皱");
}

TEST_CASE("术语堆叠：密度不到每千字五处，一处都不报") {
    const std::string filler = repeat("他推门出去，站在街上看了一会儿。", 120);
    CHECK(run("terminology-pile", repeat("褶皱", 9) + filler).hits.empty());
    CHECK(run("terminology-pile", repeat("褶皱", 10) + filler).hits.size() == 10);
}

// ---------------------------------------------------------------------------
// fiction-simile-pile：一句六十字以上串了三个比喻，且全篇密度够
// ---------------------------------------------------------------------------

TEST_CASE("比喻堆叠：短句里连用比喻不报") {
    CHECK(run("fiction-simile-pile", "她像猫，仿佛鸟，犹如鱼。" + repeat("风停了。", 20)).hits.empty());
}

TEST_CASE("比喻堆叠：长句里只有两个比喻不报") {
    const Collected out = run("fiction-simile-pile",
                              "她站在候车棚下看着雨顺着棚沿往下淌，风把围巾吹得贴住脸颊像一条醒过来"
                              "的蛇，远处的电线在暮色里一根一根绷紧，仿佛有人在半空拨弦，水洼里积着"
                              "碎玻璃一样的云。");
    CHECK(out.hits.empty());
}

TEST_CASE("比喻堆叠：全篇密度不到每千字一处，整条不报") {
    const std::string candidate =
        "她站在候车棚下看着雨顺着棚沿往下淌，风把围巾吹得贴住脸颊像一条醒过来的蛇，"
        "远处的电线在暮色里一根一根绷紧仿佛有人在半空拨弦，水洼里积着碎玻璃一样的云犹如打翻的镜子。";
    CHECK(run("fiction-simile-pile", candidate + repeat("风停了，他推门出去。", 250)).hits.empty());
    CHECK(run("fiction-simile-pile", candidate + repeat("风停了。", 100)).hits.size() == 1);
}

TEST_CASE("比喻堆叠：句首带空白时，位置取整句起点，命中去掉空白") {
    // 上一句收在句号，这一句前面多了一个空格。「位置」与「命中原文」分开记：
    // 列号落在空格上，Match 从第一个非空白字起。
    const std::string draft =
        "她走了。 她站在候车棚下，风把围巾吹得贴住脸颊像一条刚刚醒过来的蛇，远处的电线在暮色里"
        "一根一根绷紧仿佛有人在半空拨弦，水洼里积着碎玻璃一样的云犹如打翻的镜子。";
    const Collected out = run("fiction-simile-pile", draft);
    REQUIRE(out.hits.size() == 1);
    CHECK(out.hits[0].column == 5);
    CHECK(out.hits[0].length == 30);
    CHECK(out.hits[0].match == "她站在候车棚下，风把围巾吹得贴住脸颊像一条刚刚醒过来的蛇，远");
    CHECK(out.hits[0].detail == "这一句 73 字里串了 3 个比喻——读者拿到的是一串喻体，"
                               "不是一个画面；真人长篇里这种句子每千字不超过 0.08 句");
}

TEST_CASE("比喻堆叠：表情符号按两个码元算长度") {
    // 基本平面以外的字占两个下标，报出去的长度与字数都按这条算
    const std::string draft =
        "她站在候车棚下😀，风把围巾吹得贴住脸颊像一条刚刚醒过来的蛇，远处的电线在暮色里一根"
        "一根绷紧仿佛有人在半空拨弦，水洼里积着碎玻璃一样的云犹如打翻的镜子。";
    const Collected out = run("fiction-simile-pile", draft);
    REQUIRE(out.hits.size() == 1);
    CHECK(out.hits[0].column == 1);
    CHECK(out.hits[0].length == 30);
    CHECK(out.hits[0].match == "她站在候车棚下😀，风把围巾吹得贴住脸颊像一条刚刚醒过来的蛇");
    CHECK(out.hits[0].detail.find("这一句 75 字里串了 3 个比喻") == 0);
}

// ---------------------------------------------------------------------------
// mixed-bracket-width：圆括号全半角都出现过才报
// ---------------------------------------------------------------------------

TEST_CASE("括号全半角混用：只用一种不报") {
    CHECK(run("mixed-bracket-width", "排序规则（按优先级）写在下面。").hits.empty());
    CHECK(run("mixed-bracket-width", "排序规则(按优先级)写在下面。").hits.empty());
    // 报的位置是全文最先出现的那个括号，不论全半角
    const Collected out = run("mixed-bracket-width", "先(半角)，后（全角）。");
    REQUIRE(out.hits.size() == 1);
    CHECK(out.hits[0].match == "(");
    // 数的是括号字符，不是括号对
    CHECK(out.hits[0].detail == "半角 2 个 / 全角 2 个");
}

TEST_CASE("括号全半角混用：方头括号与书名号不算圆括号") {
    CHECK(run("mixed-bracket-width", "【提示】见《手册》（一节）。").hits.empty());
}

// ---------------------------------------------------------------------------
// mixed-quote-style：按「套」判混用，同一套里嵌另一种不算
// ---------------------------------------------------------------------------

TEST_CASE("引号样式混用：同一套引号里嵌单引号不算混用") {
    CHECK(run("mixed-quote-style", "他说“她喊了一声‘快来’就走了”。").hits.empty());
    CHECK(run("mixed-quote-style", "他说「她喊了一声『快来』就走了」。").hits.empty());
}

TEST_CASE("引号样式混用：各套的用量按固定次序写进说明") {
    const Collected out = run("mixed-quote-style", "「甲」『乙』\"丙\"“丁”");
    REQUIRE(out.hits.size() == 1);
    CHECK(out.hits[0].column == 1);
    CHECK(out.hits[0].detail ==
          "弯引号 2 个 / ASCII 直引号 2 个 / 直角引号 2 个 / 双直角引号 2 个");
}
