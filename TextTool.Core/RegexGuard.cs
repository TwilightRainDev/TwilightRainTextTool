using System.Text.RegularExpressions;

namespace TextTool.Services;

/// <summary>
/// 正则统一超时入口。所有来自配置/用户输入的正则必须经此构造：
/// 带超时后，灾难性回溯（ReDoS）不再无限挂起进程，而是抛
/// RegexMatchTimeoutException 由调用方显式报错——宁可响亮失败，不可静默卡死。
/// 非法模式的语法校验行为不变（仍抛 ArgumentException）。
/// </summary>
public static class RegexGuard
{
    /// <summary>单次匹配超时上限。</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    /// <summary>带超时的正则构造。</summary>
    public static Regex Create(string pattern, RegexOptions options = RegexOptions.None) =>
        new(pattern, options, Timeout);
}
