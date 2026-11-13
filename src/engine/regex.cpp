// 匹配层的实现：自己写的回溯匹配器，只读 UTF-8 字节。
//
// 为什么不直接用 std::regex：它没有超时，一条回溯量大的模式会把进程拖住，而这一层要求
// 「要么在步数上限内给结果，要么响亮报错」。std::regex 也要求把整份稿件当宽字符处理，
// 15MB 会变成两三倍，直接顶破内存线。
//
// 结构分四段：UTF-8 与字符分类的底层工具、编译期（模式文本转成节点树与预筛信息）、
// 运行期（回溯匹配）、对外的 Pattern 接口。看代码时按这个顺序读。
#include "engine/regex.h"

#include <algorithm>
#include <array>
#include <atomic>
#include <bit>
#include <cstdint>
#include <cstring>

// 快扫一批十六格里有没有候选：x86-64 上一条指令比十六格，别的平台上退回逐字节查表。
// 这里只做排除，判错的方向永远是「多留候选」，真正的判定在 key_starts_at / anchored。
#if defined(__SSE2__) || defined(_M_X64) || (defined(_M_IX86_FP) && _M_IX86_FP >= 2)
#define TEXTTOOL_HAS_SSE2 1
#include <emmintrin.h>
#endif
#include <limits>
#include <optional>
#include <stdexcept>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

namespace texttool {
namespace {

constexpr std::size_t kUnset = static_cast<std::size_t>(-1);

// 每字节文本允许的字节扫描量。回溯栈与续接点都在堆上，涨到多大只由内存决定；
// 这个上限管的是「同一段字节被反复扫」:量词在长串上一轮轮往回退，
// 退一轮的成本与剩下的字节数成正比，平方级的模式会把它迅速烧光。
// 每条规则的预算 2 秒，64 的倍数在 200KB 那种病态输入上不到一百毫秒就撞上，
// 正常的长稿（线性扫）离它还有一个数量级的余量。
constexpr std::size_t kScanPerByte = 64;

// 预筛用到的三道上限，都只是「值不值得筛」的门槛：
//   kGapBytes：命中串前面「开头能吃掉」的字节种类。种类越多，往回退越接近逐字节扫，
//     这类模式交给别的预筛或逐字节试。
//   kGapClassBudget：能参与 gap 的字符类最多几个码点，与首字节集合那道门槛同一条线。
//   kNeedleBytes / kNeedleCount：候选串最长取多少字节、最多几串。
constexpr std::size_t kGapBytes = 16;

// 按字节宽度定位时，命中串前面那一节最多吃多少字节。走得越远，每个命中串要试的起点越多，
// 超过这个数就不如退回首字节那一类预筛了。
constexpr std::size_t kMaxGapWidth = 64;
constexpr std::size_t kGapClassBudget = 48;
constexpr std::size_t kNeedleBytes = 12;
// 开头第一个字符可能有多少个码点，算「宽」的门槛：过了这条线，首字节预筛放开的候选
// 就密到每个候选都要跑一遍完整匹配，不如回头按命中串定位。
constexpr std::size_t kLeadBroad = 512;
constexpr std::size_t kNeedleCount = 64;

// ---------------------------------------------------------------- UTF-8

struct Decoded {
    uint32_t cp = 0;
    std::size_t len = 1;
};

// 解一个码点。坏字节当成一个独立的字节值（0x80..0xFF），这样点号与量词仍能前进一格，
// 不会在坏字节上原地打转；字符类里的码点区间也不会误命中它。
inline Decoded decode_utf8(const char* data, std::size_t size, std::size_t pos) {
    const auto b0 = static_cast<unsigned char>(data[pos]);
    if (b0 < 0x80) return {b0, 1};
    const std::size_t left = size - pos;
    if ((b0 & 0xE0) == 0xC0 && left >= 2) {
        const auto b1 = static_cast<unsigned char>(data[pos + 1]);
        if ((b1 & 0xC0) == 0x80) {
            return {static_cast<uint32_t>((b0 & 0x1F) << 6) | (b1 & 0x3F), 2};
        }
        return {b0, 1};
    }
    if ((b0 & 0xF0) == 0xE0 && left >= 3) {
        const auto b1 = static_cast<unsigned char>(data[pos + 1]);
        const auto b2 = static_cast<unsigned char>(data[pos + 2]);
        if ((b1 & 0xC0) == 0x80 && (b2 & 0xC0) == 0x80) {
            return {static_cast<uint32_t>((b0 & 0x0F) << 12) | static_cast<uint32_t>(b1 & 0x3F) << 6 |
                        (b2 & 0x3F),
                    3};
        }
        return {b0, 1};
    }
    if ((b0 & 0xF8) == 0xF0 && left >= 4) {
        const auto b1 = static_cast<unsigned char>(data[pos + 1]);
        const auto b2 = static_cast<unsigned char>(data[pos + 2]);
        const auto b3 = static_cast<unsigned char>(data[pos + 3]);
        if ((b1 & 0xC0) == 0x80 && (b2 & 0xC0) == 0x80 && (b3 & 0xC0) == 0x80) {
            return {static_cast<uint32_t>(b0 & 0x07) << 18 | static_cast<uint32_t>(b1 & 0x3F) << 12 |
                        static_cast<uint32_t>(b2 & 0x3F) << 6 | (b3 & 0x3F),
                    4};
        }
        return {b0, 1};
    }
    return {b0, 1};
}

inline void encode_utf8(uint32_t cp, std::string& out) {
    if (cp < 0x80) {
        out.push_back(static_cast<char>(cp));
    } else if (cp < 0x800) {
        out.push_back(static_cast<char>(0xC0 | (cp >> 6)));
        out.push_back(static_cast<char>(0x80 | (cp & 0x3F)));
    } else if (cp < 0x10000) {
        out.push_back(static_cast<char>(0xE0 | (cp >> 12)));
        out.push_back(static_cast<char>(0x80 | ((cp >> 6) & 0x3F)));
        out.push_back(static_cast<char>(0x80 | (cp & 0x3F)));
    } else {
        out.push_back(static_cast<char>(0xF0 | (cp >> 18)));
        out.push_back(static_cast<char>(0x80 | ((cp >> 12) & 0x3F)));
        out.push_back(static_cast<char>(0x80 | ((cp >> 6) & 0x3F)));
        out.push_back(static_cast<char>(0x80 | (cp & 0x3F)));
    }
}

inline bool is_char_start(const char* data, std::size_t size, std::size_t pos) {
    if (pos == 0 || pos >= size) return true;
    return (static_cast<unsigned char>(data[pos]) & 0xC0) != 0x80;
}

// 从 pos 往回退一个字符的起点。坏字节自身也算一格。
inline std::size_t prev_char_start(const char* data, std::size_t pos) {
    std::size_t p = pos;
    std::size_t steps = 0;
    while (p > 0 && steps < 3 && (static_cast<unsigned char>(data[p - 1]) & 0xC0) == 0x80) {
        --p;
        ++steps;
    }
    return p > 0 ? p - 1 : 0;
}

// ---------------------------------------------------------------- 字符分类

// 非 ASCII 里按「非词字符」算的区段：标点、符号、控制字符。
// 其余（汉字、假名、谚文、拉丁扩展等）都算词字符，与 .NET 的 \w 在这些稿件上的判定一致。
constexpr uint32_t kNonWordRanges[][2] = {
    {0x0080, 0x009F}, {0x00A0, 0x00BF}, {0x2000, 0x206F}, {0x2600, 0x27BF}, {0x2B00, 0x2BFF},
    {0x3000, 0x303F}, {0xFE00, 0xFE0F}, {0xFE30, 0xFE4F}, {0xFF00, 0xFF65}, {0xFFE0, 0xFFE6},
};

inline bool is_word_cp(uint32_t cp) {
    if (cp < 0x80) {
        return (cp >= '0' && cp <= '9') || (cp >= 'a' && cp <= 'z') || (cp >= 'A' && cp <= 'Z') ||
               cp == '_';
    }
    for (const auto& range : kNonWordRanges) {
        if (cp >= range[0] && cp <= range[1]) return false;
    }
    return true;
}

inline bool is_space_cp(uint32_t cp) {
    if (cp == 0x20 || (cp >= 0x09 && cp <= 0x0D)) return true;
    if (cp == 0x85 || cp == 0xA0 || cp == 0x1680) return true;
    if (cp >= 0x2000 && cp <= 0x200A) return true;
    if (cp == 0x2028 || cp == 0x2029 || cp == 0x202F || cp == 0x205F) return true;
    return cp == 0x3000;
}

inline bool is_digit_cp(uint32_t cp) {
    return (cp >= '0' && cp <= '9') || (cp >= 0xFF10 && cp <= 0xFF19);
}

// ---------------------------------------------------------------- 字符类

struct ClassSet {
    // 排序、合并后的码点区段。
    std::vector<std::pair<uint32_t, uint32_t>> ranges;
    bool negated = false;

    void add(uint32_t lo, uint32_t hi) {
        if (lo > hi) std::swap(lo, hi);
        ranges.emplace_back(lo, hi);
    }

    void add_cp(uint32_t cp) { add(cp, cp); }

    void normalize() {
        if (ranges.empty()) return;
        std::sort(ranges.begin(), ranges.end());
        std::vector<std::pair<uint32_t, uint32_t>> merged;
        merged.reserve(ranges.size());
        for (const auto& range : ranges) {
            if (!merged.empty() && range.first <= merged.back().second + 1) {
                merged.back().second = std::max(merged.back().second, range.second);
            } else {
                merged.push_back(range);
            }
        }
        ranges.swap(merged);
    }

    bool contains(uint32_t cp) const {
        std::size_t low = 0;
        std::size_t high = ranges.size();
        while (low < high) {
            const std::size_t mid = (low + high) / 2;
            if (cp < ranges[mid].first) {
                high = mid;
            } else if (cp > ranges[mid].second) {
                low = mid + 1;
            } else {
                return true;  // 命中区段
            }
        }
        return false;
    }

    // 类里的码点是否命中（已考虑取反）。
    bool matches(uint32_t cp) const {
        const bool inside = contains(cp);
        return negated ? !inside : inside;
    }
};

void add_case_folded(ClassSet& set) {
    // 只展开 ASCII 字母。全角与希腊字母的大小写在稿件里极少出现在字符类里，
    // 而且 .NET 用的是区域相关的折叠规则，照搬不到字节层。
    std::vector<std::pair<uint32_t, uint32_t>> extra;
    for (const auto& range : set.ranges) {
        const uint32_t lo = std::max<uint32_t>(range.first, 'a');
        const uint32_t hi = std::min<uint32_t>(range.second, 'z');
        if (lo <= hi) extra.emplace_back(lo - 0x20, hi - 0x20);
        const uint32_t lo2 = std::max<uint32_t>(range.first, 'A');
        const uint32_t hi2 = std::min<uint32_t>(range.second, 'Z');
        if (lo2 <= hi2) extra.emplace_back(lo2 + 0x20, hi2 + 0x20);
    }
    for (const auto& range : extra) set.add(range.first, range.second);
    set.normalize();
}

ClassSet make_digit_class() {
    ClassSet set;
    set.add('0', '9');
    set.add(0xFF10, 0xFF19);
    set.normalize();
    return set;
}

ClassSet make_space_class() {
    ClassSet set;
    set.add(0x09, 0x0D);
    set.add(0x20, 0x20);
    set.add(0x85, 0x85);
    set.add(0xA0, 0xA0);
    set.add(0x1680, 0x1680);
    set.add(0x2000, 0x200A);
    set.add(0x2028, 0x2029);
    set.add(0x202F, 0x202F);
    set.add(0x205F, 0x205F);
    set.add(0x3000, 0x3000);
    set.normalize();
    return set;
}

ClassSet make_word_class() {
    ClassSet set;
    set.add('0', '9');
    set.add('A', 'Z');
    set.add('_', '_');
    set.add('a', 'z');
    // 非 ASCII：从 0x80 到 0x10FFFF 挖掉标点与符号区段。
    uint32_t cursor = 0x80;
    for (const auto& range : kNonWordRanges) {
        if (range[0] > cursor) set.add(cursor, range[0] - 1);
        cursor = std::max(cursor, range[1] + 1);
    }
    if (cursor <= 0x10FFFF) set.add(cursor, 0x10FFFF);
    set.normalize();
    return set;
}

// ---------------------------------------------------------------- 节点树

enum class Kind : std::uint8_t {
    Empty,
    ByteLit,   // 一段字面量字节
    Class,     // 字符类
    Dot,       // 点号
    Concat,    // 顺序
    Alt,       // 分支，kids 是各个分支的 Concat
    Repeat,    // 量词，kids 只有一个原子
    SaveStart, // 捕获组起点
    SaveEnd,   // 捕获组终点
    Backref,   // 反向引用
    Assert,    // 锚点与环视
};

enum class AssertKind : std::uint8_t {
    BeginText,
    EndText,
    BeginLine,
    EndLine,
    WordBoundary,
    NotWordBoundary,
    LookAhead,
    NegLookAhead,
    LookBehind,
    NegLookBehind,
};

struct Node {
    Kind kind = Kind::Empty;
    AssertKind assert_kind = AssertKind::BeginText;
    std::string lit;  // ByteLit；大小写不敏感时已折成小写
    bool icase = false;
    bool dot_all = false;
    ClassSet cls;
    int min = 0;
    int max = -1;  // -1 表示无上限
    bool greedy = true;
    int cap = -1;
    int ref = 0;
    int window = 0;  // 反向环视往回探多少字节
    std::vector<int> kids;
    // 分支节点用：每一支的首字节可能是哪些。空表示「说不准，一律试」。
    // 有它之后，一个位置上只试首字节对得上的那几支，不必把二十几支挨个撞一遍。
    std::vector<std::vector<std::uint8_t>> alt_first;
};

// 预筛方式：先用很便宜的手段挑出可能命中的位置，再在候选位置上跑完整匹配。
// 467 个模式逐个扫 15MB，没有这一步太慢。
// 快扫一列最多摊几条比较指令。汉字词的候选串首字节通常只有几个，够用。
constexpr std::size_t kMaxSkipLanes = 16;

// 前缀核对表的格子数：候选串一般不超过几十串，512 格撞得开。
constexpr std::size_t kPrefixSlots = 512;

// 三字节前缀的哈希：取乘积高九位。汉字与多字节词按三字节判，两字节不够：
// 汉字的次字节几乎一样，「像」与「停」在两字节上分不开。
inline std::size_t prefix_slot_index(std::uint32_t word) {
    return static_cast<std::size_t>((word * 2654435761u) >> 23);
}

// 从 data 起的前 length（二或三）个字节，按小端拼成一个整数。
// 只读用得着的那几个字节：扫描循环里传进来的指针后面未必还有第三格。
inline std::uint32_t prefix_word(const char* data, std::size_t length) {
    const std::uint32_t word = static_cast<std::uint32_t>(static_cast<unsigned char>(data[0])) |
                               (static_cast<std::uint32_t>(static_cast<unsigned char>(data[1])) << 8);
    if (length == 2) return word;
    return word | (static_cast<std::uint32_t>(static_cast<unsigned char>(data[2])) << 16);
}

struct FilterData {
    enum class Mode {
        None,        // 没有可用信息，逐字节试
        Literal,     // 必需的字面前缀，用字节查找定位
        Keys,        // 开头必须是这几串字节之一
        Needles,     // 这几串字节必然出现在命中里（开头可能先吃掉一串空白/环视）
        Lead,        // 只知道首字节的集合
        LineStarts,  // 模式以多行 ^ 开头，只看行首
        TextStart,   // 模式以 ^ 开头，只看全文开头
    };
    Mode mode = Mode::None;
    std::string prefix;
    std::vector<std::string> keys;
    std::array<std::uint8_t, 256> lead{};
    // Needles 用：命中串前面那段「模式开头能吃掉」的字节，1 表示可能是其中的一格。
    // 候选起点只能在命中串前面这串字节里，所以退到头就停。
    std::array<std::uint8_t, 256> gap{};
    // Needles 的另一条路：前面那一段的字节种类列不出来（取反类、点号、带反向引用的分组），
    // 但能算出它最少与最多吃几个字节。这时候候选起点落在命中串前面 [min_gap, max_gap] 字节里。
    bool gap_by_bytes = false;
    std::size_t min_gap = 0;
    std::size_t max_gap = 0;
    // 模式以「同一种字符连成一串」的量词开头时，串里后面那些起点不可能命中，
    // 试完串首那一次就直接跳过整串。判据见 build_filter 里的注释。
    // keys 的两字节索引：一个候选位置上先按前两个字节把「可能是哪几串」缩到几位，
    // 省下往上百个候选串挨个比。first_only[字节] 是长度只有一字节的那些串。
    std::array<std::uint64_t, 256> keys_by_first{};
    std::vector<std::pair<std::uint16_t, std::uint64_t>> keys_by_pair;
    // 前缀核对表：逐字节扫的时候，先花一次查表把「开头对不上任何候选串」的位置整片排掉。
    // 按候选串最短的那一串定长度（最多三字节）；只装「可能有候选串」的前缀，
    // 撞表的方向永远是多留候选，不会漏。没有可用的长度（有单字节候选串）时长度为 0。
    std::array<std::uint8_t, kPrefixSlots> prefix_slot{};
    std::size_t prefix_length = 0;
    // 快扫用的两串候选字节：首字节、次字节各一列（次字节只取所有候选串第二字节的并集，
    // 比精确的「串对」宽，宽的方向是多留候选）。列数不多才摊得开比较指令。
    std::array<unsigned char, kMaxSkipLanes> lead_lanes{};
    std::array<unsigned char, kMaxSkipLanes> second_lanes{};
    std::size_t lead_lane_count = 0;
    std::size_t second_lane_count = 0;  // 0 表示不按次字节筛
    bool skip_run = false;
    int run_atom = -1;
    // lead 只装了「按正常 UTF-8 解出来的首字节」时打开：扫描循环还要就地把
    // 「坏字节落单」那一类起点接住，见 malformed_cp_start。
    bool lone_check = false;
};

inline bool is_simple_kind(Kind kind) {
    return kind == Kind::Class || kind == Kind::Dot || kind == Kind::ByteLit ||
           kind == Kind::Backref;
}

struct Program {
    std::vector<Node> arena;
    int root = -1;
    int ncaps = 0;
    std::string text;
    FilterData filter;
    // 整条模式可以什么都不吃就命中。这种模式的命中起点不止「首集那几串」的起点
    // （空命中在每一个字符边界上都成立），起点候选那套快路对它不成立。
    bool nullable = false;
};

// ---------------------------------------------------------------- 编译期

class Compiler {
public:
    explicit Compiler(std::string_view pattern) : text_(pattern), view_(pattern) {}

    Program run() {
        Program program;
        program.text = std::string(text_);
        const int root = parse_alt();
        if (pos_ < view_.size()) fail("多出来的字符");
        program.root = root;
        program.ncaps = ncaps_;
        program.filter = build_filter(root);
        program.nullable = nullable(root);
        mark_alt_first_bytes();
        program.arena = std::move(arena_);
        return program;
    }

private:
    [[noreturn]] void fail(const std::string& why) const {
        throw std::runtime_error("规则里的正则写不对（" + std::string(text_) + "）：" + why +
                                 "，位置 " + std::to_string(pos_));
    }

    int add(Node node) {
        arena_.push_back(std::move(node));
        return static_cast<int>(arena_.size()) - 1;
    }

    int make_concat(std::vector<int> kids) {
        if (kids.empty()) return add(Node{});
        if (kids.size() == 1) return kids[0];
        Node node;
        node.kind = Kind::Concat;
        node.kids = std::move(kids);
        return add(std::move(node));
    }

    int add_literal(uint32_t cp) {
        Node node;
        node.kind = Kind::ByteLit;
        encode_utf8(cp, node.lit);
        if (icase_ && node.lit.size() == 1) {
            char& c = node.lit[0];
            if (c >= 'A' && c <= 'Z') c = static_cast<char>(c - 'A' + 'a');
            node.icase = true;
        } else if (icase_) {
            node.icase = true;
        }
        return add(std::move(node));
    }

    int add_assert(AssertKind kind) {
        Node node;
        node.kind = Kind::Assert;
        node.assert_kind = kind;
        return add(std::move(node));
    }

    bool eat(char c) {
        if (pos_ < view_.size() && view_[pos_] == c) {
            ++pos_;
            return true;
        }
        return false;
    }

    char peek() const { return pos_ < view_.size() ? view_[pos_] : '\0'; }

    int parse_alt() {
        std::vector<int> branches;
        branches.push_back(parse_seq());
        while (eat('|')) branches.push_back(parse_seq());
        if (branches.size() == 1) return branches.front();
        Node node;
        node.kind = Kind::Alt;
        node.kids = std::move(branches);
        return add(std::move(node));
    }

    // 一段顺序结构。(?i)(?m) 这类开关的作用范围到所在分组结束，所以进出这里各存一份状态。
    int parse_seq() {
        const bool saved_icase = icase_;
        const bool saved_multiline = multiline_;
        const bool saved_dotall = dotall_;
        std::vector<int> elements;
        while (pos_ < view_.size()) {
            const char c = view_[pos_];
            if (c == '|' || c == ')') break;
            const int atom = parse_atom();
            if (atom < 0) continue;  // 只改了开关，没有产出节点
            elements.push_back(parse_quantifier(atom));
        }
        icase_ = saved_icase;
        multiline_ = saved_multiline;
        dotall_ = saved_dotall;
        return make_concat(std::move(elements));
    }

    int parse_quantifier(int atom) {
        if (pos_ >= view_.size()) return atom;
        int min = 0;
        int max = -1;
        const char c = view_[pos_];
        if (c == '*') {
            ++pos_;
        } else if (c == '+') {
            ++pos_;
            min = 1;
        } else if (c == '?') {
            ++pos_;
            max = 1;
        } else if (c == '{') {
            const std::size_t start = pos_;
            ++pos_;
            int low = read_number();
            int high = low;
            if (eat(',')) {
                high = (pos_ < view_.size() && view_[pos_] == '}') ? -1 : read_number();
            }
            if (!eat('}')) {
                pos_ = start;
                fail("量词 { } 写不完整");
            }
            min = low;
            max = high;
            if (max >= 0 && max < min) fail("量词下界比上界大");
        } else {
            return atom;
        }
        bool greedy = true;
        if (eat('?')) greedy = false;
        Node node;
        node.kind = Kind::Repeat;
        node.min = min;
        node.max = max;
        node.greedy = greedy;
        node.kids.push_back(atom);
        return add(std::move(node));
    }

    int read_number() {
        int value = 0;
        std::size_t digits = 0;
        while (pos_ < view_.size() && view_[pos_] >= '0' && view_[pos_] <= '9') {
            value = value * 10 + (view_[pos_] - '0');
            ++pos_;
            ++digits;
            if (digits > 6) fail("量词里的数字太大");
        }
        if (digits == 0) fail("量词里缺数字");
        return value;
    }

    int parse_atom() {
        const char c = view_[pos_];
        switch (c) {
            case '(':
                return parse_group();
            case '[':
                return parse_class_node();
            case '.':
                ++pos_;
                {
                    Node node;
                    node.kind = Kind::Dot;
                    node.dot_all = dotall_;
                    return add(std::move(node));
                }
            case '^':
                ++pos_;
                return add_assert(multiline_ ? AssertKind::BeginLine : AssertKind::BeginText);
            case '$':
                ++pos_;
                return add_assert(multiline_ ? AssertKind::EndLine : AssertKind::EndText);
            case '\\':
                return parse_escape();
            case '*':
            case '+':
            case '?':
                fail("量词前面没有东西");
            case '{':
                fail("量词前面没有东西");
            case '}':
            case ']':
                fail("多出来的右括号");
            default:
                return parse_literal_run();
        }
    }

    int parse_literal_run() {
        const std::size_t start = pos_;
        while (pos_ < view_.size()) {
            const char c = view_[pos_];
            if (c == '\\' || c == '^' || c == '$' || c == '.' || c == '[' || c == ']' || c == '(' ||
                c == ')' || c == '{' || c == '}' || c == '*' || c == '+' || c == '?' || c == '|') {
                break;
            }
            ++pos_;
        }
        std::size_t stop = pos_;
        // 「ab*」里的 b 才是量词作用的对象：最后一个字符要让出去单独成原子。
        if (pos_ < view_.size() && (view_[pos_] == '*' || view_[pos_] == '+' || view_[pos_] == '?' ||
                                    view_[pos_] == '{')) {
            std::size_t last = stop - 1;
            while (last > start && (static_cast<unsigned char>(view_[last]) & 0xC0) == 0x80) --last;
            if (last > start) stop = last;
        }
        pos_ = stop;
        if (stop == start || stop > view_.size()) fail("这里不该出现这个字符");
        Node node;
        node.kind = Kind::ByteLit;
        node.icase = icase_;
        node.lit = std::string(view_.substr(start, stop - start));
        if (icase_) {
            for (char& ch : node.lit) {
                if (ch >= 'A' && ch <= 'Z') ch = static_cast<char>(ch - 'A' + 'a');
            }
        }
        return add(std::move(node));
    }

    int parse_group() {
        ++pos_;  // '('
        if (!eat('?')) {
            const int slot = ++ncaps_;
            Node open;
            open.kind = Kind::SaveStart;
            open.cap = slot;
            const int open_id = add(std::move(open));
            const int inner = parse_alt();
            if (!eat(')')) fail("分组没有收尾的 )");
            Node close;
            close.kind = Kind::SaveEnd;
            close.cap = slot;
            const int close_id = add(std::move(close));
            return make_concat({open_id, inner, close_id});
        }

        const char kind = peek();
        if (kind == ':') {
            ++pos_;
            const int inner = parse_alt();
            if (!eat(')')) fail("分组没有收尾的 )");
            return inner;
        }
        if (kind == '=' || kind == '!') {
            ++pos_;
            const int inner = parse_alt();
            if (!eat(')')) fail("环视没有收尾的 )");
            const int id = add_assert(kind == '=' ? AssertKind::LookAhead : AssertKind::NegLookAhead);
            arena_[id].kids.push_back(inner);
            return id;
        }
        if (kind == '<') {
            ++pos_;
            const char next = peek();
            if (next == '=' || next == '!') {
                ++pos_;
                const int inner = parse_alt();
                if (!eat(')')) fail("环视没有收尾的 )");
                const int id = add_assert(next == '='
                                              ? AssertKind::LookBehind
                                              : AssertKind::NegLookBehind);
                arena_[id].kids.push_back(inner);
                arena_[id].window = lookbehind_window(inner, 0);
                return id;
            }
            // 具名分组按普通捕获处理，名字不影响匹配结果。
            while (pos_ < view_.size() && view_[pos_] != '>') ++pos_;
            if (!eat('>')) fail("具名分组少了 >");
            const int slot = ++ncaps_;
            Node open;
            open.kind = Kind::SaveStart;
            open.cap = slot;
            const int open_id = add(std::move(open));
            const int inner = parse_alt();
            if (!eat(')')) fail("分组没有收尾的 )");
            Node close;
            close.kind = Kind::SaveEnd;
            close.cap = slot;
            const int close_id = add(std::move(close));
            return make_concat({open_id, inner, close_id});
        }
        if (kind == '>') fail("不支持原子分组");

        // 内联开关：(?i) (?m) (?s) (?im) 以及限定范围到分组内的 (?i:...)
        std::string flags;
        while (pos_ < view_.size() && view_[pos_] != ':' && view_[pos_] != ')') {
            flags.push_back(view_[pos_]);
            ++pos_;
        }
        bool scoped = false;
        if (eat(':')) {
            scoped = true;
        } else if (!eat(')')) {
            fail("内联开关没有收尾");
        }
        const bool saved_icase = icase_;
        const bool saved_multiline = multiline_;
        const bool saved_dotall = dotall_;
        apply_flags(flags);
        int inner = -1;
        if (scoped) {
            inner = parse_alt();
            if (!eat(')')) fail("分组没有收尾的 )");
            icase_ = saved_icase;
            multiline_ = saved_multiline;
            dotall_ = saved_dotall;
        }
        return inner;  // 非限定时返回 -1，开关对后续元素继续生效
    }

    void apply_flags(const std::string& flags) {
        bool turn_on = true;
        for (const char c : flags) {
            if (c == '-') {
                turn_on = false;
                continue;
            }
            switch (c) {
                case 'i':
                    icase_ = turn_on;
                    break;
                case 'm':
                    multiline_ = turn_on;
                    break;
                case 's':
                    dotall_ = turn_on;
                    break;
                default:
                    fail(std::string("不认识的开关 ") + c);
            }
        }
    }

    // 环视往回最多探多少字节：按子模式最多能吃几个字符算，宽字符按 4 字节留足。
    int lookbehind_window(int id, int depth) const {
        if (depth > 32) return 64;
        const Node& node = arena_[id];
        switch (node.kind) {
            case Kind::Empty:
            case Kind::SaveStart:
            case Kind::SaveEnd:
            case Kind::Assert:
                return 0;
            case Kind::ByteLit: {
                int chars = 0;
                for (char c : node.lit) {
                    if ((static_cast<unsigned char>(c) & 0xC0) != 0x80) ++chars;
                }
                return chars;
            }
            case Kind::Class:
            case Kind::Dot:
                return 1;
            case Kind::Backref:
                return 32;
            case Kind::Concat: {
                int sum = 0;
                for (const int kid : node.kids) sum += lookbehind_window(kid, depth + 1);
                return std::min(sum, 64);
            }
            case Kind::Alt: {
                int best = 0;
                for (const int kid : node.kids) best = std::max(best, lookbehind_window(kid, depth + 1));
                return std::min(best, 64);
            }
            case Kind::Repeat: {
                const int atom = lookbehind_window(node.kids[0], depth + 1);
                if (node.max < 0) return 64;
                return std::min(atom * node.max, 64);
            }
        }
        return 64;
    }

    int parse_class_node(bool keep_surrogates = false) {
        ++pos_;  // '['
        ClassSet set;
        if (eat('^')) set.negated = true;
        bool first = true;
        while (pos_ < view_.size() && (first || view_[pos_] != ']')) {
            first = false;
            // 类里的 \d \w \s 直接并进来；其余转义当码点处理。
            if (view_[pos_] == '\\') {
                ++pos_;
                if (pos_ >= view_.size()) fail("字符类里转义没写完");
                const char escaped = view_[pos_];
                const ClassSet* preset = preset_class(escaped);
                if (preset != nullptr) {
                    ++pos_;
                    for (const auto& range : preset->ranges) set.add(range.first, range.second);
                    continue;
                }
                uint32_t cp = 0;
                if (!read_escape_cp(escaped, cp)) fail("字符类里不支持这个转义");
                maybe_add_range(set, cp, keep_surrogates);
                continue;
            }
            const Decoded decoded = decode_utf8(view_.data(), view_.size(), pos_);
            pos_ += decoded.len;
            maybe_add_range(set, decoded.cp, keep_surrogates);
        }
        if (pos_ >= view_.size()) fail("字符类没有收尾的 ]");
        ++pos_;  // ']'
        set.normalize();
        if (icase_) add_case_folded(set);
        Node node;
        node.kind = Kind::Class;
        node.cls = std::move(set);
        return add(std::move(node));
    }

    // 读一个码点，若紧接着是区间写法（- 后还有内容），就地组成区间。
    void maybe_add_range(ClassSet& set, uint32_t cp, bool keep_surrogates) {
        if (pos_ < view_.size() && view_[pos_] == '-' && pos_ + 1 < view_.size() &&
            view_[pos_ + 1] != ']') {
            ++pos_;  // '-'
            uint32_t high = 0;
            if (view_[pos_] == '\\') {
                ++pos_;
                if (pos_ >= view_.size()) fail("区间写法没写完");
                const char escaped = view_[pos_];
                if (!read_escape_cp(escaped, high)) fail("字符类里不支持这个转义");
            } else {
                const Decoded decoded = decode_utf8(view_.data(), view_.size(), pos_);
                pos_ += decoded.len;
                high = decoded.cp;
            }
            if (keep_surrogates) {
                set.add(cp, high);
            } else {
                add_range_skipping_surrogates(set, cp, high);
            }
            return;
        }
        if (keep_surrogates) {
            set.add(cp, cp);
        } else {
            add_range_skipping_surrogates(set, cp, cp);
        }
    }

    // .NET 在 UTF-16 上跑，代理区（D800-DFFF）在 UTF-8 里对应的是增补平面的码点。
    // 这里把代理区间的写法翻译成码点区间，让「排除整个代理区」这类写法还是「排除增补平面」。
    void add_range_skipping_surrogates(ClassSet& set, uint32_t lo, uint32_t hi) {
        if (hi < 0xD800 || lo > 0xDFFF) {
            set.add(lo, hi);
            return;
        }
        if (lo < 0xD800) set.add(lo, 0xD7FF);
        if (hi > 0xDFFF) set.add(0xE000, hi);
        const uint32_t high_lo = std::max<uint32_t>(lo, 0xD800);
        const uint32_t high_hi = std::min<uint32_t>(hi, 0xDBFF);
        if (high_lo <= high_hi) {
            // 单个高位代理 + 完整低位代理区 = 一段增补平面码点
            for (uint32_t surrogate = high_lo; surrogate <= high_hi; ++surrogate) {
                const uint32_t base = 0x10000 + ((surrogate - 0xD800) << 10);
                set.add(base + 0xDC00 - 0xDC00, base + 0x3FF);
            }
        }
        // 单独的低位代理在合法 UTF-8 里不存在，落下来就是永远不会命中。
    }

    const ClassSet* preset_class(char escaped) {
        static const ClassSet digit = make_digit_class();
        static const ClassSet space = make_space_class();
        static const ClassSet word = make_word_class();
        static const ClassSet not_digit = [] {
            ClassSet set = make_digit_class();
            set.negated = true;
            return set;
        }();
        static const ClassSet not_space = [] {
            ClassSet set = make_space_class();
            set.negated = true;
            return set;
        }();
        static const ClassSet not_word = [] {
            ClassSet set = make_word_class();
            set.negated = true;
            return set;
        }();
        switch (escaped) {
            case 'd':
                return &digit;
            case 'D':
                return &not_digit;
            case 's':
                return &space;
            case 'S':
                return &not_space;
            case 'w':
                return &word;
            case 'W':
                return &not_word;
            default:
                return nullptr;
        }
    }

    // 读 \uXXXX \xHH \n \r \t 等，返回码点。escaped 是反斜杠后面的第一个字符。
    bool read_escape_cp(char escaped, uint32_t& cp) {
        ++pos_;  // 吃掉 escaped 字符
        switch (escaped) {
            case 'u': {
                uint32_t value = 0;
                for (int i = 0; i < 4; ++i) {
                    if (pos_ >= view_.size()) fail("\\u 后面要跟四位十六进制");
                    const int digit = hex_digit(view_[pos_]);
                    if (digit < 0) fail("\\u 后面要跟四位十六进制");
                    value = value * 16 + static_cast<uint32_t>(digit);
                    ++pos_;
                }
                cp = value;
                return true;
            }
            case 'x': {
                uint32_t value = 0;
                int digits = 0;
                while (digits < 4 && pos_ < view_.size() && hex_digit(view_[pos_]) >= 0) {
                    value = value * 16 + static_cast<uint32_t>(hex_digit(view_[pos_]));
                    ++pos_;
                    ++digits;
                }
                if (digits == 0) fail("\\x 后面要跟十六进制");
                cp = value;
                return true;
            }
            case 'n':
                cp = '\n';
                return true;
            case 'r':
                cp = '\r';
                return true;
            case 't':
                cp = '\t';
                return true;
            case 'f':
                cp = '\f';
                return true;
            case 'v':
                cp = '\v';
                return true;
            case '0':
                cp = 0;
                return true;
            default:
                break;
        }
        // 标点转义：\. \* \( 之类，一律按字面量处理。
        if (!((escaped >= 'a' && escaped <= 'z') || (escaped >= 'A' && escaped <= 'Z') ||
              (escaped >= '0' && escaped <= '9'))) {
            cp = static_cast<unsigned char>(escaped);
            return true;
        }
        return false;
    }

    static int hex_digit(char c) {
        if (c >= '0' && c <= '9') return c - '0';
        if (c >= 'a' && c <= 'f') return c - 'a' + 10;
        if (c >= 'A' && c <= 'F') return c - 'A' + 10;
        return -1;
    }

    int parse_escape() {
        ++pos_;  // '\\'
        if (pos_ >= view_.size()) fail("反斜杠后面什么都没有");
        const char escaped = view_[pos_];

        if (escaped == 'b') {
            ++pos_;
            return add_assert(AssertKind::WordBoundary);
        }
        if (escaped == 'B') {
            ++pos_;
            return add_assert(AssertKind::NotWordBoundary);
        }
        if (escaped >= '1' && escaped <= '9') {
            ++pos_;
            Node node;
            node.kind = Kind::Backref;
            node.ref = escaped - '0';
            node.icase = icase_;
            return add(std::move(node));
        }

        const ClassSet* preset = preset_class(escaped);
        if (preset != nullptr) {
            ++pos_;
            ClassSet set = *preset;
            if (icase_) add_case_folded(set);
            Node node;
            node.kind = Kind::Class;
            node.cls = std::move(set);
            return add(std::move(node));
        }

        uint32_t cp = 0;
        const std::size_t save = pos_;
        if (!read_escape_cp(escaped, cp)) {
            pos_ = save;
            fail("不认识的转义");
        }

        // 半个代理项：\uD83C[\uDF00-\uDFFF] 这种写法在 .NET 里是在认一个完整的表情，
        // 到了字节层要合成为一个增补平面码点区间。
        if (cp >= 0xD800 && cp <= 0xDBFF && pos_ < view_.size()) {
            if (view_[pos_] == '[') {
                const int class_id = parse_class_node(true);  // 先按原始码点读，留住代理区
                const Node& cls = arena_[class_id];
                if (!cls.cls.negated) {
                    bool all_low = !cls.cls.ranges.empty();
                    for (const auto& range : cls.cls.ranges) {
                        if (range.first < 0xDC00 || range.second > 0xDFFF) all_low = false;
                    }
                    if (all_low) {
                        ClassSet merged;
                        const uint32_t base = 0x10000 + ((cp - 0xD800) << 10);
                        for (const auto& range : cls.cls.ranges) {
                            merged.add(base + (range.first - 0xDC00), base + (range.second - 0xDC00));
                        }
                        merged.normalize();
                        Node node;
                        node.kind = Kind::Class;
                        node.cls = std::move(merged);
                        return add(std::move(node));
                    }
                }
                // 合不了：把原始区间按代理区规则翻译成码点区间再交出
                ClassSet translated = cls.cls;
                ClassSet fixed;
                fixed.negated = translated.negated;
                for (const auto& range : translated.ranges) {
                    add_range_skipping_surrogates(fixed, range.first, range.second);
                }
                fixed.normalize();
                Node node;
                node.kind = Kind::Class;
                node.cls = std::move(fixed);
                return add(std::move(node));
            }
            if (view_[pos_] == '\\' && pos_ + 1 < view_.size() && view_[pos_ + 1] == 'u') {
                pos_ += 2;
                uint32_t low = 0;
                for (int i = 0; i < 4; ++i) {
                    if (pos_ >= view_.size() || hex_digit(view_[pos_]) < 0) fail("\\u 后面要跟四位十六进制");
                    low = low * 16 + static_cast<uint32_t>(hex_digit(view_[pos_]));
                    ++pos_;
                }
                if (low >= 0xDC00 && low <= 0xDFFF) {
                    return add_literal(0x10000 + ((cp - 0xD800) << 10) + (low - 0xDC00));
                }
                fail("代理项配对写错了");
            }
        }
        if (cp >= 0xD800 && cp <= 0xDFFF) {
            // 落单的代理项在合法 UTF-8 里不存在，永远不会命中。
            Node node;
            node.kind = Kind::Class;
            node.cls.negated = false;
            return add(std::move(node));
        }
        return add_literal(cp);
    }

    // ------------------------------------------------ 预筛信息的计算

    bool nullable(int id) const {
        const Node& node = arena_[id];
        switch (node.kind) {
            case Kind::Empty:
            case Kind::SaveStart:
            case Kind::SaveEnd:
            case Kind::Assert:
                return true;
            case Kind::ByteLit:
            case Kind::Class:
            case Kind::Dot:
                return false;
            case Kind::Backref:
                return true;
            case Kind::Concat: {
                for (const int kid : node.kids) {
                    if (!nullable(kid)) return false;
                }
                return true;
            }
            case Kind::Alt: {
                for (const int kid : node.kids) {
                    if (nullable(kid)) return true;
                }
                return false;
            }
            case Kind::Repeat:
                return node.min == 0 || nullable(node.kids[0]);
        }
        return true;
    }

    struct FirstSet {
        std::vector<std::string> keys;
        bool complete = true;  // keys 是否穷尽了所有可能的开头
        bool broad = false;    // 至少能给出首字节集合
        bool nullable = false;
    };

    void add_key(FirstSet& set, std::string key) const {
        if (!set.complete) return;
        if (set.keys.size() >= 64) {
            set.complete = false;
            return;
        }
        for (const std::string& existing : set.keys) {
            if (existing == key) return;
        }
        set.keys.push_back(std::move(key));
    }

    void node_first(int id, FirstSet& set) const {
        const Node& node = arena_[id];
        switch (node.kind) {
            case Kind::ByteLit: {
                if (node.icase) {
                    // 大小写不敏感时不能整串比，只按首字节收窄。
                    if (node.lit.empty()) {
                        set.complete = false;
                        return;
                    }
                    char first = node.lit[0];
                    add_key(set, std::string(1, first));
                    if (first >= 'a' && first <= 'z') add_key(set, std::string(1, static_cast<char>(first - 32)));
                    if (first >= 'A' && first <= 'Z') add_key(set, std::string(1, static_cast<char>(first + 32)));
                    set.complete = false;
                    return;
                }
                std::string key = node.lit.substr(0, std::min<std::size_t>(4, node.lit.size()));
                add_key(set, std::move(key));
                return;
            }
            case Kind::Class: {
                if (node.cls.negated) {
                    set.broad = true;
                    set.complete = false;
                    return;
                }
                std::size_t budget = 0;
                for (const auto& range : node.cls.ranges) budget += range.second - range.first + 1;
                if (budget > 48) {
                    set.broad = true;
                    set.complete = false;
                    return;
                }
                for (const auto& range : node.cls.ranges) {
                    for (uint32_t cp = range.first; cp <= range.second; ++cp) {
                        std::string key;
                        encode_utf8(cp, key);
                        add_key(set, std::move(key));
                    }
                }
                return;
            }
            case Kind::Dot:
                set.broad = true;
                set.complete = false;
                return;
            case Kind::Backref:
                set.broad = true;
                set.complete = false;
                return;
            case Kind::Concat:
                collect_first(node.kids, set);
                return;
            case Kind::Alt:
                for (const int kid : node.kids) {
                    FirstSet branch;
                    node_first(kid, branch);
                    set.keys.insert(set.keys.end(), branch.keys.begin(), branch.keys.end());
                    set.complete = set.complete && branch.complete;
                    set.broad = set.broad || branch.broad;
                }
                return;
            case Kind::Repeat: {
                FirstSet inner;
                node_first(node.kids[0], inner);
                set.keys.insert(set.keys.end(), inner.keys.begin(), inner.keys.end());
                set.complete = set.complete && inner.complete;
                set.broad = set.broad || inner.broad;
                return;
            }
            default:
                // 锚点、环视、空节点都不吃字符，对「开头是什么」没有贡献；
                // 把它们当成「说不准」会让好几条规则丢掉可用的前缀，白白逐字节扫。
                return;
        }
    }

    void collect_first(const std::vector<int>& sequence, FirstSet& set) const {
        for (const int kid : sequence) {
            const Node& node = arena_[kid];
            if (node.kind == Kind::Assert || node.kind == Kind::SaveStart ||
                node.kind == Kind::SaveEnd || nullable(kid)) {
                if (!nullable(kid)) continue;
                // 这一节可能是空的：它自己的开头与后面那一节的开头都算候选。
                if (node.kind == Kind::Repeat || node.kind == Kind::Class ||
                    node.kind == Kind::ByteLit || node.kind == Kind::Dot ||
                    node.kind == Kind::Alt) {
                    FirstSet own;
                    node_first(kid, own);
                    set.keys.insert(set.keys.end(), own.keys.begin(), own.keys.end());
                    set.broad = set.broad || own.broad;
                    if (!own.complete) set.complete = false;
                }
                continue;
            }
            node_first(kid, set);
            return;
        }
        // 整段都能是空的
        set.nullable = true;
    }

    // 类里有没有落在码点区间 [lo, hi] 里的成员（已考虑取反）。只看第一字节时要问的
    // 就是这一句：这个区间里有一个码点能命中，这个首字节就放开。
    static bool class_hits_interval(const ClassSet& cls, uint32_t lo, uint32_t hi) {
        if (!cls.negated) {
            for (const auto& range : cls.ranges) {
                if (range.second >= lo && range.first <= hi) return true;
                if (range.first > hi) break;  // 区段已排序，后面的都在 hi 之后
            }
            return false;
        }
        // 取反：区间里只要剩一个没被区段盖住的码点就算命中。逐段把区间剪掉。
        uint32_t cursor = lo;
        for (const auto& range : cls.ranges) {
            if (range.second < cursor) continue;
            if (range.first > hi) break;
            if (range.first > cursor) return true;  // [cursor, range.first - 1] 是空隙
            cursor = range.second + 1;              // 码点不超过 0x10FFFF，加一不会溢出
            if (cursor > hi) return false;
        }
        return cursor <= hi;
    }

    // 一个首字节可能解成哪些码点：按 UTF-8 的字节宽度给出区间，再加上「坏字节算一格」
    // 的那条路（解码器把落单的字节当成码点本身）。separate 返回 false 表示这个字节
    // 只有「码点就是字节本身」这一种解法。
    static bool lead_cp_interval(uint32_t byte, uint32_t& lo, uint32_t& hi) {
        if (byte >= 0xC0 && byte <= 0xDF) {
            lo = (byte & 0x1FU) << 6;
            hi = lo + 0x3F;
            return true;
        }
        if (byte >= 0xE0 && byte <= 0xEF) {
            lo = (byte & 0x0FU) << 12;
            hi = lo + 0xFFF;
            return true;
        }
        if (byte >= 0xF0 && byte <= 0xF7) {
            lo = (byte & 0x07U) << 18;
            hi = lo + 0x3FFFF;
            return true;
        }
        lo = byte;
        hi = byte;
        return false;
    }

    // allow_lone: 算不算「坏字节落单」那一条（非 ASCII 字节后面不是合法续字节时，
    // 解码器把这一格当成一个独立码点）。精确性要求算上它，但它会把取反类的首字节表
    // 撑到几乎全部字节；所以两套表都算出来，宽的那套用于快扫列，窄的那套配上运行时
    // 就地判落单（见 malformed_cp_start），两条路都不漏命中。
    void collect_lead_bytes(int id, std::array<std::uint8_t, 256>& lead, bool allow_lone) const {
        const Node& node = arena_[id];
        switch (node.kind) {
            case Kind::ByteLit: {
                if (node.lit.empty()) return;
                const auto first = static_cast<unsigned char>(node.lit[0]);
                lead[first] = 1;
                if (node.icase) {
                    if (first >= 'a' && first <= 'z') {
                        lead[first - 32] = 1;
                    } else if (first >= 'A' && first <= 'Z') {
                        lead[first + 32] = 1;
                    }
                }
                return;
            }
            case Kind::Class: {
                // 按首字节精确算，不再逐码点枚举：枚举的代价让取反类与超大区间的类只能
                // 一律全放开（等于不筛），而 CJK 稿件上一个不筛的类等于让整条规则逐格里
                // 跑一遍完整匹配。逐字节问「这个区间里有没有成员」是常数代价，两类都能算。
                for (uint32_t byte = 0; byte < 256; ++byte) {
                    // 码点就是字节本身的两条路：ASCII 一定如此，非 ASCII 只在这个字节落单
                    // （后面不是合法的续字节）时如此。取反类两条都要算——它改动前是全放开，
                    // 现在收紧就得算准；正类只算 ASCII 这一条，与改动前一致，也免得把
                    // 0xF8..0xFF 这些落单字节塞进首字节表、把快扫列撑爆。
                    const bool lone_counted = byte < 0x80 || (allow_lone && node.cls.negated);
                    if (lone_counted && node.cls.matches(byte)) {
                        lead[byte] = 1;
                        continue;
                    }
                    uint32_t lo = 0;
                    uint32_t hi = 0;
                    if (lead_cp_interval(byte, lo, hi) &&
                        class_hits_interval(node.cls, lo, hi)) {
                        lead[byte] = 1;
                    }
                }
                return;
            }
            case Kind::Dot:
            case Kind::Backref:
                lead.fill(1);
                return;
            case Kind::Concat:
                for (const int kid : node.kids) {
                    collect_lead_bytes(kid, lead, allow_lone);
                    if (!nullable(kid)) return;
                }
                return;
            case Kind::Alt:
                for (const int kid : node.kids) collect_lead_bytes(kid, lead, allow_lone);
                return;
            case Kind::Repeat:
                collect_lead_bytes(node.kids[0], lead, allow_lone);
                return;
            default:
                return;
        }
    }

    // ------------------------------------------------ 预筛：必须出现的字节串

    // 一个节点可能吃掉的字节集合（超集）。return false 表示说不准，拿它当预筛会漏命中。
    // 只看「一个字符起步」的原子与它们的量词：字符类、点号、字面量、重复、分支、串接。
    bool gap_bytes(int id, std::array<std::uint8_t, 256>& gap) const {
        const Node& node = arena_[id];
        switch (node.kind) {
            case Kind::Empty:
            case Kind::SaveStart:
            case Kind::SaveEnd:
            case Kind::Assert:
                return true;  // 不吃字节
            case Kind::ByteLit:
                for (char c : node.lit) gap[static_cast<unsigned char>(c)] = 1;
                return true;
            case Kind::Class: {
                if (node.cls.negated) return false;
                std::size_t budget = 0;
                for (const auto& range : node.cls.ranges) budget += range.second - range.first + 1;
                if (budget > kGapClassBudget) return false;
                for (const auto& range : node.cls.ranges) {
                    for (uint32_t cp = range.first; cp <= range.second; ++cp) {
                        std::string bytes;
                        encode_utf8(cp, bytes);
                        // 一个汉字占三个字节，三个字节都要算进去：反向退的时候是按字节退的
                        for (char c : bytes) gap[static_cast<unsigned char>(c)] = 1;
                    }
                }
                return true;
            }
            case Kind::Repeat:
                return gap_bytes(node.kids.front(), gap);
            case Kind::Concat:
                for (const int kid : node.kids)
                    if (!gap_bytes(kid, gap)) return false;
                return true;
            case Kind::Alt:
                for (const int kid : node.kids)
                    if (!gap_bytes(kid, gap)) return false;
                return true;
            default:
                return false;
        }
    }

    // 一个节点吃掉多少字节：下界与上界。一个 UTF-8 字符最多四字节，字符类与点号按它算上界。
    // 上界说不准（反向引用、无上限的量词）返回 false，调用方就不按字节宽度定位。
    bool byte_bounds(int id, std::size_t& minimum, std::size_t& maximum) const {
        const Node& node = arena_[id];
        switch (node.kind) {
            case Kind::Empty:
            case Kind::SaveStart:
            case Kind::SaveEnd:
            case Kind::Assert:
                minimum = 0;
                maximum = 0;
                return true;
            case Kind::ByteLit:
                // 大小写不敏感改的是内容，不改变字节数
                minimum = node.lit.size();
                maximum = node.lit.size();
                return true;
            case Kind::Class:
            case Kind::Dot:
                minimum = 1;
                maximum = 4;
                return true;
            case Kind::Backref:
                return false;
            case Kind::Concat: {
                std::size_t low = 0;
                std::size_t high = 0;
                for (const int kid : node.kids) {
                    std::size_t kid_low = 0;
                    std::size_t kid_high = 0;
                    if (!byte_bounds(kid, kid_low, kid_high)) return false;
                    low += kid_low;
                    high += kid_high;
                    if (high > kMaxGapWidth) return false;
                }
                minimum = low;
                maximum = high;
                return true;
            }
            case Kind::Alt: {
                std::size_t low = kUnset;
                std::size_t high = 0;
                for (const int kid : node.kids) {
                    std::size_t kid_low = 0;
                    std::size_t kid_high = 0;
                    if (!byte_bounds(kid, kid_low, kid_high)) return false;
                    if (kid_low < low) low = kid_low;
                    if (kid_high > high) high = kid_high;
                }
                if (low == kUnset) low = 0;
                if (high > kMaxGapWidth) return false;
                minimum = low;
                maximum = high;
                return true;
            }
            case Kind::Repeat: {
                std::size_t kid_low = 0;
                std::size_t kid_high = 0;
                if (!byte_bounds(node.kids.front(), kid_low, kid_high)) return false;
                // 零宽的东西重复多少遍都不吃字节
                if (kid_high == 0) {
                    minimum = 0;
                    maximum = 0;
                    return true;
                }
                if (node.max < 0) return false;  // 无上限：吃多少说不准
                minimum = kid_low * static_cast<std::size_t>(node.min);
                maximum = kid_high * static_cast<std::size_t>(node.max);
                if (maximum > kMaxGapWidth) return false;
                return true;
            }
        }
        return false;
    }

    // 一个节点必须以哪几串字节开头。return false 表示说不准。
    // 只收字面量：大小写不敏感的字面量、反向引用、点号都不算。
    bool literal_prefixes(int id, std::vector<std::string>& out) const {
        const Node& node = arena_[id];
        switch (node.kind) {
            case Kind::ByteLit:
                if (node.icase || node.lit.empty()) return false;
                out.push_back(node.lit.substr(0, std::min<std::size_t>(kNeedleBytes, node.lit.size())));
                return true;
            case Kind::Concat: {
                std::string prefix;
                for (const int kid : node.kids) {
                    const Node& child = arena_[kid];
                    if (child.kind == Kind::Assert || child.kind == Kind::SaveStart ||
                        child.kind == Kind::SaveEnd || child.kind == Kind::Empty)
                        continue;
                    if (child.kind != Kind::ByteLit || child.icase || child.lit.empty()) break;
                    prefix += child.lit;
                    if (prefix.size() >= kNeedleBytes) break;
                }
                if (prefix.empty()) return false;
                out.push_back(prefix.substr(0, std::min<std::size_t>(kNeedleBytes, prefix.size())));
                return true;
            }
            case Kind::Alt: {
                const std::size_t from = out.size();
                for (const int kid : node.kids) {
                    if (!literal_prefixes(kid, out)) {
                        out.resize(from);
                        return false;
                    }
                    if (out.size() - from > kNeedleCount) {
                        out.resize(from);
                        return false;
                    }
                }
                return out.size() > from;
            }
            case Kind::Repeat: {
                // 一遍都不跑就不能保证这串字节出现，所以只认下界不小于 1 的量词
                if (node.min < 1) return false;
                // 同一个字面量重复 min 遍本身就是必含串：--+ 的必含串是「--」，
                // 只拿最后一节的「-」当候选串的话一个字节长，等于没筛。
                const Node& atom = arena_[node.kids.front()];
                if (atom.kind == Kind::ByteLit && !atom.icase && !atom.lit.empty() &&
                    static_cast<std::size_t>(node.min) * atom.lit.size() <= kNeedleBytes) {
                    std::string repeated;
                    repeated.reserve(static_cast<std::size_t>(node.min) * atom.lit.size());
                    for (int round = 0; round < node.min; ++round) repeated += atom.lit;
                    out.push_back(std::move(repeated));
                    return true;
                }
                return literal_prefixes(node.kids.front(), out);
            }
            default:
                return false;
        }
    }

    // 候选串的两字节索引：候选位置上先按前两个字节把「可能是哪几串」缩到几位。
    void fill_key_index(FilterData& filter) const {
        std::vector<std::pair<std::uint16_t, std::uint64_t>> pairs;
        for (std::size_t index = 0; index < filter.keys.size(); ++index) {
            const std::string& key = filter.keys[index];
            const auto bit = std::uint64_t{1} << index;
            const auto first_byte = static_cast<unsigned char>(key[0]);
            filter.lead[first_byte] = 1;
            if (key.size() == 1) {
                filter.keys_by_first[first_byte] |= bit;
            } else {
                const std::uint16_t pair = static_cast<std::uint16_t>(
                    (first_byte << 8) | static_cast<unsigned char>(key[1]));
                pairs.emplace_back(pair, bit);
            }
        }
        std::sort(pairs.begin(), pairs.end());
        filter.keys_by_pair.reserve(pairs.size());
        for (const auto& item : pairs) {
            if (!filter.keys_by_pair.empty() && filter.keys_by_pair.back().first == item.first) {
                filter.keys_by_pair.back().second |= item.second;
            } else {
                filter.keys_by_pair.push_back(item);
            }
        }

        // 前缀核对表：按候选串里最短的那一串动刀（最多三字节）。有一字节的候选串就不建这张表：
        // 那种串只看首字节，表按几字节都会漏（把它并进首字节表试过，实测更慢）。
        std::size_t shortest = 4;
        for (const std::string& key : filter.keys) shortest = std::min(shortest, key.size());
        if (filter.keys.empty() || shortest < 2) return;
        filter.prefix_length = std::min<std::size_t>(shortest, 3);
        for (const std::string& key : filter.keys) {
            const std::uint32_t word = prefix_word(key.c_str(), filter.prefix_length);
            filter.prefix_slot[prefix_slot_index(word)] = 1;
        }
    }

    // 快扫列：首字节取候选串首字节的并集；次字节取第二字节的并集，
    // 只有一字节的候选串不看第二字节，那时不按次字节筛。
    void fill_skip_lanes(FilterData& filter) const {
        std::array<std::uint8_t, 256> firsts{};
        std::array<std::uint8_t, 256> seconds{};
        bool any_single = false;
        std::size_t first_count = 0;
        std::size_t second_count = 0;
        for (const std::string& key : filter.keys) {
            const auto first = static_cast<unsigned char>(key[0]);
            if (firsts[first] == 0) {
                firsts[first] = 1;
                ++first_count;
            }
            if (key.size() == 1) {
                any_single = true;
                continue;
            }
            const auto second = static_cast<unsigned char>(key[1]);
            if (seconds[second] == 0) {
                seconds[second] = 1;
                ++second_count;
            }
        }
        if (first_count == 0 || first_count > kMaxSkipLanes) return;
        std::size_t at = 0;
        for (std::size_t byte = 0; byte < 256 && at < first_count; ++byte)
            if (firsts[byte] != 0) filter.lead_lanes[at++] = static_cast<unsigned char>(byte);
        filter.lead_lane_count = at;

        if (any_single || second_count == 0 || second_count > kMaxSkipLanes) return;
        at = 0;
        for (std::size_t byte = 0; byte < 256 && at < second_count; ++byte)
            if (seconds[byte] != 0) filter.second_lanes[at++] = static_cast<unsigned char>(byte);
        filter.second_lane_count = at;
    }

    // 只看首字节的模式：首字节集合摊成快扫列。集合太大就退回逐字节查表。
    void fill_lead_lanes(FilterData& filter) const {
        std::size_t count = 0;
        for (std::size_t byte = 0; byte < 256; ++byte) count += filter.lead[byte] != 0 ? 1 : 0;
        if (count == 0 || count > kMaxSkipLanes) return;
        std::size_t at = 0;
        for (std::size_t byte = 0; byte < 256 && at < count; ++byte)
            if (filter.lead[byte] != 0) filter.lead_lanes[at++] = static_cast<unsigned char>(byte);
        filter.lead_lane_count = at;
    }

    // 模式开头可能从哪些首字节起：数一数有多少种。种类少的时候首字节那一类预筛
    // 一次能跳过十六格，比按命中串一个个找便宜。
    std::size_t prefix_lead_count(const std::vector<int>& sequence) const {
        FilterData probe;
        for (const int kid : sequence) {
            const Node& node = arena_[kid];
            if (node.kind == Kind::Assert || node.kind == Kind::SaveStart ||
                node.kind == Kind::SaveEnd || node.kind == Kind::Empty)
                continue;
            collect_lead_bytes(kid, probe.lead, true);
            if (!nullable(kid)) break;
        }
        std::size_t count = 0;
        for (std::size_t i = 0; i < 256; ++i) count += probe.lead[i] != 0 ? 1 : 0;
        return count;
    }

    // 开头能吃掉的那一节里，第一个字符有多少个码点可能（超集估计，只用来判断「宽不宽」）。
    // 首字节种类数是按字节数算的：[\u4e00-\u9fff] 只有六个首字节，看着很窄，
    // 在一份汉字稿上却几乎每个字都算候选，每个候选都要跑一遍完整匹配。
    // 种类数窄而码点覆盖宽的，正是「按命中串定位」划算的那一类。
    std::size_t lead_cp_budget(int id) const {
        const Node& node = arena_[id];
        switch (node.kind) {
            case Kind::ByteLit:
                return node.icase && node.lit.size() == 1 ? 2 : 1;
            case Kind::Class: {
                if (node.cls.negated) return kLeadBroad;
                std::size_t total = 0;
                for (const auto& range : node.cls.ranges) {
                    total += range.second - range.first + 1;
                    if (total >= kLeadBroad) return kLeadBroad;
                }
                return total;
            }
            case Kind::Dot:
            case Kind::Backref:
                return kLeadBroad;
            case Kind::Concat: {
                std::size_t total = 0;
                for (const int kid : node.kids) {
                    total += lead_cp_budget(kid);
                    if (total >= kLeadBroad) return kLeadBroad;
                    if (!nullable(kid)) break;
                }
                return total;
            }
            case Kind::Alt: {
                std::size_t total = 0;
                for (const int kid : node.kids) {
                    total += lead_cp_budget(kid);
                    if (total >= kLeadBroad) return kLeadBroad;
                }
                return total;
            }
            case Kind::Repeat:
                return lead_cp_budget(node.kids.front());
            default:
                return 0;  // 环视、空节点、捕获标记都不吃字符
        }
    }

    std::size_t prefix_lead_budget(const std::vector<int>& sequence) const {
        std::size_t total = 0;
        for (const int kid : sequence) {
            const Node& node = arena_[kid];
            if (node.kind == Kind::Assert || node.kind == Kind::SaveStart ||
                node.kind == Kind::SaveEnd || node.kind == Kind::Empty)
                continue;
            total += lead_cp_budget(kid);
            if (total >= kLeadBroad) return kLeadBroad;
            if (!nullable(kid)) break;
        }
        return total;
    }

    // 开头的环视与空白口子不挡住字面定位。
    //
    // 模式最前面可能是零宽的环视、也可能是能吃掉一串字节的量词或点号：它们都不妨碍
    // 「命中里必然出现的那串字」先被找出来。候选起点只能在它前面那一段里，于是有两种走法：
    //   * 那一段吃掉的字节种类列得出来：运行时从命中串往回退，退到吃不下为止；
    //   * 列不出来但字节宽度算得出来（点号、取反类、带反向引用的分组）：运行时只在
    //     命中串前面 [min_gap, max_gap] 字节里找起点。
    // 拿不到必需字面、或者前面那一段吃多少说不清（反向引用、无上限的字符类量词）就不填：
    // 预筛只能缩候选位置，不能漏命中，说不准就不筛。
    bool build_needle_filter(const std::vector<int>& sequence, FilterData& filter) const {
        std::array<std::uint8_t, 256> gap{};
        std::size_t min_gap = 0;
        std::size_t max_gap = 0;
        // 改动前认作「命中串前面那段」的节点：可能不吃字节的，或者量词（下界不低于 1 时
        // 它吃哪几个字节由字符类定，列得出来）。这一段照改动前的规矩处理。
        bool gap_enumerable = true;
        bool width_known = true;  // 前面吃掉多少字节算得出来
        // 前面出现了「列不出字节种类的必须吃字节的节点」（点号、取反类、带反向引用的分组）：
        // 这段路走不下去，改按宽度算起点范围。
        bool by_range = false;

        for (const int kid : sequence) {
            const Node& node = arena_[kid];
            if (node.kind == Kind::Assert || node.kind == Kind::SaveStart ||
                node.kind == Kind::SaveEnd || node.kind == Kind::Empty)
                continue;

            std::size_t kid_min = 0;
            std::size_t kid_max = 0;
            const bool bounded = byte_bounds(kid, kid_min, kid_max);
            const bool gap_node = nullable(kid) || node.kind == Kind::Repeat;

            // 这一节里必然出现的那串字：字面量、字面量的重复（--+ 必有「--」）、
            // 几个字面量分支。命中串从这里开始，候选起点就在它前面那一小段里。
            std::vector<std::string> needles;
            bool usable = bounded && literal_prefixes(kid, needles) && !needles.empty();
            // 下界不小于 1 的量词也保证那一串字必然出现，不必把它当成「可吃的前缀」。
            if (!usable && gap_node && node.kind == Kind::Repeat && node.min >= 1 &&
                literal_prefixes(kid, needles) && !needles.empty()) {
                usable = true;
            }
            // 一字节的候选串只在「按字节宽度定位」那条路上收（见下面那个分支）：窗口只有
            // 几格，一次快扫 + 几处尝试，比在密集的首字节候选上逐个跑完整匹配便宜几十倍。
            // 按「能吃掉的前缀字节」往回退那条路不收它：那一段长度没上限，一字节的候选串
            // 等于逐格试，也就等于没筛，还会把 (a+)+b 这类模式的回溯救回来——上限本来是
            // 该响亮报错的，不能悄悄变成「跑完了没命中」。
            if (usable && gap_enumerable) {
                for (const std::string& needle : needles) {
                    if (needle.size() < 2) {
                        usable = false;
                        break;
                    }
                }
            }

            if (usable) {
                if (gap_enumerable) {
                    std::size_t count = 0;
                    for (std::size_t i = 0; i < 256; ++i) count += gap[i] != 0 ? 1 : 0;
                    // gap 为空时起点就是命中串的位置，开头就能拿到候选串的那几种预筛更直接；
                    // 能列出来的字节太多时逐字节往回退也不如那几种，两种都让给下面的预筛。
                    if (count == 0 || count > kGapBytes) return false;
                    filter.gap = gap;
                } else if (width_known && max_gap > 0 && max_gap <= kMaxGapWidth &&
                           (prefix_lead_count(sequence) > kMaxSkipLanes ||
                            prefix_lead_budget(sequence) >= kLeadBroad)) {
                    // 前面那一节列不出来、宽度算得出来：只在命中串前面这一段里找起点。
                    // 门槛按「开头放开的码点数」判，不只按首字节种类数：快扫只管找候选串的
                    // 速度，每个候选起点还要跑一遍完整匹配，候选密的时候那才是大头。
                    filter.gap_by_bytes = true;
                    filter.min_gap = min_gap;
                    filter.max_gap = max_gap;
                } else {
                    return false;
                }
                filter.mode = FilterData::Mode::Needles;
                filter.keys = std::move(needles);
                fill_key_index(filter);
                fill_skip_lanes(filter);
                return true;
            }

            // 这一节必须吃字节，却给不出命中串：改按宽度算起点范围。
            if (!gap_node) {
                by_range = true;
                gap_enumerable = false;
                if (!bounded) return false;
            } else if (!by_range) {
                // 与改动前一致：这一段列不出字节种类就整条不筛，交给首字节那一类预筛。
                if (!gap_bytes(kid, gap)) return false;
            }

            if (bounded) {
                min_gap += kid_min;
                max_gap += kid_max;
                if (max_gap > kMaxGapWidth) width_known = false;
            } else {
                width_known = false;
            }
        }
        return false;
    }

    FilterData build_filter(int root_id) const {
        FilterData filter;
        const Node& root = arena_[root_id];
        const std::vector<int>* sequence = root.kind == Kind::Concat ? &root.kids : nullptr;

        // 开头就是「一串同样的字符」且没有上限：整串里只有第一个起点值得试。
        // 理由：从串里靠后的位置出发，量词能走到的地方是串首那次能走到的地方的子集，
        // 而后面那些条件只看落点、不看起点，所以串首那次没命中，整串都不必再试。
        // 这个模式开头没有锚点与环视，起点不影响任何条件的真假，所以成立。
        // 这条判据与后面用哪种预筛无关，先算出来，各条返回路上都带着它。
        if (sequence != nullptr && !sequence->empty() && !nullable(root_id)) {
            const Node& front = arena_[sequence->front()];
            if (front.kind == Kind::Repeat && front.max < 0 && front.min >= 1 &&
                is_simple_kind(arena_[front.kids.front()].kind)) {
                filter.skip_run = true;
                filter.run_atom = front.kids.front();
            }
        }

        // 以 ^ 开头：只看行首或全文开头，比逐字节试便宜得多。
        if (sequence != nullptr && !sequence->empty()) {
            const Node& front = arena_[sequence->front()];
            if (front.kind == Kind::Assert) {
                if (front.assert_kind == AssertKind::BeginLine) {
                    filter.mode = FilterData::Mode::LineStarts;
                    return filter;
                }
                if (front.assert_kind == AssertKind::BeginText) {
                    filter.mode = FilterData::Mode::TextStart;
                    return filter;
                }
            }
        }

        // 必需的字面前缀：从开头一路是字面量（中间可以夹零宽的锚点与环视）就连起来。
        std::string prefix;
        bool prefix_ok = sequence != nullptr;
        if (prefix_ok) {
            for (const int kid : *sequence) {
                const Node& node = arena_[kid];
                if (node.kind == Kind::ByteLit && !node.icase) {
                    prefix += node.lit;
                    continue;
                }
                if (node.kind == Kind::Assert || node.kind == Kind::SaveStart ||
                    node.kind == Kind::SaveEnd) {
                    continue;
                }
                if (node.kind == Kind::Class && !node.cls.negated && node.cls.ranges.size() == 1 &&
                    node.cls.ranges[0].first == node.cls.ranges[0].second) {
                    encode_utf8(node.cls.ranges[0].first, prefix);
                    continue;
                }
                break;
            }
        }
        if (prefix.size() >= 2) {
            filter.mode = FilterData::Mode::Literal;
            filter.prefix = std::move(prefix);
            return filter;
        }

        // 开头先是环视与一串空白、后面才是一串备选词：定位从备选词下手。
        // 按「开头必须是这几串字节」筛（下面那一段）会把每一个空格都当成候选起点，
        // 缩进、表格、命令行一多，候选点多到走不完；按「命中里必然出现的那几串字节」筛，
        // 一个空格都不必试。
        if (prefix_ok && !sequence->empty() && build_needle_filter(*sequence, filter)) return filter;

        // 其余情况：算出开头必须是哪几串字节。
        FirstSet first;
        if (root.kind == Kind::Concat) {
            collect_first(root.kids, first);
        } else {
            node_first(root_id, first);
            first.nullable = nullable(root_id);
        }
        // 开头的候选串：一次性说清了就用它，比逐字节试便宜得多。
        if (first.complete && !first.keys.empty() && first.keys.size() <= 64) {
            filter.mode = FilterData::Mode::Keys;
            filter.keys = std::move(first.keys);
            fill_key_index(filter);
            fill_skip_lanes(filter);
            return filter;
        }
        // 首字节集合。它是「可能开头」的超集，只用来放过明显不可能的位置；
        // 空命中也是命中，所以模式可空时不能拿开头当筛子，全放开。
        filter.lead.fill(1);
        if (!first.nullable) {
            // 两套首字节表：宽的算上坏字节落单，窄的不算。
            std::array<std::uint8_t, 256> wide{};
            std::array<std::uint8_t, 256> strict{};
            if (sequence != nullptr) {
                for (const int kid : *sequence) {
                    collect_lead_bytes(kid, wide, true);
                    collect_lead_bytes(kid, strict, false);
                    if (!nullable(kid)) break;
                }
            } else {
                collect_lead_bytes(root_id, wide, true);
                collect_lead_bytes(root_id, strict, false);
            }
            std::size_t wide_count = 0;
            std::size_t strict_count = 0;
            for (std::size_t byte = 0; byte < 256; ++byte) {
                wide_count += wide[byte] != 0 ? 1 : 0;
                strict_count += strict[byte] != 0 ? 1 : 0;
            }
            if (wide_count != 0) {
                filter.mode = FilterData::Mode::Lead;
                if (wide_count > kMaxSkipLanes && strict_count < wide_count) {
                    // 宽的那套摊不进快扫列，收窄之后每个字节还要就地把「落单坏字节」判掉。
                    // 两套都精确，只是一套把判定放在建表时、一套放在扫描时；后者才让
                    // 「取反类开头」这条规则在 CJK 稿件上从「几乎每个字都是候选」掉到几千个。
                    filter.lead = strict;
                    filter.lone_check = true;
                } else {
                    filter.lead = wide;
                    fill_lead_lanes(filter);
                }
            }
        }

        return filter;
    }

    // 给每个分支节点算出「每一支的首字节集合」，运行期据此跳过明显对不上的分支。
    void mark_alt_first_bytes() {
        for (std::size_t index = 0; index < arena_.size(); ++index) {
            if (arena_[index].kind != Kind::Alt) continue;
            std::vector<std::vector<std::uint8_t>> per_branch;
            for (const int kid : arena_[index].kids) {
                if (nullable(kid)) {
                    per_branch.emplace_back();  // 可能空命中，首字节说不准
                    continue;
                }
                FilterData scratch;
                collect_lead_bytes(kid, scratch.lead, true);
                std::vector<std::uint8_t> allowed;
                for (std::size_t byte = 0; byte < 256; ++byte) {
                    if (scratch.lead[byte] != 0) allowed.push_back(static_cast<std::uint8_t>(byte));
                }
                if (allowed.size() == 256) allowed.clear();  // 全都可能，等于没有信息
                per_branch.push_back(std::move(allowed));
            }
            arena_[index].alt_first = std::move(per_branch);
        }
    }

    std::string_view text_;
    std::string_view view_;
    std::size_t pos_ = 0;
    std::vector<Node> arena_;
    int ncaps_ = 0;
    bool icase_ = false;
    bool multiline_ = false;
    bool dotall_ = false;
};

// 这一格是不是某一串候选的开头：先按首字节与头两个字节把候选缩到几位，再整串比。
// 「命中里必然出现这几串」那条预筛用它。同样的索引在「开头必须是这几串」那条循环里
// 就地展开：两处都站在逐字节的热循环上，抽成一个函数两处调用时编译器不内联，
// 实测走一遍 15MB 慢七成。
inline bool key_starts_at(const FilterData& filter, std::string_view haystack, std::size_t at) {
    const char* data = haystack.data();
    const auto first_byte = static_cast<unsigned char>(data[at]);
    if (filter.lead[first_byte] == 0) return false;
    std::uint64_t mask = filter.keys_by_first[first_byte];
    if (at + 1 < haystack.size() && !filter.keys_by_pair.empty()) {
        const std::uint16_t pair =
            static_cast<std::uint16_t>((first_byte << 8) | static_cast<unsigned char>(data[at + 1]));
        const auto found =
            std::lower_bound(filter.keys_by_pair.begin(), filter.keys_by_pair.end(), pair,
                             [](const auto& item, std::uint16_t wanted) {
                                 return item.first < wanted;
                             });
        if (found != filter.keys_by_pair.end() && found->first == pair) mask |= found->second;
    }
    while (mask != 0) {
        const std::size_t index = static_cast<std::size_t>(std::countr_zero(mask));
        mask &= mask - 1;
        const std::string& key = filter.keys[index];
        if (at + key.size() <= haystack.size() && haystack.compare(at, key.size(), key) == 0)
            return true;
    }
    return false;
}

// 这一格是不是「坏字节落单」：不是 ASCII，又解不成一个完整的字符，解码器就把这一格
// 当成一个独立码点。首字节表收窄之后，这一类起点要在这里接住，否则会漏命中。
// left 是这一格后面还剩多少字节；不够判的时候一律当成落单（多留候选，不漏）。
inline bool malformed_cp_start(const char* at, std::size_t left) {
    const auto first = static_cast<unsigned char>(at[0]);
    if (first < 0xC0) return false;               // ASCII 由首字节表管，续字节不是起点
    const std::size_t want = first < 0xE0 ? 1 : (first < 0xF0 ? 2 : (first < 0xF8 ? 3 : 0));
    if (want == 0 || left <= want) return true;    // 0xF8..0xFF，或尾巴不够，一律算落单
    for (std::size_t i = 1; i <= want; ++i) {
        if ((static_cast<unsigned char>(at[i]) & 0xC0) != 0x80) return true;
    }
    return false;
}

// 这个位置上可能开头的是不是候选串：先看首字节表，配了前缀核对表的再看前缀。
// 这里只排除，判错的方向永远是「多留候选」。
inline bool may_start_here(const FilterData& filter, const char* at, std::size_t prefix_length) {
    if (filter.lead[static_cast<unsigned char>(at[0])] == 0) return false;
    if (prefix_length == 0) return true;  // 没有前缀核对表：只按首字节筛
    return filter.prefix_slot[prefix_slot_index(prefix_word(at, prefix_length))] != 0;
}



// 一批看几格（逐字节查表的那条路）。八格正好一个字节的位。
constexpr std::size_t kSkipBatch = 8;

// 摊成比较指令的路一批看几格：一个 128 位寄存器十六格。
constexpr std::size_t kSkipLanes = 16;

#if defined(TEXTTOOL_HAS_SSE2)
// 十六格里哪些格可能是候选起点：每一格一位。
// 首字节与次字节各自摊成几条相等比较，两者取与。两个集都是真集的超集，
// 所以这里放过的位置可能不是候选，但候选绝不会被放过。
inline std::uint32_t candidate_lanes_sse2(const FilterData& filter, const char* at) {
    const __m128i firsts = _mm_loadu_si128(reinterpret_cast<const __m128i*>(at));
    __m128i hit_first = _mm_setzero_si128();
    for (std::size_t i = 0; i < filter.lead_lane_count; ++i)
        hit_first = _mm_or_si128(hit_first,
                                 _mm_cmpeq_epi8(firsts, _mm_set1_epi8(static_cast<char>(
                                                           filter.lead_lanes[i]))));
    if (filter.second_lane_count == 0) return static_cast<std::uint32_t>(_mm_movemask_epi8(hit_first));

    const __m128i seconds = _mm_loadu_si128(reinterpret_cast<const __m128i*>(at + 1));
    __m128i hit_second = _mm_setzero_si128();
    for (std::size_t i = 0; i < filter.second_lane_count; ++i)
        hit_second = _mm_or_si128(
            hit_second,
            _mm_cmpeq_epi8(seconds, _mm_set1_epi8(static_cast<char>(filter.second_lanes[i]))));
    return static_cast<std::uint32_t>(
        _mm_movemask_epi8(_mm_and_si128(hit_first, hit_second)));
}
#endif

// 把 pos 推到下一个可能是候选起点的位置，或者推到离末尾不足一批的地方。
// 逐字节看的时候，汉字稿上每隔两三个字节就换一次分支，误预测比查表本身还贵。
// 两条路都只做排除，判错的方向永远是「多留候选」：
//   * 摊成比较指令的路：首字节能摊成不超过 kMaxSkipLanes 条比较时用它；
//   * 否则逐字节查表，八格一批凑成一个字节的候选位。
// LoneCheck 是编进循环里的：首字节表收窄过的那几条规则（见 build_filter）才需要每格多判
// 一次「落单坏字节」，其余绝大多数规则不该为它多付一次比较——十五格一批的那条路上，
// 每格多一句就是每个字节多一句。
template <bool LoneCheck>
inline void skip_to_candidates_impl(const FilterData& filter, std::string_view haystack,
                                   std::size_t& pos, std::size_t prefix_length) {
    const char* data = haystack.data();
    const std::size_t size = haystack.size();

#if defined(TEXTTOOL_HAS_SSE2)
    if (filter.lead_lane_count != 0) {
        while (pos + kSkipLanes + 1 <= size) {
            std::uint32_t lanes = candidate_lanes_sse2(filter, data + pos);
            if (lanes != 0) {
                pos += static_cast<std::size_t>(std::countr_zero(lanes));
                return;
            }
            pos += kSkipLanes;
        }
    }
#endif

    // 一批八格，每格最多往后看三字节，所以要留出 kSkipBatch + 2 格
    while (pos + kSkipBatch + 2 < size) {
        std::uint32_t bits = 0;
        for (std::size_t i = 0; i < kSkipBatch; ++i) {
            const char* at = data + pos + i;
            bool ok = false;
            if constexpr (LoneCheck) {
                ok = may_start_here(filter, at, prefix_length) ||
                     malformed_cp_start(at, size - (pos + i));
            } else {
                ok = may_start_here(filter, at, prefix_length);
            }
            bits |= static_cast<std::uint32_t>(ok) << i;
        }
        if (bits != 0) {
            pos += static_cast<std::size_t>(std::countr_zero(bits));
            return;
        }
        pos += kSkipBatch;
    }
}

#if defined(__GNUC__) || defined(__clang__)
__attribute__((always_inline))
#endif
inline void skip_to_candidates(const FilterData& filter, std::string_view haystack,
                              std::size_t& pos, std::size_t prefix_length) {
    if (filter.lone_check) {
        skip_to_candidates_impl<true>(filter, haystack, pos, prefix_length);
    } else {
        skip_to_candidates_impl<false>(filter, haystack, pos, prefix_length);
    }
}

// ---------------------------------------------------------------- 运行期

struct CapSlot {
    std::size_t begin = kUnset;
    std::size_t end = kUnset;
};

// 续接点：当前节点匹配完之后接着要去哪一段的哪个位置。
// 全部放在堆上的 frames_ 里，用下标代替指针：量词在长串上要走几百万轮时，
// 涨的是这个 vector，不是 C++ 调用栈，所以轮数只受内存限，不受栈大小限。
// 建好之后不再改动，回溯时按下标读。
struct Frame {
    enum class Kind : std::uint8_t { Sequence, Repeat };
    Kind kind = Kind::Sequence;
    const std::vector<int>* sequence = nullptr;  // Sequence：要接着走的这一段
    std::size_t index = 0;                       // Sequence：下一节的下标
    int repeat = -1;                             // Repeat：量词节点
    std::size_t iteration = 0;                   // Repeat：已经走完的轮数
    std::size_t start_pos = 0;                   // Repeat：这一轮的起点，用来识别零宽迭代
    int next = -1;                               // 再往后的续接点，-1 表示到此结束
};

struct TrailEntry {
    int slot;
    bool begin_side;
    std::size_t old_value;
};

// 回溯栈上的一项。node >= 0 时去匹配那个节点，node < 0 时只顺着续接点往前走。
// 压进去时顺手记下捕获记录与续接点链的长度，弹出来先按它们恢复现场，
// 换一条路就不用一路手动还原。
struct Work {
    int node = -1;
    int next = -1;
    std::size_t pos = 0;
    std::size_t trail_mark = 0;
    std::size_t frame_mark = 0;
    bool alternative = false;  // 是「换一条路」的备选，弹出来算一步
};

class Matcher {
public:
    Matcher(const Program& program, std::string_view haystack, const MatchLimits& limits)
        : program_(program),
          haystack_(haystack),
          limit_(step_budget(haystack.size(), limits)),
          scan_limit_(std::max(limit_, kScanPerByte * haystack.size())),
          caps_(own_caps_),
          trail_(own_trail_),
          frames_(own_frames_),
          work_(own_work_) {
        caps_.assign(program.ncaps + 1, CapSlot{});
        trail_.clear();
        frames_.clear();
        work_.clear();
        // 扫描阶段每个候选位置都要跑一遍，这两个 vector 一上来就留够容量，
        // 免得短模式在每个字节上都分配一次。
        if (frames_.capacity() == 0) frames_.reserve(64);
        if (work_.capacity() == 0) work_.reserve(64);
    }

    // 从 pos 起必须整段命中；命中时 out 是字节区间。
    bool anchored(std::size_t pos, MatchRange& out) {
        std::fill(caps_.begin(), caps_.end(), CapSlot{});
        trail_.clear();
        frames_.clear();
        work_.clear();
        std::size_t end = pos;
        if (!run(program_.root, -1, pos, end)) return false;
        out.begin = pos;
        out.end = end;
        return true;
    }

    // 简单原子往前吃一次。调用方只用「一次只有一种吃法」的原子（字符类、点号、
    // 字面量、反向引用），所以这里直接认，不进回溯机：扫描阶段每个字节都要调它，
    // 省下每字节一次入栈。吃不动就原样返回 pos。
    std::size_t step_atom(int atom, std::size_t pos) const {
        const Node& node = program_.arena[atom];
        switch (node.kind) {
            case Kind::ByteLit:
                return match_bytes(node, pos) ? pos + node.lit.size() : pos;
            case Kind::Class:
            case Kind::Dot: {
                if (pos >= haystack_size_) return pos;
                const Decoded decoded = decode_utf8(haystack_data_, haystack_size_, pos);
                const bool fits = node.kind == Kind::Dot
                                      ? (node.dot_all || decoded.cp != '\n')
                                      : node.cls.matches(decoded.cp);
                return fits ? pos + decoded.len : pos;
            }
            case Kind::Backref: {
                const std::size_t length = backref_length(node);
                if (length == kUnset || pos + length > haystack_size_) return pos;
                return match_bytes_at_capture(node, pos, length) ? pos + length : pos;
            }
            default:
                return pos;
        }
    }

    // 把同一个原子一路吃到底：扫描阶段用它把整串同样的字符一次跳过去。
    std::size_t step_atom_run(int atom, std::size_t pos) {
        std::size_t cursor = pos;
        for (;;) {
            const std::size_t stop = step_atom(atom, cursor);
            if (stop <= cursor) return cursor;
            cursor = stop;
        }
    }

private:
    [[noreturn]] void overflow_steps() const {
        throw std::runtime_error("模式「" + program_.text + "」超过步数上限 " +
                                 std::to_string(limit_) + " 步，已停下（病态回溯）");
    }

    [[noreturn]] void overflow_scan() const {
        throw std::runtime_error("模式「" + program_.text + "」在本段文本上超过扫描量上限 " +
                                 std::to_string(scan_limit_) + " 字节次，已停下（病态回溯）");
    }

    void tick() {
        if (++steps_ > limit_) overflow_steps();
    }

    void spend(std::size_t rounds = 1) {
        scan_ += rounds;
        if (scan_ > scan_limit_) overflow_scan();
    }

    int push_frame(Frame frame) {
        frames_.push_back(frame);
        return static_cast<int>(frames_.size()) - 1;
    }

    int push_sequence(const std::vector<int>& sequence, std::size_t index, int next) {
        Frame frame;
        frame.kind = Frame::Kind::Sequence;
        frame.sequence = &sequence;
        frame.index = index;
        frame.next = next;
        return push_frame(frame);
    }

    int push_repeat(int repeat, std::size_t iteration, std::size_t start_pos, int next) {
        Frame frame;
        frame.kind = Frame::Kind::Repeat;
        frame.repeat = repeat;
        frame.iteration = iteration;
        frame.start_pos = start_pos;
        frame.next = next;
        return push_frame(frame);
    }

    // 往回溯栈上放一条活儿。alternative 标记它是不是「换一条路」的备选：
    // 是备选就记一步，线性往前走的那些不记，免得长稿上的正常扫描被算成试错。
    void push_work(int node, int next, std::size_t pos, bool alternative = false) {
        Work item;
        item.node = node;
        item.next = next;
        item.pos = pos;
        item.trail_mark = trail_.size();
        item.frame_mark = frames_.size();
        item.alternative = alternative;
        work_.push_back(item);
    }

    // 顺着续接点从 pos 往下走，看这条路通不通。
    bool resume(int next, std::size_t pos, std::size_t& end) {
        if (next < 0) {
            end = pos;
            return true;
        }
        return run(-1, next, pos, end);
    }

    enum class Step : std::uint8_t { Matched, Node, Failed };

    // 顺着续接点往前走，直到要么整段到了头（Matched）、要么有活可干（Node）、
    // 要么这条路走不通（Failed）。量词的续接点在这里分岔：贪婪先把「再来一轮」
    // 走掉、把「不来了」压到栈上，惰性反过来。
    Step follow(std::size_t& at, int& next, int& node) {
        for (;;) {
            if (next < 0) return Step::Matched;
            const Frame frame = frames_[static_cast<std::size_t>(next)];
            if (frame.kind == Frame::Kind::Sequence) {
                const std::vector<int>& sequence = *frame.sequence;
                if (frame.index >= sequence.size()) {
                    next = frame.next;
                    continue;
                }
                next = push_sequence(sequence, frame.index + 1, frame.next);
                node = sequence[frame.index];
                return Step::Node;
            }
            const Node& repeat = program_.arena[frame.repeat];
            const std::size_t max_count =
                repeat.max < 0 ? std::numeric_limits<std::size_t>::max()
                               : static_cast<std::size_t>(repeat.max);
            const std::size_t min_count = static_cast<std::size_t>(repeat.min);
            const bool zero_width = frame.iteration > 0 && at == frame.start_pos;
            const bool can_more = !zero_width && frame.iteration < max_count;
            const bool can_exit = frame.iteration >= min_count;
            const int atom = repeat.kids.front();
            if (repeat.greedy) {
                if (!can_more) {
                    if (!can_exit) return Step::Failed;
                    next = frame.next;
                    continue;
                }
                tick();
                const int inner = push_repeat(frame.repeat, frame.iteration + 1, at, frame.next);
                if (can_exit) push_work(-1, frame.next, at, true);  // 「不来了」留到后面再试
                next = inner;
                node = atom;
                return Step::Node;
            }
            if (can_exit) {
                if (can_more) {
                    const int inner = push_repeat(frame.repeat, frame.iteration + 1, at, frame.next);
                    push_work(atom, inner, at, true);  // 「再来一轮」留到后面再试
                }
                next = frame.next;
                continue;
            }
            if (!can_more) return Step::Failed;
            tick();
            next = push_repeat(frame.repeat, frame.iteration + 1, at, frame.next);
            node = atom;
            return Step::Node;
        }
    }

    // 一条路走到底：命中返回 Matched，走不通返回 Failed。走到岔路就把备选压到回溯栈上，
    // 不在这里换路：换路由 run 统一做。
    Step walk(int& current, std::size_t& at, int& chain, std::size_t& end) {
        Step result = Step::Failed;
        bool done = false;
        while (!done) {
                if (current < 0) {
                    const Step step = follow(at, chain, current);
                    if (step == Step::Matched) {
                        end = at;
                        result = Step::Matched;
                        done = true;
                    } else if (step == Step::Failed) {
                        done = true;
                    }
                    continue;
                }
                const Node& current_node = program_.arena[current];
                switch (current_node.kind) {
                    case Kind::Empty:
                        current = -1;
                        break;

                    case Kind::ByteLit:
                        if (!match_bytes(current_node, at)) {
                            done = true;
                            break;
                        }
                        at += current_node.lit.size();
                        current = -1;
                        break;

                    case Kind::Class:
                    case Kind::Dot: {
                        if (at >= haystack_size_) {
                            done = true;
                            break;
                        }
                        const Decoded decoded = decode_utf8(haystack_data_, haystack_size_, at);
                        const bool fits = current_node.kind == Kind::Dot
                                              ? (current_node.dot_all || decoded.cp != '\n')
                                              : current_node.cls.matches(decoded.cp);
                        if (!fits) {
                            done = true;
                            break;
                        }
                        at += decoded.len;
                        current = -1;
                        break;
                    }

                    case Kind::Concat:
                        if (current_node.kids.empty()) {
                            current = -1;
                            break;
                        }
                        if (current_node.kids.size() > 1) {
                            chain = push_sequence(current_node.kids, 1, chain);
                        }
                        current = current_node.kids.front();
                        break;

                    case Kind::Alt: {
                        // 每个分支压一项，倒着压，弹出的顺序才是「按写的顺序挨个试」；
                        // 栈顶那一项就是眼下要走的分支。
                        const bool has_byte = at < haystack_size_;
                        const auto first_byte =
                            has_byte ? static_cast<std::uint8_t>(haystack_data_[at])
                                     : std::uint8_t{0};
                        const std::size_t before = work_.size();
                        for (std::size_t branch = current_node.kids.size(); branch-- > 0;) {
                            if (has_byte && branch < current_node.alt_first.size() &&
                                !current_node.alt_first[branch].empty() &&
                                !std::binary_search(current_node.alt_first[branch].begin(),
                                                     current_node.alt_first[branch].end(),
                                                     first_byte)) {
                                continue;
                            }
                            push_work(current_node.kids[branch], chain, at, true);
                        }
                        if (work_.size() == before) {
                            done = true;  // 这个位置上一条分支都不对
                            break;
                        }
                        const Work branch = work_.back();
                        work_.pop_back();
                        rollback(branch.trail_mark);
                        frames_.resize(branch.frame_mark);
                        tick();  // 试一支算一步，与压栈的备选合起来正好每支一步
                        current = branch.node;
                        break;
                    }

                    case Kind::Repeat:
                        if (is_simple_atom(current_node.kids.front())) {
                            // 简单原子的量词自带回溯循环，会连续接点一起试完，
                            // 成功就是整段到头了，回来时不必再把续接点走一遍。
                            std::size_t stop = at;
                            if (!match_repeat_simple(current_node, at, stop, chain)) {
                                done = true;
                                break;
                            }
                            end = stop;
                            result = Step::Matched;
                            done = true;
                            break;
                        }
                        chain = push_repeat(current, 0, at, chain);
                        current = -1;
                        break;

                    case Kind::SaveStart:
                    case Kind::SaveEnd: {
                        CapSlot& slot = caps_[static_cast<std::size_t>(current_node.cap)];
                        const bool begin_side = current_node.kind == Kind::SaveStart;
                        trail_.push_back(TrailEntry{current_node.cap, begin_side,
                                                    begin_side ? slot.begin : slot.end});
                        if (begin_side) {
                            slot.begin = at;
                        } else {
                            slot.end = at;
                        }
                        current = -1;
                        break;
                    }

                    case Kind::Backref: {
                        const std::size_t length = backref_length(current_node);
                        if (length == kUnset || at + length > haystack_size_ ||
                            !match_bytes_at_capture(current_node, at, length)) {
                            done = true;
                            break;
                        }
                        at += length;
                        current = -1;
                        break;
                    }

                    case Kind::Assert:
                        if (!assert_ok(current_node, at)) done = true;
                        else current = -1;
                        break;
                }
        }
        return result;
    }

    // 匹配一个节点，匹配完了就接着走续接点。node < 0 表示只接着走续接点。
    // 返回 true 表示整段命中，命中时 end 是落点。
    // 走不通就回到回溯栈上换一条路：栈上留着每条备选的出发点、捕获记录与续接点链长。
    bool run(int node, int next, std::size_t pos, std::size_t& end) {
        const std::size_t work_base = work_.size();
        const std::size_t frame_base = frames_.size();
        int current = node;
        std::size_t at = pos;
        int chain = next;
        bool matched = false;
        for (;;) {
            if (walk(current, at, chain, end) == Step::Matched) {
                matched = true;
                break;
            }
            if (work_.size() == work_base) break;  // 没有备选可换了
            const Work item = work_.back();
            work_.pop_back();
            rollback(item.trail_mark);
            frames_.resize(item.frame_mark);
            if (item.alternative) tick();
            current = item.node;
            at = item.pos;
            chain = item.next;
        }
        work_.resize(work_base);
        frames_.resize(frame_base);
        return matched;
    }

    bool match_bytes(const Node& node, std::size_t pos) const {
        const std::size_t length = node.lit.size();
        if (pos + length > haystack_size_) return false;
        if (!node.icase) {
            return std::memcmp(haystack_data_ + pos, node.lit.data(), length) == 0;
        }
        for (std::size_t i = 0; i < length; ++i) {
            const char actual = haystack_data_[pos + i];
            const char folded = (actual >= 'A' && actual <= 'Z')
                                    ? static_cast<char>(actual - 'A' + 'a')
                                    : actual;
            if (folded != node.lit[i]) return false;
        }
        return true;
    }

    // 反向引用要抄的那一段有多长；没捕获到返回 kUnset。
    std::size_t backref_length(const Node& node) const {
        const std::size_t slot = static_cast<std::size_t>(node.ref);
        if (slot >= caps_.size()) return kUnset;
        const CapSlot& captured = caps_[slot];
        if (captured.begin == kUnset || captured.end == kUnset || captured.end < captured.begin) {
            return kUnset;
        }
        return captured.end - captured.begin;
    }

    bool match_bytes_at_capture(const Node& node, std::size_t pos, std::size_t length) const {
        const CapSlot& captured = caps_[static_cast<std::size_t>(node.ref)];
        for (std::size_t i = 0; i < length; ++i) {
            char want = haystack_data_[captured.begin + i];
            char got = haystack_data_[pos + i];
            if (node.icase) {
                if (want >= 'A' && want <= 'Z') want = static_cast<char>(want - 'A' + 'a');
                if (got >= 'A' && got <= 'Z') got = static_cast<char>(got - 'A' + 'a');
            }
            if (want != got) return false;
        }
        return true;
    }

    void rollback(std::size_t mark) {
        while (trail_.size() > mark) {
            const TrailEntry entry = trail_.back();
            trail_.pop_back();
            CapSlot& slot = caps_[static_cast<std::size_t>(entry.slot)];
            if (entry.begin_side) {
                slot.begin = entry.old_value;
            } else {
                slot.end = entry.old_value;
            }
        }
    }

    // 「一次只有一种吃法」的原子：字符类、点号、字面量、反向引用。
    bool is_simple_atom(int id) const { return is_simple_kind(program_.arena[id].kind); }

    // 回溯时往回退一轮迭代。
    std::size_t step_back(int id, std::size_t pos) const {
        const Node& node = program_.arena[id];
        if (node.kind == Kind::ByteLit) {
            return pos >= node.lit.size() ? pos - node.lit.size() : 0;
        }
        if (node.kind == Kind::Backref) {
            const std::size_t length = backref_length(node);
            if (length == kUnset) return 0;
            return pos >= length ? pos - length : 0;
        }
        return prev_char_start(haystack_data_, pos);
    }

    // 简单原子的量词：一个位置只有一种吃法，不必递归，用循环走到底再一轮轮往回退，
    // 内存是常数。每走一轮、每退一轮都记一笔扫描量：退一轮的成本与剩下多少字节成正比，
    // 病态的平方级模式靠这一笔在上限内停下，不会一声不响地跑到天荒地老。
    bool match_repeat_simple(const Node& node, std::size_t pos, std::size_t& end, int next) {
        const int atom = node.kids.front();
        const std::size_t max_count =
            node.max < 0 ? std::numeric_limits<std::size_t>::max()
                         : static_cast<std::size_t>(node.max);
        const std::size_t min_count = static_cast<std::size_t>(node.min);
        const std::size_t start = pos;
        std::size_t cursor = pos;
        std::size_t count = 0;

        if (node.greedy) {
            while (count < max_count) {
                const std::size_t stop = step_atom(atom, cursor);
                if (stop <= cursor) break;
                spend();
                cursor = stop;
                ++count;
            }
            while (count >= min_count) {
                spend();
                if (resume(next, cursor, end)) return true;
                if (count == min_count || count == 0) break;
                cursor = step_back(atom, cursor);
                if (cursor < start) break;
                --count;
            }
            return false;
        }
        while (count < min_count) {
            const std::size_t stop = step_atom(atom, cursor);
            if (stop <= cursor) return false;
            cursor = stop;
            ++count;
        }
        for (;;) {
            spend();
            if (resume(next, cursor, end)) return true;
            if (count >= max_count) return false;
            const std::size_t stop = step_atom(atom, cursor);
            if (stop <= cursor) return false;
            cursor = stop;
            ++count;
        }
    }

    bool word_before(std::size_t pos) const {
        if (pos == 0) return false;
        const std::size_t start = prev_char_start(haystack_data_, pos);
        const Decoded decoded = decode_utf8(haystack_data_, haystack_size_, start);
        return is_word_cp(decoded.cp);
    }

    bool word_at(std::size_t pos) const {
        if (pos >= haystack_size_) return false;
        const Decoded decoded = decode_utf8(haystack_data_, haystack_size_, pos);
        return is_word_cp(decoded.cp);
    }

    // 锚点与环视：只判断成不成立，不往后走。环视里的子模式自己跑一趟，
    // 跑完把捕获记录退回去：环视只借位置，不留下东西。
    bool assert_ok(const Node& node, std::size_t pos) {
        switch (node.assert_kind) {
            case AssertKind::BeginText:
                return pos == 0;
            case AssertKind::BeginLine:
                return pos == 0 || haystack_data_[pos - 1] == '\n';
            case AssertKind::EndText:
                // .NET 的 $ 在全文末尾与「末尾那个换行之前」都成立。
                return pos == haystack_size_ ||
                       (pos + 1 == haystack_size_ && haystack_data_[pos] == '\n');
            case AssertKind::EndLine:
                return pos == haystack_size_ || haystack_data_[pos] == '\n';
            case AssertKind::WordBoundary:
                return word_before(pos) != word_at(pos);
            case AssertKind::NotWordBoundary:
                return word_before(pos) == word_at(pos);
            case AssertKind::LookAhead:
            case AssertKind::NegLookAhead: {
                const std::size_t mark = trail_.size();
                std::size_t sub_end = pos;
                const bool found = run(node.kids.front(), -1, pos, sub_end);
                rollback(mark);
                return node.assert_kind == AssertKind::LookAhead ? found : !found;
            }
            case AssertKind::LookBehind:
            case AssertKind::NegLookBehind: {
                const std::size_t mark = trail_.size();
                const std::size_t window = std::min<std::size_t>(
                    static_cast<std::size_t>(std::max(node.window, 1)), pos);
                bool found = false;
                std::size_t start = pos;
                for (std::size_t back = 0; back <= window && !found; ++back) {
                    if (back > 0) {
                        if (start == 0) break;
                        start = prev_char_start(haystack_data_, start);
                    }
                    tick();
                    std::size_t sub_end = start;
                    if (run(node.kids.front(), -1, start, sub_end) && sub_end == pos) found = true;
                }
                rollback(mark);
                return node.assert_kind == AssertKind::LookBehind ? found : !found;
            }
        }
        return false;
    }

public:
    std::size_t haystack_size() const { return haystack_size_; }

    // 换一份稿件接着用：段落作用域里每段都换。只改指向与上限，栈与缓冲留着复用。
    // 步数账一并归零：每次回验各自一道账，与「每次都建一个新匹配器」相比只会更宽，不会更严。
    void rebind(std::string_view haystack, const MatchLimits& limits) {
        haystack_ = haystack;
        haystack_data_ = haystack.data();
        haystack_size_ = haystack.size();
        limit_ = step_budget(haystack.size(), limits);
        scan_limit_ = std::max(limit_, kScanPerByte * haystack.size());
        steps_ = 0;
        scan_ = 0;
        trail_.clear();
        frames_.clear();
        work_.clear();
        std::fill(caps_.begin(), caps_.end(), CapSlot{});
    }


private:
    const Program& program_;
    std::string_view haystack_;
    const char* haystack_data_ = haystack_.data();
    std::size_t haystack_size_ = haystack_.size();
    std::size_t limit_ = 0;
    std::size_t scan_limit_ = 0;
    std::size_t steps_ = 0;
    std::size_t scan_ = 0;
    std::vector<CapSlot> own_caps_;
    std::vector<TrailEntry> own_trail_;
    std::vector<Frame> own_frames_;
    std::vector<Work> own_work_;
    std::vector<CapSlot>& caps_;
    std::vector<TrailEntry>& trail_;
    std::vector<Frame>& frames_;
    std::vector<Work>& work_;
};

}  // namespace

// ---------------------------------------------------------------- 对外接口

struct Pattern::Impl {
    Program program;

    // 这条模式在「当前这份稿件」上已经量出来的结论。换了稿件（见 begin_document）
    // 代号就对不上，一律当没量过。含义见本文件末尾 search() 上面那段。
    std::uint64_t memo_serial = 0;
    std::size_t no_match_from = kNoMatch;
    std::size_t hit_from = kNoMatch;
    std::size_t hit_begin = kNoMatch;
    std::size_t hit_end = kNoMatch;
};

Pattern::Pattern(std::string_view text) : impl_(nullptr) {
    Compiler compiler(text);
    impl_ = new Impl{compiler.run()};
}

Pattern::~Pattern() { delete impl_; }

Pattern::Pattern(Pattern&& other) noexcept : impl_(other.impl_) { other.impl_ = nullptr; }

Pattern& Pattern::operator=(Pattern&& other) noexcept {
    std::swap(impl_, other.impl_);
    return *this;
}

std::size_t step_budget(std::size_t haystack_bytes, const MatchLimits& limits) {
    return limits.steps + limits.steps_per_byte * haystack_bytes;
}

const std::string& Pattern::text() const { return impl_->program.text; }

namespace {

// 整条模式就是一串字面量时，把这串字节拼出来；否则返回假。
// 只认纯字面量节点与零宽的捕获标记：「命中就是这一串」只有在模式里没有别的花样时才成立，
// 有一处花样（字符类、量词、环视、大小写不敏感……）就老实地交回逐条扫。
bool pure_literal_of(const Program& program, int id, std::string& out, int depth) {
    if (depth > 64 || id < 0 || id >= static_cast<int>(program.arena.size())) return false;
    const Node& node = program.arena[id];
    switch (node.kind) {
        case Kind::SaveStart:
        case Kind::SaveEnd:
            return true;  // 零宽，不吃字节
        case Kind::ByteLit:
            if (node.icase) return false;
            out += node.lit;
            return true;
        case Kind::Concat:
            for (const int kid : node.kids) {
                std::string piece;
                if (!pure_literal_of(program, kid, piece, depth + 1)) return false;
                out += piece;
            }
            return true;
        default:
            return false;
    }
}

}  // namespace

StartPlan Pattern::start_plan() const {
    StartPlan plan;
    if (impl_ == nullptr) return plan;
    const Program& program = impl_->program;
    // 可空的模式命中在每一个字符边界上都有，起点不只是首集那几串的起点
    if (program.nullable) return plan;

    std::string literal;
    if (pure_literal_of(program, program.root, literal, 0) && !literal.empty()) {
        plan.kind = StartKind::literal;
        plan.whole = true;
        plan.needles.push_back(std::move(literal));
        return plan;
    }

    const FilterData& filter = program.filter;
    if (filter.mode == FilterData::Mode::Literal && !filter.prefix.empty()) {
        plan.kind = StartKind::literal;
        plan.needles.push_back(filter.prefix);
        return plan;
    }
    if (filter.mode == FilterData::Mode::Keys && !filter.keys.empty()) {
        // keys 是穷尽且精确的首集（build_filter 只在 first.complete 时选这条路），
        // 命中一定从其中某一串的起点开始，所以按这几串找起点不会漏。
        plan.kind = StartKind::keys;
        plan.needles = filter.keys;
        return plan;
    }
    return plan;
}

// ---------------------------------------------------------------- 连续回验

struct PatternVerifier::Impl {
    Impl(const Program& program, std::string_view haystack, const MatchLimits& limits)
        : matcher(program, haystack, limits) {}
    Matcher matcher;
};

PatternVerifier::PatternVerifier(const Pattern& pattern, std::string_view haystack,
                                 const MatchLimits& limits)
    : impl_(nullptr) {
    if (pattern.impl_ == nullptr) return;
    impl_ = new Impl(pattern.impl_->program, haystack, limits);
}

PatternVerifier::~PatternVerifier() { delete impl_; }

PatternVerifier::PatternVerifier(PatternVerifier&& other) noexcept : impl_(other.impl_) {
    other.impl_ = nullptr;
}

PatternVerifier& PatternVerifier::operator=(PatternVerifier&& other) noexcept {
    std::swap(impl_, other.impl_);
    return *this;
}

void PatternVerifier::rebind(std::string_view haystack, const MatchLimits& limits) {
    if (impl_ != nullptr) impl_->matcher.rebind(haystack, limits);
}

bool PatternVerifier::match_at(std::size_t at, MatchRange& out) {
    if (impl_ == nullptr) return false;
    if (at > impl_->matcher.haystack_size()) return false;
    return impl_->matcher.anchored(at, out);
}

namespace {

// 一份稿件的代号：登记一次换一个。换过之后旧结论的代号都对不上，一律作废。
// 只在本线程里有效：同时扫多份稿件时各线程各登记各的。
struct DocumentScope {
    const char* data = nullptr;
    std::size_t size = 0;
    std::uint64_t serial = 0;
};

DocumentScope& current_scope() {
    static thread_local DocumentScope scope;
    return scope;
}

// 从 from 起真扫一遍。这个结论能不能留到下一次查询，由下面的 Pattern::search 决定。
MatchRange search_fresh(const Program& program, std::string_view haystack, std::size_t from,
                        const MatchLimits& limits) {
    const MatchRange none{kNoMatch, kNoMatch};
    if (from > haystack.size()) return none;

    const FilterData& filter = program.filter;
    Matcher matcher(program, haystack, limits);
    MatchRange out{none};

    switch (filter.mode) {
        case FilterData::Mode::TextStart:
            if (from == 0 && matcher.anchored(0, out)) return out;
            return none;
        case FilterData::Mode::LineStarts: {
            std::size_t pos = from;
            while (pos <= haystack.size()) {
                if ((pos == 0 || haystack[pos - 1] == '\n') && matcher.anchored(pos, out)) return out;
                if (pos == haystack.size()) break;
                const std::size_t newline = haystack.find('\n', pos);
                if (newline == std::string_view::npos) break;
                pos = newline + 1;
            }
            return none;
        }

        case FilterData::Mode::Literal: {
            const std::string& prefix = filter.prefix;
            std::size_t pos = from;
            while (pos + prefix.size() <= haystack.size()) {
                const std::size_t at = haystack.find(prefix, pos);
                if (at == std::string_view::npos) break;
                if (matcher.anchored(at, out)) return out;
                pos = at + 1;
            }
            return none;
        }

        case FilterData::Mode::Needles: {
            // 逐格找「命中里必然出现的那串字节」，再回头试它前面那一小段起点。
            // tried_before 记着已经试到哪儿了：命中串前面的那串字节常常挨在一起，
            // 同一格不会被两条命中串试第二遍。
            const char* data = haystack.data();
            const std::size_t size = haystack.size();
            std::size_t tried_before = from;
            std::size_t pos = from;
            while (pos < size) {
                // 找候选串也是逐格筛：先用快扫一次排掉十六格，比逐个位置查表便宜一个量级。
                skip_to_candidates(filter, haystack, pos, 0);
                if (pos >= size) break;
                if (!key_starts_at(filter, haystack, pos)) {
                    ++pos;
                    continue;
                }
                if (pos > from && !is_char_start(data, size, pos)) {
                    ++pos;
                    continue;
                }

                if (filter.gap_by_bytes) {
                    // 命中串前面那一节有多宽算得出来：起点落在 [pos-max_gap, pos-min_gap]。
                    // 命中串前后两处挨得近时窗口会重叠，重叠的起点再试一遍没有关系：
                    // 两处命中串隔得远时窗口不重叠，也就不必记「试到哪儿了」。
                    if (pos < filter.min_gap) {
                        ++pos;
                        continue;
                    }
                    const std::size_t earliest =
                        pos > filter.max_gap ? pos - filter.max_gap : 0;
                    const std::size_t latest = pos - filter.min_gap;
                    for (std::size_t at = earliest < from ? from : earliest; at <= latest; ++at) {
                        if (!is_char_start(data, size, at)) continue;
                        if (matcher.anchored(at, out)) return out;
                    }
                    ++pos;
                    continue;
                }

                std::size_t begin_at = pos;
                while (begin_at > tried_before &&
                       filter.gap[static_cast<unsigned char>(data[begin_at - 1])] != 0)
                    --begin_at;
                for (std::size_t at = begin_at; at <= pos; ++at) {
                    if (at > from && !is_char_start(data, size, at)) continue;
                    if (matcher.anchored(at, out)) return out;
                }
                tried_before = pos + 1;
                ++pos;
            }
            return none;
        }

        case FilterData::Mode::Keys:
        case FilterData::Mode::Lead:
        case FilterData::Mode::None: {
            const bool by_keys = filter.mode == FilterData::Mode::Keys;
            const std::size_t prefix_length = by_keys ? filter.prefix_length : 0;
            const char* data = haystack.data();
            const std::size_t size = haystack.size();
            std::size_t pos = from;
            while (pos <= size) {
                skip_to_candidates(filter, haystack, pos, prefix_length);
                bool maybe = pos >= size;  // 末尾那一格留给「空命中」
                if (!maybe) {
                    const auto first_byte = static_cast<unsigned char>(data[pos]);
                    maybe = filter.lead[first_byte] != 0;
                    // 首字节表收窄过：落单的坏字节这一格也要接住
                    if (!maybe && filter.lone_check) {
                        maybe = malformed_cp_start(data + pos, size - pos);
                    }
                    if (maybe && by_keys) maybe = key_starts_at(filter, haystack, pos);
                    if (maybe && pos > from && !is_char_start(data, size, pos)) maybe = false;
                }
                if (maybe && matcher.anchored(pos, out)) return out;
                // 串里靠后的起点不可能命中，整串跳过去
                if (filter.skip_run && pos < size) {
                    const std::size_t skip_to = matcher.step_atom_run(filter.run_atom, pos);
                    if (skip_to > pos) {
                        pos = skip_to;
                        continue;
                    }
                }
                ++pos;
            }
            return none;
        }
    }
    return none;
}

}  // namespace

// ---------------------------------------------------------------- 同一份稿件上的重复查询
//
// 上层按句子、按段逐次找同一条模式（比喻堆叠那条就是）：每次单独看都是「从这里往后找一处」，
// 找不到就把后面的整篇再扫一遍。句数一多，同一个尾巴被扫上千遍，耗时随稿件长度平方涨。
// 一条模式在一份稿件上只可能有两种结论值得记住：
//
//   从某一处往后一处都没有（no_match_from）：上一次扫到末尾才知道，再扫一遍是白扫；
//   上一次从 hit_from 起找到的最近一处是 hit_begin：起点落在 [hit_from, hit_begin]
//   之间的查询，最近一处还是它（命中只可能在起点之后，[hit_from, hit_begin) 里一处都没有）。
//
// 两种结论都只对「当时那份稿件」成立，所以只在 begin_document 登记过、且查询的就是整份
// 稿件本体时记录与采用：换一份稿件代号就变了，段落切片与临时串的字节范围也对不上。
// 这样一份稿件上每条模式最多真扫一遍，一条「前面没有」的结论不会在每段上重付一次。
MatchRange Pattern::search(std::string_view haystack, std::size_t from,
                           const MatchLimits& limits) const {
    const MatchRange none{kNoMatch, kNoMatch};
    if (impl_ == nullptr || from > haystack.size()) return none;

    const DocumentScope& scope = current_scope();
    const bool same_document = scope.serial != 0 && haystack.data() == scope.data &&
                               haystack.size() == scope.size;
    if (same_document && impl_->memo_serial == scope.serial) {
        if (from >= impl_->no_match_from) return none;
        if (from >= impl_->hit_from && from <= impl_->hit_begin)
            return MatchRange{impl_->hit_begin, impl_->hit_end};
    }

    const MatchRange out = search_fresh(impl_->program, haystack, from, limits);

    if (same_document) {
        if (impl_->memo_serial != scope.serial) {
            impl_->memo_serial = scope.serial;
            impl_->no_match_from = kNoMatch;
            impl_->hit_from = kNoMatch;
        }
        if (out.begin == kNoMatch) {
            if (from < impl_->no_match_from) impl_->no_match_from = from;
        } else {
            impl_->hit_from = from;
            impl_->hit_begin = out.begin;
            impl_->hit_end = out.end;
        }
    }
    return out;
}

void begin_document(std::string_view document) {
    static std::atomic<std::uint64_t> next_serial{0};
    DocumentScope& scope = current_scope();
    scope.data = document.data();
    scope.size = document.size();
    scope.serial = next_serial.fetch_add(1, std::memory_order_relaxed) + 1;
}

void end_document() { current_scope() = DocumentScope{}; }

}  // namespace texttool
