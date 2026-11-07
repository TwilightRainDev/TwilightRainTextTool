namespace TextTool.Tests.Services;

/// <summary>
/// 规则表里的那一条被改过之后，报告里该显示哪一份文字。
///
/// 取词顺序：自定义过的字段用规则表里的原文，其余去取译文，译文缺了才退回原文。
/// 道理是改的字出自用户自己，译文文件里那份说的是内置写法；反过来，没改过的那几处必须继续取译文，
/// 否则界面在繁中与英文下会整片退回简体——界面保存一条规则写的是整条副本，
/// 标题那一栏即便没动过也躺在文件里。
///
/// 命令行没有译文，一贯拿原文；按这条取词，它和界面报告在自定义过的那几处拿到同一份文字。
/// </summary>
public class CustomRuleTextTests
{
    /// <summary>
    /// 假译文：表里有的按键取，没有的退回调用点的原文——与真实现同一种行为。
    /// everyKey 为真时任何键都给一份译文，用来证明「自定义过的字段根本没去问译文」。
    /// </summary>
    private sealed class FakeText : ILintText
    {
        private static readonly Dictionary<string, string> Known = new()
        {
            ["LintRule_cliche-words_Title"] = "譯標題",
            ["LintRule_cliche-words_Detail"] = "譯說明",
            ["LintRule_cliche-words_Suggest"] = "譯方案",
            ["LintNote_paragraph-length-uniform"] = "譯統計",
        };

        private readonly bool _everyKey;

        public FakeText(bool everyKey = false) => _everyKey = everyKey;

        public string Get(string key, string fallback, params object[] args)
        {
            if (Known.TryGetValue(key, out string? value))
                return args.Length == 0 ? value : string.Format(value, args);

            return _everyKey ? $"[译]{key}" : fallback;
        }
    }

    /// <summary>被测的那条内置规则：三处文字齐全，好逐处看取的是哪一份。</summary>
    private static LintRule BuiltIn() => new()
    {
        Id = "cliche-words", Group = "lexis", Title = "套话词", Kind = "literal",
        Patterns = new List<string> { "赋能" }, Severity = "info",
        Detail = "套话/空词", SuggestScheme = "特殊符号清除",
    };

    /// <summary>
    /// 外部条目按界面保存的样子造：以内置为底整条复制，只改调用方点名的那几处。
    /// 直接新建一条只填要改的字段是不对的，那份副本的其余字段都成了默认值，
    /// 与文件里真正长什么样对不上。
    /// </summary>
    private static LintRule ExternalCopy(Action<LintRule> change)
    {
        var copy = BuiltIn();
        change(copy);
        return copy;
    }

    /// <summary>拿合并好的规则表扫一遍，交给调用方逐处看文字。</summary>
    private static LintReport Scan(string text, IReadOnlyList<LintRule> rules, ILintText? text_ = null) =>
        new AiToneLintService(rules, text_).Scan(text, "t.txt");

    private static LintHit SingleHit(IReadOnlyList<LintRule> rules, ILintText? text_ = null) =>
        Assert.Single(Scan("赋能", rules, text_).Hits);

    [Fact]
    public void 自定义标题盖过译文()
    {
        var rules = LintRuleStore.Merge(
            new List<LintRule> { BuiltIn() },
            new List<LintRule> { ExternalCopy(rule => rule.Title = "我这台机器上叫它套话词表") });

        var hit = SingleHit(rules, new FakeText());

        Assert.Equal("我这台机器上叫它套话词表", hit.Title);
        Assert.Equal("譯說明", hit.Detail);          // 没动过的那两处仍取译文
        Assert.Equal("譯方案", hit.SuggestScheme);
    }

    [Fact]
    public void 自定义建议方案盖过译文()
    {
        var rules = LintRuleStore.Merge(
            new List<LintRule> { BuiltIn() },
            new List<LintRule> { ExternalCopy(rule => rule.SuggestScheme = "横线省略号→逗号") });

        var hit = SingleHit(rules, new FakeText());

        Assert.Equal("横线省略号→逗号", hit.SuggestScheme);
        Assert.Equal("譯標題", hit.Title);
        Assert.Equal("譯說明", hit.Detail);
    }

    [Fact]
    public void 没有自定义条目时三处文字都取译文()
    {
        var rules = LintRuleStore.Merge(new List<LintRule> { BuiltIn() }, new List<LintRule>());

        var hit = SingleHit(rules, new FakeText());

        Assert.Equal("譯標題", hit.Title);
        Assert.Equal("譯說明", hit.Detail);
        Assert.Equal("譯方案", hit.SuggestScheme);
    }

    [Fact]
    public void 自定义条目删掉后回退到译文()
    {
        var builtIn = new List<LintRule> { BuiltIn() };
        var external = new List<LintRule> { ExternalCopy(rule => rule.Title = "改过的标题") };

        Assert.Equal("改过的标题", SingleHit(LintRuleStore.Merge(builtIn, external), new FakeText()).Title);

        // 文件里那条删掉等于重新合并一份空表：标记在合并现场按内置重算，旧标记不带过来
        var reloaded = LintRuleStore.Merge(builtIn, new List<LintRule>());

        Assert.Equal("譯標題", SingleHit(reloaded, new FakeText()).Title);
    }

    [Fact]
    public void 只改了说明时标题仍用译文()
    {
        // 界面保存的副本里标题那一栏与内置一字不差，只有说明被改过：
        // 按「文件里有这一项」判会把标题也当成改过的，英文界面上就会出现一行简体标题
        var rules = LintRuleStore.Merge(
            new List<LintRule> { BuiltIn() },
            new List<LintRule> { ExternalCopy(rule => rule.Detail = "改过的说明") });

        var hit = SingleHit(rules, new FakeText());

        Assert.Equal("譯標題", hit.Title);
        Assert.Equal("改过的说明", hit.Detail);
        Assert.Equal("譯方案", hit.SuggestScheme);
    }

    [Fact]
    public void 新增的自定义判据根本不去问译文()
    {
        var custom = new LintRule
        {
            Id = "user-0a1b2c3d", Group = "lexis", Title = "我自己的判据", Kind = "literal",
            Patterns = new List<string> { "赋能" }, Severity = "info",
            Detail = "自己写的说明", SuggestScheme = "特殊符号清除",
        };

        var rules = LintRuleStore.Merge(new List<LintRule>(), new List<LintRule> { custom });

        // 任何键都给译文的那份：内置表里没有这条，译文里也不会有，取词不该往那边走
        var hit = SingleHit(rules, new FakeText(everyKey: true));

        Assert.Equal("我自己的判据", hit.Title);
        Assert.Equal("自己写的说明", hit.Detail);
        Assert.Equal("特殊符号清除", hit.SuggestScheme);
    }

    [Fact]
    public void 命令行与界面在自定义过的那几处拿到同一份文字()
    {
        var rules = LintRuleStore.Merge(
            new List<LintRule> { BuiltIn() },
            new List<LintRule>
            {
                ExternalCopy(rule =>
                {
                    rule.Title = "改过的标题";
                    rule.Detail = "改过的说明";
                    rule.SuggestScheme = "改过的方案";
                }),
            });

        // 命令行注入的是 ChineseLintText，界面注入的是按当前语言取词的实现
        var commandLine = SingleHit(rules, ChineseLintText.Instance);
        var gui = SingleHit(rules, new FakeText());

        Assert.Equal("改过的标题", commandLine.Title);
        Assert.Equal(commandLine.Title, gui.Title);
        Assert.Equal(commandLine.Detail, gui.Detail);
        Assert.Equal(commandLine.SuggestScheme, gui.SuggestScheme);
    }

    [Fact]
    public void 改过的说明也用在统计附注上()
    {
        // 统计行说的也是这条判据，说明被改过时不该还念译文里那句内置写法
        var builtIn = LintRuleStore.GetDefaultRules()
            .Where(rule => rule.Id == "paragraph-length-uniform")
            .ToList();
        Assert.Single(builtIn);

        // 六段一样长：变异系数为 0，这条判据出一行统计
        string text = string.Join("\n", Enumerable.Repeat(new string('字', 30), 6));

        var plain = Scan(text, builtIn, new FakeText());
        Assert.Equal("譯統計", Assert.Single(plain.Notes).Text);

        var custom = builtIn[0].Clone();
        custom.Detail = "改过的说明";
        var merged = LintRuleStore.Merge(builtIn, new List<LintRule> { custom });
        var editedReport = Scan(text, merged, new FakeText());

        Assert.Equal("改过的说明", Assert.Single(editedReport.Notes).Text);
    }

    [Fact]
    public void 自定义标记不写进配置文件()
    {
        using var dir = new TempDir();
        var external = ExternalCopy(rule => rule.Title = "改过的标题");

        LintRuleStore.SaveOverrides(new List<LintRule> { external }, dir.Path);

        string json = File.ReadAllText(Path.Combine(dir.Path, LintRuleStore.OverrideFileName));
        Assert.DoesNotContain(nameof(LintRule.CustomTextFields), json);

        // 读回来的是文件里那份：标记不在文件里，要等合并现场按内置重算
        var saved = Assert.Single(LintRuleStore.LoadOverrides(dir.Path));
        Assert.Empty(saved.CustomTextFields);
        Assert.False(saved.UsesTableText(LintRule.TextFieldTitle));

        var merged = LintRuleStore.Merge(new List<LintRule> { BuiltIn() }, new List<LintRule> { saved });
        Assert.True(Assert.Single(merged).UsesTableText(LintRule.TextFieldTitle));
        Assert.False(merged[0].UsesTableText(LintRule.TextFieldDetail));
    }
}
