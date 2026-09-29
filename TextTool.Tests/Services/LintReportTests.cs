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
    public void Filter_按严重度保留warn及以上_统计项不受严重度影响()
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
    public void Filter_按Id过滤_统计项同受约束()
    {
        // --only 是规则选择轴：C 组也是合法 Id（--only C1 就只报 C1 统计项），
        // 选中的 Id 与统计项无关时不应带出任何统计项
        var noNotes = Sample().Filter(new[] { "S1" }, null);

        Assert.Single(noNotes.Hits);
        Assert.Equal("S1", noNotes.Hits[0].Id);
        Assert.Empty(noNotes.Notes);

        var onlyNote = Sample().Filter(new[] { "c1" }, null);   // 小写，顺带钉大小写不敏感

        Assert.Empty(onlyNote.Hits);
        Assert.Single(onlyNote.Notes);
        Assert.Equal("C1", onlyNote.Notes[0].Id);
    }

    [Fact]
    public void Filter_不改接收者且透传File与Chars()
    {
        var original = Sample();

        var filtered = original.Filter(new[] { "S1" }, "warn");

        Assert.Equal("a.md", filtered.File);
        Assert.Equal(10, filtered.Chars);
        Assert.Equal("S1", Assert.Single(filtered.Hits).Id);

        // 过滤走纯函数路径：接收者的两个集合与元素顺序都保持原样
        Assert.Equal(new[] { "S1", "L1" }, original.Hits.Select(h => h.Id));
        Assert.Equal("C1", Assert.Single(original.Notes).Id);
    }

    [Fact]
    public void Filter_结果集合不与源报告共享()
    {
        var original = Sample();

        var filtered = original.Filter(null, null);
        filtered.Notes.Add(new LintNote { Id = "C9", Text = "外部追加" });
        filtered.Hits.Clear();

        Assert.NotSame(original.Notes, filtered.Notes);
        Assert.Equal("C1", Assert.Single(original.Notes).Id);
        Assert.Equal(new[] { "S1", "L1" }, original.Hits.Select(h => h.Id));
    }

    [Fact]
    public void Filter_结果元素不与源报告共享()
    {
        var original = Sample();

        var filtered = original.Filter(null, null);
        var hit = filtered.Hits.Single(h => h.Id == "S1");
        hit.Title = "改过的标题";
        hit.Severity = "info";
        filtered.Notes.Single(n => n.Id == "C1").Text = "改过的统计文本";

        Assert.Equal("", original.Hits[0].Title);
        Assert.Equal("warn", original.Hits[0].Severity);
        Assert.Equal("段落过于均一", Assert.Single(original.Notes).Text);
    }

    [Fact]
    public void Filter_完整复制元素字段()
    {
        var hit = new LintHit
        {
            Id = "S1", Group = "S", Title = "不是A而是B", Severity = "warn",
            Line = 3, Col = 5, Length = 6, Match = "不是A而是B", Snippet = "…不是A而是B…",
            Detail = "对举句式", Hint = "改成直陈", SuggestScheme = "去AI味",
        };
        var original = new LintReport
        {
            File = "a.md", Chars = 42,
            Hits = new List<LintHit> { hit },
            Notes = new List<LintNote> { new() { Id = "C1", Text = "段落过于均一" } },
        };

        var before = new LintReportSet { Reports = { original } }.ToJson();
        var after = new LintReportSet { Reports = { original.Filter(null, null) } }.ToJson();

        // 全字段经复制后序列化结果一致：复制不丢字段，也不动 JSON 契约
        Assert.Equal(before, after);
    }

    [Fact]
    public void ToJson_键为PascalCase且中文不转义()
    {
        var json = new LintReportSet { Reports = new List<LintReport> { Sample() } }.ToJson();

        Assert.Contains("\"Version\": 2", json);
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
        Assert.Contains("\\u200b", json);   // JSON 文本里是可读的 \uXXXX 字面量
    }

    [Fact]
    public void ToJson_契约版本为2()
    {
        Assert.Contains("\"Version\": 2", new LintReportSet().ToJson());
    }

    [Fact]
    public void ToJson_行尾为LF()
    {
        var json = new LintReportSet { Reports = new List<LintReport> { Sample() } }.ToJson();

        Assert.DoesNotContain("\r", json);
        Assert.Contains("\n", json);
    }

    [Fact]
    public void ToJson_HTML敏感字符转义()
    {
        var report = new LintReport
        {
            File = "a.md", Chars = 3,
            Hits = new List<LintHit> { new() { Id = "L1", Match = "a<b&c'd" } },
        };

        var json = new LintReportSet { Reports = new List<LintReport> { report } }.ToJson();

        Assert.Contains("\\u003C", json);   // <
        Assert.Contains("\\u0026", json);   // &
        Assert.Contains("\\u0027", json);   // '
        Assert.DoesNotContain("\\u8D4B", json);   // 中文仍不转义
    }

    [Fact]
    public void ToJson_带段落口径字段()
    {
        var report = new LintReport { File = "a.md", Chars = 3, ParagraphMode = "markdown" };

        var json = new LintReportSet { Reports = new List<LintReport> { report } }.ToJson();

        Assert.Contains("\"ParagraphMode\": \"markdown\"", json);
    }

    [Fact]
    public void Filter_透传段落口径()
    {
        // LintRunner 一律经 Filter 出报告：不在这里透传，--json 与 [NOTE] 行永远报 line，口径探测等于没做
        var report = new LintReport { File = "a.md", Chars = 3, ParagraphMode = "markdown" };

        Assert.Equal("markdown", report.Filter(null, null).ParagraphMode);
        Assert.Equal("markdown", report.Filter(new[] { "S1" }, "warn").ParagraphMode);
    }
}
