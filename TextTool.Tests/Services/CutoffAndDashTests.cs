namespace TextTool.Tests.Services;

/// <summary>
/// 知识截止与回复里的破折号。
///
/// 知识截止那条原先只认「截至某年」，于是论文交代数据时点、引用模型自述、报道写核查范围，
/// 全被当成模型在为自己开脱。现在只认指向自己知识的那几句：我的知识截止、我的训练数据、
/// 无法确认之后的事。别人在写时点，不报。
///
/// 破折号在智能体回复里是造停顿，一处就报；同一条规则不跑小说与文档，
/// 因为叙述里的插入语本来就该用破折号。
/// </summary>
public class CutoffAndDashTests
{
    private const string 知识截止 = "training-cutoff-disclaimer";
    private const string 回复破折号 = "dash-pause-in-reply";

    private static List<LintHit> Hits(string text, string? genre, string id) =>
        LintRunner.Run(new[] { ("样本.txt", text) }, null, null, genre)
            .Reports[0].Hits.Where(h => h.Id == id).ToList();

    [Theory]
    [InlineData("我的知识截止到 2024 年 6 月，之后的进展我不了解。")]
    [InlineData("我的训练数据只到 2023 年，最新情况没法确认。")]
    [InlineData("根据我最后的训练更新，这件事发生在我知道的范围之外。")]
    public void 拿知识截止说事报出(string text)
    {
        Assert.NotEmpty(Hits(text, "agent", 知识截止));
    }

    [Theory]
    [InlineData("论文的数据截至 2024 年 6 月，全文于同年 10 月修订。")]
    [InlineData("前缀能确认其截至2024年6月在任；无法离线确认具体日期。")]
    [InlineData("该行的记录截至 2025 年 3 月，之后未再更新。")]
    public void 交代时点不报(string text)
    {
        Assert.Empty(Hits(text, "doc", 知识截止));
    }

    [Fact]
    public void 回复里一处破折号就报()
    {
        var hits = Hits("它的前缀赫然写着——那个旧名字。", "agent", 回复破折号);

        Assert.Single(hits);
        Assert.Equal("——", hits[0].Match);
    }

    [Theory]
    [InlineData("doc")]
    [InlineData("fiction")]
    public void 叙述里的插入语不报(string genre)
    {
        Assert.Empty(Hits("他停了一下——也许是犹豫，然后走了。", genre, 回复破折号));
    }
}
