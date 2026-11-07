using System.Text.Json;
using TextTool.Services;

namespace TextTool.Tests.Services;

/// <summary>
/// 判据文案测试：规则表里的每一条数据判据、引擎里跑的每一个代码判据，三份语言文件里
/// 都要有 <c>LintRule_&lt;id&gt;_Title</c>（带说明的还要 <c>_Detail</c>），
/// 并且简中那份与规则表原文逐字相同。
///
/// 建议方案名（<c>_Suggest</c>）另算：它是交给命令行的数据，不是文案，
/// 三份语言一律写内置方案的原中文名，且要与规则表的 <c>SuggestScheme</c> 逐字相同。
///
/// 为什么需要这条：引擎取文案走 <c>_text.Get("LintRule_&lt;id&gt;_Title", 兜底中文)</c>，
/// 缺键时静默退回代码里或规则表里的简体中文。界面上不报错，只是繁体与英文用户在那里看到一片简体。
/// 简体那份又是规则表原文的副本，两边各改一次就分叉，而分叉以后在简中界面上看不出来。
/// 建议名分叉的后果更直接：界面让你去做的那一步，命令行会回「未找到方案」。
/// 这里读的是仓库里的源文件，测的是要提交的那份数据。
/// </summary>
public class LocalizationRuleTextTests
{
    /// <summary>支持的语言，与 Loc.SetLanguage 的分支一一对应。</summary>
    private static readonly string[] Languages = { "zh_CN", "zh_TW", "en_US" };

    /// <summary>
    /// 测试输出目录是 TextTool.Tests/bin/&lt;Configuration&gt;/net8.0，
    /// 往上四级刚好是仓库根目录（与 LocalizationDataTests 定位 Localization/ 的写法一致）。
    /// </summary>
    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    private static string JsonPath(string lang) => Path.Combine(RepoRoot, "Localization", lang + ".json");

    private static string RulesPath => Path.Combine(RepoRoot, "TextTool.Core", "default_lint_rules.json");

    private static string SchemesPath => Path.Combine(RepoRoot, "TextTool.Core", "default_schemes.json");

    private static readonly Dictionary<string, Dictionary<string, string?>> LangCache = new();
    private static List<RuleRow>? RuleCache;
    private static List<SchemeRow>? SchemeCache;

    /// <summary>
    /// 规则表里用得上的四个字段。其余字段（Patterns、Severity 等）与文案无关，不在这里反序列化。
    /// </summary>
    private sealed class RuleRow
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string? Detail { get; set; }
        public string? SuggestScheme { get; set; }
    }

    /// <summary>
    /// 内置方案名单里只用得上名字，替换规则本身不在这里反序列化。
    /// </summary>
    private sealed class SchemeRow
    {
        public string Name { get; set; } = "";
    }

    /// <summary>
    /// 读某份语言文件。文件缺失或 JSON 语法坏掉直接失败：这两种情况下游全是空字典，
    /// 「缺键」检查会变成一遍全绿的空转。
    /// </summary>
    private static Dictionary<string, string?> LoadLang(string lang)
    {
        lock (LangCache)
        {
            if (LangCache.TryGetValue(lang, out var cached))
                return cached;
        }

        string path = JsonPath(lang);
        Assert.True(File.Exists(path), $"本地化文件不存在：{path}");

        Dictionary<string, string?>? data;
        try
        {
            data = JsonSerializer.Deserialize<Dictionary<string, string?>>(
                File.ReadAllText(path), new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip });
        }
        catch (JsonException ex)
        {
            Assert.Fail($"{path} 不是合法的扁平 key -> string JSON：{ex.Message}");
            throw; // 让编译器相信 data 不可能继续为 null
        }

        Assert.NotNull(data);
        Assert.True(data!.Count > 0, $"{path} 解析出 0 个键，文件内容像是空的");

        lock (LangCache)
            LangCache[lang] = data;
        return data;
    }

    /// <summary>
    /// 读内置规则表。解析失败直接失败：解析出空表时下面每条判据都不存在，
    /// 全部断言会变成「空集合里挑不出错」而恒绿。
    /// </summary>
    private static List<RuleRow> Rules()
    {
        lock (LangCache)
        {
            if (RuleCache is not null)
                return RuleCache;
        }

        Assert.True(File.Exists(RulesPath), $"内置规则表不存在：{RulesPath}");

        List<RuleRow>? rules;
        try
        {
            rules = JsonSerializer.Deserialize<List<RuleRow>>(File.ReadAllText(RulesPath));
        }
        catch (JsonException ex)
        {
            Assert.Fail($"{RulesPath} 不是合法的规则数组：{ex.Message}");
            throw;
        }

        Assert.NotNull(rules);
        // 规则表被读成半截时下面的逐条检查会悄悄变少，留个下限兜住它。
        Assert.True(rules!.Count >= 80,
            $"内置规则表只解析出 {rules.Count} 条，解析或文件内容可能已经不对（正常是九十来条）");
        Assert.All(rules, r => Assert.False(string.IsNullOrWhiteSpace(r.Id), $"{RulesPath} 里有规则缺 Id"));

        lock (LangCache)
            RuleCache = rules;
        return rules;
    }

    /// <summary>
    /// 读内置方案名单。解析失败直接失败：解析出空表时「名字在不在名单里」的断言
    /// 会把所有建议名都判错，或者反过来静默恒绿。
    /// </summary>
    private static List<SchemeRow> Schemes()
    {
        lock (LangCache)
        {
            if (SchemeCache is not null)
                return SchemeCache;
        }

        Assert.True(File.Exists(SchemesPath), $"内置方案表不存在：{SchemesPath}");

        List<SchemeRow>? schemes;
        try
        {
            schemes = JsonSerializer.Deserialize<List<SchemeRow>>(File.ReadAllText(SchemesPath));
        }
        catch (JsonException ex)
        {
            Assert.Fail($"{SchemesPath} 不是合法的方案数组：{ex.Message}");
            throw;
        }

        Assert.NotNull(schemes);
        Assert.All(schemes!, s => Assert.False(string.IsNullOrWhiteSpace(s.Name), $"{SchemesPath} 里有方案缺 Name"));

        lock (LangCache)
            SchemeCache = schemes;
        return schemes;
    }

    /// <summary>
    /// 拦：规则表里某条判据在三份语言文件里缺 Title，或规则表带 Detail 而语言文件里没有 Detail。
    /// 失败信息按语言分组列出具体缺的键，可以直接拿去补翻译。
    /// </summary>
    [Fact]
    public void 数据判据在三份语言里都有标题与说明()
    {
        var rules = Rules();
        var data = Languages.ToDictionary(l => l, LoadLang);

        var problems = new List<string>();
        foreach (string lang in Languages)
        {
            var missing = new List<string>();
            foreach (var rule in rules)
            {
                if (!data[lang].ContainsKey($"LintRule_{rule.Id}_Title"))
                    missing.Add($"LintRule_{rule.Id}_Title");
                if (rule.Detail is not null && !data[lang].ContainsKey($"LintRule_{rule.Id}_Detail"))
                    missing.Add($"LintRule_{rule.Id}_Detail");
            }

            if (missing.Count > 0)
            {
                problems.Add($"  {lang}.json 缺 {missing.Count} 个键："
                    + string.Join(", ", missing.OrderBy(k => k, StringComparer.Ordinal)));
            }
        }

        Assert.True(problems.Count == 0,
            $"规则表里的判据在三份语言文件里没有补齐（共 {rules.Count} 条判据）："
            + Environment.NewLine + string.Join(Environment.NewLine, problems));
    }

    /// <summary>
    /// 拦：简中那份 Title/Detail 与规则表原文逐字不同。
    /// 简体是唯一真源，语言文件里那份只是副本；两边各改一次就分叉，
    /// 而分叉以后简中界面看起来总是对的，所以只能靠这条测试挡住。
    /// </summary>
    [Fact]
    public void 简中标题与说明与规则表逐字相同()
    {
        var rules = Rules();
        var zhCn = LoadLang("zh_CN");

        var problems = new List<string>();

        void Compare(string ruleId, string field, string expected)
        {
            string key = $"LintRule_{ruleId}_{field}";
            if (!zhCn.TryGetValue(key, out string? actual))
            {
                problems.Add($"  {key} —— zh_CN.json 里没有这个键（规则表原文：{expected}）");
                return;
            }

            if (!string.Equals(actual, expected, StringComparison.Ordinal))
            {
                problems.Add($"  {key}"
                    + Environment.NewLine + $"      语言文件：{actual}"
                    + Environment.NewLine + $"      规则表：  {expected}");
            }
        }

        foreach (var rule in rules)
        {
            Compare(rule.Id, "Title", rule.Title);
            if (rule.Detail is not null)
                Compare(rule.Id, "Detail", rule.Detail);
        }

        Assert.True(problems.Count == 0,
            $"以下简中译文与 default_lint_rules.json 的原文不一致（共 {problems.Count} 处）："
            + Environment.NewLine + string.Join(Environment.NewLine, problems));
    }

    /// <summary>
    /// 拦：三份语言文件里 <c>LintRule_&lt;id&gt;_Suggest</c> 与规则表同一条规则的 <c>SuggestScheme</c> 不同。
    /// 方案名是交给命令行的数据（<c>replace --scheme &lt;名字&gt;</c>），三份语言一律照中文原名写，不翻译。
    /// 两边一分叉，界面建议的名字在命令行里就是「未找到方案」，而简中界面上看不出来。
    /// </summary>
    [Fact]
    public void 建议方案名与规则表逐字相同()
    {
        var rules = Rules().Where(r => r.SuggestScheme is not null).ToList();
        Assert.NotEmpty(rules);

        var data = Languages.ToDictionary(l => l, LoadLang);

        var problems = new List<string>();
        foreach (string lang in Languages)
        {
            foreach (var rule in rules)
            {
                string key = $"LintRule_{rule.Id}_Suggest";
                if (!data[lang].TryGetValue(key, out string? actual))
                {
                    problems.Add($"  {lang}.json 里没有 {key}（规则表原文：{rule.SuggestScheme}）");
                    continue;
                }

                if (!string.Equals(actual, rule.SuggestScheme, StringComparison.Ordinal))
                {
                    problems.Add($"  {key} @ {lang}.json"
                        + Environment.NewLine + $"      语言文件：{actual}"
                        + Environment.NewLine + $"      规则表：  {rule.SuggestScheme}");
                }
            }
        }

        Assert.True(problems.Count == 0,
            $"以下建议方案名与 default_lint_rules.json 的 SuggestScheme 不一致（共 {problems.Count} 处）："
            + Environment.NewLine + string.Join(Environment.NewLine, problems));
    }

    /// <summary>
    /// 拦：建议名在 <c>default_schemes.json</c> 里根本不存在。
    /// 规则表里的 <c>SuggestScheme</c> 和三份语言里的每个 <c>LintRule_&lt;id&gt;_Suggest</c>
    /// 都得是内置方案的名字，否则用户照界面建议跑 <c>replace --scheme &lt;名字&gt;</c> 会得到「未找到方案」。
    /// </summary>
    [Fact]
    public void 建议方案名都在内置方案名单里()
    {
        var names = Schemes().Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
        Assert.NotEmpty(names);

        var problems = new List<string>();

        foreach (var rule in Rules())
        {
            if (rule.SuggestScheme is null)
                continue;
            if (!names.Contains(rule.SuggestScheme))
                problems.Add($"  default_lint_rules.json 的 {rule.Id} 建议「{rule.SuggestScheme}」，内置方案名单里没有这个名字");
        }

        foreach (string lang in Languages)
        {
            foreach (var (key, value) in LoadLang(lang))
            {
                if (!key.StartsWith("LintRule_", StringComparison.Ordinal)
                    || !key.EndsWith("_Suggest", StringComparison.Ordinal))
                    continue;

                if (value is null || !names.Contains(value))
                    problems.Add($"  {key} @ {lang}.json 的值「{value}」不在内置方案名单里");
            }
        }

        Assert.True(problems.Count == 0,
            $"以下建议方案名对不上 default_schemes.json 的名单（共 {problems.Count} 处）："
            + Environment.NewLine + string.Join(Environment.NewLine, problems));
    }

    /// <summary>
    /// 拦：代码里跑的判据（AiToneLintService.AlgorithmRuleIds）在三份语言文件里缺 Title 或 Detail。
    /// 这批判据不在规则表里，上面两条测试看不到它们，漏翻时同样只会静默显示简体。
    /// 名单直接从引擎读，代码里新增一条判据而忘了补译文，这里就红。
    /// </summary>
    [Fact]
    public void 代码判据在三份语言里都有标题与说明()
    {
        var ids = AiToneLintService.AlgorithmRuleIds;
        Assert.NotEmpty(ids);

        var data = Languages.ToDictionary(l => l, LoadLang);

        var problems = new List<string>();
        foreach (string lang in Languages)
        {
            var missing = new List<string>();
            foreach (string id in ids)
            {
                if (!data[lang].ContainsKey($"LintRule_{id}_Title"))
                    missing.Add($"LintRule_{id}_Title");
                if (!data[lang].ContainsKey($"LintRule_{id}_Detail"))
                    missing.Add($"LintRule_{id}_Detail");
            }

            if (missing.Count > 0)
            {
                problems.Add($"  {lang}.json 缺 {missing.Count} 个键："
                    + string.Join(", ", missing.OrderBy(k => k, StringComparer.Ordinal)));
            }
        }

        Assert.True(problems.Count == 0,
            $"代码判据在三份语言文件里没有补齐（共 {ids.Count} 条）："
            + Environment.NewLine + string.Join(Environment.NewLine, problems));
    }
}
