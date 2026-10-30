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
                new() { Id = "parallel-contrast", Title = "对举句式", Line = 20, Col = 1, Snippet = "…", Detail = "不是A而是B" },
                new() { Id = "parallel-contrast", Title = "对举句式", Line = 12, Col = 5, Snippet = "…不是A而是B…", Detail = "不是A而是B" },
                new() { Id = "filler-phrases", Title = "填充短语", Line = 3, Col = 2, Snippet = "…在这个时间点…", Hint = "现在" },
            },
            Notes = new List<LintNote> { new() { Id = "paragraph-length-uniform", Text = "段落长度过于均一" } },
        };

        var text = LintTextFormatter.Format(report);

        Assert.Contains("[命中] parallel-contrast 对举句式（2 处）", text);
        Assert.Contains("第 12 行 第 5 列", text);
        Assert.True(text.IndexOf("第 12 行") < text.IndexOf("第 20 行"), "组内应按行号升序");
        Assert.Contains("<- 现在", text);            // Hint 随命中给出
        Assert.Contains("<- 不是A而是B", text);      // Detail 走的是同一渲染分支
        Assert.Contains("[统计]", text);
        Assert.Contains("段落口径 markdown", text);  // 口径随报告带出，人读侧此前只钉了「只报位置」
        Assert.Contains("只报位置", text);
    }

    [Fact]
    public void Format_同一模板反复时折叠成样例加统计()
    {
        // 2717 字里 135 处对举：处数与密度都过线，人读视图只列三条例样
        var report = new LintReport { File = "a.md", Chars = 2717 };
        for (int i = 1; i <= 135; i++)
            report.Hits.Add(new LintHit { Id = "parallel-contrast", Title = "对举句式", Line = i, Col = 1, Snippet = "…" });
        report.CollapseAtDensity["parallel-contrast"] = 10;

        var text = LintTextFormatter.Format(report);

        Assert.Contains("（135 处，每千字 49.7 处）", text);
        Assert.Contains("只列样例", text);
        Assert.Contains("其余 132 处见 --json", text);
        // 只列三条例样：整份人读文本的行数远小于处数
        Assert.True(text.Split('\n').Length < 12, "折叠后不应逐条列位置");
    }

    [Fact]
    public void Format_密度不够或处数不够就不折叠()
    {
        // 同样的 135 处摊在 9 万字里：密度 1.5 处/千字，读者该逐条看
        var longReport = new LintReport { File = "b.md", Chars = 90_000 };
        for (int i = 1; i <= 135; i++)
            longReport.Hits.Add(new LintHit { Id = "parallel-contrast", Title = "对举句式", Line = i, Col = 1, Snippet = "…" });
        longReport.CollapseAtDensity["parallel-contrast"] = 10;
        Assert.DoesNotContain("只列样例", LintTextFormatter.Format(longReport));

        // 密度高但只有三处：折叠会让读者连样例都看不全
        var fewReport = new LintReport { File = "c.md", Chars = 100 };
        for (int i = 1; i <= 3; i++)
            fewReport.Hits.Add(new LintHit { Id = "parallel-contrast", Title = "对举句式", Line = i, Col = 1, Snippet = "…" });
        fewReport.CollapseAtDensity["parallel-contrast"] = 10;
        Assert.DoesNotContain("只列样例", LintTextFormatter.Format(fewReport));
    }

    [Fact]
    public void Format_没开折叠的规则照样逐条列()
    {
        // 标点、隐形字符这类位置是给人照着改的，处数再多也不折叠
        var report = new LintReport { File = "d.md", Chars = 300 };
        for (int i = 1; i <= 20; i++)
            report.Hits.Add(new LintHit { Id = "mixed-punctuation-width", Title = "全半角混排", Line = i, Col = 1, Snippet = "…" });
        report.CollapseAtDensity["parallel-contrast"] = 10;

        var text = LintTextFormatter.Format(report);

        Assert.Contains("（20 处）", text);
        Assert.DoesNotContain("只列样例", text);
        Assert.Contains("第 20 行", text);
    }

    [Fact]
    public void Json_不受折叠影响()
    {
        // 折叠只是人读视图：机器要的是全部位置，契约一个字不改
        var report = new LintReport { File = "a.md", Chars = 2717 };
        for (int i = 1; i <= 135; i++)
            report.Hits.Add(new LintHit { Id = "parallel-contrast", Title = "对举句式", Line = i, Col = 1, Snippet = "…" });
        report.CollapseAtDensity["parallel-contrast"] = 10;
        var set = new LintReportSet { Reports = { report } };

        var json = LintOutput.Render(set, asJson: true);

        Assert.DoesNotContain("只列样例", json);
        Assert.DoesNotContain("CollapseAtDensity", json);
    }

    [Fact]
    public void 端到端_密集对举在人读里折叠_json里位置全在()
    {
        // 走一遍引擎到输出的整条路：折叠口径要能穿过 Filter，否则人读视图会退回逐条列
        string text = string.Join("", Enumerable.Repeat("不是甲而是乙。", 12));
        var set = LintRunner.Run(new[] { ("样本.txt", text) }, null, null, null);

        var human = LintOutput.Render(set, asJson: false);
        Assert.Contains("只列样例", human);
        Assert.Contains("其余 9 处见 --json", human);

        var json = LintOutput.Render(set, asJson: true);
        Assert.DoesNotContain("只列样例", json);
        Assert.Equal(12, System.Text.RegularExpressions.Regex.Matches(
            json, "\"Id\": \"parallel-contrast\"").Count);
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
                new() { Id = "ellipsis-style", Title = "省略号风格", Line = 1, Col = 3, Snippet = "…", SuggestScheme = "去AI味标点归一" },
            },
        };

        var text = LintTextFormatter.Format(report);

        Assert.Contains("<- 方案：去AI味标点归一", text);
    }

    [Fact]
    public void Format_无建议方案时不渲染方案后缀()
    {
        // 反例：SuggestScheme 为空时不得出现「方案：」后缀（正例见 Format_渲染建议方案）
        var report = new LintReport
        {
            File = "a.md", Chars = 10,
            Hits = new List<LintHit>
            {
                new() { Id = "ellipsis-style", Title = "省略号风格", Line = 1, Col = 3, Snippet = "…" },
            },
        };

        Assert.DoesNotContain("方案：", LintTextFormatter.Format(report));
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
