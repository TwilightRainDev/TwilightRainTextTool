namespace TextTool.Tests.Services;

public class LintTextFormatterTests
{
    [Fact]
    public void Format_按Id分组并显示行列与建议()
    {
        var report = new LintReport
        {
            File = "a.md", Chars = 100, ParagraphMode = "markdown",
            Hits = new List<LintHit>
            {
                // 故意倒序给：引擎的命中顺序是 规则→Pattern→位置，同一规则的多条
                // Pattern 会让行号来回跳，渲染层负责排序
                new() { Id = "S1", Title = "对举句式", Line = 20, Col = 1, Snippet = "…", Detail = "不是A而是B" },
                new() { Id = "S1", Title = "对举句式", Line = 12, Col = 5, Snippet = "…不是A而是B…", Detail = "不是A而是B" },
                new() { Id = "L7", Title = "填充短语", Line = 3, Col = 2, Snippet = "…在这个时间点…", Hint = "现在" },
            },
            Notes = new List<LintNote> { new() { Id = "C1", Text = "段落长度过于均一" } },
        };

        var text = LintTextFormatter.Format(report);

        Assert.Contains("[命中] S1 对举句式（2 处）", text);
        Assert.Contains("第 12 行 第 5 列", text);
        Assert.True(text.IndexOf("第 12 行") < text.IndexOf("第 20 行"), "组内应按行号升序");
        Assert.Contains("<- 现在", text);            // Hint 随命中给出
        Assert.Contains("<- 不是A而是B", text);      // Detail 走的是同一渲染分支
        Assert.Contains("[统计]", text);
        Assert.Contains("段落口径 markdown", text);  // 口径随报告带出，人读侧此前只钉了「只报位置」
        Assert.Contains("只报位置", text);
    }

    [Fact]
    public void Format_无命中给出OK行()
    {
        var text = LintTextFormatter.Format(new LintReport { File = "a.md", Chars = 5 });

        Assert.Contains("[OK]", text);
    }

    [Fact]
    public void Format_渲染建议方案()
    {
        var report = new LintReport
        {
            File = "a.md", Chars = 10,
            Hits = new List<LintHit>
            {
                new() { Id = "P2", Title = "省略号风格", Line = 1, Col = 3, Snippet = "…", SuggestScheme = "去AI味标点归一" },
            },
        };

        var text = LintTextFormatter.Format(report);

        Assert.Contains("<- 方案：去AI味标点归一", text);
    }

    [Fact]
    public void Format_末尾不带换行()
    {
        var text = LintTextFormatter.Format(new LintReport { File = "a.md", Chars = 5 });

        Assert.False(text.EndsWith('\n'), "尾换行由调用方决定：CLI 追加一个，GUI 用 AppendLine");
    }

    [Fact]
    public void Visible_不可见字符转义为uXXXX()
    {
        var text = LintTextFormatter.Visible("前\u200b后\u00a0终");

        Assert.Equal("前\\u200b后\\u00a0终", text);
    }

    [Fact]
    public void Visible_正文中文原样保留()
    {
        Assert.Equal("正常中文", LintTextFormatter.Visible("正常中文"));
    }
}
