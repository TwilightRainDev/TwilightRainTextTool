using TextTool.Services;

namespace TextTool.Tests.Services;

/// <summary>
/// 引擎实际会做的事，与规则表、文案里写的事必须对上。
///
/// 两张对账表：
/// 1. 规则表里的算法规则与引擎的分派表双向对上，<see cref="AiToneLintService.AlgorithmRuleIds"/>
///    等于规则表里 Kind=algorithm 的那些 Id。算法规则现在只有规则表一个来源：
///    表里指名的算法必须真的存在，分派表里也不能有没人用的死算法，
///    同一条算法还不能被两条规则挂上（那会把它跑两遍，命中成对出现）。
/// 2. 谄媚判据的说明里写的实际行为（只认紧跟叹号的「好问题」）与命中一致。
///    说明里曾写着「护栏挡「修好问题」」，而模式里没有这层护栏，读说明的人会以为它不会误报。
/// </summary>
public class EngineRegistrationTests
{
    /// <summary>
    /// 点名跑一遍用的样本：一段逐字重复能咬到 repeated-verbatim，
    /// 长篇标记与叹号让窗口级、句子级的几条也有机会报，过滤口径才算真跑到了。
    /// </summary>
    private const string 样本 =
        "好问题！这一段话很长很长很长很长很长很长很长。\n\n这一段话很长很长很长很长很长很长很长。\n";

    [Fact]
    public void 登记的每条算法判据都能点名跑通()
    {
        var ids = AiToneLintService.AlgorithmRuleIds;
        // 空集合时下面的循环一次都不跑，断言恒绿：先把「表还在」这件事单独钉住
        Assert.NotEmpty(ids);

        var problems = new List<string>();
        foreach (string id in ids)
        {
            try
            {
                var report = LintRunner.Run(new[] { ("样本.md", 样本) }, new[] { id }, null).Reports[0];
                // 点名后报告里只剩这一个 Id：过滤认的是同一个 Id 拼写，大小写也算一种拼错
                var leaked = report.Hits.Select(h => h.Id)
                    .Where(hitId => !string.Equals(hitId, id, StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (leaked.Count > 0)
                    problems.Add($"  {id}：点名后还报出了 {string.Join("、", leaked)}");
            }
            catch (ArgumentException ex)
            {
                // 未知 Id、档位不符、密度基准要基线，都是「点名跑不起来」，一次列全
                problems.Add($"  {id}：{ex.Message}");
            }
        }

        Assert.True(problems.Count == 0,
            "以下算法判据点名跑不通（引擎会产出它，登记表或过滤口径却没认）："
            + Environment.NewLine + string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void 规则表里的算法规则与分派表双向对上()
    {
        var names = AiToneLintService.AlgorithmNames;
        Assert.NotEmpty(names);

        var algorithmRules = LintRuleStore.GetDefaultRules()
            .Where(rule => rule.Kind == LintRule.KindAlgorithm).ToList();
        Assert.NotEmpty(algorithmRules);

        var problems = new List<string>();
        foreach (var rule in algorithmRules)
        {
            if (rule.AlgorithmName is null
                || !names.Contains(rule.AlgorithmName, StringComparer.OrdinalIgnoreCase))
                problems.Add($"  规则 {rule.Id} 指向未知算法：{rule.AlgorithmName}");
        }

        // 分派表里没有规则认领的算法是死代码：改代码的人不知道它不跑，改规则表的人也看不到它
        var used = new HashSet<string>(
            algorithmRules.Select(rule => rule.AlgorithmName ?? ""), StringComparer.OrdinalIgnoreCase);
        foreach (string name in names)
            if (!used.Contains(name))
                problems.Add($"  算法 {name} 没有任何规则用它，引擎里那段代码不会跑");

        // 一条算法挂两条规则会把它跑两遍，同一个现象报出两处
        foreach (var group in algorithmRules
                     .GroupBy(rule => rule.AlgorithmName ?? "", StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
            problems.Add($"  算法 {group.Key} 被多条规则用到：{string.Join("、", group.Select(rule => rule.Id))}");

        // 公开的算法 Id 名单是从规则表算出来的：两边不一致说明有条款路径没走同一份来源
        var inTable = algorithmRules.Select(rule => rule.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var listed = AiToneLintService.AlgorithmRuleIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!inTable.SetEquals(listed))
            problems.Add("  AlgorithmRuleIds 与规则表里的算法规则不一致：表里多 "
                + $"「{string.Join("、", inTable.Except(listed))}」，名单里多「{string.Join("、", listed.Except(inTable))}」");

        Assert.True(problems.Count == 0,
            "规则表与引擎的分派表对不上（算法规则只该有规则表一个来源）："
            + Environment.NewLine + string.Join(Environment.NewLine, problems));
    }

    /// <summary>
    /// 谄媚判据的模式是「好问题」紧跟叹号，没有动词侧的任何后顾：
    /// 「修好问题！」与「好问题！」一样报，靠的是叹号这一个条件。
    /// 规则表与三份语言文件的说明都按这个事实写。
    /// 以后谁给模式补了否定后顾，这条会红——那就把说明、三份语言文件与这条一起改，
    /// 别再出现「说明里有一句护栏、代码里没有」。
    /// </summary>
    [Theory]
    [InlineData("好问题！我看看。")]
    [InlineData("修好问题！再看别的。")]
    public void 谄媚只认紧跟叹号的好问题(string text)
    {
        var hits = LintRunner.Run(new[] { ("样本.txt", text) }, new[] { "sycophancy" }, null)
            .Reports[0].Hits;
        Assert.Contains(hits, h => string.Equals(h.Id, "sycophancy", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>没有叹号的「好问题」不报：这一条才是它真正的护栏。</summary>
    [Fact]
    public void 谄媚不认没有叹号的好问题()
    {
        var hits = LintRunner.Run(new[] { ("样本.txt", "评论区里有不少好问题，可以看看。") },
            new[] { "sycophancy" }, null).Reports[0].Hits;
        Assert.DoesNotContain(hits, h => string.Equals(h.Id, "sycophancy", StringComparison.OrdinalIgnoreCase));
    }
}
