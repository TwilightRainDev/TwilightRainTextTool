namespace TextTool.Tests.Services;

/// <summary>
/// 免责堆叠：同一段里叠三处以上「仅供参考」「不构成承诺」「不排除」「仅限于」这类限定。
///
/// 与段末兜底那条不是一回事：那条只看段落末尾三十字，管的是「句末来一句免责」；
/// 这条管的是整段都在打太极，限定语散在几百字里，窗口式判据抓不到。
/// 三处是下限：单处免责是必要的分寸，一处就报会把正常的技术说明与合规话术全部抓成免责腔。
/// </summary>
public class DisclaimerStackTests
{
    private const string 免责堆叠 = "disclaimer-stack";

    private static List<LintHit> Hits(string text, string? genre) =>
        LintRunner.Run(new[] { ("样本.txt", text) }, null, null, genre)
            .Reports[0].Hits.Where(h => h.Id == 免责堆叠).ToList();

    private const string 堆叠段 =
        "需要首先说明的是，以下表述仅旨在提供一种非穷尽性的分析视角，而不应被理解为对任何结论的无条件确认；"
        + "此处不排除其他解释的可能性，也不宜视为唯一、最终的答案，具体仍以现场核实的结果为准。";

    [Fact]
    public void 整段打太极报出()
    {
        var hits = Hits(堆叠段, "agent");

        Assert.NotEmpty(hits);
        Assert.Contains(hits, h => h.Match.Contains("非穷尽"));
        Assert.Contains(hits, h => h.Match.Contains("无条件确认"));
    }

    [Theory]
    [InlineData("以上数据仅供参考，具体以合同为准。")]
    [InlineData("本报告不构成投资建议。")]
    [InlineData("这个方案仅供参考，是否可行还要看现场条件。")]
    public void 单处免责不报(string text)
    {
        // 一处免责是分寸：字数不足三处就整体不报，这一条判的是堆叠
        Assert.Empty(Hits(text, "doc"));
    }

    [Fact]
    public void 正常叙述不报()
    {
        Assert.Empty(Hits("他敲了敲门，没人应，就把信从门缝里塞了进去。", "fiction"));
    }

    [Fact]
    public void 免责堆叠与段末兜底各管一头()
    {
        // 段末那句由 hedging-ending 管；这一段里三处限定由本条的段内门限管
        var text = "这件事我拿不准，未必能成。具体要看后面的安排，还得再核对一遍材料，"
                 + "并确认对方的口径是否一致，才能给出结论。";

        Assert.Empty(Hits(text, "doc"));
    }
}
