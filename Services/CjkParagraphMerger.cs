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
    /// 获取字符串的最后一个非空白字符。若无有效字符则返回 '\0'。
    /// </summary>
    private static char LastNonWhitespace(string text)
    {
        for (int i = text.Length - 1; i >= 0; i--)
        {
            if (!char.IsWhiteSpace(text[i]))
                return text[i];
        }
        return '\0';
    }

    /// <summary>
    /// 修复中文截断断段。遍历所有行，若某行末字符是 CJK 汉字，
    /// 则将下一行拼接上来，继续检查直到行末不是汉字或到达最后一行。
    /// </summary>
    public static List<string> Fix(List<string> lines)
    {
        var result = new List<string>(lines);

        int i = 0;
        while (i < result.Count - 1)
        {
            char lastChar = LastNonWhitespace(result[i]);

            if (lastChar != '\0' && IsCjkCharacter(lastChar))
            {
                // 中文截断：把下一行拼上来
                result[i] = result[i] + result[i + 1];
                result.RemoveAt(i + 1);
                // 不递增 i，继续检查合并后的行
            }
            else
            {
                i++;
            }
        }

        return result;
    }
}
