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

    public static string Format(LintReport report)
    {
        var sb = new StringBuilder();

        if (report.Hits.Count > 0)
        {
            foreach (var group in report.Hits.GroupBy(h => $"{h.Id} {h.Title}"))
            {
                sb.AppendLine($"[命中] {group.Key}（{group.Count()} 处）");
                // 组内按 (行, 列) 升序：命中在引擎里的顺序是 规则→Pattern→位置，
                // 同一规则的多条 Pattern 会让行号来回跳，人读报告需要单调
                foreach (var hit in group.OrderBy(h => h.Line).ThenBy(h => h.Col))
                {
                    string hint = string.IsNullOrEmpty(hit.Hint) ? "" : $"  <- {hit.Hint}";
                    string detail = string.IsNullOrEmpty(hit.Detail) ? "" : $"  <- {hit.Detail}";
                    string scheme = string.IsNullOrEmpty(hit.SuggestScheme) ? "" : $"  <- 方案：{hit.SuggestScheme}";
                    sb.AppendLine($"       第 {hit.Line} 行 第 {hit.Col} 列  {Visible(hit.Snippet)}{hint}{detail}{scheme}");
                }
            }
        }
        else
        {
            sb.AppendLine("[OK] 未发现可疑特征");
        }

        if (report.Notes.Count > 0)
        {
            sb.AppendLine("[统计]");
            foreach (var note in report.Notes)
                sb.AppendLine($"       - {note.Id} {note.Text}");
        }

        sb.Append($"[NOTE] 全文 {report.Chars} 字，命中 {report.Hits.Count} 处，" +
                  $"统计项 {report.Notes.Count} 条，段落口径 {report.ParagraphMode}；" +
                  "本命令只报位置，改写由判断层完成");
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
