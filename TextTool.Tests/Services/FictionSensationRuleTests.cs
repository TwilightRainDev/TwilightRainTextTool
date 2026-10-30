namespace TextTool.Tests.Services;

/// <summary>
/// 感受套话（fiction-sensation-tics）用例。
///
/// 这一族收的是「感受被写成现成标签」：如坠冰窟、大脑一片空白、提线木偶。
/// 判据锚在多字搭配上，因为单个词在真人稿里是写实的（手冻得冰凉、脑子一时没转过来），
/// 锚不住的词不收。严重度 info：只提示，不给打断权。
/// </summary>
public class FictionSensationRuleTests
{
    private const string 感受套话 = "fiction-sensation-tics";

    private static List<LintHit> Hits(string text, string? genre) =>
        LintRunner.Run(new[] { ("样本.txt", text) }, null, null, genre)
            .Reports[0].Hits.Where(h => h.Id == 感受套话).ToList();

    [Theory]
    [InlineData("我的大脑一片空白。")]
    [InlineData("耳边嗡嗡作响。")]
    [InlineData("那一刻他如坠冰窟。")]
    [InlineData("她浑身冰冷，手脚冰凉。")]
    [InlineData("他成了毫无生机的提线木偶。")]
    [InlineData("仿佛被一只无形的大手攥住。")]
    [InlineData("发出银铃般清脆的笑声。")]
    [InlineData("用极其低沉而富有磁性的声音说。")]
    [InlineData("像受惊的小兽一般缩起来。")]
    [InlineData("那双深邃的眼眸望着她。")]
    [InlineData("眼眸如璀璨的星辰。")]
    public void 感受套话报出(string text)
    {
        Assert.NotEmpty(Hits(text, "fiction"));
    }

    [Theory]
    [InlineData("他手冻得冰凉，先搓了两下。")]
    [InlineData("脑子一时没转过来，他停了几秒。")]
    [InlineData("铃铛响了一声，很脆。")]
    public void 写实叙述不报(string text)
    {
        Assert.Empty(Hits(text, "fiction"));
    }

    [Fact]
    public void 非小说档不报()
    {
        // 不给档位等于跑全部规则（与加档位之前一致），所以只钉 doc 这一档。
        Assert.Empty(Hits("我的大脑一片空白。", "doc"));
    }

    [Fact]
    public void 严重度只提示()
    {
        var hit = Hits("我的大脑一片空白。", "fiction").Single();

        Assert.Equal("info", hit.Severity);
    }
}
