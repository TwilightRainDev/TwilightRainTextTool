// 匹配层的测试。
//
// 校验一律拿「命中的那段文字」比，不比字节偏移：稿件是 UTF-8，偏移是人眼看不出的数字，
// 写成偏移就得跟着多字节字符重新数一遍，改一个字全要重算。
// 唯一直接校验偏移的是「返回的区间确实按字节算」这一条。
#include "doctest/doctest.h"

#include "engine/regex.h"
#include "engine/rules.h"

#include <chrono>
#include <cstdio>
#include <cstdlib>
#include <string>
#include <string_view>
#include <vector>

using texttool::MatchLimits;
using texttool::MatchRange;
using texttool::Pattern;

namespace {

// 找到的第一处命中对应的原文；没命中返回 "<无命中>"。
std::string hit(std::string_view re, std::string_view hay, std::size_t from = 0) {
    Pattern pattern(re);
    MatchRange range = pattern.search(hay, from, MatchLimits{});
    if (range.begin == texttool::kNoMatch) return "<无命中>";
    return std::string(hay.substr(range.begin, range.end - range.begin));
}

// 把 [from, 末尾) 里所有命中的原文收出来，模拟上层「从 last_end 继续」的扫法。
std::vector<std::string> hits(std::string_view re, std::string_view hay) {
    Pattern pattern(re);
    std::vector<std::string> out;
    std::size_t from = 0;
    while (from <= hay.size()) {
        MatchRange range = pattern.search(hay, from, MatchLimits{});
        if (range.begin == texttool::kNoMatch) break;
        out.push_back(std::string(hay.substr(range.begin, range.end - range.begin)));
        from = range.end > range.begin ? range.end : range.begin + 1;
    }
    return out;
}

// 15MB 量级的合成中文稿件：段落、标点连打、代码片段、编号、链接混在一起，
// 让各种模式都有真东西可扫，不是一片同一种字符。
std::string make_draft(std::size_t target_bytes) {
    static const char* kSentences[] = {
        "这不是技术问题而是流程问题。",
        "他走了。。。真的走了。",
        "本次，我们把这件事做完。",
        "好像昨天一样，什么都没变。",
        "RTO 是恢复目标时间，rpo 是恢复点目标。",
        "见 Task #42 和 T-123 节。",
        "提交 3f9a2b7 修好了这个问题。",
        "加上 --dry-run 试试。",
        "1苹果、1香蕉、1橘子。",
        "如果像往常一样，那就没有变化。",
        "文档 第 3 条 说得很清楚。",
        "参考 https://example.com/a/b/…/c 这个路径。",
        "标题 -- 副标题，草，这不行。",
        "诗曰：山中何事，松花酿酒。",
        "把口径对齐，再落盘。",
        "他说：\"这件事该挨打！\"",
        "| 0 | - |",
        "- **重点**：不要重复。",
        "……省略号与—破折号。",
        "真的??这样也行。",
        "无\u200b断点，全角\u3000空格。",
        "中文×符号，αβγ，안녕。",
    };
    constexpr std::size_t kSentenceCount = sizeof(kSentences) / sizeof(kSentences[0]);

    std::string draft;
    draft.reserve(target_bytes + 4096);
    std::size_t round = 0;
    while (draft.size() < target_bytes) {
        for (std::size_t i = 0; i < kSentenceCount && draft.size() < target_bytes; ++i) {
            draft += kSentences[(i + round) % kSentenceCount];
            if ((i + round) % 7 == 0) draft += '\n';
            if ((i + round) % 37 == 0) draft += "\n\n";
        }
        ++round;
    }
    return draft;
}

}  // namespace

TEST_CASE("字面量：中文与英文都按原样找") {
    CHECK(hit("重复标点", "关于重复标点的说明") == "重复标点");
    CHECK(hit("RTO", "RTO 是恢复目标时间") == "RTO");
    CHECK(hit("找不到的字", "完全不相关的文本") == "<无命中>");
}

TEST_CASE("点号：只吃掉一个非换行字符") {
    CHECK(hit("A.C", "xAyC") == "AyC");
    CHECK(hit("A.C", "xABBC") == "<无命中>");  // 点号只吃一个字符
    CHECK(hit("A.C", "A\nC") == "<无命中>");  // 点号不跨行
    CHECK(hit("中.C", "x中yC") == "中yC");      // 中文按整字符算
}

TEST_CASE("字符类：区间、取反、类内转义与首尾的短横") {
    CHECK(hit("[abc]+", "xxabcabx") == "abcab");
    CHECK(hit("[a-c]+", "xxabcabx") == "abcab");
    CHECK(hit("[^abc]+", "abcxyzabc") == "xyz");
    CHECK(hit("[\\u4e00-\\u9fff]+", "ab中文cd") == "中文");
    CHECK(hit("[\\]\\[()]", "a]b") == "]");
    CHECK(hit("[a-]+", "xx---y") == "---");      // 末尾的 - 是字面量
    CHECK(hit("[.]", "a.b") == ".");             // 类里的点号是字面量
    CHECK(hit("[^\\n]+", "ab\ncd") == "ab");
}

TEST_CASE("量词：* + ? {n,m} 与惰性") {
    CHECK(hit("ab*c", "abbbc") == "abbbc");
    CHECK(hit("ab+c", "ac") == "<无命中>");
    CHECK(hit("ab?c", "ac") == "ac");
    CHECK(hit("a{2,4}", "aaaaa") == "aaaa");
    CHECK(hit("a{2}", "aaa") == "aa");
    CHECK(hit("a{2,}", "aaaa") == "aaaa");
    // 惰性：先吃最少，够后续就行
    CHECK(hit("<.+?>", "<a><b>") == "<a>");
    CHECK(hit("<.+>", "<a><b>") == "<a><b>");
    CHECK(hit("不是[^。！？\\n]{1,25}?而是", "不是甲而乙而是") == "不是甲而乙而是");
}

TEST_CASE("分组：捕获与非捕获都能用") {
    CHECK(hit("(ab)+", "ababab") == "ababab");
    CHECK(hit("(?:ab)+c", "ababc") == "ababc");
    CHECK(hit("(a|bc)d", "bcd") == "bcd");
    CHECK(hit("(?:(?:a)b)c", "abc") == "abc");
}

TEST_CASE("反向引用：连续重复的标点") {
    CHECK(hit("([。，、？！；：～])\\1+", "他走了。。。真的。") == "。。。");
    CHECK(hit("([!?])\\1+", "什么??这样") == "??");
    CHECK(hit("([!?])\\1+", "什么?!这样") == "<无命中>");
    CHECK(hits("([。])\\1+", "。。x。。") == std::vector<std::string>({"。。", "。。"}));
}

TEST_CASE("环视：四种都要在对的位置上成立") {
    CHECK(hit("(?<!…)—(?!—)", "他—说") == "—");
    CHECK(hit("(?<!…)—(?!—)", "…—好") == "<无命中>");          // 反向否定环视挡住
    CHECK(hit("(?<!…)…(?!…)", "他说…这样") == "…");
    CHECK(hit("(?<!…)…(?!…)", "他说……这样") == "<无命中>");      // 后面还有…，否定前瞻挡住
    CHECK(hit("foo(?=bar)", "xxfoobar") == "foo");
    CHECK(hit("foo(?=bar)", "xxfoobaz") == "<无命中>");
    CHECK(hit("foo(?!bar)", "xxfoobaz") == "foo");
    CHECK(hit("(?<=ab)c", "abc") == "c");
    CHECK(hit("(?<=ab)c", "xbc") == "<无命中>");
    CHECK(hit("(?<!ab)c", "xbc") == "c");
    CHECK(hit("(?<=^|\\n)[ \\t]*草[，,]", "草，这不行") == "草，");
    CHECK(hit("(?<=^|\\n)[ \\t]*草[，,]", "其实草，不行") == "<无命中>");
    CHECK(hit("(?<=^|\\n)[ \\t]*草[，,]", "上一行\n  草，不行") == "  草，");
}

TEST_CASE("分支、锚点与内联多行") {
    CHECK(hit("cat|dog", "a cat") == "cat");
    CHECK(hit("cat|dog", "a cow") == "<无命中>");
    CHECK(hit("^abc", "abcdef") == "abc");
    CHECK(hit("^abc", "xabcdef") == "<无命中>");     // 不给 (?m) 时 ^ 只认全文开头
    CHECK(hit("(?m)^(本轮|本次|这次)[，,]", "上一行\n本次，我们来。") == "本次，");
    CHECK(hit("(?m)^\\s*[-*]?\\s*暂无。?\\s*$", "第一行\n  - 暂无。\n第三行") == "  - 暂无。");
    CHECK(hit("(?m)^\\s*[-*]?\\s*暂无。?\\s*$", "第一行\n  有内容\n第三行") == "<无命中>");
    CHECK(hit("abc$", "xxabc") == "abc");
    CHECK(hit("(?m)abc$", "xxabc\nzzz") == "abc");
}

TEST_CASE("词边界 \\b \\B 与内联大小写") {
    CHECK(hit("\\bRTO\\b", "RTO 是恢复目标时间") == "RTO");
    CHECK(hit("\\bRTO\\b", "XRTOX 不是") == "<无命中>");
    CHECK(hit("(?i)\\bRTO\\b", "rto 是恢复目标时间") == "rto");
    CHECK(hit("(?i)RTO", "rto 是恢复目标时间") == "rto");
    CHECK(hit("(?i:smoke)|冒烟|烟测", "跑了一遍 SMOKE 测试") == "SMOKE");
    CHECK(hit("(?i:smoke)|冒烟|烟测", "跑了一遍冒烟测试") == "冒烟");
    CHECK(hit("a\\Bb", "abc") == "ab");
    CHECK(hit("a\\Bb", "a b") == "<无命中>");
    CHECK(hit("\\b中文", "中文写作") == "中文");
    CHECK(hit("\\b中文", "用中文写作") == "<无命中>");  // 相邻汉字都是词内字符，没有边界
}

TEST_CASE("转义类与字面量的转义") {
    CHECK(hit("\\d+", "第 42 条") == "42");
    CHECK(hit("\\D+", "42abc") == "abc");
    CHECK(hit("\\w+", "  --dry-run ") == "dry");
    CHECK(hit("\\W+", "ab, cd") == ", ");
    CHECK(hit("\\s+", "a\t\v b") == "\t\v ");
    CHECK(hit("\\S+", "  abc  ") == "abc");
    CHECK(hit("\\.", "a.b") == ".");
    CHECK(hit("\\*", "2*3") == "*");
    CHECK(hit("\\(x\\)", "a(x)b") == "(x)");
    CHECK(hit("a\\|b", "a|b") == "a|b");
    CHECK(hit("\\[\\]", "a[]b") == "[]");
}

TEST_CASE("Unicode 转义，包括成对的代理项") {
    CHECK(hit("\\u200B", "无\u200b断点") == "\u200b");
    CHECK(hit("[\\u4e00-\\u9fff]", "ab中cd") == "中");
    // .NET 在 UTF-16 上跑，这两个模式是在认「一个完整的表情」。字节侧按码点认同一个字符。
    CHECK(hit("\\uD83C[\\uDF00-\\uDFFF]", "表情\U0001F300结束") == "\U0001F300");
    CHECK(hit("\\uD83D[\\uDC00-\\uDEFF]", "表情\U0001F600结束") == "\U0001F600");
    CHECK(hit("\\uD83C[\\uDF00-\\uDFFF]", "纯中文没有表情") == "<无命中>");
    // 类里的整个代理区被排除，等价于「不要增补平面字符」
    CHECK(hit("[^\\u4e00-\\u9fff\\uD800-\\uDFFF]", "中\U0001F300x") == "x");
}

TEST_CASE("from 参数：从上次结束处继续，允许重叠") {
    Pattern pattern("哈哈");
    std::string draft = "哈哈哈";
    MatchRange first = pattern.search(draft, 0, MatchLimits{});
    REQUIRE(first.begin != texttool::kNoMatch);
    CHECK(std::string(draft.substr(first.begin, first.end - first.begin)) == "哈哈");
    // 「哈哈」占两个字符六个字节，从第二个字符的字节位置继续还能再命中一次
    MatchRange second = pattern.search(draft, first.begin + 3, MatchLimits{});
    REQUIRE(second.begin != texttool::kNoMatch);
    CHECK(second.begin == 3);
    CHECK(second.end == 9);
    // 区间按字节算
    CHECK(first.begin == 0);
    CHECK(first.end == 6);
    // 扫到末尾之后没有命中
    CHECK(pattern.search(draft, draft.size(), MatchLimits{}).begin == texttool::kNoMatch);
}

TEST_CASE("步数上限：恶意模式必须响亮报错，不静默返回没匹配") {
    // 一条真正会爆回溯的模式：一串 a 之后没有 b，(a+)+b 要把所有切法试一遍。
    std::string draft(28, 'a');
    Pattern pattern("(a+)+b");
    MatchLimits limits;
    limits.steps = 2000;
    try {
        pattern.search(draft, 0, limits);
        FAIL("应当抛 std::runtime_error");
    } catch (const std::runtime_error& error) {
        std::string message = error.what();
        CHECK(message.find("(a+)+b") != std::string::npos);  // 消息里带模式原文
    }
    // 文本短到手算得过来时，同一条模式能正常给出「没匹配」，说明不是模式写错了
    Pattern same("(a+)+b");
    std::string short_draft(12, 'a');
    CHECK(same.search(short_draft, 0, MatchLimits{}).begin == texttool::kNoMatch);
    REQUIRE(short_draft.size() == 12);
    if (draft.size() != 28) FAIL("这段文本应当有 28 个 a");
}

TEST_CASE("真实规则表里的模式：字面命中与不命中都对得上 .NET") {
    struct Case {
        const char* pattern;
        const char* text;
        const char* expect;
    };
    const Case cases[] = {
        {"([。，、？！；：～])\\1+", "他走了。。。真的。", "。。。"},
        {"(.)[^、，。；：！？\\n]{1,7}、(?:\\1[^、，。；：！？\\n]{1,7}、)+\\1[^、，。；：！？\\n]{1,7}",
         "1苹果、1香蕉、1橘子", "1苹果、1香蕉、1橘子"},
        {"(?m)^\\s*[-*]?\\s*暂无。?\\s*$", "第一行\n  - 暂无。\n第三行", "  - 暂无。"},
        {"(?m)^(本轮|本次|这次)[，,]", "上一行\n本次，我们来。", "本次，"},
        {"(?m)^\\s*\\|\\s*0\\s*\\|\\s*[—\\-]?\\s*\\|\\s*$", "| 0 | - |", "| 0 | - |"},
        {"(?i)\\bRTO\\b", "rto 是恢复目标时间", "rto"},
        {"(?i)\\bRPO\\b", "RPO 是恢复点目标", "RPO"},
        {"(?i)fixture", "这是 FIXTURE 文件", "FIXTURE"},
        {"(?i)roll ?back", "先 ROLLBACK 再重来", "ROLLBACK"},
        {"不是[^。！？\\n]{1,25}?而是", "这不是技术问题而是流程问题。", "不是技术问题而是"},
        {"(?:像|好像)(?!往常|以前|从前|平时|过去)[^。！？\\n]{0,12}?(?:一样|似的|一般)",
         "好像昨天一样。", "好像昨天一样"},
        {"(?:像|好像)(?!往常|以前|从前|平时|过去)[^。！？\\n]{0,12}?(?:一样|似的|一般)",
         "像往常一样。", "<无命中>"},
        {"(?<![结清核对分])算[^。！？\\n]{0,8}账(?![户单号簿面款项目])", "把这几笔账算清楚账", "算清楚账"},
        {"(?<![A-Za-z0-9])[A-Z]{1,2}[-_\\u2013]?\\d{1,4}(?![0-9A-Za-z])", "见 T-123 节", "T-123"},
        {"(?<![0-9a-fA-F])(?=[0-9a-f]{7,40}(?![0-9a-fA-F]))[0-9a-f]*[a-f][0-9a-f]*",
         "提交 3f9a2b7 修好", "3f9a2b7"},
        {"(?<![0-9a-fA-F])(?=[0-9a-f]{7,40}(?![0-9a-fA-F]))[0-9a-f]*[a-f][0-9a-f]*",
         "编号 1234567 不变", "<无命中>"},
        {"(?<!\\w)--[a-z][a-z-]{1,20}", "加上 --dry-run 试试", "--dry-run"},
        {"\\bTask\\s*#?\\d{1,3}\\b", "见 Task #42 里", "Task #42"},
        {"[\\u4e00-\\u9fff] ?--+ ?[\\u4e00-\\u9fff]", "标题 -- 副标题", "题 -- 副"},
        {"该挨打[。！!）)\\uff08(]|真该挨打", "这点该挨打！", "该挨打！"},
        {"[,;:?!()][\\u3400-\\u4DBF\\u4E00-\\u9FFF\\uF900-\\uFAFF\\u3000-\\u303F\\uFF00-\\uFFEF]",
         "你好,世界", ",世"},
        {"\\u200B", "无\u200b断点", "\u200b"},
        {"(均已|已经|已)[\\u4e00-\\u9fa5]{1,4}(了|完毕)", "任务已经完成了", "已经完成了"},
        {"[\\u0400-\\u04FF]+", "这是中文 α 不是西里尔", "<无命中>"},
        {"[\\u0370-\\u03FF]+", "希腊字母 αβγ 在这里", "αβγ"},
    };
    for (const Case& item : cases) {
        INFO("模式：" << item.pattern << "  文本：" << item.text);
        CHECK(hit(item.pattern, item.text) == item.expect);
    }
}

TEST_CASE("规则表里全部 467 个模式都能编译，且不会一开工就抛") {
    std::vector<texttool::LintRule> rules = texttool::load_rules(TEXTTOOL_RULES_JSON);
    std::size_t compiled = 0;
    for (const texttool::LintRule& rule : rules) {
        if (rule.kind != texttool::RuleKind::Regex) continue;
        for (const std::string& text : rule.patterns) {
            INFO("规则 " << rule.id << " 模式 " << text);
            Pattern pattern(text);
            CHECK(pattern.text() == text);
            ++compiled;
        }
    }
    CHECK(compiled == 467);
}

TEST_CASE("写坏的模式在构造时就报错，消息里带原文") {
    CHECK_THROWS_AS(Pattern("(abc"), std::runtime_error);
    CHECK_THROWS_AS(Pattern("a{2,1}"), std::runtime_error);
    CHECK_THROWS_AS(Pattern("*abc"), std::runtime_error);
    CHECK_THROWS_AS(Pattern("a\\q"), std::runtime_error);
    try {
        Pattern broken("(abc");
        FAIL("应当抛 std::runtime_error");
    } catch (const std::runtime_error& error) {
        CHECK(std::string(error.what()).find("(abc") != std::string::npos);
    }
    // 空模式匹配空串，是有定义的行为，不算写坏
    CHECK(hit("", "abc") == "");
}

TEST_CASE("可空的模式：空命中也是命中") {
    CHECK(hit("a*", "bbb") == "");
    CHECK(hit("a*", "aaab") == "aaa");
    CHECK(hit("(?m)^\\s*$", "a\n\n b") == "");
    CHECK(hit("\\s*$", "ab cd   ") == "   ");
    CHECK(hit("ab|c", "c") == "c");
}

TEST_CASE("预筛：前缀查找与长串跳过不会漏掉真命中") {
    // 字面前缀预筛：命中在文本靠后的位置
    std::string late = std::string(20000, 'x') + "目标短语";
    CHECK(hit("目标短语", late) == "目标短语");
    CHECK(hit("目标短语", std::string(20000, 'x')) == "<无命中>");
    // 领头长串跳过：串中间的起点要能命中，串首失败不能把整段跳过头
    CHECK(hit("[a-z]+@", "abc@x") == "abc@");
    CHECK(hit("[a-z]+@", "aaa bbb") == "<无命中>");
    CHECK(hit("[A-Za-z0-9_./-]+\\.(?:cs|md)", "ab.md ab") == "ab.md");
    CHECK(hit("[A-Za-z0-9_./-]+\\.(?:cs|md)", "ab.zz ab") == "<无命中>");
    // 从串里靠后的位置继续扫时也要能找到
    Pattern pattern("[a-z]+@");
    std::string two = "aaa bbb@";
    MatchRange found = pattern.search(two, 4, MatchLimits{});
    REQUIRE(found.begin != texttool::kNoMatch);
    CHECK(two.substr(found.begin, found.end - found.begin) == "bbb@");
}

TEST_CASE("15MB 中文稿件上跑一批真实模式，不崩、不超时") {
    const std::string draft = make_draft(15u * 1024 * 1024);
    REQUIRE(draft.size() >= 15u * 1024 * 1024);
    const char* patterns[] = {
        "([。，、？！；：～])\\1+",
        "不是[^。！？\\n]{1,25}?而是",
        "(?m)^(本轮|本次|这次)[，,]",
        "(?i)\\bRTO\\b",
        "(?<![A-Za-z0-9])[A-Z]{1,2}[-_\\u2013]?\\d{1,4}(?![0-9A-Za-z])",
        "[\\u4e00-\\u9fff][ \\t]*[A-Za-z]{3,}[ \\t]*[\\u4e00-\\u9fff]",
        "(?<![A-Za-z0-9/._~:@+%#\\u2026-])(?=[A-Za-z0-9/._~:@+%#\\u2026-]*/)(?=[A-Za-z0-9/._~:@+%#\\u2026-]*(?:\\u2026|\\.{3}))[A-Za-z0-9/._~:@+%#\\u2026-]+",
        "不存在的长串文字在稿子里根本没有",
    };
    for (const char* text : patterns) {
        INFO("模式 " << text);
        Pattern pattern(text);
        MatchRange range = pattern.search(draft, 0, MatchLimits{});
        CHECK((range.begin == texttool::kNoMatch || range.end <= draft.size()));
    }
}

// 病态回溯与长串的两条硬指标：要么在上限内给结果，要么响亮停下。
// 每条规则的时限是 2 秒，这里照同一条线卡。
TEST_CASE("平方级慢模式在 200KB 上：两秒内做完，或者响亮停下") {
    // 全是同一种字符的长串，前面是一段无上限的字符类、后面接一个对不上的字符。
    // 每个起点都要把剩下的整串吃进去再一轮轮退回来，退一轮的成本与剩余长度成正比，
    // 起点又几乎每个字节都够格，合起来是平方级。这条模式就在规则表里。
    const std::string digits(200u * 1024, '1');
    Pattern pattern("\\d+\\.\\d+\\s*(?:%|克|毫米|厘米|升|秒|小时|倍)");
    const auto start = std::chrono::steady_clock::now();
    MatchRange range{texttool::kNoMatch, texttool::kNoMatch};
    bool stopped = false;
    try {
        range = pattern.search(digits, 0, MatchLimits{});
    } catch (const std::runtime_error& error) {
        stopped = true;
        CHECK(std::string(error.what()).find("\\d+\\.\\d+") != std::string::npos);
    }
    const double elapsed_ms = std::chrono::duration<double, std::milli>(
                                  std::chrono::steady_clock::now() - start).count();
    INFO("200KB 平方级用例耗时 " << elapsed_ms << " ms，报错停下：" << (stopped ? "是" : "否"));
    CHECK(elapsed_ms < 2000.0);
    if (!stopped) CHECK(range.begin == texttool::kNoMatch);
    // 量词带下界时用不上「整串跳过」，靠的就是扫描量上限：
    // 每个起点都要退几十万轮，同样必须在两秒内停下。
    const std::string letters(200u * 1024, 'a');
    Pattern bounded("[a-z]{1,200000}@");
    const auto bounded_start = std::chrono::steady_clock::now();
    bool bounded_stopped = false;
    try {
        bounded.search(letters, 0, MatchLimits{});
    } catch (const std::runtime_error&) {
        bounded_stopped = true;
    }
    const double bounded_ms = std::chrono::duration<double, std::milli>(
                                  std::chrono::steady_clock::now() - bounded_start).count();
    INFO("200KB 有界量词用例耗时 " << bounded_ms << " ms，报错停下：" << (bounded_stopped ? "是" : "否"));
    CHECK(bounded_ms < 2000.0);

    // 同一条模式在正常文本上照常命中，说明不是靠改模式躲过去的
    Pattern same("\\d+\\.\\d+\\s*(?:%|克|毫米|厘米|升|秒|小时|倍)");
    MatchRange real = same.search("涨了 12.5 克，比 3 小时前多一点。", 0, MatchLimits{});
    REQUIRE(real.begin != texttool::kNoMatch);
    CHECK(std::string("涨了 12.5 克，比 3 小时前多一点。").substr(real.begin, real.end - real.begin) ==
          "12.5 克");
}

// 四万个字节、每轮两个字节的量词套分组必须走到底：既不撞栈，
// 也不被轮数上限当成「量词套得太深」误杀。
TEST_CASE("长串上的量词套分组：走到底，既不撞栈也不被轮数上限误杀") {
    std::string text;
    while (text.size() < 40000) text += "ab";
    text.resize(40000);
    CHECK(hit("(?:ab)+", text) == text);
    CHECK(hit("(ab)+", text) == text);  // 捕获组一样要能走完
    // 整串吃完还差一个字符：两万轮一路退回来，也不能崩
    CHECK(hit("^(?:ab)+c", text) == "<无命中>");

    // 分支原子每轮都有两条路，走的是通用回溯那条线；两万个字符也要能退干净
    const std::string run(20000, 'a');
    CHECK(hit("^(?:a|b)+z", run) == "<无命中>");
    CHECK(hit("(?:a|b)+", run) == run);

    // 更长的串：上限只该由内存决定，不该由固定轮数决定
    std::string long_text;
    while (long_text.size() < 1u * 1024 * 1024) long_text += "ab";
    long_text.resize(1u * 1024 * 1024);
    CHECK(hit("(?:ab)+", long_text) == long_text);
}

// 15MB 上的验收指标，默认就跑：三条硬指标一起卡住，只打印不判定等于没测。
// 想连进程峰值一起看，可以套验收脚本：
//   python3 tools/measure_peak.py --limit 40 ./build-linux/engine_tests -tc="*15MB*"
TEST_CASE("467 个模式在 15MB 稿件上：总耗时、进程峰值、撞上限的条数") {
    const std::string draft = make_draft(15u * 1024 * 1024);
    REQUIRE(draft.size() >= 15u * 1024 * 1024);

    std::vector<texttool::LintRule> rules = texttool::load_rules(TEXTTOOL_RULES_JSON);
    std::vector<std::string> patterns;
    for (const texttool::LintRule& rule : rules) {
        if (rule.kind != texttool::RuleKind::Regex) continue;
        for (const std::string& text : rule.patterns) patterns.push_back(text);
    }
    REQUIRE(patterns.size() == 467);

    std::size_t slowest = 0;
    double slowest_ms = 0.0;
    std::size_t total_hits = 0;
    std::size_t over_limit = 0;
    std::string first_over;
    MatchLimits limits;  // 默认上限：真实模式一条都不该撞上
    const auto start = std::chrono::steady_clock::now();
    for (std::size_t index = 0; index < patterns.size(); ++index) {
        Pattern pattern(patterns[index]);
        const auto one_start = std::chrono::steady_clock::now();
        MatchRange range = texttool::MatchRange{texttool::kNoMatch, texttool::kNoMatch};
        try {
            range = pattern.search(draft, 0, limits);
        } catch (const std::runtime_error& error) {
            ++over_limit;
            if (first_over.empty()) first_over = error.what();
        }
        const double one_ms = std::chrono::duration<double, std::milli>(
                                  std::chrono::steady_clock::now() - one_start).count();
        if (range.begin != texttool::kNoMatch) ++total_hits;
        if (one_ms > slowest_ms) {
            slowest_ms = one_ms;
            slowest = index;
        }
    }
    const double total_ms = std::chrono::duration<double, std::milli>(
                                std::chrono::steady_clock::now() - start).count();

    // 峰值内存读的是内核记账的 VmHWM，与验收量的是同一项。
    std::size_t peak_kb = 0;
    if (FILE* status = std::fopen("/proc/self/status", "r")) {
        char line[256];
        while (std::fgets(line, sizeof(line), status) != nullptr) {
            std::size_t value = 0;
            if (std::sscanf(line, "VmHWM: %zu kB", &value) == 1) peak_kb = value;
        }
        std::fclose(status);
    }
    const double peak_mb = static_cast<double>(peak_kb) / 1024.0;

    std::printf("\n[15MB] 文本 %zu 字节，模式 %zu 条\n", draft.size(), patterns.size());
    std::printf("[15MB] 总耗时 %.1f ms，单模式最长 %.1f ms：%s\n", total_ms, slowest_ms,
                patterns[slowest].c_str());
    std::printf("[15MB] 有命中的模式 %zu 条，撞上默认上限 %zu 条%s%s\n", total_hits, over_limit,
                first_over.empty() ? "" : "，第一条：", first_over.c_str());
    std::printf("[15MB] 进程峰值 RSS %.2f MB（VmHWM）\n", peak_mb);

    CHECK(total_ms < 10000.0);
    CHECK(peak_mb < 40.0);
    CHECK(over_limit == 0);
}

// ---------------------------------------------------------------- 两个实测出来的缺陷
//
// 一、环视与空白口子挡在字面前面时，候选位置靠什么定。
// 规则表里这条模式（开口先认同）以两支环视开头，后面是一串可选的空白，再往后才是那串
// 备选词。备选词是必须出现的字面，定位本该从它下手；没有这一步，15MB 的稿子要逐字节
// 试一遍，每次十几步，几千万步之后撞上限，正常内容被当成病态模式叫停。
//
// 二、步数上限管的是哪件事。
// 按字节线（一个字节一次试）走的扫描与稿件长度成正比，同一份稿子从 1MB 涨到 15MB，
// 步数也跟着涨十几倍。上限卡死一个常数，就等于「稿子越长越容易被误杀」。
TEST_CASE("空白与环视挡在字面前面的模式：长稿上照常给结果，不撞步数上限") {
    const char* pattern =
        "(?:(?<=^)|(?<=[。！？\n”」]))[ \\t]*(?:你说得对|你说得没错|你说得不错|确实如此|你说得有道理)";

    // 命中落在哪：起点是「前一个字是句末标点」的那一格，中间的空格与制表符跟着一起报出来
    CHECK(hit(pattern, "他停了一下。你说得对，我改。") == "你说得对");
    CHECK(hit(pattern, "你说得对，从头说。") == "你说得对");
    CHECK(hit(pattern, "他说完就走了。  \t你说得对。") == "  \t你说得对");
    CHECK(hit(pattern, "这句话里没有那几个说法。") == "<无命中>");

    // 多处命中：逐个扫下来，位置与原文都要对
    const std::string draft = "第一段。你说得对。第二段！确实如此，那就这样。第三段说完了。";
    const std::vector<std::string> found = hits(pattern, draft);
    REQUIRE(found.size() == 2);
    CHECK(found[0] == "你说得对");
    CHECK(found[1] == "确实如此");

    // 稿子里一个备选词都没有的长稿：整份扫完，一处不报。
    // 稿件里空格成堆（缩进、表格、命令行），这正是老路子吃亏的地方：
    // 空白能当起点，于是每一个空格都成了候选点，每个候选点还要把十几支备选词试一遍。
    // 这里把「按长度放大的那一份预算」关掉，只留很小的底数：能不能给出结果，
    // 全看候选位置是不是从必须出现的字面定出来的。
    std::string long_draft;
    std::size_t line = 0;
    while (long_draft.size() < 1u * 1024 * 1024) {
        long_draft += "        第 " + std::to_string(++line) +
                      " 行  前面有一串空格  中间也有几处  内容照常写着，没有那几个说法。\n";
    }
    Pattern compiled(pattern);
    MatchLimits strict;
    strict.steps = 200000;
    strict.steps_per_byte = 0;
    MatchRange range{texttool::kNoMatch, texttool::kNoMatch};
    CHECK_NOTHROW(range = compiled.search(long_draft, 0, strict));
    CHECK(range.begin == texttool::kNoMatch);

    // 同一份长稿上把备选词放进去：一处都不许漏
    const std::string somewhere =
        long_draft.substr(0, 512 * 1024) + "。你说得对，我改。" + long_draft.substr(512 * 1024);
    CHECK_NOTHROW(range = compiled.search(somewhere, 0, strict));
    CHECK(range.begin != texttool::kNoMatch);
}

TEST_CASE("步数预算管的是长度带来的线性开销，病态回溯照样叫停") {
    // 上限 = 底数 + 每字节若干步：同一份稿件越长，允许的步数越多；
    // 按字节线扫完一份稿子要试的起点数与稿件长度成正比，所以这一份必须随长度给。
    MatchLimits defaults;
    const std::size_t small_budget = texttool::step_budget(1u * 1024 * 1024, defaults);
    const std::size_t big_budget = texttool::step_budget(15u * 1024 * 1024, defaults);
    CHECK(small_budget == defaults.steps + defaults.steps_per_byte * 1u * 1024 * 1024);
    CHECK(big_budget > small_budget);
    CHECK(big_budget - small_budget == defaults.steps_per_byte * 14u * 1024 * 1024);
    // 底数为 0 也能用：只剩与长度成正比的那一份
    MatchLimits proportional;
    proportional.steps = 0;
    CHECK(texttool::step_budget(200u * 1024, proportional) ==
          proportional.steps_per_byte * 200u * 1024);

    // 与长度无关的病态回溯：底数给得再小也要响亮停下，消息里带模式原文
    std::string run(20000, 'a');
    Pattern bad("(a+)+b");
    try {
        bad.search(run, 0, MatchLimits{});
        FAIL("应当抛 std::runtime_error");
    } catch (const std::runtime_error& error) {
        CHECK(std::string(error.what()).find("(a+)+b") != std::string::npos);
    }
    // 把它放到更长的串上一样停：指数级回溯几万步就撞线，与稿件多长无关
    std::string longer(40000, 'a');
    CHECK_THROWS_AS(bad.search(longer, 0, MatchLimits{}), std::runtime_error);
}

TEST_CASE("点号开头、必需字面在后面：候选起点按命中串定，不逐字节试") {
    // 规则表里三条并列那条就是这样：开头一个点号，后面一串有界字符类，再后面才是顿号。
    // 顿号是命中里必然出现的那串字，候选起点只能落在它前面那一段里。
    const char* pattern =
        "(.)[^、，。；：！？\\n]{1,7}、(?:\\1[^、，。；：！？\\n]{1,7}、)+\\1[^、，。；：！？\\n]{1,7}";
    CHECK(hit(pattern, "1苹果、1香蕉、1橘子") == "1苹果、1香蕉、1橘子");
    CHECK(hit(pattern, "没有顿号的一段话，只有逗号。") == "<无命中>");

    // 命中落在靠后的位置：前面的顿号都要试过，起点对了才命中
    std::string late = "这一段没有顿号，只有逗号与句号。";
    for (int i = 0; i < 200; ++i) late += "又一句没有顿号的话。";
    late += "2苹果、2香蕉、2橘子";
    CHECK(hit(pattern, late) == "2苹果、2香蕉、2橘子");

    // 一 MB 量级的稿子：步数预算只给底数，不给按长度放大的那一份。能不能给出结果，
    // 全看候选起点是不是从顿号定的：逐字节试一遍要试上百万次，撞上限就报错。
    std::string draft;
    while (draft.size() < 1u * 1024 * 1024) draft += "这一行没有顿号，只有逗号，写着普通内容。\n";
    Pattern compiled(pattern);
    MatchLimits strict;
    strict.steps = 4'000'000;
    strict.steps_per_byte = 0;
    MatchRange range{texttool::kNoMatch, texttool::kNoMatch};
    CHECK_NOTHROW(range = compiled.search(draft, 0, strict));
    CHECK(range.begin == texttool::kNoMatch);

    const std::string with_match =
        draft.substr(0, 512 * 1024) + "3苹果、3香蕉、3橘子\n" + draft.substr(512 * 1024);
    CHECK_NOTHROW(range = compiled.search(with_match, 0, strict));
    REQUIRE(range.begin != texttool::kNoMatch);
    CHECK(with_match.substr(range.begin, range.end - range.begin) == "3苹果、3香蕉、3橘子");
}
