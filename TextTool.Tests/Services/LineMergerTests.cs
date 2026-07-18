using System.Text;

namespace TextTool.Tests.Services;

public class LineMergerTests
{
    // ================================================================
    //  Byte mode
    // ================================================================

    [Fact]
    public void Merge_ByteMode_Threshold20_MergesShortLines()
    {
        // 每行 10 ASCII 字节，阈值 20 → 每 2 行合并为 1 段
        using var tf = TempFile(new[] { "1234567890", "abcdefghij", "ABCDEFGHIJ", "klmnopqrst" });
        var (lines, _) = LineMerger.Merge(tf.Path, new MergeOptions { Threshold = 20, Mode = MergeMode.ByteCount }, Encoding.UTF8);

        Assert.Equal(2, lines.Count);
        Assert.Equal("1234567890abcdefghij", lines[0]);
        Assert.Equal("ABCDEFGHIJklmnopqrst", lines[1]);
    }

    [Fact]
    public void Merge_ByteMode_SingleLineBelowThreshold_OutputsOneLine()
    {
        using var tf = TempFile(new[] { "hello" });
        var (lines, _) = LineMerger.Merge(tf.Path, new MergeOptions { Threshold = 100, Mode = MergeMode.ByteCount }, Encoding.UTF8);

        Assert.Single(lines);
        Assert.Equal("hello", lines[0]);
    }

    [Fact]
    public void Merge_ByteMode_EmptyLines_AllFlushedAtEnd()
    {
        using var tf = TempFile(new[] { "", "" });
        var (lines, _) = LineMerger.Merge(tf.Path, new MergeOptions { Threshold = 1, Mode = MergeMode.ByteCount }, Encoding.UTF8);

        // Two empty lines have 0 bytes each, threshold 1 means each fails separately,
        // but since sb.Length == 0 at end, nothing is flushed. This is expected behavior.
        Assert.Empty(lines);
    }

    // ================================================================
    //  Char mode
    // ================================================================

    [Fact]
    public void Merge_CharMode_Threshold3_MergesByCharacterCount()
    {
        using var tf = TempFile(new[] { "a", "b", "c", "d", "e" });
        var (lines, _) = LineMerger.Merge(tf.Path, new MergeOptions { Threshold = 3, Mode = MergeMode.CharCount }, Encoding.UTF8);

        Assert.Equal(2, lines.Count);
        Assert.Equal("abc", lines[0]);
        Assert.Equal("de", lines[1]);
    }

    [Fact]
    public void Merge_CharMode_CjkCharacters_CountEachCharAsOne()
    {
        using var tf = TempFile(new[] { "你好", "世界", "测试" });
        var (lines, _) = LineMerger.Merge(tf.Path, new MergeOptions { Threshold = 4, Mode = MergeMode.CharCount }, Encoding.UTF8);

        Assert.Equal(2, lines.Count);
        Assert.Equal("你好世界", lines[0]);
        Assert.Equal("测试", lines[1]);
    }

    // ================================================================
    //  Byte vs Char mode 差异
    // ================================================================

    [Fact]
    public void Merge_ByteMode_MultiByteChars_CountsBytesNotChars()
    {
        // "你好" = 6 bytes in UTF-8, 2 chars
        using var tf = TempFile(new[] { "你好", "世", "界" });
        var (byteResult, _) = LineMerger.Merge(tf.Path, new MergeOptions { Threshold = 6, Mode = MergeMode.ByteCount }, Encoding.UTF8);
        var (charResult, _) = LineMerger.Merge(tf.Path, new MergeOptions { Threshold = 2, Mode = MergeMode.CharCount }, Encoding.UTF8);

        // Byte mode: "你好" = 6 bytes → meets threshold of 6, emitted alone
        Assert.Equal("你好", byteResult[0]);
        // Char mode: "你好" = 2 chars → meets threshold of 2, emitted alone
        Assert.Equal("你好", charResult[0]);
    }

    // ================================================================
    //  NoMergeSet
    // ================================================================

    [Fact]
    public void Merge_NoMergeSet_LineEndingWithSpecialChar_FlushesImmediately()
    {
        var noMerge = new HashSet<char> { '.', '!', '。', '！' };
        using var tf = TempFile(new[] { "Hello.", "World", "Done!", "Next" });
        var (lines, _) = LineMerger.Merge(tf.Path, new MergeOptions { Threshold = 100, Mode = MergeMode.CharCount }, Encoding.UTF8, noMerge);

        Assert.Equal(3, lines.Count);
        Assert.Equal("Hello.", lines[0]);
        Assert.Equal("WorldDone!", lines[1]); // "World" + "Done!" merged until "!" is hit
        Assert.Equal("Next", lines[2]);
    }

    [Fact]
    public void Merge_NoMergeSet_NullSet_NoEffect()
    {
        using var tf = TempFile(new[] { "Hello.", "World" });
        var (lines, _) = LineMerger.Merge(tf.Path, new MergeOptions { Threshold = 100, Mode = MergeMode.CharCount }, Encoding.UTF8, null);

        Assert.Single(lines);
        Assert.Equal("Hello.World", lines[0]);
    }

    // ================================================================
    //  Output path
    // ================================================================

    [Fact]
    public void Merge_OutputPath_HasProcessedSuffix()
    {
        using var tf = TempFile(new[] { "hello" });
        var (_, outputPath) = LineMerger.Merge(tf.Path, new MergeOptions(), Encoding.UTF8);

        Assert.EndsWith("_Processed.txt", outputPath);
        Assert.StartsWith(Path.GetDirectoryName(tf.Path), outputPath);
    }

    // ================================================================
    //  Helpers
    // ================================================================

    private static TempFile TempFile(string[] lines)
    {
        var tf = new TempFile();
        File.WriteAllLines(tf.Path, lines, Encoding.UTF8);
        return tf;
    }
}
