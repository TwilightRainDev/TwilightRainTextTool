using System.Text;

namespace TextTool.Tests.Services;

public class ProcessingPipelineTests
{
    // ================================================================
    //  Full pipeline: threshold merge + CJK fix + punct fix + replace
    // ================================================================

    [Fact]
    public void Run_FullPipeline_ProcessesCorrectly()
    {
        using var tf = new TempFile();
        File.WriteAllLines(tf.Path, new[]
        {
            "短行1", "短行2", "你好", "吗？", "结束"
        }, Encoding.UTF8);

        var encoding = EncodingDetector.Detect(tf.Path);
        var mergeOpts = new MergeOptions { Threshold = 10, Mode = MergeMode.CharCount };
        var post = new PostProcessOptions(
            FixCjk: true, FixPunct: true, PunctChars: "，",
            NoMerge: false, NoMergeChars: ".。！？",
            ApplyReplace: true, TrimLeadingComma: false,
            Rules: new List<ReplaceRule> { new() { Find = "吗", Replace = "嘛" } });

        var result = ProcessingPipeline.Run(tf.Path, encoding.Encoding, mergeOpts, post);

        Assert.NotNull(result);
        Assert.True(File.Exists(result.OutputPath));
        Assert.Contains("短行1短行2", result.Lines[0]);
        Assert.Contains("嘛", string.Join("", result.Lines));
    }

    [Fact]
    public void Run_NoPostProcessing_OnlyThresholdMerge()
    {
        using var tf = new TempFile();
        File.WriteAllLines(tf.Path, new[] { "a", "b", "c" }, Encoding.UTF8);

        var encoding = EncodingDetector.Detect(tf.Path);
        var mergeOpts = new MergeOptions { Threshold = 3, Mode = MergeMode.CharCount };
        var post = new PostProcessOptions(
            FixCjk: false, FixPunct: false, PunctChars: "",
            NoMerge: false, NoMergeChars: "",
            ApplyReplace: false, TrimLeadingComma: false,
            Rules: new List<ReplaceRule>());

        var result = ProcessingPipeline.Run(tf.Path, encoding.Encoding, mergeOpts, post);

        Assert.Single(result.Lines);
        Assert.Equal("abc", result.Lines[0]);
    }

    [Fact]
    public void Run_TrimLeadingComma_RemovesLeadingComma()
    {
        using var tf = new TempFile();
        File.WriteAllLines(tf.Path, new[] { "hello", "，world" }, Encoding.UTF8);

        var encoding = EncodingDetector.Detect(tf.Path);
        var mergeOpts = new MergeOptions { Threshold = 1, Mode = MergeMode.CharCount };
        var post = new PostProcessOptions(
            FixCjk: false, FixPunct: false, PunctChars: "",
            NoMerge: false, NoMergeChars: "",
            ApplyReplace: false, TrimLeadingComma: true,
            Rules: new List<ReplaceRule>());

        var result = ProcessingPipeline.Run(tf.Path, encoding.Encoding, mergeOpts, post);

        Assert.Equal("world", result.Lines[1]);
        Assert.True(result.Trimmed);
    }

    [Fact]
    public void Run_OutputPath_HasProcessedSuffix()
    {
        using var tf = new TempFile();
        File.WriteAllText(tf.Path, "test", Encoding.UTF8);

        var encoding = EncodingDetector.Detect(tf.Path);
        var result = ProcessingPipeline.Run(tf.Path, encoding.Encoding,
            new MergeOptions { Threshold = 100, Mode = MergeMode.CharCount },
            new PostProcessOptions(false, false, "", false, "", false, false, new List<ReplaceRule>()));

        Assert.EndsWith("_Processed.txt", result.OutputPath);
        Assert.True(File.Exists(result.OutputPath));
    }

    // ================================================================
    //  Verbose Option helper for readability
    // ================================================================

    private static PostProcessOptions NoOps() => new(false, false, "", false, "", false, false, new List<ReplaceRule>());

    // Helpers are in TestHelpers.cs (shared TempFile / TempDir)
}

