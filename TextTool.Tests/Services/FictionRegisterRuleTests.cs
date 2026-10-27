namespace TextTool.Tests.Services;

/// <summary>
/// 小说档的四条叙事层判据：结尾预告句、大纲残留、摘要动词、连接词切场景。
///
/// 出处是一次数语料差分：AI 短篇 2592 万字对真人小说 4816 万字，四条在两侧的密度差
/// 分别是 317 倍、真人侧零命中、172 倍、76 倍。样本全部摘自那批语料的原句。
///
/// 每条钉两件事：AI 稿里的真原句要报出来；真人小说里的同类句子不许报。
/// 少了后半边，判据会在真人的正常句式上刷屏——那正是这批规则从文档档挪到小说档的理由。
/// </summary>
public class FictionRegisterRuleTests
{
    // 规则 Id 只在这里各出现一次，用例正文一律按现象名引用。
    private const string 预告句 = "trailer-ending";
    private const string 大纲残留 = "outline-residue";
    private const string 摘要动词 = "synopsis-register";
    private const string 切场景 = "connective-scene-shift";

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
    [InlineData("他要去探索那座遗迹，也要探索自己走过的路。")]
    [InlineData("那是一段漫长的追光之旅，也是他唯一的救赎之旅。")]
    public void 摘要动词成对出现时报出(string text)
    {
        Assert.NotEmpty(Hits(text, "fiction", 摘要动词));
    }

    [Fact]
    public void 单个摘要动词不报()
    {
        // 只出现一次的「探索」是正常用词；凑够两次才说明在用概述动作写场景
        Assert.Empty(Hits("他探索了很久，才找到那扇门。", "fiction", 摘要动词));
    }

    [Fact]
    public void 连接词连续切场景报出()
    {
        Assert.NotEmpty(Hits("然而，在一个雨夜，他离开了。然而，在一个清早，他又回来了。", "fiction", 切场景));
    }

    [Fact]
    public void 单次连接词不报()
    {
        Assert.Empty(Hits("然而，在这一点上他并没有错。", "fiction", 切场景));
    }

    [Theory]
    [InlineData("所有的答案都将在接下来的探险中揭晓。", 预告句)]
    [InlineData("悬念1：法师公会的真正目的是什么？", 大纲残留)]
    [InlineData("他要去探索那座遗迹，也要探索自己走过的路。", 摘要动词)]
    [InlineData("然而，在一个雨夜，他离开了。然而，在一个清早，他又回来了。", 切场景)]
    public void 文档档一条都不报(string text, string id)
    {
        // 这四条量的是叙事口吻，编程文档里出现同样的字是正常写法
        Assert.Empty(Hits(text, "doc", id));
    }
}
