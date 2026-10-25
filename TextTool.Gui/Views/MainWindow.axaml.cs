using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;
using TextTool.Gui.Services;

namespace TextTool.Gui.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnToggleTheme(object? sender, RoutedEventArgs e)
    {
        bool dark = !AppSettings.DarkMode;
        AppSettings.SetDarkMode(dark);

        if (Application.Current is { } app)
            app.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
    }
}
