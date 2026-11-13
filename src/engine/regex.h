// 规则里的「按样子找文字」这一层。
//
// 规则表 115 条里 97 条是这种规则，共 467 个模式，全部要在 15MB 稿件上跑完并随时可叫停。
// 标准库的实现没有超时，一条回溯量大的模式会把进程拖住，所以这一层自己实现，
// 每次匹配都记步数，超过上限就停下并报错，不静默放弃。
#pragma once

#include <string>
#include <string_view>
#include <vector>

namespace texttool {

struct MatchRange {
    std::size_t begin = 0;  // 字节偏移，UTF-8
    std::size_t end = 0;
};

// search() 返回这个区间表示「这一段里没有命中」。零长度命中是 begin == end，与它不同。
inline constexpr std::size_t kNoMatch = std::string_view::npos;

// 单次 search() 的两道上限，撞上任何一道都抛 std::runtime_error，不静默放弃。
//
// 第一道是步数，按「底数 + 每字节若干步」算，与本次稿件长度挂钩：
//   steps：底数。短文本、单次匹配的预算，也是「一个候选起点上试一下」的量级。
//   steps_per_byte：每字节再放这么多步。按字节线往右扫是线性的：同一个模式在 15MB 的稿子上
//     要试的起点是 1MB 的十五倍。上限卡死一个常数，等于「稿子越长越容易被误杀」，
//     所以线性的那一份按稿件长度给，把常数留给真正与长度无关的试错。
//   计步的算法：换一条分支、量词再来一轮、环视试一个候选起点，各计一步；
//   把字符一个个吃进去不算。
//   steps_per_byte = 0 表示只给底数：上限与稿件长度无关，测试用它单独验底数那一面。
//
// 另一道是扫描量，按字节算：简单原子的量词每吃一个字符、每往回退一轮，各记一次，
//   上限取「文本字节数的 64 倍」与步数上限里大的那个。退一轮的成本与剩下多少字节成正比，
//   所以平方级的模式（一串同一字符后面接个对不上的东西）烧得极快：实测 200KB 的输入
//   几十毫秒就停下，而正常稿件的线性扫描离这道线还有一个数量级的余量。
//
// 15MB 量级的稿件上实测：467 个模式全部在默认值内跑完，撞上限的 0 条。
struct MatchLimits {
    std::size_t steps = 4'000'000;
    std::size_t steps_per_byte = 32;
};

// 本次 search() 的步数上限：底数 + 每字节若干步 × 稿件字节数。
// search() 与量测都按它算，两边不会各算一份。
std::size_t step_budget(std::size_t haystack_bytes, const MatchLimits& limits);

// 同一份稿件上的重复查询：上层会按句、按段反复找同一条模式（比喻堆叠那条就是），
// 每次「从这里往后没有命中」都会把后面的整篇重扫一遍，稿件一长就是平方级。
// 登记之后，引擎才敢把上次的结论（往后没有命中 / 最近的一处在哪）留到下一次查询。
// 一份稿件开扫前 begin_document(整份正文)，扫完 end_document()。没登记时不做任何缓存，
// 段落切片与临时串也不是「整份正文」，同样不缓存。
void begin_document(std::string_view document);
void end_document();

// 「这条模式的命中起点必然落在这几串字节的起点上」：把一批模式的首集合并成一次扫描用。
//
// 只给能证明的情形，证不出来就返回 none，调用方退回逐条扫的老路：
//   literal：模式必然以这几串字面量开头（第一条预筛是「必需的字面前缀」的那些），
//            起点就是这几串的起点；
//   keys：开头必须是这几串之一，首集是穷尽且精确的（模式本身不可空）。
enum class StartKind { none, literal, keys };

struct StartPlan {
    StartKind kind = StartKind::none;
    std::vector<std::string> needles;
    // 命中就是这串字节本身，长度等于这一串：整条模式是一串字面量，不必再回验。
    bool whole = false;
};

// 一个编译后的模式。实现放在 regex.cpp，调用方不关心内部结构。
class Pattern {
public:
    explicit Pattern(std::string_view text);
    ~Pattern();
    Pattern(Pattern&&) noexcept;
    Pattern& operator=(Pattern&&) noexcept;
    Pattern(const Pattern&) = delete;
    Pattern& operator=(const Pattern&) = delete;

    const std::string& text() const;

    // 起点候选串（见上面 StartPlan）。给不出就是 none，调用方按老路逐条扫。
    StartPlan start_plan() const;

    // 在 haystack 里找从 from 起的第一处匹配（from 是字节偏移，按 UTF-8 边界给）。
    // 上层「从上次结束处继续、允许重叠」就是把上一次的 end 再传给 from。
    // 没有命中返回 begin == kNoMatch；超过步数上限抛 std::runtime_error，消息里带模式原文。
    // haystack 正好是 begin_document 登记的那份正文时，上次的结论会被用上（见上面那两行）。
    MatchRange search(std::string_view haystack, std::size_t from, const MatchLimits& limits) const;

private:
    friend class PatternVerifier;

    struct Impl;
    Impl* impl_;
};

// 连续回验一个模式：同一份稿件（或同一段）上一串候选起点各验一次。
//
// 为什么不是「每验一次调一次 search」：那样每验一次要建一个匹配器，两次堆分配，
// 一次几十纳秒。候选起点上万时，分配器本身就比判定贵。这里把匹配器留着反复用，
// 每验一次只把步数账归零（见 regex.cpp 里 Matcher::rebind 的注释）。
// 寿命：rebind 换稿件/换段，match_at 反复调，析构放掉。
class PatternVerifier {
public:
    PatternVerifier(const Pattern& pattern, std::string_view haystack, const MatchLimits& limits);
    ~PatternVerifier();
    PatternVerifier(PatternVerifier&&) noexcept;
    PatternVerifier& operator=(PatternVerifier&&) noexcept;
    PatternVerifier(const PatternVerifier&) = delete;
    PatternVerifier& operator=(const PatternVerifier&) = delete;

    void rebind(std::string_view haystack, const MatchLimits& limits);
    // 在 at 处整段验一次：这条模式从 at 起有一处命中就返回真，区间写回 out。
    bool match_at(std::size_t at, MatchRange& out);

private:
    struct Impl;
    Impl* impl_;
};

}  // namespace texttool
