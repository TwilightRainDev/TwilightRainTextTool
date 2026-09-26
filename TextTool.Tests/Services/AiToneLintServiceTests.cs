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

    [Fact]
    public void Scan_段内序数词需同段出现两次才报()
    {
        var rule = Rule("S2", "regex", "首先|其次|最后");
        rule.Scope = "paragraph";
        rule.MinCount = 2;

        Assert.Empty(Service(rule).Scan("首先看这件事。\n其次看那件事。", "t.txt").Hits);
        Assert.Equal(2, Service(rule).Scan("首先看这件事，其次看那件事。", "t.txt").Hits.Count);
    }

    [Fact]
    public void Scan_TailChars只在段末范围内命中()
    {
        var rule = Rule("S3", "regex", "未来可期");
        rule.Scope = "paragraph";
        rule.TailChars = 30;

        var 段中 = "未来可期" + new string('字', 40);
        var 段末 = new string('字', 40) + "未来可期";

        Assert.Empty(Service(rule).Scan(段中, "t.txt").Hits);
        Assert.Single(Service(rule).Scan(段末, "t.txt").Hits);
    }

    [Fact]
    public void Scan_真实规则集端到端冒烟()
    {
        // 前面各例都用自带规则，从不碰 default_lint_rules.json 的真实数据——
        // P7 静默失效（literal + JSON 转义）正是这样漏掉的，这条负责端到端兜底。
        // 本任务让段落作用域落地后，这条才能跑全量规则集（Task 5 时 S2/S3 会撞上抛异常的桩）。
        var service = new AiToneLintService(LintRuleStore.Load());
        var text = "此外，此外，此外。这不是数据问题而是口径问题。\u200B";

        var ids = service.Scan(text, "t.txt").Hits.Select(h => h.Id).Distinct().OrderBy(x => x).ToList();

        Assert.Contains("L2", ids);   // AI 高频词（MinCount 3 达标才报）
        Assert.Contains("S1", ids);   // 对举句式
        Assert.Contains("P7", ids);   // 隐形字符——写作 literal 时这条会静默消失
    }

    [Fact]
    public void Scan_Snippet右边界也不劈开代理对()
    {
        // 命中在前、emoji 在后：右边界 = index+length+8 = 14 正落在 emoji 的代理对中间
        // （Task 5 的用例窗口右端一律到 text.Length，hi++ 分支从未被执行）
        var text = "不是A而是B" + new string('x', 5) + "😀😀";
        var hit = Assert.Single(Service(Rule("S1", "regex", "不是A而是B")).Scan(text, "t.txt").Hits);

        Assert.False(HasLoneSurrogate(hit.Snippet));
    }

    [Fact]
    public void Scan_MinCount按Pattern各自计数而非整规则合计()
    {
        // 三条 Pattern 各出现 1 次：整规则合计 3 已达阈值，按 Pattern 计数则都不达标
        var rule = Rule("L2", "literal", "此外", "赋能", "抓手");
        rule.MinCount = 2;

        Assert.Empty(Service(rule).Scan("此外，赋能，抓手。", "t.txt").Hits);
    }

    [Fact]
    public void Scan_段落命中位置为绝对行列()
    {
        var rule = Rule("S3", "regex", "未来可期");
        rule.Scope = "paragraph";
        var text = "第一段。\n第二段末未来可期";

        var hit = Assert.Single(Service(rule).Scan(text, "t.txt").Hits);

        Assert.Equal(2, hit.Line);
        Assert.Equal(5, hit.Col);
    }

    [Fact]
    public void Scan_引号风格混用产出P4且给出规模()
    {
        var text = "他说\"好\"，又说「行」。";

        var hit = Assert.Single(Service().Scan(text, "t.txt").Hits.Where(h => h.Id == "P4"));

        Assert.Contains("ASCII 直引号", hit.Detail);
        Assert.Contains("直角引号", hit.Detail);
    }

    [Fact]
    public void Scan_引号风格单一不报P4()
    {
        var report = Service().Scan("他说「好」，又说「行」。", "t.txt");

        Assert.DoesNotContain(report.Hits, h => h.Id == "P4");
    }

    [Fact]
    public void Scan_段落长度均一产出C1统计项()
    {
        var text = string.Join("\n", Enumerable.Repeat(new string('字', 20), 5));

        var note = Assert.Single(Service().Scan(text, "t.txt").Notes.Where(n => n.Id == "C1"));

        Assert.Contains("变异系数", note.Text);
    }

    [Fact]
    public void Scan_统计项不进Hits()
    {
        var text = string.Join("\n", Enumerable.Repeat(new string('字', 20), 5));

        Assert.DoesNotContain(Service().Scan(text, "t.txt").Hits, h => h.Group == "C");
    }

    [Fact]
    public void Scan_算法命中的Severity只用小写()
    {
        // 算法规则的命中不走 LintRuleStore.Validate，Severity 由代码直接给定；
        // 而 Filter 的 warn 比较是大小写敏感的——写成 "Warn" 会被 --min-severity
        // 静默丢弃、退出码变 0，是 CI 关卡最怕的失败方向。这条钉住契约。
        var report = Service().Scan("他说\"好\"，又说「行」。", "t.txt");

        Assert.All(report.Hits.Where(h => h.Group == "P"),
            h => Assert.Contains(h.Severity, new[] { "info", "warn" }));
    }

    [Fact]
    public void AllRuleIds_含数据规则与算法规则()
    {
        var ids = AiToneLintService.AllRuleIds();

        Assert.Contains("P1", ids);
        Assert.Contains("P4", ids);
        Assert.Contains("C5", ids);
    }
}
