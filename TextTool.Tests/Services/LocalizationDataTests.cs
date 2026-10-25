using System.Text.Json;
using System.Text.RegularExpressions;

namespace TextTool.Tests.Services;

/// <summary>
/// 本地化数据（Localization/*.json）一致性测试。
/// 这里拦的是两类不会报错、只会静默翻车的错：
/// 一、某份语言少了键 —— 运行时 Loc.T 查不到，界面上直接少一行字；
/// 二、某份语言的占位符个数/编号跟别的语言不一样 —— Loc.T(key, args) 走 string.Format，
///     要么抛 FormatException 被 catch 吞成原文，要么参数被错位/丢弃。
/// 测试直接读仓库里的源文件，不读输出目录里的副本，测的是要提交的那份数据。
/// </summary>
public class LocalizationDataTests
{
    /// <summary>支持的语言，与 Loc.SetLanguage 的分支一一对应。</summary>
    private static readonly string[] Languages = { "zh_CN", "zh_TW", "en_US" };

    /// <summary>
    /// 占位符：{0}、{1}、{0:N0} 之类。
    /// (?&lt;!\{) 是为了不把 C# 格式化里的转义大括号 {{0}} 当成占位符。
    /// </summary>
    private static readonly Regex PlaceholderRegex = new(@"(?<!\{)\{(\d+)[^{}]*\}", RegexOptions.Compiled);

    /// <summary>
    /// 测试输出目录是 TextTool.Tests/bin/&lt;Configuration&gt;/net8.0-windows，
    /// 往上四级刚好是仓库根目录（与 PublishScriptTests 定位 scripts/ 的写法一致）。
    /// </summary>
    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    private static string JsonPath(string lang) => Path.Combine(RepoRoot, "Localization", lang + ".json");

    private static readonly Dictionary<string, Dictionary<string, string?>> Cache = new();

    /// <summary>
    /// 读某份语言文件。文件缺失或 JSON 语法坏掉直接失败 —— 这两种情况下游全是空字典，
    /// 会让“两边键集合相等”这类断言在两边都空时变成恒真。
    /// </summary>
    private static Dictionary<string, string?> Load(string lang)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(lang, out var cached))
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

        lock (Cache)
            Cache[lang] = data;
        return data;
    }

    /// <summary>取出某个串里的占位符编号集合（去重、升序）。</summary>
    private static List<int> Placeholders(string? value) =>
        value is null
            ? new List<int>()
            : PlaceholderRegex.Matches(value)
                .Select(m => int.Parse(m.Groups[1].Value))
                .Distinct()
                .OrderBy(n => n)
                .ToList();

    private static string Show(IEnumerable<int> numbers) =>
        string.Join(" ", numbers.Select(n => "{" + n + "}"));

    private static string ShowSet(IEnumerable<int> numbers) =>
        numbers.Any() ? Show(numbers) : "(无占位符)";

    /// <summary>
    /// 拦：某份语言少了键（界面少一行字）或多出别的语言没有的键（运行时永远取不到）。
    /// 失败信息列出具体的键名和它在哪份文件里缺席，可以直接拿去补翻译。
    /// </summary>
    [Fact]
    public void 三种语言的键集合完全相等()
    {
        var data = Languages.ToDictionary(l => l, Load);

        var problems = new List<string>();
        for (int i = 0; i < Languages.Length; i++)
        {
            for (int j = 0; j < Languages.Length; j++)
            {
                if (i == j) continue;
                string a = Languages[i], b = Languages[j];

                var missing = data[a].Keys.Except(data[b].Keys).OrderBy(k => k, StringComparer.Ordinal).ToList();
                if (missing.Count > 0)
                    problems.Add($"  {b}.json 缺 {missing.Count} 个键（{a}.json 里有）：{string.Join(", ", missing)}");
            }
        }

        Assert.True(problems.Count == 0,
            "三份语言文件的键集合不一致：" + Environment.NewLine + string.Join(Environment.NewLine, problems));
    }

    /// <summary>
    /// 拦：同一个键在不同语言里占位符个数或编号不同（如 zh_CN 是 {0} {1}、en_US 只有 {0}），
    /// 格式化时参数被错位或吞掉。失败信息给出键名和三份语言各自的占位符集合。
    /// </summary>
    [Fact]
    public void 每个键在三种语言里的占位符集合相同()
    {
        var data = Languages.ToDictionary(l => l, Load);

        // 三份文件的键并集：即使键集合本身不相等（上面那条测试管），这里也要每个键都查一遍。
        var allKeys = data.Values.SelectMany(d => d.Keys).Distinct().OrderBy(k => k, StringComparer.Ordinal).ToList();

        var problems = new List<string>();
        int withPlaceholders = 0;

        foreach (string key in allKeys)
        {
            var perLang = Languages.ToDictionary(
                l => l,
                l => data[l].TryGetValue(key, out var v) ? Placeholders(v) : new List<int>());

            if (perLang.Values.Any(p => p.Count > 0))
                withPlaceholders++;

            var distinct = perLang.Values.Select(Show).Distinct().ToList();
            if (distinct.Count > 1)
            {
                string detail = string.Join("；", Languages.Select(l => $"{l}: {ShowSet(perLang[l])}"));
                problems.Add($"  {key} —— {detail}");
            }
        }

        // 正则一旦失灵，上面全部比较都会变成“空集合 == 空集合”而恒真。
        // 现有数据有 52 个键带占位符，这里留出余量兜住正则退化。
        Assert.True(withPlaceholders >= 40,
            $"只有 {withPlaceholders} 个键检出占位符，占位符正则可能已失效，本测试的其余断言会失去意义");

        Assert.True(problems.Count == 0,
            $"以下键在三种语言里的占位符不一致（共 {problems.Count} 个）："
            + Environment.NewLine + string.Join(Environment.NewLine, problems));
    }

    /// <summary>
    /// 拦：占位符编号不从 {0} 起或不连续（只有 {1} 没有 {0}、{0} 和 {2} 跳掉 {1}）。
    /// 这种串 string.Format 会直接抛 FormatException（被 Loc.T 吞成未格式化原文），
    /// 而三种语言一起错的话上一条“三份一致”是查不出来的。
    /// </summary>
    [Fact]
    public void 占位符编号从0开始且连续()
    {
        var problems = new List<string>();

        foreach (string lang in Languages)
        {
            foreach (var (key, value) in Load(lang).OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                var numbers = Placeholders(value);
                if (numbers.Count == 0) continue;

                var expected = Enumerable.Range(0, numbers.Count).ToList();
                if (!numbers.SequenceEqual(expected))
                    problems.Add($"  {lang}.json / {key} —— 实际 {Show(numbers)}，应为 {Show(expected)}；原文：{value}");
            }
        }

        Assert.True(problems.Count == 0,
            $"以下占位符编号不从 {{0}} 开始连续（共 {problems.Count} 处）："
            + Environment.NewLine + string.Join(Environment.NewLine, problems));
    }

    /// <summary>
    /// 拦：值缺失或空白。JSON 里 value 为 null、空串、纯空白，
    /// Loc.T 都会返回一个“看起来像翻译了”的空串，界面上就是一片空白，排查时完全没有线索。
    /// </summary>
    [Fact]
    public void 没有空值()
    {
        var problems = new List<string>();

        foreach (string lang in Languages)
        {
            foreach (var (key, value) in Load(lang).OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                if (value is null)
                    problems.Add($"  {lang}.json / {key} —— 值为 null");
                else if (string.IsNullOrWhiteSpace(value))
                    problems.Add($"  {lang}.json / {key} —— 值为空白串 {JsonSerializer.Serialize(value)}");
            }
        }

        Assert.True(problems.Count == 0,
            $"以下键的值为 null 或空白（共 {problems.Count} 处）："
            + Environment.NewLine + string.Join(Environment.NewLine, problems));
    }
}
