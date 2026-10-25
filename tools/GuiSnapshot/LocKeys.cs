using System.Text.RegularExpressions;

namespace TextTool.Tools.GuiSnapshot;

/// <summary>跳过的 Loc.T 调用：首参不是字面量，静态分析拿不到键名。</summary>
internal sealed record SkippedCall(string Where, string Reason);

/// <summary>一次代码扫描的结果：键名表、扫过的文件、注释里出现的调用数、被跳过的调用。</summary>
internal sealed record LocScanResult(
    SortedDictionary<string, string> Keys,
    List<string> Files,
    int CommentMentions,
    List<SkippedCall> Skipped);

/// <summary>
/// 从 TextTool.Gui 的 .cs 代码里收集 Loc.T("键名") 的字面量键名。
///
/// 为什么也要扫代码：页签（如 MergeTab）的控件文字是在 axaml.cs 里用 Loc.T 赋的，
/// axaml 里根本没有这些键。漏了它们，缺键在界面上显示成 [键名]，体检却还是绿的。
///
/// 为什么不用正则一把梭：Loc.T( 可能出现在行注释、块注释、字符串里，正则分不清；
/// 首参也可能是 $"..." 插值串或变量，那些求不出键名，只能跳过。所以走一个带状态的
/// 字符扫描：注释整段跳过，字符串只跳「字面文本」，插值孔 {} 里是真代码，照样扫；
/// 只在代码位置上认 Loc.T 调用，再按字面量规则读首个参数。不做表达式求值，
/// 读不出字面量就记成跳过并打印出来，不让它静默消失。
/// </summary>
internal static class LocKeys
{
    /// <summary>注释里出现 Loc.T(...) 的次数，只用于报告"看见了但没算"。</summary>
    private static readonly Regex CallLikePattern =
        new(@"\bLoc\s*\.\s*T\s*\(", RegexOptions.Compiled);

    /// <summary>
    /// 扫 .cs 源码。返回 键名 -> 首次引用位置（相对 TextTool.Gui 的 文件:行号）。
    /// bin 和 obj 下没有源码，但仍按目录名排除，免得哪天有个 obj 里的生成文件混进来。
    /// </summary>
    public static LocScanResult Scan(string guiDir)
    {
        var files = Directory.EnumerateFiles(guiDir, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        var keys = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var skipped = new List<SkippedCall>();
        int commentMentions = 0;

        foreach (string file in files)
        {
            string text = File.ReadAllText(file);
            string where = Path.GetRelativePath(guiDir, file);
            ScanRange(text, 0, text.Length, where, keys, skipped, ref commentMentions);
        }

        return new LocScanResult(
            keys,
            files.Select(file => Path.GetRelativePath(guiDir, file)).ToList(),
            commentMentions,
            skipped);
    }

    /// <summary>
    /// 扫 [start, end) 这一段，当成代码来读。整文件是 [0, 长度)，插值孔里则是孔内那一段。
    /// 用区间而不是子串，是为了行号直接按全文件算。
    /// </summary>
    private static void ScanRange(
        string text,
        int start,
        int end,
        string where,
        SortedDictionary<string, string> keys,
        List<SkippedCall> skipped,
        ref int commentMentions)
    {
        int i = start;
        while (i < end)
        {
            char c = text[i];

            // 行注释：到行尾为止，里面的 Loc.T 不算。
            if (c == '/' && i + 1 < end && text[i + 1] == '/')
            {
                int lineEnd = text.IndexOf('\n', i);
                int stop = lineEnd < 0 || lineEnd > end ? end : lineEnd;
                commentMentions += CountCalls(text, i, stop);
                i = stop;
                continue;
            }

            // 块注释：到 */ 为止，跨行也照跳。没闭合就到段尾，不猜。
            if (c == '/' && i + 1 < end && text[i + 1] == '*')
            {
                int close = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                int stop = close < 0 || close + 2 > end ? end : close + 2;
                commentMentions += CountCalls(text, i, stop);
                i = stop;
                continue;
            }

            // 字符字面量：'a'、'\''、'\n' 之类，跳过，免得里面的引号被当成字符串开头。
            if (c == '\'')
            {
                i = Math.Min(SkipCharLiteral(text, i), end);
                continue;
            }

            // 字符串：字面文本跳过，插值孔里的代码继续扫。
            // @ 前缀可能是 @"..." 也可能是 @class 这种转义标识符，认不出字符串就不动。
            if (c is '"' or '@' or '$' &&
                TryReadStringLiteral(text, i, out _, out int stringEnd, out bool interpolated, out int bodyStart, out int bodyEnd))
            {
                if (interpolated)
                    ScanHoles(text, bodyStart, Math.Min(bodyEnd, end), where, keys, skipped, ref commentMentions);

                i = stringEnd;
                continue;
            }

            // 认 "Loc" 这个词（前后不能粘着标识符字符），再往下读 .T( 和第一个参数。
            if (c == 'L' &&
                IsTokenStart(text, i) &&
                string.CompareOrdinal(text, i, "Loc", 0, 3) == 0 &&
                TryReadCall(text, i, out int next, out string? key, out string? skipReason))
            {
                if (key is not null)
                {
                    if (!keys.ContainsKey(key))
                        keys[key] = $"{where}:{LineOf(text, i)}";
                }
                else
                {
                    skipped.Add(new SkippedCall($"{where}:{LineOf(text, i)}", skipReason!));
                }

                i = next;
                continue;
            }

            i++;
        }
    }

    /// <summary>
    /// 扫插值字符串里的孔 {}：孔里是真代码，`$"{Loc.T("键名")}..."` 这种写法不扫就漏了。
    /// 只负责切出孔的区间，孔内的注释、嵌套字符串一律交给 ScanRange 递归处理。
    /// {{ 和 }} 是转义的字面大括号，不是孔。
    /// </summary>
    private static void ScanHoles(
        string text,
        int start,
        int end,
        string where,
        SortedDictionary<string, string> keys,
        List<SkippedCall> skipped,
        ref int commentMentions)
    {
        int p = start;
        while (p < end)
        {
            if (text[p] == '{')
            {
                if (p + 1 < end && text[p + 1] == '{')
                {
                    p += 2;
                    continue;
                }

                // 找配对的 }：孔里可能出现嵌套字符串（其中的大括号不算深度）和注释。
                int depth = 1;
                int q = p + 1;
                while (q < end && depth > 0)
                {
                    char d = text[q];

                    if (d == '\'')
                    {
                        q = Math.Min(SkipCharLiteral(text, q), end);
                        continue;
                    }

                    if (d is '"' or '@' or '$' &&
                        TryReadStringLiteral(text, q, out _, out int nestedEnd, out _, out _, out _))
                    {
                        q = nestedEnd;
                        continue;
                    }

                    if (d == '/' && q + 1 < end && text[q + 1] == '/')
                    {
                        int lineEnd = text.IndexOf('\n', q);
                        q = lineEnd < 0 || lineEnd > end ? end : lineEnd;
                        continue;
                    }

                    if (d == '/' && q + 1 < end && text[q + 1] == '*')
                    {
                        int close = text.IndexOf("*/", q + 2, StringComparison.Ordinal);
                        q = close < 0 || close + 2 > end ? end : close + 2;
                        continue;
                    }

                    if (d == '{') depth++;
                    else if (d == '}') depth--;
                    q++;
                }

                int holeEnd = depth == 0 ? q - 1 : end;
                ScanRange(text, p + 1, Math.Max(p + 1, holeEnd), where, keys, skipped, ref commentMentions);
                p = q;
                continue;
            }

            if (text[p] == '}' && p + 1 < end && text[p + 1] == '}')
            {
                p += 2;
                continue;
            }

            p++;
        }
    }

    /// <summary>
    /// 从 "Loc" 的 L 开始读一个调用。返回 true 表示确实是一处 Loc.T(...) 调用：
    /// key 非空是读到了字面量键名，key 为空则 skipReason 说明为什么跳过。
    /// 返回 false 表示根本不是调用（Loc.X、LocT、Loc.ToString 之类），调用方继续逐字扫。
    /// </summary>
    private static bool TryReadCall(
        string text,
        int start,
        out int next,
        out string? key,
        out string? skipReason)
    {
        next = start;
        key = null;
        skipReason = null;

        int p = start + 3;                                         // 跳过 "Loc"
        if (p < text.Length && IsIdentChar(text[p])) return false;  // LocXxx
        if (!TryConsume(text, ref p, '.')) return false;
        if (!TryConsume(text, ref p, 'T')) return false;
        if (p < text.Length && IsIdentChar(text[p])) return false;  // Loc.ToString
        if (!TryConsume(text, ref p, '(')) return false;
        if (p >= text.Length) return false;

        // 第一个参数是字符串字面量才谈得上知道键名；其余一律记跳过，不猜值。
        if (TryReadStringLiteral(text, p, out string content, out int after, out bool interpolated, out _, out _))
        {
            next = after;

            if (interpolated)
            {
                skipReason = "首参是插值字符串，静态读不出键名";
                return true;
            }

            // 字面量后面必须紧跟 , 或 )，否则是 "键" + 变量 这类拼接，键名不完整。
            int q = after;
            SkipTrivia(text, ref q);
            if (q >= text.Length || (text[q] != ',' && text[q] != ')'))
            {
                skipReason = "首参是拼接表达式，键名不完整";
                return true;
            }

            if (content.Length == 0)
            {
                skipReason = "首参是空字符串";
                return true;
            }

            key = content;
            next = q;
            return true;
        }

        skipReason = "首参不是字面量（变量或表达式）";
        next = p;
        return true;
    }

    /// <summary>
    /// 读一个字符串字面量，支持 "..."、@"..."、$"..."、$@"..."、@$"..."、"""..."""。
    /// content 是按字面量语法还原的文本（verbatim 的 "" 还原成 "）；interpolated 表示带 $；
    /// bodyStart/bodyEnd 是引号之间的内容区间（插值孔就在这个区间里找）。
    /// 返回 false 表示这个位置不是字符串（例如 @class 这种转义标识符）。
    /// </summary>
    private static bool TryReadStringLiteral(
        string text,
        int start,
        out string content,
        out int next,
        out bool interpolated,
        out int bodyStart,
        out int bodyEnd)
    {
        content = string.Empty;
        next = start;
        interpolated = false;
        bodyStart = start;
        bodyEnd = start;

        int p = start;
        bool verbatim = false;

        // $ 和 @ 前缀，顺序不限（$@"..." 和 @$"..." 都合法），最多各一个。
        while (p < text.Length && (text[p] == '$' || text[p] == '@'))
        {
            if (text[p] == '$')
            {
                if (interpolated) return false;
                interpolated = true;
            }
            else
            {
                if (verbatim) return false;
                verbatim = true;
            }

            p++;
        }

        if (p >= text.Length || text[p] != '"') return false;

        // 原始字符串 """ ... """：闭合是同样个数的引号。键不会是这种写法，读到就整段跳过。
        int quotes = 0;
        while (p + quotes < text.Length && text[p + quotes] == '"') quotes++;
        if (quotes >= 3)
        {
            string fence = new('"', quotes);
            int close = text.IndexOf(fence, p + quotes, StringComparison.Ordinal);
            int fenceEnd = close < 0 ? text.Length : close;
            content = text[(p + quotes)..fenceEnd];
            bodyStart = p + quotes;
            bodyEnd = fenceEnd;
            next = close < 0 ? text.Length : close + quotes;
            return true;
        }

        p++;                                   // 跳过开头的 "
        int begin = p;

        if (verbatim)
        {
            // verbatim 里只有 "" 是转义，反斜杠是普通字符，可以跨行。
            while (p < text.Length)
            {
                if (text[p] == '"')
                {
                    if (p + 1 < text.Length && text[p + 1] == '"')
                    {
                        p += 2;
                        continue;
                    }

                    content = text[begin..p].Replace("\"\"", "\"");
                    bodyStart = begin;
                    bodyEnd = p;
                    next = p + 1;
                    return true;
                }

                p++;
            }

            content = text[begin..];           // 没闭合：吃到文件尾，交给调用方当异常处理
            bodyStart = begin;
            bodyEnd = text.Length;
            next = text.Length;
            return true;
        }

        while (p < text.Length)
        {
            if (text[p] == '\\' && p + 1 < text.Length)
            {
                p += 2;                        // 转义序列整对跳，\" 不会提前收尾
                continue;
            }

            if (text[p] == '"')
            {
                content = text[begin..p];
                bodyStart = begin;
                bodyEnd = p;
                next = p + 1;
                return true;
            }

            if (text[p] == '\n')
                break;                         // 普通字符串不跨行，没闭合就到此为止

            p++;
        }

        content = text[begin..p];
        bodyStart = begin;
        bodyEnd = p;
        next = p;
        return true;
    }

    /// <summary>跳过字符字面量，处理 '\'' 和 '\\' 这两类反斜杠转义。</summary>
    private static int SkipCharLiteral(string text, int start)
    {
        int p = start + 1;
        if (p < text.Length && text[p] == '\\') p += 2;
        else if (p < text.Length) p++;

        while (p < text.Length && text[p] != '\'' && text[p] != '\n') p++;
        return p < text.Length && text[p] == '\'' ? p + 1 : p;
    }

    private static bool TryConsume(string text, ref int p, char expected)
    {
        SkipTrivia(text, ref p);
        if (p >= text.Length || text[p] != expected) return false;
        p++;
        return true;
    }

    private static void SkipTrivia(string text, ref int p)
    {
        while (p < text.Length && char.IsWhiteSpace(text[p])) p++;
    }

    /// <summary>数一个片段里出现几次 Loc.T(，用于报告注释中被排除的调用。</summary>
    private static int CountCalls(string text, int start, int end) =>
        CallLikePattern.Matches(text[start..end]).Count;

    private static int LineOf(string text, int index)
    {
        int line = 1;
        for (int i = 0; i < index && i < text.Length; i++)
            if (text[i] == '\n') line++;

        return line;
    }

    private static bool IsIdentChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static bool IsTokenStart(string text, int index) =>
        index == 0 || !IsIdentChar(text[index - 1]);

    private static bool IsBuildOutput(string path) =>
        path.Split('/', '\\').Any(segment =>
            segment.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("obj", StringComparison.OrdinalIgnoreCase));
}
