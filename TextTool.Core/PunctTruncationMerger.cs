using System.Text;

namespace TextTool.Services;

/// <summary>
/// 标点截断断段修复。
/// 如果一行文本以指定的标点字符结尾（如逗号、顿号），
/// 说明该行与下一行属于同一段落，将下一行拼接到当前行。
///
/// 同时支持"不合并标点"规则：如果一行以这些标点结尾，则即使其他规则要求合并也不合并。
/// 例："回家吧，\n孩子" → "回家吧，孩子"（逗号截断 → 拼接）
/// </summary>
public static class PunctTruncationMerger
{
    /// <summary>
    /// 修复标点截断。正向扫描，遇到以指定标点结尾的行则继续 append 下一行。
    /// noMerge 优先于 mergeChars。
    /// 两个 set 均由调用方预编译传入，避免重复构造。
    /// </summary>
    /// <param name="lines">输入行列表</param>
    /// <param name="mergeSet">触发合并的标点字符集</param>
    /// <param name="noMergeSet">阻止合并的字符集，null 或空时不检查</param>
    public static List<string> Fix(List<string> lines, HashSet<char> mergeSet, HashSet<char>? noMergeSet = null)
    {
        var result = new List<string>(lines.Count);
        var sb = new StringBuilder();

        foreach (string line in lines)
        {
            sb.Append(line);

            if (sb.EndsWithAny(noMergeSet) || !sb.EndsWithAny(mergeSet))
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
