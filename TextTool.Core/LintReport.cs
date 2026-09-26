using System.Text.Encodings.Web;
using System.Text.Json;

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
}

/// <summary>统计观察，不计入退出码。</summary>
public sealed class LintNote
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
}

public sealed class LintReport
{
    public string File { get; set; } = "";
    public int Chars { get; set; }
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

        return new LintReport
        {
            File = File,
            Chars = Chars,
            Notes = only is null ? Notes : Notes.Where(n => only.Contains(n.Id)).ToList(),
            Hits = Hits
                .Where(h => only is null || only.Contains(h.Id))
                .Where(h => !wantWarnOnly || h.Severity == "warn")
                .ToList(),
        };
    }
}

/// <summary>多文件输出的统一包装，消费者不必按文件数分支。</summary>
public sealed class LintReportSet
{
    public int Version { get; set; } = 1;
    public List<LintReport> Reports { get; set; } = new();

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,   // 中文照原样输出，不转 \uXXXX
    };

    /// <summary>序列化后整体转义隐形字符：JSON 文本里显示为 \uXXXX 字面量，解析方会解回原字符，语义不变。</summary>
    public string ToJson() => LintTextFormatter.EscapeInvisible(JsonSerializer.Serialize(this, Options));
}
