// 规则表是整个检查功能的输入，读错一个字段，全篇报告就不对。
// 这里拿 fixtures/rules_3_1.json 里的解析结果当基准逐条比：那份文件由 tools/RuleDump 生成，命令写在那个目录的注释里。
//
// 比的是解析后的值，不是文件字节：同一份 JSON，两边读出来的结构必须一样，
// 包括没写字段时的默认值（严重程度默认 info、适用范围默认 document、档位默认空）。
//
// 读规则表时的校验就在这里测：自相矛盾的配置读进来就要报，报错句子要带规则名。
#include <fstream>
#include <sstream>
#include <stdexcept>
#include <string>
#include <vector>

#include "doctest/doctest.h"
#include "engine/rules.h"
#include "json/json.hpp"

namespace {

using json = nlohmann::json;

json read_json_file(const std::string& path) {
    std::ifstream in(path, std::ios::binary);
    REQUIRE_MESSAGE(in.good(), "打不开文件：", path);
    std::ostringstream buf;
    buf << in.rdbuf();
    return json::parse(buf.str());
}

std::vector<std::string> to_strings(const json& value) {
    std::vector<std::string> out;
    if (value.is_null()) return out;
    for (const auto& item : value) out.push_back(item.get<std::string>());
    return out;
}

std::optional<std::vector<std::string>> to_optional_strings(const json& value) {
    if (value.is_null()) return std::nullopt;
    return to_strings(value);
}

std::optional<std::string> to_optional_string(const json& value) {
    if (value.is_null()) return std::nullopt;
    return value.get<std::string>();
}

std::optional<int> to_optional_int(const json& value) {
    if (value.is_null()) return std::nullopt;
    return value.get<int>();
}

texttool::LintRule expected_rule(const json& row) {
    texttool::LintRule rule;
    rule.id = row.at("id").get<std::string>();
    rule.group = row.at("group").get<std::string>();
    rule.title = row.at("title").get<std::string>();
    rule.detail = row.at("detail").get<std::string>();
    rule.kind = *texttool::parse_rule_kind(row.at("kind").get<std::string>());
    rule.algorithm_name = to_optional_string(row.at("algorithmName"));
    rule.patterns = to_strings(row.at("patterns"));
    rule.literal_patterns = to_optional_strings(row.at("literalPatterns"));
    rule.hints = to_optional_strings(row.at("hints"));
    rule.suggest_scheme = to_optional_string(row.at("suggestScheme"));
    rule.skip_in_markdown = row.at("skipInMarkdown").get<bool>();
    rule.skip_in_blog = row.at("skipInBlog").get<bool>();
    rule.scope = row.at("scope").get<std::string>();
    rule.genres = to_strings(row.at("genres"));
    rule.tail_chars = to_optional_int(row.at("tailChars"));
    rule.min_count = row.at("minCount").get<int>();
    rule.severity = row.at("severity").get<std::string>();
    rule.blocks_reply = row.at("blocksReply").get<bool>();
    rule.reply_min_count = to_optional_int(row.at("replyMinCount"));
    rule.collapse_at_density = to_optional_int(row.at("collapseAtDensity"));
    const auto& density = row.at("density");
    if (!density.is_null()) {
        texttool::LintDensity d;
        d.basis = density.at("basis").get<std::string>();
        d.min = density.at("min").get<double>();
        d.window = to_optional_int(density.at("window"));
        rule.density = d;
    }
    rule.min_density_per_kchar = to_optional_int(row.at("minDensityPerKChar"));
    rule.default_off = row.at("defaultOff").get<bool>();
    return rule;
}

// 逐字段比，报出第一处不一样的字段名与两边的取值。
// 整条比只能报「第几条不等」，定位不到字段，一次对照会反复返工。
void check_rule_equal(const texttool::LintRule& got, const texttool::LintRule& want) {
    INFO("规则 ", want.id);
    CHECK(got.id == want.id);
    CHECK(got.group == want.group);
    CHECK(got.title == want.title);
    CHECK(got.detail == want.detail);
    CHECK(texttool::to_string(got.kind) == texttool::to_string(want.kind));
    CHECK(got.algorithm_name == want.algorithm_name);
    CHECK(got.patterns == want.patterns);
    CHECK(got.literal_patterns == want.literal_patterns);
    CHECK(got.hints == want.hints);
    CHECK(got.suggest_scheme == want.suggest_scheme);
    CHECK(got.skip_in_markdown == want.skip_in_markdown);
    CHECK(got.skip_in_blog == want.skip_in_blog);
    CHECK(got.scope == want.scope);
    CHECK(got.genres == want.genres);
    CHECK(got.tail_chars == want.tail_chars);
    CHECK(got.min_count == want.min_count);
    CHECK(got.severity == want.severity);
    CHECK(got.blocks_reply == want.blocks_reply);
    CHECK(got.reply_min_count == want.reply_min_count);
    CHECK(got.collapse_at_density == want.collapse_at_density);
    CHECK(got.density == want.density);
    CHECK(got.min_density_per_kchar == want.min_density_per_kchar);
    CHECK(got.default_off == want.default_off);
}

}  // namespace

TEST_CASE("规则表：条数与顺序和 3.1 一致") {
    const auto golden = read_json_file(std::string(TEXTTOOL_FIXTURES_DIR) + "/rules_3_1.json");
    const auto rules = texttool::load_rules(TEXTTOOL_RULES_JSON);

    REQUIRE_MESSAGE(rules.size() == golden.size(),
                    "条数不等：原生版 ", rules.size(), " 条，3.1 是 ", golden.size(), " 条");

    for (size_t i = 0; i < golden.size(); ++i) {
        INFO("第 ", i + 1, " 条");
        CHECK(rules[i].id == golden[i].at("id").get<std::string>());
    }
}

TEST_CASE("规则表：每条规则的每个字段与 3.1 一致") {
    const auto golden = read_json_file(std::string(TEXTTOOL_FIXTURES_DIR) + "/rules_3_1.json");
    const auto rules = texttool::load_rules(TEXTTOOL_RULES_JSON);

    REQUIRE(rules.size() == golden.size());
    for (size_t i = 0; i < golden.size(); ++i) {
        check_rule_equal(rules[i], expected_rule(golden[i]));
    }
}

// ================================================================
//  读规则表时的校验：自相矛盾的配置读进来就报
// ================================================================

namespace {

// 一条最小的正则规则：只改要试的那一个字段，其余保持能读通的写法。
std::string rule_json_with(const std::string& extra) {
    return "[{\"Id\": \"demo-rule\", \"Group\": \"group\", \"Title\": \"标题\","
           " \"Detail\": \"说明\", \"Kind\": \"regex\", \"Patterns\": [\"甲\"]" +
           extra + "}]";
}

}  // namespace

TEST_CASE("规则表：能读通的写法不报错") {
    CHECK(texttool::parse_rules(rule_json_with("")).size() == 1);
    // 段末窗口归 paragraph，按段落算的密度也归 paragraph。
    CHECK(texttool::parse_rules(rule_json_with(", \"Scope\": \"paragraph\", \"TailChars\": 30"))
              .size() == 1);
    CHECK(texttool::parse_rules(rule_json_with(
              ", \"Scope\": \"paragraph\", \"Density\": {\"Basis\": \"paragraph\", \"Min\": 3}"))
              .size() == 1);
    // 规则表本身一条都不该报。
    CHECK_NOTHROW(texttool::load_rules(TEXTTOOL_RULES_JSON));
}

TEST_CASE("规则表：严重程度不是已知值时报错，句子带规则名") {
    CHECK_THROWS_WITH_AS(texttool::parse_rules(rule_json_with(", \"Severity\": \"hint\"")),
                         "规则 demo-rule 的 Severity 非法：hint（可选 info、warn）",
                         std::runtime_error);
}

TEST_CASE("规则表：适用档位不是已知值时报错，句子带规则名") {
    CHECK_THROWS_WITH_AS(texttool::parse_rules(rule_json_with(", \"Genres\": [\"novel\"]")),
                         "规则 demo-rule 的档位非法：novel（可选 doc、fiction、agent）",
                         std::runtime_error);
}

TEST_CASE("规则表：整篇作用域写了段末窗口字数时报错，句子带规则名") {
    CHECK_THROWS_WITH_AS(
        texttool::parse_rules(rule_json_with(", \"Scope\": \"document\", \"TailChars\": 30")),
        "规则 demo-rule 的 TailChars 仅在 Scope=paragraph 时有效", std::runtime_error);
}

TEST_CASE("规则表：整篇以外的作用域写了密度门限时报错，句子带规则名") {
    CHECK_THROWS_WITH_AS(
        texttool::parse_rules(rule_json_with(
            ", \"Scope\": \"paragraph\", \"Density\": {\"Basis\": \"document\", \"Min\": 3}")),
        "规则 demo-rule 的触发密度仅在 Scope=document 时有效", std::runtime_error);
    // 早先只有 MinDensityPerKChar 一种写法，同样落在整篇之外要拦住。
    CHECK_THROWS_WITH_AS(
        texttool::parse_rules(rule_json_with(", \"Scope\": \"paragraph\", \"MinDensityPerKChar\": 3")),
        "规则 demo-rule 的触发密度仅在 Scope=document 时有效", std::runtime_error);
}
