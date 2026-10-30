namespace TextTool.Tests.Services;

/// <summary>
/// 同义连写（fiction-synonym-chain）用例。
///
/// 这一族收的是「同一件事说两遍」：目光一凛与眼神一凛并排，心中一动与心头一震并排。
/// 判据按段内密度算，同段凑够两处才报，所以单处是正常写法，用例两边都要钉。
/// 严重度 info：只提示，不给打断权。
/// </summary>
public class FictionSynonymChainTests
{
    private const string 同义连写 = "fiction-synonym-chain";

    private static List<LintHit> Hits(string text, string? genre) =>
        LintRunner.Run(new[] { ("样本.txt", text) }, null, null, genre)
            .Reports[0].Hits.Where(h => h.Id == 同义连写).ToList();

    [Theory]
    [InlineData("她目光一凛，眼神一凛，把话咽了回去。")]
    [InlineData("他心中一动，心头一震，明白了她的意思。")]
    [InlineData("她声音很轻，声音不大，说的却是同一件事。")]
    public void 同段两处报出(string text)
    {
        Assert.Equal(2, Hits(text, "fiction").Count);
    }

    [Theory]
    [InlineData("她目光一凛，把话咽了回去。")]
    [InlineData("他心中一动，没说什么。")]
    [InlineData("她声音很轻，像是在说一件小事。")]
    public void 单处是正常写法(string text)
    {
        Assert.Empty(Hits(text, "fiction"));
    }

    [Fact]
    public void 两处落在不同段就不报()
    {
        Assert.Empty(Hits("她目光一凛。\n\n眼神一凛，把话咽了回去。", "fiction"));
    }

    [Fact]
    public void 非小说类型不报()
    {
        Assert.Empty(Hits("她目光一凛，眼神一凛，把话咽了回去。", "doc"));
    }

    [Fact]
    public void 严重度只提示()
    {
        var hit = Hits("她目光一凛，眼神一凛，把话咽了回去。", "fiction").First();

        Assert.Equal("info", hit.Severity);
    }
}
