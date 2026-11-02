namespace TextTool.Tests.Services;

/// <summary>
/// 智能体回复里的四种开场（自报家门、报错话术、先夸提问者、结论前置口号）。
///
/// 四条都只对回复生效，报错话术另外对文档生效——博客里贴出「服务器繁忙」原话，
/// 同样是拿界面文字拼稿。门限依据是真人库 86 本、4884 万字：
/// 自报家门 2 处（都指具体帮工）、报错话术 0 处、先夸提问者 1 处、结论前置口号 14 处。
/// 结论前置那 14 处多数是「说人话」「我直接说」，所以词形只收口号本身。
/// </summary>
public class AgentVoiceTests
{
    private static List<LintHit> Hits(string text, string? genre, string id) =>
        LintRunner.Run(new[] { ("样本.txt", text) }, null, null, genre)
            .Reports[0].Hits.Where(h => h.Id == id).ToList();

    [Fact]
    public void 自报家门在回复里报出()
    {
        Assert.NotEmpty(Hits("作为一个大语言模型，我必须诚实地说：这个方案有风险。", "agent", "agent-self-intro"));
    }

    [Fact]
    public void 自报家门不收作为助手()
    {
        // 「作为助手」指具体帮工，真人长篇里就是这么用的
        Assert.Empty(Hits("他作为助手跟了老板十年。", "agent", "agent-self-intro"));
    }

    [Fact]
    public void 报错话术要凑够两处()
    {
        Assert.Empty(Hits("刚才页面提示服务器繁忙。", "doc", "agent-error-paste"));
        Assert.NotEmpty(Hits("页面提示服务器繁忙，再试又显示请稍后再试。", "doc", "agent-error-paste"));
    }

    [Fact]
    public void 先夸提问者只对回复生效()
    {
        Assert.NotEmpty(Hits("你非常敏锐，一下就看到了关键。", "agent", "agent-flattery"));
        Assert.Empty(Hits("你非常敏锐，一下就看到了关键。", "fiction", "agent-flattery"));
    }

    [Fact]
    public void 结论前置口号要连来两处()
    {
        Assert.NotEmpty(Hits("一句话总结：先做能跑通的那条。让我直接告诉您原因。", "agent", "agent-conclusion-preamble"));
        Assert.Empty(Hits("一句话总结：先做能跑通的那条。", "agent", "agent-conclusion-preamble"));
    }
}
