namespace TextTool.Tests.Services;

/// <summary>
/// 五条「正文里叙述过去」的判据：词面与正则混编，只对编程文档开，每条模式各带一条改法。
///
/// 读者拿这几个用例决定三件事：这类句子在哪个档会报、报在样稿第几行第几个字、
/// 改法表少写一条会怎样。词面与正则的条数钉在这里，是因为它们是判据的实际覆盖面，
/// 从词表搬过来时少抄一条不会报错，只会少报一处。
/// </summary>
public class HistoryMarkerRuleTests
{
    private const string 无债宣告 = "no-debt-declaration";
    private const string 完工记录 = "completion-log";
    private const string 取代叙事 = "supersession";
    private const string 立碑 = "tombstone-of-deletion";
    private const string 防复报 = "re-report-suppression";

    /// <summary>样稿每族只放一处，命中的行号与原文就是断言的依据；改样稿必须同步改下面的数字。</summary>
    private const string 样稿 =
        "# 存储层\n" +
        "\n" +
        "无未解决项\n" +
        "重试上限已修复。\n" +
        "超时时间原为三十秒，现为六十秒。\n" +
        "盘上已无旧快照。\n" +
        "这条不是缺陷：边界就是三次。";

    private static List<LintHit> Hits(string text, string? genre, string? onlyId = null) =>
        LintRunner.Run(
                new[] { ("样稿.md", text) },
                onlyId is null ? null : new[] { onlyId },
                null, genre)
            .Reports[0].Hits;

    [Theory]
    [InlineData(无债宣告, 3, "无未解决项")]
    [InlineData(完工记录, 4, "已修复")]
    [InlineData(取代叙事, 5, "原为三十秒，现为")]
    [InlineData(立碑, 6, "盘上已无旧快照")]
    [InlineData(防复报, 7, "不是缺陷")]
    public void 文档档样稿每族命中一处且行号与原文对得上(string id, int line, string match)
    {
        var hit = Assert.Single(Hits(样稿, "doc", id));

        Assert.Equal(line, hit.Line);
        Assert.Equal(match, hit.Match);
    }

    [Theory]
    [InlineData("agent")]
    [InlineData("fiction")]
    public void 样稿在小说与智能体档零命中(string genre)
    {
        var ids = new[] { 无债宣告, 完工记录, 取代叙事, 立碑, 防复报 };

        Assert.DoesNotContain(Hits(样稿, genre), hit => ids.Contains(hit.Id));
    }

    [Theory]
    [InlineData(无债宣告, 10, 4)]
    [InlineData(完工记录, 23, 2)]
    [InlineData(取代叙事, 26, 3)]
    [InlineData(立碑, 18, 6)]
    [InlineData(防复报, 8, 2)]
    public void 五条判据的词面与正则条数就是词表里的数(string id, int literals, int patterns)
    {
        var rule = LintRuleStore.GetDefaultRules().Single(r => r.Id == id);

        Assert.Equal(literals, rule.LiteralPatterns!.Count);
        Assert.Equal(patterns, rule.Patterns.Count);
        // 每条模式配一条改法：少一条会在加载期整表报错，这里钉住条数是因为它在两份数据之间同步
        Assert.Equal(literals + patterns, rule.Hints!.Count);
        Assert.All(rule.Hints!, hint => Assert.False(string.IsNullOrWhiteSpace(hint)));
    }

    [Fact]
    public void 五条判据只在文档档生效且不带打断权与密度门限()
    {
        // 密度门限没有真人编程文档语料可标定：真写出来的文档本来就常带这些词，
        // 该不该报要看「这次写入有没有让词变多」，那是护栏的净增判据，不是这里的密度
        var rules = LintRuleStore.GetDefaultRules().ToDictionary(r => r.Id);

        foreach (var id in new[] { 无债宣告, 完工记录, 取代叙事, 立碑, 防复报 })
        {
            var rule = rules[id];
            Assert.Equal(new[] { "doc" }, rule.Genres);
            Assert.Equal("document", rule.Scope);
            Assert.Equal("lexis", rule.Group);
            Assert.Equal(1, rule.MinCount);
            Assert.Equal("info", rule.Severity);
            Assert.Null(rule.MinDensityPerKChar);
            Assert.Null(rule.CollapseAtDensity);
            Assert.False(rule.BlocksReply);
            Assert.Null(rule.ReplyMinCount);
            Assert.False(rule.DefaultOff);
            Assert.False(string.IsNullOrWhiteSpace(rule.Detail));
        }
    }

    [Theory]
    [InlineData(无债宣告, "第一行\n暂无\n第三行", "暂无")]
    [InlineData(无债宣告, "没有欠账\n| 0 | \u2014 |", "| 0 | \u2014 |")]
    [InlineData(无债宣告, "第一行\n——已清零\n第三行", "——已清零")]
    [InlineData(完工记录, "上一句。\n本轮，改了两处。", "本轮，")]
    public void 行首行末的判据在正文中间也生效(string id, string text, string match)
    {
        // 词表源自带行锚点：不按多行解释，这三条只认全文第一行，正文中段的空节与表格行就永远漏报。
        // 断言的命中原文落在中间那行，就是为了证明锚点认的是每一行的首尾。
        var hit = Assert.Single(Hits(text, "doc", id));

        Assert.Equal(match, hit.Match);
        Assert.Equal(2, hit.Line);
    }

    [Fact]
    public void 改法少写一条时加载期即报错()
    {
        // 词面与正则分开写在两个列表里，改法是一整个列表：对不上时后面的建议整体错位，
        // 读者会照着别的词的批语改自己的稿子，所以合并条数在加载期就要拦住
        var rule = new LintRule
        {
            Id = "probe", Group = "lexis", Title = "探针", Kind = "regex",
            LiteralPatterns = new List<string> { "甲" },
            Patterns = new List<string> { "乙" },
            Hints = new List<string> { "只写一条" },
            Severity = "info", Detail = "d",
        };

        var ex = Assert.Throws<ArgumentException>(() => LintRuleStore.Validate(new List<LintRule> { rule }));

        Assert.Contains("1 条建议对 2 条模式", ex.Message);
    }
}
