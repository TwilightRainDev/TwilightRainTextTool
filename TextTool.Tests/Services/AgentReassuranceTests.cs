namespace TextTool.Tests.Services;

/// <summary>
/// 接住式安抚（agent-reassurance-pile）用例。
///
/// 收的是回复里连着来的安抚句：稳稳地接住你、你问到核心、太清醒了、彻底开悟、一步一步说清楚。
/// 这种句子单看都没问题，连着来就说明回答被安抚替掉了。
///
/// 门限两条一起用：全文至少两处，且每千字三处。依据是真人库 86 本、4696 万字里
/// 这几种说法合计只有 8 处（最密的一本每千字 0.02），而一段 137 字的回复里能出现 8 处。
/// 只给智能体回复这一类：小说里的「接住你」是动作，不是话术。
/// </summary>
public class AgentReassuranceTests
{
    private const string 安抚 = "agent-reassurance-pile";

    private static List<LintHit> Hits(string text, string? genre = "agent") =>
        LintRunner.Run(new[] { ("样本.txt", text) }, null, null, genre)
            .Reports[0].Hits.Where(h => h.Id == 安抚).ToList();

    private const string 安抚稿 =
        "我就在这里，稳稳地接住你，你问到问题的核心，你是太清醒了，"
        + "我逐步说清楚，你看完会彻底开悟，不用硬撑，不用向我解释。";

    [Fact]
    public void 连着几处安抚时报出()
    {
        var hits = Hits(安抚稿);

        Assert.True(hits.Count >= 2);
        Assert.All(hits, h => Assert.Equal("info", h.Severity));
    }

    [Fact]
    public void 单处安抚不报()
    {
        // 一句话里出现「接住你」是正常说法，凑够两处才是话术
        Assert.Empty(Hits("这里我接住你这句话往下说。"));
    }

    [Fact]
    public void 长文里密度不够不报()
    {
        string text = 安抚稿 + string.Concat(Enumerable.Repeat("接下来是三条具体做法。", 200));
        Assert.Empty(Hits(text));
    }

    [Fact]
    public void 只在智能体回复里报()
    {
        Assert.Empty(Hits(安抚稿, "fiction"));
        Assert.Empty(Hits(安抚稿, "doc"));
        Assert.NotEmpty(Hits(安抚稿, null));
    }
}
