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
}
