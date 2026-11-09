namespace TextTool.Tests.Services;

/// <summary>
/// 单字动词表承诺（agent-one-char-commitment）用例。
///
/// 收的是智能体回复里「我认」「我接」「我跟」这类把承诺压成两个字的写法：认下的哪一条、
/// 接到什么、跟到哪里都没说，态度替掉了内容。判据要求单字动词落在停顿处（句末、顿号、逗号），
/// 所以「我认为」「我记得」「我认同」「我接受」这类正常搭配不会进来。
///
/// 单处就算：一句「我认。」已经把话收了尾，认完不写改哪句，读者手里只剩一句收尾。
/// 只看有没有，不看密度：短回复凑不到什么密度，一句收尾就够读者吃亏。
/// </summary>
public class AgentCommitmentTests
{
    private const string 单字动词 = "agent-one-char-commitment";

    private static List<LintHit> Hits(string text, string? genre = "agent") =>
        LintRunner.Run(new[] { ("样本.txt", text) }, null, null, genre)
            .Reports[0].Hits.Where(h => h.Id == 单字动词).ToList();

    private static string 承诺稿() =>
        "行，我认。你去，我跟。你要走我不拦。这个账我记住了，这话我也收下了。";

    [Theory]
    [InlineData("我认为这样不妥。")]
    [InlineData("我记得昨天说过。")]
    [InlineData("我接受这个方案。")]
    [InlineData("我认同你的判断。")]
    [InlineData("我留下来看一眼。")]
    [InlineData("我跟着他走了一段。")]
    public void 正常搭配不报(string text)
    {
        Assert.Empty(Hits(text));
    }

    [Fact]
    public void 连着两处以上报出()
    {
        var hits = Hits(承诺稿());

        Assert.True(hits.Count >= 2);
        Assert.All(hits, h => Assert.Equal("warn", h.Severity));
    }

    [Fact]
    public void 只出现在智能体档()
    {
        // 小说里「我拦。」这类短句是对话本身，不是协作承诺
        Assert.Empty(Hits(承诺稿(), "fiction"));
        Assert.Empty(Hits(承诺稿(), "doc"));
    }

    [Fact]
    public void 单处也报()
    {
        // 一处「我认。」夹在一大段正常内容里也是一处收尾：判据看有没有，不看比例
        string text = "行，我认。" + new string('这', 800);
        Assert.Single(Hits(text));
    }
}
