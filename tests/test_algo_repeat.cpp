// repeat 组三条算法规则的测试：逐字重复、复读串、段首重复。
//
// 两条线：
//   * 与 fixtures/cli 下的 .expected.json 逐字段对照，稿件与期望输出都在那里；
//   * 每条规则自己的命中规则：门槛、同一段里只报一次、并段、哪些串不算复读。
//
// 对照取的字段是 Id、Line、Col、Length、Match、Detail 与统计附注的原文。Snippet 带上下文，
// 由渲染层切，不归算法层，这里不比。命中的说明文字不在 AlgoWriter 的入参里，
// 算法把它放在 repeat_pending_detail() 里，所以要在 writer 回调里取：本文件就是这么取的。
#include "doctest/doctest.h"

#include <algorithm>
#include <cstddef>
#include <fstream>
#include <sstream>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

#include "engine/location.h"
#include "engine/rules.h"
#include "engine/scan.h"
#include "json/json.hpp"

namespace texttool {

// algo_repeat.cpp 交给接线层的两个口子：本次命中的说明、本轮复读串的字节范围。
const std::string& repeat_pending_detail();
std::vector<Span> repeat_token_run_spans();

}  // namespace texttool

namespace {

using texttool::AlgoContext;
using texttool::LineIndex;
using texttool::LintRule;
using texttool::Location;
using texttool::Span;

std::string read_file(const std::string& path) {
    std::ifstream in(path, std::ios::binary);
    REQUIRE_MESSAGE(in.good(), "读不到文件：" << path);
    std::ostringstream buffer;
    buffer << in.rdbuf();
    return buffer.str();
}

const std::vector<LintRule>& rule_table() {
    static const std::vector<LintRule> rules = texttool::load_rules(TEXTTOOL_RULES_JSON);
    return rules;
}

const LintRule& rule_by_id(const std::string& id) {
    for (const LintRule& rule : rule_table())
        if (rule.id == id) return rule;
    FAIL("规则表里没有这条规则：" << id);
    return rule_table().front();
}

// 一条命中的可对照字段，字段名与 .expected.json 里的字段一一对应。
struct Hit {
    std::string id;
    std::string group;
    std::string title;
    std::string severity;
    std::size_t line = 0;
    std::size_t column = 0;
    std::size_t length = 0;
    std::string match;   // 命中原文
    std::string detail;  // 命中说明
};

std::string describe(const Hit& hit) {
    std::ostringstream out;
    out << hit.id << " " << hit.line << ":" << hit.column << " 长 " << hit.length << " 「" << hit.match
        << "」「" << hit.detail << "」";
    return out.str();
}

struct Collected {
    std::vector<Hit> hits;
    std::vector<std::string> notes;
};

// writer 回调只有一份上下文指针，规则与行首下标表跟着它一起传。
struct WriterContext {
    const LintRule* rule = nullptr;
    const LineIndex* index = nullptr;
    Collected* collected = nullptr;
};

void collect(std::size_t begin, std::size_t end, const std::string& excerpt, void* context) {
    auto* writer_context = static_cast<WriterContext*>(context);
    const Location location = writer_context->index->locate(begin, end);

    Hit hit;
    hit.id = writer_context->rule->id;
    hit.group = writer_context->rule->group;
    hit.title = writer_context->rule->title;
    hit.severity = writer_context->rule->severity;
    hit.line = location.line;
    hit.column = location.column;
    hit.length = location.length;
    hit.match = excerpt;
    hit.detail = texttool::repeat_pending_detail();
    writer_context->collected->hits.push_back(std::move(hit));
}

// 这份稿按哪种方式切段：前 4096 字里空行占行数一成以上就是 markdown。
// 本组三条规则吃的是切好的段，markdown 的分段归接线层，测试只跑逐行分段的稿件。
bool is_line_mode(std::string text) {
    if (text.size() > 4096) text = text.substr(0, 4096);
    if (!text.empty() && text.back() == '\n') text.pop_back();
    std::size_t lines = 0;
    std::size_t blanks = 0;
    std::size_t start = 0;
    for (std::size_t at = 0; at <= text.size(); ++at) {
        if (at < text.size() && text[at] != '\n') continue;
        ++lines;
        bool blank = true;
        for (std::size_t i = start; i < at; ++i)
            if (text[i] != ' ' && text[i] != '\t' && text[i] != '\r') blank = false;
        if (blank) ++blanks;
        start = at + 1;
    }
    return !(lines > 0 && blanks < lines && static_cast<double>(blanks) / static_cast<double>(lines) >= 0.10);
}

// 跑一条算法规则。整表是按 (行, 列) 稳定排一次的，排序归调用方，
// 这里照同一规则排，单条规则的命中次序才与报告里一致。
Collected scan(const std::string& rule_id, std::string_view text) {
    const LintRule& rule = rule_by_id(rule_id);
    const LineIndex index(text);
    const AlgoContext algo_context{text, index.text_units()};

    Collected collected;
    WriterContext writer_context{&rule, &index, &collected};
    texttool::run_algorithm(rule, algo_context, collect, &writer_context, collected.notes);

    std::stable_sort(collected.hits.begin(), collected.hits.end(), [](const Hit& a, const Hit& b) {
        if (a.line != b.line) return a.line < b.line;
        return a.column < b.column;
    });
    return collected;
}

Hit to_hit(const nlohmann::json& node) {
    Hit hit;
    hit.id = node.at("Id").get<std::string>();
    hit.group = node.at("Group").get<std::string>();
    hit.title = node.at("Title").get<std::string>();
    hit.severity = node.at("Severity").get<std::string>();
    hit.line = node.at("Line").get<std::size_t>();
    hit.column = node.at("Col").get<std::size_t>();
    hit.length = node.at("Length").get<std::size_t>();
    hit.match = node.at("Match").get<std::string>();
    hit.detail = node.at("Detail").get<std::string>();
    return hit;
}

}  // namespace

TEST_CASE("三条规则与对照基准的 --json 报告逐字段一致") {
    const std::vector<std::string> fixtures = {"repeat_verbatim", "repeat_token", "repeat_opening"};
    const std::vector<std::string> rules = {"repeated-verbatim", "repeated-token-run",
                                            "repeated-paragraph-opening"};
    const std::string dir = std::string(TEXTTOOL_FIXTURES_DIR) + "/cli/";

    for (const std::string& fixture : fixtures) {
        CAPTURE(fixture);
        const std::string text = read_file(dir + fixture + ".txt");
        const nlohmann::json expected =
            nlohmann::json::parse(read_file(dir + fixture + ".expected.json"));
        const nlohmann::json& report = expected.at("Reports").at(0);

        for (const std::string& rule_id : rules) {
            CAPTURE(rule_id);

            std::vector<Hit> want_hits;
            for (const nlohmann::json& node : report.at("Hits"))
                if (node.at("Id").get<std::string>() == rule_id) want_hits.push_back(to_hit(node));

            std::vector<std::string> want_notes;
            for (const nlohmann::json& node : report.at("Notes"))
                if (node.at("Id").get<std::string>() == rule_id)
                    want_notes.push_back(node.at("Text").get<std::string>());

            const Collected got = scan(rule_id, text);

            REQUIRE_MESSAGE(got.hits.size() == want_hits.size(),
                            "基准 " << want_hits.size() << " 处，实得 " << got.hits.size() << " 处");
            for (std::size_t i = 0; i < want_hits.size(); ++i) {
                CAPTURE(i);
                CAPTURE(describe(got.hits[i]));
                CHECK(got.hits[i].id == want_hits[i].id);
                CHECK(got.hits[i].group == want_hits[i].group);
                CHECK(got.hits[i].title == want_hits[i].title);
                CHECK(got.hits[i].severity == want_hits[i].severity);
                CHECK(got.hits[i].line == want_hits[i].line);
                CHECK(got.hits[i].column == want_hits[i].column);
                CHECK(got.hits[i].length == want_hits[i].length);
                CHECK(got.hits[i].match == want_hits[i].match);
                CHECK(got.hits[i].detail == want_hits[i].detail);
            }
            CHECK(got.notes == want_notes);
        }
    }
}

TEST_CASE("现有对照稿件上，三条规则与对照基准的 --json 报告逐字段一致") {
    // 逐份稿件跑一遍：期望输出里的命中按规则过滤后与本组算出来的比。
    // 只收逐行分段的稿件（每行一段）；markdown 的分段要按空行与块另切，不在本组。
    struct Fixture {
        std::string dir;
        std::string name;
    };
    const std::vector<Fixture> fixtures = {
        {"cli", "clean"},   {"cli", "gbk"},  {"cli", "long"},
        {"cli", "short"},   {"report", "mixed"}, {"report", "mixed_severity"},
        {"report", "escaping"}, {"report", "exotic_codepoints"}, {"report", "crlf"},
        {"report", "collapse"},
    };
    const std::vector<std::string> rules = {"repeated-verbatim", "repeated-token-run",
                                            "repeated-paragraph-opening"};

    for (const Fixture& fixture : fixtures) {
        CAPTURE(fixture.dir);
        CAPTURE(fixture.name);
        const std::string dir = std::string(TEXTTOOL_FIXTURES_DIR) + "/" + fixture.dir + "/";
        const std::string text = read_file(dir + fixture.name + ".txt");
        // gbk 那份稿件是 GBK 字节，要先按 GBK 解码再算位置，这份不比位置。
        if (fixture.name == "gbk") continue;
        if (!is_line_mode(text)) continue;
        const nlohmann::json expected =
            nlohmann::json::parse(read_file(dir + fixture.name + ".expected.json"));
        const nlohmann::json& report = expected.at("Reports").at(0);

        for (const std::string& rule_id : rules) {
            CAPTURE(rule_id);
            std::vector<Hit> want_hits;
            for (const nlohmann::json& node : report.at("Hits"))
                if (node.at("Id").get<std::string>() == rule_id) want_hits.push_back(to_hit(node));
            std::vector<std::string> want_notes;
            for (const nlohmann::json& node : report.at("Notes"))
                if (node.at("Id").get<std::string>() == rule_id)
                    want_notes.push_back(node.at("Text").get<std::string>());

            const Collected got = scan(rule_id, text);
            REQUIRE_MESSAGE(got.hits.size() == want_hits.size(),
                            "基准 " << want_hits.size() << " 处，实得 " << got.hits.size() << " 处");
            for (std::size_t i = 0; i < want_hits.size(); ++i) {
                CAPTURE(i);
                CAPTURE(describe(got.hits[i]));
                CHECK(got.hits[i].line == want_hits[i].line);
                CHECK(got.hits[i].column == want_hits[i].column);
                CHECK(got.hits[i].length == want_hits[i].length);
                CHECK(got.hits[i].match == want_hits[i].match);
                CHECK(got.hits[i].detail == want_hits[i].detail);
            }
            CHECK(got.notes == want_notes);
        }
    }
}

TEST_CASE("逐字重复：段落重复只报后续出现，说明指向首现") {
    const std::string line = "这段文字整段贴了三遍是为了看逐字重复报在哪里。";
    const std::string text = line + "\n" + line + "\n" + line + "\n";

    const Collected got = scan("repeated-verbatim", text);
    REQUIRE(got.hits.size() == 2);
    CHECK(got.hits[0].line == 2);
    CHECK(got.hits[1].line == 3);
    for (const Hit& hit : got.hits) {
        CHECK(hit.length == 23);
        CHECK(hit.match == line);
        CHECK(hit.detail == "与第 1 行第 1 列逐字相同");
    }
    CHECK(got.notes.empty());
}

TEST_CASE("逐字重复：整段重复时不再逐句报") {
    // 一段两句、整段重复：报段一处，段里的两句不再各报一处。
    const std::string paragraph = "第一句话要写够十二个字。第二句话也要写够十二个字。";
    const Collected got = scan("repeated-verbatim", paragraph + "\n" + paragraph + "\n");

    REQUIRE(got.hits.size() == 1);
    CHECK(got.hits[0].line == 2);
    CHECK(got.hits[0].column == 1);
    CHECK(got.hits[0].length == 25);
    CHECK(got.hits[0].detail == "与第 1 行第 1 列逐字相同");
}

TEST_CASE("逐字重复：连着贴回的几句并成一段") {
    // 后一段是前一段末尾两句的原样复制：三句报一处，说明里给句数与源头位置。
    const std::string text =
        "开头先交代一句不同的话。甲句讲的是同一件事情的经过。乙句讲的是另外一件事情的前因。\n"
        "甲句讲的是同一件事情的经过。乙句讲的是另外一件事情的前因。\n";

    const Collected got = scan("repeated-verbatim", text);
    REQUIRE(got.hits.size() == 1);
    CHECK(got.hits[0].line == 2);
    CHECK(got.hits[0].column == 1);
    CHECK(got.hits[0].length == 29);
    CHECK(got.hits[0].detail == "从这一句起共 2 句与第 1 行第 13 列起逐字相同");
}

TEST_CASE("逐字重复：两处相距对不上就不并段") {
    // 复制的两句之间插了一句别的：后一句的间距与它在源头那边的间距对不上，各报一处。
    const std::string text =
        "丙句单独出现一次说明情况的起因。\n"
        "丁句另起一段接着说别的。\n"
        "丙句单独出现一次说明情况的起因。\n"
        "再补一句把间距拉开一点的内容。\n"
        "丁句另起一段接着说别的。\n";

    const Collected got = scan("repeated-verbatim", text);
    REQUIRE(got.hits.size() == 2);
    CHECK(got.hits[0].line == 3);
    CHECK(got.hits[0].length == 16);
    CHECK(got.hits[0].detail == "与第 1 行第 1 列逐字相同");
    CHECK(got.hits[1].line == 5);
    CHECK(got.hits[1].length == 12);
    CHECK(got.hits[1].detail == "与第 2 行第 1 列逐字相同");
}

TEST_CASE("逐字重复：段落门槛按字算，不到二十字的整行按句子口径报") {
    // 段与句各有一条门槛：段 20 字、句 12 字。这一行 16 字，按段落不算，按句子算。
    const std::string line = "这一行只有十八个字刚好差两个字。";
    const Collected got = scan("repeated-verbatim", line + "\n" + line + "\n");
    REQUIRE(got.hits.size() == 1);
    CHECK(got.hits[0].line == 2);
    CHECK(got.hits[0].detail == "与第 1 行第 1 列逐字相同");
}

TEST_CASE("逐字重复：句子门槛按字算，差一个字就不报") {
    // 句门槛 12 字：11 字的一句重复不报，12 字的报。
    const std::string eleven = "十一个字的一句重复了。";   // 11 字
    const std::string twelve = "十二个字的一句话重复了。";  // 12 字
    const std::string other = "别的段落写点别的事情吧。";
    CHECK(scan("repeated-verbatim", eleven + "\n" + other + "\n" + eleven + "\n").hits.empty());

    const Collected got = scan("repeated-verbatim", twelve + "\n" + other + "\n" + twelve + "\n");
    REQUIRE(got.hits.size() == 1);
    CHECK(got.hits[0].line == 3);
    CHECK(got.hits[0].length == 12);
}

TEST_CASE("复读串：同一单元连写四遍报一处，报整串") {
    const Collected got = scan("repeated-token-run", "这个方案极其极其极其极其极其极其难维护。");
    REQUIRE(got.hits.size() == 1);
    CHECK(got.hits[0].line == 1);
    CHECK(got.hits[0].column == 5);
    CHECK(got.hits[0].length == 12);  // 六遍，报的是整串的十二字
    CHECK(got.hits[0].match == "极其");  // 命中原文是串首那个单元
    CHECK(got.hits[0].detail == "「极其」连写 6 次");
}

TEST_CASE("复读串：单元按从短到长试，报最短的那个") {
    // 「一句话」连写四遍：先撞上的周期是两个字（「一句」不成立），三字周期成立，报「一句话」。
    const Collected got =
        scan("repeated-token-run", "他用一句话一句话一句话一句话把意思讲完了。");
    REQUIRE(got.hits.size() == 1);
    CHECK(got.hits[0].match == "一句话");
    CHECK(got.hits[0].length == 12);
    CHECK(got.hits[0].detail == "「一句话」连写 4 次");
}

TEST_CASE("复读串：这些串不算复读") {
    const std::string text =
        "极其极其极其还没到四遍。\n"                     // 只连写三遍
        "一二三四五一二三四五一二三四五一二三四五。\n"     // 单元五个字，超出上限
        "哈哈哈哈哈哈哈哈哈哈不是词。\n"                   // 全是语气字
        "字字字字字字字字不是词。\n"                       // 同一个字连写
        "非 常非 常非 常非 常带空格。\n"                   // 单元里带空白
        "……！……！……！……！只有标点。\n";                  // 单元里没有字

    const Collected got = scan("repeated-token-run", text);
    CHECK(got.hits.empty());
}

TEST_CASE("复读串：串的字节范围交给接线层，用来剔掉串里其他规则的命中") {
    const std::string unit = "极其";
    const Collected got = scan("repeated-token-run", unit + unit + unit + unit + "难维护。\n");
    REQUIRE(got.hits.size() == 1);

    const std::vector<Span> spans = texttool::repeat_token_run_spans();
    REQUIRE(spans.size() == 1);
    CHECK(spans[0].begin == 0);
    CHECK(spans[0].end == unit.size() * 4);
}

TEST_CASE("段首重复：四段以上、同一开头出现三次才报") {
    // 只有三段：不判
    const std::string three =
        "他说这句话的时候没有抬头。\n他说完就把杯子放回桌上。\n另一个人一直没有开口。\n";
    CHECK(scan("repeated-paragraph-opening", three).notes.empty());

    // 四段但同一开头只两段：不报
    const std::string two =
        "他说这句话的时候没有抬头。\n他说完就把杯子放回桌上。\n另一个人一直没有开口。\n"
        "今天外面一直在下雨。\n";
    CHECK(scan("repeated-paragraph-opening", two).notes.empty());

    // 七段里两组各三次：按首见次序一列出来
    const std::string grouped =
        "他说这句话的时候没有抬头。\n他说完就把杯子放回桌上。\n他说话的声音比平时低。\n"
        "另一个人一直没有开口。\n然后他把窗子推开一条缝。\n然后他站着听了一会儿雨声。\n"
        "然后他回到桌前把灯调暗。\n";
    const Collected got = scan("repeated-paragraph-opening", grouped);
    CHECK(got.hits.empty());
    REQUIRE(got.notes.size() == 1);
    CHECK(got.notes[0] == "段首两字高度重复：「他说」×3、「然后」×3");
}

TEST_CASE("段首重复：一个字占两个码元时按整字比") {
    // 段首取两个码元：基本平面以外的字一个就占两个，取到的是那一个字，不会切半个。
    const Collected got = scan("repeated-paragraph-opening",
                               "𝄞开头这一段是四段里的第一段。\n𝄞开头这一段是四段里的第二段。\n"
                               "𝄞开头这一段是四段里的第三段。\n别的内容收尾。\n");
    REQUIRE(got.notes.size() == 1);
    CHECK(got.notes[0] == "段首两字高度重复：「𝄞」×3");
}

TEST_CASE("段首重复：段不足两字时整段拿来比") {
    const Collected got =
        scan("repeated-paragraph-opening", "好\n好\n好\n今天外面一直在下雨。\n");
    REQUIRE(got.notes.size() == 1);
    CHECK(got.notes[0] == "段首两字高度重复：「好」×3");
}
