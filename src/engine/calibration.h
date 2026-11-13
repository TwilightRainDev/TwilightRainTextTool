// 规则标定：同一套规则分别跑人类文本与模型文本，按规则汇总两侧的命中与每千字密度。
//
// 判定沿用检查那一套：每份稿都过一次 run_lint，规则表、档位、密度门限、重叠去重全按同一套走。
// 这一层只做汇总：在扫出的命中上过一次账，不回头重扫原文。
//
// 人读表与 JSON 都出自同一份结果，两种输出不会各算一遍而给出不同数字。
#pragma once

#include <optional>
#include <string>
#include <string_view>
#include <vector>

#include "engine/rules.h"

namespace texttool {

// 一侧文本的规模：文件数、总字数（按 UTF-16 码元）、总命中数、每千字命中。
struct CalibrationCollection {
    long long files = 0;
    long long chars = 0;
    long long hits = 0;
    double per_kchar = 0.0;
};

// 一条规则在两侧的读数。verdict 取三个英文标识：separable / weak / indistinguishable。
struct CalibrationRow {
    std::string id;
    std::string title;
    long long human_hits = 0;
    double human_per_kchar = 0.0;
    long long ai_hits = 0;
    double ai_per_kchar = 0.0;
    std::string verdict = "indistinguishable";
};

struct CalibrationResult {
    int version = 1;
    std::optional<std::string> genre;  // 没给档位时为空，JSON 里写 null
    CalibrationCollection human;
    CalibrationCollection ai;
    long long separable_count = 0;
    std::vector<CalibrationRow> rows;  // 已按模型侧密度从高到低排好

    // JSON：Version、Genre、Human、Ai、SeparableCount、Rules 六项，字段次序就是契约。
    std::string to_json() const;

    // 人读表格：列按「模型侧密度高的先看」排，末行是两侧规模与可分条数。
    // 数字列右对齐，末列不补空格，行尾因此没有空白。
    std::string render() const;
};

// 判定用中文标签：可分 / 弱 / 不可分。
std::string_view calibration_label(std::string_view verdict);

// 密度：每千字命中处数，按 UTF-16 码元算。chars 为 0 时记 hits。
double density(long long hits, long long chars);

// 递归收一个目录里的 .txt，按路径顺序排好。
// 目录不存在、目录里没有 .txt 都抛 std::runtime_error，消息里带传进来的那个选项名。
std::vector<std::string> calibration_txt_files(const std::string& directory,
                                               const std::string& flag);

// 两侧各跑一遍规则并汇总。档位非法时抛 std::runtime_error，消息与检查命令同一句。
// 读不了的稿整次停下：一个文件的编码认不出来，两侧的密度就不可比。
CalibrationResult calibrate(const std::vector<LintRule>& rules,
                            const std::vector<std::string>& human_files,
                            const std::vector<std::string>& ai_files,
                            const std::optional<std::string>& genre);

}  // namespace texttool
