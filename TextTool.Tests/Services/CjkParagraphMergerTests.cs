namespace TextTool.Tests.Services;

public class CjkParagraphMergerTests
{
    // ================================================================
    //  Basic CJK merging
    // ================================================================

    [Fact]
    public void Fix_CjkEnding_MergesNextLine()
    {
        var input = new List<string> { "你好", "吗？" };
        var result = CjkParagraphMerger.Fix(input);

        Assert.Single(result);
        Assert.Equal("你好吗？", result[0]);
    }

    [Fact]
    public void Fix_NonCjkEnding_DoesNotMerge()
    {
        var input = new List<string> { "你好吗？", "下一个段落" };
        var result = CjkParagraphMerger.Fix(input);

        Assert.Equal(2, result.Count);
        Assert.Equal("你好吗？", result[0]);
        Assert.Equal("下一个段落", result[1]);
    }

    [Fact]
    public void Fix_EmptyLines_ReturnsAsIs()
    {
        var input = new List<string> { "", "" };
        var result = CjkParagraphMerger.Fix(input);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Fix_ConsecutiveCjk_MergesAllAsOne()
    {
        var input = new List<string> { "今天", "天气", "真", "好。" };
        var result = CjkParagraphMerger.Fix(input);

        Assert.Single(result);
        Assert.Equal("今天天气真好。", result[0]);
    }

    // ================================================================
    //  NoMergeSet interaction
    // ================================================================

    [Fact]
    public void Fix_NoMergeSet_CjkEndingNoMergeChar_Flushes()
    {
        var noMerge = new HashSet<char> { '好' }; // "好" is both CJK and in noMerge
        var input = new List<string> { "你好", "世界" };
        var result = CjkParagraphMerger.Fix(input, noMerge);

        // "好" in noMerge prevents CJK merging → 2 separate lines
        Assert.Equal(2, result.Count);
        Assert.Equal("你好", result[0]);
        Assert.Equal("世界", result[1]);
    }

    [Fact]
    public void Fix_NoMergePrecedesCjk_NoMergeWins()
    {
        // noMerge char "。" should prevent merging even though "。" is not CJK
        var noMerge = new HashSet<char> { '。' };
        var input = new List<string> { "你好。" };
        var result = CjkParagraphMerger.Fix(input, noMerge);

        // "。" triggers ShouldFlush → no merge with next line needed
        Assert.Single(result);
        Assert.Equal("你好。", result[0]);
    }

    // ================================================================
    //  Whitespace handling
    // ================================================================

    [Fact]
    public void Fix_TrailingWhitespace_SkippedForCjkCheck()
    {
        var input = new List<string> { "你好  ", "世界" };
        var result = CjkParagraphMerger.Fix(input);

        Assert.Single(result);
        Assert.Equal("你好  世界", result[0]);
    }

    [Fact]
    public void Fix_WhitespaceOnlyLine_DoesNotBreakCjkChain()
    {
        var input = new List<string> { "你好", "   ", "世界" };
        var result = CjkParagraphMerger.Fix(input);

        // 好 → CJK, so "你好" absorbs "   " then absorbs "世界"
        Assert.Single(result);
        Assert.Equal("你好   世界", result[0]); // 你好 + 3 spaces + 世界
    }

    // ================================================================
    //  CJK range coverage
    // ================================================================

    [Fact]
    public void Fix_CjkExtensionB_NotDetected_StopsMerge()
    {
        // U+20000 (CJK Extension B) is a surrogate pair in .NET strings.
        // IsCjkCharacter checks each char individually; surrogates don't match CJK ranges.
        // So Extension B characters at line endings don't trigger CJK merging.
        var input = new List<string> { "𠀀", "test" };
        var result = CjkParagraphMerger.Fix(input);

        Assert.Equal(2, result.Count);
        Assert.Equal("𠀀", result[0]);
        Assert.Equal("test", result[1]);
    }
}
