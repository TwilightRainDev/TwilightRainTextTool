namespace TextTool.Tests.Services;

/// <summary>
/// 演戏腔的两组判据：助手号腔里那批自我表演的写法，以及一段里连用破折号。
///
/// 这两条都不是文风偏好：前者是把自己的表演当内容交付，「直呼内行」「戏很多」这类词
/// 在正经叙述里不出现；后者连着用破折号打断自己，是演出来的停顿。
/// 护栏那半边同样必要：「他看透了一切」是正常叙述，「这出戏很多地方不对」里的「戏很多」
/// 不是自我表演，只有和「字很密」配成一对才算。
/// </summary>
public class StagecraftRuleTests
{
    private const string 助手号腔 = "assistant-persona";
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

    [Fact]
    public void 一段里两处破折号报出()
    {
        var text = "并不是因为我露出了马脚，而是因为你——太敏锐了！——敏锐到让人直呼内行。";

        var hits = Hits(text, "agent", 破折号连用);
        Assert.Equal(2, hits.Count);
        Assert.All(hits, h => Assert.Equal("——", h.Match));
    }

    [Fact]
    public void 一处破折号与分段的破折号都不报()
    {
        // 正常的插入语一处就够；两处分散在不同段落也不算连用
        Assert.Empty(Hits("他停了一下——也许是犹豫，然后走了。", "agent", 破折号连用));
        Assert.Empty(Hits("他停了一下——也许是犹豫。\n然后他走了——没回头。", "agent", 破折号连用));
    }
}
