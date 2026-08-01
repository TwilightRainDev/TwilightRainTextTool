using System.Text;

namespace TextTool.Tests.Services;

/// <summary>
/// PunctFixerService 单元测试。
/// 覆盖：对话行补标点、非对话行不动、句末已有点不补、嵌套引号、文件 API。
/// </summary>
public class PunctFixerServiceTests
{
    // ================================================================
    //  补标点
    // ================================================================

    [Fact]
    public void Fix_DialogueLine_AppendsPeriodBeforeLastCloseQuote()
    {
        var svc = new PunctFixerService();
        var result = svc.Fix("「你好」「我很好」");

        Assert.Equal("「你好」「我很好。」", result);
    }

    [Fact]
    public void Fix_DialogueLine_AlreadyHasPeriod_Unchanged()
    {
        var svc = new PunctFixerService();
        var result = svc.Fix("「你好」「我很好。」");

        Assert.Equal("「你好」「我很好。」", result);
    }

    [Fact]
    public void Fix_SingleQuoteLine_NotDialogue_Unchanged()
    {
        // 单个「引文」不是「角色」「台词」对话模式，不加点
        var svc = new PunctFixerService();
        var result = svc.Fix("「内心独白」");

        Assert.Equal("「内心独白」", result);
    }

    [Fact]
    public void Fix_NonDialogue_Narrative_Unchanged()
    {
        var svc = new PunctFixerService();
        var result = svc.Fix("今天天气真好。");

        Assert.Equal("今天天气真好。", result);
    }

    // ================================================================
    //  自定义终止符集合
    // ================================================================

    [Fact]
    public void Fix_CustomTerminalChars_SkipWhenEndingWith()
    {
        // 终止符不含「？」→ 以？结尾仍补。
        var svc = new PunctFixerService("。！");
        var result = svc.Fix("「你好」「真的吗？」");

        Assert.Equal("「你好」「真的吗？。」", result);
    }

    // ================================================================
    //  嵌套引号
    // ================================================================

    [Fact]
    public void Fix_NestedQuotes_InsertsBeforeLastClose()
    {
        // 末尾连续两个 」— 应越过外层」，在内容末尾补点
        var svc = new PunctFixerService();
        var result = svc.Fix("「他说「你好」」");

        // 不是双对话模式？「[^」]*」「 需要相邻两个「
        Assert.Equal("「他说「你好」」", result);
    }

    // ================================================================
    //  多行输入
    // ================================================================

    [Fact]
    public void Fix_MultipleLines_OnlyDialogueLinesModified()
    {
        var svc = new PunctFixerService();
        string input = "叙事行。\n「甲」「你好」\n「乙」「嗯」";
        string expected = "叙事行。\n「甲」「你好。」\n「乙」「嗯。」";

        Assert.Equal(expected, svc.Fix(input));
    }

    // ================================================================
    //  文件 API
    // ================================================================

    [Fact]
    public void FixFile_WritesOutput()
    {
        using var tf = new TempFile();
        File.WriteAllText(tf.Path, "「甲」「你好」\n「乙」「嗯」", Encoding.UTF8);

        string outPath = System.IO.Path.ChangeExtension(tf.Path, null) + "_fixed.txt";
        try
        {
            var svc = new PunctFixerService();
            svc.FixFile(tf.Path, outPath, Encoding.UTF8);

            Assert.True(File.Exists(outPath));
            string written = File.ReadAllText(outPath, Encoding.UTF8);
            Assert.Equal("「甲」「你好。」\n「乙」「嗯。」", written);
        }
        finally
        {
            if (File.Exists(outPath)) File.Delete(outPath);
        }
    }
}
