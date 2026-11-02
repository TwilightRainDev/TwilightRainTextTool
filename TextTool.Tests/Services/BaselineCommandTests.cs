using System.Text.Json;
using TextTool.Cli;
using TextTool.Services;

namespace TextTool.Tests.Services;

/// <summary>
/// 基线差分：写前那份当基线、写后那份当正文，报告只列多出来的位置。
///
/// 口径：
/// - 按「规则 Id 加命中词面」计数相减，位置按正文那份的行号算；
/// - 每条参与比较的规则一行新增数（新增 0 也给），护栏据此判「有没有变多」；
/// - 退出码只认新增：有新增退 1，没新增退 0，读不到基线退 2；
/// - 契约升到 4，旧字段一个不动。
/// </summary>
public class BaselineCommandTests
{
    /// <summary>写前那一份：两行，一处命中。</summary>
    private const string 写前 = "第一行写到探。\n第二行没有。\n";

    /// <summary>写后那一份：多出第三行，也多出一处命中。</summary>
    private static readonly string 写后 = 写前 + "第三行也有探。\n";

    /// <summary>探针规则：字面词「探」，不设门限。别的内置规则与本类无关，全由调用方注入。</summary>
    private static LintRule 探针(string id = "probe", string pattern = "探") => new()
    {
        Id = id,
        Group = "test",
        Title = id,
        Kind = "literal",
        Scope = "document",
        Patterns = new List<string> { pattern },
        Genres = new List<string>(),
        Severity = "info",
    };

    private static LintReport 跑(string body, string baseline, params LintRule[] rules) =>
        LintRunner.Run(
            new[] { ("body.md", body) }, null, null,
            rules: rules, baseline: ("before.md", baseline)).Reports[0];

    // ================================================================
    //  三种情形
    // ================================================================

    [Fact]
    public void 存量命中不报()
    {
        var report = 跑(写前, 写前, 探针());

        Assert.Empty(report.Hits);
        // 报告里的统计项（句长、段落长度之类）不是本类要测的东西，只挑探针那一行
        var note = Assert.Single(report.Notes.Where(n => n.Id == "probe"));
        Assert.Contains("新增 0 处", note.Text);
        Assert.Equal(1, report.BaselineHits);
    }

    [Fact]
    public void 净增报出且行号按正文算()
    {
        var report = 跑(写后, 写前, 探针());

        var hit = Assert.Single(report.Hits);
        Assert.Equal("probe", hit.Id);
        Assert.Equal(3, hit.Line);          // 正文里的第三行，不是基线里的第一行
        Assert.Equal("探", hit.Match);

        var note = Assert.Single(report.Notes.Where(n => n.Id == "probe"));
        Assert.Contains("新增 1 处", note.Text);
    }

    [Fact]
    public void 删掉新增那几句后不再报()
    {
        // 先确认写后有新增，再确认把新增那句删掉之后归零：
        // 只测前者会把「永远报一处」的实现在测试里放过
        Assert.Single(跑(写后, 写前, 探针()).Hits);
        Assert.Empty(跑(写前, 写前, 探针()).Hits);
    }

    // ================================================================
    //  每条规则一行新增数
    // ================================================================

    [Fact]
    public void 每条参与比较的规则各给一行新增数()
    {
        // 甲两处命中都在基线里，乙被整段删掉：两条都要给一行「新增 0 处」——
        // 「这次没变多」与「这条根本没查」是两件事，护栏靠这一行分清
        var before = "甲甲。锚。\n";
        var after = "甲甲。\n";
        var report = 跑(after, before, 探针("probe-a", "甲"), 探针("probe-b", "锚"));

        var notes = report.Notes.Where(n => n.Id.StartsWith("probe-", StringComparison.Ordinal)).ToList();
        Assert.Empty(report.Hits);
        Assert.Equal(new[] { "probe-a", "probe-b" }, notes.Select(n => n.Id));
        Assert.All(notes, note => Assert.Contains("新增 0 处", note.Text));
    }

    [Fact]
    public void 新增数里的第一个整数就是处数()
    {
        // 护栏只认 Text 里的第一个整数，别的字都由工具侧自行措辞
        var body = 写前 + "探。\n探。\n";
        var report = 跑(body, 写前, 探针());

        // 报告里的统计项也有数字，护栏只读它那五条规则的 Id，这里同样只看探针那一行
        var note = Assert.Single(report.Notes.Where(n => n.Id == "probe"));
        var digits = System.Text.RegularExpressions.Regex.Match(note.Text, @"\d+");
        Assert.True(digits.Success);
        Assert.Equal(2, int.Parse(digits.Value));
        Assert.Equal(2, report.Hits.Count);
    }

    // ================================================================
    //  人读分支
    // ================================================================

    [Fact]
    public void 人读报告开头一行说明相对基线新增多少()
    {
        var report = 跑(写后, 写前, 探针());
        string text = LintTextFormatter.Format(report);

        Assert.StartsWith("相对基线新增 1 处，基线 1 处", text);
    }

    // ================================================================
    //  用法错误
    // ================================================================

    [Fact]
    public void 基线配多个输入即报错()
    {
        var ex = Assert.Throws<ArgumentException>(() => LintRunner.Run(
            new[] { ("a.md", 写后), ("b.md", 写后) }, null, null,
            rules: new List<LintRule> { 探针() }, baseline: ("before.md", 写前)));
        Assert.Contains("--baseline", ex.Message);
    }

    [Fact]
    public void 基线文件读不到时退二并说清是基线()
    {
        using var dir = new TempDir();
        string missing = Path.Combine(dir.Path, "nope.md");
        var ex = Assert.Throws<ArgumentException>(() => Program.ReadBaseline(missing));
        Assert.Contains("基线文件读不到", ex.Message);
        Assert.Contains(missing, ex.Message);
    }

    // ================================================================
    //  JSON 契约
    // ================================================================

    [Fact]
    public void Json版本升到四且旧字段原样在()
    {
        var report = 跑(写后, 写前, 探针());
        var set = new LintReportSet { Reports = new List<LintReport> { report } };

        using var doc = JsonDocument.Parse(set.ToJson());
        var root = doc.RootElement;
        Assert.Equal(4, root.GetProperty("Version").GetInt32());

        var first = root.GetProperty("Reports")[0];
        Assert.Equal("body.md", first.GetProperty("File").GetString());
        Assert.Equal(写后.Length, first.GetProperty("Chars").GetInt32());
        Assert.Equal("line", first.GetProperty("ParagraphMode").GetString());
        Assert.Equal(1, first.GetProperty("BaselineHits").GetInt32());

        var hit = first.GetProperty("Hits")[0];
        foreach (string field in new[] { "Id", "Match", "Line", "Col", "Severity", "Snippet" })
            Assert.True(hit.TryGetProperty(field, out _), $"命中缺字段 {field}");

        var note = first.GetProperty("Notes").EnumerateArray()
            .Single(n => n.GetProperty("Id").GetString() == "probe");
        Assert.Equal("相对基线新增 1 处", note.GetProperty("Text").GetString());
    }

    // ================================================================
    //  命令行
    // ================================================================

    /// <summary>
    /// 跑一次命令行并取回 stdout。命令行只从磁盘读规则表，所以这里只用内置规则、
    /// 不插探针：要验的就是真实入口的行为。
    /// </summary>
    private static (int Code, string Stdout) 命令行(params string[] args)
    {
        var writer = new StringWriter();
        var original = Console.Out;
        try
        {
            Console.SetOut(writer);
            int code = Program.Main(args);
            return (code, writer.ToString());
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    [Fact]
    public void 命令行有新增退一没有新增退零()
    {
        using var dir = new TempDir();
        string before = Path.Combine(dir.Path, "before.md");
        string after = Path.Combine(dir.Path, "after.md");
        File.WriteAllText(before, "这一段没有对比句。\n", new UTF8Encoding(false));
        File.WriteAllText(after, "这一段没有对比句。\n不是这样，而是那样。\n", new UTF8Encoding(false));
        string[] common = { "--genre", "doc", "--only", "parallel-contrast", "--baseline", before, "--json" };

        var 有新增 = 命令行(new[] { "lint", after }.Concat(common).ToArray());
        Assert.Equal(1, 有新增.Code);
        Assert.Contains("parallel-contrast", 有新增.Stdout);

        // 自己跟自己比：一处新增也没有，退 0
        var 无新增 = 命令行(new[] { "lint", before }.Concat(common).ToArray());
        Assert.Equal(0, 无新增.Code);
    }

    [Fact]
    public void 命令行基线文件不存在时退二()
    {
        using var dir = new TempDir();
        string after = Path.Combine(dir.Path, "after.md");
        File.WriteAllText(after, "不是这样，而是那样。\n", new UTF8Encoding(false));

        var run = 命令行("lint", after, "--baseline", Path.Combine(dir.Path, "nope.md"), "--json");
        Assert.Equal(2, run.Code);
    }
}
