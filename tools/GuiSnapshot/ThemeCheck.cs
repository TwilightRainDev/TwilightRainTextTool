using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace TextTool.Tools.GuiSnapshot;

/// <summary>
/// 主题键体检：Light 和 Dark 两个变体下，逐个键问资源宿主"有没有这一项"。
///
/// 为什么要查这个：ThemeDictionaries 里少一个键，视图上的 DynamicResource 不会抛异常，
/// 只会留空或者沿用上一个主题的旧刷子。这种缺陷在截图上看是"某块颜色有点怪"，
/// 用肉眼很难稳定发现，用 TryGetResource 一问就是确定的。
/// </summary>
internal static class ThemeCheck
{
    public static int Run()
    {
        Report.Section("主题键体检");

        if (Application.Current is not { } app)
        {
            Report.Fail("Application.Current 为空，主题资源无从查起");
            return 1;
        }

        var rows = new List<string[]>();
        var sameValueKeys = new List<string>();
        int missing = 0;

        foreach (string key in AxamlKeys.ThemeKeys)
        {
            bool lightOk = app.TryGetResource(key, ThemeVariant.Light, out object? lightValue);
            bool darkOk = app.TryGetResource(key, ThemeVariant.Dark, out object? darkValue);

            rows.Add(new[]
            {
                key,
                lightOk ? $"命中 {Describe(lightValue)}" : "缺失",
                darkOk ? $"命中 {Describe(darkValue)}" : "缺失",
            });

            if (!lightOk)
            {
                missing++;
                Report.Fail($"主题键 {key} 在 Light 变体下查不到");
            }

            if (!darkOk)
            {
                missing++;
                Report.Fail($"主题键 {key} 在 Dark 变体下查不到");
            }

            // 两边都在、取到的值却一样，多半是 Theme.axaml 里某个 ResourceInclude 指到了同一个文件。
            // 只比都命中的行：两边都缺失是上面已经报过的错，不能在这里再算一次。
            if (lightOk && darkOk && Describe(lightValue) == Describe(darkValue))
                sameValueKeys.Add(key);
        }

        if (sameValueKeys.Count > 0)
            Report.Fail($"{sameValueKeys.Count} 个主题键在 Light 和 Dark 下取了同一个值: {string.Join(", ", sameValueKeys)}，检查 Theme.axaml 的 ThemeDictionaries");

        int probes = AxamlKeys.ThemeKeys.Length * 2;

        Report.PrintTable(
            new[] { "键", "Light", "Dark" },
            rows,
            missing == 0 && sameValueKeys.Count == 0
                ? $"{AxamlKeys.ThemeKeys.Length} 个键 x 2 个变体，共查 {probes} 次，缺失 0 个，两套配色各不相同"
                : $"{AxamlKeys.ThemeKeys.Length} 个键 x 2 个变体，共查 {probes} 次，缺失 {missing} 个，两个变体同值 {sameValueKeys.Count} 个，见上方失败行");

        return missing + sameValueKeys.Count;
    }

    /// <summary>把资源值写成人看得懂的样子。颜色打 #RRGGBB，正好能核对浅深两套配色。</summary>
    private static string Describe(object? value) => value switch
    {
        ISolidColorBrush brush => brush.Color.ToString(),
        null => "null",
        _ => value.GetType().Name,
    };
}
