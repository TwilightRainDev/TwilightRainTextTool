using System.Text;

namespace TextTool.Tests.Services;

/// <summary>
/// 密度门限的分母对象。
///
/// 口径：
/// - 旧写法 MinDensityPerKChar 与新写法 Density{document} 必须给出同一个结果，
///   两种写法走的是同一条判定路径，不是两套算法；
/// - 只有 document 与 delta 实现了。paragraph 与 window 加载能过、扫到就抛「尚未实现」，
///   不静默退回全文口径——那样报出来的密度看着合理，其实不是配的那个人想要的；
/// - delta 的分母是「净增处数除以写后文本千字数」，没有基线就没有净增，此时报用法错。
/// </summary>
public class DensityBasisTests
{
    /// <summary>
    /// 垫文：每个字符都不同，凑到指定字数。同字连写会撞上复读串判据，
    /// 那条判据会把落在串里的其他命中一并撤掉，密度就量不出来了。
    /// </summary>
    private static string 垫文(int chars) =>
        string.Concat(Enumerable.Range(0, chars).Select(i => (char)(0x4E00 + i % 2000)));

    /// <summary>一份约 totalChars 字、恰好含 hits 处「探」的样稿。</summary>
    private static string 样稿(int 命中, int 字数)
    {
        int 段 = Math.Max(1, 字数 / (命中 + 1));
        var sb = new StringBuilder();
        for (int i = 0; i < 命中; i++)
        {
            sb.Append(垫文(段));
            sb.Append('探');
        }
        sb.Append(垫文(段));
        return sb.ToString();
    }

    private static LintRule 规则(string id) => new()
    {
        Id = id,
        Group = "test",
        Title = id,
        Kind = "literal",
        Scope = "document",
        Patterns = new List<string> { "探" },
        Genres = new List<string>(),
        Severity = "info",
    };

    private static List<LintHit> 扫(string text, params LintRule[] rules) =>
        new AiToneLintService(rules).Scan(text, "sample.md").Hits;

    // ================================================================
    //  兼容期：两种写法等价
    // ================================================================

    [Fact]
    public void 旧写法与新写法在同一份样稿上命中完全一致()
    {
        var 旧 = 规则("probe-legacy");
        旧.MinDensityPerKChar = 2;
        var 新 = 规则("probe-density");
        新.Density = new LintDensity { Basis = LintDensity.BasisDocument, Min = 2 };

        // 一份过门限、一份不过：两种情形都要对上，只比一种等于没比
        foreach (int 命中 in new[] { 1, 6 })
        {
            var text = 样稿(命中, 2000);
            var 旧命中 = 扫(text, 旧);
            var 新命中 = 扫(text, 新);

            Assert.Equal(旧命中.Count, 新命中.Count);
            Assert.Equal(
                旧命中.Select(h => (h.Line, h.Col, h.Match)),
                新命中.Select(h => (h.Line, h.Col, h.Match)));
        }
    }

    [Fact]
    public void 新写法按全文千字数判门限()
    {
        var rule = 规则("probe");
        rule.Density = new LintDensity { Basis = LintDensity.BasisDocument, Min = 2 };

        // 2000 字里 1 处 = 每千字 0.5，不到 2
        Assert.Empty(扫(样稿(1, 2000), rule));
        // 2000 字里 6 处 ≈ 每千字 3，过了 2
        Assert.Equal(6, 扫(样稿(6, 2000), rule).Count);
    }

    [Fact]
    public void 两种写法同时写即报错()
    {
        var rule = 规则("probe-both");
        rule.MinDensityPerKChar = 2;
        rule.Density = new LintDensity { Basis = LintDensity.BasisDocument, Min = 2 };
        var ex = Assert.Throws<ArgumentException>(() => LintRuleStore.Validate(new List<LintRule> { rule }));
        Assert.Contains("只能留一种", ex.Message);
    }

    // ================================================================
    //  尚未实现的两种基准
    // ================================================================

    [Fact]
    public void 段落基准扫到即报尚未实现()
    {
        var rule = 规则("probe-paragraph");
        rule.Density = new LintDensity { Basis = LintDensity.BasisParagraph, Min = 2 };

        // 校验认这个取值：能不能用是运行期的事，配置本身不是拼写错
        LintRuleStore.Validate(new List<LintRule> { rule });

        var ex = Assert.Throws<NotImplementedException>(
            () => 扫(样稿(3, 1000), rule));
        Assert.Contains("尚未实现", ex.Message);
        Assert.Contains("paragraph", ex.Message);
    }

    [Fact]
    public void 窗口基准扫到即报尚未实现()
    {
        var rule = 规则("probe-window");
        rule.Density = new LintDensity { Basis = LintDensity.BasisWindow, Min = 2, Window = 200 };

        LintRuleStore.Validate(new List<LintRule> { rule });

        var ex = Assert.Throws<NotImplementedException>(() => 扫(样稿(3, 1000), rule));
        Assert.Contains("window", ex.Message);
    }

    [Fact]
    public void 基准不在取值表里加载即报错()
    {
        var rule = 规则("probe-bogus");
        rule.Density = new LintDensity { Basis = "sentence", Min = 2 };
        var ex = Assert.Throws<ArgumentException>(() => LintRuleStore.Validate(new List<LintRule> { rule }));
        Assert.Contains("密度基准非法", ex.Message);
        Assert.Contains("sentence", ex.Message);
    }

    [Fact]
    public void 窗口长度写在别的基准上报错()
    {
        var rule = 规则("probe-window-on-document");
        rule.Density = new LintDensity { Basis = LintDensity.BasisDocument, Min = 2, Window = 200 };
        var ex = Assert.Throws<ArgumentException>(() => LintRuleStore.Validate(new List<LintRule> { rule }));
        Assert.Contains("Window", ex.Message);
    }

    [Fact]
    public void 窗口基准缺窗口长度报错()
    {
        var rule = 规则("probe-window-without-size");
        rule.Density = new LintDensity { Basis = LintDensity.BasisWindow, Min = 2 };
        var ex = Assert.Throws<ArgumentException>(() => LintRuleStore.Validate(new List<LintRule> { rule }));
        Assert.Contains("窗口长度", ex.Message);
    }

    [Fact]
    public void 密度下限非正数报错()
    {
        var rule = 规则("probe-zero");
        rule.Density = new LintDensity { Basis = LintDensity.BasisDocument, Min = 0 };
        var ex = Assert.Throws<ArgumentException>(() => LintRuleStore.Validate(new List<LintRule> { rule }));
        Assert.Contains("必须为正数", ex.Message);
    }

    // ================================================================
    //  delta 基准
    // ================================================================

    [Fact]
    public void 净增基准没有基线时抛用法错()
    {
        var rule = 规则("probe-delta");
        rule.Density = new LintDensity { Basis = LintDensity.BasisDelta, Min = 2 };

        var ex = Assert.Throws<ArgumentException>(() => 扫(样稿(3, 1000), rule));
        Assert.Contains("--baseline", ex.Message);
    }

    [Fact]
    public void 净增基准按净增处数除以写后千字数判()
    {
        var rule = 规则("probe-delta");
        rule.Density = new LintDensity { Basis = LintDensity.BasisDelta, Min = 2 };

        // 基线 3 处、正文 4 处：净增 1 处，正文约 2000 字 → 每千字 0.5，不到 2，一条都不报
        var baseline = 样稿(3, 2000);
        var 稀疏 = LintRunner.Run(
            new[] { ("body.md", 样稿(4, 2000)) }, null, null, rules: new List<LintRule> { rule },
            baseline: ("before.md", baseline)).Reports[0];
        Assert.Empty(稀疏.Hits);

        // 基线 3 处、正文 8 处：净增 5 处，每千字约 2.5，过门限，只报多出来的那 5 处
        var 密 = LintRunner.Run(
            new[] { ("body.md", 样稿(8, 2000)) }, null, null, rules: new List<LintRule> { rule },
            baseline: ("before.md", baseline)).Reports[0];
        Assert.Equal(5, 密.Hits.Count);
    }

    // ================================================================
    //  复制
    // ================================================================

    [Fact]
    public void 复制带上密度门限且与原件脱钩()
    {
        var rule = 规则("probe");
        rule.Density = new LintDensity { Basis = LintDensity.BasisDelta, Min = 2.5, Window = null };

        var copy = rule.Clone();
        Assert.Equal(LintDensity.BasisDelta, copy.Density!.Basis);
        Assert.Equal(2.5, copy.Density.Min);

        // 改了复制件不该动到原件：一份规则在合并、保存、编辑窗里各复制一次，
        // 共用同一个密度对象等于几处改动互相踩着
        copy.Density.Min = 9;
        Assert.Equal(2.5, rule.Density!.Min);
    }
}
