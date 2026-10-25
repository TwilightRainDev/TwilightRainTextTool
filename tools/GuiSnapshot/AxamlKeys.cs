using System.Text.RegularExpressions;

namespace TextTool.Tools.GuiSnapshot;

/// <summary>
/// 从源码里的 .axaml 文本中收集 {DynamicResource 键名}。
///
/// 为什么读源码而不是问运行中的程序：运行时只能查到"哪些键有值"，查不到"哪些键被引用"，
/// 少一个键的界面看起来只是文字空白，不会报错。文本扫描才能把引用的键列全。
/// </summary>
internal static class AxamlKeys
{
    /// <summary>
    /// 主题色键。Theme.axaml 的 ThemeDictionaries 里，Light 和 Dark 两边都必须有这十个。
    /// 顺序按语义分组，跟 Theme.Light.axaml 保持一致，方便对着看。
    /// </summary>
    public static readonly string[] ThemeKeys =
    {
        "Bg", "Fg", "ControlBg", "MutedFg", "SuccessFg",
        "WarningFg", "ActionBg", "ActionFg", "StatusBg", "DragHighlightBg",
    };

    /// <summary>也不是文案的键：字号和字体。这两个只定义在 App.axaml 里。</summary>
    public static readonly string[] NonTextKeys = { "AppFontSize", "AppFontFamily" };

    /// <summary>去掉 XML 注释。注释里写的 {DynamicResource 键名} 会被正则一起抓，那是假的。</summary>
    private static readonly Regex CommentPattern = new("<!--.*?-->", RegexOptions.Singleline);

    private static readonly Regex KeyPattern =
        new(@"\{\s*DynamicResource\s+([^\s\}]+)\s*\}", RegexOptions.Compiled);

    public static bool IsNonText(string key) =>
        ThemeKeys.Contains(key, StringComparer.Ordinal) ||
        NonTextKeys.Contains(key, StringComparer.Ordinal);

    /// <summary>
    /// 找 TextTool.Gui 源码目录。程序集在 tools/GuiSnapshot/bin/... 下，运行时工作目录不保证，
    /// 所以从程序集目录和当前目录分别往上找，碰到含 .axaml 的 TextTool.Gui 就算找到。
    /// </summary>
    public static string? FindGuiSourceDir()
    {
        foreach (string start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var dir = new DirectoryInfo(start);
            for (int i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
            {
                string candidate = Path.Combine(dir.FullName, "TextTool.Gui");
                if (Directory.Exists(candidate) && CollectAxamlFiles(candidate).Count > 0)
                    return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// 扫出所有键，返回 键名 -> 首次引用它的文件（相对 TextTool.Gui）。files 是扫过的文件清单。
    /// bin 和 obj 下的 .axaml 是构建产物的副本，不算源码引用，排除掉。
    /// </summary>
    public static SortedDictionary<string, string> Scan(string guiDir, out List<string> files)
    {
        files = CollectAxamlFiles(guiDir);

        var keys = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (string file in files)
        {
            string text = CommentPattern.Replace(File.ReadAllText(file), string.Empty);

            foreach (Match match in KeyPattern.Matches(text))
            {
                string key = match.Groups[1].Value;
                if (!keys.ContainsKey(key))
                    keys[key] = Path.GetRelativePath(guiDir, file);
            }
        }

        return keys;
    }

    private static List<string> CollectAxamlFiles(string guiDir) =>
        Directory.EnumerateFiles(guiDir, "*.axaml", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

    private static bool IsBuildOutput(string path) =>
        path.Split('/', '\\').Any(segment =>
            segment.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("obj", StringComparison.OrdinalIgnoreCase));
}
