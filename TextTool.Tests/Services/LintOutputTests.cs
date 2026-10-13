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
}
