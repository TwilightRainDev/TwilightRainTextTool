using System.Text;

namespace TextTool.Services;

/// <summary>
/// 命中日志：一行一条 JSON，追加写。给留档与脚本读，不参与人读报告，也不进 <c>--json</c> 契约。
///
/// 开关是 <c>lint --log &lt;文件&gt;</c>；不给就不写，平时跑检查不留任何痕迹。
///
/// 日志里只写 ASCII 与汉字、全角标点，别的字符一律转成 <c>\uXXXX</c>。理由是可搬运：
/// 日志常被拷进工作区的文档目录留档，那边不许出现老控制台画不出来的字形，
/// 而命中原文里恰好常有箭头、省略号、表情这类字。转义之后整份日志是纯文本安全文件，
/// 汉字照原样，读者仍能看懂命中的是哪句话。
/// </summary>
public static class HitLog
{
    /// <summary>
    /// 把报告集合渲染成日志正文。每份报告先一行汇总，再逐条命中各一行。
    /// 纯函数：不碰磁盘，行尾为 LF。
    /// </summary>
    public static string Lines(LintReportSet reportSet)
    {
        var sb = new StringBuilder();
        foreach (var report in reportSet.Reports)
        {
            sb.Append("{\"Kind\":\"Report\",\"File\":").Append(Quote(report.File))
                .Append(",\"Chars\":").Append(report.Chars)
                .Append(",\"ParagraphMode\":").Append(Quote(report.ParagraphMode))
                .Append(",\"Hits\":").Append(report.Hits.Count)
                .Append("}\n");

            foreach (var hit in report.Hits)
            {
                sb.Append("{\"Kind\":\"Hit\",\"File\":").Append(Quote(report.File))
                    .Append(",\"Line\":").Append(hit.Line)
                    .Append(",\"Col\":").Append(hit.Col)
                    .Append(",\"Length\":").Append(hit.Length)
                    .Append(",\"Id\":").Append(Quote(hit.Id))
                    .Append(",\"Severity\":").Append(Quote(hit.Severity))
                    .Append(",\"Match\":").Append(Quote(hit.Match))
                    .Append(",\"Snippet\":").Append(Quote(hit.Snippet))
                    .Append("}\n");
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// 追加到日志文件。文件不存在就新建；目录不存在时报错交给调用方，不悄悄换落点。
    /// </summary>
    public static void Append(string path, LintReportSet reportSet)
    {
        string text = Lines(reportSet);
        if (text.Length == 0) return;

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            throw new DirectoryNotFoundException($"日志目录不存在：{directory}");

        File.AppendAllText(path, text, new UTF8Encoding(false));
    }

    /// <summary>
    /// 一个 JSON 字符串字面量。可画字符照原样，其余转 <c>\uXXXX</c>（小写十六进制，星平面字拆成两个）。
    /// </summary>
    internal static string Quote(string value)
    {
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); continue;
                case '\\': sb.Append("\\\\"); continue;
                // 命中可以跨行，原文里的换行要转义，否则一条命中会被拆成两行 JSON
                case '\n': sb.Append("\\n"); continue;
                case '\r': sb.Append("\\r"); continue;
                case '\t': sb.Append("\\t"); continue;
            }
            if (Drawable(c)) { sb.Append(c); continue; }
            sb.Append("\\u").Append(((int)c).ToString("x4"));
        }
        sb.Append('"');
        return sb.ToString();
    }

    /// <summary>
    /// 老控制台画得出来的字：ASCII 可打印区、换行回车制表、汉字与全角标点。
    /// 与工作区的字符面同一套范围，日志因此可以直接提交进文档目录。
    /// </summary>
    private static bool Drawable(char c) =>
        c is >= ' ' and <= '~'
        || c is >= '\u3000' and <= '\u303f'
        || c is >= '\u3400' and <= '\u4dbf'
        || c is >= '\u4e00' and <= '\u9fff'
        || c is >= '\uf900' and <= '\ufaff'
        || c is >= '\uff01' and <= '\uff5e'
        || c is >= '\uffe0' and <= '\uffe6';
}
