namespace TextTool.Services;

/// <summary>
/// 一条命中。Match 是命中的原文，Snippet 是带上下文的展示片段。
///
/// 长稿上的命中是几十万条，每处都把这两个片段切成字符串，报告还没写出去就先占掉几百兆。
/// 命中因此只记「原文在哪、取多长」，两段文字在读取时才从原文切——
/// 输出路径各读一次，切完即丢，不留在命中里。
/// 直接构造命中并把字符串填进这两个属性也照旧可用（老调用方与测试走的就是这条）。
/// </summary>
public sealed class LintHit
{
    public string Id { get; set; } = "";
    public string Group { get; set; } = "";
    public string Title { get; set; } = "";
    public string Severity { get; set; } = "info";
    public int Line { get; set; }
    public int Col { get; set; }
    public int Length { get; set; }

    /// <summary>这份命中挂在哪份原文上。只存引用，整份稿只此一份。</summary>
    private string? _source;

    /// <summary>Match 取自原文的哪一段。与 Length 不是一回事：复读串报的是整串长度，Match 只是串首那个单元。</summary>
    private int _matchIndex;
    private int _matchLength;

    /// <summary>Snippet 取自原文的哪一段，以及前后各扩展几个字。</summary>
    private int _snippetIndex;
    private int _snippetLength;
    private int _snippetPad;

    /// <summary>直接填进来的字面值。填了就以它为准，没填才回原文切。</summary>
    private string? _match;
    private string? _snippet;

    /// <summary>命中的原文。从原文切出来，不缓存：调用方读一次就写出去，留着只是白占内存。</summary>
    public string Match
    {
        get => _match ?? (_source ?? "").Substring(_matchIndex, _matchLength);
        set => _match = value;
    }

    /// <summary>带上下文的展示片段，取窗口的规则见 <see cref="AiToneLintService.Snippet"/>。取值时才切，不缓存。</summary>
    public string Snippet
    {
        get => _snippet ?? (_source is null
            ? ""
            : AiToneLintService.Snippet(_source, _snippetIndex, _snippetLength, _snippetPad));
        set => _snippet = value;
    }

    public string Detail { get; set; } = "";
    public string? Hint { get; set; }
    public string? SuggestScheme { get; set; }

    /// <summary>
    /// 把命中挂到原文上。Match 取 [matchIndex, matchIndex + matchLength)，
    /// Snippet 取 [snippetIndex, snippetIndex + snippetLength) 前后各扩展 snippetPad 个字。
    /// 两段区间分开记：命中原文与展示片段不总是同一段（比喻堆砌的片段从整句起，命中原文只是句首那几个字）。
    /// </summary>
    internal void BindTo(string source, int matchIndex, int matchLength,
        int snippetIndex, int snippetLength, int snippetPad = 8)
    {
        _source = source;
        _matchIndex = matchIndex;
        _matchLength = matchLength;
        _snippetIndex = snippetIndex;
        _snippetLength = snippetLength;
        _snippetPad = snippetPad;
    }

    /// <summary>
    /// 展示片段在原文里的窗口：两端的省略号加不加也一并给出。
    /// 给人读输出逐段写出去时用——只为写出去而先拼成一份字符串，几十万处命中就是几百兆垃圾。
    /// 命中带的是直接填进来的字面值、或者根本没挂原文时返回 false，调用方退回读 <see cref="Snippet"/>。
    /// </summary>
    internal bool TryGetSnippetWindow(out string source, out int from, out int to,
        out bool leftEllipsis, out bool rightEllipsis)
    {
        if (_snippet is not null || _source is null)
        {
            source = "";
            from = to = 0;
            leftEllipsis = rightEllipsis = false;
            return false;
        }
        source = _source;
        AiToneLintService.SnippetWindow(_source, _snippetIndex, _snippetLength, _snippetPad, out from, out to);
        leftEllipsis = from > 0;
        rightEllipsis = to < _source.Length;
        return true;
    }

    /// <summary>
    /// 逐字段复制，供 Filter 切断结果报告与源报告的元素引用。
    /// 成员全是 string/int，按字段整体复制即完整复制，新增属性也不会漏拷——
    /// 原文引用与两段区间按值复制过去，复制出的命中仍能自己切出同样的两段文字。
    /// </summary>
    internal LintHit Copy() => (LintHit)MemberwiseClone();
}

/// <summary>统计观察，不计入退出码。</summary>
public sealed class LintNote
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";

    /// <summary>逐字段复制，理由同 <see cref="LintHit.Copy"/>。</summary>
    internal LintNote Copy() => (LintNote)MemberwiseClone();
}

public sealed class LintReport
{
    public string File { get; set; } = "";
    public int Chars { get; set; }

    /// <summary>
    /// 这份稿的原文。命中只记位置，Match 与 Snippet 到输出时才从这里切；
    /// 报告写完、这个引用一放，几十兆的稿子就能跟着回收。
    /// 不进 --json 契约：机器手上的原文比报告里那份更权威，多写一遍只是把同一段字扩大一倍。
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? SourceText { get; set; }

    /// <summary>本次扫描采用的段落口径：line（非空行即一段）或 markdown（空行分段）。</summary>
    public string ParagraphMode { get; set; } = "line";

    public List<LintHit> Hits { get; set; } = new();
    public List<LintNote> Notes { get; set; } = new();

    /// <summary>
    /// 基线那份的命中处数。null 表示这次没给基线，Hits 就是整篇的命中；
    /// 给了基线时 Hits 只含新增位置，这个数供人读报告开头那一行对照用。
    /// 没给基线时整个字段不出现在 JSON 里：不带 --baseline 的调用输出与升版前一致。
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? BaselineHits { get; set; }

    /// <summary>
    /// 规则 Id 到折叠密度的对照，由引擎按规则表填好，只供人读渲染用。
    /// 不进 --json 契约：机器要的是全部位置，折叠是读者的负担问题。
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public Dictionary<string, int> CollapseAtDensity { get; set; } = new();

    /// <summary>
    /// 过滤命中与统计项。--only 是规则选择轴，Hits 与 Notes 都按 Id 过滤；
    /// --min-severity 是严重度轴，Notes 无 severity 故不受它影响。退出码基于过滤后的 Hits 判定。
    /// </summary>
    public LintReport Filter(IReadOnlyCollection<string>? onlyIds, string? minSeverity)
    {
        var only = onlyIds is null
            ? null
            : new HashSet<string>(onlyIds, StringComparer.OrdinalIgnoreCase);
        bool wantWarnOnly = string.Equals(minSeverity, "warn", StringComparison.OrdinalIgnoreCase);

        // 结果报告与接收者不共享可变状态：两个集合都是新实例，元素逐个复制。
        // 共享集合则 filtered.Notes.Add(...) 写回源报告，共享元素则改 filtered.Hits[0] 的
        // 任一属性都写回源报告——Filter 是纯函数，结果不能反向影响输入。
        return new LintReport
        {
            File = File,
            Chars = Chars,
            SourceText = SourceText,
            ParagraphMode = ParagraphMode,
            BaselineHits = BaselineHits,
            // 折叠口径随报告一起过来：过滤只该裁掉命中，不该让人读视图换一种渲染方式
            CollapseAtDensity = new Dictionary<string, int>(CollapseAtDensity, StringComparer.OrdinalIgnoreCase),
            Notes = Notes
                .Where(n => only is null || only.Contains(n.Id))
                .Select(n => n.Copy())
                .ToList(),
            Hits = Hits
                .Where(h => only is null || only.Contains(h.Id))
                .Where(h => !wantWarnOnly || h.Severity == "warn")
                .Select(h => h.Copy())
                .ToList(),
        };
    }
}

/// <summary>多文件输出的统一包装，消费者不必按文件数分支。</summary>
public sealed class LintReportSet
{
    /// <summary>契约版本。4：新增 --baseline 差分，报告多出基线处数这一字段，命中只剩新增位置。
    /// 3：规则 Id 由短码改为语义化 slug（如 `parallel-contrast`）。
    /// 2：行尾为 LF、HTML 敏感字符转义、Hits 保证按 (Line, Col) 有序、新增 ParagraphMode 字段。</summary>
    public int Version { get; set; } = 4;
    public List<LintReport> Reports { get; set; } = new();

    /// <summary>
    /// 整段 JSON。一次只出几份、又要拿着整段看（测试、报告对比）时走这里；
    /// 命令行逐份输出走 <see cref="LintJsonWriter"/>，两条路径用的是同一个写入器，
    /// 键序、缩进、隐形字符转义、行尾为 LF 都只维护一份。
    /// </summary>
    public string ToJson()
    {
        var text = new StringWriter();
        using (var sink = new LintJsonWriter(text, Version, trailingNewLine: false))
        {
            foreach (var report in Reports)
                sink.Add(report);
            sink.Finish();
        }
        return text.ToString();
    }
}
