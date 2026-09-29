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
        Assert.Contains(all.Reports[0].Hits, h => h.Id == "L2");

        var warnOnly = LintRunner.Run(inputs, null, "warn");
        Assert.DoesNotContain(warnOnly.Reports[0].Hits, h => h.Id == "L2");

        var onlyL2 = LintRunner.Run(inputs, new[] { "l2" }, null);
        Assert.All(onlyL2.Reports[0].Hits, h => Assert.Equal("L2", h.Id));
        Assert.Empty(onlyL2.Reports[0].Notes);
    }

    [Fact]
    public void Run_最小严重度大小写不敏感()
    {
        var reportSet = LintRunner.Run(new[] { ("t.txt", "您说得完全正确。") }, null, "WARN");

        Assert.NotEmpty(reportSet.Reports.SelectMany(r => r.Hits));
        Assert.All(reportSet.Reports.SelectMany(r => r.Hits), h => Assert.Equal("warn", h.Severity));
    }

    [Fact]
    public void Run_最小严重度非法值仍抛()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => LintRunner.Run(new[] { ("t.txt", "随便一段文本。") }, null, "error"));

        Assert.Contains("--min-severity", ex.Message);
    }
}
