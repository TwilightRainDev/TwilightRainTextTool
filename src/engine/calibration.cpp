// 规则标定的实现。判定与命令输出须与 fixtures/cli 下 calibrate_* 的期望输出逐字节相同：判定只有两条，
// 密度用未取整的值算，表里取两位小数。
#include "engine/calibration.h"

#include <algorithm>
#include <cerrno>
#include <charconv>
#include <cmath>
#include <cstring>
#include <cstddef>
#include <cstdint>
#include <filesystem>
#include <fstream>
#include <stdexcept>
#include <string>
#include <string_view>
#include <system_error>
#include <vector>

#include "engine/encoding.h"
#include "engine/io.h"
#include "engine/lint_runner.h"
#include "engine/report.h"

namespace texttool {
namespace {

// 密度取两位小数的显示写法。取整按「四舍五入、半数离开零」，
// 所以不用 printf 那一套（它是半数取偶）。
std::string format_density(double value) {
    const double scaled = std::round(value * 100.0);
    const auto hundredths = static_cast<long long>(scaled);
    const long long whole = hundredths / 100;
    const long long rest = hundredths % 100;
    std::string out = std::to_string(whole);
    out.push_back('.');
    if (rest < 10) out.push_back('0');
    out.append(std::to_string(rest));
    return out;
}

// 双精度按最短能还原的写法给，与报告里 JSON 的数值写法同一种：
// 定点与指数两种形式的分界在十进指数 -4 与 16（0.0001 写定点，0.00001 写 1E-05），
// 指数写作大写的 E，带符号、至少两位。
std::string format_shortest(double value) {
    if (value == 0.0) return std::signbit(value) ? "-0" : "0";

    char buffer[64];
    const std::to_chars_result result =
        std::to_chars(buffer, buffer + sizeof buffer, value, std::chars_format::scientific);
    std::string_view text(buffer, static_cast<std::size_t>(result.ptr - buffer));

    bool negative = false;
    if (!text.empty() && text.front() == '-') {
        negative = true;
        text.remove_prefix(1);
    }
    const std::size_t marker = text.find('e');
    const std::string_view mantissa = text.substr(0, marker);
    const int exponent = std::stoi(std::string(text.substr(marker + 1)));

    std::string digits;
    digits.reserve(mantissa.size());
    for (const char c : mantissa)
        if (c != '.') digits.push_back(c);
    while (digits.size() > 1 && digits.back() == '0') digits.pop_back();

    std::string out;
    if (negative) out.push_back('-');

    if (exponent >= -4 && exponent <= 16) {
        if (exponent >= 0) {
            const auto before = static_cast<std::size_t>(exponent) + 1;
            if (digits.size() <= before) {
                out.append(digits);
                out.append(before - digits.size(), '0');
            } else {
                out.append(digits.substr(0, before));
                out.push_back('.');
                out.append(digits.substr(before));
            }
        } else {
            out.append("0.");
            out.append(static_cast<std::size_t>(-exponent - 1), '0');
            out.append(digits);
        }
        return out;
    }

    out.push_back(digits.front());
    if (digits.size() > 1) {
        out.push_back('.');
        out.append(digits.substr(1));
    }
    out.push_back('E');
    out.push_back(exponent >= 0 ? '+' : '-');
    std::string magnitude = std::to_string(exponent >= 0 ? exponent : -exponent);
    if (magnitude.size() < 2) out.push_back('0');
    out.append(magnitude);
    return out;
}

// 终端显示宽度：汉字、假名、全角标点占两格，其余占一格。按 UTF-16 码元数，
// 辅助平面的字算两个码元，两格。
bool is_wide(char32_t code_point) {
    return (code_point >= 0x1100 && code_point <= 0x115F) ||
           (code_point >= 0x2E80 && code_point <= 0xA4CF) ||
           (code_point >= 0xAC00 && code_point <= 0xD7A3) ||
           (code_point >= 0xF900 && code_point <= 0xFAFF) ||
           (code_point >= 0xFE30 && code_point <= 0xFE6F) ||
           (code_point >= 0xFF00 && code_point <= 0xFF60) ||
           (code_point >= 0xFFE0 && code_point <= 0xFFE6);
}

std::size_t next_utf8(std::string_view text, std::size_t at, char32_t& code_point) {
    const auto lead = static_cast<unsigned char>(text[at]);
    std::size_t length = 1;
    char32_t value = lead;
    if ((lead & 0xE0) == 0xC0) {
        length = 2;
        value = lead & 0x1Fu;
    } else if ((lead & 0xF0) == 0xE0) {
        length = 3;
        value = lead & 0x0Fu;
    } else if ((lead & 0xF8) == 0xF0) {
        length = 4;
        value = lead & 0x07u;
    }
    if (length > 1) {
        if (at + length > text.size()) {
            code_point = lead;
            return 1;
        }
        for (std::size_t i = 1; i < length; ++i) {
            const auto next = static_cast<unsigned char>(text[at + i]);
            if ((next & 0xC0) != 0x80) {
                code_point = lead;
                return 1;
            }
            value = (value << 6) | (next & 0x3Fu);
        }
    }
    code_point = value;
    return length;
}

std::size_t display_width(std::string_view text) {
    std::size_t width = 0;
    for (std::size_t at = 0; at < text.size();) {
        char32_t code_point = 0;
        const std::size_t length = next_utf8(text, at, code_point);
        at += length;
        const std::size_t units = code_point >= 0x10000 ? 2 : 1;
        width += is_wide(code_point) ? 2 * units : units;
    }
    return width;
}

std::string read_text_file(const std::string& path) {
    errno = 0;
    std::ifstream in(path, std::ios::binary);
    if (!in) throw std::runtime_error(std::strerror(errno == 0 ? EACCES : errno));

    std::string bytes;
    std::error_code size_error;
    const auto on_disk = std::filesystem::file_size(path, size_error);
    if (!size_error) bytes.reserve(static_cast<std::size_t>(on_disk));
    char buffer[64 * 1024];
    while (in) {
        in.read(buffer, sizeof buffer);
        bytes.append(buffer, static_cast<std::size_t>(in.gcount()));
    }
    if (in.bad()) throw std::runtime_error("读取中断：" + path);

    const TextEncoding encoding = detect_encoding(bytes);
    if (encoding == TextEncoding::Utf8) return bytes;
    return to_utf8(bytes, encoding);
}

bool same_id(std::string_view a, std::string_view b) {
    if (a.size() != b.size()) return false;
    for (std::size_t i = 0; i < a.size(); ++i) {
        const char left = (a[i] >= 'A' && a[i] <= 'Z') ? static_cast<char>(a[i] - 'A' + 'a') : a[i];
        const char right = (b[i] >= 'A' && b[i] <= 'Z') ? static_cast<char>(b[i] - 'A' + 'a') : b[i];
        if (left != right) return false;
    }
    return true;
}

// 生效规则：给档位时只留该档的（不绑档位的算通用），没给档位就跑整张表。
// 两边都不跑标了 default_off 的规则：那是默认关着、要靠 --only 点名的那些。
std::vector<const LintRule*> active_rules(const std::vector<LintRule>& rules,
                                         const std::optional<std::string>& genre) {
    if (genre) {
        bool known = false;
        for (std::string_view id : {"doc", "fiction", "agent"})
            if (same_id(*genre, id)) known = true;
        if (!known) throw std::runtime_error("--genre 只接受 doc、fiction、agent");
    }

    std::vector<const LintRule*> out;
    for (const auto& rule : rules) {
        if (rule.default_off) continue;
        if (genre && !rule.genres.empty()) {
            bool allowed = false;
            for (const auto& item : rule.genres)
                if (same_id(item, *genre)) allowed = true;
            if (!allowed) continue;
        }
        out.push_back(&rule);
    }
    return out;
}

std::string_view verdict_of(long long human_hits, double human_density, long long ai_hits,
                            double ai_density) {
    if (human_hits == 0 && ai_hits > 0) return "separable";
    if (human_hits > 0 && ai_hits > 0 && ai_density >= human_density * 3.0) return "weak";
    return "indistinguishable";
}

struct Tally {
    long long human = 0;
    long long ai = 0;
};

// 「这条规则没生效」：命中不会出现在这些行上。
constexpr std::size_t kNoRow = static_cast<std::size_t>(-1);

std::string json_string(std::string_view value) {
    return "\"" + json_escape(value) + "\"";
}

std::string json_optional_string(const std::optional<std::string>& value) {
    return value ? json_string(*value) : "null";
}

// 嵌套对象从第四列起写：外层已经写了两格缩进与键名，这里只接「{」与里面的字段。
std::string json_collection_body(const CalibrationCollection& collection) {
    std::string out;
    out.append("{\n");
    out.append("    \"Files\": ").append(std::to_string(collection.files)).append(",\n");
    out.append("    \"Chars\": ").append(std::to_string(collection.chars)).append(",\n");
    out.append("    \"Hits\": ").append(std::to_string(collection.hits)).append(",\n");
    out.append("    \"PerKChar\": ").append(format_shortest(collection.per_kchar)).append("\n");
    out.append("  }");
    return out;
}

}  // namespace

double density(long long hits, long long chars) {
    if (chars <= 0) return static_cast<double>(hits);
    return static_cast<double>(hits) * 1000.0 / static_cast<double>(chars);
}

std::string_view calibration_label(std::string_view verdict) {
    if (verdict == "separable") return "可分";
    if (verdict == "weak") return "弱";
    return "不可分";
}

std::vector<std::string> calibration_txt_files(const std::string& directory,
                                               const std::string& flag) {
    std::error_code ignored;
    if (!std::filesystem::is_directory(directory, ignored))
        throw std::runtime_error(flag + " 目录不存在：" + directory);

    // 递归收 .txt：收法在引擎里（engine/io.h 的 scan_documents），界面拖进目录也走它。
    // 后缀按字节比，区分大小写，只认小写 .txt。
    const DirectoryScan scan = scan_documents(directory, {".txt"});
    if (!scan.unreadable.empty())
        throw std::runtime_error(flag + " 目录读不了：" + scan.unreadable.front());
    if (scan.files.empty()) throw std::runtime_error(flag + " 目录下没有 .txt 文件：" + directory);
    return scan.files;
}

CalibrationResult calibrate(const std::vector<LintRule>& rules,
                            const std::vector<std::string>& human_files,
                            const std::vector<std::string>& ai_files,
                            const std::optional<std::string>& genre) {
    // 先按生效规则建满表：两侧都零命中的规则也要占一行，那种「活着但量不到东西」的判据
    // 正是标定要暴露的对象，只列有命中的规则就把它们藏起来了。
    const std::vector<const LintRule*> active = active_rules(rules, genre);

    // 两侧的每份稿子共用一次规则编译，与检查命令跑多份时的做法相同。
    const CompiledRules compiled(rules);

    // 命中里的规则下标指向整张规则表，这里换成生效规则里的行号；没生效的规则不会有命中。
    std::vector<std::size_t> row_of(rules.size(), kNoRow);
    for (std::size_t i = 0; i < active.size(); ++i) {
        const auto at = static_cast<std::size_t>(active[i] - rules.data());
        row_of[at] = i;
    }

    CalibrationResult result;
    result.genre = genre;
    std::vector<Tally> tallies(active.size());

    // 每份稿都过一次检查，命中立刻入账再丢掉报告：峰值内存是「一份稿」而不是「全部报告」。
    const auto scan = [&](const std::vector<std::string>& files, CalibrationCollection& stats,
                          bool ai_side) {
        stats.files = static_cast<long long>(files.size());
        LintOptions options;
        options.genre = genre.value_or(std::string());
        for (const auto& file : files) {
            const std::string text = read_text_file(file);
            const LintOutcome outcome = run_lint(compiled, text, options);
            stats.chars += static_cast<long long>(outcome.units);
            // 处数读 rule_hits：命中表在扫描时按条封顶，表里的条数不是处数。
            for (std::size_t index = 0; index < outcome.rule_hits.size(); ++index) {
                const long long count = static_cast<long long>(outcome.rule_hits[index]);
                stats.hits += count;
                if (index >= row_of.size() || row_of[index] == kNoRow) continue;
                Tally& tally = tallies[row_of[index]];
                if (ai_side) tally.ai += count;
                else tally.human += count;
            }
        }
        stats.per_kchar = density(stats.hits, stats.chars);
    };

    scan(human_files, result.human, false);
    scan(ai_files, result.ai, true);

    result.rows.reserve(active.size());
    for (std::size_t i = 0; i < active.size(); ++i) {
        CalibrationRow row;
        row.id = active[i]->id;
        row.title = active[i]->title;
        row.human_hits = tallies[i].human;
        row.ai_hits = tallies[i].ai;
        row.human_per_kchar = density(row.human_hits, result.human.chars);
        row.ai_per_kchar = density(row.ai_hits, result.ai.chars);
        row.verdict = std::string(verdict_of(row.human_hits, row.human_per_kchar, row.ai_hits,
                                             row.ai_per_kchar));
        result.rows.push_back(std::move(row));
    }

    // 排序用未取整的密度：读到 3.00 就以为刚好三倍，会误判门限。
    std::stable_sort(result.rows.begin(), result.rows.end(),
                     [](const CalibrationRow& a, const CalibrationRow& b) {
                         if (a.ai_per_kchar != b.ai_per_kchar) return a.ai_per_kchar > b.ai_per_kchar;
                         if (a.ai_hits != b.ai_hits) return a.ai_hits > b.ai_hits;
                         if (a.human_hits != b.human_hits) return a.human_hits > b.human_hits;
                         return a.id < b.id;
                     });

    for (const auto& row : result.rows)
        if (row.verdict == "separable") ++result.separable_count;
    return result;
}

std::string CalibrationResult::to_json() const {
    std::string out;
    out.append("{\n");
    out.append("  \"Version\": ").append(std::to_string(version)).append(",\n");
    out.append("  \"Genre\": ").append(json_optional_string(genre)).append(",\n");
    out.append("  \"Human\": ").append(json_collection_body(human)).append(",\n");
    out.append("  \"Ai\": ").append(json_collection_body(ai)).append(",\n");
    out.append("  \"SeparableCount\": ").append(std::to_string(separable_count)).append(",\n");
    if (rows.empty()) {
        out.append("  \"Rules\": []\n");
    } else {
        out.append("  \"Rules\": [\n");
        for (std::size_t i = 0; i < rows.size(); ++i) {
            const CalibrationRow& row = rows[i];
            out.append("    {\n");
            out.append("      \"Id\": ").append(json_string(row.id)).append(",\n");
            out.append("      \"Title\": ").append(json_string(row.title)).append(",\n");
            out.append("      \"HumanHits\": ").append(std::to_string(row.human_hits)).append(",\n");
            out.append("      \"HumanPerKChar\": ")
                .append(format_shortest(row.human_per_kchar))
                .append(",\n");
            out.append("      \"AiHits\": ").append(std::to_string(row.ai_hits)).append(",\n");
            out.append("      \"AiPerKChar\": ")
                .append(format_shortest(row.ai_per_kchar))
                .append(",\n");
            out.append("      \"Verdict\": ").append(json_string(row.verdict)).append("\n");
            out.append("    }");
            out.append(i + 1 == rows.size() ? "\n" : ",\n");
        }
        out.append("  ]\n");
    }
    out.append("}");
    return out;
}

std::string CalibrationResult::render() const {
    std::vector<std::vector<std::string>> table;
    table.push_back({"规则", "标题", "人类命中", "人类/千字", "模型命中", "模型/千字", "判定"});
    for (const auto& row : rows) {
        table.push_back({row.id, row.title, std::to_string(row.human_hits),
                         format_density(row.human_per_kchar), std::to_string(row.ai_hits),
                         format_density(row.ai_per_kchar),
                         std::string(calibration_label(row.verdict))});
    }

    std::vector<std::size_t> widths(table.front().size(), 0);
    for (const auto& row : table)
        for (std::size_t i = 0; i < row.size(); ++i)
            widths[i] = std::max(widths[i], display_width(row[i]));

    std::string out;
    for (const auto& row : table) {
        for (std::size_t i = 0; i < row.size(); ++i) {
            if (i > 0) out.append("  ");
            // 判定列是最后一列，不补齐，行尾因此没有空白。
            if (i + 1 == row.size()) {
                out.append(row[i]);
                break;
            }
            const std::size_t pad = widths[i] - display_width(row[i]);
            if (i >= 2 && i <= 5) {
                out.append(pad, ' ');
                out.append(row[i]);
            } else {
                out.append(row[i]);
                out.append(pad, ' ');
            }
        }
        out.push_back('\n');
    }

    out.append("人类 ").append(std::to_string(human.files)).append(" 个文件 ")
        .append(std::to_string(human.chars)).append(" 字，命中 ")
        .append(std::to_string(human.hits)).append(" 处（每千字 ")
        .append(format_density(human.per_kchar)).append("）；模型 ")
        .append(std::to_string(ai.files)).append(" 个文件 ")
        .append(std::to_string(ai.chars)).append(" 字，命中 ")
        .append(std::to_string(ai.hits)).append(" 处（每千字 ")
        .append(format_density(ai.per_kchar)).append("）；可分规则 ")
        .append(std::to_string(separable_count)).append(" 条。\n");
    return out;
}

}  // namespace texttool
