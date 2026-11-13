// 规则表的解析与校验。字段名照规则文件里的写法，没写的字段取规则表规定的默认值，
// 写成 null 与没写等价。
//
// 类型不对一律报错并指出是第几条：规则表是唯一数据源，读错时宁可起不来，
// 也不能悄悄少读几条，那会让报告少几行，读者分不出「没问题」与「没跑」。
//
// 同一套字段解析与同一套校验，内置规则表和用户改过的条目（lint_rules.json）都走这里：
// 两边各写一份的话，会出现「内置读得进、覆盖读不进」这种说不清谁对的分歧。
// 报错句子的定位只差前面几个字：一份说「规则表」，另一份说「规则覆盖文件」加文件路径。
#include "engine/rules.h"

#include <cstdlib>

#include <fstream>
#include <sstream>
#include <stdexcept>

#include "json/json.hpp"

namespace texttool {

namespace {

using json = nlohmann::json;

// 规则文件里的一条。where 是报错句子的定位：哪个文件、第几条，条号从 1 数给人看。
struct Row {
    const json& value;
    std::string where;

    [[noreturn]] void fail(const std::string& what) const { throw std::runtime_error(where + what); }
};

// 没写与写成 null 是同一件事：都表示这份文件没给这一项。
const json* member(const Row& row, const char* key) {
    const auto it = row.value.find(key);
    if (it == row.value.end() || it->is_null()) return nullptr;
    return &*it;
}

std::optional<std::string> opt_string(const Row& row, const char* key) {
    const json* value = member(row, key);
    if (value == nullptr) return std::nullopt;
    if (!value->is_string()) row.fail(std::string("的 ") + key + " 不是字符串");
    return value->get<std::string>();
}

std::optional<int> opt_int(const Row& row, const char* key) {
    const json* value = member(row, key);
    if (value == nullptr) return std::nullopt;
    if (!value->is_number_integer()) row.fail(std::string("的 ") + key + " 不是整数");
    return value->get<int>();
}

std::optional<double> opt_double(const Row& row, const char* key) {
    const json* value = member(row, key);
    if (value == nullptr) return std::nullopt;
    if (!value->is_number()) row.fail(std::string("的 ") + key + " 不是数字");
    return value->get<double>();
}

bool flag(const Row& row, const char* key, bool fallback) {
    const json* value = member(row, key);
    if (value == nullptr) return fallback;
    if (!value->is_boolean()) row.fail(std::string("的 ") + key + " 不是真假值");
    return value->get<bool>();
}

std::string text(const Row& row, const char* key, const char* fallback) {
    const json* value = member(row, key);
    if (value == nullptr) return fallback;
    if (!value->is_string()) row.fail(std::string("的 ") + key + " 不是字符串");
    return value->get<std::string>();
}

std::optional<std::vector<std::string>> opt_strings(const Row& row, const char* key) {
    const json* value = member(row, key);
    if (value == nullptr) return std::nullopt;
    if (!value->is_array()) row.fail(std::string("的 ") + key + " 不是数组");
    std::vector<std::string> out;
    out.reserve(value->size());
    for (const auto& item : *value) {
        if (!item.is_string()) row.fail(std::string("的 ") + key + " 里有不是字符串的元素");
        out.push_back(item.get<std::string>());
    }
    return out;
}

std::vector<std::string> strings_or_empty(const Row& row, const char* key) {
    auto value = opt_strings(row, key);
    return value ? std::move(*value) : std::vector<std::string>{};
}

RuleKind kind_of(const Row& row) {
    const std::string name = text(row, "Kind", "regex");
    const auto kind = parse_rule_kind(name);
    if (!kind) row.fail("的 Kind 非法：" + name + "（可选 literal、regex、algorithm）");
    return *kind;
}

std::optional<LintDensity> density_of(const Row& row) {
    const json* value = member(row, "Density");
    if (value == nullptr) return std::nullopt;
    if (!value->is_object()) row.fail("的 Density 不是对象");
    const Row inner{*value, row.where};
    LintDensity density;
    density.basis = text(inner, "Basis", "document");
    density.min = opt_double(inner, "Min").value_or(0.0);
    density.window = opt_int(inner, "Window");
    return density;
}

LintRule rule_at(const Row& row) {
    if (!row.value.is_object()) row.fail("不是对象");

    LintRule rule;
    rule.id = text(row, "Id", "");
    rule.group = text(row, "Group", "");
    rule.title = text(row, "Title", "");
    rule.detail = text(row, "Detail", "");
    rule.kind = kind_of(row);
    rule.algorithm_name = opt_string(row, "AlgorithmName");
    rule.patterns = strings_or_empty(row, "Patterns");
    rule.literal_patterns = opt_strings(row, "LiteralPatterns");
    rule.hints = opt_strings(row, "Hints");
    rule.suggest_scheme = opt_string(row, "SuggestScheme");
    rule.skip_in_markdown = flag(row, "SkipInMarkdown", false);
    rule.skip_in_blog = flag(row, "SkipInBlog", false);
    rule.scope = text(row, "Scope", "document");
    rule.genres = strings_or_empty(row, "Genres");
    rule.tail_chars = opt_int(row, "TailChars");
    rule.min_count = opt_int(row, "MinCount").value_or(1);
    rule.severity = text(row, "Severity", "info");
    rule.blocks_reply = flag(row, "BlocksReply", false);
    rule.reply_min_count = opt_int(row, "ReplyMinCount");
    rule.collapse_at_density = opt_int(row, "CollapseAtDensity");
    rule.density = density_of(row);
    rule.min_density_per_kchar = opt_int(row, "MinDensityPerKChar");
    rule.default_off = flag(row, "DefaultOff", false);
    return rule;
}

std::vector<LintRule> rules_from_json(const json& root, const char* file) {
    if (!root.is_array()) throw std::runtime_error(std::string(file) + "最外层不是数组");
    std::vector<LintRule> rules;
    rules.reserve(root.size());
    for (size_t i = 0; i < root.size(); ++i) {
        rules.push_back(rule_at(Row{root[i], std::string(file) + "第 " + std::to_string(i + 1) + " 条"}));
    }
    for (const LintRule& rule : rules) {
        validate_rule(rule);
    }
    return rules;
}

}  // namespace

std::string_view to_string(RuleKind kind) {
    switch (kind) {
        case RuleKind::Literal: return "literal";
        case RuleKind::Regex: return "regex";
        case RuleKind::Algorithm: return "algorithm";
    }
    return "regex";
}

std::optional<RuleKind> parse_rule_kind(std::string_view name) {
    if (name == "literal") return RuleKind::Literal;
    if (name == "regex") return RuleKind::Regex;
    if (name == "algorithm") return RuleKind::Algorithm;
    return std::nullopt;
}

// 读进来就地校验：自相矛盾的配置会让规则永远不生效，或者换个方式算，
// 读者分不出「没问题」与「没跑」。报错指明是哪条规则的哪个字段，
// 照句子改那一个字段就能修好。读到不合格的一条就不往下跑。
//
// 只查「取值本身站不站得住」：正则能不能编、算法名有没有实现不在这里查，
// 那两样要动手编译或查表，由用户改条目时那一层做（见 engine/rule_overrides.h）。
void validate_rule(const LintRule& rule) {
    // Id 是覆盖的依据，也是报告里对上的那一行；空 Id 没法按它覆盖，也没法在报告里指出是谁。
    if (rule.id.empty()) throw std::runtime_error("规则没有写 Id：覆盖与报告都按 Id 认这一条");

    const auto fail = [&rule](const std::string& what) {
        throw std::runtime_error("规则 " + rule.id + " 的" + what);
    };

    if (rule.severity != "info" && rule.severity != "warn")
        fail(" Severity 非法：" + rule.severity + "（可选 info、warn）");

    // 适用范围只有这两档：拼成别的写法时，按段落办的那些处理一档都命中不了。
    if (rule.scope != "document" && rule.scope != "paragraph")
        fail(" Scope 非法：" + rule.scope + "（可选 document、paragraph）");

    // 拼错的档位会让规则在任何 --genre 下都不跑，与规则名打错一样致命。
    for (const std::string& genre : rule.genres) {
        if (genre != "doc" && genre != "fiction" && genre != "agent")
            fail("档位非法：" + genre + "（可选 doc、fiction、agent）");
    }

    // 段末窗口在整篇作用域里读不到：写进来等于配了一个永远不生效的字段。
    if (rule.tail_chars) {
        if (*rule.tail_chars < 1) fail(" TailChars 必须为正整数");
        if (rule.scope != "paragraph") fail(" TailChars 仅在 Scope=paragraph 时有效");
    }

    // 段落作用域拿不到全文长度，密度算不出每千字；按段落算的基准不在这里拦。
    const bool has_density = rule.density.has_value() || rule.min_density_per_kchar.has_value();
    const bool by_paragraph = rule.density && rule.density->basis == "paragraph";
    if (has_density && !by_paragraph && rule.scope != "document")
        fail("触发密度仅在 Scope=document 时有效");

    // 两种写法同时出现时说不清哪个算数，读者会以为自己改的那份生效了。
    if (rule.density && rule.min_density_per_kchar)
        fail("的触发密度写了两种：Density 与 MinDensityPerKChar 只能留一种");

    if (rule.density) {
        const std::string& basis = rule.density->basis;
        // 四个取值都认，但只有 document 与 delta 实现了：写另外两个，扫到这条时响亮报「尚未实现」。
        const bool known = basis == "document" || basis == "paragraph" || basis == "window" ||
                           basis == "delta";
        if (!known)
            fail("的密度基准非法：" + basis + "（可选 document、paragraph、window、delta）");
        // 门限为 0 等于没配：一条都不拦，读者会以为这条在按密度筛。
        if (rule.density->min <= 0) fail("的触发密度必须为正数");
        if (rule.density->window) {
            if (*rule.density->window < 1) fail("的窗口长度必须为正整数");
            // 写了窗口却按别的基准算，读者会以为窗口生效了。
            if (basis != "window") fail("的 Window 仅在密度基准为 window 时有效");
        }
        // 写了窗口基准却没给长度，算出来的是一整篇的密度，不是窗口里的。
        if (basis == "window" && !rule.density->window)
            fail("的密度基准为 window，但没有给窗口长度");
    }

    if (rule.min_density_per_kchar && *rule.min_density_per_kchar < 1)
        fail("的触发密度必须为正整数");
    if (rule.collapse_at_density && *rule.collapse_at_density < 1)
        fail("的折叠密度必须为正整数");
    if (rule.min_count < 1) fail("的 MinCount 必须 >= 1");
    if (rule.reply_min_count && *rule.reply_min_count < 1) fail("的回复门限必须 >= 1");

    // 空串模式会命中每一处：写进来是配置错，不是「暂时留空」。
    for (const std::string& pattern : rule.patterns)
        if (pattern.empty()) fail("的 Patterns 里有空串：空串会命中每一处");
    for (const std::string& pattern : rule.literal_patterns.value_or(std::vector<std::string>{}))
        if (pattern.empty()) fail("的 LiteralPatterns 里有空串：空串会命中每一处");

    const bool has_literal = rule.literal_patterns && !rule.literal_patterns->empty();
    if (rule.kind == RuleKind::Algorithm) {
        // 算法规则的实现只能由算法名指认：名字没写或拼错，这条规则在报告里永远沉默。
        if (!rule.algorithm_name || rule.algorithm_name->empty())
            fail("没写 AlgorithmName：算法规则靠它指到引擎里的实现，缺了这条永远跑不到");
        // 算法规则的模式与建议都由代码给，表里再写一份只会让人以为改它有用。
        if (!rule.patterns.empty())
            fail("是算法规则，不能写 Patterns：命不命中由算法算，词表写了也不生效");
        if (has_literal) fail("是算法规则，不能写 LiteralPatterns：命不命中由算法算，词面写了也不生效");
        if (rule.hints) fail("是算法规则，不能写 Hints：建议改法由算法给，写进来只会对不上");
    } else {
        if (rule.algorithm_name)
            fail("是数据规则，不能写 AlgorithmName：" + *rule.algorithm_name);
        // 一条模式和词面都没有的数据规则永远命不中，与拼错 Id 一样让人以为它在跑。
        if (rule.patterns.empty() && !has_literal) fail("没写任何词面或正则：数据规则至少给一条");
    }

    // 建议改法按「词面在前、正则在后」合并后的顺序排，条数必须对得上：
    // 少一条的后果是后面的建议整体错位，读者照错的建议改稿。
    if (rule.hints) {
        const std::size_t total = rule.patterns.size() + rule.literal_patterns.value_or(
                                                          std::vector<std::string>{})
                                                          .size();
        if (rule.hints->size() != total)
            fail("的 Hints 条数与词面加正则对不上：" + std::to_string(rule.hints->size()) +
                 " 条建议对 " + std::to_string(total) + " 条模式");
    }
}

std::vector<LintRule> parse_rules(std::string_view json_text) {
    json root;
    try {
        root = json::parse(json_text.begin(), json_text.end());
    } catch (const json::parse_error& error) {
        throw std::runtime_error(std::string("规则表不是合法的 JSON：") + error.what());
    }
    return rules_from_json(root, "规则表");
}

LintRule parse_rule_row(std::string_view json_text, const std::string& where) {
    json row;
    try {
        row = json::parse(json_text.begin(), json_text.end());
    } catch (const json::parse_error&) {
        throw std::runtime_error(where + "读不成一条规则");
    }
    return rule_at(Row{row, where});
}

std::vector<LintRule> load_rules(const std::string& path) {
    std::ifstream in(path, std::ios::binary);
    if (!in) throw std::runtime_error("打不开规则表：" + path);
    std::ostringstream buffer;
    buffer << in.rdbuf();
    return parse_rules(buffer.str());
}

std::optional<std::filesystem::path> find_rules_file(const std::filesystem::path& start_dir) {
    std::error_code ignored;
    if (const char* env = std::getenv("TEXTTOOL_RULES_JSON"); env != nullptr && *env != '\0') {
        if (std::filesystem::is_regular_file(env, ignored)) return std::filesystem::path(env);
    }

    std::filesystem::path start = start_dir;
    for (int depth = 0; depth < 6 && !start.empty(); ++depth) {
        const std::filesystem::path candidates[] = {
            start / "default_lint_rules.json",
            start / "assets" / "default_lint_rules.json",
        };
        for (const auto& candidate : candidates)
            if (std::filesystem::is_regular_file(candidate, ignored)) return candidate;

        const std::filesystem::path parent = start.parent_path();
        if (parent == start) break;
        start = parent;
    }
    return std::nullopt;
}

}  // namespace texttool
