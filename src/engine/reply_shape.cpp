// 回复检查的实现。同一行同一个片段只报一次，门限按空行分段数，
// 判围栏的代码与博文结构区域的处理共用 engine/markdown.h 那一份。
#include "engine/reply_shape.h"

#include <cstddef>
#include <string>
#include <string_view>
#include <unordered_map>
#include <unordered_set>
#include <utility>
#include <vector>

#include "engine/markdown.h"
#include "engine/regex.h"
#include "engine/report.h"

namespace texttool {
namespace {

struct Shape {
    std::string id;
    std::string title;
    bool blocks = false;
    std::size_t min_count = 1;
    std::vector<Pattern> patterns;
};

bool same_id(std::string_view a, std::string_view b) {
    if (a.size() != b.size()) return false;
    for (std::size_t i = 0; i < a.size(); ++i) {
        const char left = (a[i] >= 'A' && a[i] <= 'Z') ? static_cast<char>(a[i] - 'A' + 'a') : a[i];
        const char right = (b[i] >= 'A' && b[i] <= 'Z') ? static_cast<char>(b[i] - 'A' + 'a') : b[i];
        if (left != right) return false;
    }
    return true;
}

std::vector<Shape> build_shapes(const std::vector<LintRule>& rules) {
    std::vector<Shape> shapes;
    for (const std::string_view id : reply_shape_rule_ids()) {
        const LintRule* found = nullptr;
        for (const auto& rule : rules)
            if (same_id(rule.id, id)) {
                found = &rule;
                break;
            }
        if (found == nullptr)
            throw std::runtime_error("规则表缺少 " + std::string(id) + "，回复形状扫描依赖它");
        if (found->kind != RuleKind::Regex)
            throw std::runtime_error("规则 " + found->id + " 应当是 regex，实际是 " +
                                     std::string(to_string(found->kind)));
        if (found->patterns.empty())
            throw std::runtime_error("规则 " + found->id + " 没有模式");

        Shape shape;
        shape.id = found->id;
        shape.title = found->title;
        shape.blocks = found->blocks_reply;
        shape.min_count = static_cast<std::size_t>(found->reply_min_count.value_or(1));
        shape.patterns.reserve(found->patterns.size());
        for (const auto& pattern : found->patterns) shape.patterns.emplace_back(pattern);
        shapes.push_back(std::move(shape));
    }
    return shapes;
}

// 一行里某个模式的所有命中。与检查命令同一套走法：非重叠，零长度的往后挪一个字。
template <class Emit>
void for_each_match(const Pattern& pattern, std::string_view line, Emit emit) {
    const MatchLimits limits;
    std::size_t at = 0;
    while (true) {
        const MatchRange match = pattern.search(line, at, limits);
        if (match.begin == kNoMatch) return;
        emit(match.begin, match.end);
        if (match.end > match.begin) {
            at = match.end;
        } else {
            if (match.begin >= line.size()) return;
            at = match.begin + 1;
            while (at < line.size() && (static_cast<unsigned char>(line[at]) & 0xC0) == 0x80) ++at;
        }
    }
}

// .NET 的 string.Trim()：两头去掉空白。这里只判「整行是不是空白」，按同一组空白字算。
bool is_blank_line(std::string_view line) {
    for (std::size_t at = 0; at < line.size();) {
        const auto lead = static_cast<unsigned char>(line[at]);
        std::size_t length = 1;
        char32_t code_point = lead;
        if ((lead & 0xE0) == 0xC0) {
            length = 2;
            code_point = lead & 0x1Fu;
        } else if ((lead & 0xF0) == 0xE0) {
            length = 3;
            code_point = lead & 0x0Fu;
        } else if ((lead & 0xF8) == 0xF0) {
            length = 4;
            code_point = lead & 0x07u;
        }
        if (length > 1 && at + length <= line.size()) {
            for (std::size_t i = 1; i < length; ++i) {
                const auto next = static_cast<unsigned char>(line[at + i]);
                if ((next & 0xC0) != 0x80) {
                    length = 1;
                    code_point = lead;
                    break;
                }
                code_point = (code_point << 6) | (next & 0x3Fu);
            }
        }
        const bool space = (code_point >= 0x09 && code_point <= 0x0D) || code_point == 0x20 ||
                           code_point == 0x85 || code_point == 0xA0 || code_point == 0x1680 ||
                           (code_point >= 0x2000 && code_point <= 0x200A) ||
                           code_point == 0x2028 || code_point == 0x2029 || code_point == 0x202F ||
                           code_point == 0x205F || code_point == 0x3000;
        if (!space) return false;
        at += length;
    }
    return true;
}

}  // namespace

std::vector<std::string_view> reply_shape_rule_ids() {
    return {
        "agent-code-list",         "agent-truncated-path",     "emoji-and-symbols",
        "char-face",               "violent-verbs",            "agent-process-jargon",
        "agent-smoke-jargon",      "agent-test-jargon",        "agent-conclusion-first",
        "agent-permission-ask",    "agent-one-char-commitment", "agent-receipt-commitment",
        "agent-template-preamble", "agent-weight-comparison",  "agent-keepsake",
        "agent-measure-invite",    "agent-caretaker-line",     "agent-probe-question",
    };
}

std::vector<ReplyShapeHit> scan_reply_shape(const std::vector<LintRule>& rules,
                                            std::string_view text) {
    const std::vector<Shape> shapes = build_shapes(rules);

    // 按 \n 切行，行尾的 \r 留着：命中在带 \r 的整行上找。
    std::vector<std::string_view> lines;
    for (std::size_t pos = 0;;) {
        const std::size_t newline = text.find('\n', pos);
        if (newline == std::string_view::npos) {
            lines.push_back(text.substr(pos));
            break;
        }
        lines.push_back(text.substr(pos, newline - pos));
        pos = newline + 1;
    }

    // 段落按空行切：门限判据按这个规则数，与文档侧的空行分段一致。
    std::vector<std::size_t> paragraph_of_line(lines.size() + 1, 0);
    std::size_t paragraph = 0;
    for (std::size_t i = 0; i < lines.size(); ++i) {
        if (is_blank_line(lines[i])) ++paragraph;
        paragraph_of_line[i + 1] = paragraph;
    }

    std::vector<ReplyShapeHit> hits;
    // 同一行同一个片段只报一次：一段正文里同一个写法复述两遍，读者的问题只有一个。
    std::unordered_set<std::string> seen;
    FenceTracker fence;

    for (std::size_t index = 0; index < lines.size(); ++index) {
        const std::string_view raw = lines[index];
        const std::size_t line = index + 1;
        if (fence.advance(raw)) continue;
        if (fence.in_fence()) continue;

        for (const Shape& shape : shapes) {
            for (const Pattern& pattern : shape.patterns) {
                for_each_match(pattern, raw, [&](std::size_t begin, std::size_t end) {
                    const std::string_view token = raw.substr(begin, end - begin);
                    std::string key(shape.id);
                    key.push_back('\0');
                    key.append(token);
                    key.push_back('\0');
                    key.append(std::to_string(line));
                    if (!seen.insert(std::move(key)).second) return;
                    hits.push_back({shape.id, shape.title, std::string(token), line, shape.blocks});
                });
            }
        }
    }

    // 门限以下的命中不返回：够不着门限的判据在回复里连提一句都不值。
    bool any_threshold = false;
    for (const Shape& shape : shapes)
        if (shape.min_count > 1) any_threshold = true;
    if (!any_threshold) return hits;

    std::unordered_map<std::string, std::size_t> thresholds;
    for (const Shape& shape : shapes) thresholds[shape.id] = shape.min_count;

    std::unordered_map<std::string, std::size_t> counts;
    for (const ReplyShapeHit& hit : hits) {
        std::string key(hit.id);
        key.push_back('\0');
        key.append(std::to_string(paragraph_of_line[hit.line]));
        ++counts[key];
    }

    std::vector<ReplyShapeHit> kept;
    kept.reserve(hits.size());
    for (ReplyShapeHit& hit : hits) {
        std::string key(hit.id);
        key.push_back('\0');
        key.append(std::to_string(paragraph_of_line[hit.line]));
        const std::size_t threshold = thresholds[hit.id];
        if (counts[key] < threshold) continue;
        kept.push_back(std::move(hit));
    }
    return kept;
}

std::string render_reply_shape(const std::vector<ReplyShapeHit>& hits, bool as_json) {
    if (as_json) {
        std::string out;
        out.append("{\n");
        out.append("  \"Version\": ").append(std::to_string(kReplyShapeJsonVersion)).append(",\n");
        if (hits.empty()) {
            out.append("  \"Hits\": []\n");
        } else {
            out.append("  \"Hits\": [\n");
            for (std::size_t i = 0; i < hits.size(); ++i) {
                const ReplyShapeHit& hit = hits[i];
                out.append("    {\n");
                out.append("      \"Id\": \"").append(json_escape(hit.id)).append("\",\n");
                out.append("      \"Token\": \"").append(json_escape(hit.token)).append("\",\n");
                out.append("      \"Line\": ").append(std::to_string(hit.line)).append(",\n");
                out.append("      \"Blocks\": ").append(hit.blocks ? "true" : "false").append("\n");
                out.append("    }");
                out.append(i + 1 == hits.size() ? "\n" : ",\n");
            }
            out.append("  ]\n");
        }
        out.append("}\n");
        return out;
    }

    if (hits.empty()) return "没有读者解不开的写法。\n";

    // 打回与只提醒分开列：读报告的人先看哪几处是硬伤。
    std::vector<std::size_t> group_of(hits.size(), 0);
    std::vector<std::size_t> group_count;
    std::vector<std::size_t> group_first;
    std::unordered_map<std::string, std::size_t> groups;
    for (std::size_t i = 0; i < hits.size(); ++i) {
        std::string key(hits[i].id);
        key.push_back('\0');
        key.append(hits[i].title);
        key.push_back('\0');
        key.push_back(hits[i].blocks ? '1' : '0');
        const auto found = groups.find(key);
        if (found == groups.end()) {
            const std::size_t id = group_first.size();
            groups.emplace(std::move(key), id);
            group_first.push_back(i);
            group_count.push_back(1);
            group_of[i] = id;
        } else {
            group_of[i] = found->second;
            ++group_count[found->second];
        }
    }

    std::string out;
    for (std::size_t group = 0; group < group_first.size(); ++group) {
        const ReplyShapeHit& head = hits[group_first[group]];
        out.append(head.blocks ? "[发现] " : "[只提醒] ");
        out.append(head.title);
        out.append("（").append(std::to_string(group_count[group])).append(" 处）\n");
        for (std::size_t i = 0; i < hits.size(); ++i) {
            if (group_of[i] != group) continue;
            out.append("       第 ").append(std::to_string(hits[i].line)).append(" 行  ");
            out.append(hits[i].token);
            out.push_back('\n');
        }
    }
    out.append("共 ").append(std::to_string(hits.size())).append(" 处。\n");
    return out;
}

}  // namespace texttool
