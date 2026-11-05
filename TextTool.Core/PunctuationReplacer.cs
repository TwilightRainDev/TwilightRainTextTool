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
    /// <param name="protectBlogRegions">
    /// 为 true 时博文结构（头部字段、三冒号指令行、代码块、公式、链接地址）原样保留，
    /// 替换只落在正文上。行合并流水线按行调用，看不到整份稿子的头尾，那里传 false。
    /// </param>
    public static string Apply(string text, List<ReplaceRule> rules, bool protectBlogRegions = true)
    {
        var mask = protectBlogRegions ? BlogMarkdownMask.Create(text) : null;
        if (mask is null || mask.MaskedChars == 0) return ApplyRules(text, rules);
        // 按遮罩切段：没遮的地方套规则，遮住的地方原封不动拼回去。
        // 规则原本就在一条串上连着套，按段套与整串套在段内等价；
        // 跨段才成立的替换（一条规则横跨代码块边界）不做，那本来也不是标点该管的事。
        var result = new StringBuilder(text.Length);
        int runStart = 0;
        for (int i = 1; i <= text.Length; i++)
        {
            if (i < text.Length && mask.IsMasked(i) == mask.IsMasked(runStart)) continue;

            string run = text[runStart..i];
            result.Append(mask.IsMasked(runStart) ? run : ApplyRules(run, rules));
            runStart = i;
        }
        return result.ToString();
    }

    private static string ApplyRules(string text, List<ReplaceRule> rules)
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
