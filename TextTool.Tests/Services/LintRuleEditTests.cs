using TextTool.Services;

namespace TextTool.Tests.Services;

/// <summary>
/// 规则编辑窗保存那条路：表单只开放一部分字段，其余必须原样带回。
///
/// 拦的是一类不报错的错：编辑窗从空白规则重建一条规则时，没写到的字段就等于删了。
/// 词面表丢了只剩正则，密度门限丢了就变成「出现一次就报」，而校验都不拦——
/// 校验只看得到剩下的东西。所以这里比的是「表单没碰的字段还在不在」，不是某一条判据。
/// </summary>
public class LintRuleEditTests
{
    /// <summary>词面与正则都有的规则：这两列由同一个 Kind 之外的两个字段装，最容易只抄走一列。</summary>
    private static LintRule 双列表规则() => new()
    {
        Id = "probe-mixed",
        Group = "syntax",
        Title = "既有词面又有正则",
        Kind = "regex",
        Patterns = new List<string> { "首先[^。]*其次" },
        LiteralPatterns = new List<string> { "当时留下的", "没能复现" },
        Scope = "document",
        Genres = new List<string> { "doc" },
        MinCount = 1,
        Severity = "warn",
        Density = new LintDensity { Basis = LintDensity.BasisDocument, Min = 2.5 },
        DefaultOff = true,
        Detail = "说明",
        Hints = null,
        SuggestScheme = null,
    };

    [Fact]
    public void 编辑一条同时有词面与正则的规则保存后两者都还在()
    {
        using var dir = new TempDir();

        // 编辑窗只改标题与正则那一列，改完经编辑窗的入口重建整条、落盘、再读回
        var edited = LintRuleEdit.Rebuild(双列表规则(), rule =>
        {
            rule.Title = "改过的标题";
            rule.Patterns = new List<string> { "其次先[^。]*" };
        });
        LintRuleStore.SaveOverrides(new List<LintRule> { edited }, baseDir: dir.Path);

        var saved = Assert.Single(LintRuleStore.LoadOverrides(baseDir: dir.Path));
        Assert.Equal("改过的标题", saved.Title);
        Assert.Equal(new[] { "其次先[^。]*" }, saved.Patterns);
        Assert.Equal(new[] { "当时留下的", "没能复现" }, saved.LiteralPatterns!);
        Assert.Equal(2.5, saved.Density!.Min);
    }

    [Fact]
    public void 表单没碰的字段一律原样带回()
    {
        var original = 完整规则();
        var edited = LintRuleEdit.Rebuild(original, rule => rule.Title = "改过的标题");

        foreach (var property in typeof(LintRule).GetProperties())
        {
            // 只读的折算属性不是配置项；标题、正则那一列由表单改过，单独断言过
            if (property.SetMethod is null) continue;
            if (property.Name is nameof(LintRule.Title) or nameof(LintRule.Patterns)) continue;

            object? before = property.GetValue(original);
            object? after = property.GetValue(edited);
            if (before is LintDensity beforeDensity)
            {
                var afterDensity = Assert.IsType<LintDensity>(after);
                // 密度对象也要各复制一份：共用同一个实例，改一处会连带改掉别处的规则
                Assert.NotSame(beforeDensity, afterDensity);
                Assert.Equal(beforeDensity.Basis, afterDensity.Basis);
                Assert.Equal(beforeDensity.Min, afterDensity.Min);
                Assert.Equal(beforeDensity.Window, afterDensity.Window);
                continue;
            }
            if (before is IEnumerable<string> beforeList)
                Assert.Equal(beforeList, Assert.IsAssignableFrom<IEnumerable<string>>(after));
            else
                Assert.Equal(before, after);
        }
    }

    [Fact]
    public void 复制件改词面不动原件()
    {
        var original = 双列表规则();
        var edited = LintRuleEdit.Rebuild(original, rule => rule.LiteralPatterns!.Add("多一条"));

        Assert.Equal(2, original.LiteralPatterns!.Count);
        Assert.Equal(3, edited.LiteralPatterns!.Count);
    }

    /// <summary>每个字段都填上值：任何一项没被带回，上面那条逐字段比对就会指出来。</summary>
    private static LintRule 完整规则() => new()
    {
        Id = "probe-full",
        Group = "syntax",
        Title = "每个字段都有值",
        Kind = "regex",
        Patterns = new List<string> { "正则一", "正则二" },
        LiteralPatterns = new List<string> { "词面一" },
        Scope = "document",
        Genres = new List<string> { "doc", "fiction" },
        TailChars = null,
        MinCount = 3,
        Severity = "warn",
        BlocksReply = true,
        ReplyMinCount = 2,
        CollapseAtDensity = 7,
        Density = new LintDensity { Basis = LintDensity.BasisDocument, Min = 1.5, Window = null },
        MinDensityPerKChar = null,
        DefaultOff = true,
        SkipInMarkdown = true,
        Detail = "说明",
        Hints = new List<string> { "改法一", "改法二", "改法三" },
        SuggestScheme = "引号括号统一",
    };
}
