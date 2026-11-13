// 渲染层的验收：同一份稿件上的两种输出与 expected 文件比，必须逐字节相同。
//
// 稿件与期望输出都在 fixtures/report/：<名>.txt|.md 是稿件，<名>.expected.txt 是人读报告，
// <名>.expected.json 是 --json 输出，<名>.expected.exit 两行分别是两次运行的退出码。
// 这三样都由同目录的 make_goldens.sh 生成，不要手改。
//
// 判据（规则编号、行列、严重度、标题、摘录）不参与：命中项直接从期望输出的 JSON 里读出来，
// 交给人读渲染器重新排版，因此这一层对不对与引擎的判据是否已经实现无关。
//
// 渲染层的输入是「规则表下标 + 字节区间」：规则元数据回表里取，命中原文与展示片段按区间
// 从稿子里切。期望输出的 JSON 里没有稿子，所以夹具那一路把命中原文与片段一起塞进 extra，
// 规则元数据按期望输出里出现的组合临时拼一张规则表。
#include "doctest/doctest.h"
#include "engine/report.h"
#include "engine/rules.h"

#include <algorithm>
#include <fstream>
#include <map>
#include <optional>
#include <sstream>
#include <stdexcept>
#include <string>
#include <vector>

#include "json/json.hpp"

namespace {

using json = nlohmann::json;

std::string fixtures_root() { return TEXTTOOL_FIXTURES_DIR; }

std::string read_text(const std::string& path) {
    std::ifstream in(path, std::ios::binary);
    if (!in) throw std::runtime_error("读不到基准文件：" + path);
    std::ostringstream text;
    text << in.rdbuf();
    return text.str();
}

std::string golden_path(const std::string& name, const std::string& suffix) {
    return fixtures_root() + "/report/" + name + ".expected." + suffix;
}

std::optional<std::string> optional_slot(const json& node, const char* key) {
    auto it = node.find(key);
    if (it == node.end() || it->is_null()) return std::nullopt;
    return it->get<std::string>();
}

// 折叠阈值随报告带出，规则表里写了的全带上：命令行与界面也是这么填的，
// 渲染层不查规则表。少了这一项，折叠那条分支在任何期望输出上都跑不起来。
std::map<std::string, int> collapse_thresholds() {
    static const std::map<std::string, int> table = [] {
        std::map<std::string, int> out;
        for (const auto& rule : texttool::load_rules(TEXTTOOL_RULES_JSON))
            if (rule.collapse_at_density) out[rule.id] = *rule.collapse_at_density;
        return out;
    }();
    return table;
}

// 排版用的输入：规则表 + 命中 + 附加串。规则表是临时拼的，命中里的下标指向它，
// 附加串里放命中原文与已经切好的展示片段（期望输出里只有这两种字符串，没有稿子可切）。
struct Synthetic {
    std::vector<texttool::LintRule> rules;
    texttool::ReportInput input;

    Synthetic() { input.rules = &rules; }

    std::uint32_t add_string(std::string_view text, std::uint32_t& length) {
        const auto offset = static_cast<std::uint32_t>(input.arena.size());
        input.arena.append(text);
        length = static_cast<std::uint32_t>(text.size());
        return offset;
    }
    Synthetic(Synthetic&& other) noexcept : rules(std::move(other.rules)), input(std::move(other.input)) {
        input.rules = &rules;
    }
    Synthetic& operator=(Synthetic&&) = delete;
    Synthetic(const Synthetic&) = delete;

    // 同一条规则只放一次：组的划分按规则下标，同一规则得指向同一条
    std::size_t rule_index(std::string_view id, std::string_view title, std::string_view group,
                           std::string_view severity, std::string_view detail,
                           std::optional<std::string> hint, std::optional<std::string> scheme) {
        for (std::size_t i = 0; i < rules.size(); ++i) {
            const texttool::LintRule& rule = rules[i];
            const std::optional<std::string> rule_hint =
                rule.hints ? std::optional<std::string>((*rule.hints).front()) : std::nullopt;
            if (rule.id == id && rule.title == title && rule.detail == detail &&
                rule.group == group && rule.severity == severity && rule_hint == hint &&
                rule.suggest_scheme == scheme)
                return i;
        }
        texttool::LintRule rule;
        rule.id = std::string(id);
        rule.title = std::string(title);
        rule.group = std::string(group);
        rule.severity = std::string(severity);
        rule.detail = std::string(detail);
        if (hint) rule.hints = std::vector<std::string>{*hint};
        rule.suggest_scheme = scheme;
        rules.push_back(std::move(rule));
        return rules.size() - 1;
    }

    void add(std::size_t rule, std::size_t line, std::size_t column, std::size_t length,
             std::string match, std::string snippet) {
        // 附加串按偏移放在 arena 里，与引擎那条路一致
        texttool::LintHitExtra extra;
        extra.match_offset = add_string(match, extra.match_length);
        extra.snippet_offset = add_string(snippet, extra.snippet_length);
        input.extras.push_back(extra);
        texttool::LintHit hit;
        hit.rule = static_cast<std::uint16_t>(rule);
        hit.extra = static_cast<std::uint16_t>(input.extras.size() - 1);
        hit.line = static_cast<std::uint32_t>(line);
        hit.column = static_cast<std::uint32_t>(column);
        hit.length = static_cast<std::uint32_t>(length);
        if (rules[rule].hints) hit.hint = 0;
        input.hits.push_back(hit);
    }
};

void fill_from_golden_json(const std::string& text, Synthetic& out) {
    const json doc = json::parse(text);
    const json& report = doc.at("Reports").at(0);

    out.input.source_name = report.at("File").get<std::string>();
    out.input.total_scanned = report.at("Chars").get<int>();
    out.input.paragraph_mode = report.at("ParagraphMode").get<std::string>();
    out.input.collapse_at_density = collapse_thresholds();

    for (const auto& hit : report.at("Hits")) {
        const std::size_t rule = out.rule_index(
            hit.at("Id").get<std::string>(), hit.at("Title").get<std::string>(),
            hit.at("Group").get<std::string>(), hit.at("Severity").get<std::string>(),
            hit.at("Detail").get<std::string>(), optional_slot(hit, "Hint"),
            optional_slot(hit, "SuggestScheme"));
        out.add(rule, hit.at("Line").get<std::size_t>(), hit.at("Col").get<std::size_t>(),
                hit.at("Length").get<std::size_t>(), hit.at("Match").get<std::string>(),
                hit.at("Snippet").get<std::string>());
    }

    for (const auto& note : report.at("Notes"))
        out.input.notes.push_back(
            {note.at("Id").get<std::string>(), note.at("Text").get<std::string>()});

    if (auto it = report.find("BaselineHits"); it != report.end() && !it->is_null())
        out.input.baseline_hits = it->get<int>();
}

// 期望输出里记了两次运行的退出码：人读一次、JSON 一次，两次必须相同。
int golden_exit_code(const std::string& name) {
    std::istringstream lines(read_text(golden_path(name, "exit")));
    std::string first;
    std::getline(lines, first);
    return std::stoi(first);
}

// 每份稿件跑一遍：人读报告、JSON、退出码三样都比。
struct Fixture {
    const char* name;
    const char* what;
};

const Fixture kFixtures[] = {
    {"no_hits", "没有命中"},
    {"mixed", "两条命中，两条规则各一处"},
    {"mixed_severity", "警告级与提示级混排，一条规则两处，带提示与方案"},
    {"escaping", "JSON 敏感字符与隐形字符"},
    {"exotic_codepoints", "补充区、私有区、未分配码位与非字符的转义"},
    {"crlf", "CRLF 行尾"},
    {"collapse", "同一规则 11 处、密度远超阈值：折叠成三处样例"},
    {"real_doc_agents", "真实文档，带统计项"},
    {"real_doc_architecture", "真实长文档，多处命中"},
    {"baseline", "带基线文件：只列新增位置"},
};

// 命中行的缩进是七个空格，组头与其余行不缩进到这个形状。
std::size_t count_hit_lines(const std::string& report) {
    std::size_t count = 0;
    std::istringstream lines(report);
    std::string line;
    while (std::getline(lines, line))
        if (line.rfind("       第 ", 0) == 0) ++count;
    return count;
}

}  // namespace

TEST_CASE("人读报告与对照基准逐字节相同") {
    for (const auto& fixture : kFixtures) {
        INFO(fixture.name << "：" << fixture.what);
        Synthetic fixture_input;
        fill_from_golden_json(read_text(golden_path(fixture.name, "json")), fixture_input);
        CHECK(texttool::render_text_report(fixture_input.input) ==
              read_text(golden_path(fixture.name, "txt")));
    }
}

TEST_CASE("JSON 输出与对照基准逐字节相同") {
    for (const auto& fixture : kFixtures) {
        INFO(fixture.name << "：" << fixture.what);
        Synthetic fixture_input;
        fill_from_golden_json(read_text(golden_path(fixture.name, "json")), fixture_input);
        CHECK(texttool::render_json_report(fixture_input.input) ==
              read_text(golden_path(fixture.name, "json")));
    }
}

TEST_CASE("退出码只看有没有命中") {
    for (const auto& fixture : kFixtures) {
        INFO(fixture.name);
        Synthetic fixture_input;
        fill_from_golden_json(read_text(golden_path(fixture.name, "json")), fixture_input);
        CHECK(texttool::exit_code_for(fixture_input.input) == golden_exit_code(fixture.name));
    }
    CHECK(texttool::exit_code_for({}) == 0);
}

TEST_CASE("折叠分支：处数够多、密度到阈值时只列三处样例") {
    // 这条分支只有在阈值真的送到渲染器、处数门槛与密度都算对的时候才出现。
    // 生成那边自己也会比对，这里单独钉一遍：样例数从 3 改成别的、密度算成 0.0、
    // 该不该折叠恒判为否，三者任一被改都会在这里露出来。
    Synthetic fixture_input;
    fill_from_golden_json(read_text(golden_path("collapse", "json")), fixture_input);
    const std::string report = texttool::render_text_report(fixture_input.input);

    INFO(report);
    CHECK(report.find("（11 处，每千字 76.9 处）：同一句式反复出现，只列几处样例") != std::string::npos);
    CHECK(report.find("       其余 8 处未逐条列出，位置一条不少\n") != std::string::npos);
    // 整份稿 12 处命中：折叠的那组列三处样例，另一组一处，合计四行命中
    CHECK(count_hit_lines(report) == 4);
}

TEST_CASE("JSON 转义：补充区、私有区、未分配码位与非字符") {
    // JSON 只放行 Unicode 已分配的码位：私有区、未分配码位与非字符
    // 一律写成大写十六进制；补充区的字按码元成对写，落在补充私有区里也一样
    // （写成 \u0000 那种截断是错的）。隐形字符由输出收尾那一步改写，写小写。
    // 字节全部写死，不靠实现算出来：
    //   U+FFFE EF BF BE  U+FFFF EF BF BF  U+0378 CD B8  U+E000 EE 80 80
    //   U+F0000 F3 B0 80 80  U+1F600 F0 9F 98 80  U+200B E2 80 8B
    const std::string exotic =
        "\xEF\xBF\xBE\xEF\xBF\xBF\xCD\xB8\xEE\x80\x80\xF3\xB0\x80\x80\xF0\x9F\x98\x80\xE2\x80\x8B";

    Synthetic synthetic;
    synthetic.input.source_name = "exotic.txt";
    synthetic.input.total_scanned = 7;
    const std::size_t rule =
        synthetic.rule_index("char-face", "越界字符", "punctuation", "warn", "", std::nullopt,
                             std::nullopt);
    synthetic.add(rule, 1, 1, 7, exotic, exotic);

    const std::string json = texttool::render_json_report(synthetic.input);
    INFO(json);
    CHECK(json.find(R"("Match": "\uFFFE\uFFFF\u0378\uE000\uDB80\uDC00\uD83D\uDE00\u200b",)") !=
          std::string::npos);
    CHECK(json.find(R"("Snippet": "\uFFFE\uFFFF\u0378\uE000\uDB80\uDC00\uD83D\uDE00\u200b",)") !=
          std::string::npos);
}

TEST_CASE("命中顺序：调用方先按行列全局排好，渲染层不再重排") {
    // 契约写在 report.h：hits 必须是调用方按 (行, 列) 排好的全表。
    // 渲染层只做两件事：JSON 照传入次序逐条写，人读报告按首次出现定组序、组内按行列排。
    // 整表排序放在渲染层会让组序变成「各组最早那条的位置」，与期望输出的顺序不同。
    Synthetic synthetic;
    synthetic.input.source_name = "order.txt";
    synthetic.input.total_scanned = 100;

    const auto add = [&synthetic](const char* id, const char* title, std::size_t line,
                                  std::size_t column) {
        const std::size_t rule =
            synthetic.rule_index(id, title, "syntax", "warn", "", std::nullopt, std::nullopt);
        synthetic.add(rule, line, column, 2, "样例", "样例");
    };
    // 全表按行列升序；乙组先出现一次，甲组有两条，乙组又补一条
    add("rule-yi", "乙", 1, 5);
    add("rule-jia", "甲", 2, 1);
    add("rule-jia", "甲", 3, 2);
    add("rule-yi", "乙", 4, 4);

    const std::string json = texttool::render_json_report(synthetic.input);
    INFO(json);
    const auto first = json.find(R"("Id": "rule-yi")");
    const auto second = json.find(R"("Id": "rule-jia")");
    const auto third = json.find(R"("Id": "rule-jia")", second + 1);
    const auto fourth = json.find(R"("Id": "rule-yi")", first + 1);
    CHECK(first != std::string::npos);
    CHECK(first < second);
    CHECK(second < third);
    CHECK(third < fourth);

    const std::string report = texttool::render_text_report(synthetic.input);
    INFO(report);
    // 组序按首次出现：乙在甲前
    const auto group_yi = report.find("[发现] 乙（2 处）");
    const auto group_jia = report.find("[发现] 甲（2 处）");
    CHECK(group_yi != std::string::npos);
    CHECK(group_jia != std::string::npos);
    CHECK(group_yi < group_jia);
    // 组内按行列升序：乙组两条分别是 1 行 5 列、4 行 4 列
    const auto yi_1 = report.find("第 1 行 第 5 列");
    const auto yi_4 = report.find("第 4 行 第 4 列");
    CHECK(yi_1 != std::string::npos);
    CHECK(yi_1 < yi_4);
}

// ---------------------------------------------------------------- 报告装配

TEST_CASE("报告装配：一次检查的产出整张挪过来，折叠阈值只收写了这一项的规则") {
    std::vector<texttool::LintRule> rules(2);
    rules[0].id = "with-collapse";
    rules[0].collapse_at_density = 12;
    rules[1].id = "without-collapse";

    const std::map<std::string, int> collapse = texttool::collapse_thresholds(rules);
    REQUIRE(collapse.size() == 1);
    CHECK(collapse.at("with-collapse") == 12);

    texttool::LintOutcome outcome;
    outcome.units = 42;
    outcome.paragraph_mode = "markdown";
    outcome.rules = &rules;
    outcome.text = "稿子";
    texttool::LintHit hit;
    hit.rule = 0;
    hit.begin = 0;
    hit.end = 6;
    outcome.hits.push_back(hit);
    outcome.notes.push_back({"with-collapse", "一条统计"});

    const texttool::ReportInput report =
        texttool::make_report("(粘贴的文本)", std::move(outcome), collapse);
    CHECK(report.source_name == "(粘贴的文本)");
    CHECK(report.total_scanned == 42);
    CHECK(report.paragraph_mode == "markdown");
    CHECK(report.rules == &rules);
    CHECK(report.text == "稿子");
    CHECK(report.hits.size() == 1);
    CHECK(report.notes.size() == 1);
    CHECK(report.collapse_at_density == collapse);
    // 整张挪过去，不留第二份：产出里的表已经空了
    CHECK(outcome.hits.empty());
    CHECK(outcome.notes.empty());
}

TEST_CASE("命中日志：一行汇总加逐条命中，画不出来的字写成转义") {
    const std::vector<texttool::LintRule> rules = texttool::parse_rules(R"TXT([
      {"Id":"cliche-words","Group":"lexis","Title":"套话词","Kind":"literal",
       "LiteralPatterns":["值得注意的是"],"Detail":"套话/空词","Severity":"info"}
    ])TXT");

    // 命中原文后面跟一个零宽空格与一个表情：两样都画不出来，日志里要写成 \uXXXX
    const std::string text = "值得注意的是\xE2\x80\x8B\xF0\x9F\x98\x80，这不是重点。\n";
    texttool::LintOptions options;
    texttool::LintOutcome outcome = texttool::run_lint(rules, text, options);
    const texttool::ReportInput report = texttool::make_report(
        "a.txt", std::move(outcome), texttool::collapse_thresholds(rules));

    const std::string log = texttool::render_hit_log(report);
    const std::size_t first_end = log.find('\n');
    REQUIRE(first_end != std::string::npos);
    CHECK(log.substr(0, first_end + 1) ==
          "{\"Kind\":\"Report\",\"File\":\"a.txt\",\"Chars\":17,\"ParagraphMode\":\"line\","
          "\"Hits\":1}\n");
    CHECK(log.find("\"Id\":\"cliche-words\"") != std::string::npos);
    CHECK(log.find("\"Match\":\"值得注意的是\"") != std::string::npos);
    CHECK(log.find("\\u200b") != std::string::npos);
    CHECK(log.find("\\ud83d\\ude00") != std::string::npos);
    // 日志里不留真换行：一条命中占一行
    CHECK(std::count(log.begin(), log.end(), '\n') == 2);
    CHECK(log.back() == '\n');
}

// ---------------------------------------------------------------- 封顶

namespace {

// 一条只认「针」的探针规则：命中处数好数，报告里的两处数字好对。
std::vector<texttool::LintRule> probe_rules(int collapse_at_density) {
    std::vector<texttool::LintRule> rules = texttool::parse_rules(R"TXT([
      {"Id":"probe-word","Group":"probe","Title":"探针词","Kind":"literal",
       "LiteralPatterns":["针"],"Detail":"探针","Severity":"info","Scope":"document"}
    ])TXT");
    if (collapse_at_density > 0) rules[0].collapse_at_density = collapse_at_density;
    return rules;
}

std::string needles(std::size_t count) {
    std::string text;
    text.reserve(count * 3);
    for (std::size_t i = 0; i < count; ++i) text += "针";
    return text;
}

// 带基线的一份报告：写前那一份 1 处，写后那份由 body 给。
// body 归调用方保管：报告里的命中原文与片段按区间从它切。
texttool::ReportInput probe_report(const std::vector<texttool::LintRule>& rules,
                                   const std::string& body) {
    texttool::LintOptions before;
    before.role = texttool::LintScanRole::Baseline;
    before.cut_to_named = false;
    const texttool::BaselineCounts counts =
        texttool::count_baseline(texttool::run_lint(rules, "针\n", before));

    texttool::LintOptions after;
    after.role = texttool::LintScanRole::Compared;
    after.cut_to_named = false;
    texttool::LintOutcome outcome = texttool::run_lint(rules, body, after);
    texttool::apply_baseline(outcome, counts, {}, "info");
    return texttool::make_report("probe.txt", std::move(outcome),
                                 texttool::collapse_thresholds(rules));
}

}  // namespace

TEST_CASE("封顶：组头、开头那句与统计段都写真实处数，列表只到上限") {
    // 250 处里抵掉基线那一处，净增 249 处：列表只留 200 处，两处写出来的数字都是真实处数。
    const std::vector<texttool::LintRule> rules = probe_rules(0);
    const std::string body = needles(250);
    const texttool::ReportInput report = probe_report(rules, body);

    CHECK(report.hits.size() == texttool::kMaxListedHitsPerRule);
    REQUIRE(report.rule_hits.size() == 1);
    CHECK(report.rule_hits[0] == 249);
    CHECK(texttool::total_hits(report) == 249);

    const std::string text = texttool::render_text_report(report);
    INFO(text);
    CHECK(text.find("[发现] 探针词（249 处）") != std::string::npos);
    CHECK(text.find("[说明] 全文 250 字，发现 249 处") != std::string::npos);
    CHECK(count_hit_lines(text) == texttool::kMaxListedHitsPerRule);

    // 统计段：新增处数那句在前，截断那句在后——按规则读新增处数的一侧取第一条附注的整数
    const auto added = text.find("相对基线新增 249 处");
    const auto truncated = text.find("另有 49 处未列出（同一条规则每份稿最多列 200 处）");
    REQUIRE(added != std::string::npos);
    REQUIRE(truncated != std::string::npos);
    CHECK(added < truncated);

    // JSON 与命中日志同一口径：条数到上限，总数写真实处数
    const std::string json = texttool::render_json_report(report);
    CHECK(json.find("另有 49 处未列出") != std::string::npos);
    // 命中条目按 Length 数：附注里也有这条规则的 Id，按 Id 会多数两条
    std::size_t listed = 0;
    for (auto at = json.find("\"Length\": 1,"); at != std::string::npos;
         at = json.find("\"Length\": 1,", at + 1))
        ++listed;
    CHECK(listed == texttool::kMaxListedHitsPerRule);

    const std::string log = texttool::render_hit_log(report);
    CHECK(log.find("\"Hits\":249") != std::string::npos);
    CHECK(std::count(log.begin(), log.end(), '\n') ==
          static_cast<std::ptrdiff_t>(texttool::kMaxListedHitsPerRule + 1));
}

TEST_CASE("封顶之后折叠那一组不再说「位置一条不少」") {
    // 折叠只列三处样例，处数与密度按真实处数算；位置本来就不全了，那一句说不起来。
    const std::vector<texttool::LintRule> rules = probe_rules(1);
    const std::string body = needles(250);
    texttool::LintOutcome outcome = texttool::run_lint(rules, body, texttool::LintOptions{});
    const texttool::ReportInput report = texttool::make_report(
        "probe.txt", std::move(outcome), texttool::collapse_thresholds(rules));

    const std::string text = texttool::render_text_report(report);
    INFO(text);
    CHECK(text.find("（250 处，每千字 1000.0 处）") != std::string::npos);
    CHECK(text.find("       其余 247 处未逐条列出\n") != std::string::npos);
    CHECK(text.find("位置一条不少") == std::string::npos);
    CHECK(count_hit_lines(text) == 3);
}
