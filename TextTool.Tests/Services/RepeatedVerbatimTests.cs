namespace TextTool.Tests.Services;

/// <summary>
/// 逐字重复：完全相同的段落或句子再次出现时报出来。
///
/// 这条针对的是采样退化：同一段被一字不差地又写一遍，是模型原地打转，不是修辞。
/// 只认逐字相同，近似复述不在里面——排比、副歌式的刻意反复都属于近似，收进来必然误伤。
/// 短于下限的段落与句子不比对，否则「好。」这类短句会把整篇报成重复。
/// </summary>
public class RepeatedVerbatimTests
{
    private const string 逐字重复 = "repeated-verbatim";

    private static List<LintHit> Hits(string text) =>
        LintRunner.Run(new[] { ("样本.txt", text) }, null, null, "fiction")
            .Reports[0].Hits.Where(h => h.Id == 逐字重复).ToList();

    private static readonly string 段 = "蝶缓缓俯身，那双深邃的蓝眼睛直视着奎的眼睛，唇瓣几乎要触碰到他的嘴唇。";

    [Fact]
    public void 整段逐字重复报出并指出首次位置()
    {
        var text = 段 + "\n" + 段;

        var hit = Assert.Single(Hits(text));
        Assert.Equal(2, hit.Line);
        Assert.Contains("第 1 行", hit.Detail);
        Assert.Equal(段, hit.Match);
    }

    [Fact]
    public void 段重复时不再逐句报()
    {
        // 同一段里三句全重复，只报段这一处：逐句再报三处只是噪声
        var text = 段 + "\n" + 段;

        Assert.Single(Hits(text));
    }

    [Fact]
    public void 三次重复报两处()
    {
        var text = 段 + "\n" + 段 + "\n" + 段;

        var hits = Hits(text);
        Assert.Equal(2, hits.Count);
        Assert.All(hits, h => Assert.Contains("第 1 行", h.Detail));
    }

    [Fact]
    public void 差一个字的近似复述不报()
    {
        var text = 段 + "\n" + 段.Replace("几乎", "险些");

        Assert.Empty(Hits(text));
    }

    [Fact]
    public void 短句重复不报()
    {
        Assert.Empty(Hits("好。\n好。"));
        Assert.Empty(Hits("他点了点头。\n他点了点头。"));
        // 句子下限也是同一把尺：十一字的句子重复了也不报
        Assert.Empty(Hits("她说这件事不必再提了。\n她说这件事不必再提了。"));
    }

    [Fact]
    public void 同一句在一段里出现两次也报()
    {
        var text = "她说这件事以后不必再提了。他抬头看她，什么也没说。她说这件事以后不必再提了。";

        var hit = Assert.Single(Hits(text));
        Assert.Contains("她说这件事以后不必再提了。", hit.Match);
    }

    [Fact]
    public void 正常的整段叙述重复不报()
    {
        var text = "他把外套搭在椅背上，坐下，等水凉。\n"
                 + "楼道里的灯坏了两盏，他摸着墙走下去。";

        Assert.Empty(Hits(text));
    }
}
