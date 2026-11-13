// markdown 逐行判定与博文结构遮罩的测试。
//
// 期望值不是从某次运行抄来的，而是按判定逐条写下来的：认博文要 title 与 date
// 两行键值同在，或出现过以 :: 开头的行；认成博文之后才遮头部字段、指令行、围栏与行内代码、
// 公式、链接地址。改判定就往这里加测试，别只改实现。
#include "doctest/doctest.h"

#include "engine/markdown.h"

#include <algorithm>
#include <string>
#include <string_view>
#include <vector>

using texttool::BlogMask;
using texttool::BlogMaskScanner;
using texttool::FenceTracker;

namespace {

// 把被遮住的地方标成 ^，用来一眼看出遮到哪：报告里只报位置，测试要看得见范围。
std::string outline(const BlogMask& mask, std::string_view text) {
    std::string out;
    bool inside = false;
    for (std::size_t i = 0; i < text.size(); ++i) {
        const bool masked = mask.is_masked(i);
        if (masked && !inside) out += '[';
        if (!masked && inside) out += ']';
        inside = masked;
        out += text[i];
    }
    if (inside) out += ']';
    return out;
}

}  // namespace

TEST_CASE("围栏：三种以上反引号或波浪号开门，关门要比开门那一串不短") {
    FenceTracker fence;
    CHECK(fence.advance("````csharp"));
    CHECK(fence.in_fence());
    CHECK_FALSE(fence.advance("还在块里"));
    CHECK(fence.in_fence());
    // 三个反引号关不掉四个反引号开的门，还在块里
    CHECK_FALSE(fence.advance("```"));
    CHECK(fence.in_fence());
    CHECK(fence.advance("````"));
    CHECK_FALSE(fence.in_fence());

    FenceTracker tilde;
    CHECK(tilde.advance("~~~"));
    CHECK(tilde.in_fence());
    CHECK(tilde.advance("~~~"));
    CHECK_FALSE(tilde.in_fence());
}

TEST_CASE("围栏：两个反引号与行内的三个反引号都不开门") {
    FenceTracker fence;
    CHECK_FALSE(fence.advance("``两个"));
    CHECK_FALSE(fence.in_fence());
    CHECK(fence.advance("   ```"));
    CHECK(fence.advance("```   "));
}

TEST_CASE("头部字段：键名后面紧跟冒号，中间隔着空白也算；汉字键名不算") {
    CHECK(texttool::markdown::is_front_matter_key("title: 一份博文", "title"));
    CHECK(texttool::markdown::is_front_matter_key("TITLE : 一份博文", "title"));
    CHECK(texttool::markdown::is_front_matter_key("date:2024-01-01", "date"));
    CHECK_FALSE(texttool::markdown::is_front_matter_key("titles: 不是这个键", "title"));
    CHECK_FALSE(texttool::markdown::is_front_matter_key("注意：这不是 YAML", "title"));
    CHECK_FALSE(texttool::markdown::is_front_matter_key("date", "date"));
}

TEST_CASE("头部字段：开门行要去空白、字前空白也算，收尾行是三个短横或三点") {
    CHECK(texttool::markdown::is_front_matter_opening("---"));
    CHECK(texttool::markdown::is_front_matter_opening(" --- "));
    CHECK(texttool::markdown::is_front_matter_opening("\xEF\xBB\xBF---"));
    CHECK_FALSE(texttool::markdown::is_front_matter_opening("----"));
    CHECK(texttool::markdown::is_front_matter_closing("---"));
    CHECK(texttool::markdown::is_front_matter_closing("..."));
    CHECK_FALSE(texttool::markdown::is_front_matter_closing(".."));
}

TEST_CASE("指令行：行首双冒号，单冒号与缩进过的都算准") {
    CHECK(texttool::markdown::is_directive(":::[info] 提示"));
    CHECK(texttool::markdown::is_directive(":: end"));
    CHECK_FALSE(texttool::markdown::is_directive(": 单冒号"));
}

TEST_CASE("不是博文就一个字都不遮：文档站的头部字段与行内代码都要照原样检查") {
    // 只有 title，没有 date：VuePress 那类文档站的头部字段长这样
    const std::string doc = "---\ntitle: 文档\n---\n\n正文里的 `代码` 要检查\n";
    const BlogMask mask = BlogMask::create(doc);
    CHECK_FALSE(mask.is_blog_post());
    CHECK(mask.masked_chars() == 0);
    CHECK(mask.apply(doc) == doc);

    // 三个短横当分隔线的纯文本：后面那段不是头部字段
    const std::string plain = "---\n\n正文第一段\n\n`代码` 照旧\n";
    CHECK_FALSE(BlogMask::create(plain).is_blog_post());
}

TEST_CASE("title 与 date 同在才算博文，头部字段整段连换行一起遮") {
    const std::string text =
        "---\ntitle: 一份博文\ndate: 2024-01-01\n---\n"
        "正文一句。\n";
    const BlogMask mask = BlogMask::create(text);
    REQUIRE(mask.is_blog_post());

    const std::string head = "---\ntitle: 一份博文\ndate: 2024-01-01\n---\n";
    CHECK(mask.masked_chars() == head.size());
    CHECK(outline(mask, text) == "[" + head + "]正文一句。\n");
    CHECK(mask.is_masked(head.size() - 1));   // 收尾那行的换行也算被遮
    CHECK_FALSE(mask.is_masked(head.size())); // 正文第一个字不遮
}

TEST_CASE("指令行让一份稿子算博文，整行连换行一起遮") {
    const std::string text = "正文一句。\n\n:::[info] 这一行不替换\n\n正文二句。\n";
    const BlogMask mask = BlogMask::create(text);
    REQUIRE(mask.is_blog_post());
    CHECK(outline(mask, text) == "正文一句。\n\n[:::[info] 这一行不替换\n]\n正文二句。\n");
}

TEST_CASE("围栏代码块整块遮，块里的行内代码与链接一并算在内") {
    const std::string text =
        ":: end\n正文\n\n```csharp\nvar a = 1, b = `x`;\n```\n正文二\n";
    const BlogMask mask = BlogMask::create(text);
    REQUIRE(mask.is_blog_post());
    CHECK(outline(mask, text) ==
          "[:: end\n]正文\n\n[```csharp\nvar a = 1, b = `x`;\n```\n]正文二\n");
}

TEST_CASE("行内代码只遮成对的那一串，反引号个数要配得上") {
    const std::string text = ":: end\n行内 `a, b` 与落单的 ` 一个\n两个反引号 ``x`y``\n";
    const BlogMask mask = BlogMask::create(text);
    REQUIRE(mask.is_blog_post());
    CHECK(outline(mask, text) ==
          "[:: end\n]行内 [`a, b`] 与落单的 ` 一个\n两个反引号 [``x`y``]\n");
}

TEST_CASE("链接只遮地址，方括号里的字照旧能检查") {
    const std::string text = ":: end\n看 [这里的文字](http://example.com/a,b) 与 ![图](pic.png)\n";
    const BlogMask mask = BlogMask::create(text);
    REQUIRE(mask.is_blog_post());
    CHECK(outline(mask, text) ==
          "[:: end\n]看 [这里的文字][(http://example.com/a,b)] 与 ![图][(pic.png)]\n");
}

TEST_CASE("自动链接整段遮，普通尖括号不动") {
    const std::string text = ":: end\n看 <https://example.com/a,b> 与 <不是链接>\n";
    const BlogMask mask = BlogMask::create(text);
    REQUIRE(mask.is_blog_post());
    CHECK(outline(mask, text) ==
          "[:: end\n]看 [<https://example.com/a,b>] 与 <不是链接>\n");
}

TEST_CASE("公式：行内公式与整块公式都遮，货币金额不遮") {
    const std::string text =
        ":: end\n行内 $a,b$ 与金额 $5 与 $10\n$$\nx = 1, y = 2\n$$\n";
    const BlogMask mask = BlogMask::create(text);
    REQUIRE(mask.is_blog_post());
    CHECK(outline(mask, text) ==
          "[:: end\n]行内 [$a,b$] 与金额 $5 与 $10\n[$$\nx = 1, y = 2\n$$\n]");
}

TEST_CASE("跨行的 HTML 注释连着遮到收尾") {
    const std::string text = ":: end\n正文 <!-- 注释开头\n还在注释里, 带逗号\n--> 之后\n";
    const BlogMask mask = BlogMask::create(text);
    REQUIRE(mask.is_blog_post());
    // 注释没在这一行收尾：这一行的尾巴遮到行末，换行不遮；后面的行整行遮，包括收尾那行
    CHECK(outline(mask, text) ==
          "[:: end\n]正文 [<!-- 注释开头]\n[还在注释里, 带逗号\n--> 之后\n]");
}

TEST_CASE("引用式链接定义整行遮") {
    const std::string text = ":: end\n见 [名字][ref]\n\n[ref]: http://example.com/a,b\n";
    const BlogMask mask = BlogMask::create(text);
    REQUIRE(mask.is_blog_post());
    CHECK(outline(mask, text) == "[:: end\n]见 [名字][ref]\n\n[[ref]: http://example.com/a,b\n]");
}

TEST_CASE("遮罩的 apply 把被遮的字换成空格，换行留着") {
    const std::string text = ":: x\n正文 `代` 尾\n";
    const BlogMask mask = BlogMask::create(text);
    REQUIRE(mask.is_blog_post());
    // 指令行那四个字变成空格、它的换行留着；行内代码「`代`」五个字节也变成空格
    CHECK(mask.apply(text) == std::string("    \n") + "正文 " + std::string(5, ' ') + " 尾\n");
}

TEST_CASE("空稿与没有换行的单行都不崩") {
    CHECK_FALSE(BlogMask::create("").is_blog_post());
    CHECK_FALSE(BlogMask::create("一句正文").is_blog_post());

    const BlogMask mask = BlogMask::create(":: 只有一行的指令");
    REQUIRE(mask.is_blog_post());
    CHECK(mask.masked_chars() == std::string(":: 只有一行的指令").size());
}

namespace {

// 逐行状态机遮出来的样子：一个字节一个字符，被遮的写 1，没遮的写 0。
// chunk 是喂进去的块有多大：块边界落在哪都不该改变结果。
std::string scan_flags(std::string_view text, std::size_t chunk) {
    std::string flags;
    flags.reserve(text.size());
    BlogMaskScanner scanner;
    const auto mark_line = [&flags](const BlogMaskScanner::LineMask& line) {
        const std::size_t width = line.line.size() + (line.has_newline ? 1 : 0);
        std::string current(width, '0');
        if (line.whole_line) {
            current.assign(width, '1');
        } else {
            for (const BlogMaskScanner::Span& span : line.spans)
                for (std::size_t at = span.begin; at < span.end; ++at) current[at] = '1';
        }
        flags += current;
    };
    for (std::size_t at = 0; at < text.size(); at += chunk)
        scanner.feed(text.substr(at, std::min(chunk, text.size() - at)), mark_line);
    scanner.finish(mark_line);
    return flags;
}

// 整份建遮罩的样子，画法与上面一样。
std::string mask_flags(const BlogMask& mask, std::string_view text) {
    std::string flags;
    flags.reserve(text.size());
    for (std::size_t at = 0; at < text.size(); ++at) flags += mask.is_masked(at) ? '1' : '0';
    return flags;
}

bool scan_says_blog_post(std::string_view text, std::size_t chunk) {
    BlogMaskScanner scanner;
    const auto skip = [](const BlogMaskScanner::LineMask&) {};
    for (std::size_t at = 0; at < text.size(); at += chunk)
        scanner.feed(text.substr(at, std::min(chunk, text.size() - at)), skip);
    scanner.finish(skip);
    return scanner.is_blog_post();
}

// 开头是 --- 的那份稿子：前 head_lines 行是正文，之后才出现 title 键。
std::string front_matter_delayed(std::size_t head_lines) {
    std::string text = "---\n";
    for (std::size_t line = 0; line < head_lines; ++line) text += "正文一行，逗号\n";
    text += "title: 一份博文\ndate: 2024-01-01\n---\n正文，一句\n";
    return text;
}

}  // namespace

TEST_CASE("逐行状态机与整份遮罩逐字节相同：块切在哪里都不影响结果") {
    const std::vector<std::string> docs = {
        "",
        "\n",
        "一段没有博文结构的正文，带逗号\n第二行\n",
        "---\ntitle: 一份博文\ndate: 2024-01-01\n---\n正文，一句\n\n```\ncode, a\n```\n"
        "行内 `a, b` 与链接 [字](http://x/a,b)\n:::[info] 指令行\n",
        "---\ntitle: 围栏没关\ndate: 2024-01-01\n---\n正文\n```\ncode\n行内 `a, b`\n",
        "---\ntitle: 注释跨段\ndate: 2024-01-01\n---\n正文\n<!-- 注释从这里开始\n还在注释里\n-->\n"
        "注释外面\n",
        "---\ntitle: 末尾无换行\ndate: 2024-01-01\n---\n最后一行没有换行",
        "---\n标题：没有 YAML 键\n---\n分隔线后面是正文\n",
        "---\ntitle: 块公式\ndate: 2024-01-01\n---\n$$\na, b\n$$\n引用式 [名字]: http://x/a,b\n",
        "甲\r\n乙\r\n\r\n丙。\r\n",
        front_matter_delayed(3),
    };

    int checked = 0;
    for (const std::string& doc : docs) {
        const BlogMask mask = BlogMask::create(doc);
        for (const std::size_t chunk : {std::size_t{1}, std::size_t{2}, std::size_t{3},
                                       std::size_t{7}, std::size_t{64}, std::size_t{4096}}) {
            CHECK_MESSAGE(scan_flags(doc, chunk) == mask_flags(mask, doc),
                          "按 " << chunk << " 字节一块时遮出来的地方与整份不一样");
            CHECK(scan_says_blog_post(doc, chunk) == mask.is_blog_post());
            ++checked;
        }
    }
    MESSAGE("[逐行遮罩] 比了 " << checked << " 次");
    CHECK(checked > 0);
}

TEST_CASE("开头是 --- 时往后看两百行：键值行在第二百行算，在第二百零一行不算") {
    // 与整份看全文时同一个窗口：开门那行之后数两百行。
    const std::string inside = front_matter_delayed(199);   // title 在第 200 行
    const std::string outside = front_matter_delayed(200);  // title 在第 201 行
    CHECK(BlogMask::create(inside).is_blog_post());
    CHECK_FALSE(BlogMask::create(outside).is_blog_post());
    for (const std::size_t chunk : {std::size_t{1}, std::size_t{5}, std::size_t{4096}}) {
        CHECK(scan_says_blog_post(inside, chunk));
        CHECK_FALSE(scan_says_blog_post(outside, chunk));
        CHECK(scan_flags(inside, chunk) == mask_flags(BlogMask::create(inside), inside));
        CHECK(scan_flags(outside, chunk) == mask_flags(BlogMask::create(outside), outside));
    }
}
