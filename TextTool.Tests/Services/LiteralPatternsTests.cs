namespace TextTool.Tests.Services;

/// <summary>
/// 一条规则里同时有字面模式与正则模式时的行为。
///
/// 读者拿这几个用例决定三件事：字面里的正则元字符要不要自己转义、两个列表的先后顺序是什么、
/// 改法提示按哪个下标对齐。这三处错了都不会报错，只会让字面词悄悄匹配不到或者建议错位。
/// </summary>
public class LiteralPatternsTests
{
    private static LintRule Rule(string[] literals, string[] patterns, string[]? hints = null) => new()
    {
        Id = "probe", Group = "lexis", Title = "探针", Kind = "regex",
        LiteralPatterns = literals.ToList(), Patterns = patterns.ToList(),
        Hints = hints?.ToList(), Severity = "info", Detail = "d",
    };

    private static List<LintHit> Hits(LintRule rule, string text) =>
        new AiToneLintService(new[] { rule }).Scan(text, "t.txt").Hits;

    [Fact]
    public void Scan_字面里的正则元字符按字面匹配()
    {
        // 「已.*完成」写进字面字段就是这六个字符：作者想找星号和点号本身，
        // 不转义的话会变成「已 + 任意字符 + 完成」，把正常句子一并咬住。
        var hits = Hits(Rule(new[] { "已.*完成" }, Array.Empty<string>()), "任务已.*完成，另一处已经完成。");

        var hit = Assert.Single(hits);
        Assert.Equal("已.*完成", hit.Match);
        Assert.Equal(3, hit.Col);
    }

    [Fact]
    public void Scan_字面与正则同一条规则各按本义命中()
    {
        var hits = Hits(Rule(new[] { "甲已清零" }, new[] { "乙(?:改|换)为" }), "甲已清零。乙改为丙。");

        Assert.Equal(new[] { "甲已清零", "乙改为" }, hits.Select(h => h.Match));
    }

    [Fact]
    public void Scan_改法按词面在前正则在后对齐()
    {
        var rule = Rule(new[] { "字面词" }, new[] { "正则词" }, new[] { "改字面那条", "改正则那条" });

        var hits = Hits(rule, "字面词 正则词");

        Assert.Equal(new[] { "改字面那条", "改正则那条" }, hits.Select(h => h.Hint));
    }

    [Fact]
    public void Scan_字面与正则咬住同一处只报最长的一条()
    {
        // 同一处只算一处证据：报两条会让读者以为有两个地方要改
        var hit = Assert.Single(Hits(Rule(new[] { "砍一刀" }, new[] { "一刀" }), "先砍一刀再看。"));

        Assert.Equal("砍一刀", hit.Match);
        Assert.Equal(2, hit.Col);
    }

    [Fact]
    public void Validate_改法条数按字面加正则合计()
    {
        // 两条模式配两条改法：这是合法形状，既有只写 Patterns 的规则不受影响
        LintRuleStore.Validate(new List<LintRule>
        {
            Rule(new[] { "字面词" }, new[] { "正则词" }, new[] { "改字面那条", "改正则那条" }),
        });
    }
}
