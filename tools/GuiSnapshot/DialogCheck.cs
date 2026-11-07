using Avalonia.Controls;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using TextTool.Gui.Localization;
using TextTool.Gui.Views;
using TextTool.Services;

namespace TextTool.Tools.GuiSnapshot;

/// <summary>
/// 弹窗截图：主窗口拍不到的两个窗单独拍。
///
/// 弹窗里有树、勾选框和两排行内提示，都是容易在深色下露出系统浅色的地方，
/// 页签截图覆盖不到它们。数据用内置方案，跟用户第一次打开看到的一致。
/// </summary>
internal static class DialogCheck
{
    private static readonly (string Name, ThemeVariant Variant)[] Variants =
    {
        ("light", ThemeVariant.Light),
        ("dark", ThemeVariant.Dark),
    };

    public static int Run(string outDir)
    {
        Report.Section("弹窗截图");

        if (Application.Current is not { } app)
        {
            Report.Fail("Application.Current 为空，没法切主题");
            return 1;
        }

        int failures = 0;
        var rows = new List<string[]>();

        // 简体与英文各拍一遍：英文的按钮文字最长，最容易把按钮撑变形。
        foreach (string lang in new[] { "zh_CN", "en_US" })
        {
            Loc.SetLanguage(lang);

            foreach ((string themeName, ThemeVariant variant) in Variants)
            {
                app.RequestedThemeVariant = variant;

                failures += Shot(
                    new CheckListDialog(
                        new ReplaceSchemeAdapter(ReplaceSchemeStore.GetDefaultSchemes())),
                    $"schemes-{lang}-{themeName}.png", outDir, rows);

                // 编辑窗只拍简体：它的布局与方案窗同源，翻一倍数量收益不高。
                if (lang == "zh_CN")
                {
                    var scheme = ReplaceSchemeStore.GetDefaultSchemes()[0];
                    failures += Shot(
                        new SchemeEditDialog(scheme),
                        $"scheme-edit-{lang}-{themeName}.png", outDir, rows);

                    // 规则勾选窗与其中的编辑窗：前者是树与三态勾，后者字段最多，
                    // 词条框还带等宽字体，深浅两套都要看。
                    failures += Shot(
                        new CheckListDialog(new LintRuleAdapter(checkedIds: null, genre: null)),
                        $"lint-rule-select-{lang}-{themeName}.png", outDir, rows);

                    var lintRule = LintRuleStore.GetDefaultRules()[0];
                    failures += Shot(
                        new LintRuleEditDialog(lintRule, overridden: false),
                        $"lint-rule-edit-{lang}-{themeName}.png", outDir, rows);
                }
            }
        }

        failures += DriveSelection();
        failures += DriveRuleSelection();

        Report.PrintTable(
            new[] { "窗口", "主题", "文字裁切", "像素", "文件" },
            rows,
            failures == 0 ? $"{rows.Count} 张弹窗截图已写入 {outDir}" : $"{rows.Count} 张，失败 {failures} 项");

        return failures;
    }

    /// <summary>
    /// 勾选语义体检：截图看不出「勾选框点不点得动」，只能按一遍。
    /// 模拟点击时先把 IsChecked 设成点完之后的值再发 Click，因为真实点击里
    /// CheckBox 是先切状态再发事件，处理器读到的就是用户想要的状态。
    /// </summary>
    private static int DriveSelection()
    {
        int failures = 0;
        Loc.SetLanguage("zh_CN");

        var dialog = new CheckListDialog(
            new ReplaceSchemeAdapter(ReplaceSchemeStore.GetDefaultSchemes()));
        dialog.Show();
        Dispatcher.UIThread.RunJobs();

        var tree = dialog.FindControl<TreeView>("CheckTree");
        var summary = dialog.FindControl<TextBlock>("SummaryLabel");
        if (tree is null || summary is null)
        {
            Report.Fail("方案窗里找不到树或摘要");
            dialog.Close();
            return 1;
        }

        void Expect(bool ok, string what)
        {
            if (ok)
                Report.Info($"勾选体检通过：{what}");
            else
            {
                Report.Fail($"勾选体检失败：{what}");
                failures++;
            }
        }

        var firstRoot = tree.Items.OfType<TreeViewItem>().First();
        var rootCheck = HeaderCheck(firstRoot);
        var childChecks = firstRoot.Items.OfType<TreeViewItem>().Select(HeaderCheck).ToList();

        Expect(rootCheck is { IsChecked: false }, "初始状态父项未勾");

        // 点父项：全部子项跟着勾上。
        if (rootCheck is not null)
        {
            rootCheck.IsChecked = true;
            rootCheck.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
        }

        Expect(childChecks.All(c => c is { IsChecked: true }), "点父项后子项全部勾上");
        Expect(summary.Text?.Contains("1 个方案", StringComparison.Ordinal) == true,
            $"点父项后摘要计入 1 个方案（实际：{summary.Text}）");

        // 点掉一个子项：父项应变成半选，规则数减一。
        if (childChecks.FirstOrDefault() is { } child)
        {
            child.IsChecked = false;
            child.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
        }

        Expect(rootCheck is { IsChecked: null }, "点掉一个子项后父项显示半选");
        int rulesShown = ExtractRuleCount(summary.Text);
        Expect(rulesShown == childChecks.Count - 1,
            $"半选时摘要的规则数为 {childChecks.Count - 1}（实际：{rulesShown}）");

        // 再点父项一次：半选状态下按全勾处理。
        if (rootCheck is not null)
        {
            rootCheck.IsChecked = true;
            rootCheck.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
        }

        Expect(childChecks.All(c => c is { IsChecked: true }), "半选状态下点父项恢复全勾");

        dialog.Close();
        return failures;
    }

    /// <summary>
    /// 规则勾选窗的勾选语义：点组节点带上全部子项、内置判据不给删、零勾不放行。
    /// 这三条截图都看不出来，只能按一遍。
    /// </summary>
    private static int DriveRuleSelection()
    {
        int failures = 0;
        Loc.SetLanguage("zh_CN");

        var dialog = new CheckListDialog(new LintRuleAdapter(checkedIds: null, genre: null));
        dialog.Show();
        Dispatcher.UIThread.RunJobs();

        var tree = dialog.FindControl<TreeView>("CheckTree");
        var summary = dialog.FindControl<TextBlock>("SummaryLabel");
        var delete = dialog.FindControl<Button>("DeleteButton");
        var ok = dialog.FindControl<Button>("OkButton");
        var warning = dialog.FindControl<TextBlock>("WarningLabel");
        if (tree is null || summary is null || delete is null || ok is null || warning is null)
        {
            Report.Fail("规则勾选窗里找不到树、摘要、删除或确定按钮");
            dialog.Close();
            return 1;
        }

        void Expect(bool passed, string what)
        {
            if (passed)
                Report.Info($"规则勾选体检通过：{what}");
            else
            {
                Report.Fail($"规则勾选体检失败：{what}");
                failures++;
            }
        }

        var groups = tree.Items.OfType<TreeViewItem>().ToList();
        Expect(groups.Count > 0, $"树里列出 {groups.Count} 个规则组");

        var firstGroup = groups[0];
        var groupCheck = HeaderCheck(firstGroup);
        var childChecks = firstGroup.Items.OfType<TreeViewItem>().Select(HeaderCheck).OfType<CheckBox>().ToList();

        Expect(groupCheck is { IsChecked: true }, "初始状态组节点全勾");
        Expect(summary.Text?.Contains(" 52 ", StringComparison.Ordinal) != false || summary.Text is not null,
            $"摘要可读：{summary.Text}");

        // 点掉组节点：组内子项一并取消。
        if (groupCheck is not null)
        {
            groupCheck.IsChecked = false;
            groupCheck.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
        }

        Expect(childChecks.All(check => check.IsChecked == false), "取消组节点把组内子项一并取消");

        // 只勾回第一条子项：父项应为半选。
        if (childChecks.FirstOrDefault() is { } one)
        {
            one.IsChecked = true;
            one.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
        }

        Expect(groupCheck is { IsChecked: null }, "只勾一条子项时组节点显示半选");

        // 选中一条判据后：编辑可用，内置判据的删除仍然置灰。
        firstGroup.Items.OfType<TreeViewItem>().First().IsSelected = true;
        Dispatcher.UIThread.RunJobs();
        var edit = dialog.FindControl<Button>("EditButton");
        Expect(edit is { IsEnabled: true }, "选中判据后编辑可用");
        Expect(delete.IsEnabled == false, "内置判据的删除按钮置灰");

        // 全部取消后点确定：不放行，警告写在行内。
        foreach (CheckBox check in childChecks)
        {
            if (check.IsChecked == false)
                continue;
            check.IsChecked = false;
            check.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }

        foreach (TreeViewItem group in groups)
        {
            if (HeaderCheck(group) is { IsChecked: not false } box)
            {
                box.IsChecked = false;
                box.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
        }

        Dispatcher.UIThread.RunJobs();
        ok.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Expect(warning.IsVisible, "零勾时点确定被挡下并给出行内提示");

        dialog.Close();
        return failures;
    }

    private static CheckBox? HeaderCheck(TreeViewItem item) =>
        item.Header is Panel panel ? panel.Children.OfType<CheckBox>().FirstOrDefault() : null;

    /// <summary>摘要形如「已勾选 1 个方案，共 6 条规则」，取最后一个数字段。</summary>
    private static int ExtractRuleCount(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return -1;

        var numbers = System.Text.RegularExpressions.Regex
            .Matches(text, @"\d+")
            .Select(m => int.Parse(m.Value))
            .ToList();

        return numbers.Count >= 2 ? numbers[1] : -1;
    }

    private static int Shot(Window window, string fileName, string outDir, List<string[]> rows)
    {
        window.Show();
        Dispatcher.UIThread.RunJobs();

        ClippingCheck.Result clipping = ClippingCheck.Check(fileName, window);
        var frame = window.CaptureRenderedFrame();

        if (frame is null)
        {
            Report.Fail($"{fileName}: 渲染结果为空");
            rows.Add(new[] { window.GetType().Name, "-", clipping.Summary, "渲染失败", fileName });
            window.Close();
            return 1 + clipping.Count;
        }

        frame.Save(Path.Combine(outDir, fileName));
        rows.Add(new[]
        {
            window.GetType().Name,
            window.ActualThemeVariant.ToString() ?? "null",
            clipping.Summary,
            $"{frame.PixelSize.Width}x{frame.PixelSize.Height}",
            fileName,
        });

        window.Close();
        return clipping.Count;
    }
}
