using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace TextTool.Services;

/// <summary>
/// 回复形状扫描的输出出口：人读与 JSON 两条分支，行尾一律 LF。
/// 与 lint 报告分开的理由：lint 面向整份稿件的复检，这里只回位置，字段少一半，消费方是脚本与实时闸。
/// </summary>
public static class ReplyShapeOutput
{
    /// <summary>JSON 契约版本。字段增减时加一，调用方按它判断能不能解析。</summary>
    public const int JsonVersion = 2;

    private sealed record JsonHit(string Id, string Token, int Line, bool Blocks);

    private sealed record JsonPayload(int Version, IReadOnlyList<JsonHit> Hits);

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        // 与 lint 的 JSON 同一套编码口径：中文原样，HTML 敏感字符照默认转义
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    public static string Render(IReadOnlyList<ReplyShapeHit> hits, bool asJson)
    {
        if (asJson)
        {
            var payload = new JsonPayload(
                JsonVersion,
                hits.Select(h => new JsonHit(h.Id, h.Token, h.Line, h.Blocks)).ToList());
            return LintTextFormatter
                .EscapeInvisible(JsonSerializer.Serialize(payload, Options))
                .Replace("\r\n", "\n") + "\n";
        }

        if (hits.Count == 0)
            return "没有读者解不开的写法。\n";

        var sb = new System.Text.StringBuilder();
        foreach (var group in hits.GroupBy(h => (h.Id, h.Title, h.Blocks)))
        {
            // 打回与只提醒分开列：读报告的人先看哪几处是硬伤
            string marker = group.Key.Blocks ? "[命中]" : "[只提醒]";
            sb.Append($"{marker} {group.Key.Title}（{group.Count()} 处）\n");
            foreach (var hit in group)
                sb.Append($"       第 {hit.Line} 行  {hit.Token}\n");
        }
        sb.Append($"共 {hits.Count} 处。\n");
        return sb.ToString();
    }
}
