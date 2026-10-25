namespace TextTool.Services;

/// <summary>
/// 检查报告里的文字出处。报告要能换语言，就不能把中文焊死在引擎和渲染器里：
/// 引擎产出命中时问一次，渲染器排版时再问一次。
///
/// 找不到键时返回调用点自带的中文原文，所以外部只提供部分译文也能用，
/// 缺的那几条退化成中文而不是显示成键名。
/// </summary>
public interface ILintText
{
    /// <param name="key">文案键。规则类带规则 Id，如 LintRule_repeated-punctuation_Title。</param>
    /// <param name="fallback">取不到时用的原文，可以带占位符。</param>
    /// <param name="args">占位符实参。</param>
    string Get(string key, string fallback, params object[] args);
}

/// <summary>
/// 中文兜底：一律用调用点的原文。命令行不换语言，走这条。
/// </summary>
public sealed class ChineseLintText : ILintText
{
    public static readonly ChineseLintText Instance = new();

    private ChineseLintText()
    {
    }

    public string Get(string key, string fallback, params object[] args) =>
        args.Length == 0 ? fallback : string.Format(fallback, args);
}
