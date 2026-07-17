using System.Text;

namespace TextTool.Services;

/// <summary>
/// 中文截断断段修复。
/// 如果一行文本的最后一个字是中文字符（非标点），
/// 说明段落被错误截断，将下一行拼接到当前行，循环直到结尾不是中文字符。
///
/// 例："你好\n吗？" → "你好吗？"（好=CJK → 拼接 → ？=标点 → 停止）
/// </summary>
public static class CjkParagraphMerger
{
    /// <summary>
    /// 判断字符是否为 CJK 汉字（中日韩统一表意文字）
    /// </summary>
    private static bool IsCjkCharacter(char c)
    {
        return c >= 0x4E00 && c <= 0x9FFF     // CJK 基本区
            || c >= 0x3400 && c <= 0x4DBF     // CJK Extension A
            || c >= 0xF900 && c <= 0xFAFF     // CJK 兼容汉字
            || c >= 0x20000 && c <= 0x2A6DF;  // CJK Extension B
    }

    /// <summary>
    /// 一次反向扫描完成 noMerge 检查和 CJK 判断。
    /// 返回 true 表示应该 flush 当前累积文本（停止合并）。
    /// </summary>
    private static bool ShouldFlush(StringBuilder sb, HashSet<char>? noMergeSet)
    {
        for (int i = sb.Length - 1; i >= 0; i--)
        {
            if (!char.IsWhiteSpace(sb[i]))
            {
                char c = sb[i];
                // noMerge 优先于 CJK 规则
                if (noMergeSet != null && noMergeSet.Count > 0 && noMergeSet.Contains(c))
                    return true;
                // 以 CJK 结尾 → 继续合并（不 flush）
                return !IsCjkCharacter(c);
            }
        }
        // 全空白行 → flush
        return true;
    }

    /// <summary>
    /// 修复中文截断断段。正向累积模式 — 逐行扫描，遇到以 CJK 结尾的行
    /// 则继续 append 下一行，直到行末不是汉字再输出。
    /// noMerge 规则由调用方预编译为 HashSet 传入，避免重复构造。
    /// </summary>
    /// <param name="lines">输入行列表</param>
    /// <param name="noMergeSet">阻止合并的字符集，null 或空时不检查</param>
    public static List<string> Fix(List<string> lines, HashSet<char>? noMergeSet = null)
    {
        var result = new List<string>(lines.Count);
        var sb = new StringBuilder();

        foreach (string line in lines)
        {
            sb.Append(line);

            if (ShouldFlush(sb, noMergeSet))
            {
                result.Add(sb.ToString());
                sb.Clear();
            }
        }

        if (sb.Length > 0)
            result.Add(sb.ToString());

        return result;
    }
}
