using System.Text;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using TextTool.Gui.Localization;
using TextTool.Gui.Services;
using TextTool.Gui.Views;

namespace TextTool.Gui;

public partial class App : Application
{
    public override void Initialize()
    {
        // 代码页要在这里注册：GBK 之类的编码不在 .NET 默认清单里，不注册的话
        // 读非 UTF-8 文件时 GetEncoding 直接抛异常，界面上什么都不发生。
        // 放在 Application 初始化里而不是入口的 Main：验收工具走的是同一个 App，
        // 这样两边环境一致，少注册一次就会在体检里暴露。
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            AppSettings.Load();
            RequestedThemeVariant = AppSettings.DarkMode ? ThemeVariant.Dark : ThemeVariant.Light;
            // 文案要在建窗口之前挂上，否则首帧全是 [键名]。
            Loc.Init(AppSettings.Language);
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
