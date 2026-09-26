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

    /// <summary>过滤命中（Notes 不受影响）。退出码基于过滤后的集合判定。</summary>
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
            Notes = Notes,
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

    public string ToJson() => JsonSerializer.Serialize(this, Options);
}
