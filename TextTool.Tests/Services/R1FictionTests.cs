namespace TextTool.Tests.Services;

/// <summary>
/// R1 文风这一族：定语挂成链、身体部位轮着点名、时间副词成串、比喻一比一大堆。
///
/// 四条各管一段写法，判据都锚在形状上而不是具体词：
/// 定语链要四层才算（两层是正常写法，四层就是堆）；部位词要连着一个反应动作；
/// 时间副词按段内两处算；比喻认「像……一样」这个最常见的形状，不认单词。
/// </summary>
public class R1FictionTests
{
    private const string 定语链 = "fiction-modifier-chain";
    private const string 生理反应 = "fiction-physio-tics";
    private const string 副词堆叠 = "adverb-overuse";
    private const string 陈词比喻 = "fiction-dead-metaphor";

    private static List<LintHit> Hits(string text, string? genre, string id) =>
        LintRunner.Run(new[] { ("样本.txt", text) }, null, null, genre)
            .Reports[0].Hits.Where(h => h.Id == id).ToList();

    [Fact]
    public void 四层定语挂到句尾才报()
    {
        var text = "门外站着一个穿着湿透工装、头发黏在额角、眼神浑浊得像隔夜茶水的、从北方小镇来的、在雨夜里迷路的、名叫阿川的青年。";

        Assert.NotEmpty(Hits(text, "fiction", 定语链));
    }

    [Fact]
    public void 两层定语与饰名列举都不报()
    {
        Assert.Empty(Hits("那件穿了很久的旧棉袄挂在那里。", "fiction", 定语链));
        // 饰名并列不是一条链：每个都自带名词，读者不会读到句尾才知道说的是谁
        Assert.Empty(Hits("银铃般的笑声、富有磁性的声音、深邃的眼眸。", "fiction", 定语链));
    }

    [Theory]
    [InlineData("喉头滚动了一下，他没接话。")]
    [InlineData("耳根发烫，他把脸埋进衣领。")]
    [InlineData("指腹按住关门键，指尖顿时发白。")]
    [InlineData("掌心顿时一沉，剑比看上去重。")]
    public void 部位词带反应动作报出(string text)
    {
        Assert.NotEmpty(Hits(text, "fiction", 生理反应));
    }

    [Theory]
    [InlineData("他咽喉发炎，说不出话。")]
    [InlineData("掌心的茧子磨得发亮。")]
    public void 部位词不带反应动作不报(string text)
    {
        Assert.Empty(Hits(text, "fiction", 生理反应));
    }

    [Fact]
    public void 时间副词同段两处报出()
    {
        Assert.NotEmpty(Hits("门蓦地滑开，他陡然后退半步。", "fiction", 副词堆叠));
        Assert.Empty(Hits("门蓦地滑开。", "fiction", 副词堆叠));
    }

    [Fact]
    public void 像某某一样这个形状报出()
    {
        Assert.NotEmpty(Hits("一股腥潮气像烧开的沥青一样翻涌上来。", "fiction", 陈词比喻));
        Assert.Empty(Hits("他像往常一样锁门。", "fiction", 陈词比喻));
    }
}
