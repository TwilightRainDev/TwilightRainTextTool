using System.Text;

namespace TextTool.Services;

/// <summary>
/// 人读报告渲染。不可见字符必须转义：Windows 控制台 GBK 码页编码不出 U+200B，
/// 直接打印会抛异常，报隐形字符等于不报。
/// </summary>
public static class LintTextFormatter
{
    internal static readonly HashSet<char> Escaped = new()
    {
        '\u200B', '\u200C', '\u200D', '\u200E', '\u200F', '\u2060', '\u00AD', '\u00A0', '\uFEFF',
    };

    /// <summary>
    /// 只替换隐形字符集本身（不碰 JSON 自身的换行等控制字符），供 --json 路径复用：
    /// JSON 文本里出现转义字面量，任何解析方都会把它解回原字符，语义不变。
    /// </summary>
    internal static string EscapeInvisible(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
            sb.Append(Escaped.Contains(c) ? $"\\u{(int)c:x4}" : c);
        return sb.ToString();
    }

    /// <summary>折叠只在处数够多时才有意义：两条命中也折叠，读者连样例都看不出来。</summary>
    internal const int CollapseMinHits = 10;

    /// <summary>折叠后列几条例样。</summary>
    internal const int CollapseSamples = 3;

    /// <summary>每千字处数，取一位小数只是为了显示，判定用原值。</summary>
    internal static double Density(int hits, int chars) => chars <= 0 ? hits : hits * 1000.0 / chars;

    /// <summary>
    /// 该不该折叠：规则表里开了折叠密度、处数不少于 CollapseMinHits、密度达到阈值。
    /// 只看处数不看密度会让长篇必然折叠，只看密度不看处数会让短文本的两处也折叠。
    /// </summary>
    internal static bool ShouldCollapse(LintReport report, string id, int hits)
    {
        if (!report.CollapseAtDensity.TryGetValue(id, out int threshold)) return false;
        return hits >= CollapseMinHits && Density(hits, report.Chars) >= threshold;
    }

    /// <summary>
    /// 渲染人读报告。text 为 null 时用中文原文，命令行走这条；
    /// 界面传入按当前语言取词的实现。
    /// </summary>
    public static string Format(LintReport report, ILintText? text = null)
    {
        ILintText t = text ?? ChineseLintText.Instance;
        var sb = new StringBuilder();

        if (report.Hits.Count > 0)
        {
            foreach (var group in report.Hits.GroupBy(h => $"{h.Id} {h.Title}"))
            {
                // 组内按 (行, 列) 升序：命中在引擎里的顺序是 规则→Pattern→位置，
                // 同一规则的多条 Pattern 会让行号来回跳，人读报告需要单调
                var ordered = group.OrderBy(h => h.Line).ThenBy(h => h.Col).ToList();
                bool collapsed = ShouldCollapse(report, ordered[0].Id, ordered.Count);
                double density = Density(ordered.Count, report.Chars);
                sb.AppendLine(collapsed
                    ? t.Get("LintGroupHeaderDense",
                        "[命中] {0}（{1} 处，每千字 {2} 处）：同一模板反复，只列样例",
                        group.Key, ordered.Count, density.ToString("0.0"))
                    : t.Get("LintGroupHeader", "[命中] {0}（{1} 处）", group.Key, ordered.Count));
                foreach (var hit in collapsed ? ordered.Take(CollapseSamples) : ordered)
                {
                    string hint = string.IsNullOrEmpty(hit.Hint) ? "" : $"  <- {hit.Hint}";
                    string detail = string.IsNullOrEmpty(hit.Detail) ? "" : $"  <- {hit.Detail}";
                    string scheme = string.IsNullOrEmpty(hit.SuggestScheme)
                        ? ""
                        : $"  <- {t.Get("LintSchemeTag", "方案：{0}", hit.SuggestScheme)}";
                    string where = t.Get("LintLocation", "第 {0} 行 第 {1} 列", hit.Line, hit.Col);
                    sb.AppendLine($"       {where}  {Visible(hit.Snippet)}{hint}{detail}{scheme}");
                }
                if (collapsed)
                    sb.AppendLine("       " + t.Get(
                        "LintCollapsedRest", "其余 {0} 处见 --json（位置一条不少）", ordered.Count - CollapseSamples));
            }
        }
        else
        {
            sb.AppendLine(t.Get("LintNoHits", "[OK] 未发现可疑特征"));
        }

        if (report.Notes.Count > 0)
        {
            sb.AppendLine(t.Get("LintNotesHeader", "[统计]"));
            foreach (var note in report.Notes)
                sb.AppendLine($"       - {note.Id} {note.Text}");
        }

        sb.Append(t.Get(
            "LintSummary",
            "[NOTE] 全文 {0} 字，命中 {1} 处，统计项 {2} 条，段落口径 {3}；本命令只报位置，改写由判断层完成",
            report.Chars, report.Hits.Count, report.Notes.Count, report.ParagraphMode));
        return sb.ToString();
    }

    public static string Visible(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
            sb.Append(Escaped.Contains(c) || c < 0x20 ? $"\\u{(int)c:x4}" : c);
        return sb.ToString();
    }
}
