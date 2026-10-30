namespace TextTool.Tests.Services;

/// <summary>
/// 越界字符：允许面之外的字一个都不放行。
///
/// 面只有三块：英文可打印区、汉字各平面、中文全角标点。面外的箭头、方框线、几何图形、
/// 带圈数字、约等号、乘号、人民币符号，老控制台字体没有字形或宽度对不上，一行里出现一个，
/// 后面整行都会错位。
///
/// 已经各有判据的那几族不在这里重复报：表情、隐形字符、外文字母各归各条；弯引号、破折号、
/// 省略号在中文里是合格标点，也不在此列。
/// </summary>
public class CharFaceTests
{
    private const string 越界字符 = "char-face";

    private static List<LintHit> Hits(string text, string? genre) =>
        LintRunner.Run(new[] { ("样本.txt", text) }, null, null, genre)
            .Reports[0].Hits.Where(h => h.Id == 越界字符).ToList();

    [Theory]
    [InlineData("先改配置 → 再提交。")]
    [InlineData("┌──────┐")]
    [InlineData("进度 ● 完成。")]
    [InlineData("共 ① ② 三项。")]
    [InlineData("见 §3。")]
    [InlineData("清单一 • 二。")]
    [InlineData("权重 ≈ 0.8。")]
    [InlineData("误差 ±2 毫米。")]
    [InlineData("单价 ¥120。")]
    [InlineData("阶段 1–2。")]
    public void 面外字符报出(string text)
    {
        Assert.NotEmpty(Hits(text, "doc"));
    }

    [Theory]
    [InlineData("先改配置，再提交。")]
    [InlineData("他说：「明天见。」")]
    [InlineData("他说：“明天见。”")]
    [InlineData("他停了一下——也许是犹豫。")]
    [InlineData("这件事……算了。")]
    public void 面内字与另有判据的标点不报(string text)
    {
        Assert.Empty(Hits(text, "doc"));
    }

    [Theory]
    [InlineData("结果在下面 🎉")]
    [InlineData("他说 OK。")]
    [InlineData("希腊字母 α 与 β。")]
    public void 表情外文各归各条不在此报(string text)
    {
        // 这几族由表情、外文混排两条管；这里再报一次只会让同一处出现两条命中
        Assert.Empty(Hits(text, "agent"));
    }

    [Fact]
    public void 小说不跑()
    {
        Assert.Empty(Hits("先改配置 → 再提交。", "fiction"));
    }
}
