namespace TextTool.Tests.Services;

/// <summary>
/// 一句里叠着比喻（fiction-simile-pile）用例。
///
/// 判的是形状，不是词表：一句六十字以上、串了三个以比况词引出的喻体。
/// 与两条比喻类规则的边界要钉住——陈词比喻认「哪个喻体被用旧了」，比喻词表数全篇的器物词，
/// 本规则只看一句之内，用的是新喻体也照报。
///
/// 门限来自真人库 86 本、4696 万字：最密的一本每千字 0.08 句，而一句两百字连缀八个比喻的稿子是
/// 每千字 4.69 句。全篇密度不到 1.0 就不报——一句长句带几个比喻，真人写作里也有。
/// </summary>
public class SimilePileTests
{
    private const string 叠喻 = "fiction-simile-pile";

    private static LintReport Report(string text, string? genre = "fiction") =>
        LintRunner.Run(new[] { ("样本.txt", text) }, null, null, genre).Reports[0];

    private static List<LintHit> Hits(string text, string? genre = "fiction") =>
        Report(text, genre).Hits.Where(h => h.Id == 叠喻).ToList();

    /// <summary>一句话串起八个比喻，中间没有一处停顿。</summary>
    private const string 叠喻句 =
        "说话像一颗石子投入湖中泛起一丝涟漪就像一根针扎破气球令我瞬间泄气放弃了思考"
        + "使我的手紧紧握住指节因为用力而泛白犹如一柄器械精准地划开我所有伪装"
        + "又将皮球踢给了我让我像受惊的小兽一般发出悲鸣和低沉的嘶吼"
        + "我的声音像砂纸打磨玻璃如同石头一般重重压得我喘不过气";

    [Fact]
    public void 一句里串三个以上比喻时报出()
    {
        var hits = Hits(叠喻句);

        var hit = Assert.Single(hits);
        Assert.Contains("串了", hit.Detail);
        Assert.Equal("info", hit.Severity);
    }

    [Fact]
    public void 短句里连用比喻不报()
    {
        // 不足六十字的句子里连用比喻是修辞，不是堆砌
        Assert.Empty(Hits("像风，像雨，像雾。天亮了。"));
    }

    [Fact]
    public void 一句只有两个比喻不报()
    {
        Assert.Empty(Hits("那句话像石子落进湖里，声音如同远处有人关门。"));
    }

    [Fact]
    public void 长文里只偶有一句不报()
    {
        // 密度门限每千字一处：真人长篇里这种句子每千字不超过 0.08 句，偶有一句不算病灶
        string text = 叠喻句 + string.Concat(Enumerable.Repeat("他把窗子推开，让风进来。", 400));
        Assert.Empty(Hits(text));
    }

    [Fact]
    public void 编程文档与智能体回复两档不判()
    {
        Assert.Empty(Hits(叠喻句, "doc"));
        Assert.Empty(Hits(叠喻句, "agent"));
        Assert.NotEmpty(Hits(叠喻句, null));
    }
}
