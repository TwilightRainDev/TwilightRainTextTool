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
    public void Load_内置规则集可加载且通过校验()
    {
        var rules = LintRuleStore.Load();

        Assert.Equal(19, rules.Count);
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
        var offenders = LintRuleStore.GetDefaultRules()
            .Where(r => r.Kind == "literal")
            .SelectMany(r => r.Patterns.Select(p => (r.Id, Pattern: p)))
            .Where(x => x.Pattern.Contains('\\'))
            .Select(x => x.Id)
            .ToList();

        Assert.Empty(offenders);
    }
}
