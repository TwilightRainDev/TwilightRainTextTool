namespace TextTool.Tests.Services;

public class PunctTruncationMergerTests
{
    // ================================================================
    //  Basic punct merging
    // ================================================================

    [Fact]
    public void Fix_LineEndsWithMergePunct_MergesNextLine()
    {
        var mergeSet = new HashSet<char> { '，' };
        var input = new List<string> { "回家吧，", "孩子" };
        var result = PunctTruncationMerger.Fix(input, mergeSet);

        Assert.Single(result);
        Assert.Equal("回家吧，孩子", result[0]);
    }

    [Fact]
    public void Fix_LineEndsWithoutMergePunct_DoesNotMerge()
    {
        var mergeSet = new HashSet<char> { '，' };
        var input = new List<string> { "回家吧", "孩子" };
        var result = PunctTruncationMerger.Fix(input, mergeSet);

        Assert.Equal(2, result.Count);
        Assert.Equal("回家吧", result[0]);
        Assert.Equal("孩子", result[1]);
    }

    [Fact]
    public void Fix_EmptyMergeSet_NothingMerges()
    {
        var input = new List<string> { "你好，", "世界" };
        var result = PunctTruncationMerger.Fix(input, new HashSet<char>());

        Assert.Equal(2, result.Count);
    }

    // ================================================================
    //  NoMergeSet priority
    // ================================================================

    [Fact]
    public void Fix_NoMergeTakesPriority_OverMergePunct()
    {
        var mergeSet = new HashSet<char> { '，' };
        var noMerge = new HashSet<char> { '。' };
        // Even though "。" ends the line, noMerge should prevent merging
        var input = new List<string> { "回家吧。", "孩子" };
        var result = PunctTruncationMerger.Fix(input, mergeSet, noMerge);

        Assert.Equal(2, result.Count);
        Assert.Equal("回家吧。", result[0]);
        Assert.Equal("孩子", result[1]);
    }

    [Fact]
    public void Fix_MultipleMergeChars_AllTriggerMerge()
    {
        var mergeSet = new HashSet<char> { '，', '、', '；' };
        var input = new List<string> { "第一，", "第二、", "第三；", "结束" };
        var result = PunctTruncationMerger.Fix(input, mergeSet);

        Assert.Single(result);
        Assert.Equal("第一，第二、第三；结束", result[0]);
    }

    // ================================================================
    //  Edge cases
    // ================================================================

    [Fact]
    public void Fix_TrailingWhitespace_SkippedForPunctCheck()
    {
        var mergeSet = new HashSet<char> { '，' };
        var input = new List<string> { "你好，  ", "世界" };
        var result = PunctTruncationMerger.Fix(input, mergeSet);

        Assert.Single(result);
        Assert.Equal("你好，  世界", result[0]);
    }

    [Fact]
    public void Fix_SingleLine_ReturnsAsIs()
    {
        var mergeSet = new HashSet<char> { '，' };
        var input = new List<string> { "你好，世界" };
        var result = PunctTruncationMerger.Fix(input, mergeSet);

        Assert.Single(result);
        Assert.Equal("你好，世界", result[0]);
    }
}
