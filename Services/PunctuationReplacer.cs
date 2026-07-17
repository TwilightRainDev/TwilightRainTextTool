using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

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
/// 标点符号替换引擎。
/// 规则持久化到 JSON 文件，支持用户自由增删。
/// </summary>
public static class PunctuationReplacer
{
    private static string RulesPath =>
        Path.Combine(AppContext.BaseDirectory, "replace_rules.json");

    /// <summary>
    /// 加载替换规则
    /// </summary>
    public static List<ReplaceRule> LoadRules()
    {
        try
        {
            if (File.Exists(RulesPath))
            {
                string json = File.ReadAllText(RulesPath, Encoding.UTF8);
                var rules = JsonSerializer.Deserialize<List<ReplaceRule>>(json);
                return rules ?? new List<ReplaceRule>();
            }
        }
        catch { /* 文件损坏则返回空列表 */ }

        return new List<ReplaceRule>();
    }

    /// <summary>
    /// 保存替换规则
    /// </summary>
    public static void SaveRules(List<ReplaceRule> rules)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        string json = JsonSerializer.Serialize(rules, options);
        File.WriteAllText(RulesPath, json, new UTF8Encoding(false));
    }

    /// <summary>
    /// 按规则逐条替换文本内容
    /// </summary>
    public static string Apply(string text, List<ReplaceRule> rules)
    {
        foreach (var rule in rules)
        {
            if (string.IsNullOrEmpty(rule.Find))
                continue;

            // 使用简单字符串替换（非正则），避免转义问题
            text = text.Replace(rule.Find, rule.Replace);
        }
        return text;
    }

    /// <summary>
    /// 按规则逐条替换文件中每一行
    /// </summary>
    public static void ApplyToFile(string filePath, Encoding encoding, List<ReplaceRule> rules)
    {
        string[] lines = File.ReadAllLines(filePath, encoding);

        for (int i = 0; i < lines.Length; i++)
        {
            lines[i] = Apply(lines[i], rules);
        }

        File.WriteAllLines(filePath, lines, new UTF8Encoding(true));
    }
}
