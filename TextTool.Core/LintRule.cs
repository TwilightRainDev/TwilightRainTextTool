using System.Reflection;
using System.Text;
using System.Text.Json;

namespace TextTool.Services;

/// <summary>
/// AI 味检查规则（数据驱动部分）。算法规则不进本模型，见 AiToneLintService。
/// </summary>
public class LintRule
{
    public string Id { get; set; } = "";
    public string Group { get; set; } = "";
    public string Title { get; set; } = "";
    /// <summary>literal 或 regex。</summary>
    public string Kind { get; set; } = "regex";
    /// <summary>多条模式——P1 有 2 条正则、L1 有 26 条词，单串装不下。</summary>
    public List<string> Patterns { get; set; } = new();
    /// <summary>document 或 paragraph。</summary>
    public string Scope { get; set; } = "document";
    /// <summary>仅 Scope=paragraph 有效：只检查段末 N 字。</summary>
    public int? TailChars { get; set; }
    /// <summary>每条 Pattern 各自的出现次数达到本值才报。</summary>
    public int MinCount { get; set; } = 1;
    /// <summary>info 或 warn。</summary>
    public string Severity { get; set; } = "info";
    public string Detail { get; set; } = "";
    /// <summary>与 Patterns 下标对齐的建议改法（可空）。</summary>
    public List<string>? Hints { get; set; }
    public string? SuggestScheme { get; set; }
}

/// <summary>
/// 规则持久化：内置嵌入式资源 + 外部 lint_rules.json 按 Id 合并。
/// </summary>
public static class LintRuleStore
{
    /// <summary>资源名前缀取 RootNamespace（TextTool），不是程序集名（TextTool.Core）。</summary>
    public const string EmbeddedResourceName = "TextTool.default_lint_rules.json";

    public static List<LintRule> Load()
    {
        var builtIn = GetDefaultRules();
        var external = JsonFileStore.Load<LintRule>("lint_rules.json");
        // 外部先校验：重复 Id 必须在这里得到清晰报错。
        // 若留到 Merge 之后再校验，重复 Id 会在 Merge 内部因索引指向已被替换的对象
        // 而先抛 ArgumentOutOfRangeException，Validate 的报错永远轮不到。
        Validate(external);
        var merged = Merge(builtIn, external);
        Validate(merged);
        return merged;
    }

    public static List<LintRule> GetDefaultRules()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(EmbeddedResourceName)
            ?? throw new InvalidOperationException($"缺少嵌入式资源 {EmbeddedResourceName}，请检查 csproj 的 EmbeddedResource 配置");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return JsonSerializer.Deserialize<List<LintRule>>(reader.ReadToEnd()) ?? new List<LintRule>();
    }

    /// <summary>外部规则按 Id 覆盖内置（覆盖后仍排原位置），新 Id 追加在末尾。</summary>
    internal static List<LintRule> Merge(List<LintRule> builtIn, List<LintRule> external)
    {
        var result = builtIn.Select(Clone).ToList();
        var index = result.ToDictionary(r => r.Id, StringComparer.Ordinal);
        foreach (var rule in external)
        {
            if (index.TryGetValue(rule.Id, out var existing))
                result[result.IndexOf(existing)] = rule;
            else
            {
                result.Add(rule);
                index[rule.Id] = rule;
            }
        }
        return result;
    }

    internal static void Validate(List<LintRule> rules)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Id))
                throw new ArgumentException("规则 Id 不能为空");
            if (!seen.Add(rule.Id))
                throw new ArgumentException($"规则 Id 重复：{rule.Id}");
            if (rule.Kind is not ("literal" or "regex"))
                throw new ArgumentException($"规则 {rule.Id} 的 Kind 非法：{rule.Kind}");
            if (rule.Scope is not ("document" or "paragraph"))
                throw new ArgumentException($"规则 {rule.Id} 的 Scope 非法：{rule.Scope}");
            if (rule.Severity is not ("info" or "warn"))
                throw new ArgumentException($"规则 {rule.Id} 的 Severity 非法：{rule.Severity}");
            if (rule.Patterns.Count == 0)
                throw new ArgumentException($"规则 {rule.Id} 的 Patterns 为空");
            if (rule.MinCount < 1)
                throw new ArgumentException($"规则 {rule.Id} 的 MinCount 必须 >= 1");
            if (rule.TailChars is <= 0)
                throw new ArgumentException($"规则 {rule.Id} 的 TailChars 必须为正整数");
            if (rule.TailChars is not null && rule.Scope != "paragraph")
                throw new ArgumentException($"规则 {rule.Id} 的 TailChars 仅在 Scope=paragraph 时有效");
            if (rule.Hints is not null && rule.Hints.Count != rule.Patterns.Count)
                throw new ArgumentException($"规则 {rule.Id} 的 Hints 长度与 Patterns 不一致");
            if (rule.Kind == "regex")
                foreach (var pattern in rule.Patterns)
                    try { RegexGuard.Create(pattern); }
                    catch (ArgumentException ex)
                    {
                        throw new ArgumentException($"规则 {rule.Id} 的正则非法：{pattern}（{ex.Message}）", ex);
                    }
        }
    }

    private static LintRule Clone(LintRule r) => new()
    {
        Id = r.Id, Group = r.Group, Title = r.Title, Kind = r.Kind,
        Patterns = new List<string>(r.Patterns), Scope = r.Scope,
        TailChars = r.TailChars, MinCount = r.MinCount, Severity = r.Severity,
        Detail = r.Detail, Hints = r.Hints is null ? null : new List<string>(r.Hints),
        SuggestScheme = r.SuggestScheme,
    };
}
