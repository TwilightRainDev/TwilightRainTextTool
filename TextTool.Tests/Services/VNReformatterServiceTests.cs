using System.Text;
using System.Text.RegularExpressions;

namespace TextTool.Tests.Services;

/// <summary>
/// VNReformatterService 单元测试。
/// 覆盖：线条分类（对话/混合/场景/路线/分隔线）、段落构建、补句号、
/// 长段拆分、标点修复、BOM 移除、文件 API。
/// 断言基于实现实际行为（句末补中文句号「。」）。
/// </summary>
public class VNReformatterServiceTests
{
    // ================================================================
    //  基本叙事合并
    // ================================================================

    [Fact]
    public void Reformat_NarrativeLines_JoinedIntoParagraphs()
    {
        var svc = new VNReformatterService();
        var result = svc.Reformat("今天天气真好。\n\n明天去公园。");

        Assert.Equal("今天天气真好。\n\n明天去公园。", result);
    }

    [Fact]
    public void Reformat_MissingSentenceEnd_AppendsPeriod()
    {
        var svc = new VNReformatterService();
        var result = svc.Reformat("回家吧\n\n孩子");

        Assert.Equal("回家吧。\n\n孩子。", result);
    }

    [Fact]
    public void Reformat_BlankLineFlushesBuffer()
    {
        var svc = new VNReformatterService();
        var result = svc.Reformat("第一段\n\n第二段");

        Assert.Equal("第一段。\n\n第二段。", result);
    }

    // ================================================================
    //  对话行识别
    // ================================================================

    [Fact]
    public void Reformat_DialogueLine_DetectedAsDialogue()
    {
        var svc = new VNReformatterService();
        var result = svc.Reformat("「你好」「你好呀」");

        // 对话行不补句号、原样输出
        Assert.Equal("「你好」「你好呀」", result);
    }

    [Fact]
    public void Reformat_CharacterNameQuote_MixedLineSplit()
    {
        var svc = new VNReformatterService(characters: new[] { "伦太郎" });
        var result = svc.Reformat("伦太郎「你好」");

        Assert.Equal("伦太郎「你好」", result);
    }

    [Fact]
    public void Reformat_CharacterNameCommaQuote_MixedLineSplit()
    {
        var svc = new VNReformatterService(characters: new[] { "伦太郎" });
        var result = svc.Reformat("伦太郎，「你好」");

        Assert.Equal("伦太郎，「你好」", result);
    }

    [Fact]
    public void Reformat_DialogueAndNarrative_BlankLineBetween()
    {
        var svc = new VNReformatterService(characters: new[] { "伦太郎" });
        var result = svc.Reformat("窗外在下雨。\n\n伦太郎「走吧」");

        Assert.Equal("窗外在下雨。\n\n伦太郎「走吧」", result);
    }

    // ================================================================
    //  场景标记与路线标题
    // ================================================================

    [Fact]
    public void Reformat_SceneMarker_RenderedAsScene()
    {
        var svc = new VNReformatterService();
        var result = svc.Reformat("── 终章 ──");

        Assert.Equal("─── 终章 ───", result);
    }

    [Fact]
    public void Reformat_SGFDCode_DetectedAsScene()
    {
        // 场景正则需显式注入（SGFD 模式是 Steins;Gate 方案的属性，非引擎默认）
        var svc = new VNReformatterService(scenePattern: new Regex(@"SGFD_[A-Z]+[〇零一二三四五六七八九十百千万\d]+"));
        var result = svc.Reformat("SGFD_OP1");

        Assert.Equal("─── SGFD_OP1 ───", result);
    }

    [Fact]
    public void Reformat_SceneThenDialogue_BlankLineBetween()
    {
        var svc = new VNReformatterService();
        var result = svc.Reformat("── 序章 ──\n\n伦太郎「开始」");

        Assert.Equal("─── 序章 ───\n\n伦太郎「开始」", result);
    }

    [Fact]
    public void Reformat_RouteHeading_RenderedAsHeading()
    {
        // 路线名需显式注入（S;G 路线是方案属性，非引擎默认）
        var svc = new VNReformatterService(routeNames: new[] { "序章共通线" });
        var result = svc.Reformat("序章共通线");

        Assert.Equal("# 序章共通线", result);
    }

    [Fact]
    public void Reformat_SeparatorLine_IsSkipped()
    {
        var svc = new VNReformatterService();
        var result = svc.Reformat("★★★★\n\n正文内容");

        // 装饰分隔线不产生输出
        Assert.Equal("正文内容。", result);
    }

    // ================================================================
    //  Golden test — 代表性完整输入
    // ================================================================

    [Fact]
    public void Reformat_GoldenSample_FullPipeline()
    {
        var svc = new VNReformatterService(
            characters: new[] { "伦太郎", "真由理" },
            routeNames: new[] { "序章共通线" },
            scenePattern: new Regex(@"SGFD_[A-Z]+[〇零一二三四五六七八九十百千万\d]+"));
        string input = "SGFD_OP1\n\n"
                     + "伦太郎「终于开始了」\n"
                     + "天开始下起了雨。\n\n"
                     + "真由理「嗯，走吧」\n\n"
                     + "序章共通线";

        string expected = "─── SGFD_OP1 ───\n\n"
                        + "伦太郎「终于开始了」\n\n"
                        + "天开始下起了雨。\n\n"
                        + "真由理「嗯，走吧」\n\n"
                        + "# 序章共通线";

        Assert.Equal(expected, svc.Reformat(input));
    }

    // ================================================================
    //  长段拆分
    // ================================================================

    [Fact]
    public void Reformat_LongParagraph_SplitAtPunctuation()
    {
        // maxPara=100, minPara=80 → FindCut 搜索窗口 [79, 99]。
        // 在 index 98 放置句号，使切割点在句号之后。
        var svc = new VNReformatterService(maxParaLength: 100);
        string text = new string('甲', 98) + "。" + new string('乙', 10);
        // 总长 98+1+10=109 > 100 → 应拆为两段

        var result = svc.Reformat(text);
        string[] lines = result.Split('\n');

        Assert.Equal(3, lines.Length);
        Assert.Equal(new string('甲', 98) + "。", lines[0]);
        Assert.Equal("", lines[1]);
        Assert.Equal(new string('乙', 10) + "。", lines[2]);
    }

    [Fact]
    public void Reformat_LongParagraph_NoPunctuation_ForcedCut()
    {
        // 无句号时在 maxPara 处硬切；整体补一次句号落在末段
        var svc = new VNReformatterService(maxParaLength: 100);
        string text = new string('字', 120);

        var result = svc.Reformat(text);
        string[] lines = result.Split('\n');

        Assert.Equal(3, lines.Length);
        Assert.Equal(new string('字', 100), lines[0]);
        Assert.Equal("", lines[1]);
        Assert.Equal(new string('字', 20) + "。", lines[2]);
    }

    // ================================================================
    //  标点修复
    // ================================================================

    [Fact]
    public void Reformat_RepeatedPunctuation_Consolidated()
    {
        var svc = new VNReformatterService();
        var result = svc.Reformat("好冷。。\n\n嗯。。");

        // 。。→。
        Assert.DoesNotContain("。。", result);
        Assert.Contains("。", result);
    }

    [Fact]
    public void Reformat_TriplePeriod_ConsolidatedToSingle()
    {
        // 3+ 个重复句号应折叠为单个（链式 Replace 单趟无法处理 3+，需 RepeatedEnder 兜底）
        var svc = new VNReformatterService();
        var result = svc.Reformat("好冷。。。\n\n嗯。。。");

        Assert.DoesNotContain("。。", result);
        Assert.DoesNotContain("。。。", result);
        Assert.Equal("好冷。\n\n嗯。", result);
    }

    [Fact]
    public void Reformat_SceneMarkerContainingRouteName_KeepsSceneText()
    {
        // 场景标记文本内含路线名时，不得剥离路线名后把场景行清空（回归 #5）。
        // 场景标记本身就是路线名（── 牧濑红莉栖线 ──）时，保留原文作为场景行。
        var svc = new VNReformatterService(routeNames: new[] { "牧濑红莉栖线" });
        var result = svc.Reformat("── 牧濑红莉栖线 ──");

        // 场景行保留非空文本，不被清空
        Assert.Contains("───", result);
        Assert.Contains("牧濑红莉栖线", result);
        Assert.DoesNotContain("───  ───", result);
    }

    [Fact]
    public void Reformat_MixedLineWithLaterCharacterName_ExtractsDialogue()
    {
        // 最左角色名不是对话起点、后续角色名才是：对话部分必须被正确提取而非整行吞入叙事（回归 #6）
        var svc = new VNReformatterService(characters: new[] { "红莉栖", "伦太郎" });
        var result = svc.Reformat("红莉栖不是那样，伦太郎「嗯」。");

        Assert.Contains("伦太郎「嗯」。", result);
        Assert.Contains("红莉栖不是那样，", result);
    }

    // ================================================================
    //  BOM 与空白
    // ================================================================

    [Fact]
    public void Reformat_LeadingBom_Removed()
    {
        // 用字面 ﻿ 构造 BOM（与实现检查的 U+FEFF 一致）
        var svc = new VNReformatterService();
        var result = svc.Reformat("﻿开头文本");

        Assert.Equal("开头文本。", result);
    }

    [Fact]
    public void Reformat_EmptyInput_ReturnsEmpty()
    {
        var svc = new VNReformatterService();
        Assert.Equal("", svc.Reformat(""));
    }

    [Fact]
    public void Reformat_CRLF_LineEndings_Normalized()
    {
        var svc = new VNReformatterService();
        string result = svc.Reformat("第一段\r\n\r\n第二段");

        // \r 被剥离，输出为 \n 分隔
        Assert.DoesNotContain('\r', result);
        Assert.Equal("第一段。\n\n第二段。", result);
    }

    // ================================================================
    //  文件 API
    // ================================================================

    [Fact]
    public void ReformatFile_WritesOutput()
    {
        using var tf = new TempFile();
        File.WriteAllText(tf.Path, "今天天气真好。\n\n明天去公园。", Encoding.UTF8);

        string outPath = System.IO.Path.ChangeExtension(tf.Path, null) + "_formatted.txt";
        try
        {
            var svc = new VNReformatterService();
            svc.ReformatFile(tf.Path, outPath, Encoding.UTF8);

            Assert.True(File.Exists(outPath));
            string written = File.ReadAllText(outPath, Encoding.UTF8);
            Assert.Equal("今天天气真好。\n\n明天去公园。", written);
        }
        finally
        {
            if (File.Exists(outPath)) File.Delete(outPath);
        }
    }
}
