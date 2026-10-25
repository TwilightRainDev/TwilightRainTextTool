using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;
using TextTool.Gui.Localization;
using TextTool.Gui.Services;

namespace TextTool.Gui.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        SetupLanguageCombo();
    }

    private void SetupLanguageCombo()
    {
        // 显示名用各自的语言写（简体中文 / 繁體中文 / English），不随界面语言变，
        // 也就不会在切换时触发重入。
        LanguageCombo.ItemsSource = Loc.SupportedLanguages;
        LanguageCombo.SelectedItem = Loc.SupportedLanguages.First(l => l.Code == Loc.CurrentLang);
        LanguageCombo.SelectionChanged += OnLanguageSelected;
    }

    private void OnLanguageSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (LanguageCombo.SelectedItem is LanguageOption option && option.Code != Loc.CurrentLang)
            Loc.SetLanguage(option.Code);
    }

    private void OnToggleTheme(object? sender, RoutedEventArgs e)
    {
        bool dark = !AppSettings.DarkMode;
        AppSettings.SetDarkMode(dark);

        if (Application.Current is { } app)
            app.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
    }
}
