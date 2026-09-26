namespace TextTool.Tests.Services;

public class LintReportTests
{
    private static LintReport Sample() => new()
    {
        File = "a.md", Chars = 10,
        Hits = new List<LintHit>
        {
            new() { Id = "S1", Severity = "warn", Line = 1, Col = 1, Match = "不是A而是B" },
            new() { Id = "L1", Severity = "info", Line = 2, Col = 3, Match = "赋能" },
        },
        Notes = new List<LintNote> { new() { Id = "C1", Text = "段落过于均一" } },
    };

    [Fact]
    public void Filter_按严重度保留warn及以上_统计项不受影响()
    {
        var filtered = Sample().Filter(null, "warn");

        Assert.Single(filtered.Hits);
        Assert.Equal("S1", filtered.Hits[0].Id);
        Assert.Single(filtered.Notes);
    }

    [Fact]
    public void Filter_按Id过滤_大小写不敏感()
    {
        var filtered = Sample().Filter(new[] { "l1" }, null);

        Assert.Single(filtered.Hits);
        Assert.Equal("L1", filtered.Hits[0].Id);
    }

    [Fact]
    public void ToJson_键为PascalCase且中文不转义()
    {
        var json = new LintReportSet { Reports = new List<LintReport> { Sample() } }.ToJson();

        Assert.Contains("\"Version\": 1", json);
        Assert.Contains("\"Reports\"", json);
        Assert.Contains("\"Match\": \"赋能\"", json);
        Assert.DoesNotContain("\\u8D4B", json);   // 中文未被转义成 \uXXXX
    }

    [Fact]
    public void ToJson_命中隐形字符不抛异常()
    {
        var report = new LintReport
        {
            File = "a.md", Chars = 3,
            Hits = new List<LintHit> { new() { Id = "P7", Severity = "warn", Match = "\u200B" } },
        };

        var json = new LintReportSet { Reports = new List<LintReport> { report } }.ToJson();

        Assert.Contains("P7", json);
    }
}
