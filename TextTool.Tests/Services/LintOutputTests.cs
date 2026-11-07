namespace TextTool.Tests.Services;

public class LintOutputTests
{
    private static LintReport Report(string file) => new() { File = file, Chars = 10 };

    [Fact]
    public void Render_人读多报告每份前印文件名且之间空一行()
    {
        var set = new LintReportSet { Reports = { Report("a.md"), Report("b.md") } };

        var text = LintOutput.Render(set, asJson: false);

        // 多份时两段命中的标题一模一样，只有文件名能告诉读者这一条属于哪一份
        string first = "a.md\n" + LintTextFormatter.Format(set.Reports[0]).Replace("\r\n", "\n");
        string second = "b.md\n" + LintTextFormatter.Format(set.Reports[1]).Replace("\r\n", "\n");
        Assert.Equal(first + "\n" + "\n" + second + "\n", text);   // 报告间空一行，尾部只一个换行
        Assert.DoesNotContain("\r", text);
    }

    [Fact]
    public void Render_人读单报告不印文件名()
    {
        var set = new LintReportSet { Reports = { Report("a.md") } };

        var text = LintOutput.Render(set, asJson: false);

        string expected = LintTextFormatter.Format(set.Reports[0]).Replace("\r\n", "\n");
        Assert.Equal(expected + "\n", text);
        Assert.DoesNotContain("a.md\n", text);   // 只有一份时输出与从前一字不差
    }

    [Fact]
    public void Render_Json分支以单个LF结尾且不夹带CR()
    {
        var set = new LintReportSet { Reports = { Report("a.md") } };

        var text = LintOutput.Render(set, asJson: true);

        Assert.Equal(set.ToJson() + "\n", text);
        Assert.DoesNotContain("\r", text);
        Assert.False(text.EndsWith("\n\n"), "尾部只保留一个换行");
    }

    [Fact]
    public void Render_无报告时人读分支输出空串()
    {
        Assert.Equal("", LintOutput.Render(new LintReportSet(), asJson: false));
    }

    [Fact]
    public void Render_单份人读报告行尾全为LF且尾部恰好一个换行()
    {
        var set = new LintReportSet { Reports = { Report("a.md") } };

        var text = LintOutput.Render(set, asJson: false);

        // 尾换行由 Render 统一追加：整体 LF，且尾部恰好一个换行而不是两个
        Assert.Equal(LintTextFormatter.Format(set.Reports[0]).Replace("\r\n", "\n") + "\n", text);
        Assert.DoesNotContain("\r", text);
        Assert.EndsWith("\n", text);
        Assert.False(text.EndsWith("\n\n"), "尾部只保留一个换行");
    }

    [Fact]
    public void Render_两份报告之间恰好一个空行()
    {
        var set = new LintReportSet { Reports = { Report("a.md"), Report("b.md") } };

        var text = LintOutput.Render(set, asJson: false);

        // 去掉尾换行后按行切：空行只能来自报告之间的分隔，恰好 1 行（0 行或 2 行都会红）
        var lines = text[..^1].Split('\n');
        Assert.Equal(1, lines.Count(line => line.Length == 0));
        Assert.DoesNotContain("\r", text);
    }

    [Fact]
    public void Render_三份及以上报告间空行数恰为报告数减一()
    {
        var set = new LintReportSet { Reports = { Report("a.md"), Report("b.md"), Report("c.md"), Report("d.md") } };

        var text = LintOutput.Render(set, asJson: false);

        // 多份报告经 CLI 冒烟只钉过「有空行」；这里精确钉住空行数 = 报告数 - 1
        var lines = text[..^1].Split('\n');
        Assert.Equal(set.Reports.Count - 1, lines.Count(line => line.Length == 0));
        Assert.DoesNotContain("\r", text);
    }

    [Fact]
    public void Render_人读报告正文原带CRLF时输出不残留CR()
    {
        // Hint 未经 Visible 转义，可原样把 CRLF 带进渲染结果；Snippet 会被转义成 \u000d，验不到
        var report = new LintReport
        {
            File = "a.md", Chars = 10,
            Hits = new List<LintHit>
            {
                new() { Id = "parallel-contrast", Title = "对举句式", Line = 1, Col = 1, Snippet = "片段", Hint = "上行\r\n下行" },
            },
        };

        // 先证明带 CRLF 的那一段确实进到了渲染结果里（收成 LF 之后仍是两行），否则下面的断言是空转
        Assert.Contains("上行\n下行", LintTextFormatter.Format(report));

        var text = LintOutput.Render(new LintReportSet { Reports = { report } }, asJson: false);

        Assert.DoesNotContain("\r", text);
    }

    // ================================================================
    //  逐份出口与整段出口必须是同一份字节
    // ================================================================
    //  命令行逐份写（ILintReportSink）与整段渲染（Render）走的是同一套排版，
    //  一处改了另一处没跟，逐字节比对才会红。下面几条把两条路径钉在一起。

    /// <summary>
    /// 一处不干净的字段塞满：隐形字符、HTML 敏感字符、CRLF、表情、空标题、
    /// 缺 Hint/SuggestScheme、带基线与不带基线。转义与键序的差异全在这份夹具上露头。
    /// </summary>
    private static LintReport 脏报告(string file, bool withBaseline) => new()
    {
        File = file, Chars = 42, ParagraphMode = "markdown",
        BaselineHits = withBaseline ? 3 : null,
        Notes = new List<LintNote> { new() { Id = "paragraph-length-uniform", Text = "段落过于均一" } },
        Hits = new List<LintHit>
        {
            new()
            {
                Id = "cliche-words", Group = "wording", Title = "套话词", Severity = "warn",
                Line = 3, Col = 5, Length = 2,
                Match = "赋能", Snippet = "…前\u200b后 a<b&c'd+e>f`g\"h…", Detail = "套话/空词",
                Hint = "直接说做了什么", SuggestScheme = "去 AI 味",
            },
            new()
            {
                Id = "emoji-and-symbols", Title = "表情", Line = 9, Col = 1, Length = 2,
                Match = "😀", Snippet = "🎉🎉", Detail = "表情符号或装饰性符号",
                Hint = null, SuggestScheme = null,
            },
            new()
            {
                // Detail 里夹 CRLF：人读与 JSON 两条出口都要按 LF 收口
                Id = "dash", Title = "破折号", Line = 12, Col = 2, Length = 1,
                Match = "—", Snippet = "甲—乙", Detail = "上行\r\n下行",
            },
        },
    };

    private static string 逐份写(LintReportSet set, bool asJson)
    {
        var output = new StringWriter();
        using (var sink = LintOutput.BeginSink(output, asJson, version: set.Version))
        {
            foreach (var report in set.Reports)
                sink.Add(report);
            sink.Finish();
        }
        return output.ToString();
    }

    [Theory]
    [InlineData(0)]     // 一份都没有时 JSON 是空数组、人读是空串
    [InlineData(1)]
    [InlineData(3)]
    public void 逐份写与整段渲染逐字节相同_Json(int count)
    {
        var set = new LintReportSet();
        for (int i = 0; i < count; i++)
            set.Reports.Add(脏报告($"第{i}份.md", withBaseline: i == 0));

        Assert.Equal(LintOutput.Render(set, asJson: true), 逐份写(set, asJson: true));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void 逐份写与整段渲染逐字节相同_人读(int count)
    {
        var set = new LintReportSet();
        for (int i = 0; i < count; i++)
            set.Reports.Add(脏报告($"第{i}份.md", withBaseline: i == 0));

        Assert.Equal(LintOutput.Render(set, asJson: false), 逐份写(set, asJson: false));
    }

    [Fact]
    public void 逐份写_没用到的出口一个字节都不出()
    {
        // 一份都读不了时 stdout 必须是空的，不能先写个空数组/空报告出去
        Assert.Equal("", 逐份写(new LintReportSet(), asJson: false));

        string json = 逐份写(new LintReportSet(), asJson: true);
        Assert.Equal("{\n  \"Version\": 4,\n  \"Reports\": []\n}\n", json);
    }

    [Fact]
    public void 逐份写_隐形字符与HTML敏感字符的转义形状与整段相同()
    {
        var report = new LintReport
        {
            File = "a.md", Chars = 3,
            Hits = new List<LintHit>
            {
                // U+200B 由隐形字符这一层补转义（小写），U+00A0 内层自己就转义（大写）：
                // 两种大小写都在 --json 契约里，改转义器先对这两条
                new() { Id = "cliche-words", Match = "赋\u200b能\u00a0a<b&c'd+e>f`g\"h" },
            },
        };

        string json = 逐份写(new LintReportSet { Reports = { report } }, asJson: true);

        Assert.Contains("\\u200b", json);      // U+200B：隐形字符这一层补的，小写
        Assert.Contains("\\u00A0", json);      // U+00A0：内层编码器转的，大写
        Assert.Contains("\\u003C", json);      // <
        Assert.Contains("\\u0026", json);      // &
        Assert.Contains("\\u0027", json);      // '
        Assert.Contains("\\u002B", json);      // +
        Assert.Contains("\\u003E", json);      // >
        Assert.Contains("\\u0060", json);      // 反引号
        Assert.Equal(json, LintTextFormatter.EscapeInvisible(json));   // 已经收口过，再转一遍不动
        Assert.DoesNotContain("\r", json);      // 行尾与正文里的 CRLF 都收成 LF
    }
}
