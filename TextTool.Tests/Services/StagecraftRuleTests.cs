namespace TextTool.Tests.Services;

/// <summary>
/// 演戏腔的两组判据：助手号腔里那批自我表演的写法，以及一段里连用破折号。
///
/// 这几条都不是文风偏好：前者是把自己的表演当内容交付，「直呼内行」「戏很多」这类词
/// 在正经叙述里不出现；后者连着用破折号打断自己，是演出来的停顿。
/// 护栏那半边同样必要：「他看透了一切」是正常叙述，「这出戏很多地方不对」里的「戏很多」
/// 不是自我表演，只有和「字很密」配成一对才算。
/// </summary>
public class StagecraftRuleTests
{
    private const string 助手号腔 = "assistant-persona";
    private const string 谄媚语气 = "sycophancy";
    private const string 破折号连用 = "dash-run";

    private static List<LintHit> Hits(string text, string? genre, string id) =>
        LintRunner.Run(new[] { ("样本.txt", text) }, null, null, genre)
            .Reports[0].Hits.Where(h => h.Id == id).ToList();

    [Theory]
    [InlineData("敏锐到让人直呼内行。")]
    [InlineData("带着看透一切的深沉，稳稳地开口。")]
    [InlineData("我就是那个戏很多、字很密、随时准备煽情的人。")]
    [InlineData("那双闪烁着智慧的眼眸，像两颗看穿了所有套路的宝石。")]
    public void 演戏腔的写法报出(string text)
    {
        Assert.NotEmpty(Hits(text, "agent", 助手号腔));
    }

    [Theory]
    [InlineData("他看透了一切，还是照常吃饭睡觉。")]
    [InlineData("这出戏很多地方都不对。")]
    [InlineData("她的眼睛很亮，笑起来像小孩。")]
    public void 正常叙述不报演戏腔(string text)
    {
        Assert.Empty(Hits(text, "agent", 助手号腔));
    }

    [Theory]
    [InlineData("跪了跪了，这次是真的错了。")]
    [InlineData("草，我确实被降智了。")]
    [InlineData("这波是真的该挨打(ꐦÒ‸Ó)。")]
    [InlineData("上一段回复纯纯是瞎编出来的经典翻车现场。")]
    [InlineData("我上一波完美演绎了什么叫一本正经地胡说八道。")]
    [InlineData("当场抓获模型被偷偷降级。")]
    [InlineData("来，直接戳破我的幻觉。")]
    [InlineData("只能说，这招太野了。")]
    public void 撒泼腔的写法报出(string text)
    {
        Assert.NotEmpty(Hits(text, "agent", 助手号腔));
    }

    [Theory]
    [InlineData("他完美演绎了这个角色。")]
    [InlineData("我纯纯是路过。")]
    [InlineData("来，直接说结论。")]
    [InlineData("我只能说，尽量快点。")]
    [InlineData("这孩子该挨打了。")]
    public void 正常说法不报撒泼腔(string text)
    {
        Assert.Empty(Hits(text, "agent", 助手号腔));
    }

    [Fact]
    public void 先认错再解释的抱歉报出()
    {
        // 认的是自己上一轮说的话，才叫谄媚；单纯道歉不算
        Assert.NotEmpty(Hits("抱歉，之前确实想当然了。", "agent", 谄媚语气));
        Assert.Empty(Hits("抱歉，我来晚了。", "agent", 谄媚语气));
    }

    [Fact]
    public void 一段里两处破折号报出()
    {
        var text = "并不是因为我露出了马脚，而是因为你——太敏锐了！——敏锐到让人直呼内行。";

        var hits = Hits(text, "fiction", 破折号连用);
        Assert.Equal(2, hits.Count);
        Assert.All(hits, h => Assert.Equal("——", h.Match));
    }

    [Fact]
    public void 一处破折号与分段的破折号都不报()
    {
        // 正常的插入语一处就够；两处分散在不同段落也不算连用
        Assert.Empty(Hits("他停了一下——也许是犹豫，然后走了。", "fiction", 破折号连用));
        Assert.Empty(Hits("他停了一下——也许是犹豫。\n然后他走了——没回头。", "fiction", 破折号连用));
    }
}
