namespace TextTool.Tests.Services;

public class PunctuationReplacerTests
{
    // ================================================================
    //  Basic replacement
    // ================================================================

    [Fact]
    public void Apply_SingleRule_ReplacesCorrectly()
    {
        var rules = new List<ReplaceRule> { new() { Find = "。。", Replace = "。" } };
        var result = PunctuationReplacer.Apply("你好。。。世界", rules);

        Assert.Equal("你好。。世界", result); // 3 → 2 = 1 replacement
    }

    [Fact]
    public void Apply_MultipleRules_ExecutesInOrder()
    {
        var rules = new List<ReplaceRule>
        {
            new() { Find = "a", Replace = "b" },
            new() { Find = "b", Replace = "c" }
        };
        var result = PunctuationReplacer.Apply("a", rules);

        // First rule: a → b, Second rule: b → c
        Assert.Equal("c", result);
    }

    [Fact]
    public void Apply_NoMatch_ReturnsOriginal()
    {
        var rules = new List<ReplaceRule> { new() { Find = "x", Replace = "y" } };
        var result = PunctuationReplacer.Apply("hello", rules);

        Assert.Equal("hello", result);
    }

    // ================================================================
    //  Edge cases
    // ================================================================

    [Fact]
    public void Apply_EmptyRules_ReturnsOriginal()
    {
        var result = PunctuationReplacer.Apply("hello", new List<ReplaceRule>());
        Assert.Equal("hello", result);
    }

    [Fact]
    public void Apply_EmptyFind_SkipsRule()
    {
        var rules = new List<ReplaceRule>
        {
            new() { Find = "", Replace = "x" },
            new() { Find = "hello", Replace = "world" }
        };
        var result = PunctuationReplacer.Apply("hello", rules);

        Assert.Equal("world", result);
    }

    [Fact]
    public void Apply_EmptyText_ReturnsEmpty()
    {
        var rules = new List<ReplaceRule> { new() { Find = "a", Replace = "b" } };
        var result = PunctuationReplacer.Apply("", rules);

        Assert.Equal("", result);
    }

    [Fact]
    public void Apply_NullFindInRule_DoesNotThrow()
    {
        var rules = new List<ReplaceRule> { new() { Find = null!, Replace = "x" } };
        var exception = Record.Exception(() => PunctuationReplacer.Apply("test", rules));

        Assert.Null(exception);
    }

    // ================================================================
    //  CJK punctuation cases
    // ================================================================

    [Fact]
    public void Apply_CjkPunctuation_ReplacesCorrectly()
    {
        var rules = new List<ReplaceRule>
        {
            new() { Find = "，", Replace = "," },
            new() { Find = "。", Replace = "." }
        };
        var result = PunctuationReplacer.Apply("你好，世界。", rules);

        Assert.Equal("你好,世界.", result);
    }
}
