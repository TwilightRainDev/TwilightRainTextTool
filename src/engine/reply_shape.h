// 回复检查：回复里「离开文件就看不懂」的写法，一行一处。
//
// 判据与正则都取自规则表：本文件只认 RuleIds 里那十八条，其余规则不参与。
// 围栏代码块整块跳过：那是引用的原文，不是回复的措辞。
// 规则表里标了回复门限的，要同一段内凑够那个数才报，不够的整段不提：
// 单处行话既不值得打断也不值得提醒。
#pragma once

#include <cstddef>
#include <string>
#include <string_view>
#include <vector>

#include "engine/rules.h"

namespace texttool {

// 一处命中。line 从 1 起，按 \n 数；token 是可直接回显的那几个字。
// blocks 表示命中时值得当场重写，取自规则表的同名开关。
struct ReplyShapeHit {
    std::string id;
    std::string title;
    std::string token;
    std::size_t line = 0;
    bool blocks = false;
};

// 本扫描器认的规则编号。外部系统按这些编号认契约，改名等于改对方的调用。
std::vector<std::string_view> reply_shape_rule_ids();

// 扫一份回复。规则表缺条目、或编号那几条不是正则规则时抛 std::runtime_error。
std::vector<ReplyShapeHit> scan_reply_shape(const std::vector<LintRule>& rules,
                                            std::string_view text);

// 输出：as_json 为假时一行一处；为真时带版本号的 JSON（契约版本 2）。行尾一律 LF。
std::string render_reply_shape(const std::vector<ReplyShapeHit>& hits, bool as_json);

// JSON 契约版本。字段增减时加一，调用方按它判断能不能解析。
inline constexpr int kReplyShapeJsonVersion = 2;

}  // namespace texttool
