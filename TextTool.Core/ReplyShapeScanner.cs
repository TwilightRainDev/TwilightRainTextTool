using System.Text.RegularExpressions;

namespace TextTool.Services;

/// <summary>
/// 一处「离开文件就看不懂」或「不该出现在文件里」的写法。Line 从 1 起，Token 是可直接回显的原始片段。
/// Blocks 表示命中时值得打断对话当场重写，取自规则表的同名开关。
/// </summary>
public sealed record ReplyShapeHit(string Id, string Title, string Token, int Line, bool Blocks);

/// <summary>
/// 回复里机器词与越界字符的定位：每条判据一行一处，围栏代码块整块不看。
/// 判据与正则都取自规则表（<see cref="RuleIds"/>）。按「出现即算」匹配，不看 Scope 与 MinCount。
/// </summary>
public sealed class ReplyShapeScanner
{
    /// <summary>
    /// 本扫描器消费的规则 Id。外部系统按这些 Id 认契约，改名等于改对方的调用；
    /// </summary>
    public static readonly IReadOnlyList<string> RuleIds =
        new[] { "agent-code-list", "agent-truncated-path", "emoji-and-symbols" };

    private readonly List<(string Id, string Title, bool Blocks, List<Regex> Patterns)> _shapes;

    public ReplyShapeScanner(IEnumerable<LintRule> rules)
    {
        var byId = rules.ToDictionary(r => r.Id, StringComparer.OrdinalIgnoreCase);
        _shapes = new List<(string, string, bool, List<Regex>)>();
        foreach (var id in RuleIds)
        {
            if (!byId.TryGetValue(id, out var rule))
                throw new InvalidOperationException($"规则表缺少 {id}，回复形状扫描依赖它");
            if (rule.Kind != "regex")
                throw new InvalidOperationException($"规则 {id} 应当是 regex，实际是 {rule.Kind}");
            // 多条模式是正常形状（装饰符号按码区分了五条），空表才是配置错
            if (rule.Patterns.Count == 0)
                throw new InvalidOperationException($"规则 {id} 没有模式");
            _shapes.Add((rule.Id, rule.Title, rule.BlocksReply,
                rule.Patterns.Select(p => RegexGuard.Create(p)).ToList()));
        }
    }

    /// <summary>规则表缺条目或写法不符时抛 InvalidOperationException：位置错在配置，不在调用方。</summary>
    public static ReplyShapeScanner FromStore() => new(LintRuleStore.Load());

    /// <summary>围栏线：整行只由三连反引号或三连波浪号开头，代码块的开与闭都靠它。</summary>
    private static readonly Regex FenceLine = RegexGuard.Create(@"^[ \t]*(?:```|~~~)");

    public IReadOnlyList<ReplyShapeHit> Scan(string text)
    {
        var hits = new List<ReplyShapeHit>();
        // 同一行同一个片段只报一次：一段正文里同一个写法复述两遍，读者的问题只有一个
        var seen = new HashSet<string>(StringComparer.Ordinal);
        bool inFence = false;
        int line = 0;
        foreach (var raw in text.Split('\n'))
        {
            line++;
            if (FenceLine.IsMatch(raw))
            {
                inFence = !inFence;
                continue;
            }
            if (inFence) continue;
            foreach (var (id, title, blocks, patterns) in _shapes)
                foreach (var pattern in patterns)
                    foreach (Match match in pattern.Matches(raw))
                    {
                        if (!seen.Add($"{id}\u0000{match.Value}\u0000{line}")) continue;
                        hits.Add(new ReplyShapeHit(id, title, match.Value, line, blocks));
                    }
        }
        return hits;
    }
}
