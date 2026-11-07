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

    // ================================================================
    //  逐份扫描的会话：与整批入口是同一份结果
    // ================================================================

    /// <summary>
    /// 一次建引擎、逐份扫描（命令行走这条）与攒齐再出（界面走这条）必须给同一份报告：
    /// 引擎复用是省下规则编译与繁简归一的功夫，不该换来不一样的命中。
    /// 夹具刻意掺上两种口径的命中：一套 warn、一套 info，还有一个统计项。
    /// </summary>
    [Fact]
    public void 会话逐份扫描与整批结果的每份报告逐字段相同()
    {
        var inputs = new (string File, string Text)[]
        {
            ("甲.md", "您说得完全正确。赋能。\n\n首先，我们要看一个问题；其次，我们要看另一个问题；最后，我们收尾。\n"),
            ("乙.txt", "干净的一段话。\n\n\n第一行。\n第二行。\n"),
            ("丙.md", ""),
        };

        var batch = LintRunner.Run(inputs, null, null).Reports;
        var session = LintSession.Create(null, null, null);
        var one = inputs.Select(i => session.Scan(i.File, i.Text)).ToList();

        Assert.Equal(batch.Count, one.Count);
        for (int i = 0; i < batch.Count; i++)
        {
            Assert.Equal(batch[i].File, one[i].File);
            Assert.Equal(batch[i].Chars, one[i].Chars);
            Assert.Equal(batch[i].ParagraphMode, one[i].ParagraphMode);
            Assert.Equal(batch[i].BaselineHits, one[i].BaselineHits);
            Assert.Equal(
                batch[i].Hits.Select(h => (h.Id, h.Line, h.Col, h.Length, h.Match, h.Snippet, h.Detail, h.Hint)),
                one[i].Hits.Select(h => (h.Id, h.Line, h.Col, h.Length, h.Match, h.Snippet, h.Detail, h.Hint)));
            Assert.Equal(
                batch[i].Notes.Select(n => (n.Id, n.Text)),
                one[i].Notes.Select(n => (n.Id, n.Text)));
            // 折叠口径随报告一起出来，两条路径一致
            Assert.Equal(batch[i].CollapseAtDensity, one[i].CollapseAtDensity);
        }
    }

    [Fact]
    public void 会话逐份扫描_过滤与基线都按同一口径()
    {
        const string 稿 = "您说得完全正确。赋能。\n\n首先，我们要看一个问题；其次，我们要看另一个问题；最后，我们收尾。\n";
        const string 基线 = "首先，我们要看一个问题；其次，我们要看另一个问题；最后，我们收尾。\n";

        var batch = LintRunner.Run(
            new[] { ("甲.md", 稿) }, null, null, baseline: ("基.md", 基线)).Reports[0];

        var session = LintSession.Create(null, null, null);
        var one = session.ScanAgainstBaseline("甲.md", 稿, "基.md", 基线);

        Assert.Equal(batch.BaselineHits, one.BaselineHits);
        Assert.Equal(
            batch.Hits.Select(h => (h.Id, h.Line, h.Col, h.Match)),
            one.Hits.Select(h => (h.Id, h.Line, h.Col, h.Match)));
    }

    [Fact]
    public void 会话建表的校验顺序与整批入口一致()
    {
        // 档位拼错先报档位，不先去撞规则文件
        Assert.Contains("-genre", Assert.Throws<ArgumentException>(
            () => LintSession.Create("poetry", null, null)).Message);
        // 规则 Id 未知排在 --only 空集合之后
        Assert.Contains("--only", Assert.Throws<ArgumentException>(
            () => LintSession.Create(null, Array.Empty<string>(), null)).Message);
        // 基线条数与「未知规则 Id」同时不成立时，先报规则表那一条
        Assert.Contains("未知规则 Id", Assert.Throws<ArgumentException>(
            () => LintSession.Create(null, new[] { "NOPE" }, null, baselineInputs: 2)).Message);
        Assert.Contains("只配一个输入文件", Assert.Throws<ArgumentException>(
            () => LintSession.Create(null, null, null, baselineInputs: 2)).Message);
    }
}
