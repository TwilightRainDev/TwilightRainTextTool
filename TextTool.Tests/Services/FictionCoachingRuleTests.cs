namespace TextTool.Tests.Services;

/// <summary>
/// 劝慰腔、心理归因、小动作当句读（fiction-permission-consoling、fiction-choice-diagnosis、
/// fiction-beat-action）三条判据的用例。
///
/// 三条都按读者口碑立，不是人机差分：真人稿里也写得出同样字面，所以每条都钉两半——
/// 正例是跑团与角色扮演里成片出现的那几句，反例是真人叙述里长得像、但不该报的写法。
/// 反例那半就是它们的立据：不把「正常叙述长什么样」写进用例，这三条只会在真人稿上刷屏。
///
/// 递许可要求带具体去处，停顿要求接上「说/道」，眨眼与神情渐变只收固定搭配；
/// 单收「你可以」「他可以」这类主语加情态动词，会连日常对话一起抓。
/// </summary>
public class FictionCoachingRuleTests
{
    private const string 劝慰腔 = "fiction-permission-consoling";
    private const string 心理归因 = "fiction-choice-diagnosis";
    private const string 小动作 = "fiction-beat-action";

    private static List<LintHit> Hits(string text, string? genre, string id) =>
        LintRunner.Run(new[] { ("样本.txt", text) }, null, null, genre)
            .Reports[0].Hits.Where(h => h.Id == id).ToList();

    [Theory]
    [InlineData("帕利卡里：「不过，那也不意味着你必须永远待在那里。」")]
    [InlineData("你可以跟他们商量，让议事会自己继续管理。")]
    [InlineData("帕利卡里：「你可以先自己安排剩下的事情。」")]
    [InlineData("他不必非得今天把话说完。")]
    public void 劝慰腔的许可句报出(string text)
    {
        Assert.NotEmpty(Hits(text, "fiction", 劝慰腔));
    }

    [Theory]
    [InlineData("你可以把窗关上，风有点大。")]
    [InlineData("她跟他商量了一下，把订金退了。")]
    [InlineData("他知道这并不意味着什么。")]
    [InlineData("剩下的活他自己安排。")]
    public void 日常对话里的许可与商量不报(string text)
    {
        Assert.Empty(Hits(text, "fiction", 劝慰腔));
    }

    [Theory]
    [InlineData("帕利卡里：「你是不想再管理那么多事情了，还是发现自己其实根本不喜欢命令别人？」")]
    [InlineData("还是觉得自己其实不适合带队。")]
    [InlineData("其实根本不喜欢命令别人。")]
    public void 二选一的心理归因报出(string text)
    {
        Assert.NotEmpty(Hits(text, "fiction", 心理归因));
    }

    [Theory]
    [InlineData("她终于明白自己在怕什么。")]
    [InlineData("其实他根本不知道，也没打算问。")]
    [InlineData("他觉得自己不一定赶得上。")]
    public void 转述与日常自省不报(string text)
    {
        Assert.Empty(Hits(text, "fiction", 心理归因));
    }

    [Theory]
    [InlineData("他停了一会儿，又说道。")]
    [InlineData("他眨了眨眼。")]
    [InlineData("他重新看向你。")]
    [InlineData("神情渐渐轻松下来。")]
    public void 小动作当句读报出(string text)
    {
        Assert.NotEmpty(Hits(text, "fiction", 小动作));
    }

    [Theory]
    [InlineData("他停下来，看了看表。")]
    [InlineData("他眼睛眯了一下，没说话。")]
    [InlineData("她没有再看向你。")]
    [InlineData("神情并不轻松。")]
    public void 写实的停顿与神情不报(string text)
    {
        Assert.Empty(Hits(text, "fiction", 小动作));
    }

    [Fact]
    public void 三条只在小说档跑()
    {
        // 这三句在编程文档与智能体回复里换个人称就能写成别的东西，
        // 立据又只有读者口碑，所以档位是它们唯一的护栏
        const string text = "你可以跟他们商量。还是发现自己其实不行。他眨了眨眼。";
        Assert.NotEmpty(Hits(text, "fiction", 劝慰腔));
        Assert.NotEmpty(Hits(text, "fiction", 心理归因));
        Assert.NotEmpty(Hits(text, "fiction", 小动作));
        Assert.Empty(Hits(text, "doc", 劝慰腔));
        Assert.Empty(Hits(text, "agent", 心理归因));
        Assert.Empty(Hits(text, "doc", 小动作));
        // 不给档位时跑全部规则，这三条照旧参与
        Assert.NotEmpty(Hits(text, null, 小动作));
    }
}
