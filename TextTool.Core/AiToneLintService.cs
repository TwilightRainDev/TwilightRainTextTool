using System.Text.RegularExpressions;

namespace TextTool.Services;

/// <summary>
/// 中文 AI 味检查引擎：只报位，不改写。
/// 数据规则来自 LintRuleStore；统计类算法规则在本类内。
/// </summary>
public sealed class AiToneLintService
{
    private readonly List<CompiledRule> _rules;

    public AiToneLintService(IEnumerable<LintRule> rules)
        => _rules = rules.Select(CompiledRule.Create).ToList();

    public LintReport Scan(string text, string fileName)
    {
        var report = new LintReport { File = fileName, Chars = text.Length };

        foreach (var rule in _rules)
        {
            if (rule.Rule.Scope == "paragraph")
                ScanParagraphScope(rule, text, report);
            else
                ScanDocumentScope(rule, text, report);
        }

        return report;
    }

    private static void ScanDocumentScope(CompiledRule rule, string text, LintReport report)
    {
        foreach (var (regex, hint) in rule.Patterns)
        {
            var matches = regex.Matches(text);
            if (matches.Count < rule.Rule.MinCount) continue;
            foreach (Match m in matches)
                report.Hits.Add(Hit(rule.Rule, text, m.Index, m.Length, m.Value, hint));
        }
    }

    private static void ScanParagraphScope(CompiledRule rule, string text, LintReport report)
        => throw new NotImplementedException("任务 6 实现");

    internal static LintHit Hit(LintRule rule, string text, int index, int length, string match, string? hint)
    {
        var (line, col) = Position(text, index);
        return new LintHit
        {
            Id = rule.Id, Group = rule.Group, Title = rule.Title, Severity = rule.Severity,
            Line = line, Col = col, Length = length, Match = match,
            Snippet = Snippet(text, index, length),
            Detail = rule.Detail, Hint = hint, SuggestScheme = rule.SuggestScheme,
        };
    }

    /// <summary>全文偏移量换算行列；逐字符累计而非查找首次出现（重复段落会定位错）。</summary>
    internal static (int Line, int Col) Position(string text, int index)
    {
        int line = 1, lineStart = 0;
        for (int i = 0; i < index && i < text.Length; i++)
            if (text[i] == '\n') { line++; lineStart = i + 1; }
        return (line, index - lineStart + 1);
    }

    /// <summary>
    /// 前后各 8 字的上下文，换行去除。窗口边界不劈开代理对——
    /// P8 会命中 emoji，命中点附近的窗口若不外扩，切片会产出落单代理项。
    /// </summary>
    internal static string Snippet(string text, int index, int length, int pad = 8)
    {
        int lo = Math.Max(0, index - pad);
        int hi = Math.Min(text.Length, index + length + pad);
        if (lo > 0 && char.IsLowSurrogate(text[lo])) lo--;          // 左边界落在低代理上：回退一格
        if (hi < text.Length && char.IsHighSurrogate(text[hi - 1])) hi++;   // 右边界停在高代理后：前进一格
        var s = text[lo..hi].Replace("\r", "").Replace("\n", "");
        if (lo > 0) s = "…" + s;
        if (hi < text.Length) s += "…";
        return s;
    }

    private sealed class CompiledRule
    {
        public required LintRule Rule { get; init; }
        public required List<(Regex Regex, string? Hint)> Patterns { get; init; }

        public static CompiledRule Create(LintRule rule) => new()
        {
            Rule = rule,
            Patterns = rule.Patterns.Select((p, i) =>
            {
                // literal 经 Regex.Escape 走同一条匹配路径；regex 原文经 RegexGuard 构造
                var regex = rule.Kind == "literal"
                    ? RegexGuard.Create(Regex.Escape(p))
                    : RegexGuard.Create(p);
                string? hint = rule.Hints is not null && i < rule.Hints.Count ? rule.Hints[i] : null;
                return (regex, hint);
            }).ToList(),
        };
    }
}
