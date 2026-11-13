// 由代码统计的规则：repeat 组，含逐字重复、复读串、段首重复三条。
//
// 三条的期望输出见 fixtures/cli 下的 repeat_verbatim、repeat_token、repeat_opening
// 三份 .expected.json：命中逐字段对得上，这几条规则才算没写坏。
// 三条的阈值都是代码常量，规则表只带标题、严重程度与说明文字的模板：
//   * 逐字重复：去首尾空白后完全相同的段落（20 字以上）再次出现就报；整段已报过时，
//     段里的句子不再逐句报。句级（12 字以上）的多处重复若指向同一处原文、间距又对得上，
//     并成一处，说明里给句数与源头位置。
//   * 复读串：同一个单元（2 到 4 字）在同一段里连写四遍以上，整串报一处，
//     命中原文是串首那个单元。单字连写、语气字、带空白的、不含字的单元都不算。
//   * 段首重复：四段以上，头两个字相同的段出现三次以上，只出统计附注，不出命中。
//
// 字数一律按 UTF-16 码元算，基本平面以外的字符算两个，与报告里的列号算法相同。
//
// 切段用 scan.h 的 split_paragraphs：一行一段，空行跳过。markdown 那份分段（空行分段、
// 标题与列表各自成段）要另切，所以用哪种分段由接线层定，切好的段随 AlgoContext 交过来；
// 上下文里没有段落时才按逐行分段现切一份（逐个调算法的测试走这条）。
//
// 命中说明的去处：AlgoWriter 的入参只有字节范围与命中原文，说明里的行列与次数只有算法知道，
// 所以每次调 writer 之前把本次的说明放进 repeat_pending_detail()，接线层在 writer 回调里取。
// 整串报一处时命中可以很长，展示片段的上限同样由这里交接（repeat_pending_snippet_chars()）。
// 逐字重复报过的段里若还有别的规则的命中，是在复读串这一步剔掉的；
// 那批区间由 repeat_token_run_spans() 交出去，剔除动作归接线层。
#include <algorithm>
#include <cstddef>
#include <cstdint>
#include <optional>
#include <string>
#include <string_view>
#include <vector>

#include "engine/location.h"
#include "engine/scan.h"

namespace texttool {
namespace {

// ---------------------------------------------------------------- 阈值

constexpr std::size_t kMinRepeatParagraphChars = 20;  // 段重复的字数门槛
constexpr std::size_t kMinRepeatSentenceChars = 12;   // 句重复的字数门槛
constexpr std::size_t kRepeatJoinGap = 2;             // 并段容忍的间隔，按字算
constexpr std::size_t kTokenMinRun = 4;               // 同一单元连写几遍算复读
constexpr std::size_t kTokenMinUnit = 2;              // 单元的字数下限
constexpr std::size_t kTokenMaxUnit = 4;              // 单元的字数上限
constexpr std::size_t kOpeningMinParagraphs = 4;      // 段首重复要看的段数下限
constexpr std::size_t kOpeningMinCount = 3;           // 同一开头出现几次才报
constexpr std::size_t kOpeningHeadUnits = 2;          // 段首取几个码元

// 逐字重复的片段上限，按字数算：每一处命中都取命中长度与四十字的较小者。
constexpr std::size_t kSnippetChars = 40;

// 「从这一句起共 N 句与第 X 行第 Y 列起逐字相同」：并段时用的说明。
// 规则表里只有一条说明字段，这句是它的第二种写法，写死在代码里。
constexpr std::string_view kGroupDetail = "从这一句起共 {0} 句与第 {1} 行第 {2} 列起逐字相同";

// 语气字与拟声字：连着写是说话的样子，不是没词了。
constexpr std::u32string_view kInterjections = U"哈呵嘿嘻呜哇呀吧啦嗯哦喔欸咦唔噗额呃嗗嘎啊嘤咪嘘唶";

// ---------------------------------------------------------------- 字符

struct CodePoint {
    std::size_t bytes = 1;
    char32_t value = 0;
};

// 解一个字符。坏字节按一个字节处理，与 scan.cpp 的折算方式相同。
CodePoint decode(std::string_view text, std::size_t at) {
    const auto lead = static_cast<unsigned char>(text[at]);
    if (lead < 0x80) return {1, lead};

    std::size_t bytes = 0;
    char32_t value = 0;
    if ((lead & 0xE0) == 0xC0) {
        bytes = 2;
        value = lead & 0x1F;
    } else if ((lead & 0xF0) == 0xE0) {
        bytes = 3;
        value = lead & 0x0F;
    } else if ((lead & 0xF8) == 0xF0) {
        bytes = 4;
        value = lead & 0x07;
    } else {
        return {1, lead};
    }
    if (at + bytes > text.size()) return {1, lead};
    for (std::size_t i = 1; i < bytes; ++i) {
        const auto next = static_cast<unsigned char>(text[at + i]);
        if ((next & 0xC0) != 0x80) return {1, lead};
        value = (value << 6) | (next & 0x3F);
    }
    return {bytes, value};
}

// 一个码元占几个 UTF-16 码元：基本平面以外算两个。
std::size_t utf16_width(char32_t value) { return value > 0xFFFF ? 2 : 1; }

bool is_whitespace(char32_t value) {
    return (value >= 0x09 && value <= 0x0D) || value == 0x20 || value == 0x85 || value == 0xA0 ||
           value == 0x1680 || (value >= 0x2000 && value <= 0x200A) || value == 0x2028 ||
           value == 0x2029 || value == 0x202F || value == 0x205F || value == 0x3000;
}

// 字（字母与汉字）：复读串要求单元里至少有一个字，否则标点、emoji、数字连写也会被当成复读。
// 表只收到日常写在稿子里的那几段文字，未收录的冷门文字会被当成「不是字」，
// 代价是那些文字上的复读串不报，不会多报。
bool is_letter(char32_t value) {
    if (value < 0x80)
        return (value >= U'a' && value <= U'z') || (value >= U'A' && value <= U'Z');
    if (value < 0xC0) return false;
    return (value >= 0xC0 && value <= 0x24F)     // 拉丁扩展
           || (value >= 0x370 && value <= 0x4FF)  // 希腊、西里尔
           || (value >= 0x531 && value <= 0x58F)  // 亚美尼亚
           || (value >= 0x5D0 && value <= 0x5EA)  // 希伯来
           || (value >= 0x620 && value <= 0x64A)  // 阿拉伯
           || (value >= 0x900 && value <= 0x0DFF)  // 天城文到僧伽罗
           || (value >= 0x0E00 && value <= 0x0E7F)  // 泰文
           || (value >= 0x1E00 && value <= 0x1EFF)  // 拉丁扩展附加
           || (value >= 0x3040 && value <= 0x30FF)  // 平假名、片假名
           || (value >= 0x3400 && value <= 0x4DBF)  // 汉字扩展 A
           || (value >= 0x4E00 && value <= 0x9FFF)  // 汉字
           || (value >= 0xAC00 && value <= 0xD7A3)  // 谚文
           || (value >= 0xF900 && value <= 0xFAFF)  // 兼容汉字
           || (value >= 0x20000 && value <= 0x2FA1F);  // 汉字扩展 B 及以后
}

bool is_sentence_end(char32_t value) {
    return value == U'。' || value == U'！' || value == U'？' || value == U'…';
}

// 往前走 chars 个字符，撞到 limit 就停；reached 回填实际走了几个。
// 从 from 起最多取 want 个字符，每个字符记下码点与它在这里占几个字节。
// 撞到 limit 就少取；末一个字符被 limit 切断时按切出来的那几个字节算，与逐字往前走的取法一致。
// 返回实际取到几个字符。
struct UnitChar {
    std::size_t bytes = 1;
    char32_t value = 0;
};

std::size_t take_chars(std::string_view text, std::size_t from, std::size_t limit, UnitChar* out,
                       std::size_t want) {
    std::size_t at = from;
    std::size_t taken = 0;
    while (at < limit && taken < want) {
        const CodePoint cp = decode(text, at);
        out[taken].bytes = std::min(cp.bytes, limit - at);
        out[taken].value = cp.value;
        at += out[taken].bytes;
        ++taken;
    }
    return taken;
}

// 往前走 units 个 UTF-16 码元，撞到码元中间就停在那个字符之前。
std::size_t advance_units(std::string_view text, std::size_t from, std::size_t limit,
                          std::size_t units) {
    std::size_t at = from;
    std::size_t used = 0;
    while (at < limit) {
        const CodePoint cp = decode(text, at);
        const std::size_t width = utf16_width(cp.value);
        if (used + width > units) break;
        used += width;
        at += std::min(cp.bytes, limit - at);
    }
    return at;
}

// ---------------------------------------------------------------- 说明文字的占位符

// 占位符的拼法在 scan.cpp：接线层按存的数现拼说明时用的是同一份（见 fill_detail_args）。

std::string number(std::size_t value) { return std::to_string(value); }

// 这三条规则要的段落：接线层切好的那份；没有就按逐行分段现切一份留着。
// 交回来的是引用：一份十五兆的稿子有十五万段，按值返回就是每一条规则各拷一份两兆半，
// 三条规则叠着拷，峰值就白抬上去。现切的那份按线程留一份，用完下次覆盖。
const std::vector<Span>& paragraphs_of(const AlgoContext& context) {
    static thread_local std::vector<Span> fresh;
    if (context.paragraphs != nullptr) return *context.paragraphs;
    fresh = split_paragraphs(context.text);
    return fresh;
}

// ---------------------------------------------------------------- 命中说明的交接

std::string& pending_detail() {
    static thread_local std::string detail;
    return detail;
}

std::vector<Span>& token_spans_of_run() {
    static thread_local std::vector<Span> spans;
    return spans;
}

// 本次命中的展示片段最多取多少字，0 表示按命中长度取。整串报一处时命中可以很长，
// 片段窗口截到四十字：读者要看的是那一串的样子，不是整串全文。
std::size_t& pending_snippet_chars() {
    static thread_local std::size_t chars = 0;
    return chars;
}

// 写一条命中：先把说明与片段上限放进交接口，再调 writer。
// position 给出时，说明里那两个数就是这个位置的行列，接线层可以只留位置现拼说明（见 scan.h）。
void emit(AlgoWriter writer, void* writer_context, std::size_t begin, std::size_t end,
          std::string_view excerpt, std::string detail, std::size_t snippet_chars = 0,
          DetailPosition position = DetailPosition{}) {
    pending_detail() = std::move(detail);
    pending_snippet_chars() = snippet_chars;
    set_pending_detail_position(position);
    writer(begin, end, std::string(excerpt), writer_context);
}

// ---------------------------------------------------------------- 句子

// 句末标点之后另起一句，句末标点留在它那句的末尾；切出来的句子去首尾空白。
// 切法就是每个句末字符后切一刀，连着的「……」因此算两刀，
// 与 scan.h 里按「连续的句末标点算一句的结尾」切出来的句子不同，逐字重复认句只认前一种。
//
// 切出一句交一句，不把整份句子表攒起来：一份十五兆的稿子有十几万句，攒起来就是一兆多。
// sink 收到的是这一句在 text 上的字节区间。
template <typename Sink>
void for_each_sentence(std::string_view text, Sink&& sink) {
    auto push_trimmed = [&](std::size_t from, std::size_t to) {
        std::size_t begin = from;
        std::size_t end = to;
        while (begin < end) {
            const CodePoint cp = decode(text, begin);
            if (!is_whitespace(cp.value)) break;
            begin += cp.bytes;
        }
        while (end > begin) {
            // 从尾部退到一个字符的起点再判空白
            std::size_t start = end - 1;
            while (start > begin && (static_cast<unsigned char>(text[start]) & 0xC0) == 0x80) --start;
            if (!is_whitespace(decode(text, start).value)) break;
            end = start;
        }
        if (end > begin) sink(begin, end);
    };

    std::size_t start = 0;
    std::size_t at = 0;
    while (at < text.size()) {
        const CodePoint cp = decode(text, at);
        at += cp.bytes;
        if (!is_sentence_end(cp.value)) continue;
        push_trimmed(start, at);
        start = at;
    }
    push_trimmed(start, text.size());
}

// ---------------------------------------------------------------- 首现表

// 「同一段字在前面出现过没有」的平表：一次分配，一段占八字节，用完整块交回系统。
//
// 键不是字串副本，是稿子上那一段的偏移与长度，比字串时直接拿稿子比。这么存不为省几个字节，
// 是为了一件事：这块记忆要能整块还回去。一个键一个节点的表（std::map、unordered_map 都是）
// 十几万段就是十几万次分配，用完交回去也留在分配器里，十五兆的稿子上实测六兆半不还页。
//
// 槽位八字节：偏移四字节、长度四字节，长度为零表示空槽（段至少有十二个字，不会是零）。
// 不存哈希值：哈希只用来挑起手的槽位，比的时候一律拿稿子逐字节比，所以哈希算得糙也不会
// 多报或少报，只多探几个槽位。
//
// 容量按「要放几段」估。估少了翻倍，翻倍那一刻新旧两份同时在，峰值就落在那一瞬，
// 数得出段数的地方（段级）按段数一次给足。稿子只到四吉字节（见 lint_runner 的
// kMaxTextBytes），偏移与长度按三十二位存得下。

class SpanFirstSeen {
public:
    explicit SpanFirstSeen(std::size_t expected) {
        std::size_t slots = 64;
        while (slots * 3 < expected * 4) slots *= 2;  // 装到七成五
        slots_.assign(slots, Slot{});
    }

    SpanFirstSeen(const SpanFirstSeen&) = delete;
    SpanFirstSeen& operator=(const SpanFirstSeen&) = delete;

    // 这一段（text 上的 offset 起、length 字节）在前面见过没有。
    // 见过返回真，并把首次出现那一段的起点写回 first；没见过就记下这一次，返回假。
    bool seen_before(std::string_view text, std::size_t offset, std::size_t length,
                     std::size_t& first) {
        std::size_t index = hash_of(text, offset, length) & (slots_.size() - 1);
        while (true) {
            Slot& slot = slots_[index];
            if (slot.length == 0) {
                slot.offset = static_cast<std::uint32_t>(offset);
                slot.length = static_cast<std::uint32_t>(length);
                if (++used_ * 4 >= slots_.size() * 3) grow(text);
                return false;
            }
            if (slot.length == length && text.compare(offset, length, text, slot.offset, length) == 0) {
                first = slot.offset;
                return true;
            }
            index = (index + 1) & (slots_.size() - 1);
        }
    }

    // 这张表到此不用了：整块交回系统。留到函数返回时才析构也一样要还，写出来是让
    // 读者一眼看到表不跨规则留着。
    void release() {
        std::vector<Slot>().swap(slots_);
        used_ = 0;
    }

private:
    struct Slot {
        std::uint32_t offset = 0;  // 首次出现那一段的起点
        std::uint32_t length = 0;  // 那一段的字节数；0 表示空槽
    };

    static std::uint64_t hash_of(std::string_view text, std::size_t offset, std::size_t length) {
        std::uint64_t hash = 1469598103934665603ull;
        for (std::size_t i = 0; i < length; ++i) {
            hash ^= static_cast<unsigned char>(text[offset + i]);
            hash *= 1099511628211ull;
        }
        return hash;
    }

    void grow(std::string_view text) {
        std::vector<Slot> bigger(slots_.size() * 2, Slot{});
        const std::size_t mask = bigger.size() - 1;
        for (const Slot& slot : slots_) {
            if (slot.length == 0) continue;
            std::size_t index = hash_of(text, slot.offset, slot.length) & mask;
            while (bigger[index].length != 0) index = (index + 1) & mask;
            bigger[index] = slot;
        }
        slots_.swap(bigger);
    }

    std::vector<Slot> slots_;
    std::size_t used_ = 0;
};

// ---------------------------------------------------------------- 逐字重复

void run_verbatim(const LintRule& rule, const AlgoContext& context, AlgoWriter writer,
                  void* writer_context, std::vector<std::string>& notes) {
    (void)notes;  // 这条规则只出命中，不出统计附注
    const std::string_view text = context.text;
    const MeasuredText measured = context.measured();
    // 行列按原文算：行列里的「列」是码元数。空格副本（把结构区域都写成空格、与原文等长的
    // 那一份）里被遮的多字节字会把它撑大。
    // 接线层把行索引交过来了就用它，单跑这条算法时现建一份。
    const std::optional<LineIndex> own_index =
        context.locations == nullptr ? std::optional<LineIndex>(context.original) : std::nullopt;
    const LineIndex& index = context.locations != nullptr ? *context.locations : *own_index;

    // 段落级：键是段原文，值是首现的位置。首现之外每出现一次报一处。
    // 首现表按段数一次给足：段数数得出来，翻倍那一刻的双份就不会发生。
    const std::vector<Span>& paragraphs = paragraphs_of(context);
    SpanFirstSeen first_seen(paragraphs.size());
    // 已报过的段只留它在段落表里的下标：段表本身就在手边，存下标比存一份段区间省一半。
    std::vector<std::uint32_t> reported_paragraphs;
    for (const Span& paragraph : paragraphs) {
        if (measured_units(measured, paragraph.begin, paragraph.end) < kMinRepeatParagraphChars) continue;
        // 键用空格副本上的那一段：两段只差代码块里的字的，按博文规则就是同一段。
        std::size_t first_begin = 0;
        if (!first_seen.seen_before(text, paragraph.begin, paragraph.end - paragraph.begin,
                                    first_begin))
            continue;

        // 首现那一处的行列现算：表里只存偏移，不存行列，一处省十六字节。
        const Location first = index.locate(first_begin, first_begin +
                                                         (paragraph.end - paragraph.begin));
        // 报给读者的命中原文从原文上切：空格副本里结构区域是空格。
        emit(writer, writer_context, paragraph.begin, paragraph.end,
             context.original.substr(paragraph.begin, paragraph.end - paragraph.begin),
             fill_detail_args(rule.detail, {number(first.line), number(first.column)}), 0,
             DetailPosition{true, first_begin});
        reported_paragraphs.push_back(
            static_cast<std::uint32_t>(static_cast<std::size_t>(&paragraph - paragraphs.data())));
    }
    // 段级表到此不用了：整块交回去，句级那张表不与它叠着占。
    first_seen.release();

    // 句级：首现表与段级分开，段与句的长度不同，同一段的两份不会互相冒充。
    // 句子数事先数不出来，从一小档起边放边翻倍。
    SpanFirstSeen first_at(0);
    struct Repeat {
        std::size_t begin = 0;
        std::size_t end = 0;
        std::size_t source_begin = 0;
        std::size_t source_end = 0;
    };
    std::vector<Repeat> repeats;
    for_each_sentence(text, [&](std::size_t sentence_begin, std::size_t sentence_end) {
        if (measured_units(measured, sentence_begin, sentence_end) < kMinRepeatSentenceChars) return;
        // 段里已经报过的句子不再逐句报
        const auto after = std::upper_bound(
            reported_paragraphs.begin(), reported_paragraphs.end(), sentence_begin,
            [&](std::size_t offset, std::uint32_t slot) {
                return offset < paragraphs[slot].begin;
            });
        if (after != reported_paragraphs.begin()) {
            const Span& last = paragraphs[*(after - 1)];
            if (sentence_begin < last.end) return;
        }

        const std::size_t length = sentence_end - sentence_begin;
        std::size_t source = 0;
        if (!first_at.seen_before(text, sentence_begin, length, source)) return;
        repeats.push_back({sentence_begin, sentence_end, source, source + length});
    });

    // 把连着的重复并成一段：贴回一长段时，逐句各报一处只会把报告淹掉。
    // 判据是两边的间隔对得上：相邻两次重复在当前位置之间的间隔，要等于它们在源头之间的间隔。
    std::size_t at = 0;
    while (at < repeats.size()) {
        std::size_t begin = repeats[at].begin;
        std::size_t end = repeats[at].end;
        std::size_t source_begin = repeats[at].source_begin;
        std::size_t source_end = repeats[at].source_end;
        std::size_t count = 1;
        while (at + 1 < repeats.size()) {
            const Repeat& next = repeats[at + 1];
            if (next.begin < end || next.source_begin < source_end) break;
            const std::size_t gap = measured_units(measured, end, next.begin);
            const std::size_t source_gap = measured_units(measured, source_end, next.source_begin);
            if (gap > kRepeatJoinGap || source_gap != gap) break;
            end = next.end;
            source_end = next.source_end;
            ++count;
            ++at;
        }

        const Location source = index.locate(source_begin, source_end);
        const std::string detail =
            count > 1 ? fill_detail_args(kGroupDetail, {number(count), number(source.line), number(source.column)})
                      : fill_detail_args(rule.detail, {number(source.line), number(source.column)});
        // 片段上限按四十字：并段报的是好几句话，单句报的是长句，都给读者看开头那一段。
        const std::size_t snippet = std::min(measured_units(measured, begin, end), kSnippetChars);
        emit(writer, writer_context, begin, end, context.original.substr(begin, end - begin), detail,
             snippet, count > 1 ? DetailPosition{} : DetailPosition{true, source_begin});
        ++at;
    }
    // 句级表也放掉：这条规则的两张表都不跨规则留着。
    first_at.release();
}

// ---------------------------------------------------------------- 复读串

struct TokenRun {
    std::size_t run_begin = 0;
    std::size_t run_end = 0;
    std::size_t unit_begin = 0;
    std::size_t unit_end = 0;
    std::size_t count = 0;

    explicit operator bool() const { return count != 0; }
};

// 从 from 起的复读串：单元从短到长试，最短的那个凑够四遍就是这串的周期。
// 从短往长试，撞上的那个才是读者眼里的「那个词」，「极其极其极其极其」是「极其」在复读。
//
// 段里的每个位置都要试一遍，所以开头的几个字符只解一次：短单元是长单元的前缀，
// 三种单元共用同一批码点。逐字判断各自再解一遍时，一份 15MB 的稿子在这一条规则上要白解
// 上千万次。
TokenRun find_token_run(std::string_view text, const Span& paragraph, std::size_t from) {
    UnitChar chars[kTokenMaxUnit + 1];
    const std::size_t available =
        take_chars(text, from, paragraph.end, chars, kTokenMaxUnit + 1);

    for (std::size_t unit_chars = kTokenMinUnit; unit_chars <= kTokenMaxUnit; ++unit_chars) {
        if (available < unit_chars) break;
        // 连写四遍意味着第二遍的第一个字与第一遍的第一个字相同。这一条一次比较就能排掉
        // 绝大多数位置，省下下面那几样逐字判断与整串比较。
        if (available > unit_chars && chars[0].value != chars[unit_chars].value) continue;

        bool has_whitespace = false;
        bool has_letter = false;
        bool all_same = true;
        bool all_interjection = true;
        std::size_t unit_end = from;
        for (std::size_t i = 0; i < unit_chars; ++i) {
            unit_end += chars[i].bytes;
            has_whitespace = has_whitespace || is_whitespace(chars[i].value);
            has_letter = has_letter || is_letter(chars[i].value);
            all_same = all_same && chars[i].value == chars[0].value;
            all_interjection =
                all_interjection &&
                kInterjections.find(chars[i].value) != std::u32string_view::npos;
        }
        if (has_whitespace) continue;
        if (!has_letter) continue;
        if (all_same) continue;
        if (all_interjection) continue;

        const std::string_view unit = text.substr(from, unit_end - from);
        std::size_t count = 1;
        std::size_t at = unit_end;
        while (at + unit.size() <= paragraph.end &&
               text.substr(at, unit.size()) == unit) {
            at += unit.size();
            ++count;
        }
        if (count < kTokenMinRun) continue;
        return TokenRun{from, at, from, unit_end, count};
    }
    return {};
}

void run_token_run(const LintRule& rule, const AlgoContext& context, AlgoWriter writer,
                   void* writer_context, std::vector<std::string>& notes) {
    (void)notes;  // 这条规则只出命中，不出统计附注
    const std::string_view text = context.text;
    const MeasuredText measured = context.measured();

    std::vector<Span>& runs = token_spans_of_run();
    runs.clear();

    for (const Span& paragraph : paragraphs_of(context)) {
        std::size_t at = paragraph.begin;
        while (at < paragraph.end) {
            const TokenRun run = find_token_run(text, paragraph, at);
            if (!run) {
                at += decode(text, at).bytes;
                continue;
            }

            // 说明里的那个词与命中原文都从原文上切：复读串里不会跨过被遮的字符（空白断串），
            // 两边切出来的内容因此一致。
            const std::string_view shown =
                context.original.substr(run.unit_begin, run.unit_end - run.unit_begin);
            emit(writer, writer_context, run.run_begin, run.run_end, shown,
                 fill_detail_args(rule.detail, {std::string(shown), number(run.count)}),
                 std::min(measured_units(measured, run.run_begin, run.run_end), kSnippetChars));
            runs.push_back({span_index(run.run_begin), span_index(run.run_end)});
            at = run.run_end;
        }
    }
}

// ---------------------------------------------------------------- 段首重复

void run_paragraph_opening(const LintRule& rule, const AlgoContext& context, AlgoWriter writer,
                           void* writer_context, std::vector<std::string>& notes) {
    (void)writer;
    (void)writer_context;
    const std::string_view text = context.text;

    const std::vector<Span>& paragraphs = paragraphs_of(context);
    if (paragraphs.size() < kOpeningMinParagraphs) return;

    struct Group {
        std::string_view head;  // 段首的头两个字
        std::size_t count = 0;
    };
    std::vector<Group> groups;  // 按首见次序
    for (const Span& paragraph : paragraphs) {
        const std::size_t head_end =
            advance_units(context.original, paragraph.begin, paragraph.end, kOpeningHeadUnits);
        // 段首那两个字从空格副本取：被遮的字符在那一份里是空格。
        const std::string_view head = text.substr(paragraph.begin, head_end - paragraph.begin);
        auto found = std::find_if(groups.begin(), groups.end(),
                                  [head](const Group& group) { return group.head == head; });
        if (found == groups.end())
            groups.push_back({head, 1});
        else
            ++found->count;
    }

    std::string joined;
    for (const Group& group : groups) {
        if (group.count < kOpeningMinCount) continue;
        if (!joined.empty()) joined += "、";
        joined += "「";
        joined += group.head;
        joined += "」×";
        joined += number(group.count);
    }
    if (joined.empty()) return;

    notes.push_back(fill_detail_args(rule.detail, {joined}));
}

}  // namespace

// 本次命中的说明：算好之后放进交接口，接线层在 writer 回调里取一次。
const std::string& repeat_pending_detail() { return pending_detail(); }

// 本次命中的展示片段上限，0 表示按命中长度取。
std::size_t repeat_pending_snippet_chars() { return pending_snippet_chars(); }

// 本轮复读串在原文里的字节范围：串里其他规则的命中由接线层按这批区间剔掉。
std::vector<Span> repeat_token_run_spans() { return token_spans_of_run(); }

void register_repeat_algorithms() {
    register_algorithm("repeated-verbatim", run_verbatim);
    register_algorithm("repeated-token-run", run_token_run);
    register_algorithm("repeated-paragraph-opening", run_paragraph_opening);
}

}  // namespace texttool
