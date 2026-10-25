using Avalonia;
using TextTool.Gui.Localization;

namespace TextTool.Tools.GuiSnapshot;

/// <summary>
/// 文案键体检：.axaml 里引用的每个文案键，三种语言都得给得出值。
///
/// 为什么要排除名单：资源宿主里既有主题色键，也有 AppFontSize 这类非文案键，
/// 只问"查不查得到"是分不出文案和配色的。所以先用文本扫描定出"引用了哪些键"，
/// 再用 AxamlKeys.IsNonText 剔掉非文案的，剩下的逐个语言验。
///
/// 缺键在界面上表现为空白或 [键名]，不会抛异常，是最容易静默漏过去的一类缺陷。
/// </summary>
internal static class TextKeyCheck
{
    public static int Run()
    {
        Report.Section("文案键体检");

        string? guiDir = AxamlKeys.FindGuiSourceDir();
        if (guiDir is null)
        {
            Report.Fail("找不到 TextTool.Gui 源码目录，文案键无从检查（不接受跳过）");
            return 1;
        }

        SortedDictionary<string, string> referenced = AxamlKeys.Scan(guiDir, out List<string> files);

        int excludedTheme = referenced.Keys.Count(AxamlKeys.ThemeKeys.Contains);
        int excludedMisc = referenced.Keys.Count(k => AxamlKeys.NonTextKeys.Contains(k));
        var textKeys = referenced.Where(pair => !AxamlKeys.IsNonText(pair.Key)).ToList();

        Report.Info($"扫描 {guiDir}: {files.Count} 个 .axaml，引用 {referenced.Count} 个键");
        Report.Info($"排除主题色键 {excludedTheme} 个，字号字体键 {excludedMisc} 个，剩文案键 {textKeys.Count} 个");
        Report.Info($"文案键: {string.Join(", ", textKeys.Select(pair => pair.Key))}");

        if (textKeys.Count == 0)
        {
            // 扫描器自己坏掉时不能让检查空过：零个键的"全部通过"是假绿。
            Report.Fail("一个文案键都没扫到，扫描逻辑可能失效");
            return 1;
        }

        if (Application.Current is not { } app)
        {
            Report.Fail("Application.Current 为空，文案资源无从查起");
            return 1;
        }

        if (Loc.SupportedLanguages.Count == 0)
        {
            Report.Fail("Loc.SupportedLanguages 是空的，一个语言都验不了");
            return 1;
        }

        int failures = 0;
        var rows = new List<string[]>();

        foreach (LanguageOption language in Loc.SupportedLanguages)
        {
            // 换语言走的就是界面上的那条路径：整份字典换掉，DynamicResource 自己重取。
            Loc.SetLanguage(language.Code);

            var missing = textKeys
                .Where(pair => !app.TryGetResource(pair.Key, null, out _))
                .ToList();

            foreach (var pair in missing)
                Report.Fail($"{language.Code} 缺文案键 {pair.Key}（被 {pair.Value} 引用）");

            failures += missing.Count;
            rows.Add(new[]
            {
                $"{language.Code} ({language.Display})",
                textKeys.Count.ToString(),
                missing.Count.ToString(),
                missing.Count == 0 ? "齐全" : string.Join(", ", missing.Select(pair => pair.Key)),
            });
        }

        Report.PrintTable(
            new[] { "语言", "文案键数", "缺失", "缺失的键" },
            rows,
            failures == 0
                ? $"{Loc.SupportedLanguages.Count} 个语言 x {textKeys.Count} 个文案键，全部命中"
                : $"共缺 {failures} 处，见上方失败行");

        return failures;
    }
}
