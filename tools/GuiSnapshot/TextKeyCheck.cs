using Avalonia;
using TextTool.Gui.Localization;

namespace TextTool.Tools.GuiSnapshot;

/// <summary>
/// 文案键体检：代码里引用的每个文案键，三种语言都得给得出值。
///
/// 键有两个来源，缺一不可：
///   - .axaml 里的 {DynamicResource 键名}（静态布局）；
///   - .cs 里的 Loc.T("键名")（页签代码里赋的文字，axaml 里看不到）。
/// 两边合并去重后再逐个语言验。
///
/// 为什么要排除名单：资源宿主里既有主题色键，也有 AppFontSize 这类非文案键，
/// 只问"查不查得到"是分不出文案和配色的。所以先用文本扫描定出"引用了哪些键"，
/// 再用 AxamlKeys.IsNonText 剔掉非文案的，剩下的逐个语言验。
///
/// 缺键在界面上表现为空白或 [键名]，不会抛异常，是最容易静默漏过去的一类缺陷。
/// </summary>
internal static class TextKeyCheck
{
    /// <summary>一个文案键被引用的地方：来自哪边、首次出现的位置（代码带行号）。</summary>
    private sealed record KeyRef(bool InAxaml, bool InCode, string References)
    {
        public string Origin => InAxaml && InCode ? "两边" : InAxaml ? "axaml" : "代码";
    }

    public static int Run()
    {
        Report.Section("文案键体检");

        string? guiDir = AxamlKeys.FindGuiSourceDir();
        if (guiDir is null)
        {
            Report.Fail("找不到 TextTool.Gui 源码目录，文案键无从检查（不接受跳过）");
            return 1;
        }

        SortedDictionary<string, string> axamlKeys = AxamlKeys.Scan(guiDir, out List<string> axamlFiles);
        LocScanResult code = LocKeys.Scan(guiDir);

        SortedDictionary<string, KeyRef> merged = Merge(axamlKeys, code.Keys);

        int excludedTheme = merged.Keys.Count(AxamlKeys.ThemeKeys.Contains);
        int excludedMisc = merged.Keys.Count(k => AxamlKeys.NonTextKeys.Contains(k));
        var textKeys = merged.Where(pair => !AxamlKeys.IsNonText(pair.Key))
            .Select(pair => (Key: pair.Key, Ref: pair.Value))
            .ToList();

        int onlyAxaml = merged.Values.Count(pair => pair.InAxaml && !pair.InCode);
        int onlyCode = merged.Values.Count(pair => pair.InCode && !pair.InAxaml);
        int both = merged.Values.Count(pair => pair.InAxaml && pair.InCode);

        Report.Info($"扫描 {guiDir}");
        Report.Info($".axaml: {axamlFiles.Count} 个文件，引用 {axamlKeys.Count} 个键");
        Report.Info(
            $".cs: {code.Files.Count} 个文件，引用 {code.Keys.Count} 个键" +
            $"（跳过非字面量调用 {code.Skipped.Count} 处，注释里出现的 Loc.T {code.CommentMentions} 处不计）");
        Report.Info($"合并去重后共引用 {merged.Count} 个键（含主题色与字体键）：仅 axaml {onlyAxaml} 个，仅代码 {onlyCode} 个，两边都有 {both} 个");
        Report.Info($"排除主题色键 {excludedTheme} 个，字号字体键 {excludedMisc} 个，剩文案键 {textKeys.Count} 个");

        foreach (SkippedCall skipped in code.Skipped)
            Report.Info($"跳过 {skipped.Where} 的 Loc.T: {skipped.Reason}");

        if (textKeys.Count == 0)
        {
            // 扫描器自己坏掉时不能让检查空过：零个键的"全部通过"是假绿。
            Report.Fail("一个文案键都没扫到，扫描逻辑可能失效");
            return 1;
        }

        // 代码侧一个键都没扫到，多半是 Loc.T 的写法变了，扫描器对不上，同样不接受空过。
        if (code.Keys.Count == 0)
        {
            Report.Fail("代码里一个 Loc.T 字面量键都没扫到，代码扫描逻辑可能失效");
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

        Report.PrintTable(
            new[] { "文案键", "来源", "引用位置" },
            textKeys.Select(pair => new[] { pair.Key, pair.Ref.Origin, pair.Ref.References }).ToList(),
            $"{textKeys.Count} 个文案键：axaml 独有 {textKeys.Count(p => !p.Ref.InCode)} 个，" +
            $"代码独有 {textKeys.Count(p => !p.Ref.InAxaml)} 个");

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
                Report.Fail($"{language.Code} 缺文案键 {pair.Key}（被 {pair.Ref.References} 引用）");

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

    /// <summary>
    /// 合并两边扫到的键。同一个键可能两处都引用，来源要都记下来，
    /// 缺键时才知道该去 axaml 还是去代码里找。
    /// </summary>
    private static SortedDictionary<string, KeyRef> Merge(
        SortedDictionary<string, string> axamlKeys,
        SortedDictionary<string, string> codeKeys)
    {
        var merged = new SortedDictionary<string, KeyRef>(StringComparer.Ordinal);

        foreach (var pair in axamlKeys)
            merged[pair.Key] = new KeyRef(InAxaml: true, InCode: false, $"axaml {pair.Value}");

        foreach (var pair in codeKeys)
        {
            merged[pair.Key] = merged.TryGetValue(pair.Key, out KeyRef? existing)
                ? existing with { InCode = true, References = $"{existing.References}；代码 {pair.Value}" }
                : new KeyRef(InAxaml: false, InCode: true, $"代码 {pair.Value}");
        }

        return merged;
    }
}
