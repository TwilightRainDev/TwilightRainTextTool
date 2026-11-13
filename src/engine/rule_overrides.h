// 用户改过的检查规则：程序目录里的 lint_rules.json。
//
// 只存改过的条目：一条都没有就等于全走内置。读的时候按 Id 整条覆盖内置那条（位置留在原处），
// 内置表里没有的 Id 接在末尾：手改文件把 Id 写错时，结果会多出一条规则，而不是悄悄顶掉别的。
// Id 不分大小写比，与报告、--only、规则表里写的那套口径一致。
//
// 位置口径与替换规则、方案那几份相同：有环境变量 TEXTTOOL_LINT_RULES_JSON 就写它指的那份，
// 否则写程序所在目录的 lint_rules.json。命令行与界面读的是同一份文件，
// 界面上改一条，命令行下一次跑就是改过的。
//
// 文件形状：顶层数组，一项一条规则，字段与 default_lint_rules.json 完全相同，写法也一样
// （没写的字段取默认值，null 与没写等价）。文件不在、或者里面只有空白，都等于没有覆盖；
// 读不出来、解析不了、字段不对都算损坏，不静默降级成空表。
//
// 写盘走 engine/io 的 write_text：先写临时文件再整体改名。校验不过时不写盘，
// 盘上原来那份一个字没动。
#pragma once

#include <filesystem>
#include <string>
#include <vector>

#include "engine/rules.h"

namespace texttool {

// 覆盖文件的位置：环境变量 TEXTTOOL_LINT_RULES_JSON 指的那份，没指就是程序所在目录的
// lint_rules.json。读与写用同一个位置。
std::filesystem::path lint_rules_override_file(const char* argv0);

// 文件里的一条：解析出来的规则，加上文件里那一项的对象原文。
// 原文留着是有用的：写回时以它为底，这一版不认识的字段（换台机器、换个版本写过同一份文件）
// 原样带过去，不会因为这里存过一次就消失。
struct RuleOverride {
    LintRule rule;
    std::string raw;
};

// 读覆盖文件。文件不在、里面只有空白都等于没有覆盖：返回 true，overrides 为空。
// 读不出来、解析不了、字段类型不对、字段取值站不住、正则编不过、算法名没人实现时返回 false，
// error 里说清是哪一个文件、第几条、哪一条 Id、哪个字段，照句子改那一处就行。
bool load_rule_overrides(const char* argv0, std::vector<RuleOverride>& out, std::string& error);

// 把覆盖并到内置表上：按 Id 整条覆盖内置那条，内置里没有的 Id 接在末尾。
// 进来的两份都该是校验过的（load_rules 与 load_rule_overrides 各自验过一遍）。
std::vector<LintRule> merge_rule_overrides(const std::vector<LintRule>& built_in,
                                          const std::vector<RuleOverride>& overrides);

// 读内置规则表并合上覆盖：命令行与界面拿规则表都走这一处，两边看到的是同一张表。
// 返回 false 时 error 说清是哪一步不行（内置表打不开，或者覆盖文件损坏），out 不可用。
bool load_rules_with_overrides(const std::filesystem::path& built_in_path, const char* argv0,
                               std::vector<LintRule>& out, std::string& error);

// 一条改过的规则能不能跑：字段取值、必填字段、正则能不能编、算法名有没有实现。
// 通过返回空串；不通过返回给人看的一句话，里面带规则 Id 与字段名。
// 界面拿它在弹窗里当场提示，存盘前再走一遍同一份，两处判出同一个结果。
std::string rule_problem(const LintRule& rule);

// 存一条改过的条目：文件里已有同 Id 的那一条就整条换掉，没有就接在末尾。
// 校验不过就抛 std::runtime_error（消息就是 rule_problem 那一句），盘上一个字不动。
void save_rule_override(const char* argv0, const LintRule& rule);

// 恢复内置：把这一个 Id 从覆盖文件里去掉，规则回到内置那条。文件里本来没有这一条时
// 不写盘。内置表里没有的 Id（也就是自己加的条目）同样是去掉这一条。
void restore_builtin_rule(const char* argv0, const std::string& id);

// 全部恢复内置：文件内容改写成一个空数组，文件本身留着。
// 读的时候空数组与文件不在是一回事，所以删掉文件也等于全部恢复。
void restore_all_builtin_rules(const char* argv0);

}  // namespace texttool
