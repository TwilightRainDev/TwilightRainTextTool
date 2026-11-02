namespace TextTool.Tests.Services;

/// <summary>
/// 节拍化短句（sentence-tempo）用例。
///
/// 这一条看的是整篇的形状：按 400 到 800 字切窗口、每次挪 400 字，窗口里平均句长低于 15 字、
/// 最长句不超过 28 字即成立。
///
/// 它只出统计附注，不进命中：真人写的对话体与意识流小说在四千七百万字的真人库里出现 65 个
/// 这样的窗口，最密的一本 34 处。短句密度分不开人与模型，所以它只是观察，不是判据。
/// 与 short-sentence-run 的分工也要钉住：那条看「连着几句短」，本规则看「整窗没有一句长句」，
/// 本规则成立时那条不再重复报同一个现象。
/// </summary>
public class SentenceTempoTests
{
    private const string 节拍 = "sentence-tempo";

    private static LintReport Report(string text, string? genre = "fiction") =>
        LintRunner.Run(new[] { ("样本.txt", text) }, null, null, genre).Reports[0];

    /// <summary>把一句话重复到超过一个窗口：句句十来字，正是节拍的形状。</summary>
    private static string 节拍稿(int 句数 = 100) =>
        string.Concat(Enumerable.Repeat("他站着听了很久，没有说话。", 句数));

    /// <summary>长句为主的稿子：长短参差，平均句长在四十五字上下。</summary>
    private static string 长句稿(int 句数 = 20) =>
        string.Concat(Enumerable.Repeat(
            "他站在门口，把昨夜的每一句话在心里过了一遍，又觉得哪一句都不必再提，于是把外套挂回原处。", 句数));

    [Fact]
    public void 整窗都是短句时报出附注而不是命中()
    {
        var report = Report(节拍稿());

        Assert.Empty(report.Hits.Where(h => h.Id == 节拍));
        var note = Assert.Single(report.Notes.Where(n => n.Id == 节拍));
        Assert.Contains("最长 13 字", note.Text);
    }

    [Fact]
    public void 长句为主时不报()
    {
        Assert.Empty(Report(长句稿()).Notes.Where(n => n.Id == 节拍));
    }

    [Fact]
    public void 短于下限不判()
    {
        // 短稿的均句长由个别句子左右：四百字以下不判，宁可漏也不误报
        Assert.Empty(Report("他站着。她坐着。天亮了。").Notes.Where(n => n.Id == 节拍));
        Assert.Empty(Report(string.Concat(Enumerable.Repeat("他站着听了很久，没有说话。", 25))).Notes.Where(n => n.Id == 节拍));
    }

    [Fact]
    public void 有一句长句就不报()
    {
        // 整条判据要的是「一句都没说长」：均句长压到九字，但每隔二百字有一句三十四字的，就不是节拍
        string 单元 = string.Concat(Enumerable.Repeat("他站着听了很久。", 19))
            + "他把昨夜那些没说完的话在心里重新排了一遍，又一句一句地放回原处。";
        string text = string.Concat(Enumerable.Repeat(单元, 10));
        Assert.Empty(Report(text).Notes.Where(n => n.Id == 节拍));
    }

    [Fact]
    public void 小说档也跑()
    {
        // 短句在叙事里是正常手法，所以 choppy-clauses 在小说档不跑；这一条只是观察，各档都出
        Assert.Single(Report(节拍稿(), "fiction").Notes.Where(n => n.Id == 节拍));
        Assert.Single(Report(节拍稿(), null).Notes.Where(n => n.Id == 节拍));
    }

    [Fact]
    public void 报出时连续短句附注不再重复报()
    {
        var report = Report(节拍稿());

        Assert.Contains(report.Notes, n => n.Id == 节拍);
        Assert.DoesNotContain(report.Notes, n => n.Id == "short-sentence-run");
    }

    [Fact]
    public void 长文连着几窗都短时只报一处()
    {
        // 步长 400、窗口 800：报过一窗之后从该窗末尾接着看，同一段节拍不跟着步长刷屏
        var notes = Report(节拍稿(300)).Notes.Where(n => n.Id == 节拍).ToList();

        Assert.Single(notes);
    }
}
