// 用户改过的检查规则：读、校验、写回、恢复内置。
//
// 这一层只认文件与规则表，不碰命令行参数与界面：命令行读规则表时顺手合上覆盖，
// 界面上改完调这里存一条，两边走同一个位置、同一套校验。
//
// 字段解析与字段取值校验都在 engine/rules 里，与读内置规则表共用一份；
// 这里补上两样只有动手才看得出来的检查：正则能不能编、算法名有没有人实现。
// 内置表不查这两样，它随程序发布，写坏了在构建与逐字节对照里就红；
// 覆盖文件是用户在盘上手改的，要在写进去之前挡住，否则下一次启动直接抛。
#include "engine/rule_overrides.h"

#include <algorithm>
#include <cstddef>
#include <cstdlib>
#include <exception>
#include <fstream>
#include <iterator>
#include <optional>
#include <stdexcept>
#include <string_view>
#include <utility>

#include "engine/io.h"
#include "engine/regex.h"
#include "engine/schemes.h"
#include "engine/scan.h"
#include "json/json.hpp"

namespace texttool {
namespace {

namespace fs = std::filesystem;
using Json = nlohmann::json;

// 文件名与报错句子里的说法只有一处来源。
constexpr char kFileName[] = "lint_rules.json";

// 环境变量指的那份文件；没指时返回空。
std::optional<fs::path> env_path(const char* name) {
    const char* value = std::getenv(name);
    if (value == nullptr || *value == '\0') return std::nullopt;
    return fs::path(value);
}

// Id 比法：不分大小写，与报告、--only 认规则的口径一致。
bool same_id(std::string_view left, std::string_view right) {
    if (left.size() != right.size()) return false;
    for (std::size_t at = 0; at < left.size(); ++at) {
        const auto lower = [](char c) {
            return (c >= 'A' && c <= 'Z') ? static_cast<char>(c - 'A' + 'a') : c;
        };
        if (lower(left[at]) != lower(right[at])) return false;
    }
    return true;
}

// 整份文件内容。规则文件都是几 KB，整份读进来比分块省事。
std::string read_all(std::istream& in) {
    return std::string(std::istreambuf_iterator<char>(in), std::istreambuf_iterator<char>());
}

// 全是空白：空文件与只按了几个回车的文件都算「没写过」。
bool only_blank(const std::string& text) {
    return std::all_of(text.begin(), text.end(), [](char c) {
        return c == ' ' || c == '\t' || c == '\r' || c == '\n';
    });
}

// 可空字段：有值就写进去，没有就把键去掉。
// 去掉是必须的：盘上那一条原来有这一项时，留着等于把旧值悄悄带进新条目。
template <class T>
void set_or_erase(Json& row, const char* key, const std::optional<T>& value) {
    if (value)
        row[key] = *value;
    else
        row.erase(key);
}

// 一条规则写成规则文件里的那一项。
//
// 认识的字段逐个覆盖，其余字段（这一版不认识的）从原文里原样留着：
// 从空白对象重建的话，往后新加的字段会在用户存一次之后悄悄消失。
// 可空的那几项没有值就把键去掉，读回来与写成 null 是一回事。
Json row_json(const RuleOverride& override_rule) {
    Json row = Json::object();
    if (!override_rule.raw.empty()) {
        try {
            Json parsed = Json::parse(override_rule.raw);
            if (parsed.is_object()) row = std::move(parsed);
        } catch (const std::exception&) {
            // 原文读不成对象：按空对象重写，不把半份坏数据接着写回去。
        }
    }

    const LintRule& rule = override_rule.rule;
    row["Id"] = rule.id;
    row["Group"] = rule.group;
    row["Title"] = rule.title;
    row["Detail"] = rule.detail;
    row["Kind"] = std::string(to_string(rule.kind));
    row["Patterns"] = rule.patterns;
    row["SkipInMarkdown"] = rule.skip_in_markdown;
    row["SkipInBlog"] = rule.skip_in_blog;
    row["Scope"] = rule.scope;
    row["Genres"] = rule.genres;
    row["MinCount"] = rule.min_count;
    row["Severity"] = rule.severity;
    row["BlocksReply"] = rule.blocks_reply;
    row["DefaultOff"] = rule.default_off;

    set_or_erase(row, "AlgorithmName", rule.algorithm_name);
    set_or_erase(row, "LiteralPatterns", rule.literal_patterns);
    set_or_erase(row, "Hints", rule.hints);
    set_or_erase(row, "SuggestScheme", rule.suggest_scheme);
    set_or_erase(row, "TailChars", rule.tail_chars);
    set_or_erase(row, "ReplyMinCount", rule.reply_min_count);
    set_or_erase(row, "CollapseAtDensity", rule.collapse_at_density);
    set_or_erase(row, "MinDensityPerKChar", rule.min_density_per_kchar);

    if (rule.density) {
        Json density = Json::object();
        density["Basis"] = rule.density->basis;
        density["Min"] = rule.density->min;
        if (rule.density->window) density["Window"] = *rule.density->window;
        row["Density"] = std::move(density);
    } else {
        row.erase("Density");
    }
    return row;
}

// 把整张覆盖表写回盘上。先写临时文件再整体改名，写不进去时原来那份一个字没动。
void write_overrides(const fs::path& path, const std::vector<RuleOverride>& overrides) {
    Json array = Json::array();
    for (const RuleOverride& override_rule : overrides) array.push_back(row_json(override_rule));

    std::string text;
    try {
        text = array.dump(2) + "\n";
    } catch (const std::exception&) {
        // 不能写成合法 UTF-8 的文字写不进 JSON：宁可不写，也不留一份读不回来的规则。
        throw std::runtime_error("这几条里有认不出的字符，没能存下来");
    }
    write_text(path.string(), text, TextEncoding::Utf8);
}

}  // namespace

fs::path lint_rules_override_file(const char* argv0) {
    return env_path("TEXTTOOL_LINT_RULES_JSON").value_or(program_dir(argv0) / kFileName);
}

std::string rule_problem(const LintRule& rule) {
    try {
        validate_rule(rule);
    } catch (const std::exception& problem) {
        return problem.what();
    }

    // 算法名拼错或没人实现，这条规则扫到就抛：写进去等于让下一次启动直接报错。
    if (rule.kind == RuleKind::Algorithm && rule.algorithm_name &&
        !algorithm_is_registered(*rule.algorithm_name))
        return "规则 " + rule.id + " 的算法名没有实现：" + *rule.algorithm_name;

    // 正则编不过，同样扫到就抛。词面那一路进来时已经转义过，怎么填都能编。
    if (rule.kind == RuleKind::Regex) {
        for (const std::string& pattern : rule.patterns) {
            try {
                const Pattern probe(pattern);
                (void)probe;
            } catch (const std::exception& problem) {
                return "规则 " + rule.id + " 的正则非法：" + pattern + "（" + problem.what() + "）";
            }
        }
    }
    return {};
}

bool load_rule_overrides(const char* argv0, std::vector<RuleOverride>& out, std::string& error) {
    out.clear();
    error.clear();
    const fs::path path = lint_rules_override_file(argv0);

    std::error_code ignored;
    if (!fs::is_regular_file(path, ignored)) return true;  // 还没改过：全走内置

    std::ifstream in(path, std::ios::binary);
    if (!in) {
        error = "规则覆盖文件读不了：" + path.string();
        return false;
    }
    const std::string bytes = read_all(in);
    // 空文件等于没改过：界面清空文件、或者刚建一个空文件，都按全走内置算。
    if (only_blank(bytes)) return true;

    Json root;
    try {
        root = Json::parse(bytes);
    } catch (const std::exception& problem) {
        error = "规则覆盖文件不是合法的 JSON：" + path.string() + "（" + problem.what() + "）";
        return false;
    }
    if (!root.is_array()) {
        error = "规则覆盖文件里不是数组：" + path.string() + "（一条规则一项，照 default_lint_rules.json 的写法）";
        return false;
    }

    out.reserve(root.size());
    for (std::size_t index = 0; index < root.size(); ++index) {
        const std::string where =
            "规则覆盖文件 " + path.string() + " 第 " + std::to_string(index + 1) + " 条";

        RuleOverride entry;
        try {
            entry.raw = root[index].dump();
            entry.rule = parse_rule_row(entry.raw, where);
        } catch (const std::exception& problem) {
            // 定位句里已经带了文件与条号，原样交出去。
            error = problem.what();
            return false;
        }

        // 同一个 Id 写两条时，合并按「后者覆盖前者」取，用户无从知道自己的配置里有两份。
        for (const RuleOverride& seen : out)
            if (same_id(seen.rule.id, entry.rule.id)) {
                error = where + "：Id 重复：" + entry.rule.id;
                return false;
            }

        const std::string problem = rule_problem(entry.rule);
        if (!problem.empty()) {
            error = where + "：" + problem;
            return false;
        }
        out.push_back(std::move(entry));
    }
    return true;
}

std::vector<LintRule> merge_rule_overrides(const std::vector<LintRule>& built_in,
                                           const std::vector<RuleOverride>& overrides) {
    std::vector<LintRule> merged = built_in;
    for (const RuleOverride& override_rule : overrides) {
        auto found = std::find_if(merged.begin(), merged.end(), [&](const LintRule& rule) {
            return same_id(rule.id, override_rule.rule.id);
        });
        if (found != merged.end()) {
            // 整条换掉，位置留在原处：规则表的次序决定报告里分组的先后，覆盖不该把顺序打乱。
            *found = override_rule.rule;
        } else {
            merged.push_back(override_rule.rule);
        }
    }
    return merged;
}

bool load_rules_with_overrides(const fs::path& built_in_path, const char* argv0,
                               std::vector<LintRule>& out, std::string& error) {
    out.clear();
    error.clear();

    std::vector<LintRule> built_in;
    try {
        built_in = load_rules(built_in_path.string());
    } catch (const std::exception& problem) {
        error = problem.what();
        return false;
    }

    std::vector<RuleOverride> overrides;
    if (!load_rule_overrides(argv0, overrides, error)) return false;

    out = merge_rule_overrides(built_in, overrides);
    return true;
}

void save_rule_override(const char* argv0, const LintRule& rule) {
    // 先校验：不过就抛，盘上一个字不动。
    if (const std::string problem = rule_problem(rule); !problem.empty())
        throw std::runtime_error(problem);

    std::vector<RuleOverride> overrides;
    std::string error;
    if (!load_rule_overrides(argv0, overrides, error)) throw std::runtime_error(error);

    RuleOverride entry;
    entry.rule = rule;
    bool replaced = false;
    for (RuleOverride& existing : overrides) {
        if (!same_id(existing.rule.id, rule.id)) continue;
        // 盘上已经有同 Id 的一条：把它那一项的原文当底，这一版不认识的字段原样带过去。
        entry.raw = existing.raw;
        existing = std::move(entry);
        replaced = true;
        break;
    }
    if (!replaced) overrides.push_back(std::move(entry));

    write_overrides(lint_rules_override_file(argv0), overrides);
}

void restore_builtin_rule(const char* argv0, const std::string& id) {
    std::vector<RuleOverride> overrides;
    std::string error;
    if (!load_rule_overrides(argv0, overrides, error)) throw std::runtime_error(error);

    const auto kept = std::remove_if(overrides.begin(), overrides.end(),
                                     [&](const RuleOverride& item) {
                                         return same_id(item.rule.id, id);
                                     });
    // 文件里本来没有这一条：不写盘，不留下一个空文件。
    if (kept == overrides.end()) return;
    overrides.erase(kept, overrides.end());
    write_overrides(lint_rules_override_file(argv0), overrides);
}

void restore_all_builtin_rules(const char* argv0) {
    write_overrides(lint_rules_override_file(argv0), {});
}

}  // namespace texttool
