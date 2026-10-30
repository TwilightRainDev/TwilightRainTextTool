namespace TextTool.Tests.Services;

/// <summary>
/// 短句硬连：该断的地方用分号、破折号硬接，一长句里七八个短促分句，主语宾语全省。
///
/// 判的是句子形状，不是用词，所以两个门限要同时够：分句数够多，且平均句长够短。
/// 单看分号会误伤正常的并列句，单看顿号会把满篇列举当成电报体。
/// 序号列表（首先、其次、一、二、三）不算：那是结构，不是硬接。
/// 小说不跑：短句在叙事里是正常手法。
/// </summary>
public class ChoppyClauseTests
{
    private const string 短句硬连 = "choppy-clauses";

    private static List<LintHit> Hits(string text, string? genre) =>
        LintRunner.Run(new[] { ("样本.txt", text) }, null, null, genre)
            .Reports[0].Hits.Where(h => h.Id == 短句硬连).ToList();

    private const string 绕口令 =
        "前句我没想清：星、叉、依赖，行；但拿去做魔改、二开，不行；这套你留，不开——"
        + "前句我收，这一刀，我认——议里那先挂着——都这么久了。";

    private const string 人话 =
        "前面那句我确实还没完全想明白。标星标、标叉、依赖这些都可以。但拿去做魔改和二次开发不行。"
        + "这套你先自己留着，不公开。你前面说的这些批评意见，我都接受。议题里那事先挂着吧，都这么久了。";

    [Fact]
    public void 切碎硬接的段落报出()
    {
        var hits = Hits(绕口令, "agent");

        var hit = Assert.Single(hits);
        Assert.Contains("分句", hit.Detail);
    }

    [Fact]
    public void 同一件事说成人话不报()
    {
        // 意思一样，只是把成分补全、断成整句，判据就该不响
        Assert.Empty(Hits(人话, "agent"));
    }

    [Theory]
    [InlineData("苹果、香蕉、梨、橘子、葡萄、西瓜、桃、李子、柿子、柚子。")]
    [InlineData("第一，先查日志；第二，再复现；第三，才改代码。")]
    [InlineData("首先核对数据；其次跑一遍；最后把结论写下来。")]
    public void 列举与编号列表不报(string text)
    {
        Assert.Empty(Hits(text, "doc"));
    }

    [Fact]
    public void 没有分号与破折号不报()
    {
        // 分句再多，只要没有硬接的痕迹，就是正常的短句连写
        Assert.Empty(Hits("他说了。她听了。没人接话。风停了。灯还亮着。屋里很静。", "agent"));
    }

    [Fact]
    public void 小说不跑()
    {
        Assert.Empty(Hits(绕口令, "fiction"));
    }

    [Fact]
    public void 同一处只报一条()
    {
        // 暴力说法那条有两张词表都盖得住「这一刀」，同一位置只该出一条
        var hits = LintRunner.Run(new[] { ("样本.txt", "这一刀，我认。") }, null, null, "agent")
            .Reports[0].Hits.Where(h => h.Id == "violent-verbs").ToList();

        Assert.Single(hits);
    }
}
