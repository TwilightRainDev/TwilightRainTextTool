using System.Text.RegularExpressions;

namespace TextTool.Services;

/// <summary>
/// 对话行识别共享逻辑 — 「角色」「台词」模式的检测。
/// VNReformatterService 与 PunctFixerService 共用，避免两处重复定义。
/// </summary>
public static class DialogueLine
{
    /// <summary>匹配「角色」「台词」格式的对话行（角色名后紧跟「）。</summary>
    public static readonly Regex Pattern = new(
        @"「[^」]*」「", RegexOptions.Compiled);

    /// <summary>判断一行是否为「角色」「台词」格式的对话。</summary>
    public static bool IsDialogueLine(string s) =>
        s.StartsWith('「') && Pattern.IsMatch(s);
}
