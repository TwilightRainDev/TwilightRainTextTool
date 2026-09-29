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
        var report = Service().Scan(text, "t.txt");

        // 先证明这条文本确实产出了 C 组统计项：没有它，下面的 DoesNotContain 是空转
        Assert.Contains(report.Notes, n => n.Id.StartsWith('C'));
        Assert.DoesNotContain(report.Hits, h => h.Group == "C");
        Assert.DoesNotContain(report.Hits, h => h.Id.StartsWith('C'));
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
    public void Scan_括号全半角混用产出P5()
    {
        var hit = Assert.Single(Service().Scan("他说(好)，又说（行）。", "t.txt").Hits.Where(h => h.Id == "P5"));

        Assert.Contains("半角 2 个", hit.Detail);
        Assert.Contains("全角 2 个", hit.Detail);
    }

    [Fact]
    public void Scan_句长过于整齐产出C2统计项()
    {
        // 10 句、每句 13 字，长度完全一致 → 变异系数 0
        var text = string.Concat(Enumerable.Repeat("今天天气很好我们去公园散步。", 10));

        var note = Assert.Single(Service().Scan(text, "t.txt").Notes.Where(n => n.Id == "C2"));

        Assert.Contains("变异系数 0.00，10 句", note.Text);   // 长度全等 → 变异系数 0，句数 10
    }

    [Fact]
    public void Scan_段首重复产出C3统计项()
    {
        var text = string.Join("\n", "我们去看海。", "我们去爬山。", "我们回家吧。", "我们去吃饭。");

        var note = Assert.Single(Service().Scan(text, "t.txt").Notes.Where(n => n.Id == "C3"));

        Assert.Contains("「我们」×4", note.Text);   // 4 段全部以「我们」起头
    }

    [Fact]
    public void Scan_叹号密度偏高产出C4统计项()
    {
        var text = new string('好', 400) + "！！！";   // 403 字 / 3 个叹号，密度 7.4‰ > 3‰

        var note = Assert.Single(Service().Scan(text, "t.txt").Notes.Where(n => n.Id == "C4"));

        Assert.Contains("叹号密度偏高：3 个 / 403 字", note.Text);   // 密度 7.4‰，门槛 3‰
    }

    [Fact]
    public void Scan_序数词骨架密度产出C5统计项()
    {
        var text = "首先看甲。其次看乙。";

        var note = Assert.Single(Service().Scan(text, "t.txt").Notes.Where(n => n.Id == "C5"));

        Assert.Contains("「首先/其次/最后」出现 2 次", note.Text);   // 门槛 2 次
    }

    [Fact]
    public void Scan_显式序号标记产出C6统计项()
    {
        var text = "1. 甲\n2. 乙\n3. 丙\n";

        var note = Assert.Single(Service().Scan(text, "t.txt").Notes.Where(n => n.Id == "C6"));

        Assert.Contains("显式序号标记 3 处", note.Text);
    }

    [Fact]
    public void Scan_显式序号标记不足三处不报C6()
    {
        var report = Service().Scan("1. 甲\n正文一段。\n", "t.txt");

        Assert.DoesNotContain(report.Notes, n => n.Id == "C6");
    }

    [Fact]
    public void Scan_中文序号与括号序号也计入C6()
    {
        var text = "一、甲\n（2）乙\n3) 丙\n";

        var note = Assert.Single(Service().Scan(text, "t.txt").Notes.Where(n => n.Id == "C6"));

        // 整串断言：只写 "3 处" 的话 "13 处"、"23 处" 也会通过
        Assert.Contains("显式序号标记 3 处", note.Text);
    }

    [Theory]
    [InlineData("1、甲\n2、乙\n3、丙\n")]
    [InlineData("(1) 甲\n(2) 乙\n(3) 丙\n")]
    [InlineData("一. 甲\n二. 乙\n三. 丙\n")]
    public void Scan_显式序号标记其他形态也计入C6(string text)
    {
        // 三种替代分支：数字顿号、半角括号、中文数字点号
        var note = Assert.Single(Service().Scan(text, "t.txt").Notes.Where(n => n.Id == "C6"));

        Assert.Contains("显式序号标记 3 处", note.Text);
    }

    [Fact]
    public void Scan_序号标记只在行首计数()
    {
        // 序号不在行首（前有正文）：不计数
        var 行中 = Service().Scan("甲 1. 乙\n甲 2. 乙\n甲 3. 乙\n", "t.txt");
        Assert.DoesNotContain(行中.Notes, n => n.Id == "C6");

        // 前导空白不破行首：计数
        var 缩进 = Service().Scan("  1. 甲\n  2. 乙\n  3. 丙\n", "t.txt");
        Assert.Contains(缩进.Notes, n => n.Id == "C6");
    }

    [Fact]
    public void AllRuleIds_含数据规则与算法规则()
    {
        var ids = AiToneLintService.AllRuleIds();

        Assert.Contains("P1", ids);
        Assert.Contains("P4", ids);
        Assert.Contains("C5", ids);
        Assert.Contains("C6", ids);
    }

    [Fact]
    public void Scan_命中按行列升序()
    {
        var rule = Rule("L1", "literal", "乙", "甲");   // Pattern 顺序与文本位置相反

        var report = Service(rule).Scan("甲乙", "t.txt");

        Assert.Equal(new[] { "甲", "乙" }, report.Hits.Select(h => h.Match));
    }

    [Fact]
    public void Scan_同位置多规则命中保持引擎顺序()
    {
        var text = "反复。。。。";
        var ids = new AiToneLintService(LintRuleStore.Load()).Scan(text, "t.txt")
            .Hits.Where(h => h.Line == 1 && h.Col == 3).Select(h => h.Id).ToList();

        Assert.Equal(new[] { "P1", "P2" }, ids);   // P1 在前：同跨度双报是既定行为，排序不得打乱
    }

    [Fact]
    public void Scan_无空行文本判为行式口径()
    {
        var text = "首先看这件事。\n其次看那件事。";
        var rule = Rule("S2", "regex", "首先|其次|最后");
        rule.Scope = "paragraph";
        rule.MinCount = 2;

        var report = Service(rule).Scan(text, "t.txt");

        Assert.Equal("line", report.ParagraphMode);
        Assert.Empty(report.Hits);   // 两行各自成段，段内各 1 次
    }

    [Fact]
    public void Scan_空行密度达标判为Markdown口径且软换行同段()
    {
        var text = "首先看这件事，\n其次看那件事。\n\n下一段。\n";
        var rule = Rule("S2", "regex", "首先|其次|最后");
        rule.Scope = "paragraph";
        rule.MinCount = 2;

        var report = Service(rule).Scan(text, "t.txt");

        Assert.Equal("markdown", report.ParagraphMode);
        Assert.Equal(2, report.Hits.Count);   // 软换行的两行属同一段，段内合计 2 次
    }

    [Fact]
    public void Scan_Markdown列表项各自成段()
    {
        var text = "- 首先看这件事。\n- 其次看那件事。\n\n尾段。";
        var rule = Rule("S2", "regex", "首先|其次|最后");
        rule.Scope = "paragraph";
        rule.MinCount = 2;

        Assert.Empty(Service(rule).Scan(text, "t.txt").Hits);   // 并成一段会误报，列表项必须断开
    }

    [Theory]
    [InlineData("* ")]
    [InlineData("+ ")]
    [InlineData("> ")]
    [InlineData("1. ")]
    [InlineData("1、")]
    public void Scan_Markdown块行各形态各自成段(string marker)
    {
        // IsBlockLine 的 * / + / > 与数字编号分支：漏掉任一条，两行会并成一段而误报
        var text = $"{marker}首先看这件事。\n{marker}其次看那件事。\n\n尾段。";
        var rule = Rule("S2", "regex", "首先|其次|最后");
        rule.Scope = "paragraph";
        rule.MinCount = 2;

        Assert.Empty(Service(rule).Scan(text, "t.txt").Hits);
    }

    [Fact]
    public void Scan_Markdown段末窗口按整段计算()
    {
        var text = "前面的话。\n后缀行未来可期\n\n尾段。\n";
        var rule = Rule("S3", "regex", "未来可期");
        rule.Scope = "paragraph";
        rule.TailChars = 30;

        var hit = Assert.Single(Service(rule).Scan(text, "t.txt").Hits);

        Assert.Equal(2, hit.Line);   // 命中在第二行，属第一段（整段 30 字窗口内）
    }

    [Fact]
    public void Scan_Markdown段长超窗口时命中落在窗口外不报()
    {
        // 上面那条的段落只有 18 字 < TailChars 30，窗口起点恒为 0，没验到「整段」。
        // 这条的段长 45 字 > 30：窗口起点在段内第 15 字，命中在段首故不报。
        // 窗口若按行算，首行只有 4 字、起点为 0，这条就会误报——「整段」由此可验。
        var text = "未来可期\n" + new string('字', 40) + "\n\n尾段。\n";
        var rule = Rule("S3", "regex", "未来可期");
        rule.Scope = "paragraph";
        rule.TailChars = 30;

        Assert.Empty(Service(rule).Scan(text, "t.txt").Hits);
    }

    [Fact]
    public void Scan_跨行命中的Snippet保留原文()
    {
        var rule = Rule("L4", "regex", @"截至\s*\d{4}\s*年");

        var hit = Assert.Single(Service(rule).Scan("前文。\n截至\n2024 年发布。", "t.txt").Hits);

        Assert.Contains(hit.Match, hit.Snippet);   // Snippet 是原文切片：跨行命中不再丢掉 Match 原文
        Assert.Contains("\n", hit.Snippet);
    }

    [Fact]
    public void DetectParagraphMode_阈值边界()
    {
        // 10 行里 1 空行 = 10%：达到阈值（空行必须在中间，末尾的换行不计入行数）
        Assert.Equal("markdown", AiToneLintService.DetectParagraphMode("行\n行\n行\n行\n行\n\n行\n行\n行\n行"));
        // 11 行里 1 空行 = 9.1%：未达阈值
        Assert.Equal("line", AiToneLintService.DetectParagraphMode("行\n行\n行\n行\n行\n行\n\n行\n行\n行\n行"));
    }

    [Fact]
    public void DetectParagraphMode_末尾换行不计入行数()
    {
        // 10 个内容行后跟两个换行：剥掉尾换行后 1/11 = 9.1% 判 line；
        // 不剥则是 2/12 = 16.7% 判 markdown——这条钉住「末尾换行不算行」
        var text = string.Join("\n", Enumerable.Repeat("行", 10)) + "\n\n";

        Assert.Equal("line", AiToneLintService.DetectParagraphMode(text));
    }

    [Fact]
    public void DetectParagraphMode_空与纯空白判为行式()
    {
        Assert.Equal("line", AiToneLintService.DetectParagraphMode(""));
        Assert.Equal("line", AiToneLintService.DetectParagraphMode("\n \n\t\n"));
    }

    [Fact]
    public void Scan_行首索引与逐字符扫描结果一致()
    {
        var text = "第一行\n第二行有 赋能 二字\n第三行有 赋能 二字";
        var report = Service(Rule("L1", "literal", "赋能")).Scan(text, "t.txt");

        Assert.Equal(2, report.Hits.Count);
        Assert.Equal((2, 6), (report.Hits[0].Line, report.Hits[0].Col));
        Assert.Equal((3, 6), (report.Hits[1].Line, report.Hits[1].Col));
    }
}
