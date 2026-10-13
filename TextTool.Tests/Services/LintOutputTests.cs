namespace TextTool.Tests.Services;

public class LintOutputTests
{
    private static LintReport Report(string file) => new() { File = file, Chars = 10 };

    [Fact]
    public void Render_人读多报告之间空一行且以单个LF结尾()
    {
        var set = new LintReportSet { Reports = { Report("a.md"), Report("b.md") } };

        var text = LintOutput.Render(set, asJson: false);

        string first = LintTextFormatter.Format(set.Reports[0]).Replace("\r\n", "\n");
        string second = LintTextFormatter.Format(set.Reports[1]).Replace("\r\n", "\n");
        Assert.Equal(first + "\n" + "\n" + second + "\n", text);   // 报告间空一行，尾部只一个换行
        Assert.DoesNotContain("\r", text);
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

        // 先证明原始渲染确实夹带 CRLF，否则下面的断言是空转
        Assert.Contains("\r\n", LintTextFormatter.Format(report));

        var text = LintOutput.Render(new LintReportSet { Reports = { report } }, asJson: false);

        Assert.DoesNotContain("\r", text);
    }
}
