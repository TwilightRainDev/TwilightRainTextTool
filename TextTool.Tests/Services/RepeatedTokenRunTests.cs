namespace TextTool.Tests.Services;

/// <summary>
/// 复读串（repeated-token-run）用例：同一个单元在同一段里连写四遍以上，报整串一处。
///
/// 立据是它要替掉「四十几条命中讲同一件事」的噪音，所以除了报不报，还钉住两件事：
/// 串里的单点命中不再出现；语气字与拟声字连写不算，那是说话声不是复读。
/// </summary>
public class RepeatedTokenRunTests
{
    private const string 复读串 = "repeated-token-run";
    private const string 副词堆叠 = "adverb-overuse";

    private static List<LintHit> Hits(string text) =>
        LintRunner.Run(new[] { ("样本.txt", text) }, null, null, null).Reports[0].Hits;

    [Fact]
    public void 连写四遍报一处_并给出次数()
    {
        var run = Hits("我认输了。极其极其极其极其极其极其。").Single(h => h.Id == 复读串);

        Assert.Equal("极其", run.Match);
        Assert.Contains("6 次", run.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void 串里的单点命中不再各报一遍()
    {
        // 六处「极其」本来会各报一条副词堆叠；报过整串之后，报告里只剩那一条。
        var hits = Hits("极其极其极其极其极其极其");

        Assert.Single(hits);
        Assert.Equal(复读串, hits[0].Id);
        Assert.DoesNotContain(hits, h => h.Id == 副词堆叠);
    }

    [Fact]
    public void 两字单元报整串()
    {
        var run = Hits("不是不是不是不是不是。").Single(h => h.Id == 复读串);

        Assert.Equal("不是", run.Match);
        Assert.Contains("5 次", run.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void 连写三遍不报()
    {
        Assert.DoesNotContain(Hits("极其极其极其。"), h => h.Id == 复读串);
    }

    [Theory]
    [InlineData("哈哈哈哈哈哈哈哈。")]
    [InlineData("呵呵呵呵呵呵。")]
    public void 语气字与拟声字连写不算(string text)
    {
        Assert.DoesNotContain(Hits(text), h => h.Id == 复读串);
    }

    [Theory]
    [InlineData("很远很远。")]
    [InlineData("很久很久以前。")]
    public void 正常的重复不报(string text)
    {
        Assert.DoesNotContain(Hits(text), h => h.Id == 复读串);
    }
}
