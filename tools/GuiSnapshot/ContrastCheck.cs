using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace TextTool.Tools.GuiSnapshot;

/// <summary>
/// 配色对比度体检：Light 和 Dark 两个变体下，算关键配对是否符合 WCAG 2.x 对比度门槛。
///
/// 为什么查这个：主题色是手挑的十六进制值，改一个数就可能让次要文字落到门槛以下。
/// 这种缺陷截图上只是"看着有点灰"，人眼在深浅两套配色、三种语言之间比对不稳定，
/// 而两个颜色一张一比就是确定的结论。
///
/// 取色走 Application 的资源宿主并带上主题变体：语义色在 ThemeDictionaries 下，
/// 不带变体的重载按 Default 查，一个也查不到。亮度按 sRGB 分段公式自己算，不引第三方包。
/// </summary>
internal static class ContrastCheck
{
    /// <summary>正文类门槛。WCAG AA 对正文的要求。</summary>
    private const double BodyMinimum = 4.5;

    /// <summary>提示类门槛。WCAG AA 对大字号与非文字周边的要求。</summary>
    private const double HintMinimum = 3.0;

    /// <summary>一个被测配色组合：前景键、背景键、门槛、类别。</summary>
    private sealed record Pair(string Foreground, string Background, double Minimum, string Kind);

    /// <summary>正文类是页面上真正阅读的文字，提示类是状态栏、警告一类的辅助文字。</summary>
    private static readonly Pair[] Pairs =
    {
        new("Fg", "Bg", BodyMinimum, "正文"),
        new("MutedFg", "Bg", BodyMinimum, "正文"),
        new("ActionFg", "ActionBg", BodyMinimum, "正文"),
        new("Fg", "ControlBg", BodyMinimum, "正文"),
        new("MutedFg", "StatusBg", HintMinimum, "提示"),
        new("WarningFg", "Bg", HintMinimum, "提示"),
    };

    private static readonly (string Name, ThemeVariant Variant)[] Variants =
    {
        ("Light", ThemeVariant.Light),
        ("Dark", ThemeVariant.Dark),
    };

    public static int Run()
    {
        Report.Section("配色对比度体检");

        if (Application.Current is not { } app)
        {
            Report.Fail("Application.Current 为空，主题色无从查起");
            return 1;
        }

        var rows = new List<string[]>();
        int failures = 0;

        foreach ((string variantName, ThemeVariant variant) in Variants)
        {
            foreach (Pair pair in Pairs)
            {
                Color? foreground = ColorOf(app, pair.Foreground, variant);
                Color? background = ColorOf(app, pair.Background, variant);

                if (foreground is null || background is null)
                {
                    string missing = foreground is null ? pair.Foreground : pair.Background;
                    Report.Fail($"{variantName} {pair.Foreground}/{pair.Background}: 主题键 {missing} 取不到");
                    failures++;
                    rows.Add(new[]
                    {
                        variantName, $"{pair.Foreground}/{pair.Background}",
                        "-", "-", pair.Kind, $"{pair.Minimum:F1}:1", "取色失败",
                    });
                    continue;
                }

                double ratio = Contrast(foreground.Value, background.Value);
                bool ok = ratio >= pair.Minimum;

                if (!ok)
                {
                    Report.Fail(
                        $"{variantName} {pair.Foreground}/{pair.Background} 对比度 {ratio:F2}:1 "
                        + $"低于 {pair.Minimum:F1}:1（{pair.Kind}）");
                    failures++;
                }

                rows.Add(new[]
                {
                    variantName,
                    $"{pair.Foreground}/{pair.Background}",
                    foreground.Value.ToString(),
                    background.Value.ToString(),
                    pair.Kind,
                    $"{pair.Minimum:F1}:1",
                    $"{ratio:F2}:1 {(ok ? "通过" : "不达标")}",
                });
            }
        }

        Report.PrintTable(
            new[] { "主题", "配对", "前景", "背景", "类别", "门槛", "实测" },
            rows,
            failures == 0
                ? $"{Variants.Length} 个主题 x {Pairs.Length} 组配对，共 {rows.Count} 次计算，全部达标"
                : $"{rows.Count} 次计算，{failures} 组不达标，见上方失败行");

        return failures;
    }

    private static Color? ColorOf(Application app, string key, ThemeVariant variant) =>
        app.TryFindResource(key, variant, out object? value) && value is ISolidColorBrush brush
            ? brush.Color
            : null;

    /// <summary>WCAG 对比度：(较亮 + 0.05) / (较暗 + 0.05)，范围 1 到 21。</summary>
    private static double Contrast(Color a, Color b)
    {
        double first = RelativeLuminance(a);
        double second = RelativeLuminance(b);
        return first > second
            ? (first + 0.05) / (second + 0.05)
            : (second + 0.05) / (first + 0.05);
    }

    /// <summary>相对亮度：三个分量各自线性化后按 0.2126 / 0.7152 / 0.0722 加权。</summary>
    private static double RelativeLuminance(Color color) =>
        0.2126 * Linearize(color.R) + 0.7152 * Linearize(color.G) + 0.0722 * Linearize(color.B);

    /// <summary>sRGB 分段线性化：低端是直线段，高端是指数为 2.4 的幂函数。</summary>
    private static double Linearize(byte channel)
    {
        double value = channel / 255.0;
        return value <= 0.03928
            ? value / 12.92
            : Math.Pow((value + 0.055) / 1.055, 2.4);
    }
}
