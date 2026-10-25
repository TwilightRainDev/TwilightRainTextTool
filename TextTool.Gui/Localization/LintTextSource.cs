using TextTool.Services;

namespace TextTool.Gui.Localization;

/// <summary>
/// 把检查报告的文案接到当前界面语言上。
///
/// 引擎在产出命中时问一次，渲染器排版时再问一次，所以报告正文会跟着语言走，
/// 而不是永远中文。缺键时用调用点自带的中文原文兜底：宁可那一条是中文，
/// 也不要界面上出现键名。
/// </summary>
public sealed class LintTextSource : ILintText
{
    public static readonly LintTextSource Instance = new();

    private LintTextSource()
    {
    }

    public string Get(string key, string fallback, params object[] args)
    {
        string value = Loc.T(key);
        if (value == $"[{key}]")
            return args.Length == 0 ? fallback : SafeFormat(fallback, args);

        return SafeFormat(value, args);
    }

    /// <summary>译文里的占位符个数对不上时退回原文，不让一条译文把整个报告打崩。</summary>
    private static string SafeFormat(string format, object[] args)
    {
        if (args.Length == 0)
            return format;

        try
        {
            return string.Format(format, args);
        }
        catch (FormatException)
        {
            return format;
        }
    }
}
