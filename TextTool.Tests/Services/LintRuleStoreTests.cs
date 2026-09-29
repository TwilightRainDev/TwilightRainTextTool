namespace TextTool.Tests.Services;

public class LintRuleStoreTests
{
    private static LintRule Rule(string id, string pattern = "测试") => new()
    {
        Id = id, Group = "L", Title = "标题", Kind = "literal",
        Patterns = new List<string> { pattern }, Severity = "info",
    };

    [Fact]
    public void Merge_外部规则按Id覆盖内置_其余保留()
    {
        var builtIn = new List<LintRule> { Rule("L1"), Rule("L2") };
        var external = new List<LintRule> { Rule("L2", "覆盖"), Rule("L9") };

        var merged = LintRuleStore.Merge(builtIn, external);

        Assert.Equal(3, merged.Count);
        Assert.Equal(new[] { "L1", "L2", "L9" }, merged.Select(r => r.Id));
        Assert.Equal("覆盖", merged[1].Patterns[0]);   // 覆盖发生在原位置
    }

    [Fact]
    public void Validate_Id为空即抛()
    {
        var rule = Rule("L1");
        rule.Id = "";

        Assert.Throws<ArgumentException>(() => LintRuleStore.Validate(new List<LintRule> { rule }));
    }

    [Fact]
    public void Validate_Id重复即抛()
    {
        var rules = new List<LintRule> { Rule("L1"), Rule("L1") };

        Assert.Throws<ArgumentException>(() => LintRuleStore.Validate(rules));
    }

    [Fact]
    public void Validate_正则语法错即抛()
    {
        var rule = Rule("P1", "([a-z]");
        rule.Kind = "regex";

        var ex = Assert.Throws<ArgumentException>(() => LintRuleStore.Validate(new List<LintRule> { rule }));
        Assert.Contains("P1", ex.Message);
    }

    [Fact]
    public void Validate_Hints长度与Patterns不匹配即抛()
    {
        var rule = Rule("L7");
        rule.Patterns.Add("第二条");
        rule.Hints = new List<string> { "只有一条" };

        Assert.Throws<ArgumentException>(() => LintRuleStore.Validate(new List<LintRule> { rule }));
    }

    [Fact]
    public void Validate_Patterns为null即抛而非NRE()
    {
        var rule = Rule("L1");
        rule.Patterns = null!;   // JSON 里的 "Patterns": null 会走到这里

        var ex = Assert.Throws<ArgumentException>(() => LintRuleStore.Validate(new List<LintRule> { rule }));
        Assert.Contains("Patterns", ex.Message);
    }

    [Fact]
    public void Validate_Patterns含空串即抛()
    {
        var rule = Rule("L1", "");   // 空串模式会匹配任意位置，是配置错误

        Assert.Throws<ArgumentException>(() => LintRuleStore.Validate(new List<LintRule> { rule }));
    }

    [Fact]
    public void GetDefaultRules_数据规则19条且顺序完整()
    {
        var ids = LintRuleStore.GetDefaultRules().Select(r => r.Id).ToArray();

        Assert.Equal(
            new[] { "P1", "P2", "P3", "P6", "P7", "P8",
                    "L1", "L2", "L3", "L4", "L5", "L6", "L7",
                    "S1", "S2", "S3", "S4", "S5", "S6" },
            ids);
    }

    [Fact]
    public void GetDefaultRules_非默认字段真的绑定()
    {
        // Validate 不拦未知键：JSON 里把 MinCount 拼成 Mincount，值会静默回落到模型默认
        // （MinCount=1、TailChars=null、Hints=null），规则行为随数据变而无人报错。
        // 这里把四个非默认字段的实际取值钉在数据上。
        var rules = LintRuleStore.GetDefaultRules().ToDictionary(r => r.Id);

        Assert.Equal(3, rules["L2"].MinCount);

        Assert.Equal("paragraph", rules["S2"].Scope);
        Assert.Equal(2, rules["S2"].MinCount);

        Assert.Equal("paragraph", rules["S3"].Scope);
        Assert.Equal(30, rules["S3"].TailChars);

        Assert.Equal(
            new[] { "为了实现这一点", "因为下雨", "现在", "如果您需要帮助", "系统可以处理", "数据显示" },
            rules["L7"].Hints!.ToArray());
    }

    [Fact]
    public void Load_内置规则集可加载且通过校验()
    {
        var builtIn = LintRuleStore.GetDefaultRules();
        var rules = LintRuleStore.Load();

        // 计数不钉 19：Load 读的是程序目录的 lint_rules.json（装过方案的机器上可能有它），
        // 钉死计数就把这条用例变成环境耦合。内置 19 条的精确清单由
        // GetDefaultRules_数据规则19条且顺序完整 在资源层钉住，与目录内容无关。
        Assert.NotEmpty(builtIn);
        Assert.All(builtIn, b => Assert.Contains(rules, r => r.Id == b.Id));
        Assert.All(rules, r =>
        {
            Assert.False(string.IsNullOrWhiteSpace(r.Title));
            Assert.False(string.IsNullOrWhiteSpace(r.Detail));
        });
    }

    [Fact]
    public void GetDefaultRules_literal规则的Patterns不含反斜杠()
    {
        // literal 走 Regex.Escape：JSON 里写的 \uXXXX 解码后是六个普通字符，
        // 被 Escape 再转义一次后永远匹配不到真实字符——这类规则必须写成 regex
        var literal = LintRuleStore.GetDefaultRules().Where(r => r.Kind == "literal").ToList();

        // literal 规则集为空时下面的 Assert.Empty 必然空过，先把「有可查对象」钉住
        Assert.NotEmpty(literal);

        var offenders = literal
            .SelectMany(r => r.Patterns.Select(p => (r.Id, Pattern: p)))
            .Where(x => x.Pattern.Contains('\\'))
            .Select(x => x.Id)
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Merge_同Id外部规则出现两次不抛且以最后一次为准()
    {
        var builtIn = new List<LintRule> { Rule("L1") };
        var external = new List<LintRule> { Rule("L1", "第一次"), Rule("L1", "第二次") };

        // Load() 会先被 Validate(external) 挡住，但 Merge 自身也不能把索引留在被替换掉的对象上
        var merged = LintRuleStore.Merge(builtIn, external);

        Assert.Single(merged);
        Assert.Equal("第二次", merged[0].Patterns[0]);
    }

    [Fact]
    public void Merge_结果不与任一侧入参共享引用()
    {
        var builtIn = new List<LintRule> { Rule("L1") };
        var overwritten = Rule("L1", "覆盖");
        var appended = Rule("L9");
        var external = new List<LintRule> { overwritten, appended };

        var merged = LintRuleStore.Merge(builtIn, external);

        Assert.NotSame(builtIn[0], merged[0]);
        Assert.NotSame(overwritten, merged[0]);
        Assert.NotSame(appended, merged[1]);
    }

    [Fact]
    public void Validate_列表含null元素即抛而非NRE()
    {
        var rules = new List<LintRule> { Rule("L1"), null! };

        var ex = Assert.Throws<ArgumentException>(() => LintRuleStore.Validate(rules));

        Assert.Contains("null", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateNoAlgorithmIdCollision_外部Id撞算法Id即抛()
    {
        var external = new List<LintRule> { Rule("P4") };

        // 直接测守卫本身，不往程序目录写 lint_rules.json：那个文件会被并行跑的其它测试类读到
        var ex = Assert.Throws<ArgumentException>(() => LintRuleStore.ValidateNoAlgorithmIdCollision(external));

        Assert.Contains("P4", ex.Message);
    }

    [Fact]
    public void ValidateNoAlgorithmIdCollision_数据规则Id放行()
    {
        LintRuleStore.ValidateNoAlgorithmIdCollision(new List<LintRule> { Rule("L1"), Rule("X9") });
    }
}
