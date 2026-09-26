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

        ScanAlgorithmRules(text, report);
        return report;
    }

    /// <summary>代码内算法规则的 Id 全集（数据规则之外的 P4/P5 与 C1-C5）。</summary>
    public static readonly IReadOnlyList<string> AlgorithmRuleIds =
        new[] { "P4", "P5", "C1", "C2", "C3", "C4", "C5" };

    /// <summary>算法规则与数据规则的 Id 并集，即引擎实际可能产出的 Id 全集（--only 的校验集）。</summary>
    public static IReadOnlyCollection<string> AllRuleIds()
    {
        var ids = new List<string>(AlgorithmRuleIds);
        foreach (var rule in LintRuleStore.Load())
            if (!ids.Contains(rule.Id)) ids.Add(rule.Id);
        return ids;
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
    {
        var paragraphs = Paragraphs(text);
        foreach (var (regex, hint) in rule.Patterns)
        {
            // MinCount 按段判定：段内序数词必须是同一段里凑够次数才算骨架，
            // 分散在多段的单次出现合计到达阈值不算（任务名与用例的「段内」口径）。
            foreach (var p in paragraphs)
            {
                int windowStart = rule.Rule.TailChars is int tail && p.Text.Length > tail
                    ? p.Text.Length - tail
                    : 0;
                var matches = regex.Matches(p.Text, windowStart);
                if (matches.Count < rule.Rule.MinCount) continue;
                foreach (Match m in matches)
                    report.Hits.Add(Hit(rule.Rule, text, p.Offset + m.Index, m.Length, m.Value, hint));
            }
        }
    }

    private static readonly (string Name, Regex Pattern)[] QuoteStyles =
    {
        ("ASCII 直引号", RegexGuard.Create("\"")),
        ("弯引号", RegexGuard.Create("[“”]")),
        ("直角引号", RegexGuard.Create("[「」]")),
        ("双直角引号", RegexGuard.Create("[『』]")),
        ("ASCII 直单引号", RegexGuard.Create("'")),
        ("弯单引号", RegexGuard.Create("[‘’]")),
    };

    private static readonly Regex OrdinalSkeleton = RegexGuard.Create("首先|其次|再次|最后|其一|其二|其三");

    private static void ScanAlgorithmRules(string text, LintReport report)
    {
        // P4 引号风格混用：命中定位到全文最早出现的引号字符，规模写进 Detail
        var used = QuoteStyles
            .Select(s => (s.Name, Count: s.Pattern.Matches(text).Count, Index: s.Pattern.Match(text).Index))
            .Where(s => s.Count > 0)
            .ToList();
        if (used.Count > 1)
        {
            int first = used.Min(s => s.Index);
            string detail = string.Join(" / ", used.Select(s => $"{s.Name} {s.Count} 个"));
            report.Hits.Add(new LintHit
            {
                Id = "P4", Group = "P", Title = "引号风格混用", Severity = "info",
                Line = Position(text, first).Line, Col = Position(text, first).Col, Length = 1,
                Match = text[first].ToString(), Snippet = Snippet(text, first, 1),
                Detail = detail, SuggestScheme = "引号括号统一",
            });
        }

        // P5 括号全半角混用
        int half = RegexGuard.Create("[()]").Matches(text).Count;
        int full = RegexGuard.Create("[（）]").Matches(text).Count;
        if (half > 0 && full > 0)
        {
            var m = RegexGuard.Create("[()（）]").Match(text);
            report.Hits.Add(new LintHit
            {
                Id = "P5", Group = "P", Title = "括号全半角混用", Severity = "info",
                Line = Position(text, m.Index).Line, Col = Position(text, m.Index).Col, Length = 1,
                Match = m.Value, Snippet = Snippet(text, m.Index, 1),
                Detail = $"半角 {half} 个 / 全角 {full} 个", SuggestScheme = "引号括号统一",
            });
        }

        var paragraphs = Paragraphs(text);

        // C1 段落长度过于均一
        if (paragraphs.Count >= 5)
        {
            var lens = paragraphs.Select(p => (double)p.Text.Length).ToList();
            double mean = lens.Average(), cv = StdDev(lens, mean) / mean;
            if (cv < 0.25)
                report.Notes.Add(new LintNote
                {
                    Id = "C1",
                    Text = $"段落长度过于均一（变异系数 {cv:0.00}，{paragraphs.Count} 段）——人写文本段落长短通常更参差",
                });
        }

        // C2 句长分布过于整齐
        var sentences = RegexGuard.Create("(?<=[。！？…])").Split(text)
            .Select(s => s.Trim()).Where(s => s.Length >= 6).ToList();
        if (sentences.Count >= 10)
        {
            var lens = sentences.Select(s => (double)s.Length).ToList();
            double mean = lens.Average(), cv = StdDev(lens, mean) / mean;
            if (cv < 0.35)
                report.Notes.Add(new LintNote
                {
                    Id = "C2",
                    Text = $"句长分布过于整齐（变异系数 {cv:0.00}，{sentences.Count} 句）",
                });
        }

        // C3 段首两字高度重复
        if (paragraphs.Count >= 4)
        {
            var dup = paragraphs.Select(p => p.Text[..Math.Min(2, p.Text.Length)])
                .GroupBy(h => h).Where(g => g.Count() >= 3).ToList();
            if (dup.Count > 0)
                report.Notes.Add(new LintNote
                {
                    Id = "C3",
                    Text = "段首两字高度重复：" + string.Join("、", dup.Select(g => $"「{g.Key}」×{g.Count()}")),
                });
        }

        // C4 叹号密度偏高
        int exclam = text.Count(c => c == '！');
        if (text.Length >= 400 && exclam * 1000.0 / text.Length > 3)
            report.Notes.Add(new LintNote { Id = "C4", Text = $"叹号密度偏高：{exclam} 个 / {text.Length} 字" });

        // C5 序数词骨架密度
        int ordinals = OrdinalSkeleton.Matches(text).Count;
        if (ordinals >= 2)
            report.Notes.Add(new LintNote
            {
                Id = "C5",
                Text = $"序数词骨架「首先/其次/最后」出现 {ordinals} 次——结构化排版的典型痕迹",
            });
    }

    private static double StdDev(List<double> values, double mean)
    {
        double sum = values.Sum(v => (v - mean) * (v - mean));
        return Math.Sqrt(sum / values.Count);
    }

    /// <summary>
    /// 非空行即一段（与技能侧 tone_lint 的段落口径一致，Markdown 软换行会被拆开——已知简化）。
    /// Offset 为去空白后段首在全文中的绝对下标。
    /// </summary>
    internal static List<(int Offset, string Text)> Paragraphs(string text)
    {
        var result = new List<(int, string)>();
        int offset = 0;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var trimmed = line.Trim();
            if (trimmed.Length > 0)
                result.Add((offset + (line.Length - line.TrimStart().Length), trimmed));
            offset += raw.Length + 1;
        }
        return result;
    }

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
