namespace TextTool.Tests.Services;

/// <summary>
/// 命中日志：留档用的一行一条 JSON。
///
/// 两条判据：字段能原样读回，且整份日志只含 ASCII 与汉字、全角标点。
/// 后一条是它能被提交进工作区文档目录的前提，命中原文里的箭头、省略号、表情都转义。
/// </summary>
public class HitLogTests
{
    private static LintReportSet 报告集(params LintHit[] hits)
    {
        var set = new LintReportSet();
        var report = new LintReport { File = "样本.md", Chars = 42, ParagraphMode = "markdown" };
        report.Hits.AddRange(hits);
        set.Reports.Add(report);
        return set;
    }

    private static LintHit 命中(string id, int line, int col, string match, string snippet) => new()
    {
        Id = id, Severity = "warn", Line = line, Col = col, Length = match.Length,
        Match = match, Snippet = snippet,
    };

    [Fact]
    public void 每份报告一行汇总_每条命中一行()
    {
        var text = HitLog.Lines(报告集(
            命中("parallel-contrast", 3, 5, "不是甲而是乙", "……不是甲而是乙……"),
            命中("cliche-words", 7, 1, "换句话说", "换句话说，他走了。")));

        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(3, lines.Length);
        Assert.Contains("\"Kind\":\"Report\"", lines[0]);
        Assert.Contains("\"Chars\":42", lines[0]);
        Assert.Contains("\"Hits\":2", lines[0]);
        Assert.Contains("\"Kind\":\"Hit\"", lines[1]);
        Assert.Contains("\"Line\":3", lines[1]);
        Assert.Contains("\"Id\":\"parallel-contrast\"", lines[1]);
        Assert.Contains("不是甲而是乙", lines[1]);
    }

    [Fact]
    public void 画不出来的字符转义_汉字照原样()
    {
        var text = HitLog.Lines(报告集(命中("dash", 1, 1, "甲→乙", "箭头 → 与省略号 … 与「引号」")));

        Assert.DoesNotContain("→", text);
        Assert.DoesNotContain("…", text);
        Assert.Contains("\\u2192", text);
        Assert.Contains("\\u2026", text);
        Assert.Contains("汉字", HitLog.Lines(报告集(命中("x", 1, 1, "汉字", "正文"))));
        Assert.Contains("「引号」", text);
    }

    [Fact]
    public void 星平面字符拆成两个转义()
    {
        var text = HitLog.Lines(报告集(命中("emoji", 1, 1, "😀", "笑 😀")));

        Assert.Contains("\\ud83d\\ude00", text);
    }

    [Fact]
    public void 跨行命中的换行被转义成一条()
    {
        var text = HitLog.Lines(报告集(命中("repeated-verbatim", 2, 1, "第一行\n第二行", "……第一行\n第二行……")));

        // 汇总一行、命中一行：命中里的换行不能把一条拆成两行
        Assert.Equal(2, text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Contains("第一行\\n第二行", text);
    }

    [Fact]
    public void 引号与反斜杠按JSON转义()
    {
        var text = HitLog.Lines(报告集(命中("quote", 1, 1, "\"x\"", "路径 C:\\tmp")));

        Assert.Contains("\\\"x\\\"", text);
        Assert.Contains("C:\\\\tmp", text);
    }

    [Fact]
    public void 追加写不覆盖已有内容_目录不存在时报错()
    {
        using var dir = new TempDir();
        string path = Path.Combine(dir.Path, "hits.jsonl");

        HitLog.Append(path, 报告集(命中("a", 1, 1, "甲", "甲")));
        HitLog.Append(path, 报告集(命中("b", 2, 1, "乙", "乙")));

        var lines = File.ReadAllLines(path);
        Assert.Equal(4, lines.Length);
        Assert.Contains(lines, l => l.Contains("\"Id\":\"a\""));
        Assert.Contains(lines, l => l.Contains("\"Id\":\"b\""));

        Assert.Throws<DirectoryNotFoundException>(
            () => HitLog.Append(Path.Combine(dir.Path, "缺失", "hits.jsonl"), 报告集()));
    }

    // ================================================================
    //  逐份追加：一次开流、写一份一次，与整批写出来的是同一份字节
    // ================================================================

    [Fact]
    public void 逐份追加与整批追加写出同一份字节()
    {
        using var dir = new TempDir();
        string 整批 = Path.Combine(dir.Path, "整批.jsonl");
        string 逐份 = Path.Combine(dir.Path, "逐份.jsonl");

        var set = 报告集(
            命中("a", 1, 1, "甲", "甲"),
            命中("b", 2, 1, "乙→丙", "……乙…丙……"));
        set.Reports.Add(new LintReport
        {
            File = "另一份.md", Chars = 7,
            Hits = new List<LintHit> { 命中("c", 1, 1, "😀", "笑 😀") },
        });

        HitLog.Append(整批, set);
        using (var log = HitLog.OpenAppender(逐份))
        {
            foreach (var report in set.Reports)
                log.Write(HitLog.Lines(report));
        }

        Assert.Equal(File.ReadAllBytes(整批), File.ReadAllBytes(逐份));
        // 单份正文拼起来就是整批正文
        Assert.Equal(HitLog.Lines(set), string.Concat(set.Reports.Select(HitLog.Lines)));
    }

    [Fact]
    public void 追加流开不了时点名目录()
    {
        using var dir = new TempDir();

        var ex = Assert.Throws<DirectoryNotFoundException>(
            () => HitLog.OpenAppender(Path.Combine(dir.Path, "缺失", "hits.jsonl")));
        Assert.Contains("日志目录不存在", ex.Message);
    }
}
