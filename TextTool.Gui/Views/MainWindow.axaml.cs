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

        // 页签只负责干活，状态栏归窗口管。
        MergeTabView.StatusChanged += message => StatusText.Text = message;
        MergeTabView.ErrorOccurred += ShowError;
        MergeTabView.SetRules(LoadReplaceRules());
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

    /// <summary>
    /// 替换规则来自程序目录的 replace_rules.json。配置坏了不能静悄悄地当空规则跑，
    /// 这里先报到状态栏；把错误做成醒目提示留给 Step 4 的标点替换页。
    /// </summary>
    private List<TextTool.Services.ReplaceRule> LoadReplaceRules()
    {
        try
        {
            return TextTool.Services.ReplaceRuleStore.Load();
        }
        catch (InvalidDataException ex)
        {
            StatusText.Text = ex.Message;
            return new List<TextTool.Services.ReplaceRule>();
        }
    }

    private void ShowError(string message) => StatusText.Text = message;

    private void OnToggleTheme(object? sender, RoutedEventArgs e)
    {
        bool dark = !AppSettings.DarkMode;
        AppSettings.SetDarkMode(dark);

        if (Application.Current is { } app)
            app.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
    }
}
