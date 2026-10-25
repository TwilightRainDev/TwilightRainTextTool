using Avalonia.Controls;
using Avalonia;
using Avalonia.Headless;
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
                    new SchemeSelectionDialog(ReplaceSchemeStore.GetDefaultSchemes()),
                    $"schemes-{lang}-{themeName}.png", outDir, rows);

                // 编辑窗只拍简体：它的布局与方案窗同源，翻一倍数量收益不高。
                if (lang == "zh_CN")
                {
                    var scheme = ReplaceSchemeStore.GetDefaultSchemes()[0];
                    failures += Shot(
                        new SchemeEditDialog(scheme),
                        $"scheme-edit-{lang}-{themeName}.png", outDir, rows);
                }
            }
        }

        Report.PrintTable(
            new[] { "窗口", "主题", "文字裁切", "像素", "文件" },
            rows,
            failures == 0 ? $"{rows.Count} 张弹窗截图已写入 {outDir}" : $"{rows.Count} 张，失败 {failures} 项");

        return failures;
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
