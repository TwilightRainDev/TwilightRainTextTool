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
    internal const string EmbeddedResourceName = "TextTool.default_lint_rules.json";

    public static List<LintRule> Load()
    {
        var builtIn = GetDefaultRules();
        var external = JsonFileStore.Load<LintRule>("lint_rules.json");
        // 两侧各自先校验，再合并，再校验合并结果。四个边界各有理由：
        // 算法撞码——external 的 Id 撞上代码产出的算法 Id 时先挡下来；
        // 内置侧——Merge 会 Clone，Patterns 为 null 时 Clone 先 NRE；
        // 外部侧——重复 Id 必须在这里得到清晰报错，否则 Merge 按「后者覆盖前者」
        //   静默取最后一条，用户无从知道自己的配置里写了两个同 Id 规则。
        ValidateNoAlgorithmIdCollision(external);
        Validate(builtIn);
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
            {
                // 索引必须跟着替换走：否则同 Id 第二次进来时 existing 已不在列表里，
                // IndexOf 返回 -1，result[-1] 抛 ArgumentOutOfRangeException
                int at = result.IndexOf(existing);
                result[at] = Clone(rule);
                index[rule.Id] = result[at];
            }
            else
            {
                // 外部侧同样 Clone：合并结果与调用方的对象脱钩，两侧对称
                result.Add(Clone(rule));
                index[rule.Id] = result[^1];
            }
        }
        return result;
    }

    /// <summary>
    /// 算法规则的 Id 由代码产出（见 AiToneLintService.AlgorithmRuleIds）。外部文件撞码会让同一个 Id 既有算法命中又有数据命中，
    /// 报告出现重复 Id 且 --only 无法区分来源——加载即校验，不留到运行期。
    /// </summary>
    internal static void ValidateNoAlgorithmIdCollision(List<LintRule> external)
    {
        foreach (var rule in external)
            if (rule is not null && AiToneLintService.AlgorithmRuleIds.Contains(rule.Id, StringComparer.OrdinalIgnoreCase))
                throw new ArgumentException($"外部规则的 Id 与算法规则冲突：{rule.Id}");
    }

    internal static void Validate(List<LintRule> rules)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in rules)
        {
            if (rule is null)
                throw new ArgumentException("规则列表含 null 元素");
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
            // 显式 null 会被 System.Text.Json 写回属性，模型上的 = new() 默认值挡不住，
            // 不在这里拦就会变成 NullReferenceException 而非清晰的校验报错
            if (rule.Patterns is null || rule.Patterns.Count == 0)
                throw new ArgumentException($"规则 {rule.Id} 的 Patterns 为空");
            if (rule.Patterns.Any(string.IsNullOrEmpty))
                throw new ArgumentException($"规则 {rule.Id} 的 Patterns 含空串");
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
