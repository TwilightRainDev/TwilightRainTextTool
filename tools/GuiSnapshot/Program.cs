using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using TextTool.Gui;
using TextTool.Gui.Views;

namespace TextTool.Tools.GuiSnapshot;

/// <summary>
/// 离屏截图工具：在进程里把主窗口渲染成 PNG，不开任何可见窗口。
///
/// 用法：dotnet run --project tools/GuiSnapshot -- &lt;输出目录&gt;
/// 输出：&lt;输出目录&gt;/light.png 和 dark.png，同时打印布局体检结果。
///
/// 存在的理由：上一轮缺陷全是靠肉眼比对截图发现的，而正常运行程序验图会在开发
/// 伙伴的桌面上弹窗抢焦点。headless 加 Skia 走的是同一套渲染路径，只是画到内存。
/// </summary>
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        string outDir = args.Length > 0 ? args[0] : "snapshots";
        Directory.CreateDirectory(outDir);

        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();

        int failures = 0;
        foreach ((string name, ThemeVariant variant) in new[]
                 {
                     ("light", ThemeVariant.Light),
                     ("dark", ThemeVariant.Dark),
                 })
        {
            Application.Current!.RequestedThemeVariant = variant;

            var window = new MainWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();

            failures += Report(name, window);

            var frame = window.CaptureRenderedFrame();
            if (frame is null)
            {
                Console.Error.WriteLine($"[失败] {name}: 渲染结果为空");
                failures++;
            }
            else
            {
                string path = Path.Combine(outDir, $"{name}.png");
                frame.Save(path);
                Console.WriteLine($"[渲染] {name}: {frame.PixelSize.Width}x{frame.PixelSize.Height} -> {path}");
            }

            window.Close();
        }

        return failures == 0 ? 0 : 1;
    }

    /// <summary>
    /// 布局体检：页签条需要的宽度超过可用宽度，就说明会折行或标题被截。
    /// 这类判断在像素上不好量，在布局阶段是一句话的事。
    /// </summary>
    private static int Report(string name, MainWindow window)
    {
        if (window.FindControl<TabControl>("Tabs") is not { } tab)
        {
            Console.Error.WriteLine($"[失败] {name}: 找不到页签控件 Tabs");
            return 1;
        }

        double wanted = 0;
        for (int i = 0; i < tab.ItemCount; i++)
        {
            if (tab.ContainerFromIndex(i) is not Control container)
            {
                Console.Error.WriteLine($"[警告] {name}: 第 {i} 个页签还没有容器");
                continue;
            }

            container.Measure(Size.Infinity);
            wanted += container.DesiredSize.Width;
        }

        double available = tab.Bounds.Width;
        Console.WriteLine($"[布局] {name}: 页签合宽 {wanted:F1}，可用 {available:F1}，窗口 {window.Width:F0}x{window.Height:F0}");

        if (wanted > available)
        {
            Console.Error.WriteLine($"[失败] {name}: 页签要 {wanted:F1} 宽，只有 {available:F1}，会折行");
            return 1;
        }

        return 0;
    }
}
