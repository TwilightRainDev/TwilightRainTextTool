namespace TextTool.Tests.Services;

/// <summary>
/// 汇报腔：降本、提效、赋能、抓手、拉通这一族。
///
/// 判据是段内两处起报：这些词单独出现常常是正常说法（这次降本方案先缓一缓），
/// 挨在一起才是对着同事讲话的腔调。落地、复盘、对齐不收：这三个词在技术文档里
/// 有正当用法，本工作区的变更记录里「落地」就出现了十次。
/// </summary>
public class DeckSpeakTests
{
    private const string 汇报腔 = "deck-speak";

    private static List<LintHit> Hits(string text, string? genre) =>
        LintRunner.Run(new[] { ("样本.txt", text) }, null, null, genre)
            .Reports[0].Hits.Where(h => h.Id == 汇报腔).ToList();

    [Fact]
    public void 汇报名词挨在一起报出()
    {
        var hits = Hits("调整目标是降本、提效还是收缩，先定一个。", "agent");

        Assert.Equal(3, hits.Count);
    }

    [Theory]
    [InlineData("这次降本方案先缓一缓。")]
    [InlineData("把那三条落地到文档里，再跑一遍测试。")]
    [InlineData("复盘的时候把这个问题带上。")]
    public void 单处与正当用法不报(string text)
    {
        Assert.Empty(Hits(text, "doc"));
    }

    [Fact]
    public void 小说不跑()
    {
        Assert.Empty(Hits("降本和提效都要，但先别收缩。", "fiction"));
    }
}
