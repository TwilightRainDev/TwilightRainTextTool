using System.Text.RegularExpressions;

namespace TextTool.Tests.Services;

/// <summary>
/// 一处能静态确定键名的 Loc.T 调用。ArgCount 是键名之后的实参个数（不含键名本身）。
/// </summary>
internal sealed record LocCallSite(string File, int Line, string Key, int ArgCount, string Snippet)
{
    public string Where => $"{File}:{Line}";
}

/// <summary>一处 Loc.T 调用，但键名不是字面量，静态分析读不出键名，只能跳过并报出来。</summary>
internal sealed record SkippedLocCall(string File, int Line, string Reason, string Snippet)
{
    public string Where => $"{File}:{Line}";
}

/// <summary>一次扫描的结果：字面量调用、被跳过的调用、注释里出现的调用数、扫过的文件。</summary>
internal sealed record LocScanResult(
    List<LocCallSite> Calls,
    List<SkippedLocCall> Skipped,
    int CommentMentions,
    List<string> Files);

/// <summary>
/// 从 C# 源码里找出所有 Loc.T(...) 调用，读出键名和实参个数。
///
/// 为什么不 grep 一把梭：`Loc.T(` 会出现在行注释、块注释、字符串字面量里，正则分不清；
/// 首个实参也可能是变量、拼接、插值串，那些求不出键名，只能跳过；而数实参又必须跨过
/// 嵌套括号、字符串里的逗号和引号。所以走一个带状态的逐字符扫描：
///
/// - 注释整段跳过（注释里的 Loc.T 不算调用）；
/// - 字符串只跳「字面文本」，插值串的 {} 孔里是真代码，孔内递归继续扫；
/// - 只在代码位置上认 Loc.T 调用，首参是字面量才谈得上知道键名；
/// - 认到调用后从键名后的逗号起逐字符走，按括号深度数逗号，字符串/字符字面量/注释整段跳过。
///
/// 读不出键名的一律记成 Skipped 并带上原因，不静默丢弃 —— 跳过和「没有问题」必须能区分。
/// </summary>
internal static class LocCallScanner
{
    /// <summary>注释里出现 Loc.T(...) 的次数，只用于报告「看见了但没算」，不参与断言。</summary>
    private static readonly Regex CallLikePattern =
        new(@"\bLoc\s*\.\s*T\s*\(", RegexOptions.Compiled);

    /// <summary>
    /// 扫仓库里调用 Loc.T 的源码：TextTool.Gui/**/*.cs（排除 bin、obj）。
    /// 路径按相对仓库根目录的显示，方便失败信息直接抄进编辑器。
    /// </summary>
    public static LocScanResult ScanRepository(string repoRoot)
    {
        var paths = new List<string>();

        string gui = Path.Combine(repoRoot, "TextTool.Gui");
        if (Directory.Exists(gui))
            paths.AddRange(Directory.EnumerateFiles(gui, "*.cs", SearchOption.AllDirectories)
                .Where(path => !IsBuildOutput(path)));

        var calls = new List<LocCallSite>();
        var skipped = new List<SkippedLocCall>();
        var files = new List<string>();
        int commentMentions = 0;

        foreach (string path in paths.OrderBy(p => p, StringComparer.Ordinal))
        {
            string display = Path.GetRelativePath(repoRoot, path).Replace('\\', '/');
            files.Add(display);

            string text = File.ReadAllText(path);
            ScanCode(text, 0, text.Length, display, calls, skipped, ref commentMentions);
        }

        return new LocScanResult(calls, skipped, commentMentions, files);
    }

    /// <summary>扫一段文本（自检用），displayName 会出现在结果里的 Where 上。</summary>
    public static LocScanResult ScanText(string text, string displayName)
    {
        var calls = new List<LocCallSite>();
        var skipped = new List<SkippedLocCall>();
        int commentMentions = 0;
        ScanCode(text, 0, text.Length, displayName, calls, skipped, ref commentMentions);
        return new LocScanResult(calls, skipped, commentMentions, new List<string> { displayName });
    }

    /// <summary>
    /// 扫 [start, end) 这一段，当成代码来读。整文件是 [0, 长度)，插值孔里则是孔内那一段。
    /// 用区间而不是子串，是为了行号直接按全文件算。
    /// </summary>
    private static void ScanCode(
        string text,
        int start,
        int end,
        string file,
        List<LocCallSite> calls,
        List<SkippedLocCall> skipped,
        ref int commentMentions)
    {
        int i = start;
        while (i < end)
        {
            char c = text[i];

            // 行注释：到行尾为止，里面的 Loc.T 不算。
            if (c == '/' && i + 1 < end && text[i + 1] == '/')
            {
                int stop = LineEnd(text, i, end);
                commentMentions += CountCallLike(text, i, stop);
                i = stop;
                continue;
            }

            // 块注释：到 */ 为止，跨行也照跳。没闭合就到段尾，不猜。
            if (c == '/' && i + 1 < end && text[i + 1] == '*')
            {
                int close = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                int stop = close < 0 || close + 2 > end ? end : close + 2;
                commentMentions += CountCallLike(text, i, stop);
                i = stop;
                continue;
            }

            // 字符字面量：'a'、'\''、'\n' 之类，跳过，免得里面的引号被当成字符串开头。
            if (c == '\'')
            {
                i = Math.Min(SkipCharLiteral(text, i), end);
                continue;
            }

            // 字符串：字面文本跳过，插值孔的孔内继续当代码扫。
            if (c is '"' or '@' or '$')
            {
                var holes = new List<(int Start, int End)>();
                int stringEnd = ReadStringLiteral(text, i, holes);
                if (stringEnd > i)
                {
                    i = Math.Min(stringEnd, end);
                    foreach (var (holeStart, holeEnd) in holes)
                        ScanCode(text, holeStart, Math.Min(holeEnd, end), file, calls, skipped, ref commentMentions);

                    continue;
                }
            }

            // 认 "Loc" 这个词（前后不能粘着标识符字符），再看是不是 .T( 调用。
            if (c == 'L' &&
                IsTokenStart(text, i) &&
                string.CompareOrdinal(text, i, "Loc", 0, 3) == 0 &&
                TryReadCall(text, i, out int next, out LocCallSite? site, out SkippedLocCall? skip))
            {
                if (site is not null)
                    calls.Add(site with { File = file });
                else
                    skipped.Add(skip! with { File = file });

                i = next > i ? next : i + 1;
                continue;
            }

            i++;
        }
    }

    /// <summary>
    /// 从 "Loc" 的 L 开始读一个调用。返回 true 表示确实是一处 Loc.T(...) 调用：
    /// site 非空是读到了字面量键名（含实参个数），否则 skip 说明为什么跳过、跳过哪儿。
    /// 返回 false 表示根本不是调用（Loc.X、LocT、Loc.ToString 之类），调用方继续逐字扫。
    /// next 是调用方该接着扫的位置 —— 刻意停在实参里，好让实参里的嵌套 Loc.T 也被扫到。
    /// </summary>
    private static bool TryReadCall(
        string text,
        int start,
        out int next,
        out LocCallSite? site,
        out SkippedLocCall? skip)
    {
        next = start;
        site = null;
        skip = null;

        int p = start + 3;                                          // 跳过 "Loc"
        if (p < text.Length && IsIdentChar(text[p])) return false;   // LocXxx
        if (!TryConsume(text, ref p, '.')) return false;
        if (!TryConsume(text, ref p, 'T')) return false;
        if (p < text.Length && IsIdentChar(text[p])) return false;   // Loc.ToString
        if (!TryConsume(text, ref p, '(')) return false;
        if (p >= text.Length) return false;

        int q = p;
        SkipTrivia(text, ref q);

        // 整段调用压成一行，只用于失败/跳过信息里的「原文」。
        int callEnd = FindClosingParen(text, p - 1);   // p - 1 就是 ( 的位置

        // 只有首参是字符串字面量才谈得上知道键名；其余一律记跳过，不猜值。
        int literalEnd = q;
        KeyKind kind = ReadKeyLiteral(text, q, out string key, out literalEnd);

        if (kind == KeyKind.NotLiteral)
        {
            skip = new SkippedLocCall(string.Empty, LineOf(text, start),
                "首参不是字面量（变量或表达式），静态读不出键名", Snippet(text, start, callEnd));
            next = q;
            return true;
        }

        // 字面量后面必须紧跟 , 或 )，否则是 "键" + 变量 这类拼接，键名不完整。
        int r = literalEnd;
        SkipTrivia(text, ref r);
        bool callShaped = r < text.Length && (text[r] == ',' || text[r] == ')');

        if (kind == KeyKind.Interpolated)
        {
            // 插值串的孔里有真代码，交给主循环重新扫这个字面量（那里会递归进孔）。
            skip = new SkippedLocCall(string.Empty, LineOf(text, start),
                "首参是插值字符串，静态读不出键名", Snippet(text, start, callEnd));
            next = q;
            return true;
        }

        if (!callShaped)
        {
            skip = new SkippedLocCall(string.Empty, LineOf(text, start),
                "首参是拼接表达式（如 前缀 + \"键名\"），静态拼不出完整键名", Snippet(text, start, callEnd));
            next = literalEnd;
            return true;
        }

        if (key.Length == 0)
        {
            skip = new SkippedLocCall(string.Empty, LineOf(text, start),
                "首参是空字符串", Snippet(text, start, callEnd));
            next = r;
            return true;
        }

        if (text[r] == ')')
        {
            site = new LocCallSite(string.Empty, LineOf(text, start), key, 0, Snippet(text, start, r + 1));
            next = r + 1;
            return true;
        }

        int argCount = CountArguments(text, r, out _);
        site = new LocCallSite(string.Empty, LineOf(text, start), key, argCount, Snippet(text, start, callEnd));
        next = literalEnd;
        return true;
    }

    /// <summary>首参的形态：读不出、插值串、字面量。</summary>
    private enum KeyKind
    {
        NotLiteral,
        Interpolated,
        Literal,
    }

    /// <summary>
    /// 读 Loc.T 的首个实参。Literal 时 key 是还原后的键名文本（verbatim 的 "" 还原成 "）；
    /// 返回的不是 NotLiteral 时，after 是字面量之后的位置。
    /// </summary>
    private static KeyKind ReadKeyLiteral(string text, int start, out string key, out int after)
    {
        key = string.Empty;
        after = start;

        if (start >= text.Length) return KeyKind.NotLiteral;
        if (text[start] is not ('"' or '@' or '$')) return KeyKind.NotLiteral;

        int p = start;
        bool verbatim = false;
        bool interpolated = false;

        while (p < text.Length && (text[p] == '$' || text[p] == '@'))
        {
            if (text[p] == '$')
            {
                if (interpolated) return KeyKind.NotLiteral;
                interpolated = true;
            }
            else
            {
                if (verbatim) return KeyKind.NotLiteral;
                verbatim = true;
            }

            p++;
        }

        if (p >= text.Length || text[p] != '"') return KeyKind.NotLiteral;   // @class 这种转义标识符

        // 原始字符串 """..."""：键不会是这种写法，当读不出来处理。
        int quotes = 0;
        while (p + quotes < text.Length && text[p + quotes] == '"') quotes++;
        if (quotes >= 3) return KeyKind.NotLiteral;

        int end = ReadStringLiteral(text, start, null);
        if (end <= start) return KeyKind.NotLiteral;

        after = end;
        if (interpolated) return KeyKind.Interpolated;

        // 引号之间的内容；end 指向收尾引号之后（未闭合时可能到行尾/文件尾）。
        int bodyEnd = end - 1;
        if (bodyEnd < p + 1 || bodyEnd > text.Length || text[bodyEnd] != '"') bodyEnd = Math.Min(end, text.Length);

        key = Unescape(text[(p + 1)..bodyEnd], verbatim);
        return KeyKind.Literal;
    }

    /// <summary>
    /// 从逗号开始数实参个数（逗号前已经数了一个）。按括号深度数顶层逗号，
    /// 字符串、字符字面量、注释整段跳过 —— 否则 $"a,b" 里的逗号、Foo(x, y) 里的逗号都会数错。
    /// afterParen 是配对 ) 之后的位置；没闭合就到文本尾，交给调用方按读不下去处理。
    /// </summary>
    private static int CountArguments(string text, int commaAt, out int afterParen)
    {
        int depth = 0;
        int count = 1;
        int p = commaAt + 1;

        while (p < text.Length)
        {
            char c = text[p];

            if (c is '"' or '@' or '$')
            {
                int end = ReadStringLiteral(text, p, null);
                if (end > p)
                {
                    p = end;
                    continue;
                }
            }

            if (c == '\'')
            {
                p = Math.Max(p + 1, SkipCharLiteral(text, p));
                continue;
            }

            if (c == '/' && p + 1 < text.Length && text[p + 1] == '/')
            {
                p = LineEnd(text, p, text.Length);
                continue;
            }

            if (c == '/' && p + 1 < text.Length && text[p + 1] == '*')
            {
                int close = text.IndexOf("*/", p + 2, StringComparison.Ordinal);
                p = close < 0 ? text.Length : close + 2;
                continue;
            }

            if (c is '(' or '[' or '{')
            {
                depth++;
                p++;
                continue;
            }

            if (c == ')')
            {
                if (depth == 0)
                {
                    afterParen = p + 1;
                    return count;
                }

                depth--;
                p++;
                continue;
            }

            if (c is ']' or '}')
            {
                if (depth > 0) depth--;
                p++;
                continue;
            }

            if (c == ',' && depth == 0)
            {
                count++;
                p++;
                continue;
            }

            p++;
        }

        afterParen = text.Length;
        return count;
    }

    /// <summary>
    /// 从 ( 的位置出发找配对的 )，返回 ) 之后的位置（没闭合则返回文本尾）。
    /// 只用来切出「原文」片段，字符串、字符字面量、注释一律整段跳过。
    /// </summary>
    private static int FindClosingParen(string text, int openParenAt)
    {
        int depth = 0;
        int p = openParenAt + 1;

        while (p < text.Length)
        {
            char c = text[p];

            if (c is '"' or '@' or '$')
            {
                int end = ReadStringLiteral(text, p, null);
                if (end > p)
                {
                    p = end;
                    continue;
                }
            }

            if (c == '\'')
            {
                p = Math.Max(p + 1, SkipCharLiteral(text, p));
                continue;
            }

            if (c == '/' && p + 1 < text.Length && text[p + 1] == '/')
            {
                p = LineEnd(text, p, text.Length);
                continue;
            }

            if (c == '/' && p + 1 < text.Length && text[p + 1] == '*')
            {
                int close = text.IndexOf("*/", p + 2, StringComparison.Ordinal);
                p = close < 0 ? text.Length : close + 2;
                continue;
            }

            if (c is '(' or '[' or '{') depth++;
            else if (c == ')')
            {
                if (depth == 0) return p + 1;
                depth--;
            }
            else if (c is ']' or '}')
            {
                if (depth > 0) depth--;
            }

            p++;
        }

        return text.Length;
    }

    /// <summary>
    /// 读一个字符串字面量，支持 "..."、@"..."、$"..."、$@"..."、@$"..."、"""..."""。
    /// 返回字面量之后的位置；返回 start 表示这个位置不是字符串（例如 @class 这种转义标识符）。
    /// holes 非空时，插值串的每个 {} 孔都会加进去（区间不含大括号本身），孔内的代码由调用方递归扫。
    /// </summary>
    private static int ReadStringLiteral(string text, int start, List<(int Start, int End)>? holes)
    {
        int p = start;
        bool verbatim = false;
        bool interpolated = false;

        while (p < text.Length && (text[p] == '$' || text[p] == '@'))
        {
            if (text[p] == '$')
            {
                if (interpolated) return start;      // $$"""...""" 之类，不猜
                interpolated = true;
            }
            else
            {
                if (verbatim) return start;
                verbatim = true;
            }

            p++;
        }

        if (p >= text.Length || text[p] != '"') return start;

        int quotes = 0;
        while (p + quotes < text.Length && text[p + quotes] == '"') quotes++;
        if (quotes >= 3)
        {
            string fence = new('"', quotes);
            int close = text.IndexOf(fence, p + quotes, StringComparison.Ordinal);
            return close < 0 ? text.Length : close + quotes;
        }

        return interpolated
            ? SkipInterpolatedLiteral(text, p + 1, verbatim, holes)
            : SkipPlainLiteral(text, p + 1, verbatim);
    }

    /// <summary>普通 / verbatim 字符串：吃到收尾引号。没闭合就到行尾（verbatim 到文件尾）。</summary>
    private static int SkipPlainLiteral(string text, int p, bool verbatim)
    {
        if (verbatim)
        {
            while (p < text.Length)
            {
                if (text[p] == '"')
                {
                    if (p + 1 < text.Length && text[p + 1] == '"')
                    {
                        p += 2;                       // "" 是转义的引号，不是收尾
                        continue;
                    }

                    return p + 1;
                }

                p++;
            }

            return text.Length;
        }

        while (p < text.Length)
        {
            if (text[p] == '\\' && p + 1 < text.Length)
            {
                p += 2;                              // 转义序列整对跳，\" 不会提前收尾
                continue;
            }

            if (text[p] == '"') return p + 1;
            if (text[p] == '\n') return p;           // 普通字符串不跨行，没闭合就到此为止
            p++;
        }

        return text.Length;
    }

    /// <summary>
    /// 插值字符串的字面部分：{{ 和 }} 是转义的字面大括号，单个 { 是孔。
    /// 孔内可能是真代码（含嵌套字符串、嵌套大括号），交给 SkipHole 配对，
    /// 区间只记下来，扫不扫由调用方决定。
    /// </summary>
    private static int SkipInterpolatedLiteral(
        string text,
        int p,
        bool verbatim,
        List<(int Start, int End)>? holes)
    {
        while (p < text.Length)
        {
            char c = text[p];

            if (c == '"')
            {
                if (verbatim && p + 1 < text.Length && text[p + 1] == '"')
                {
                    p += 2;
                    continue;
                }

                return p + 1;
            }

            if (!verbatim && c == '\\' && p + 1 < text.Length)
            {
                p += 2;
                continue;
            }

            if (!verbatim && c == '\n') return p;

            if (c == '{')
            {
                if (p + 1 < text.Length && text[p + 1] == '{')
                {
                    p += 2;                          // {{ 是字面大括号，不是孔
                    continue;
                }

                int holeEnd = SkipHole(text, p + 1);
                int contentEnd = holeEnd > p + 1 && text[holeEnd - 1] == '}' ? holeEnd - 1 : holeEnd;
                holes?.Add((p + 1, contentEnd));
                p = holeEnd;
                continue;
            }

            p++;
        }

        return text.Length;
    }

    /// <summary>
    /// p 指向孔内第一个字符，返回配对 } 之后的位置。孔里可能有嵌套字符串（其中的大括号不算深度）、注释。
    /// </summary>
    private static int SkipHole(string text, int p)
    {
        int depth = 1;

        while (p < text.Length && depth > 0)
        {
            char c = text[p];

            if (c is '"' or '@' or '$')
            {
                int end = ReadStringLiteral(text, p, null);
                if (end > p)
                {
                    p = end;
                    continue;
                }
            }

            if (c == '\'')
            {
                p = Math.Max(p + 1, SkipCharLiteral(text, p));
                continue;
            }

            if (c == '/' && p + 1 < text.Length && text[p + 1] == '/')
            {
                p = LineEnd(text, p, text.Length);
                continue;
            }

            if (c == '/' && p + 1 < text.Length && text[p + 1] == '*')
            {
                int close = text.IndexOf("*/", p + 2, StringComparison.Ordinal);
                p = close < 0 ? text.Length : close + 2;
                continue;
            }

            if (c == '{') depth++;
            else if (c == '}') depth--;

            p++;
        }

        return p;
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

    /// <summary>把字面量里的转义还原成文本。键名一般不含转义，这里只覆盖常见几种。</summary>
    private static string Unescape(string raw, bool verbatim)
    {
        if (verbatim) return raw.Replace("\"\"", "\"");
        if (!raw.Contains('\\')) return raw;

        var sb = new System.Text.StringBuilder(raw.Length);
        for (int i = 0; i < raw.Length; i++)
        {
            if (raw[i] != '\\' || i + 1 >= raw.Length)
            {
                sb.Append(raw[i]);
                continue;
            }

            char e = raw[++i];
            sb.Append(e switch
            {
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                '0' => '\0',
                '\\' => '\\',
                '"' => '"',
                '\'' => '\'',
                _ => e,                              // \uXXXX 之类不打算支持，原样保留
            });
        }

        return sb.ToString();
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
    private static int CountCallLike(string text, int start, int end) =>
        CallLikePattern.Matches(text[start..end]).Count;

    /// <summary>行号，从 1 开始。</summary>
    private static int LineOf(string text, int index)
    {
        int line = 1;
        for (int i = 0; i < index && i < text.Length; i++)
            if (text[i] == '\n') line++;

        return line;
    }

    private static int LineEnd(string text, int index, int end)
    {
        int newline = text.IndexOf('\n', index);
        return newline < 0 || newline > end ? end : newline;
    }

    /// <summary>把 [start, end) 这段源码压成一行，用作失败信息里的「原文」。</summary>
    private static string Snippet(string text, int start, int end)
    {
        int stop = Math.Min(Math.Max(end, start), text.Length);
        string raw = text[start..stop];
        var sb = new System.Text.StringBuilder(Math.Min(raw.Length, 240));
        bool lastWasSpace = false;

        foreach (char c in raw)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace && sb.Length > 0) sb.Append(' ');
                lastWasSpace = true;
                continue;
            }

            sb.Append(c);
            lastWasSpace = false;
        }

        string line = sb.ToString().Trim();
        return line.Length > 240 ? line[..240] + "…" : line;
    }

    private static bool IsIdentChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static bool IsTokenStart(string text, int index) =>
        index == 0 || !IsIdentChar(text[index - 1]);

    private static bool IsBuildOutput(string path) =>
        path.Split('/', '\\').Any(segment =>
            segment.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("obj", StringComparison.OrdinalIgnoreCase));
}
