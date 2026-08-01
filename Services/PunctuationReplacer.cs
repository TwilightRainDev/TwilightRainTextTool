using System.Text;
using System.Text.Json;

namespace TextTool.Services;

/// <summary>
/// 单条替换规则
/// </summary>
public class ReplaceRule
{
    public string Find { get; set; } = "";
    public string Replace { get; set; } = "";
}

/// <summary>
/// 标点符号替换引擎（纯转换职责）。
/// 规则持久化由 <see cref="ReplaceRuleStore"/> 负责。
/// </summary>
public static class PunctuationReplacer
{
    /// <summary>
    /// 按规则逐条替换文本内容。
    /// 使用 StringBuilder.Replace 避免 O(lines×rules) 次中间字符串分配。
    /// </summary>
    public static string Apply(string text, List<ReplaceRule> rules)
    {
        var sb = new StringBuilder(text);
        foreach (var rule in rules)
        {
            if (string.IsNullOrEmpty(rule.Find))
                continue;

            sb.Replace(rule.Find, rule.Replace);
        }
        return sb.ToString();
    }
}

/// <summary>
/// 替换规则持久化存储（JSON 文件）。
/// </summary>
public static class ReplaceRuleStore
{
    public static List<ReplaceRule> Load() => JsonFileStore.Load<ReplaceRule>("replace_rules.json");

    public static void Save(List<ReplaceRule> rules) => JsonFileStore.Save("replace_rules.json", rules);
}
