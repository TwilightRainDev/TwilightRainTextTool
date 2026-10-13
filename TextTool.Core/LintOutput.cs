using System.Text;

namespace TextTool.Services;

/// <summary>
/// lint 结果的统一输出出口。CLI 的两条分支（人读 / JSON）都经由此处，
/// 行尾与分隔契约只维护一份，注入 <see cref="Console"/> 之外的纯函数，测试可直接断言。
/// </summary>
public static class LintOutput
{
    /// <summary>
    /// 把报告集合渲染成可直接写到 stdout 的完整文本。输出契约：
    /// <list type="bullet">
    /// <item>行尾一律为 LF（<c>\n</c>），不夹带 CR；</item>
    /// <item>人读分支在多份报告之间空一行，JSON 分支不插入额外分隔；</item>
    /// <item>整段文本以单个换行结尾（无报告时为空串）。</item>
    /// </list>
    /// 入参与返回值都不依赖 Console，是纯函数。
    /// </summary>
    /// <param name="reportSet">要输出的报告集合。</param>
    /// <param name="asJson">true 走 JSON 契约，false 走人读契约。</param>
    /// <returns>可直接写入 stdout 的文本。</returns>
    public static string Render(LintReportSet reportSet, bool asJson)
    {
        if (asJson)
            // ToJson 的正文已是 LF；末尾补一个 stdout 需要的换行，不再经 Console.WriteLine
            // 以免宿主把 \n 转成 CRLF
            return reportSet.ToJson() + "\n";

        var sb = new StringBuilder();
        for (int i = 0; i < reportSet.Reports.Count; i++)
        {
            // 报告之间空一行（多文件可读性）；Format 用 AppendLine，行尾在此统一归一为 LF
            if (i > 0)
                sb.Append('\n');
            sb.Append(LintTextFormatter.Format(reportSet.Reports[i]).Replace("\r\n", "\n"));
            sb.Append('\n');
        }
        return sb.ToString();
    }
}
