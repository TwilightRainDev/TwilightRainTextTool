// 规则标定的测试：汇总规则、判定、排序、两种输出、目录收稿与报错。
//
// 期望值按判据写死，不是从某次运行抄的：判定只有两条（人类侧零命中记可分、
// 模型侧密度达到人类侧三倍记弱），密度按未取整的值算，表里取两位小数。
#include <cmath>
#include <filesystem>
#include <fstream>
#include <stdexcept>
#include <string>
#include <vector>

#include "doctest/doctest.h"
#include "engine/calibration.h"
#include "engine/rules.h"

namespace {

namespace fs = std::filesystem;

using texttool::CalibrationResult;
using texttool::LintRule;

// 一条最小的正则规则：只有一个模式，命中一处算一处。
LintRule make_rule(const std::string& id, const std::string& title, const std::string& pattern) {
    LintRule rule;
    rule.id = id;
    rule.title = title;
    rule.kind = texttool::RuleKind::Regex;
    rule.patterns = {pattern};
    return rule;
}

// 测试自己造一个小目录：每份稿一个文件，跑完整个目录删掉。
class TempTree {
public:
    explicit TempTree(const std::string& name) {
        root_ = fs::temp_directory_path() / ("texttool-calib-" + name);
        fs::remove_all(root_);
        fs::create_directories(root_);
    }
    ~TempTree() {
        std::error_code ignored;
        fs::remove_all(root_, ignored);
    }
    TempTree(const TempTree&) = delete;
    TempTree& operator=(const TempTree&) = delete;

    std::string file(const std::string& relative, const std::string& content) {
        const fs::path path = root_ / relative;
        fs::create_directories(path.parent_path());
        std::ofstream out(path, std::ios::binary);
        out.write(content.data(), static_cast<std::streamsize>(content.size()));
        return path.string();
    }

    std::string dir(const std::string& relative) const { return (root_ / relative).string(); }

private:
    fs::path root_;
};

}  // namespace

TEST_CASE("密度：按千字算，字数取零时退回命中数") {
    CHECK(texttool::density(12, 249) == doctest::Approx(12 * 1000.0 / 249));
    CHECK(texttool::density(0, 1000) == 0.0);
    CHECK(texttool::density(5, 0) == 5.0);
    CHECK(texttool::calibration_label("separable") == "可分");
    CHECK(texttool::calibration_label("weak") == "弱");
    CHECK(texttool::calibration_label("indistinguishable") == "不可分");
}

TEST_CASE("判定：人类侧零命中记可分，模型侧密度够三倍记弱，其余不可分") {
    TempTree tree("verdict");
    // 两侧各一份四百字的稿：字数一样，密度之比就是命中数之比。
    //   only-ai  人类侧零命中、模型侧一处      算可分
    //   triple   人类一处、模型三处（正好三倍）  算弱
    //   same     人类一处、模型两处（不到三倍）  算不可分
    tree.file("human/a.txt", "@~" + std::string(398, 'a'));
    // 模型侧那一处「乙」（U+4E59）占一个码元，后头补足到四百字
    tree.file("ai/a.txt", "@@@~~\xE4\xB9\x99" + std::string(394, 'a'));

    const std::vector<LintRule> rules = {
        make_rule("triple", "三倍", "@"),
        make_rule("same", "两侧比", "~"),
        make_rule("only-ai", "只在模型侧", "乙"),
    };
    const CalibrationResult result = texttool::calibrate(
        rules, texttool::calibration_txt_files(tree.dir("human"), "--human"),
        texttool::calibration_txt_files(tree.dir("ai"), "--ai"), std::nullopt);

    CHECK(result.human.files == 1);
    CHECK(result.ai.files == 1);
    CHECK(result.human.chars == 400);
    CHECK(result.ai.chars == 400);
    REQUIRE(result.rows.size() == 3);
    for (const auto& row : result.rows) {
        if (row.id == "only-ai") {
            CHECK(row.human_hits == 0);
            CHECK(row.ai_hits == 1);
            CHECK(row.verdict == "separable");
        } else if (row.id == "triple") {
            CHECK(row.human_hits == 1);
            CHECK(row.ai_hits == 3);
            CHECK(row.verdict == "weak");
        } else {
            CHECK(row.human_hits == 1);
            CHECK(row.ai_hits == 2);
            CHECK(row.verdict == "indistinguishable");
        }
    }
    CHECK(result.separable_count == 1);
    CHECK(result.human.hits == 2);
    CHECK(result.ai.hits == 6);
}

TEST_CASE("行按模型侧密度从高到低排，密度相同按两侧命中数与编号排") {
    TempTree tree("order");
    fs::create_directories(tree.dir("human"));
    fs::create_directories(tree.dir("ai"));
    tree.file("human/a.txt", "甲乙丙\n");
    tree.file("ai/a.txt", "甲甲甲\n");

    const std::vector<LintRule> rules = {
        make_rule("c-rule", "丙", "丙"),
        make_rule("a-rule", "甲", "甲"),
        make_rule("b-rule", "乙", "乙"),
    };
    const CalibrationResult result = texttool::calibrate(
        rules, texttool::calibration_txt_files(tree.dir("human"), "--human"),
        texttool::calibration_txt_files(tree.dir("ai"), "--ai"), std::nullopt);

    REQUIRE(result.rows.size() == 3);
    CHECK(result.rows[0].id == "a-rule");  // 模型侧三处，密度最高
    // 另外两条两侧都零命中：密度、命中数都相同，按编号升序
    CHECK(result.rows[1].id == "b-rule");
    CHECK(result.rows[2].id == "c-rule");
}

TEST_CASE("两侧都零命中的规则也占一行：那种活着但量不到东西的判据正是要看的东西") {
    TempTree tree("zero");
    fs::create_directories(tree.dir("human"));
    fs::create_directories(tree.dir("ai"));
    tree.file("human/a.txt", "正文\n");
    tree.file("ai/b.txt", "正文\n");

    const std::vector<LintRule> rules = {make_rule("never-hit", "从不命中", "找不到的写法")};
    const CalibrationResult result = texttool::calibrate(
        rules, texttool::calibration_txt_files(tree.dir("human"), "--human"),
        texttool::calibration_txt_files(tree.dir("ai"), "--ai"), std::nullopt);

    REQUIRE(result.rows.size() == 1);
    CHECK(result.rows[0].id == "never-hit");
    CHECK(result.rows[0].human_hits == 0);
    CHECK(result.rows[0].ai_hits == 0);
    CHECK(result.rows[0].verdict == "indistinguishable");
}

TEST_CASE("档位筛掉不绑该档的规则，默认关着的不跑") {
    TempTree tree("genre");
    fs::create_directories(tree.dir("human"));
    fs::create_directories(tree.dir("ai"));
    tree.file("human/a.txt", "甲乙\n");
    tree.file("ai/a.txt", "甲乙\n");

    LintRule agent_only = make_rule("agent-only", "只跑智能体档", "甲");
    agent_only.genres = {"agent"};
    LintRule doc_only = make_rule("doc-only", "只跑文档档", "乙");
    doc_only.genres = {"doc", "fiction"};
    LintRule off = make_rule("off-by-default", "默认关", "甲");
    off.default_off = true;
    const std::vector<LintRule> rules = {agent_only, doc_only, off};

    const auto files = [&tree](const std::string& name, const std::string& flag) {
        return texttool::calibration_txt_files(tree.dir(name), flag);
    };

    const CalibrationResult docs =
        texttool::calibrate(rules, files("human", "--human"), files("ai", "--ai"), "doc");
    REQUIRE(docs.rows.size() == 1);
    CHECK(docs.rows[0].id == "doc-only");

    const CalibrationResult agents =
        texttool::calibrate(rules, files("human", "--human"), files("ai", "--ai"), "agent");
    REQUIRE(agents.rows.size() == 1);
    CHECK(agents.rows[0].id == "agent-only");

    CHECK_THROWS_WITH_AS(
        texttool::calibrate(rules, files("human", "--human"), files("ai", "--ai"), "webfiction"),
        "--genre 只接受 doc、fiction、agent", std::runtime_error);
}

TEST_CASE("收目录：递归收 .txt，按路径顺序排，别的后缀不收") {
    TempTree tree("files");
    tree.file("human/b.txt", "乙\n");
    tree.file("human/a.txt", "甲\n");
    tree.file("human/sub/c.txt", "丙\n");
    tree.file("human/note.md", "不收这个\n");
    tree.file("human/upper.TXT", "不收这个\n");

    const std::vector<std::string> files =
        texttool::calibration_txt_files(tree.dir("human"), "--human");
    REQUIRE(files.size() == 3);
    CHECK(files[0].find("a.txt") != std::string::npos);
    CHECK(files[1].find("b.txt") != std::string::npos);
    CHECK(files[2].find("c.txt") != std::string::npos);
}

TEST_CASE("收目录：不存在与没有 .txt 分开报，消息里带选项名") {
    TempTree tree("missing");
    fs::create_directories(tree.dir("empty"));

    const std::string missing_message = "--ai 目录不存在：" + tree.dir("nope");
    const std::string empty_message = "--human 目录下没有 .txt 文件：" + tree.dir("empty");
    CHECK_THROWS_WITH_AS(texttool::calibration_txt_files(tree.dir("nope"), "--ai"),
                         missing_message.c_str(), std::runtime_error);
    CHECK_THROWS_WITH_AS(texttool::calibration_txt_files(tree.dir("empty"), "--human"),
                         empty_message.c_str(), std::runtime_error);
}

TEST_CASE("人读表：表头、每行不留行尾空白、末行写两侧规模与可分条数") {
    CalibrationResult result;
    result.human = {2, 249, 12, 48.19277108433735};
    result.ai = {2, 13572, 84, 6.18921308576481};
    result.separable_count = 1;
    result.rows = {{"only-ai", "只在模型侧", 0, 0.0, 21, 1.5473032714412025, "separable"}};

    const std::string text = result.render();
    CHECK(text.find("规则") == 0);
    CHECK(text.find("人类命中") != std::string::npos);
    CHECK(text.find("模型/千字") != std::string::npos);
    CHECK(text.find("only-ai") != std::string::npos);
    CHECK(text.find("只在模型侧") != std::string::npos);

    // 每一行都不留行尾空白：这张表经常被原样贴进文档
    std::size_t pos = 0;
    while (pos < text.size()) {
        const std::size_t newline = text.find('\n', pos);
        const std::size_t end = newline == std::string::npos ? text.size() : newline;
        REQUIRE(end > pos);
        CHECK(text[end - 1] != ' ');
        pos = end + 1;
    }

    CHECK(text.find("人类 2 个文件 249 字，命中 12 处（每千字 48.19）；模型 2 个文件 13572 字，"
                    "命中 84 处（每千字 6.19）；可分规则 1 条。\n") != std::string::npos);
}

TEST_CASE("JSON：字段次序、null 档位、最短还原的双精度写法") {
    CalibrationResult result;
    result.human = {1, 10, 1, 0.0001};
    result.ai = {1, 10, 1, 1e-5};
    result.separable_count = 0;
    result.rows = {{"rule-a", "标题", 1, 100.0, 1, 0.00001, "indistinguishable"}};

    const std::string text = result.to_json();
    CHECK(text.rfind("{\n  \"Version\": 1,\n  \"Genre\": null,\n", 0) == 0);
    CHECK(text.find("  \"Human\": {\n    \"Files\": 1,\n    \"Chars\": 10,\n    \"Hits\": 1,\n"
                    "    \"PerKChar\": 0.0001\n  },\n") != std::string::npos);
    CHECK(text.find("\"PerKChar\": 1E-05\n") != std::string::npos);
    CHECK(text.find("\"HumanPerKChar\": 100,\n") != std::string::npos);
    CHECK(text.find("\"AiPerKChar\": 1E-05,\n") != std::string::npos);
    CHECK(text.find("\"Verdict\": \"indistinguishable\"\n") != std::string::npos);
    CHECK(text.find("  \"Rules\": [\n    {\n      \"Id\": \"rule-a\",\n") != std::string::npos);
}

TEST_CASE("JSON：一条规则都没有时 Rules 写成空数组，Genre 是给的那一档") {
    CalibrationResult result;
    result.genre = std::string("doc");
    const std::string text = result.to_json();
    CHECK(text.find("\"Genre\": \"doc\",\n") != std::string::npos);
    CHECK(text.find("  \"Rules\": []\n") != std::string::npos);
}
