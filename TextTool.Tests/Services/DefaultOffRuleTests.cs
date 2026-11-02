namespace TextTool.Tests.Services;

/// <summary>
/// 默认不勾选的判据：不点名不跑，点名就跑。
///
/// 判据在真人语料上守不住误报时，删掉可惜、默认开着又会淹掉报告。
/// 中文夹英文就是这种：真人小说每千字 0.108 处，一本动画改编小说 2582 处，
/// 全是 ZERO、Knightmare 这类专名与「市zheng府」这种拼音替换——对作者来说全是噪声。
/// 所以它默认关着，要查这一类的人自己勾上或 --only 点名。
/// </summary>
public class DefaultOffRuleTests
{
    private const string 夹英文 = "他打开 iPhone 看了一眼。";

    [Fact]
    public void 不给档位也不点名时不跑()
    {
        var report = LintRunner.Run(new[] { ("样本.txt", 夹英文) }, null, null).Reports[0];
        Assert.Empty(report.Hits.Where(h => h.Id == "latin-word-in-cjk"));
    }

    [Fact]
    public void 给档位也不点名时不跑()
    {
        var report = LintRunner.Run(new[] { ("样本.txt", 夹英文) }, null, null, "fiction").Reports[0];
        Assert.Empty(report.Hits.Where(h => h.Id == "latin-word-in-cjk"));
    }

    [Fact]
    public void 点名就跑()
    {
        var report = LintRunner.Run(new[] { ("样本.txt", 夹英文) }, new[] { "latin-word-in-cjk" }, null, "fiction")
            .Reports[0];
        Assert.NotEmpty(report.Hits.Where(h => h.Id == "latin-word-in-cjk"));
    }

    [Fact]
    public void 生效表里默认不含它()
    {
        var table = LintRuleStore.Load();
        var active = LintRunner.ActiveRules(table, "fiction");

        Assert.DoesNotContain(active, r => r.Id == "latin-word-in-cjk");
        Assert.Contains(LintRunner.ActiveRules(table, "fiction", new[] { "latin-word-in-cjk" }),
            r => r.Id == "latin-word-in-cjk");
    }
}
