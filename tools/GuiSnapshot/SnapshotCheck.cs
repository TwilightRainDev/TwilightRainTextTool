using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using TextTool.Gui.Localization;
using TextTool.Gui.Views;

namespace TextTool.Tools.GuiSnapshot;

/// <summary>
/// 截图矩阵：3 个语言 x 2 个主题，一共六张图，文件名是 语言-主题.png。
///
/// 语言用 Loc.SetLanguage 切，主题用 Application.Current.RequestedThemeVariant 切，
/// Application 自始至终只有一份。这么做是故意的：换语言之后 DynamicResource 应该自己
/// 重取新文案，重建 Application 会把这条链路的缺陷一起盖住。
/// 换语言能不能热生效，另外由 HotSwitch 用一个一直开着的窗口单独验。
/// </summary>
internal static class SnapshotCheck
{
    private static readonly (string Name, ThemeVariant Variant)[] Variants =
    {
        ("light", ThemeVariant.Light),
        ("dark", ThemeVariant.Dark),
    };

    /// <summary>
    /// 六张图，每张图后面跟两次体检：页签条会不会折行，窗口里有没有文字被裁。
    /// 两者都是布局结论，所以 --tab N 切到别的页签时同样生效。
    /// </summary>
    public static int Run(string outDir, int tabIndex = 0)
    {
        Report.Section("截图矩阵");

        if (Application.Current is not { } app)
        {
            Report.Fail("Application.Current 为空，没法切主题");
            return 1;
        }

        int failures = 0;
        var rows = new List<string[]>();

        foreach (LanguageOption language in Loc.SupportedLanguages)
        {
            Loc.SetLanguage(language.Code);

            foreach ((string themeName, ThemeVariant variant) in Variants)
            {
                app.RequestedThemeVariant = variant;

                // 每个组合都新建窗口：窗口在构造时就按当时的语言和主题取了一次资源，
                // 复用旧窗口验不到"新建窗口拿到的是新语言"。
                var window = new MainWindow();
                window.Show();
                Dispatcher.UIThread.RunJobs();

                // 默认停在第一个页签。要看别的页签时用 --tab 指定，
                // 否则关于页那种带下拉框的控件永远拍不到。
                if (tabIndex > 0 && window.FindControl<TabControl>("Tabs") is { } tabs)
                {
                    tabs.SelectedIndex = tabIndex;
                    Dispatcher.UIThread.RunJobs();
                }

                string fileName = tabIndex > 0
                    ? $"{language.Code}-{themeName}-tab{tabIndex}.png"
                    : $"{language.Code}-{themeName}.png";
                string actualVariant = window.ActualThemeVariant.ToString() ?? "null";
                bool variantOk = actualVariant.Equals(
                    variant.ToString(), StringComparison.OrdinalIgnoreCase);

                if (!variantOk)
                {
                    Report.Fail($"{fileName}: 请求 {variant}，实际 {actualVariant}");
                    failures++;
                }

                (int layoutFailures, double wanted, double available) = CheckTabs(fileName, window);
                failures += layoutFailures;

                var frame = window.CaptureRenderedFrame();

                // 拍完图对同一个窗口做一次文字裁切体检。它只看布局量出来的尺寸，跟像素无关，
                // 所以拍图失败时也照验，免得出图的问题把布局问题一起盖住。
                ClippingCheck.Result clipping = ClippingCheck.Check(fileName, window);
                failures += clipping.Count;

                if (frame is null)
                {
                    Report.Fail($"{fileName}: 渲染结果为空");
                    failures++;
                    rows.Add(new[]
                    {
                        language.Code, themeName, actualVariant,
                        $"{wanted:F1}/{available:F1}", layoutFailures == 0 ? "装得下" : "折行",
                        clipping.Summary, "渲染失败", fileName,
                    });
                    window.Close();
                    continue;
                }

                string path = Path.Combine(outDir, fileName);
                frame.Save(path);
                string pixels = $"{frame.PixelSize.Width}x{frame.PixelSize.Height}";

                rows.Add(new[]
                {
                    language.Code, themeName, actualVariant,
                    $"{wanted:F1}/{available:F1}", layoutFailures == 0 ? "装得下" : "折行",
                    clipping.Summary, pixels, fileName,
                });

                window.Close();
            }
        }

        Report.PrintTable(
            new[] { "语言", "主题", "实际变体", "页签合宽/可用", "页签", "文字裁切", "像素", "文件" },
            rows,
            failures == 0
                ? $"{Loc.SupportedLanguages.Count} 个语言 x {Variants.Length} 个主题，{rows.Count} 张图已写入 {outDir}，布局与文字裁切全通过"
                : $"{rows.Count} 张图，失败 {failures} 项，见上方失败行");

        return failures;
    }

    /// <summary>
    /// 页签条需要的宽度超过可用宽度，就说明会折行或者标题被截。像素上看不出来，布局阶段是一句话的事。
    /// 页签宽度随语言变长，所以每个组合都要单独量。
    /// </summary>
    private static (int Failures, double Wanted, double Available) CheckTabs(string label, MainWindow window)
    {
        if (window.FindControl<TabControl>("Tabs") is not { } tab)
        {
            Report.Fail($"{label}: 找不到页签控件 Tabs");
            return (1, 0, 0);
        }

        double wanted = 0;
        for (int i = 0; i < tab.ItemCount; i++)
        {
            if (tab.ContainerFromIndex(i) is not Control container)
            {
                Report.Info($"{label}: 第 {i} 个页签还没有容器");
                continue;
            }

            container.Measure(Size.Infinity);
            wanted += container.DesiredSize.Width;
        }

        double available = tab.Bounds.Width;
        if (wanted > available)
        {
            Report.Fail($"{label}: 页签要 {wanted:F1} 宽，只有 {available:F1}，会折行");
            return (1, wanted, available);
        }

        return (0, wanted, available);
    }

    /// <summary>
    /// 热切换体检：一个窗口开着不动，连着换三种语言，看绑了 DynamicResource 的标题和状态栏
    /// 有没有跟着变。Application 实例在前后必须是同一个，否则这条检查没有意义。
    /// </summary>
    public static int HotSwitch()
    {
        Report.Section("换语言热切换体检");

        if (Application.Current is not { } app)
        {
            Report.Fail("Application.Current 为空");
            return 1;
        }

        object appBefore = app;

        var window = new MainWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();

        int failures = 0;
        var rows = new List<string[]>();

        foreach (LanguageOption language in Loc.SupportedLanguages)
        {
            Loc.SetLanguage(language.Code);
            Dispatcher.UIThread.RunJobs();

            string expectedTitle = Loc.T("AppTitle");
            string actualTitle = window.Title ?? string.Empty;
            string expectedStatus = Loc.T("StatusReady");
            string actualStatus = window.FindControl<TextBlock>("StatusText")?.Text ?? string.Empty;

            bool titleOk = expectedTitle == actualTitle && !expectedTitle.StartsWith('[');
            bool statusOk = expectedStatus == actualStatus && !expectedStatus.StartsWith('[');

            if (!titleOk)
            {
                Report.Fail($"{language.Code}: 标题应为 {expectedTitle}，实际 {actualTitle}");
                failures++;
            }

            if (!statusOk)
            {
                Report.Fail($"{language.Code}: 状态栏应为 {expectedStatus}，实际 {actualStatus}");
                failures++;
            }

            rows.Add(new[]
            {
                language.Code,
                titleOk ? actualTitle : $"{actualTitle}（不对）",
                statusOk ? actualStatus : $"{actualStatus}（不对）",
                titleOk && statusOk ? "通过" : "失败",
            });
        }

        window.Close();

        Report.PrintTable(
            new[] { "语言", "窗口标题", "状态栏", "结果" },
            rows,
            failures == 0
                ? $"共用同一个 Application 和窗口，{rows.Count} 次切换后 DynamicResource 全部重取"
                : $"失败 {failures} 项");

        if (!ReferenceEquals(appBefore, Application.Current))
        {
            Report.Fail("Application 实例被重建了，热切换结论不成立");
            failures++;
        }

        return failures;
    }
}
