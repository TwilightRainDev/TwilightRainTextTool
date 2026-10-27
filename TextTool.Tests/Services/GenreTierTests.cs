namespace TextTool.Tests.Services;

/// <summary>
/// 档位（doc / fiction）的隔离，以及小说档与通用档新增规则的行为。
///
/// 口径：
/// - 档位只决定「哪些规则参与扫描」，报告结构不变，所以 --json 的契约版本不动；
/// - 不给 --genre 时跑全部规则，与加档位之前的行为一致；
/// - 小说档的判据（一丝、瞬间、他知道）在编程文档里是正常用法，文档档必须一条不报——
///   这就是分两档的全部理由，混在一档里只能二选一：要么小说里漏，要么文档里吵。
/// </summary>
public class GenreTierTests
{
    // 规则 Id 只在这里各出现一次，用例正文按现象名引用；改 Id 时只改这几行。
    private const string 套话词 = "cliche-words";
    private const string 网文脸 = "fiction-body-tics";
    private const string 程度词堆叠 = "fiction-filler-modifiers";
    private const string 陈词比喻 = "fiction-dead-metaphor";
    private const string 心理告知 = "fiction-emotion-telling";
    private const string 副词重复 = "adverb-overuse";
    private const string 抽象名词化 = "fiction-vague-nominalization";
    private const string 万能比喻 = "fiction-vague-simile";
    private const string 暴力动词 = "violent-verbs";
    private const string 碎片短句 = "short-sentence-run";

    private static List<LintHit> Hits(string text, string? genre, string id) =>
        LintRunner.Run(new[] { ("样本.txt", text) }, null, null, genre)
            .Reports[0].Hits.Where(h => h.Id == id).ToList();

    [Theory]
    [InlineData(网文脸, "他眼中闪过一丝不耐，嘴角勾起一抹笑。")]
    [InlineData(网文脸, "她深吸一口气，心头一震，瞳孔微缩。")]
    [InlineData(程度词堆叠, "她微微侧头，语气里带着些许冷，还藏着几分不甘，只剩一抹苦笑。")]
    [InlineData(陈词比喻, "他的话像潮水般涌来，仿佛整个世界都被淹了一般。")]
    [InlineData(陈词比喻, "她哭得梨花带雨，笑得如沐春风。")]
    [InlineData(心理告知, "他这才明白，原来不是他想的那样。他不知道的是，门已经开了。")]
    [InlineData(心理告知, "此刻，她终于意识到自己错了。")]
    [InlineData(副词重复, "他猛地站起，椅子刮过地面。她猛地回头。")]
    [InlineData(抽象名词化, "他目光里有一种读不懂的东西。")]
    [InlineData(万能比喻, "他语气很轻，像是在说今天天气不错。")]
    public void 小说档规则命中样本(string id, string text)
    {
        Assert.NotEmpty(Hits(text, "fiction", id));
    }

    [Theory]
    [InlineData(网文脸, "他眼中闪过一丝不耐，嘴角勾起一抹笑。")]
    [InlineData(程度词堆叠, "她微微侧头，语气里带着些许冷，还藏着几分不甘，只剩一抹苦笑。")]
    [InlineData(陈词比喻, "他的话像潮水般涌来，仿佛整个世界都被淹了一般。")]
    [InlineData(心理告知, "此刻，她终于意识到自己错了。")]
    [InlineData(抽象名词化, "他目光里有一种读不懂的东西。")]
    [InlineData(万能比喻, "他语气很轻，像是在说今天天气不错。")]
    public void 文档档不报小说规则(string id, string text)
    {
        Assert.Empty(Hits(text, "doc", id));
    }

    [Theory]
    [InlineData("砍一刀就能把这段改完。")]
    [InlineData("这一刀最狠，直接补一刀就行。")]
    [InlineData("这个报错咬了我一下。")]
    public void 暴力动词在文档档也报(string text)
    {
        Assert.NotEmpty(Hits(text, "doc", 暴力动词));
    }

    [Theory]
    [InlineData("一刀切的策略不可取。")]
    [InlineData("他一口咬定是环境问题。")]
    [InlineData("齿轮咬合不良会影响寿命。")]
    public void 暴力动词的术语护标挡住正常用法(string text)
    {
        Assert.Empty(Hits(text, "doc", 暴力动词));
    }

    [Fact]
    public void 连续碎片短句作为统计项报出()
    {
        var notes = LintRunner.Run(
                new[] { ("样本.txt", "他没说话。她也没说。屋里很静。风停了。") }, null, null, "fiction")
            .Reports[0].Notes;

        Assert.Contains(notes, n => n.Id == 碎片短句);
    }

    [Fact]
    public void 长短句交错的段落不报碎片短句()
    {
        var notes = LintRunner.Run(
                new[] { ("样本.txt", "他停下来，把杯子放回桌上，看了她一会儿才开口。没事。然后他走了。") },
                null, null, "fiction")
            .Reports[0].Notes;

        Assert.DoesNotContain(notes, n => n.Id == 碎片短句);
    }

    [Theory]
    [InlineData("他看了她一眼，没说话。她把杯子放下，起身走了。")]
    [InlineData("她微微点头，没接话。")]
    [InlineData("他猛地站起，椅子刮过地面。")]
    public void 小说档的正常描写不报(string text)
    {
        foreach (var id in new[] { 网文脸, 陈词比喻 })
            Assert.Empty(Hits(text, "fiction", id));
        // 程度词与副词都是「同段凑够次数」才报：单次出现是正常写法
        Assert.Empty(Hits(text, "fiction", 程度词堆叠));
        Assert.Empty(Hits(text, "fiction", 副词重复));
    }

    [Fact]
    public void 小说档仍跑通用规则()
    {
        // 档位是过滤不是替换：小说档照样要抓套话
        Assert.NotEmpty(Hits("不可否认，这套方案赋能了整个流程。", "fiction", 套话词));
    }

    [Fact]
    public void 不给档位时跑全部规则()
    {
        Assert.NotEmpty(Hits("此刻，她终于意识到自己错了。", null, 心理告知));
        Assert.NotEmpty(Hits("不可否认，这套方案赋能了整个流程。", null, 套话词));
    }

    [Fact]
    public void 未知档位即抛()    {
        var ex = Assert.Throws<ArgumentException>(() =>
            LintRunner.Run(new[] { ("a.txt", "干净的一段话。") }, null, null, "poetry"));

        Assert.Contains("--genre", ex.Message);
    }

    [Fact]
    public void 点名了不在该档的规则即抛()
    {
        // 「零命中」与「这条规则不在这个档」是两种完全不同的沉默，后者必须说出来
        var ex = Assert.Throws<ArgumentException>(() =>
            LintRunner.Run(new[] { ("a.txt", "干净的一段话。") }, new[] { 网文脸 }, null, "doc"));

        Assert.Contains(网文脸, ex.Message);
    }

    [Fact]
    public void 档位与只跑指定规则可叠加()
    {
        var hits = LintRunner.Run(
                new[] { ("样本.txt", "此刻，她终于意识到自己错了。不可否认，这套方案赋能了整个流程。") },
                new[] { 心理告知 }, null, "fiction")
            .Reports[0].Hits;

        Assert.All(hits, h => Assert.Equal(心理告知, h.Id));
        Assert.NotEmpty(hits);
    }
}
