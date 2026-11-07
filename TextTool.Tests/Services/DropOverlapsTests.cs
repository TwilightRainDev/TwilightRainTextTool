namespace TextTool.Tests.Services;

/// <summary>
/// 同一条规则里重叠候选的取舍。
///
/// 一条规则可能有两张词表盖住同一个词（「砍一刀」与「这一刀」），也可能两条词表从不同位置
/// 咬住同一个词（「验收口径」里既有「口径」又有「收口」）。这些都要算同一处证据，只报一次，
/// 而且留长的那个：读者认的是整个词。
///
/// 取舍按优先级逐条过：长的先留，同长按起点升序，与已留的任一段都不相交才留；返回按起点升序。
/// 候选多时走前缀最大值索引、候选少时逐个比较，两条路必须给出同一个结果——下面最后一条
/// 用随机清单把这件事钉住，另外几条钉的是每一条具体规则。
/// </summary>
public class DropOverlapsTests
{
    /// <summary>候选写法：起点、长度，提示词只用于辨认留下的是哪一条。</summary>
    private static (int Index, int Length, string? Hint) 候选(int index, int length, string? hint = null) =>
        (index, length, hint);

    /// <summary>把结果写成「起点+长度」的形式，提示词单独列出来看是哪条候选留下的。</summary>
    private static string 位置(List<(int Index, int Length, string? Hint)> kept) =>
        string.Join(" ", kept.Select(k => $"{k.Index}+{k.Length}"));

    [Fact]
    public void 交叠时留长的()
    {
        // 两条都从同一处起头，长的那条是整词，短的是它的一截
        var kept = AiToneLintService.DropOverlaps(new List<(int Index, int Length, string? Hint)>
        {
            候选(10, 3, "短"),
            候选(10, 7, "长"),
        });

        Assert.Equal("10+7", 位置(kept));
        Assert.Equal("长", Assert.Single(kept).Hint);
    }

    [Fact]
    public void 同长时留起点靠前的()
    {
        var kept = AiToneLintService.DropOverlaps(new List<(int Index, int Length, string? Hint)>
        {
            候选(9, 5, "后"),
            候选(6, 5, "前"),
        });

        Assert.Equal("6+5", 位置(kept));
        Assert.Equal("前", Assert.Single(kept).Hint);
    }

    [Fact]
    public void 同长同起点时留候选里先出现的()
    {
        // 两条词表在同一处咬住同一个词：留下的 Hint 是候选顺序里的那一个
        var kept = AiToneLintService.DropOverlaps(new List<(int Index, int Length, string? Hint)>
        {
            候选(4, 3, "先"),
            候选(4, 3, "后"),
        });

        Assert.Equal("4+3", 位置(kept));
        Assert.Equal("先", Assert.Single(kept).Hint);
    }

    [Fact]
    public void 不相交的都留下并按起点升序返回()
    {
        // 候选给的是乱序，优先级顺序（长度降序）与起点顺序也不一致
        var kept = AiToneLintService.DropOverlaps(new List<(int Index, int Length, string? Hint)>
        {
            候选(30, 4),
            候选(2, 9),
            候选(20, 2),
        });

        Assert.Equal("2+9 20+2 30+4", 位置(kept));
    }

    [Fact]
    public void 多个区间互相交叠时长的先占位()
    {
        // 0..10 最长先留；3..8、5..12、9..14 都与它相交，全让给它；
        // 20..24 不相交，留下。留下的 2..9 之外那截（9..12）没有候选单独覆盖，也不补。
        var kept = AiToneLintService.DropOverlaps(new List<(int Index, int Length, string? Hint)>
        {
            候选(5, 7),    // 5..12
            候选(3, 5),    // 3..8
            候选(0, 10),   // 0..10
            候选(9, 5),    // 9..14
            候选(20, 4),   // 20..24
        });

        Assert.Equal("0+10 20+4", 位置(kept));
    }

    [Fact]
    public void 首尾相接不算相交()
    {
        // 10..15 与 15..20 只贴边：两段都留下
        var kept = AiToneLintService.DropOverlaps(new List<(int Index, int Length, string? Hint)>
        {
            候选(15, 5, "后"),
            候选(10, 5, "前"),
        });

        Assert.Equal("10+5 15+5", 位置(kept));
        Assert.Equal(new[] { "前", "后" }, kept.Select(k => k.Hint));
    }

    [Fact]
    public void 零长度的两条落在同一处都留下()
    {
        // 零长度与零长度在同一个位置上不算相交：两条都不盖住任何字
        var kept = AiToneLintService.DropOverlaps(new List<(int Index, int Length, string? Hint)>
        {
            候选(7, 0, "甲"),
            候选(7, 0, "乙"),
        });

        Assert.Equal("7+0 7+0", 位置(kept));
    }

    [Fact]
    public void 零长度落在已留区间里面被判相交()
    {
        // 零长度的那条在 0..10 里面：虽然是空的，位置仍被盖住，报它就是重复同一处证据
        var kept = AiToneLintService.DropOverlaps(new List<(int Index, int Length, string? Hint)>
        {
            候选(5, 0, "空"),
            候选(0, 10, "整段"),
        });

        Assert.Equal("0+10", 位置(kept));
    }

    [Fact]
    public void 零长度落在已留区间的起点或终点不算相交()
    {
        // 0..10 的起点与终点都是边界之外：offset < start+length 与 index+length > start 两条
        // 都不成立，两处零长度都留下
        var kept = AiToneLintService.DropOverlaps(new List<(int Index, int Length, string? Hint)>
        {
            候选(0, 10),
            候选(0, 0),
            候选(10, 0),
        });

        Assert.Equal("0+10 0+0 10+0", 位置(kept));
    }

    [Fact]
    public void 已留的零长度区间不会挡外面的大区间()
    {
        // 零长度那条不在 3..8 之内，不相交：两条都留
        var kept = AiToneLintService.DropOverlaps(new List<(int Index, int Length, string? Hint)>
        {
            候选(2, 0),
            候选(3, 5),
        });

        Assert.Equal("2+0 3+5", 位置(kept));
    }

    [Fact]
    public void 单条候选原样返回()
    {
        var candidates = new List<(int Index, int Length, string? Hint)> { 候选(3, 2, "只有一条") };

        Assert.Same(candidates, AiToneLintService.DropOverlaps(candidates));
    }

    /// <summary>
    /// 逐个比较的写法：候选少时走的就是这条，也是「留长的、同长留靠前的」的原文口径。
    /// 下面的随机清单拿它当参照。
    /// </summary>
    private static List<(int Index, int Length, string? Hint)> 逐个比较(
        List<(int Index, int Length, string? Hint)> candidates)
    {
        var kept = new List<(int Index, int Length, string? Hint)>();
        foreach (var c in candidates.OrderByDescending(c => c.Length).ThenBy(c => c.Index))
        {
            bool overlaps = kept.Any(k => c.Index < k.Index + k.Length && k.Index < c.Index + c.Length);
            if (!overlaps) kept.Add(c);
        }
        return kept.OrderBy(k => k.Index).ToList();
    }

    [Fact]
    public void 候选很多时与逐个比较逐条一致()
    {
        // 超过索引的门限（32 条）走索引那条路：随机清单上逐条比对两条路的结果，
        // 位置、长度、留下的提示词三样都要一致。段内候选上千条的长稿走的就是这条路。
        var random = new Random(20251107);
        for (int round = 0; round < 40; round++)
        {
            var candidates = new List<(int Index, int Length, string? Hint)>();
            int count = 33 + random.Next(300);
            for (int i = 0; i < count; i++)
                candidates.Add((random.Next(0, 400), random.Next(0, 12), $"#{i}"));

            var expected = 逐个比较(candidates);
            var actual = AiToneLintService.DropOverlaps(candidates);

            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void 相邻与零长度混在长清单里也一致()
    {
        // 位置挤在几十个字里、长度从零到九：相交、贴边、零长度三种边界同时出现，两条路仍要一致
        var random = new Random(4);
        for (int round = 0; round < 40; round++)
        {
            var candidates = new List<(int Index, int Length, string? Hint)>();
            int count = 50 + random.Next(50);
            for (int i = 0; i < count; i++)
                candidates.Add((random.Next(0, 60), random.Next(0, 10), $"#{i}"));

            Assert.Equal(逐个比较(candidates), AiToneLintService.DropOverlaps(candidates));
        }
    }
}
