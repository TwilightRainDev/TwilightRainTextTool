namespace TextTool.Tests.Services;

/// <summary>
/// 叠着免责（hedge-denial-pile）的用例。
///
/// 判据只看形状：同一段里连着三处「不代表」「不意味着」「不能证明」。
/// 不设密度门限，也不用语料对账——人说话有目的，要么给判断，要么说不知道，
/// 不会一句话后面挂三个否定；真出现三连，靠数处数就够了。
///
/// 反例是「单处免责」与「两处」，这两种在技术文档里是必要的分寸。
/// </summary>
public class HedgeDenialPileTests
{
    private const string 叠着免责 = "hedge-denial-pile";

    private static List<LintHit> Hits(string text, string? genre) =>
        LintRunner.Run(new[] { ("样本.txt", text) }, null, null, genre)
            .Reports[0].Hits.Where(h => h.Id == 叠着免责).ToList();

    [Theory]
    [InlineData("测试通过不代表代码正确，测试失败不代表代码错误，哈希一致不代表项目可用。")]
    [InlineData("这不意味着你在防御，也不等于你没有防御，更不能证明你在回避。")]
    [InlineData("测试通过不代表结果可信，哈希一致不代表项目可用，回滚成功不代表问题解决。")]
    public void 一段里三处否定式免责报出(string text)
    {
        Assert.NotEmpty(Hits(text, "agent"));
    }

    [Theory]
    [InlineData("这次结果不代表普遍规律，样本只有六个。")]
    [InlineData("他没说同意，这不代表反对。")]
    [InlineData("这不完全等同于他不同意，但也不能说明他反对。")]
    [InlineData("仅供参考，不构成投资建议。")]
    [InlineData("样本不代表全体，结论还不够硬。\n\n另一次统计不意味着方法有问题。")]
    public void 单处与两处免责不报(string text)
    {
        Assert.Empty(Hits(text, "agent"));
    }

    [Fact]
    public void 只对智能体回复与编程文档生效()
    {
        const string text = "这不代表 A，也不意味着 B，更不能证明 C。";
        Assert.NotEmpty(Hits(text, "agent"));
        Assert.NotEmpty(Hits(text, "doc"));
        Assert.Empty(Hits(text, "fiction"));
    }
}
