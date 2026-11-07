namespace TextTool.Tests.Services;

/// <summary>
/// 叠着免责（hedge-denial-pile）的用例。
///
/// 判据只看形状：同一段里连着三处「不代表」「不意味着」「不能证明」。
///
/// 门限定在三处，不能降。口径：`HumanProject/Blog/source/_posts` 那 25 份博文，
/// 按空行分段，段内数「不代表|不意味着|不等于|不能证明|不能说明|不足以证明|不能断言|不能保证|不能排除」，
/// 一段三处的 1 段、恰好两处的 2 段；降到两处，多报的就是那 2 段。
/// 三处那一段照样报，这点噪声收下——真人那三处是三个主语各否各的，形状判据分不出来。
///
/// 反例是「单处免责」与「两处」，这两种在技术文档里是必要的分寸。
/// </summary>
public class HedgeDenialPileTests
{
    private const string 叠着免责 = "hedge-denial-pile";

    /// <summary>同一段里恰好两处否定。</summary>
    private const string 同段两处 = "这不代表结论错了，也不能证明方法有问题。";

    private static List<LintHit> Hits(string text, string? genre) =>
        LintRunner.Run(new[] { ("样本.txt", text) }, null, null, genre)
            .Reports[0].Hits.Where(h => h.Id == 叠着免责).ToList();

    /// <summary>内置规则表里的这一条，另开一份：改门限不连累别的用例。</summary>
    private static LintRule 改门限(int 门限)
    {
        var rule = LintRuleStore.Load().Single(r => r.Id == 叠着免责).Clone();
        rule.MinCount = 门限;
        return rule;
    }

    /// <summary>按指定门限跑一遍，数这条规则的处数。</summary>
    private static int 处数(string text, int 门限) =>
        LintRunner.Run(new[] { ("样本.txt", text) }, null, null, "agent", rules: new[] { 改门限(门限) })
            .Reports[0].Hits.Count(h => h.Id == 叠着免责);

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
    [InlineData(同段两处)]
    public void 单处与两处免责不报(string text)
    {
        Assert.Empty(Hits(text, "agent"));
    }

    [Fact]
    public void 同一段恰好两处的样例本身只够两处()
    {
        // 上一条「不报」要归功于门限三处，不能归功于样例凑不够：
        // 同一段文字，门限三处零命中，门限两处正好两处。
        // 门限真被改成 2 时，这一条与上一条一起红。
        Assert.Equal(0, 处数(同段两处, 门限: 3));
        Assert.Equal(2, 处数(同段两处, 门限: 2));
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
