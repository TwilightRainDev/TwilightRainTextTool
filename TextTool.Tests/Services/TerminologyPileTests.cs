namespace TextTool.Tests.Services;

/// <summary>
/// 术语与意象堆砌（terminology-pile）用例。
///
/// 判的是这批词在引号之外堆了多密：褶皱、熵增、递归、拓扑、量子纠缠、镜像、琥珀一类。
/// 真人库 86 本、4696 万字里最密的一本每千字 0.054 处，全篇堆这批词的稿子是 16 到 43 处，
/// 门限因此落在每千字 5 处。
///
/// 引号内的文字一律不计：一段人写的批评稿会把被批评的词原样念一遍，按字面数，
/// 那种稿子会比真正的病灶还密。判据要认的是「用」这批词，不是「念」这批词。
/// </summary>
public class TerminologyPileTests
{
    private const string 堆砌 = "terminology-pile";

    private static LintReport Report(string text, string? genre = "fiction") =>
        LintRunner.Run(new[] { ("样本.txt", text) }, null, null, genre).Reports[0];

    private static List<LintHit> Hits(string text, string? genre = "fiction") =>
        Report(text, genre).Hits.Where(h => h.Id == 堆砌).ToList();

    private static string 堆砌稿() =>
        "文明的褶皱里藏着熵增的递归，镜像是拓扑的量子纠缠；琥珀把星轨封进晶格，"
        + "超新星在坍缩里写十四行诗；镜像再一次递归，褶皱再一次熵增。";

    [Fact]
    public void 引号外堆这批词时报出()
    {
        var hits = Hits(堆砌稿());

        Assert.NotEmpty(hits);
        Assert.All(hits, h => Assert.Equal("info", h.Severity));
    }

    [Fact]
    public void 引号内念这批词不报()
    {
        // 人在批评这批词：把词念一遍给人看，全在引号里
        string text = "题目里出现了“降维碾压、密码学、效能密码、二级编码”这些词，"
            + "还有“架构、建构、结构”，一眼就能看出是拼出来的。"
            + string.Concat(Enumerable.Repeat("他把卷子翻过去，没再说话。", 60));

        Assert.Empty(Hits(text));
    }

    [Fact]
    public void 长文里偶有一两个不报()
    {
        string text = "文明的褶皱。" + string.Concat(Enumerable.Repeat("他把窗子推开，让风进来。", 400));
        Assert.Empty(Hits(text));
    }

    [Fact]
    public void 编程文档与智能体回复两档不判()
    {
        Assert.Empty(Hits(堆砌稿(), "doc"));
        Assert.Empty(Hits(堆砌稿(), "agent"));
        Assert.NotEmpty(Hits(堆砌稿(), null));
    }

    [Fact]
    public void 命中位置指向原文而不是归一字面()
    {
        // 繁体稿：判据按归一字面认词，报出来的字要是用户自己写的那几个
        string text = "文明的褶皺裡藏著熵增的遞歸，鏡像是拓撲的量子糾纏；琥珀把星軌封進晶格，"
            + "超新星在坍縮裡寫十四行詩；鏡像再一次遞歸。";

        var hits = Hits(text);
        Assert.NotEmpty(hits);
        Assert.All(hits, h => Assert.Contains(h.Match, text));
    }
}
