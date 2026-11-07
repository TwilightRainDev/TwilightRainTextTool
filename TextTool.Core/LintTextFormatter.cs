using System.Buffers;
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

    /// <summary>与 <see cref="Escaped"/> 同一组字，供向量化查找用：绝大多数文本里一个都没有，先扫一遍再决定要不要复制。</summary>
    private static readonly SearchValues<char> EscapedSearch = SearchValues.Create(Escaped.ToArray());

    /// <summary>
    /// 一段输出要不要改写：要么带隐形字符，要么带 CR。两件事都只在改写出完整一段时才需要，
    /// 逐段写出的路径先拿它扫一遍，没有就直接把原样的字交出去。
    /// </summary>
    internal static readonly SearchValues<char> NeedsRewrite = SearchValues.Create(Escaped.Append('\r').ToArray());

    /// <summary>
    /// 一段输出收口：隐形字符转 <c>\uXXXX</c> 字面量，CRLF 收成 LF。
    /// 隐形字符表与 <see cref="EscapeInvisible"/> 是同一份，区别是这里连行尾一起收——
    /// 逐段写出时两件事都归这一处，不先拼成整段再改一遍。
    /// </summary>
    internal static string RewriteChunk(ReadOnlySpan<char> text)
    {
        var sb = new StringBuilder(text.Length + 8);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            // CRLF 收成 LF：CR 后面跟着 LF 时只写后面的 LF，落单的 CR 照原样留着
            if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') continue;
            if (Escaped.Contains(c))
            {
                sb.Append("\\u").Append(((int)c).ToString("x4"));
                continue;
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// 只替换隐形字符集本身（不碰 JSON 自身的换行等控制字符），供 --json 路径复用：
    /// JSON 文本里出现转义字面量，任何解析方都会把它解回原字符，语义不变。
    /// 一个隐形字符都没有时直接把入参交回去，不做复制。
    /// </summary>
    internal static string EscapeInvisible(string text)
    {
        var span = text.AsSpan();
        int first = span.IndexOfAny(EscapedSearch);
        if (first < 0) return text;

        var sb = new StringBuilder(text.Length + 8);
        sb.Append(span[..first]);
        for (int i = first; i < span.Length; i++)
        {
            char c = span[i];
            if (Escaped.Contains(c))
                sb.Append("\\u").Append(((int)c).ToString("x4"));
            else
                sb.Append(c);
        }
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
    /// 一批报告的人读输出：多份时每份前面先印一行文件名，报告之间空一行，
    /// 行尾一律 LF，整段以单个换行收尾。整段拿在手里时走这里，命令行逐份输出走 <see cref="LintHumanWriter"/>。
    ///
    /// 只有一份时不印文件名：一份稿的输出保持原来的样子，脚本按行数读不会被多出的一行绊住。
    /// </summary>
    public static string FormatSet(LintReportSet reportSet, ILintText? text = null)
    {
        var output = new StringWriter();
        using var sink = new LintHumanWriter(output, text);
        foreach (var report in reportSet.Reports)
            sink.Add(report);
        sink.Finish();
        return output.ToString();
    }

    /// <summary>
    /// 渲染人读报告，整份拿在手里。命令行逐份输出走 <see cref="WriteReport"/>：一份大报告的正文
    /// 也有几十兆，拼成字符串等于把那段文本在内存里放两遍。
    ///
    /// text 为 null 时用中文原文，命令行走这条；界面传入按当前语言取词的实现。
    ///
    /// showFile 为 true 时第一行是这份稿的文件名：一次报多份时两段命中的标题一模一样，
    /// 只有文件名能告诉读者这一条属于哪一份。
    /// </summary>
    public static string Format(LintReport report, ILintText? text = null, bool showFile = false)
    {
        var output = new StringWriter();
        WriteReport(output, report, text, showFile);
        return output.ToString();
    }

    /// <summary>
    /// 渲染一份人读报告到写出器：一行一行写出去，不攒整段。行尾一律 LF。
    /// 最后一行（汇总）不带换行，报告之间的空行由调用方补。
    /// </summary>
    public static void WriteReport(TextWriter sink, LintReport report, ILintText? text = null, bool showFile = false)
    {
        ILintText t = text ?? ChineseLintText.Instance;

        // 文件名顶格单独一行：命中行都缩进七格，读者一眼能看出这是新的一份稿
        if (showFile && !string.IsNullOrEmpty(report.File))
            Line(sink, report.File);

        // 给了基线时先说清下面这些数是跟谁比的：只列新增位置时，读者第一眼会以为命中变少了
        if (report.BaselineHits is int baselineHits)
            Line(sink, t.Get(
                "LintBaselineHeader", "相对基线新增 {0} 处，基线 {1} 处", report.Hits.Count, baselineHits));

        if (report.Hits.Count > 0)
        {
            foreach (var group in report.Hits.GroupBy(h => $"{h.Id} {h.Title}"))
            {
                // 组内按 (行, 列) 升序：命中在引擎里的顺序是 规则→Pattern→位置，
                // 同一规则的多条 Pattern 会让行号来回跳，人读报告需要单调
                var ordered = group.OrderBy(h => h.Line).ThenBy(h => h.Col).ToList();
                bool collapsed = ShouldCollapse(report, ordered[0].Id, ordered.Count);
                double density = Density(ordered.Count, report.Chars);
                Line(sink, collapsed
                    ? t.Get("LintGroupHeaderDense",
                        "[命中] {0}（{1} 处，每千字 {2} 处）：同一模板反复，只列样例",
                        group.Key, ordered.Count, density.ToString("0.0"))
                    : t.Get("LintGroupHeader", "[命中] {0}（{1} 处）", group.Key, ordered.Count));
                foreach (var hit in collapsed ? ordered.Take(CollapseSamples) : ordered)
                {
                    // 一行命中逐段写出去。先拼成一整行再写，几十万处命中就是几十万份副本；
                    // 片段也按原文的段落写，不先切出来
                    sink.Write("       ");
                    sink.Write(ToLf(t.Get("LintLocation", "第 {0} 行 第 {1} 列", hit.Line, hit.Col)));
                    sink.Write("  ");
                    WriteSnippet(sink, hit);
                    WriteLinked(sink, hit.Hint);
                    WriteLinked(sink, hit.Detail);
                    if (!string.IsNullOrEmpty(hit.SuggestScheme))
                    {
                        sink.Write("  <- ");
                        sink.Write(ToLf(t.Get("LintSchemeTag", "方案：{0}", hit.SuggestScheme)));
                    }
                    sink.Write('\n');
                }
                if (collapsed)
                    Line(sink, "       " + t.Get(
                        "LintCollapsedRest", "其余 {0} 处见 --json（位置一条不少）", ordered.Count - CollapseSamples));
            }
        }
        else
        {
            Line(sink, t.Get("LintNoHits", "[OK] 未发现可疑特征"));
        }

        if (report.Notes.Count > 0)
        {
            Line(sink, t.Get("LintNotesHeader", "[统计]"));
            foreach (var note in report.Notes)
                Line(sink, $"       - {note.Id} {note.Text}");
        }

        sink.Write(ToLf(t.Get(
            "LintSummary",
            "[NOTE] 全文 {0} 字，命中 {1} 处，统计项 {2} 条，段落口径 {3}；本命令只报位置，改写由判断层完成",
            report.Chars, report.Hits.Count, report.Notes.Count, report.ParagraphMode)));
    }

    /// <summary>一行以 LF 收尾：输出常被重定向成文件，本工作区文件一律 LF，不跟运行平台走。</summary>
    private static void Line(TextWriter sink, string text)
    {
        sink.Write(ToLf(text));
        sink.Write('\n');
    }

    /// <summary>命中说明前那一截箭头，空的就是没有。</summary>
    private static void WriteLinked(TextWriter sink, string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        sink.Write("  <- ");
        sink.Write(ToLf(text));
    }

    /// <summary>
    /// 一行里的片段：命中只记位置时直接按原文写出去，省略号、重叠掉落的隐形字、折成空格的换行
    /// 都落在输出流上。几十万处命中，每处先切一份再转义就是两份副本。
    /// 口径与 <see cref="Visible"/> 同一套：隐形字符与不可见控制符写成 \uXXXX，\r \n \t 折成一个空格，
    /// 连着几个只换一个空格、相邻的原有空格也不再多加。
    /// </summary>
    private static void WriteSnippet(TextWriter sink, LintHit hit)
    {
        if (!hit.TryGetSnippetWindow(out string source, out int from, out int to,
                out bool leftEllipsis, out bool rightEllipsis))
        {
            // 直接填进来的字面片段（老用法）：只有这一小段要转义，按原样拼一份即可
            string literal = hit.Snippet;
            if (literal.Length > 0) sink.Write(Visible(literal));
            return;
        }

        // 普通字符成段写出去，只在遇到要改写的字时断开：一个片段一般一两段就写完了
        bool empty = true;
        bool endsWithSpace = false;
        if (leftEllipsis)
        {
            sink.Write('…');
            empty = false;
        }
        int run = from;
        for (int i = from; i < to; i++)
        {
            char c = source[i];
            if (c is not ('\r' or '\n' or '\t') && !Escaped.Contains(c) && c >= 0x20) continue;
            if (i > run)
            {
                sink.Write(source.AsSpan(run, i - run));
                empty = false;
                endsWithSpace = source[i - 1] == ' ';
            }
            if (c is '\r' or '\n' or '\t')
            {
                if (empty || !endsWithSpace) sink.Write(' ');
                endsWithSpace = true;
            }
            else
            {
                sink.Write("\\u");
                sink.Write(((int)c).ToString("x4"));
                endsWithSpace = false;
            }
            empty = false;
            run = i + 1;
        }
        if (to > run) sink.Write(source.AsSpan(run, to - run));
        if (rightEllipsis) sink.Write('…');
    }

    /// <summary>
    /// 行尾收成 LF。命中说明与规则表里的文案可能自带 CRLF（配置在 Windows 上编辑过），
    /// 那些换行也要跟着本工作区一律 LF。没有回车时直接把入参交回去，不另存一份。
    /// </summary>
    private static string ToLf(string text) => text.Contains('\r') ? text.Replace("\r\n", "\n") : text;

    /// <summary>
    /// 片段里给人看的样子：隐形字符必须转义（老控制台编不出来），
    /// 但换行不是隐形字符，印成 \u000a 读者只会看到一串码位——折成一个空格，
    /// 让跨行的命中仍然读得成一句。
    /// </summary>
    public static string Visible(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (c is '\r' or '\n' or '\t')
            {
                // 连续几个换行只换一个空格，相邻的原有空格也不再多加
                if (sb.Length == 0 || sb[^1] != ' ')
                    sb.Append(' ');
                continue;
            }
            sb.Append(Escaped.Contains(c) || c < 0x20 ? $"\\u{(int)c:x4}" : c);
        }
        return sb.ToString();
    }
}

/// <summary>
/// 一批报告的人读出口：一份一份渲染、写完即丢，不再拼成一整段字符串。
///
/// 排版与 <see cref="FormatSet"/> 同一套口径：报告之间空一行、多份时每份前印文件名、行尾 LF。
/// 文件名印不印取决于总份数，而总份数要到最后一份才知道：第一份先留着，见到第二份时才认定「多份」，
/// 两份都带文件名。留着的始终只有一份报告。
/// </summary>
public sealed class LintHumanWriter : ILintReportSink
{
    private readonly TextWriter _sink;
    private readonly ILintText? _text;
    private LintReport? _pending;
    private bool _wroteAny;

    public LintHumanWriter(TextWriter sink, ILintText? text = null)
    {
        _sink = sink;
        _text = text;
    }

    public void Add(LintReport report)
    {
        if (_pending is null)
        {
            _pending = report;
            return;
        }
        Emit(_pending, showFile: true);
        _pending = report;
    }

    public void Finish()
    {
        if (_pending is null) return;
        // 走到这里还留着的那份是最后一份：前面已经写过一份，这一批就是多份，文件名照印
        Emit(_pending, showFile: _wroteAny);
        _pending = null;
    }

    /// <summary>没有要释放的东西：排版状态只在内存里，写完就没了。</summary>
    public void Dispose() => _pending = null;

    private void Emit(LintReport report, bool showFile)
    {
        if (_wroteAny)
            _sink.Write('\n');
        LintTextFormatter.WriteReport(_sink, report, _text, showFile);
        _sink.Write('\n');
        _wroteAny = true;
    }
}
