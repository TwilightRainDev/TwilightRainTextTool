using Avalonia;
using Avalonia.Headless;
using TextTool.Gui;
using TextTool.Gui.Localization;

namespace TextTool.Tools.GuiSnapshot;

/// <summary>
/// Step 2 验收工具：离屏把主窗口渲染成 PNG，并做两项资源体检。
///
/// 用法：dotnet run --project tools/GuiSnapshot -- &lt;输出目录&gt;
///
/// 做的六件事：
///   1. 主题键体检：Light / Dark 下逐个键查 ThemeDictionaries 有没有漏。
///   2. 配色对比度体检：关键配对的 WCAG 对比度是否达到正文 4.5:1、提示 3:1。
///   3. 截图矩阵：3 个语言 x 2 个主题，共六张 语言-主题.png，同时量页签会不会折行，
///      并对同一个窗口查一遍文字裁切（控件的真实需要尺寸比实际 Bounds 大就是被裁）。
///   4. 换语言热切换体检：同一个窗口连着换语言，看 DynamicResource 有没有跟着重取。
///   5. 交互体检：驱动标点替换页按按钮，读回磁盘上的结果比对。
///   6. 文案键体检：.axaml 里的 {DynamicResource 键名} 和 .cs 里的 Loc.T("键名") 合并后，
///      每个文案键三种语言都得给得出值。
///
/// 退出码 0 表示全部通过，非 0 表示有失败项。
///
/// 存在的理由：上一轮缺陷全是靠肉眼比对截图发现的，而正常运行程序验图会在开发伙伴的
/// 桌面上弹窗抢焦点。headless 加 Skia 走的是同一套渲染路径，只是画到内存。
/// </summary>
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        string outDir = args.Length > 0 && !args[0].StartsWith("--", StringComparison.Ordinal)
            ? args[0]
            : "snapshots";

        // --tab N：先切到第 N 个页签再拍。默认 0，即第一个页签。
        int tabIndex = 0;
        int flag = Array.IndexOf(args, "--tab");
        if (flag >= 0 && flag + 1 < args.Length && int.TryParse(args[flag + 1], out int parsed))
            tabIndex = parsed;

        int failures = 0;

        try
        {
            Directory.CreateDirectory(outDir);

            AppBuilder.Configure<App>()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();

            // 不读磁盘上的偏好：验收要一个确定的起点，否则本机 app_config.json 里的语言和主题
            // 会悄悄改变这里量出来的结果。
            Loc.Init(null);

            failures += ThemeCheck.Run();
            failures += ContrastCheck.Run();
            failures += SnapshotCheck.Run(outDir, tabIndex);
            failures += SnapshotCheck.HotSwitch();
            failures += DialogCheck.Run(outDir);
            failures += PreviewCheck.Run();
            failures += ReplaceTabDriveCheck.Run();
            failures += TextKeyCheck.Run();
        }
        catch (Exception error)
        {
            Report.Fail($"未处理异常: {error.GetType().Name}: {error.Message}");
            Console.Error.WriteLine(error);
            failures++;
        }

        Console.WriteLine();
        if (failures == 0)
        {
            Console.WriteLine("[结果] 全部通过，退出码 0");
            return 0;
        }

        Console.WriteLine($"[结果] 失败 {failures} 项，退出码 1");
        return 1;
    }
}
