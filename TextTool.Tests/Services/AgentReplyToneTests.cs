namespace TextTool.Tests.Services;

/// <summary>
/// 智能体回复里的两条腔调判据：开口先认同、把推演写进回复。
///
/// 两条都只在智能体回复这一类里生效。理由在数据上：认同的说法在真人小说里合计 310 处，
/// 只认句首与段首之后剩 46 处（每千字不到 0.002），而小说里的「你说得对」是人物对话，
/// 不是回复的开场白。推演那句同理——读者要的是答案，不是思考过程。
/// </summary>
public class AgentReplyToneTests
{
    private const string 先认同 = "agent-agreement-opener";
    private const string 推演 = "agent-deliberation-leak";

    private static List<LintHit> Hits(string text, string? genre = "agent") =>
        LintRunner.Run(new[] { ("样本.txt", text) }, null, null, genre)
            .Reports[0].Hits.Where(h => h.Id == 先认同 || h.Id == 推演).ToList();

    private static List<LintHit> HitsOf(string id, string text, string? genre = "agent") =>
        Hits(text, genre).Where(h => h.Id == id).ToList();

    [Theory]
    [InlineData("你说得对，这确实是模板化句式的典型特征。")]
    [InlineData("你说得没错。\n下一句开始说具体怎么改。")]
    [InlineData("确实如此，问题出在第三段。")]
    public void 句首认同报出(string text)
    {
        Assert.NotEmpty(HitsOf(先认同, text));
    }

    [Theory]
    [InlineData("他说了一句「你说得对」，然后又摇头。")]
    [InlineData("我问他是不是这样，他说你说得对。")]
    public void 正文里的转述不报(string text)
    {
        Assert.Empty(HitsOf(先认同, text));
    }

    [Theory]
    [InlineData("嗯，用户提出了一个观点，内容是关于模板化句式。")]
    [InlineData("需要决定回答长度，先给结论。")]
    [InlineData("让我先确认一下这段的目标读者。")]
    public void 把推演写进回复时报出(string text)
    {
        Assert.NotEmpty(HitsOf(推演, text));
    }

    [Fact]
    public void 两条都只在智能体回复里报()
    {
        const string text = "你说得对，这确实是模板化句式的典型特征。让我先确认一下。";

        Assert.Empty(Hits(text, "fiction"));
        Assert.Empty(Hits(text, "doc"));
        Assert.NotEmpty(Hits(text, "agent"));
        Assert.NotEmpty(Hits(text, null));
    }
}
