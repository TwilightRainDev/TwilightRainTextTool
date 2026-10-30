namespace TextTool.Tests.Services;

/// <summary>
/// 编号引用：拿一张别处的表当路标。
///
/// 判据是「读者手里只有这段话」。决策记录编号、章节条目号、阶段号、任务号、回归编号、
/// 审计条目号，凡是要靠别处一份清单才解得开的写法都算；把那条本身写进来才算说完。
///
/// 只跑智能体回复：文档里的章节号是交叉引用，读者手里本来就有那份文档；
/// 注释里的编号等 TextTool 能读源码注释时再管。
/// </summary>
public class NumberedReferenceTests
{
    private const string 编号引用 = "numbered-reference";

    private static List<LintHit> Hits(string text, string? genre) =>
        LintRunner.Run(new[] { ("样本.txt", text) }, null, null, genre)
            .Reports[0].Hits.Where(h => h.Id == 编号引用).ToList();

    [Theory]
    [InlineData("这条判据按 ADR-12 定的。")]
    [InlineData("具体见规范第 3 条。")]
    [InlineData("阶段 2 的产物已经出来了。")]
    [InlineData("Task 7 还没开始。")]
    [InlineData("任务 12 还没做。")]
    [InlineData("回归 #42 覆盖了这个路径。")]
    [InlineData("审计结论 3 也提到过。")]
    [InlineData("请对照第 4 章的做法执行。")]
    public void 编号当路标报出(string text)
    {
        Assert.NotEmpty(Hits(text, "agent"));
    }

    [Theory]
    [InlineData("第二条我同意，别的先放一放。")]
    [InlineData("这是第二次调这个参数了。")]
    [InlineData("任务完成了，阶段性的东西明天再说。")]
    [InlineData("第 3 次跑还是同样的结果。")]
    public void 序数与次数不报(string text)
    {
        // 条与项要贴着文档名才判：单说「第二条」多半指上一段那几条，读者解得开
        Assert.Empty(Hits(text, "agent"));
    }

    [Fact]
    public void 同段两处编号各报一次且不互相吞掉()
    {
        var hits = Hits("这条按 ADR-12 定的，具体见规范第 3 条。", "agent");

        Assert.Equal(2, hits.Count);
        Assert.Contains(hits, h => h.Match == "ADR-12");
        Assert.Contains(hits, h => h.Match.EndsWith("第 3 条"));
    }

    [Fact]
    public void 文档类型不报()
    {
        Assert.Empty(Hits("具体见规范第 3 条，第 4 章有完整推导。", "doc"));
    }
}
