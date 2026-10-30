namespace TextTool.Tests.Services;

/// <summary>
/// 比例式情绪（fiction-proportion-eyes）用例。
///
/// 社区把这个写法叫扇形统计图式眼神：一分不屑、两分不满、三分凉薄。
/// 判据按段内密度算，同段凑够两处才报，所以单处不响；
/// 数字后面必须紧跟情绪词，所以三分钟、三分之二、平静一分这些正常说法落在外面。
/// </summary>
public class FictionProportionEyesTests
{
    private const string 比例式情绪 = "fiction-proportion-eyes";

    private static List<LintHit> Hits(string text, string? genre) =>
        LintRunner.Run(new[] { ("样本.txt", text) }, null, null, genre)
            .Reports[0].Hits.Where(h => h.Id == 比例式情绪).ToList();

    [Theory]
    [InlineData("他眼里带着一分不屑、两分不满。")]
    [InlineData("她的目光里有三分凉薄，还有四分漫不经心。")]
    [InlineData("一分不屑；两分不满；三分讥笑。")]
    public void 同段两处以上报出(string text)
    {
        Assert.NotEmpty(Hits(text, "fiction"));
    }

    [Theory]
    [InlineData("她眼里只有一分不屑。")]
    [InlineData("抽搐持续大概三分钟。")]
    [InlineData("入口被碎石堵住三分之二。")]
    [InlineData("她的声音没法再平静一分。")]
    [InlineData("这牛排三分熟，八分饱就够了。")]
    public void 单处与正常说法不报(string text)
    {
        Assert.Empty(Hits(text, "fiction"));
    }

    [Fact]
    public void 非小说类型不报()
    {
        Assert.Empty(Hits("他眼里带着一分不屑、两分不满。", "doc"));
    }

    [Fact]
    public void 严重度可提醒()
    {
        var hit = Hits("他眼里带着一分不屑、两分不满。", "fiction").First();

        Assert.Equal("warn", hit.Severity);
    }
}
