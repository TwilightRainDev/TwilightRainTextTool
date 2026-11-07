namespace TextTool.Tests.Services;

/// <summary>
/// 记事写成记账（accounting-word-misuse）的用例。
///
/// 判的是搭配：「记一笔」是记账时的说法（笔数账目），记事写「值得一记」。
/// 词面只收「值得…记一笔」这一形状，因为再宽就分不出真记账：
/// 「账上记一笔支出」「给他记一笔」都是汉语里的正常用法，收进来等于每篇账目都报。
///
/// 立据时量过 25 份真人博文（11.8 万字）与全库 757 份文件：这个形状命中 1 处，
/// 那 1 处在模型写的一段里，人写的稿子里零处。那一段后来改写了，现在全库零命中。
/// 它因此不是措辞建议，是机器痕迹，级别是警告；样本只此一处，别当成「模型必这么写」。
/// </summary>
public class AccountingWordMisuseTests
{
    private const string 记事写成记账 = "accounting-word-misuse";

    private static List<LintHit> Hits(string text, string? genre = null) =>
        LintRunner.Run(new[] { ("样本.txt", text) }, null, null, genre)
            .Reports[0].Hits.Where(h => h.Id == 记事写成记账).ToList();

    [Theory]
    [InlineData("它写了一遍自己怎么修坏了自己，这件事本身值得记一笔。")]
    [InlineData("这次回滚值得记一笔，原因不在代码。")]
    [InlineData("值得记上一笔。")]
    [InlineData("这件事值得单独记一笔。")]
    public void 记事写成记账报出(string text) =>
        Assert.NotEmpty(Hits(text));

    [Theory]
    [InlineData("这件事值得一记。")]
    [InlineData("账上记一笔支出，月底对一遍。")]
    [InlineData("他这态度，我给他记一笔。")]
    [InlineData("值得记下来的细节都写在本子上。")]
    public void 真记账与改好的写法不报(string text) =>
        Assert.Empty(Hits(text));

    [Fact]
    public void 三个档位都跑()
    {
        // 措辞对不对与文体无关，所以不绑档位：档位留空等于各档通用
        const string text = "这件事本身值得记一笔。";
        Assert.NotEmpty(Hits(text, "doc"));
        Assert.NotEmpty(Hits(text, "agent"));
        Assert.NotEmpty(Hits(text, "fiction"));
    }

    [Fact]
    public void 级别是警告()
    {
        // 唯一一处实测样本是模型写的，人写的零处，所以它够得上警告而不是提醒。
        // 降回 info 时这条红，改级别的人得先想清楚用什么证据换。
        var rule = LintRuleStore.Load().Single(r => r.Id == 记事写成记账);
        Assert.Equal("warn", rule.Severity);
        Assert.All(Hits("这件事本身值得记一笔。"), h => Assert.Equal("warn", h.Severity));
    }
}
