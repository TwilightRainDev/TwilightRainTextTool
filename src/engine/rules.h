// 规则表的数据结构，字段与规则文件里的写法一一对应。
//
// 字段看着碎，是因为报告里每一句话、界面上每一行文案都由这些字段决定：
// 少读一个字段，输出就跟 fixtures 下的期望输出对不上，而那是按字节比的，没有「差不多」。
//
// 可空的地方保持可空：null 用来区分「这份规则文件没写这一项」与「写了但是空的」，
// 两者在合并外部规则、判断哪些文字用了自定义值时要分开看，用空字符串代替会丢掉这个区别。
#pragma once

#include <filesystem>
#include <optional>
#include <string>
#include <string_view>
#include <vector>

namespace texttool {

// 去哪儿找规则表，界面与命令行共用这一处，两边不会各找各的。
// 先看环境变量 TEXTTOOL_RULES_JSON；再从 start 这一层往上找六层，每层试两个位置：
// default_lint_rules.json、assets/default_lint_rules.json。都找不到返回空。
// 打包发布时把规则表放在程序旁边，第一个位置就命中。
std::optional<std::filesystem::path> find_rules_file(const std::filesystem::path& start);

// 规则的三种驱动方式：词面、正则、由代码计算的算法。
enum class RuleKind { Literal, Regex, Algorithm };

// 规则表里写的是字符串，比较与输出都用字符串。
std::string_view to_string(RuleKind kind);
std::optional<RuleKind> parse_rule_kind(std::string_view text);

// 密度判据的基准。paragraph 与 window 只做了取值校验，扫到时会响亮报错。
struct LintDensity {
    std::string basis = "document";
    double min = 0.0;
    std::optional<int> window;

    bool operator==(const LintDensity&) const = default;
};

struct LintRule {
    std::string id;
    std::string group;
    std::string title;
    std::string detail;

    RuleKind kind = RuleKind::Regex;
    std::optional<std::string> algorithm_name;
    std::vector<std::string> patterns;
    std::optional<std::vector<std::string>> literal_patterns;
    std::optional<std::vector<std::string>> hints;
    std::optional<std::string> suggest_scheme;

    bool skip_in_markdown = false;
    bool skip_in_blog = false;
    std::string scope = "document";
    std::vector<std::string> genres;

    std::optional<int> tail_chars;
    int min_count = 1;
    std::string severity = "info";
    bool blocks_reply = false;
    std::optional<int> reply_min_count;
    std::optional<int> collapse_at_density;
    std::optional<LintDensity> density;
    std::optional<int> min_density_per_kchar;
    bool default_off = false;

    bool operator==(const LintRule&) const = default;
};

// 读规则表文件。文件打不开、JSON 结构不对、字段类型不对都会抛 std::runtime_error，
// 消息里带出错的位置，照消息修文件即可。
std::vector<LintRule> load_rules(const std::string& path);

// 从内存里的 JSON 文本解析，测试与界面里的导入路径用它。
std::vector<LintRule> parse_rules(std::string_view json_text);

// 读一条规则对象（用户改过的条目就是一条一条存在覆盖文件里的）。
// where 是报错句子的定位：前面几个字说清哪个文件，后面跟着第几条。
// 字段语义与整份规则表相同：没写的字段取默认值，null 与没写等价。
// 只管字段类型：取值站不站得住由 validate_rule 查，调用方先定位再校验，报错句子才带得上文件。
LintRule parse_rule_row(std::string_view json_text, const std::string& where);

// 一条规则站不站得住：取值在不在枚举里、阈值是不是正数、必填字段在不在、
// 有没有「写了也不生效」的组合。不过就抛 std::runtime_error，消息里带规则 Id 与字段名。
//
// 读规则表与校验用户改过的条目走同一份：两处各写一套，就会出现「内置读得进、
// 覆盖读不进」这类说不清谁对的分歧。
// 正则能不能编、算法名有没有实现不在这里查：那两样要动手编译或查登记表，
// 由写覆盖文件那一层做（见 engine/rule_overrides.h）。
void validate_rule(const LintRule& rule);

}  // namespace texttool
