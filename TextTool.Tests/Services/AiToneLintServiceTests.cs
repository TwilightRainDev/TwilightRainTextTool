namespace TextTool.Tests.Services;

public class AiToneLintServiceTests
{
    private static AiToneLintService Service(params LintRule[] rules) => new(rules);

    private static LintRule Rule(string id, string kind, params string[] patterns) => new()
    {
        Id = id, Group = "L", Title = id, Kind = kind,
        Patterns = patterns.ToList(), Severity = "warn", Detail = "d",
    };

    [Fact]
    public void Scan_字面规则命中并给出行列与匹配原文()
    {
        var text = "第一行\n第二行有 赋能 二字";
        var report = Service(Rule("L1", "literal", "赋能")).Scan(text, "t.txt");

        var hit = Assert.Single(report.Hits);
        Assert.Equal(2, hit.Line);
        Assert.Equal(6, hit.Col);
        Assert.Equal("赋能", hit.Match);
        Assert.Contains("赋能", hit.Snippet);
        Assert.Equal(14, report.Chars);
    }

    [Fact]
    public void Scan_正则规则命中()
    {
        var report = Service(Rule("S1", "regex", "不是[^。]*?而是"))
            .Scan("这不是数据问题而是口径问题。", "t.txt");

        Assert.Single(report.Hits);
        Assert.Equal("不是数据问题而是", Assert.Single(report.Hits).Match);
    }

    [Fact]
    public void Scan_重复段落中第二处定位正确()
    {
        var text = "同样的一段\n同样的一段";
        var report = Service(Rule("L1", "literal", "同样")).Scan(text, "t.txt");

        Assert.Equal(2, report.Hits.Count);
        Assert.Equal(1, report.Hits[0].Line);
        Assert.Equal(2, report.Hits[1].Line);
        Assert.Equal(1, report.Hits[1].Col);
    }

    [Fact]
    public void Scan_MinCount未达阈值不报()
    {
        var rule = Rule("L2", "literal", "此外");
        rule.MinCount = 3;

        Assert.Empty(Service(rule).Scan("此外只有一次。", "t.txt").Hits);
        Assert.Equal(3, Service(rule).Scan("此外，此外，此外。", "t.txt").Hits.Count);
    }

    [Fact]
    public void Scan_Snippet不劈开代理对()
    {
        // 5 个 emoji（10 个 UTF-16 码元）+ 1 个字符后命中：左边界 11-8=3 正落在
        // 第 2 个 emoji 的代理对中间，不外扩就会切出落单代理项
        var text = "😀😀😀😀😀x不是A而是B";
        var hit = Assert.Single(Service(Rule("S1", "regex", "不是A而是B")).Scan(text, "t.txt").Hits);

        Assert.False(HasLoneSurrogate(hit.Snippet));
    }

    private static bool HasLoneSurrogate(string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            if (char.IsHighSurrogate(s[i]))
            {
                if (i + 1 >= s.Length || !char.IsLowSurrogate(s[i + 1])) return true;
                i++;
            }
            else if (char.IsLowSurrogate(s[i])) return true;
        }
        return false;
    }

    [Fact]
    public void Scan_未命中返回空报告且Chars正确()
    {
        var report = Service(Rule("L1", "literal", "赋能")).Scan("干净的一段话。", "t.txt");

        Assert.Empty(report.Hits);
        Assert.Empty(report.Notes);
        Assert.Equal(7, report.Chars);
        Assert.Equal("t.txt", report.File);
    }
}
