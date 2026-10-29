namespace TextTool.Tests.Services;

/// <summary>
/// 小说档的排版判据：正文加粗与机器式数值。
///
/// 它们不看用词，看字形：加粗标记是 markdown 语法，出版正文不这样排；小数、百分比、
/// 时间码、条款编号是机器与公文的写法，叙事写「七八成」「二百来块」。
/// 护栏那半边是本条的立据：真人科幻里数字并不少，三体写「放大一千八百九十万倍」
/// 「头发丝的十分之一」，全是汉字数字，一条都不该报。
/// </summary>
public class FictionReportToneTests
{
    // 规则 Id 只在这里各出现一次，用例正文一律按现象名引用。
    private const string 加粗模板 = "fiction-bold-template";
    private const string 机器式数值 = "fiction-machine-numbers";

    private static List<LintHit> Hits(string text, string? genre, string id) =>
        LintRunner.Run(new[] { ("样本.txt", text) }, null, null, genre)
            .Reports[0].Hits.Where(h => h.Id == id).ToList();

    [Theory]
    [InlineData("系统判定：**低熵表达，情感裸奔，价格协商未经修辞镀层。**")]
    [InlineData("**检测到家庭场景语用错位，建议启动共情接口降级方案。**")]
    [InlineData("他喊了一声**别动**。")]
    public void 正文里的加粗标记报出(string text)
    {
        Assert.NotEmpty(Hits(text, "fiction", 加粗模板));
    }

    [Theory]
    [InlineData("他喊了一声，别动。")]
    [InlineData("那一行字印得特别重，像有人用铅笔描过两遍。")]
    public void 不用加粗标记的强调不报(string text)
    {
        Assert.Empty(Hits(text, "fiction", 加粗模板));
    }

    [Theory]
    [InlineData("磨损0.5毫米道德镀层。")]
    [InlineData("人话使用率下降63.8%。")]
    [InlineData("00:01:06 真理溢出通风管。")]
    [InlineData("按《新语城低熵语言管理条例》第7条处罚。")]
    public void 机器式数值报出(string text)
    {
        Assert.NotEmpty(Hits(text, "fiction", 机器式数值));
    }

    [Theory]
    [InlineData("放大一千八百九十万倍，仍然完全光滑。")]
    [InlineData("头发丝的十分之一粗细，肉眼几乎不可见。")]
    [InlineData("他等了三个钟头。")]
    [InlineData("她一百米跑了十二秒。")]
    [InlineData("第一百零三条街的尽头有一家面馆。")]
    [InlineData("屏幕上的读数是6177906，他数了三遍。")]
    public void 汉字数字与大整数不报(string text)
    {
        // 真人科幻的数字不少，只是写成汉字；大整数单独出现没有单位也没有百分号，同样不算
        Assert.Empty(Hits(text, "fiction", 机器式数值));
    }

    [Fact]
    public void 排版判据不掺进文档类型()
    {
        // 加粗与数值在文档里是正常排版，只有小说正文才成为判据
        Assert.Empty(Hits("系统判定：**低熵表达。**", "doc", 加粗模板));
        Assert.Empty(Hits("接口超时设置0.5秒。", "doc", 机器式数值));
    }
}
