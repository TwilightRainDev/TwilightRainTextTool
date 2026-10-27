namespace TextTool.Tests.Services;

/// <summary>
/// markdown 语境下的判据边界。
///
/// 这套规则原本是给纯文本文档写的，套到 markdown 上有两类系统性误报：
/// 表格分隔线／frontmatter／命令行参数里的连字符不是破折号误用；
/// 「- **要点**：」在 markdown 里是标准写法，不是 AI 排版痕迹。
/// 工作区实测这两条占了全部命中的四分之三，所以边界要钉在用例里。
/// </summary>
public class MarkdownAwarenessTests
{
    private const string 破折号 = "dash-and-tilde";
    private const string 行内标题列表 = "inline-title-list";

    private static List<LintHit> Hits(string text, string id) =>
        LintRunner.Run(new[] { ("样本.md", text) }, new[] { id }, null).Reports[0].Hits.ToList();

    // 空行分隔是 markdown 的判定依据（空行比例超过阈值），样本按这个形态写
    [Fact]
    public void 表格分隔线不算破折号误用()
    {
        const string text = "| 规则 | 命中 |\n|---|---|\n| 破折号 | 768 |\n\n正文另起一段。";

        Assert.Empty(Hits(text, 破折号));
    }

    [Fact]
    public void 命令行参数不算破折号误用()
    {
        const string text = "运行下面这条命令。\n\n```\ntexttool lint --genre doc --json 文件.md\n```\n\n参数说明见上。";

        Assert.Empty(Hits(text, 破折号));
    }

    [Theory]
    [InlineData("他停了一下—然后就走了。", true)]        // 单发破折号：错
    [InlineData("他停了一下——然后就走了。", false)]      // 中文破折号本就是这个长度
    [InlineData("这里用中文--连接，看着别扭。", true)]   // 中文之间的双连字符
    public void 中文之间的连字符按形态判(string text, bool reported)
    {
        Assert.Equal(reported, Hits(text, 破折号).Count > 0);
    }

    [Fact]
    public void markdown里的加粗列表项不报行内标题()
    {
        // 空行分隔 → markdown 语境；「- **要点**：」是标准写法
        const string text = "## 结论\n\n- **判定**：命中是真命中\n- **出处**：记忆条\n\n以上。";

        Assert.Empty(Hits(text, 行内标题列表));
    }

    [Fact]
    public void 纯文本里的加粗列表项照报()
    {
        // 无空行分隔 → 纯文本文档，同样的写法就是硬编号排版痕迹
        const string text = "结论如下。\n- **判定**：命中是真命中\n- **出处**：记忆条";

        Assert.NotEmpty(Hits(text, 行内标题列表));
    }
}
