// markdown 的逐行形状判定与博文结构遮罩。
//
// 判定只有这一处：围栏（代码块）的开关、头部字段的键值行、以 :: 开头的指令行。
// 遮罩、回复检查都用这一套，不再各自定宽严，否则同一份稿子在两个功能里边界不同。
//
// 围栏按 CommonMark：行首三连以上反引号或波浪号开门，关门要同一个字符、长度不短于开门那次。
// 四个反引号的块里出现三个反引号不算关门，仍在块内。
#pragma once

#include <cstddef>
#include <cstdint>
#include <functional>
#include <span>
#include <string>
#include <string_view>
#include <vector>

namespace texttool {

// 围栏的开关状态：一行一行喂进去，回答「这一行是不是围栏行」与「喂完之后围栏开着没有」。
class FenceTracker {
public:
    bool in_fence() const { return in_fence_; }

    // 喂一行（换行符不用带）。返回这一行是不是围栏行（开门或关门的那行）。不是围栏行时状态不变。
    bool advance(std::string_view raw_line);

private:
    bool in_fence_ = false;
    char fence_char_ = '\0';
    std::size_t fence_length_ = 0;
};

namespace markdown {

// 围栏的开门形状：去空白后的行首是成串的三连以上反引号或波浪号。
bool opens_fence(std::string_view trimmed, char& fence_char, std::size_t& fence_length);

// 这一行关不关掉开着的围栏：整行全由开门那个字符组成，长度不短于开门那次。
bool closes_fence(std::string_view trimmed, char fence_char, std::size_t fence_length);

// 头部字段的开门行：去空白后是 ---（字节序标记不算字）。
bool is_front_matter_opening(std::string_view trimmed);

// 收尾行：--- 或 ...。
bool is_front_matter_closing(std::string_view trimmed);

// 这一行是不是给定的头部字段键：title: 标题 与 title : 标题 都算，键名不分 ASCII 大小写。
bool is_front_matter_key(std::string_view trimmed, std::string_view key);

// 收尾分隔符之前有没有 YAML 键值行（title: 标题 这种）。有才当头部字段：
// 纯文本拿 --- 当分隔线时，后面那段多半是正文，遮了等于没检查。
// after_opening_line 是开门那行的末字节偏移。
bool looks_like_front_matter(std::string_view text, std::size_t after_opening_line);

// 指令行：行首是双冒号。这是本站主题的写法，博文靠它认出来。
bool is_directive(std::string_view trimmed);

}  // namespace markdown

// 结构判定的逐行状态机：一行一行喂进去，回答每一行该怎么遮，以及这份稿子算不算博文。
//
// 记住的只有这几格：围栏开着没有、现在是不是在头部字段里、跳行注释与公式块开着没有、
// 见过 title 与 date 了没有、见过 :: 指令行了没有、读到第几行，
// 以及开头那几行与末尾没凑齐那一行的原话。
// 这几格里只有后两样随行数变：开头那几行最多压两百行（判头部字段要往后看那么多行），
// 末尾那一行就是当前这一行。所以整份正文不必装进状态机，喂进来的每一块直接在原处判，
// 状态机不留副本，调用方那份正文与这里不会有同一时刻的两份。
// 整份遮罩（BlogMask::create）也走这一个状态机，判定规则因此只有一处。
class BlogMaskScanner {
public:
    // 行内要遮的一段，按这一行的行首起算。
    struct Span {
        std::size_t begin = 0;
        std::size_t end = 0;
    };

    // 一行的结论。line 指向这一行的字节（行尾的换行不在里面），只在交出来的那一刻有效。
    struct LineMask {
        std::string_view line;
        bool has_newline = false;  // 这一行后面跟着换行
        bool whole_line = false;   // 整行连它的换行一起遮
        std::span<const Span> spans;  // 行内要遮的那几段；整行遮时是空的
    };

    using MaskSink = std::function<void(const LineMask&)>;

    // 正文一块一块喂进来，凑够一行就判一行、调 sink。块边界落在哪个字节上都行。
    // 交出去的那一行指向这一块或 buffer_，只在 sink 返回之前有效。
    void feed(std::string_view utf8, const MaskSink& sink);

    // 收尾：最后没有换行的那一行在这里交出去。全文喂完才问 is_blog_post。
    void finish(const MaskSink& sink);

    // 到现在为止看起来是不是博文：头部字段里有 title 与 date，或者出现过 :: 指令行。
    // 开头是 --- 时，头几行要等看够后面几行才判得出来，在那之前这里的答案是「还不是」。
    bool is_blog_post() const { return (has_title_ && has_date_) || saw_directive_; }

private:
    struct DeferredLine {
        std::string bytes;
        bool has_newline = false;
    };

    // 一行交给判定，行数在这里加。
    void take_line(std::string_view line, bool has_newline, const MaskSink& sink);
    // 一行（不带换行）。first 表示这是稿子的第一行。
    void advance(std::string_view line, bool has_newline, bool first, const MaskSink& sink);
    // 普通行：围栏、跳行注释、公式块、指令行、引用式链接定义、行内那几样。
    void plain_line(std::string_view line, bool has_newline, const MaskSink& sink);
    // 开头那几行的结论出来了：按 accepted 分成「是头部字段」与「不是」两条路交出去。
    void decide_front_matter(bool accepted, const MaskSink& sink);
    // 整行遮与只遮行内几段，两种结论各一个出口。
    void emit_whole_line(std::string_view line, bool has_newline, const MaskSink& sink);
    void emit_inline(std::string_view line, bool has_newline, const MaskSink& sink);

    std::string buffer_;       // 上一块末尾还没凑齐的那一行，仅此一行
    std::vector<Span> spans_;  // 当前这一行行内要遮的那几段，复用它的容量
    std::vector<DeferredLine> deferred_;  // 开头是 --- 时压着的那几行，判出来就清空
    std::size_t lines_seen_ = 0;
    bool deciding_ = false;    // 正在等「开头那几行算不算头部字段」的结论
    bool in_front_matter_ = false;
    bool has_title_ = false;
    bool has_date_ = false;
    bool saw_directive_ = false;
    bool in_comment_ = false;     // 跳行 HTML 注释
    bool in_math_block_ = false;  // 整块公式
    FenceTracker fence_;
};

// 博文稿的结构遮罩：先认出这份稿子是不是博文，再标出其中不属于正文的字符。
//
// 认博文要「头部字段里同时有 title 与 date 两行键值」或者「出现过以 :: 开头的行」。
// 光凭有头部字段不行：VuePress、Docusaurus 那类文档站也有头部字段，它们该按文档检查。
//
// 认成博文之后才遮，遮五类：开头的头部字段整段、以 :: 开头的整行、围栏代码块与行内代码、
// $...$ 与 $$...$$ 公式、链接与图片的地址（连同引用式链接定义整行）。
// 不是博文的稿子一个字都不遮：行内代码里的文件名对文档与回复是有用的证据。
//
// 遮罩与原文逐字节对齐，换行不动：命中的位置、行列仍按原文取，被遮处的字只当空格看。
// 一个字节占一比特（十五兆的稿子约占两兆），被遮的字数在构造时顺手数完。
//
// 检查那条路要整份遮罩（定位、统计都要按原始偏移取），就建这张表；换标点那条路只看
// 「这一段遮不遮」，走上面那个逐行状态机，不建表。两条路的判定是同一套。
class BlogMask {
public:
    // 读一份稿子建遮罩。不是博文时返回空遮罩（masked_chars() 为 0），调用方不必先判断。
    static BlogMask create(std::string_view text);

    // 这份稿子按博文处理：头部字段里有 title 与 date，或者出现过 :: 指令行。
    bool is_blog_post() const { return is_blog_post_; }

    // 被遮住的字节数。不是博文时为 0。
    std::size_t masked_chars() const { return masked_chars_; }

    // 这个字节偏移是不是被遮住的区域。越界一律 false。
    bool is_masked(std::size_t index) const;

    // 把遮住的字换成空格，换行原样留着。规则与统计用它：空格匹配不上任何词。
    // 不是博文或长度对不上时原样交回。
    std::string apply(std::string_view text) const;

    // 标上 [from, to) 这一段；越界的部分直接丢掉。建遮罩时按行喂，外面不必调。
    void mark(std::size_t from, std::size_t to);

private:
    std::vector<std::uint64_t> words_;
    std::size_t length_ = 0;
    std::size_t masked_chars_ = 0;
    bool is_blog_post_ = false;
};

}  // namespace texttool
