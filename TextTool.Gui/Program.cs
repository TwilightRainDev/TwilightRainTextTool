using Avalonia;

namespace TextTool.Gui;

internal static class Program
{
    // Avalonia 自己处理 DPI（布局单位是 1/96 英寸），不需要 WinForms 那套
    // AutoScaleMode。AppBuilder 的初始化必须早于任何可见控件。
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
