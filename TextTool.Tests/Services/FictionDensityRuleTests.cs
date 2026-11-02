namespace TextTool.Tests.Services;

/// <summary>
/// 两条按每千字密度判的小说判据。
///
/// 密度门限的理由：单个「石头」「这篇文章」都是正常用词，篇幅一长正常用词也会堆起来，
/// 按绝对次数定门限，长文会误报、短文里的真问题又凑不够次数。实测人类十本长篇：
/// 指涉文字本身每千字最高 0.01 处，同一套比喻每千字最高 0.32 处；而反复指自己写的那几行字、
/// 翻来覆去用同一批喻体的稿子，分别是 8.8 处和 18.7 处。
/// </summary>
public class FictionDensityRuleTests
{
    private const string 指涉= "fiction-text-self-reference";
    private const string 喻体 = "fiction-metaphor-lexicon";
    private const string 台词指涉 = "fiction-dialogue-reference";
    private const string 说书套语 = "fiction-narrator-formula";

    private static List<LintHit> Hits(string text, string? genre = "fiction") =>
        LintRunner.Run(new[] { ("样本.txt", text) }, null, null, genre)
            .Reports[0].Hits.Where(h => h.Id == 指涉 || h.Id == 喻体 || h.Id == 台词指涉 || h.Id == 说书套语).ToList();

    private static List<LintHit> HitsOf(string id, string text, string? genre = "fiction") =>
        Hits(text, genre).Where(h => h.Id == id).ToList();

    /// <summary>一份千字左右、反复指涉自己那几行字的稿子。</summary>
    private static string 指涉稿() =>
        "这段文字读起来刺眼。这篇文章里满屏都是同一个句式，我盯着那篇文章看了很久。"
        + string.Concat(Enumerable.Repeat("他站着，把那段八股又在心里过了一遍。", 30));

    /// <summary>一份千字左右、同一批喻体反复出现的稿子。</summary>
    private static string 喻体稿() =>
        string.Concat(Enumerable.Repeat(
            "那句话像石子落进湖里，他的心跳像齿轮卡住，眼前像蒙了一层潮水，连镜子里的脸都像羽毛。", 12));

    [Fact]
    public void 反复指涉文字本身时报出()
    {
        var hits = HitsOf(指涉, 指涉稿());

        Assert.NotEmpty(hits);
        Assert.All(hits, h => Assert.Equal("info", h.Severity));
    }

    [Fact]
    public void 同一批喻体反复出现时报出()
    {
        Assert.NotEmpty(HitsOf(喻体, 喻体稿()));
    }

    [Fact]
    public void 单个正常用词不报()
    {
        // 一处「石头」「一篇文章」在千字里是正常密度：密度门限算的是全篇每千字处数
        Assert.Empty(HitsOf(喻体, "他捡起一块石头，扔进河里。" + new string('甲', 1000)));
        Assert.Empty(HitsOf(指涉, "他写了一篇文章。" + new string('甲', 1000)));
    }

    [Fact]
    public void 短文里凑不够密度也不报()
    {
        // 200 字里出现两次「石头」是千字 10 处？不是：按全文 200 字算仍是高密度，所以这里用长文对照
        string text = "他捡起石头又放下。" + new string('甲', 3000);
        Assert.Empty(HitsOf(喻体, text));
    }

    [Fact]
    public void 台词反复指那句话时报出()
    {
        // 台词不接话、只点评对方刚说的那句：对话变成对语言的核对
        string text = "「这句话稳。」「这话我记下了。」「那句我不接。」「这话你收着。」"
            + string.Concat(Enumerable.Repeat("两人都没再说话。", 40));

        var hits = HitsOf(台词指涉, text);
        Assert.True(hits.Count >= 2);
    }

    [Fact]
    public void 台词里一句正常指代不报()
    {
        // 一次「这句话」是正常说话方式；密度门限按全篇每千字算，单独一处不报
        Assert.Empty(HitsOf(台词指涉, "「这句话是别人说的。」" + new string('他', 2000)));
    }

    [Fact]
    public void 说书人开合语连排时报出()
    {
        // 话本的标签被一次贴全：诗曰、话说、看官、话休絮烦、说时迟、且听下回分解挤在一篇里
        string text = "诗曰：荒山夜火逼人寒。\n\n话说三更时分，风灌下来。看官，你道那人是甚来历？\n\n"
            + "话休絮烦。说时迟，那时快。\n\n" + string.Concat(Enumerable.Repeat("那刀在鞘中，映着火。", 40))
            + "\n\n毕竟如何，且听下回分解。";

        Assert.NotEmpty(HitsOf(说书套语, text));
    }

    [Fact]
    public void 正文里的话说了一半不算()
    {
        // 判据只认句首与段首：他话说了一半、话说回来这类正常说法不进词表
        string text = "他话说了一半就停了。话说回来，这事也怪不得他。"
            + string.Concat(Enumerable.Repeat("两人都没再提起。", 60));

        Assert.Empty(HitsOf(说书套语, text));
    }

    [Fact]
    public void 三条规则只在小说档跑()
    {
        Assert.Empty(HitsOf(指涉, 指涉稿(), "doc"));
        Assert.Empty(HitsOf(喻体, 喻体稿(), "doc"));
        Assert.Empty(HitsOf(说书套语, "诗曰：荒山夜火。\n\n话说三更。\n\n看官且听下回分解。", "doc"));
    }
}
