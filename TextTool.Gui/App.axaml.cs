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
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

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
