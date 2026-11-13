// 用户改过的检查规则（lint_rules.json）：读、合并、校验、写回、恢复内置。
//
// 用例不碰仓库里那份内置规则表以外的任何文件：覆盖文件每一份都在临时目录里现写、用完删掉，
// 靠环境变量 TEXTTOOL_LINT_RULES_JSON 指过去，程序目录里有没有 lint_rules.json 都不影响结果。
// 内置表读的是真实数据（TEXTTOOL_RULES_JSON），合并结果才和命令行跑的一致。
#include "doctest/doctest.h"

#include <cstdlib>
#include <filesystem>
#include <fstream>
#include <stdexcept>
#include <string>
#include <vector>

#ifdef _WIN32
#include <process.h>
#else
#include <unistd.h>
#endif

#include "engine/rule_overrides.h"
#include "engine/rules.h"

namespace {

namespace fs = std::filesystem;

int current_process_id() {
#ifdef _WIN32
    return _getpid();
#else
    return static_cast<int>(::getpid());
#endif
}

// 临时目录：构造时建，析构时连里面的一起删。
class TempDir {
public:
    TempDir() {
        static int counter = 0;
        path_ = fs::temp_directory_path() /
                ("texttool_rule_overrides_" + std::to_string(++counter) + "_" +
                 std::to_string(current_process_id()));
        std::error_code ignored;
        fs::remove_all(path_, ignored);
        fs::create_directories(path_);
    }
    ~TempDir() {
        std::error_code ignored;
        fs::remove_all(path_, ignored);
    }
    TempDir(const TempDir&) = delete;
    TempDir& operator=(const TempDir&) = delete;

    const fs::path& path() const { return path_; }

    fs::path write(const std::string& name, const std::string& bytes) const {
        const fs::path file = path_ / name;
        std::ofstream out(file, std::ios::binary | std::ios::trunc);
        out.write(bytes.data(), static_cast<std::streamsize>(bytes.size()));
        out.close();
        return file;
    }

    std::string read(const std::string& name) const {
        std::ifstream in(path_ / name, std::ios::binary);
        return std::string((std::istreambuf_iterator<char>(in)), std::istreambuf_iterator<char>());
    }

private:
    fs::path path_;
};

// 环境变量：指到临时文件上，测试结束后清掉。
class EnvVar {
public:
    EnvVar(const char* name, const std::string& value) : name_(name) { set(value); }
    ~EnvVar() { clear(); }
    EnvVar(const EnvVar&) = delete;
    EnvVar& operator=(const EnvVar&) = delete;

    void set(const std::string& value) const {
#ifdef _WIN32
        _putenv_s(name_.c_str(), value.c_str());
#else
        ::setenv(name_.c_str(), value.c_str(), 1);
#endif
    }
    void clear() const {
#ifdef _WIN32
        _putenv_s(name_.c_str(), "");
#else
        ::unsetenv(name_.c_str());
#endif
    }

private:
    std::string name_;
};

// 仓库里那份内置规则表，与命令行读的是同一份。
const std::vector<texttool::LintRule>& built_in() {
    static const std::vector<texttool::LintRule> rules =
        texttool::load_rules(TEXTTOOL_RULES_JSON);
    return rules;
}

const texttool::LintRule* find_rule(const std::vector<texttool::LintRule>& rules,
                                   const std::string& id) {
    for (const texttool::LintRule& rule : rules)
        if (rule.id == id) return &rule;
    return nullptr;
}

// 一条最小可用的正则规则：只改要试的那一个字段，其余保持能跑通的写法。
std::string override_json(const std::string& extra) {
    return R"([{"Id": "demo-rule", "Group": "group", "Title": "标题", "Detail": "说明",)"
           R"( "Kind": "regex", "Patterns": ["甲"] )" + extra + "}]";
}

}  // namespace

// ================================================================
//  读与合并
// ================================================================

TEST_CASE("覆盖文件不在：等于没有覆盖，合并结果与内置一字不差") {
    const TempDir dir;
    const EnvVar env("TEXTTOOL_LINT_RULES_JSON", (dir.path() / "没有这个文件.json").string());

    std::vector<texttool::RuleOverride> overrides;
    std::string error;
    REQUIRE(texttool::load_rule_overrides(nullptr, overrides, error));
    CHECK(error.empty());
    CHECK(overrides.empty());

    std::vector<texttool::LintRule> merged;
    REQUIRE(texttool::load_rules_with_overrides(TEXTTOOL_RULES_JSON, nullptr, merged, error));
    CHECK(merged == built_in());
}

TEST_CASE("覆盖文件是空文件：同样等于没有覆盖") {
    const TempDir dir;
    // 0 字节，与只有几个换行的文件都是「没写过」。
    const fs::path empty = dir.write("lint_rules.json", "");
    {
        const EnvVar env("TEXTTOOL_LINT_RULES_JSON", empty.string());
        std::vector<texttool::RuleOverride> overrides;
        std::string error;
        REQUIRE(texttool::load_rule_overrides(nullptr, overrides, error));
        CHECK(overrides.empty());

        std::vector<texttool::LintRule> merged;
        REQUIRE(texttool::load_rules_with_overrides(TEXTTOOL_RULES_JSON, nullptr, merged, error));
        CHECK(merged == built_in());
    }

    const fs::path blank_lines = dir.write("blank.json", "\n\n   \n");
    const EnvVar env("TEXTTOOL_LINT_RULES_JSON", blank_lines.string());
    std::vector<texttool::RuleOverride> overrides;
    std::string error;
    REQUIRE(texttool::load_rule_overrides(nullptr, overrides, error));
    CHECK(overrides.empty());
}

TEST_CASE("单条覆盖：按 Id 整条换掉内置那条，位置留在原处，其余一条不动") {
    const TempDir dir;
    // 挑一条内置规则，只改它的严重程度与次数门限。
    const texttool::LintRule& original = built_in().front();
    const std::string row = std::string(R"({"Id": ")") + original.id +
                            R"(", "Title": "改过的标题", "Detail": "改过的说明", "Kind": "regex",)"
                            R"( "Patterns": ["改过的样子"], "Severity": "warn", "MinCount": 2})";
    const fs::path file = dir.write("lint_rules.json", "[" + row + "]");
    const EnvVar env("TEXTTOOL_LINT_RULES_JSON", file.string());

    std::vector<texttool::RuleOverride> overrides;
    std::string error;
    REQUIRE(texttool::load_rule_overrides(nullptr, overrides, error));
    REQUIRE(overrides.size() == 1);
    CHECK(overrides[0].rule.id == original.id);
    CHECK_FALSE(overrides[0].raw.empty());

    std::vector<texttool::LintRule> merged;
    REQUIRE(texttool::load_rules_with_overrides(TEXTTOOL_RULES_JSON, nullptr, merged, error));
    // 条数不变、位置不变：报告里分组的先后由规则表次序决定，覆盖不该把它打乱。
    REQUIRE(merged.size() == built_in().size());
    for (std::size_t at = 0; at < merged.size(); ++at) CHECK(merged[at].id == built_in()[at].id);

    const texttool::LintRule* changed = find_rule(merged, original.id);
    REQUIRE(changed != nullptr);
    CHECK(changed->title == "改过的标题");
    CHECK(changed->detail == "改过的说明");
    CHECK(changed->severity == "warn");
    CHECK(changed->min_count == 2);
    // 整条覆盖：覆盖文件里没写的字段取默认值，不从内置那条继承。
    CHECK(changed->scope == "document");
    CHECK(changed->patterns == std::vector<std::string>{"改过的样子"});
}

TEST_CASE("只改一条：别的规则一个字段都不动") {
    const TempDir dir;
    const std::string id = built_in().at(2).id;
    const fs::path file = dir.write("lint_rules.json",
                                     R"([{"Id": ")" + id + R"(", "Kind": "literal",)"
                                     R"( "LiteralPatterns": ["探针"], "Title": "探针" }])");
    const EnvVar env("TEXTTOOL_LINT_RULES_JSON", file.string());

    std::vector<texttool::LintRule> merged;
    std::string error;
    REQUIRE(texttool::load_rules_with_overrides(TEXTTOOL_RULES_JSON, nullptr, merged, error));
    REQUIRE(merged.size() == built_in().size());
    for (std::size_t at = 0; at < merged.size(); ++at) {
        if (merged[at].id == id) continue;
        CHECK(merged[at] == built_in()[at]);
    }
}

TEST_CASE("Id 在内置表里没有：接在末尾，不顶掉任何一条") {
    const TempDir dir;
    const fs::path file =
        dir.write("lint_rules.json", R"([{"Id": "user-demo", "Kind": "literal",)"
                                      R"( "LiteralPatterns": ["探针"], "Title": "自加的"}])");
    const EnvVar env("TEXTTOOL_LINT_RULES_JSON", file.string());

    std::vector<texttool::LintRule> merged;
    std::string error;
    REQUIRE(texttool::load_rules_with_overrides(TEXTTOOL_RULES_JSON, nullptr, merged, error));
    REQUIRE(merged.size() == built_in().size() + 1);
    CHECK(merged.back().id == "user-demo");
    for (std::size_t at = 0; at < built_in().size(); ++at) CHECK(merged[at] == built_in()[at]);
}

TEST_CASE("字段缺省值：覆盖条目没写的字段按规则表的默认值算") {
    const TempDir dir;
    const fs::path file = dir.write("lint_rules.json", override_json(""));
    const EnvVar env("TEXTTOOL_LINT_RULES_JSON", file.string());

    std::vector<texttool::RuleOverride> overrides;
    std::string error;
    REQUIRE(texttool::load_rule_overrides(nullptr, overrides, error));
    REQUIRE(overrides.size() == 1);

    const texttool::LintRule& rule = overrides[0].rule;
    CHECK(rule.severity == "info");
    CHECK(rule.scope == "document");
    CHECK(rule.min_count == 1);
    CHECK(rule.kind == texttool::RuleKind::Regex);
    CHECK_FALSE(rule.skip_in_markdown);
    CHECK_FALSE(rule.skip_in_blog);
    CHECK_FALSE(rule.blocks_reply);
    CHECK_FALSE(rule.default_off);
    CHECK(rule.genres.empty());
    CHECK_FALSE(rule.tail_chars.has_value());
    CHECK_FALSE(rule.reply_min_count.has_value());
    CHECK_FALSE(rule.density.has_value());
    CHECK_FALSE(rule.min_density_per_kchar.has_value());
    CHECK_FALSE(rule.hints.has_value());
    CHECK_FALSE(rule.suggest_scheme.has_value());
    CHECK_FALSE(rule.algorithm_name.has_value());
    CHECK_FALSE(rule.literal_patterns.has_value());
}

// ================================================================
//  校验：不通过的条目不许进盘
// ================================================================

TEST_CASE("坏正则被挡下：说出是哪一份文件、第几条、哪一条 Id") {
    const TempDir dir;
    const fs::path file = dir.write(
        "lint_rules.json",
        R"([{"Id": "demo-rule", "Kind": "regex", "Patterns": ["(未闭合"], "Title": "坏正则"}])");
    const EnvVar env("TEXTTOOL_LINT_RULES_JSON", file.string());

    std::vector<texttool::RuleOverride> overrides;
    std::string error;
    CHECK_FALSE(texttool::load_rule_overrides(nullptr, overrides, error));
    CHECK(error.find("lint_rules.json") != std::string::npos);
    CHECK(error.find("第 1 条") != std::string::npos);
    CHECK(error.find("demo-rule") != std::string::npos);
    CHECK(error.find("正则非法") != std::string::npos);
    CHECK(overrides.empty());
}

TEST_CASE("坏正则存不进去：盘上一个字不动") {
    const TempDir dir;
    const fs::path file = dir.write("lint_rules.json", R"([{"Id": "demo-rule", "Kind": "regex",)"
                                                        R"( "Patterns": ["甲"], "Title": "原来那条"}])");
    const EnvVar env("TEXTTOOL_LINT_RULES_JSON", file.string());
    const std::string before = dir.read("lint_rules.json");

    texttool::LintRule broken;
    broken.id = "demo-rule";
    broken.kind = texttool::RuleKind::Regex;
    broken.patterns = {"(未闭合"};
    CHECK_THROWS_AS(texttool::save_rule_override(nullptr, broken), std::runtime_error);
    CHECK(dir.read("lint_rules.json") == before);
}

TEST_CASE("字段取值不对：严重程度、适用范围、阈值都挡在写盘之前") {
    const TempDir dir;
    const EnvVar env("TEXTTOOL_LINT_RULES_JSON", (dir.path() / "lint_rules.json").string());

    // 每份坏文件都读一遍，返回它那句话给下面对。
    const auto rejected = [] {
        std::vector<texttool::RuleOverride> overrides;
        std::string error;
        CHECK_FALSE(texttool::load_rule_overrides(nullptr, overrides, error));
        return error;
    };

    // 严重程度不是 info 或 warn：这条规则永远不会按警告处理，读者会以为它只是提示。
    dir.write("lint_rules.json", override_json(R"(, "Severity": "hint")"));
    CHECK(rejected() == "规则覆盖文件 " + (dir.path() / "lint_rules.json").string() +
                               " 第 1 条：规则 demo-rule 的 Severity 非法：hint（可选 info、warn）");

    // 适用范围拼错：按段落办的那套处理一档都命中不了。
    dir.write("lint_rules.json", override_json(R"(, "Scope": "paragraphs")"));
    CHECK(rejected().find("Scope 非法：paragraphs") != std::string::npos);

    // 档位拼错：在任何 --genre 下都不跑。
    dir.write("lint_rules.json", override_json(R"(, "Genres": ["novel"])"));
    CHECK(rejected().find("档位非法：novel") != std::string::npos);

    // 次数门限小于 1：门限等于没有。
    dir.write("lint_rules.json", override_json(R"(, "MinCount": 0)"));
    CHECK(rejected().find("MinCount 必须 >= 1") != std::string::npos);

    // 段末窗口归段落作用域，写在整篇作用域里等于配了一个永不生效的字段。
    dir.write("lint_rules.json", override_json(R"(, "TailChars": 30)"));
    CHECK(rejected().find("TailChars 仅在 Scope=paragraph 时有效") != std::string::npos);

    // 密度门限为 0：一条都不拦。
    dir.write("lint_rules.json", override_json(R"(, "MinDensityPerKChar": 0)"));
    CHECK(rejected().find("触发密度必须为正整数") != std::string::npos);
}

TEST_CASE("必填字段缺了：没写 Id、数据规则一条模式都没有、正则规则没写 Kind 也不行") {
    const TempDir dir;
    const EnvVar env("TEXTTOOL_LINT_RULES_JSON", (dir.path() / "lint_rules.json").string());

    std::vector<texttool::RuleOverride> overrides;
    std::string error;

    // 没写 Id：覆盖按 Id 认，报告也按 Id 指出是谁。
    dir.write("lint_rules.json", R"([{"Kind": "literal", "LiteralPatterns": ["甲"]}])");
    CHECK_FALSE(texttool::load_rule_overrides(nullptr, overrides, error));
    CHECK(error.find("没有写 Id") != std::string::npos);

    // 数据规则一条模式和词面都没有：永远命不中，与拼错 Id 一样让人以为它在跑。
    dir.write("lint_rules.json", R"([{"Id": "demo-rule", "Kind": "regex"}])");
    CHECK_FALSE(texttool::load_rule_overrides(nullptr, overrides, error));
    CHECK(error.find("没写任何词面或正则") != std::string::npos);

    // 空串模式会命中每一处。
    dir.write("lint_rules.json",
              R"([{"Id": "demo-rule", "Kind": "regex", "Patterns": [""] }])");
    CHECK_FALSE(texttool::load_rule_overrides(nullptr, overrides, error));
    CHECK(error.find("Patterns 里有空串") != std::string::npos);

    // 建议条数与模式条数对不上：后面的建议整体错位，读者照错的建议改稿。
    dir.write("lint_rules.json", R"([{"Id": "demo-rule", "Kind": "regex", "Patterns": ["甲", "乙"],)"
                                  R"( "Hints": ["只给一条"] }])");
    CHECK_FALSE(texttool::load_rule_overrides(nullptr, overrides, error));
    CHECK(error.find("Hints 条数与词面加正则对不上") != std::string::npos);
}

TEST_CASE("算法规则：算法名必须有人实现，模式与建议写了不生效") {
    const TempDir dir;
    const EnvVar env("TEXTTOOL_LINT_RULES_JSON", (dir.path() / "lint_rules.json").string());

    std::vector<texttool::RuleOverride> overrides;
    std::string error;

    dir.write("lint_rules.json", R"([{"Id": "demo-rule", "Kind": "algorithm"}])");
    CHECK_FALSE(texttool::load_rule_overrides(nullptr, overrides, error));
    CHECK(error.find("没写 AlgorithmName") != std::string::npos);

    dir.write("lint_rules.json",
              R"([{"Id": "demo-rule", "Kind": "algorithm", "AlgorithmName": "没有这一段"}])");
    CHECK_FALSE(texttool::load_rule_overrides(nullptr, overrides, error));
    CHECK(error.find("算法名没有实现：没有这一段") != std::string::npos);

    dir.write("lint_rules.json", R"([{"Id": "demo-rule", "Kind": "algorithm",)"
                                  R"( "AlgorithmName": "sentence-tempo", "Patterns": ["甲"]}])");
    CHECK_FALSE(texttool::load_rule_overrides(nullptr, overrides, error));
    CHECK(error.find("不能写 Patterns") != std::string::npos);

    // 反过来也一样：数据规则写了算法名，那个名字没人读。
    dir.write("lint_rules.json", R"([{"Id": "demo-rule", "Kind": "regex", "Patterns": ["甲"],)"
                                  R"( "AlgorithmName": "sentence-tempo"}])");
    CHECK_FALSE(texttool::load_rule_overrides(nullptr, overrides, error));
    CHECK(error.find("不能写 AlgorithmName") != std::string::npos);

    // 引擎里真有的算法名配算法规则：能读进来。
    dir.write("lint_rules.json", R"([{"Id": "sentence-tempo", "Kind": "algorithm",)"
                                  R"( "AlgorithmName": "sentence-tempo", "Title": "改过的标题"}])");
    CHECK(texttool::load_rule_overrides(nullptr, overrides, error));
    CHECK(error.empty());
    REQUIRE(overrides.size() == 1);
    CHECK(overrides[0].rule.title == "改过的标题");
}

TEST_CASE("文件本身坏了：不是 JSON、顶层不是数组、同一个 Id 写了两条") {
    const TempDir dir;
    const EnvVar env("TEXTTOOL_LINT_RULES_JSON", (dir.path() / "lint_rules.json").string());

    std::vector<texttool::RuleOverride> overrides;
    std::string error;

    dir.write("lint_rules.json", R"([{"Id": "demo-rule",)");
    CHECK_FALSE(texttool::load_rule_overrides(nullptr, overrides, error));
    CHECK(error.find("不是合法的 JSON") != std::string::npos);

    dir.write("lint_rules.json", R"({"Id": "demo-rule"})");
    CHECK_FALSE(texttool::load_rule_overrides(nullptr, overrides, error));
    CHECK(error.find("不是数组") != std::string::npos);

    dir.write("lint_rules.json", R"([{"Id": "demo-rule", "Kind": "literal", "LiteralPatterns": ["甲"]},)"
                                  R"( {"Id": "Demo-Rule", "Kind": "literal", "LiteralPatterns": ["乙"]}])");
    CHECK_FALSE(texttool::load_rule_overrides(nullptr, overrides, error));
    CHECK(error.find("Id 重复：Demo-Rule") != std::string::npos);
}

TEST_CASE("字段类型不对：说出是第几条的哪个字段") {
    const TempDir dir;
    // MinCount 写成字符串：读不进来，不能当成默认值接着跑。
    const fs::path file = dir.write("lint_rules.json", R"([{"Id": "demo-rule", "Kind": "literal",)"
                                                        R"( "LiteralPatterns": ["甲"], "MinCount": "两"}])");
    const EnvVar env("TEXTTOOL_LINT_RULES_JSON", file.string());

    std::vector<texttool::RuleOverride> overrides;
    std::string error;
    CHECK_FALSE(texttool::load_rule_overrides(nullptr, overrides, error));
    CHECK(error.find("第 1 条") != std::string::npos);
    CHECK(error.find("MinCount 不是整数") != std::string::npos);
}

// ================================================================
//  写回
// ================================================================

TEST_CASE("写回后读回一致：每个字段都写进去，也都能读回来") {
    const TempDir dir;
    const EnvVar env("TEXTTOOL_LINT_RULES_JSON", (dir.path() / "lint_rules.json").string());

    texttool::LintRule rule;
    rule.id = "demo-rule";
    rule.group = "group";
    rule.title = "标题";
    rule.detail = "说明";
    rule.kind = texttool::RuleKind::Regex;
    rule.patterns = {"甲", "乙"};
    rule.literal_patterns = std::vector<std::string>{"丙"};
    rule.hints = std::vector<std::string>{"一", "二", "三"};
    rule.suggest_scheme = std::string("第一套");
    rule.skip_in_markdown = true;
    rule.skip_in_blog = true;
    rule.genres = {"doc", "agent"};
    rule.min_count = 2;
    rule.severity = "warn";
    rule.blocks_reply = true;
    rule.reply_min_count = 3;
    rule.collapse_at_density = 4;
    // 密度门限归整篇作用域；段末窗口归段落作用域，两者放不进同一条，下面另起一条。
    rule.density = texttool::LintDensity{"delta", 2.5, std::nullopt};
    rule.min_density_per_kchar = std::nullopt;
    rule.default_off = true;

    texttool::LintRule paragraph_rule;
    paragraph_rule.id = "demo-paragraph";
    paragraph_rule.kind = texttool::RuleKind::Literal;
    paragraph_rule.literal_patterns = std::vector<std::string>{"丁"};
    paragraph_rule.scope = "paragraph";
    paragraph_rule.tail_chars = 30;

    REQUIRE(texttool::rule_problem(rule).empty());
    REQUIRE(texttool::rule_problem(paragraph_rule).empty());
    texttool::save_rule_override(nullptr, rule);
    texttool::save_rule_override(nullptr, paragraph_rule);

    std::vector<texttool::RuleOverride> overrides;
    std::string error;
    REQUIRE(texttool::load_rule_overrides(nullptr, overrides, error));
    REQUIRE(overrides.size() == 2);
    CHECK(overrides[0].rule == rule);
    CHECK(overrides[1].rule == paragraph_rule);
}

TEST_CASE("写回：只存改过的条目，目标那条按 Id 整条换掉，不追加第二条") {
    const TempDir dir;
    const fs::path file = dir.path() / "lint_rules.json";
    const EnvVar env("TEXTTOOL_LINT_RULES_JSON", file.string());

    texttool::LintRule first;
    first.id = "demo-a";
    first.kind = texttool::RuleKind::Literal;
    first.literal_patterns = std::vector<std::string>{"甲"};
    first.title = "第一条";
    texttool::save_rule_override(nullptr, first);

    texttool::LintRule second;
    second.id = "demo-b";
    second.kind = texttool::RuleKind::Literal;
    second.literal_patterns = std::vector<std::string>{"乙"};
    texttool::save_rule_override(nullptr, second);

    // 同一个 Id 再存一次：换掉，不新增。
    first.title = "第一条改过";
    first.min_count = 3;
    texttool::save_rule_override(nullptr, first);

    std::vector<texttool::RuleOverride> overrides;
    std::string error;
    REQUIRE(texttool::load_rule_overrides(nullptr, overrides, error));
    REQUIRE(overrides.size() == 2);
    CHECK(overrides[0].rule.id == "demo-a");
    CHECK(overrides[0].rule.title == "第一条改过");
    CHECK(overrides[0].rule.min_count == 3);
    CHECK(overrides[1].rule.id == "demo-b");
}

TEST_CASE("写回：这一版不认识的字段原样留着，不会被清空") {
    const TempDir dir;
    // 盘上那一条带着一个这一版没有的字段，模拟换个版本写过的同一份文件。
    const fs::path file = dir.write(
        "lint_rules.json",
        R"([{"Id": "demo-rule", "Kind": "literal", "LiteralPatterns": ["甲"],)"
        R"( "Title": "原来的标题", "FutureField": {"Keep": ["这些", "都留着"]}}])");
    const EnvVar env("TEXTTOOL_LINT_RULES_JSON", file.string());

    std::vector<texttool::RuleOverride> overrides;
    std::string error;
    REQUIRE(texttool::load_rule_overrides(nullptr, overrides, error));
    REQUIRE(overrides.size() == 1);

    // 界面改的是标题：从读回来的这条起步，只改它。
    texttool::LintRule edited = overrides[0].rule;
    edited.title = "改过的标题";
    texttool::save_rule_override(nullptr, edited);

    const std::string saved = dir.read("lint_rules.json");
    CHECK(saved.find("FutureField") != std::string::npos);
    CHECK(saved.find("这些") != std::string::npos);
    CHECK(saved.find("改过的标题") != std::string::npos);

    // 认得的字段也跟着改过的那一份走。
    std::vector<texttool::RuleOverride> reloaded;
    REQUIRE(texttool::load_rule_overrides(nullptr, reloaded, error));
    REQUIRE(reloaded.size() == 1);
    CHECK(reloaded[0].rule == edited);
}

// ================================================================
//  恢复内置
// ================================================================

TEST_CASE("单条恢复内置：去掉这一条，别的还在，合并结果回到内置那条") {
    const TempDir dir;
    const EnvVar env("TEXTTOOL_LINT_RULES_JSON", (dir.path() / "lint_rules.json").string());

    const std::string id = built_in().front().id;
    texttool::LintRule changed = built_in().front();
    changed.title = "改过的标题";
    texttool::save_rule_override(nullptr, changed);

    texttool::LintRule other;
    other.id = "user-demo";
    other.kind = texttool::RuleKind::Literal;
    other.literal_patterns = std::vector<std::string>{"探针"};
    texttool::save_rule_override(nullptr, other);

    texttool::restore_builtin_rule(nullptr, id);

    std::vector<texttool::RuleOverride> overrides;
    std::string error;
    REQUIRE(texttool::load_rule_overrides(nullptr, overrides, error));
    REQUIRE(overrides.size() == 1);
    CHECK(overrides[0].rule.id == "user-demo");

    std::vector<texttool::LintRule> merged;
    REQUIRE(texttool::load_rules_with_overrides(TEXTTOOL_RULES_JSON, nullptr, merged, error));
    const texttool::LintRule* restored = find_rule(merged, id);
    REQUIRE(restored != nullptr);
    CHECK(*restored == built_in().front());
}

TEST_CASE("单条恢复内置：文件里本来没有这一条时不写盘，不留下空文件") {
    const TempDir dir;
    const fs::path file = dir.path() / "lint_rules.json";
    const EnvVar env("TEXTTOOL_LINT_RULES_JSON", file.string());

    texttool::restore_builtin_rule(nullptr, built_in().front().id);
    CHECK_FALSE(fs::exists(file));
}

TEST_CASE("全部恢复内置：文件清空，合并结果与内置一字不差") {
    const TempDir dir;
    const fs::path file = dir.path() / "lint_rules.json";
    const EnvVar env("TEXTTOOL_LINT_RULES_JSON", file.string());

    texttool::LintRule changed = built_in().front();
    changed.severity = "warn";
    texttool::save_rule_override(nullptr, changed);
    REQUIRE(fs::exists(file));

    texttool::restore_all_builtin_rules(nullptr);

    std::vector<texttool::RuleOverride> overrides;
    std::string error;
    REQUIRE(texttool::load_rule_overrides(nullptr, overrides, error));
    CHECK(overrides.empty());

    std::vector<texttool::LintRule> merged;
    REQUIRE(texttool::load_rules_with_overrides(TEXTTOOL_RULES_JSON, nullptr, merged, error));
    CHECK(merged == built_in());
}

// ================================================================
//  位置
// ================================================================

TEST_CASE("覆盖文件的位置：有环境变量就写它指的那份，没有就写程序目录") {
    CHECK(texttool::lint_rules_override_file(nullptr) ==
          fs::current_path() / "lint_rules.json");

    const TempDir dir;
    const fs::path file = dir.path() / "别处.json";
    const EnvVar env("TEXTTOOL_LINT_RULES_JSON", file.string());
    CHECK(texttool::lint_rules_override_file(nullptr) == file);
}
