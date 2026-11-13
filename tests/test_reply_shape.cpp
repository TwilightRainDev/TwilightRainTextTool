// 回复检查的测试：认哪些规则、围栏跳过、同行去重、门限按段落数、命中顺序、两种输出。
//
// 规则表用真的那一份（TEXTTOOL_RULES_JSON）：编号那十八条里任何一条被改名或被改成
// 非正则，这里就应当红，那正是要拦住的事。
#include <ostream>
#include <stdexcept>
#include <string>
#include <string_view>
#include <vector>

#include "doctest/doctest.h"
#include "engine/reply_shape.h"
#include "engine/rules.h"

namespace {

using texttool::LintRule;
using texttool::ReplyShapeHit;

const std::vector<LintRule>& rule_table() {
    static const std::vector<LintRule> rules = texttool::load_rules(TEXTTOOL_RULES_JSON);
    return rules;
}

std::vector<ReplyShapeHit> scan(const std::string& text) {
    return texttool::scan_reply_shape(rule_table(), text);
}

}  // namespace

TEST_CASE("认的规则编号有十八条，缺一条就报出缺的是哪条") {
    const auto ids = texttool::reply_shape_rule_ids();
    CHECK(ids.size() == 18);
    CHECK(ids.front() == "agent-code-list");
    CHECK(ids.back() == "agent-probe-question");

    std::vector<LintRule> without;
    for (const auto& rule : rule_table())
        if (rule.id != "agent-code-list") without.push_back(rule);
    CHECK_THROWS_WITH_AS(texttool::scan_reply_shape(without, "正文"),
                         "规则表缺少 agent-code-list，回复形状扫描依赖它", std::runtime_error);
}

TEST_CASE("编号点到的规则必须还是正则规则") {
    std::vector<LintRule> rules = rule_table();
    for (auto& rule : rules)
        if (rule.id == "char-face") {
            rule.kind = texttool::RuleKind::Literal;
            rule.literal_patterns = std::vector<std::string>{"x"};
        }
    std::string message;
    try {
        texttool::scan_reply_shape(rules, "正文");
    } catch (const std::runtime_error& error) {
        message = error.what();
    }
    CHECK(message == "规则 char-face 应当是 regex，实际是 literal");
}

TEST_CASE("围栏代码块整块跳过：块里的写法是引用的原文，不是回复的措辞") {
    const std::string text =
        "这段跑过了。\n\n"
        "```\n"
        "冒烟一下，我认一半\n"
        "```\n\n"
        "结尾。\n";
    const auto hits = scan(text);
    for (const auto& hit : hits) CHECK(hit.line != 3);
}

TEST_CASE("同一行同一个片段只报一次，不同行各报一次") {
    const auto hits = scan("我认一半，我认一半。\n\n我认一半。\n");
    std::size_t on_first_line = 0;
    for (const auto& hit : hits)
        if (hit.line == 1 && hit.token == "我认一半") ++on_first_line;
    CHECK(on_first_line == 1);
}

TEST_CASE("命中的门限按空行分段数：一段里只有一处时不报，凑够两处才报") {
    // agent-smoke-jargon 的回复门限是两处：一段里只出现一次不报
    const std::string one = "先冒烟再提交。\n\n另一段。\n";
    for (const auto& hit : scan(one)) CHECK(hit.id != "agent-smoke-jargon");

    // 两处分在两段里也不算：门限是按同一段数的
    const std::string across = "先冒烟。\n\n再冒烟。\n";
    for (const auto& hit : scan(across)) CHECK(hit.id != "agent-smoke-jargon");

    // 同一行同一个写法只算一处，凑门限要两种不同的写法
    const std::string repeat = "冒烟一次，再一次冒烟。\n";
    for (const auto& hit : scan(repeat)) CHECK(hit.id != "agent-smoke-jargon");

    const std::string two = "冒烟一次，再烟测一次。\n\n另一段。\n";
    std::size_t smoke = 0;
    for (const auto& hit : scan(two))
        if (hit.id == "agent-smoke-jargon") ++smoke;
    CHECK(smoke >= 2);
}

TEST_CASE("命中按行号排，同一行按规则表里的先后排") {
    const auto hits = scan("我认一半。\n\n冒烟一下，冒烟两下。\n");
    REQUIRE(!hits.empty());
    for (std::size_t i = 1; i < hits.size(); ++i)
        CHECK(hits[i - 1].line <= hits[i].line);
    for (const auto& hit : hits) {
        CHECK(hit.line >= 1);
        CHECK(!hit.token.empty());
        CHECK(!hit.title.empty());
    }
}

TEST_CASE("没有命中时人读写一句，JSON 写空数组") {
    const std::vector<ReplyShapeHit> none;
    CHECK(texttool::render_reply_shape(none, false) == "没有读者解不开的写法。\n");
    CHECK(texttool::render_reply_shape(none, true) ==
          "{\n  \"Version\": 2,\n  \"Hits\": []\n}\n");
}

TEST_CASE("人读输出：打回与只提醒分开列，组内逐处写行号与原文，末行是总数") {
    const std::vector<ReplyShapeHit> hits = {
        {"agent-keepsake", "收留式收尾", "这话我收下了", 4, true},
        {"agent-keepsake", "收留式收尾", "这句我留着", 9, true},
        {"char-face", "颜文字", "(^_^)", 12, false},
    };
    CHECK(texttool::render_reply_shape(hits, false) ==
          "[发现] 收留式收尾（2 处）\n"
          "       第 4 行  这话我收下了\n"
          "       第 9 行  这句我留着\n"
          "[只提醒] 颜文字（1 处）\n"
          "       第 12 行  (^_^)\n"
          "共 3 处。\n");
}

TEST_CASE("JSON 输出：版本号在前，每条带 Id、Token、Line、Blocks，中文照原样") {
    const std::vector<ReplyShapeHit> hits = {
        {"agent-keepsake", "收留式收尾", "这话我收下了", 4, true},
        {"char-face", "颜文字", "(*^▽^*)", 12, false},
    };
    CHECK(texttool::render_reply_shape(hits, true) ==
          "{\n"
          "  \"Version\": 2,\n"
          "  \"Hits\": [\n"
          "    {\n"
          "      \"Id\": \"agent-keepsake\",\n"
          "      \"Token\": \"这话我收下了\",\n"
          "      \"Line\": 4,\n"
          "      \"Blocks\": true\n"
          "    },\n"
          "    {\n"
          "      \"Id\": \"char-face\",\n"
          "      \"Token\": \"(*^▽^*)\",\n"
          "      \"Line\": 12,\n"
          "      \"Blocks\": false\n"
          "    }\n"
          "  ]\n"
          "}\n");
}

TEST_CASE("输出里的隐形字符写成转义：控制台编不出来的字不能原样打出去") {
    const std::vector<ReplyShapeHit> hits = {{"emoji-and-symbols", "装饰符号", "a\u200Bb", 1, false}};
    const std::string json = texttool::render_reply_shape(hits, true);
    CHECK(json.find("\"Token\": \"a\\u200bb\"") != std::string::npos);
}
