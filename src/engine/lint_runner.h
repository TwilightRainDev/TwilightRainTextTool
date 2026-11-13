// 按规则表跑一遍检查。
//
// 这一层把「规则」变成「命中」：博文的结构区域先写成空格，做成一份与原文逐字节对齐的副本
// （下面简称空格副本），再让词面、正则、算法三类规则在空格副本上找位置；哪些规则跳过
// （文本类型、Markdown、博客稿）、命中怎么去重与排序，都在这里定。报告层只管排版，
// 不做判断，所以命中的顺序在这里就要排好。
//
// 空格副本与原文逐字节对齐：命中的行列、长度、原文仍按原文取，被遮住的字只当空格看。
//
// 命中只存下标与字节区间。标题、说明、命中原文都按需从规则表与原文里切：
// 一份 15MB 的稿子能报十几万处，每处存一份字符串副本就是几十兆内存。
// 切的动作发生在排版的时候，一处一处来。
//
// 同一条规则的命中按条封顶（kMaxListedHitsPerRule）：超出上限的那几处只计数、不入表，
// 真实处数记在 rule_hits 里。
#pragma once

#include <chrono>
#include <cstddef>
#include <cstdint>
#include <map>
#include <optional>
#include <string>
#include <string_view>
#include <vector>

#include "engine/needle_scan.h"
#include "engine/regex.h"
#include "engine/rules.h"

namespace texttool {

// 规则下标、建议下标的「没有这一项」：两个都按 16 位存，规则表与建议表都短得过这道线。
inline constexpr std::uint16_t kNoIndex = 0xFFFF;

// 附加串下标的「没有这一项」。附加串按 32 位存（见 LintHit::extra），这一项单独一个值，
// 不跟 kNoIndex 合用：一份稿子里命中可以有很多处，附加串的格数不短，0xFFFF 装不下它的界。
inline constexpr std::uint32_t kNoExtra = 0xFFFFFFFFu;

// 一份稿子里，同一条规则最多列出多少处。处数照旧数清（报告开头的「发现 N 处」、组头、
// 密度与标定都按真实处数算），只是超出这个数的命中不攒在内存里、也不逐条排出去：
// 同一条规则能报十几万处，逐条排出去的报告上百兆，条目本身也占着几兆。
//
// 为什么是 200：fixtures/report 下那份长文档的 agent-inline-code 单条规则报了 96 处，
// 是对照基准里单规则最多的一处；取 200 留出一倍余量，现有基准一条都不会因为封顶而变。
// 再小就要在一块长文档的同类命中里挑位置给人看，再大就省不下这几兆。
inline constexpr std::size_t kMaxListedHitsPerRule = 200;

// 命中里不能从「规则表 + 原文区间」切出来的那几样：算法类规则算出来的命中原文与说明，
// 以及已经切好的展示片段。三样都按「偏移 + 长度」存在 LintOutcome::arena 里：
// 十几万处各分配一个 std::string，分配器的开销比内容还多，位置还散。
//
// 单扫一份时只给进得了命中表的命中建这一项：没列出去的命中不排版，建了也没人读
// （见 LintOutcome::extras）。
//
// 数据规则（词面与正则两类）的命中不带这一项：它的原文就是原文区间里的那段字节。
// 长度 0 表示没有这一样，按区间从稿子里切。
struct LintHitExtra {
    std::uint32_t match_offset = 0;    // 命中原文，报给读者的那几个字
    std::uint32_t match_length = 0;
    std::uint32_t detail_offset = 0;   // 规则说明
    std::uint32_t detail_length = 0;
    std::uint32_t snippet_offset = 0;  // 已经切好的展示片段
    std::uint32_t snippet_length = 0;
};

// 一处命中就是「哪条规则、原文哪一段、在第几行第几列」。
// 标题、说明、命中原文都不在这里：标题与说明回规则表取，原文按区间从稿件里切。
struct LintHit {
    // 字节区间。用 32 位存：十几万处命中乘以 8 字节就是一两兆，而峰值只剩一兆的余量。
    // 稿件因此限在 4GB 以内（run_lint 遇到更大的直接报错，不静默截断）。
    std::uint32_t begin = 0;
    std::uint32_t end = 0;
    std::uint32_t line = 0;            // 从 1 开始，按 UTF-16 码元数列
    std::uint32_t column = 0;
    std::uint32_t length = 0;          // 命中长度，按 UTF-16 码元
    std::uint32_t order = 0;           // 第几处：起点相同时按它保序，等价于按出现的先后
    std::uint16_t rule = kNoIndex;     // 规则表下标
    std::uint16_t hint = kNoIndex;     // 命中的那条模式对应的建议，没有就是 kNoIndex
    // 附加串下标，指向 LintOutcome::extras；没有就是 kNoExtra。
    // 按 32 位存：扫描途中它先是「这一处自己的那一格」（一格只属于一条命中），
    // 命中表定下来之后换成 extras 里的位置。16 位装不下同一份稿子里的每一处，
    // 六万多处之后会绕回到前面别的命中那一格，读到的说明就是别人的。
    std::uint32_t extra = kNoExtra;
    std::uint16_t snippet_length = 0;  // 展示片段最多取多少字，0 表示同命中长度
};

// 一份稿子在与写前那一份对差时站的位置。净增是两稿之间的事，单扫一份无从判定；
// 「手上没有写前那一份」与「这就是写前那一份」对读者是两件事，所以分开说。
enum class LintScanRole {
    Standalone,  // 单独一份稿子，手上没有写前那一份
    Baseline,    // 写前那一篇：它自己没有净增可比，按净增算的门限不判
    Compared,    // 写后那一篇：按净增算的门限留到差分之后判
};

struct LintOptions {
    std::vector<std::string> only_rules;   // 只跑这些规则，空表示按默认集合
    std::string min_severity = "info";     // info 或 warn
    std::string genre;                     // 空表示不限

    // 这份稿子在与写前那一份对差时站什么位置。只有按净增算的密度门限读它。
    LintScanRole role = LintScanRole::Standalone;

    // 点名的规则之外要不要从结果里裁掉。写前那一份要留着全部命中来计数，
    // 那一趟把这一项关掉；其余情况下按点名裁。
    bool cut_to_named = true;

    // 命中表要不要留全。默认按每条规则 kMaxListedHitsPerRule 处封顶（为守住内存）；
    // 要写命中日志的那一趟把它打开：日志按全部命中写，写完再按上限裁给人读报告与 JSON 用。
    bool keep_all_hits = false;

    // 单条规则的时限。按整条规则累计，累计的量里算着每一处正则试错的花费：
    // 到点停下并报错，不静默跳过。测试把它调小，用来触发超时那条路径。
    std::chrono::milliseconds rule_time_budget{2000};
};

struct LintNote {
    std::string rule_id;  // 附注属于哪条规则，JSON 里按它写出去
    std::string text;     // 报告里原样印出的那一句
};

// 把命中里的下标与字节区间换成字符串。排版与验收都只走这一处，两边不会各切一份。
// 三个素材都由调用方保管，生命周期要长过用到它的那份报告。
struct HitFields {
    const std::vector<LintRule>& rules;
    const std::vector<LintHitExtra>& extras;
    std::string_view arena;  // 附加串都在这块内存里，按偏移取
    std::string_view text;

    const LintRule& rule_of(const LintHit& hit) const;
    const LintHitExtra* extra_of(const LintHit& hit) const;
    // arena 里那一段；长度 0 或越界就是空。
    std::string_view arena_part(std::uint32_t offset, std::uint32_t length) const;

    // 命中原文；数据规则就是原文里那一段字节，算法类规则用它写回来的那一串。
    std::string_view match_of(const LintHit& hit) const;
    std::string_view detail_of(const LintHit& hit) const;
    // 命中的那条模式对应的建议、规则表里写的可套用方案，没写就是空。
    std::optional<std::string_view> hint_of(const LintHit& hit) const;
    std::optional<std::string_view> scheme_of(const LintHit& hit) const;
    // 已经切好的展示片段；没有就是空，由排版那层按原文区间现切。
    std::string_view ready_snippet_of(const LintHit& hit) const;
};

struct LintOutcome {
    std::vector<LintHit> hits;             // 已按行列升序排好；每条规则最多留 kMaxListedHitsPerRule 处
    // 附加串：只给留在 hits 里的命中建，一条命中一处。
    // 扫描时先把命中的说明按「位置」记在别处（见 Scan::store_early_detail），命中表定下来之后
    // 再按格取成这一份。命中里的下标就是它自己那一格，一格只属于一条命中。
    std::vector<LintHitExtra> extras;      // 命中里 extra 下标指向这里
    std::string arena;                     // 附加串的内容，extras 里的偏移指向这里
    std::vector<LintNote> notes;           // 统计附注，报告里单独一段

    // 每条规则的真实命中处数，按规则表下标。命中封顶之后 hits 里只剩前几处，这个数仍是全的：
    // 密度门限、报告开头的「发现 N 处」、组头与「另有 N 处未列出」都按它算。
    // 空表示没数过（手上只有 hits 的调用方），排版时按 hits 现数。
    std::vector<std::size_t> rule_hits;
    std::size_t units = 0;                 // 正文字数，按 UTF-16 码元：博文结构那部分不计
    std::string paragraph_mode = "line";   // 分段方式：line 或 markdown，报告里原样写出去

    // 给了写前那一份时才有：写前那一份的命中总处数，人读报告开头与 JSON 的 BaselineHits 都是它。
    std::optional<int> baseline_hits;

    // 命中里的下标按这两样换字符串：本次跑的规则表、本次扫的原文。
    // 都归调用方保管，生命周期要长过这份结果。
    const std::vector<LintRule>* rules = nullptr;
    std::string_view text;

    HitFields fields() const { return {*rules, extras, arena, text}; }
};

// 一条数据规则编译后的样子：词面在前、正则在后的模式表，外加建议改法的下标对齐。
//
// 两类规则的匹配走同一条路：词面串与 literal 规则的模式各转义一遍再交给引擎，
// 只有 regex 规则的模式原文送进引擎解释。Hints 的下标按合并后的模式表算。
//
// 规则里那些「命中起点必然落在某几串字面量的起点上」的模式（见 Pattern::start_plan）
// 另外收成一张前缀索引（见 needle_scan.h）：一条规则一轮全文扫描，
// 比一个模式扫一遍全文省几十趟。索引与模式表的生命周期一致，扫几份稿子只建一次。
class CompiledRule {
public:
    // 走快路的模式的一串候选：命中起点落在这串字节的起点上。
    struct FastEntry {
        std::size_t pattern = 0;  // patterns() 里的下标
        std::size_t hint = 0;
        bool whole = false;       // 命中就是这一串本身，不必回验（整条模式是一串字面量）
    };

    explicit CompiledRule(const LintRule& rule);
    CompiledRule(CompiledRule&&) noexcept = default;
    CompiledRule& operator=(CompiledRule&&) noexcept = default;
    CompiledRule(const CompiledRule&) = delete;
    CompiledRule& operator=(const CompiledRule&) = delete;

    const LintRule& rule() const { return rule_; }
    const std::vector<std::pair<Pattern, std::size_t>>& patterns() const { return patterns_; }

    // 这条模式的建议改法；规则表没给这一项就是空。
    std::optional<std::string> hint_at(std::size_t index) const;

    // 快路：候选串表、索引，以及「这个下标的模式走不走快路」。
    const std::vector<FastEntry>& fast_entries() const { return fast_entries_; }
    const NeedleScanner& needle_scanner() const { return scanner_; }
    bool has_fast() const { return !fast_entries_.empty(); }
    bool fast_pattern(std::size_t index) const { return fast_first_[index] != kNoFastEntry; }

    // 回验用：快路对每一条走快路的模式留一个匹配器反复用（见 PatternVerifier）。
    const Pattern& pattern_at(std::size_t index) const { return patterns_[index].first; }

private:
    static constexpr std::size_t kNoFastEntry = static_cast<std::size_t>(-1);

    // 把能走快路的模式收成一张前缀索引（见 lint_runner.cpp 里的 build_fast_path）。
    void build_fast_path();

    const LintRule& rule_;
    std::vector<std::pair<Pattern, std::size_t>> patterns_;
    std::vector<FastEntry> fast_entries_;
    std::vector<std::size_t> fast_first_;  // 每个模式的第一个候选串下标，没有就是 kNoFastEntry
    NeedleScanner scanner_;                 // 候选串的前缀索引
};

// 一次运行里编译好的规则表：同一张规则表只编译一遍，这一次运行里的所有稿子共用。
//
// 编译按需进行：某条规则第一次真正要跑时才编译，时机与逐条扫描时一样，
// 所以规则表里写坏的密度基准仍然在扫到那一条时才报，被跳过的规则不报。
// 规则表的生命周期必须长过这份编译结果：编译结果里的引用与命中下标都指回那张表。
// 同一份编译结果不要同时拿到两个线程里跑。
class CompiledRules {
public:
    explicit CompiledRules(const std::vector<LintRule>& rules);
    CompiledRules(const CompiledRules&) = delete;
    CompiledRules& operator=(const CompiledRules&) = delete;

    const std::vector<LintRule>& rules() const { return *rules_; }

    // 第 index 条规则的编译结果。算法规则没有这一项，返回空。
    // 第一次调用时编译那一条，之后直接给同一份。
    const CompiledRule* compiled_at(std::size_t index) const;

private:
    const std::vector<LintRule>* rules_;
    mutable std::vector<std::optional<CompiledRule>> cache_;
};

// 跑一遍检查。单条规则执行超过时限时抛 std::runtime_error，消息里带规则编号，
// 到点就停下并响亮报错，不静默跳过。
//
// 三类规则都在这一层跑，按规则表顺序：词面与正则两类靠匹配层找位置，
// 标了 algorithm 的那 15 条交给算法层，命中位置与统计附注都回到同一份结果里。
//
// 跑多份稿子时先把规则表编译一次（CompiledRules），每份都走下面那个重载：
// 四百多个模式只编译一遍，不必每份稿子重来一次。
LintOutcome run_lint(const CompiledRules& compiled, std::string_view text,
                     const LintOptions& options);
LintOutcome run_lint(const std::vector<LintRule>& rules, std::string_view text,
                     const LintOptions& options);

// ---------------------------------------------------------------------------
// 与修改前那一稿对比
//
// 同一套规则把写前那份也扫一遍，按「规则编号加命中原文」计数相减，只留正文里比写前那一份多出来的位置。
// 写前与写后的行号没有对应关系，一句话挪了位置不该算成新增又删掉；同一个词多出现一次才是真新增。
// 代价是「把一个词换成同义的另一个」会被算成删一处、加一处，那是宁可多报的方向。
// ---------------------------------------------------------------------------

// 写前那一份的命中计数。键是规则编号（不区分大小写）与命中原文（逐字节比）。
struct BaselineCounts {
    std::map<std::string, std::map<std::string, int>> by_rule_and_match;
    std::vector<std::string> rule_ids;  // 写前那一份里出现过的规则编号，报告里按它写新增处数
    int total_hits = 0;
};

// 数一份写前的结果。那一份按 role = Baseline 扫：点名的规则不裁，按净增算的门限不判。
BaselineCounts count_baseline(const LintOutcome& baseline);

// 把正文那一份裁成「比写前那一份新增的位置」，并按规则写一行新增处数。
// 收尾的两刀：命中只留点名的规则与警告级，统计附注只留点名的规则。
// 新增处数按裁之前的命中数写，所以一条规则即使命中全被警告级门限挡掉，仍会留下「新增 0 处」那一行。
void apply_baseline(LintOutcome& body, const BaselineCounts& baseline,
                    const std::vector<std::string>& only_rules, const std::string& min_severity);

// 每条规则只留前 kMaxListedHitsPerRule 处，多出来的从 hits 里去掉；真实处数留在 rule_hits 里不动。
//
// 单扫一份稿子时，扫描那一层在攒命中时就顺手做了（省的不只是排版的字节，是峰值）；
// 带 --baseline 的一趟要先留全命中才裁得出净增位置，封顶排在 apply_baseline 之后，由报告装配调用。
// 已经封过顶的再调一次什么也不改。
void cap_listed_hits(LintOutcome& outcome);

}  // namespace texttool
