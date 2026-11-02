namespace TextTool.Tests.Services;

/// <summary>
/// 三项并列排比（triadic-parallel）用例。
///
/// 判的是形状：三项以上并列，每项以同一个字开头。判据认的是同字开头的节奏，与具体是哪个字无关。
///
/// 门限来自真人库 86 本、4696 万字：这种排比最密的一本每千字 0.10 处（罗生门），全库合计 96 处；
/// 一段百来字的宣言里出现一次就是每千字 8.5 处。全篇密度不到 1.0 不报。
/// </summary>
public class TriadicParallelTests
{
    private const string 排比 = "triadic-parallel";

    private static List<LintHit> Hits(string text) =>
        LintRunner.Run(new[] { ("样本.txt", text) }, null, null, "fiction")
            .Reports[0].Hits.Where(h => h.Id == 排比).ToList();

    [Fact]
    public void 同字开头的三项并列报出()
    {
        var hits = Hits("我将用最简单、最直接、最不绕弯子的语言告诉你。");

        var hit = Assert.Single(hits);
        Assert.Equal("info", hit.Severity);
    }

    [Fact]
    public void 不同字开头的三项并列不报()
    {
        // 顿号连写本身是正常的中文列举，判据只认同字开头的节奏
        Assert.Empty(Hits("桌上放着苹果、香蕉、橘子。"));
        Assert.Empty(Hits("他买了米、面、油，还有一袋盐。"));
    }

    [Fact]
    public void 两项并列不报()
    {
        Assert.Empty(Hits("最简单、最直接的说法。"));
    }

    [Fact]
    public void 长文里偶有一处不报()
    {
        // 真人长篇里这种排比每千字不超过 0.10 处，一段长文里出现一次够不上门限
        string text = "我将用最简单、最直接、最不绕弯子的语言告诉你。"
            + string.Concat(Enumerable.Repeat("他把窗子推开，让风进来。", 400));

        Assert.Empty(Hits(text));
    }

    [Fact]
    public void 同类写法也认()
    {
        // 判据认的是同字开头的三项节奏，与具体是哪个字无关：又、好、不都是同一个形状
        Assert.NotEmpty(Hits("他又高、又壮、又结实，站在那里像块门板。"));
        Assert.NotEmpty(Hits("不为名、不为利、不为别的，只为一口气。"));
    }
}
