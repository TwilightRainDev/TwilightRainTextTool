namespace TextTool.Tests.Services;

/// <summary>
/// 命中只记「原文在哪、取多长」：Match 与 Snippet 到取值时才从原文切，报告带着原文走。
///
/// 钉三件事：切出来的两段与先切好的那一份逐字相同（含省略号与代理对边界）、
/// 三条输出路径对「只记位置」与「填好字符串」两种命中逐字节相同、
/// 原文不进 --json 契约且报告被复制（Filter）之后仍能切。
/// </summary>
public class LintHitSourceTests
{
    private static AiToneLintService Service(params LintRule[] rules) => new(rules);

    private static LintRule Rule(string id, string kind, params string[] patterns) => new()
    {
        Id = id, Group = "lexis", Title = id, Kind = kind,
        Patterns = patterns.ToList(), Severity = "warn", Detail = "d",
    };

    private static LintReport Scan(string text, params LintRule[] rules) => Service(rules).Scan(text, "t.txt");

    /// <summary>把命中上的两段先切好填回去：老用法，也是「只记位置」这条新走法的对照物。</summary>
    private static LintReport 先切好(LintReport report)
    {
        foreach (var hit in report.Hits)
        {
            hit.Match = hit.Match;
            hit.Snippet = hit.Snippet;
        }
        return report;
    }

    [Fact]
    public void 只记位置时_命中的两段照旧取原文()
    {
        var text = "第一行\n第二行有赋能二字。";

        var hit = Assert.Single(Scan(text, Rule("cliche-words", "literal", "赋能")).Hits);

        Assert.Equal("赋能", hit.Match);
        Assert.Equal("第一行\n第二行有赋能二字。", hit.Snippet);   // 全文都在八字窗口内：两端不加省略号
        Assert.Contains(hit.Match, hit.Snippet);                  // 片段含命中原文，跨行也不丢
    }

    [Fact]
    public void 只记位置时_片段前后各留八个字且两端加省略号()
    {
        var text = "零一二三四五六七八九" + "赋能" + "甲乙丙丁戊己庚辛壬癸";

        var hit = Assert.Single(Scan(text, Rule("cliche-words", "literal", "赋能")).Hits);

        Assert.Equal("…二三四五六七八九赋能甲乙丙丁戊己庚辛…", hit.Snippet);
    }

    [Fact]
    public void 只记位置时_片段边界不劈开代理对()
    {
        // 窗口左端正好落在 emoji 的低代理上：往后退一格，否则片段里会出现落单的代理项
        var text = "起头" + string.Concat(Enumerable.Repeat("😀", 9)) + "书赋能后面";

        var hit = Assert.Single(Scan(text, Rule("cliche-words", "literal", "赋能")).Hits);

        Assert.Equal("…😀😀😀😀书赋能后面", hit.Snippet);
        Assert.True(IsWellFormed(hit.Snippet));
    }

    private static bool IsWellFormed(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i])) { i++; continue; }
            if (char.IsLowSurrogate(text[i])) return false;
        }
        return true;
    }

    [Fact]
    public void 直接填字符串的老用法照旧可读()
    {
        var hit = new LintHit { Id = "parallel-contrast", Match = "不是甲而是乙", Snippet = "…不是甲而是乙…" };

        Assert.Equal("不是甲而是乙", hit.Match);
        Assert.Equal("…不是甲而是乙…", hit.Snippet);
    }

    [Fact]
    public void 没有原文也没填字面值时_两段是空串()
    {
        // 老调用方造空命中不填这两段：两段默认是空串，取值不能抛
        var hit = new LintHit { Id = "cliche-words" };

        Assert.Equal("", hit.Match);
        Assert.Equal("", hit.Snippet);
    }

    [Fact]
    public void 命中原文与片段可以取原文里不同的两段()
    {
        // 复读串那条报的是整串长度，命中原文只是串首那个单元：两处区间各记各的
        var text = "前面铺垫。极其极其极其极其，后面收尾。";
        var hit = new LintHit { Id = "repeated-token-run", Length = 8 };

        hit.BindTo(text, 5, 2, 5, 8);

        Assert.Equal("极其", hit.Match);
        Assert.Equal(8, hit.Length);
        Assert.Equal(text, hit.Snippet);   // 全文都在八字窗口内
    }

    [Fact]
    public void 报告的原文就是交进去的那一份_不另存副本()
    {
        var text = "第一行\n第二行有赋能二字";

        var report = Scan(text, Rule("cliche-words", "literal", "赋能"));

        Assert.Same(text, report.SourceText);
    }

    [Fact]
    public void 三条输出路径_只记位置与填好字符串逐字节相同()
    {
        var text = "这不是数据问题而是口径问题。\n赋能、抓手、闭环都在这两句里。";
        LintRule[] rules =
        {
            Rule("parallel-contrast", "regex", "不是[^。]*?而是"),
            Rule("cliche-words", "literal", "赋能", "抓手", "闭环"),
        };
        var lazy = Scan(text, rules);
        var eager = 先切好(Scan(text, rules));
        Assert.NotEmpty(lazy.Hits);

        Assert.Equal(
            LintOutput.Render(new LintReportSet { Reports = { eager } }, asJson: true),
            LintOutput.Render(new LintReportSet { Reports = { lazy } }, asJson: true));
        Assert.Equal(
            LintOutput.Render(new LintReportSet { Reports = { eager } }, asJson: false),
            LintOutput.Render(new LintReportSet { Reports = { lazy } }, asJson: false));
        Assert.Equal(HitLog.Lines(eager), HitLog.Lines(lazy));
    }

    [Fact]
    public void 三条输出路径_跨行与隐形字符两种命中写法逐字节相同()
    {
        // 折换行、转隐形字是片段改写的两处关口：两条路必须走出同一串字节
        LintRule[] rules = { Rule("cliche-words", "literal", "赋能", "\u200B") };
        var text = "第一句有赋能，\n接上一段\u200B还带着隐形字。";
        var lazy = Scan(text, rules);
        var eager = 先切好(Scan(text, rules));
        Assert.Equal(2, lazy.Hits.Count);

        Assert.Equal(LintTextFormatter.Format(eager), LintTextFormatter.Format(lazy));
        Assert.Equal(
            LintOutput.Render(new LintReportSet { Reports = { eager } }, asJson: true),
            LintOutput.Render(new LintReportSet { Reports = { lazy } }, asJson: true));
        Assert.Equal(HitLog.Lines(eager), HitLog.Lines(lazy));
    }

    [Fact]
    public void 原文不进JSON契约()
    {
        var report = Scan("正文有赋能二字", Rule("cliche-words", "literal", "赋能"));

        var json = new LintReportSet { Reports = { report } }.ToJson();

        // 机器手上的原文比报告里那份更权威，多写一遍只是把同一段字扩大一倍
        Assert.DoesNotContain("SourceText", json);
        Assert.Contains("\"Match\": \"赋能\"", json);
    }

    [Fact]
    public void Filter之后命中仍切得出片段且原文跟着走()
    {
        var text = "这不是数据问题而是口径问题。";
        var report = Scan(text, Rule("parallel-contrast", "regex", "不是[^。]*?而是"));

        var filtered = report.Filter(new[] { "parallel-contrast" }, null);

        Assert.Same(report.SourceText, filtered.SourceText);
        var hit = Assert.Single(filtered.Hits);
        Assert.Equal("不是数据问题而是", hit.Match);
        Assert.Equal("这不是数据问题而是口径问题。", hit.Snippet);
    }

    [Fact]
    public void 繁转简按片走_长稿跨片照旧按简中判据命中且两段取原文()
    {
        // 五十兆那种长度上繁转简是分片做的：片与片之间的行不能被切开，
        // 命中位置与片段仍要落在原文那一段上
        var line = "隨著人工智能技術的發展，這意味著每個人都要用起來。\n";
        var text = string.Concat(Enumerable.Repeat(line, 1500));
        Assert.True(text.Length > 32000);

        var report = Scan(text, Rule("cliche-words", "literal", "这意味着"));

        Assert.Equal(1500, report.Hits.Count);
        Assert.Equal("這意味著", report.Hits[0].Match);
        Assert.Contains("這意味著", report.Hits[0].Snippet);
    }

    [Fact]
    public void 行式口径_段落起止按原文下标给出()
    {
        // 段落的 Offset + Match.Index 要能换算回原文：空行不占段，首尾空白不算进起止
        var paragraphs = AiToneLintService.Paragraphs("甲\r\n\r\n  乙  \n丙", "line");

        Assert.Equal(new List<(int Offset, string Text)> { (0, "甲"), (7, "乙"), (11, "丙") }, paragraphs);
    }

    [Fact]
    public void 整篇空着时两种口径都不出段()
    {
        Assert.Empty(AiToneLintService.Paragraphs("", "line"));
        Assert.Empty(AiToneLintService.Paragraphs("", "markdown"));
        Assert.Empty(AiToneLintService.Paragraphs("\n\n  \n", "line"));
        Assert.Empty(AiToneLintService.Paragraphs("\n\n  \n", "markdown"));
    }
}
