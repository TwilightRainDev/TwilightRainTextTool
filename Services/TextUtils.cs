using System.Text;

namespace TextTool.Services;

/// <summary>
/// 文本工具扩展方法。
/// </summary>
public static class TextUtils
{
    /// <summary>
    /// 判断 StringBuilder 末尾（跳过尾部空白后）是否以 chars 中的任一字符结尾。
    /// chars 为 null 或空时返回 false。
    /// </summary>
    public static bool EndsWithAny(this StringBuilder sb, HashSet<char>? chars)
    {
        if (chars == null || chars.Count == 0) return false;
        for (int i = sb.Length - 1; i >= 0; i--)
        {
            if (!char.IsWhiteSpace(sb[i]))
                return chars.Contains(sb[i]);
        }
        return false;
    }
}
