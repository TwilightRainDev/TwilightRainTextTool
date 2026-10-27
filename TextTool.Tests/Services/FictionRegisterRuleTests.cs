namespace TextTool.Tests.Services;

/// <summary>
/// 小说档的两条结构判据：结尾预告句、大纲残留。
///
/// 出处是一次数语料差分：AI 短篇 2592 万字对真人小说 4816 万字，两条在两侧的密度差

///
/// 每条钉两件事：AI 稿里的真原句要报出来；真人小说里的同类句子不许报。
/// 少了后半边，判据会在真人的正常句式上刷屏——那正是这批规则从文档档挪到小说档的理由。
/// </summary>
public class FictionRegisterRuleTests
{
    // 规则 Id 只在这里各出现一次，用例正文一律按现象名引用。
    private const string 预告句 = "trailer-ending";
    private const string 大纲残留 = "outline-residue";

    private static List<LintHit> Hits(string text, string? genre, string id) =>
        LintRunner.Run(new[] { ("样本.txt", text) }, null, null, genre)
            .Reports[0].Hits.Where(h => h.Id == id).ToList();

    [Theory]
    [InlineData("所有的答案都将在接下来的探险中揭晓。")]
    [InlineData("而他的冒险旅程也才刚刚开始，前方还有更多的未知和挑战等待着他……")]
    [InlineData("至此，时光之门的故事拉开序幕。")]
    [InlineData("康纳和他的团队能否成功探索这个星球？更多的危险在等着他们。")]
    public void 结尾预告句报出(string text)
    {
        Assert.NotEmpty(Hits(text, "fiction", 预告句));
    }

    [Theory]
    [InlineData("他把门带上，走下楼去。楼道的灯坏了两盏。")]
    [InlineData("她问了一句，没人回答，风把窗帘吹得鼓起来。")]
    [InlineData("他才刚坐下，茶还没凉，人就到了。")]
    public void 段末没有预告句不报(string text)
    {
        Assert.Empty(Hits(text, "fiction", 预告句));
    }

    [Theory]
    [InlineData("悬念1：法师公会的真正目的是什么？")]
    [InlineData("伏笔：他手腕上的那道疤。")]
    public void 大纲残留报出(string text)
    {
        Assert.NotEmpty(Hits(text, "fiction", 大纲残留));
    }

    [Theory]
    [InlineData("这个悬念一直没有解开。")]
    [InlineData("伏笔埋得很深，到最后一章才收。")]
    [InlineData("这段表演的看点全在最后三分钟。")]
    public void 正常叙述里的悬念伏笔不报(string text)
    {
        // 「悬念」后面跟的不是编号加冒号，就是正常用词——编号才是提问模板的残留
        Assert.Empty(Hits(text, "fiction", 大纲残留));
    }

    [Theory]
    [InlineData("所有的答案都将在接下来的探险中揭晓。", 预告句)]
    [InlineData("悬念1：法师公会的真正目的是什么？", 大纲残留)]
    public void 文档档一条都不报(string text, string id)
    {
        // 这两条量的是叙事口吻，编程文档里出现同样的字是正常写法
        Assert.Empty(Hits(text, "doc", id));
    }
}
