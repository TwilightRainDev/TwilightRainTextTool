using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace TextTool.Services;

/// <summary>一条命中。Match 是命中的原文，Snippet 是带上下文的展示片段。</summary>
public sealed class LintHit
{
    public string Id { get; set; } = "";
    public string Group { get; set; } = "";
    public string Title { get; set; } = "";
    public string Severity { get; set; } = "info";
    public int Line { get; set; }
    public int Col { get; set; }
    public int Length { get; set; }
    public string Match { get; set; } = "";
    public string Snippet { get; set; } = "";
    public string Detail { get; set; } = "";
    public string? Hint { get; set; }
    public string? SuggestScheme { get; set; }

    /// <summary>
    /// 逐字段复制，供 Filter 切断结果报告与源报告的元素引用。
    /// 成员全是 string/int，按字段整体复制即完整复制，新增属性也不会漏拷。
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

    /// <summary>本次扫描采用的段落口径：line（非空行即一段）或 markdown（空行分段）。</summary>
    public string ParagraphMode { get; set; } = "line";

    public List<LintHit> Hits { get; set; } = new();
    public List<LintNote> Notes { get; set; } = new();

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
            ParagraphMode = ParagraphMode,
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
    /// <summary>契约版本。2：行尾为 LF、HTML 敏感字符转义、Hits 保证按 (Line, Col) 有序。</summary>
    public int Version { get; set; } = 2;
    public List<LintReport> Reports { get; set; } = new();

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        // 中文照原样输出，不转 \uXXXX；HTML 敏感字符（< > & '）与 UTF-7 的 + 仍按默认编码器转义——
        // UnsafeRelaxedJsonEscaping 会把它们一起放开，是另一个方向的过头
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    /// <summary>
    /// 序列化后统一收口两处：隐形字符转义为 \uXXXX 字面量、行尾归一为 LF
    /// （输出常被重定向成文件，本工作区文件一律 LF）。
    /// </summary>
    public string ToJson() => LintTextFormatter
        .EscapeInvisible(JsonSerializer.Serialize(this, Options))
        .Replace("\r\n", "\n");
}
