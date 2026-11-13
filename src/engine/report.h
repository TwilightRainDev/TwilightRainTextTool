// 检查结果的渲染：给人读的报告、给机器读的 JSON、退出码。
//
// 这一层只管排版，不做判断。人读报告不写规则编号，组头是「[发现] 标题（N 处）」，
// 用词以 fixtures/report 下对应的 .expected.txt 为准；JSON 字段与退出码以
// .expected.json、.expected.exit 为准。两者都要逐字节相同。
//
// 输入就是一次检查的产出（engine/lint_runner.h 的 LintOutcome）：命中只带规则下标与字节区间，
// 标题、说明、命中原文在排版的时候按需从规则表与稿子里切。十几万处命中先各拼一份字符串，
// 光这一项就上百兆；切片一处只占几十字节。
//
// 同一条规则超过 kMaxListedHitsPerRule 处时，这里只排前几处，处数写真实总数，
// 剩下的在统计段里补一句「另有 N 处未列出」。人读报告、--json、--log 三条路同一口径。
#pragma once

#include <map>
#include <optional>
#include <string>
#include <string_view>
#include <vector>

#include "engine/lint_runner.h"
#include "engine/rules.h"

namespace texttool {

struct ReportInput {
    std::string source_name;

    // 命中必须已经按 (line, column) 全局升序（同位置的多条保持原有先后）。
    // 排序归调用方：整表在扫完那一步排好，渲染层只做组内排序与「组序 = 首次出现」。
    // 把整表排序挪进渲染层，组序会变成「各组首条的位置顺序」之外的另一种次序，输出就对不上期望输出。
    std::vector<LintHit> hits;
    int total_scanned = 0;

    // 每条规则的真实命中处数，按规则表下标。命中封顶时 hits 里只留前 kMaxListedHitsPerRule 处，
    // 组头、「发现 N 处」、「另有 N 处未列出」三处都按这个数写。
    // 空表示没数过，那就按 hits 现数。
    std::vector<std::size_t> rule_hits;

    // 命中里的下标按这两样换字符串：本次跑的规则表、本次扫的原文（UTF-8）。
    // 都归调用方保管，生命周期要长过这次排版。
    const std::vector<LintRule>* rules = nullptr;
    std::string_view text;
    std::vector<LintHitExtra> extras;  // 命中里 extra 下标指向这里
    std::string arena;                 // 附加串的内容，extras 里的偏移指向这里

    // 分段方式，取值 line 或 markdown，原样写进人读报告与 JSON。
    std::string paragraph_mode = "line";
    std::vector<LintNote> notes;  // 统计项：只出现在人读报告末尾的 [统计] 段，不进退出码

    // 给了写前那一份时才有：报告只列新增位置，开头先写清楚是跟谁比的。
    std::optional<int> baseline_hits;

    // 规则编号到折叠密度的对照。规则表里没写这一项就不出现，人读报告也就不折叠。
    // 调用方把规则表里写了的全部填进来，阈值随报告一起带出去。
    std::map<std::string, int> collapse_at_density;

    HitFields fields() const { return {*rules, extras, arena, text}; }
};

// 规则表里写了折叠阈值的规则收成一张表，键是规则编号。没有写这一项的规则不进表。
// 命令行的检查与界面上的检查页都从这里取，两边看到的人读报告才是同一份。
std::map<std::string, int> collapse_thresholds(const std::vector<LintRule>& rules);

// 把一次检查的产出装成报告输入。source_name 是报告里印的那份稿的名字
// （界面里粘贴的文本是「(粘贴的文本)」）。命中、附加串与统计附注整张挪过来，不复份。
// 规则表与原文归调用方保管，生命周期要长过这份报告：命中里的下标与字节区间都指回它们。
// cap_hits 为假时先不封顶，调用方要拿全命中写日志；日志写完再调 cap_report_hits 裁报告。
ReportInput make_report(std::string source_name, LintOutcome&& outcome,
                        const std::map<std::string, int>& collapse, bool cap_hits = true);

// 按每条规则 kMaxListedHitsPerRule 处裁报告里的命中；处数表（rule_hits）不动，报告里的
// 「发现 N 处」与「另有 N 处未列出」仍按真实处数写。重复调用没有副作用。
void cap_report_hits(ReportInput& report);

// 报告的输出口子：写完一块就交出去。人读报告按命中逐行写，同一份 15MB 的稿子能到六十兆，
// 整份拼在内存里等于把这个数再占一遍。
using ReportSink = void (*)(std::string_view chunk, void* context);

// 一份报告的人读排版：行尾一律 LF，以单个换行收尾。整份返回的版本给测试与短报告用。
std::string render_text_report(const ReportInput& input);
void render_text_report(const ReportInput& input, ReportSink sink, void* context);

// 一份报告的 JSON 排版：与 `lint --json` 的一整段输出相同，含结尾换行。
std::string render_json_report(const ReportInput& input);
void render_json_report(const ReportInput& input, ReportSink sink, void* context);

// 命中日志：一行一条，先一行汇总再逐条命中，行尾为 LF。
// 只留 ASCII 可打印字符、汉字与全角标点，其余字符写成 \uXXXX（小写十六进制），
// 这样整份日志拷进文档目录仍然能看。开关是 `lint --log <文件>`，默认不写。
// 按命中逐行写出去：一份长稿的日志有几十兆，不整份拼在内存里。
std::string render_hit_log(const ReportInput& input);
void render_hit_log(const ReportInput& input, ReportSink sink, void* context);

// 0 = 没有命中，1 = 有命中。用法与读取错误由调用方处理，不在这里。
int exit_code_for(const ReportInput& input);

// 一份报告的真实命中处数。命中按条封顶之后 hits 里的条数可能少于真实处数，
// 要报「发现几处」就取这个数（人读报告与命中日志自己就是这么写的）。
std::size_t total_hits(const ReportInput& input);

// 一个字符串的 JSON 转义，逐字节得这样写（规则见 report.cpp 里那段说明）。
// calibrate 与 reply-check 的 JSON 也走这里，三个命令不会各转一套。
std::string json_escape(std::string_view value);

}  // namespace texttool
