namespace TextTool.Tests.Services;

public class LintRunnerTests
{
    [Fact]
    public void Run_空输入即抛()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            LintRunner.Run(Array.Empty<(string, string)>(), null, null));
        Assert.Contains("至少一个输入", ex.Message);
    }

    [Fact]
    public void Run_空only集合即抛()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            LintRunner.Run(new[] { ("a.txt", "干净的一段话。") }, Array.Empty<string>(), null));
        Assert.Contains("--only", ex.Message);
    }

    [Fact]
    public void Run_未知Id即抛()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            LintRunner.Run(new[] { ("a.txt", "干净的一段话。") }, new[] { "NOPE" }, null));
        Assert.Contains("NOPE", ex.Message);
    }

    [Fact]
    public void Run_非法minSeverity即抛()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            LintRunner.Run(new[] { ("a.txt", "干净的一段话。") }, null, "error"));
        Assert.Contains("--min-severity", ex.Message);
    }

    [Fact]
    public void Run_多输入按顺序出报告且过滤生效()
    {
        var inputs = new (string File, string Text)[]
        {
            ("hot.txt", "此外，此外，此外。"),
            ("clean.txt", "干净的一段话。"),
        };

        var all = LintRunner.Run(inputs, null, null);
        Assert.Equal(2, all.Reports.Count);
        Assert.Equal("hot.txt", all.Reports[0].File);
        Assert.Equal("clean.txt", all.Reports[1].File);
        Assert.Contains(all.Reports[0].Hits, h => h.Id == "ai-frequent-words");

        var warnOnly = LintRunner.Run(inputs, null, "warn");
        Assert.DoesNotContain(warnOnly.Reports[0].Hits, h => h.Id == "ai-frequent-words");

        var onlyL2 = LintRunner.Run(inputs, new[] { "AI-FREQUENT-WORDS" }, null);
        Assert.All(onlyL2.Reports[0].Hits, h => Assert.Equal("ai-frequent-words", h.Id));
        Assert.Empty(onlyL2.Reports[0].Notes);
    }

    [Fact]
    public void Run_最小严重度大小写不敏感()
    {
        // 夹具必须同时含 warn 与 info 命中：若大小写口径被改成区分大小写，
        // "WARN" 会被当成未知档位而一条都不过滤，info 命中就会漏进来。
        // 此前夹具只有 warn 命中，这条断言在现夹具上恒真、测不出东西。
        var inputs = new (string File, string Text)[] { ("t.txt", "您说得完全正确。赋能。") };

        var unfiltered = LintRunner.Run(inputs, null, null).Reports.SelectMany(r => r.Hits).ToList();
        Assert.Contains(unfiltered, h => h.Severity == "warn");   // 夹具确实有可命中的 warn 项
        Assert.Contains(unfiltered, h => h.Severity == "info");   // 也有 info 项，大小写口径坏了才会露出来

        var upper = LintRunner.Run(inputs, null, "WARN").Reports.SelectMany(r => r.Hits).ToList();
        var lower = LintRunner.Run(inputs, null, "warn").Reports.SelectMany(r => r.Hits).ToList();

        Assert.NotEmpty(lower);
        Assert.Equal(
            lower.Select(h => (h.Id, h.Line, h.Col, h.Severity)),
            upper.Select(h => (h.Id, h.Line, h.Col, h.Severity)));   // 两种大小写写法命中集合逐条相同
        Assert.All(lower, h => Assert.Equal("warn", h.Severity));
    }
}
