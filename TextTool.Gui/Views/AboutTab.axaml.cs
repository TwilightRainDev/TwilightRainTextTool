using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using TextTool.Gui.Localization;
using TextTool.Gui.Services;
using TextTool.Services;

namespace TextTool.Gui.Views;

/// <summary>
/// 关于页签：程序信息、作者与仓库链接、语言与深浅切换、配置导入导出、检查更新。
///
/// 本页不弹对话框：导出、导入、检查更新的结果都写在结果行上，导出导入另外报一句到状态栏。
/// 颜色一律取自主题资源，切主题后按新主题变体重取语义色画刷。
/// </summary>
public partial class AboutTab : UserControl
{
    /// <summary>配置导入导出只认这两个文件名，别的文件不进程序目录。</summary>
    private const string ConfigFileName = "app_config.json";

    private const string RulesFileName = "replace_rules.json";

    /// <summary>结果行当前显示的文案。存键与实参，切语言时按新语言重刷。</summary>
    private Message? _result;

    /// <summary>状态栏文字。由 MainWindow 订阅。</summary>
    public event Action<string>? StatusChanged;

    /// <summary>需要让用户知道出了错。由 MainWindow 显示。</summary>
    public event Action<string>? ErrorOccurred;

    public AboutTab()
    {
        InitializeComponent();
        SetupLanguageCombo();
        ApplyLocalization();

        // 切页签会把本页从可视树上摘下来，切回来再挂上，所以订阅跟着挂载走。
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;

        // 结果行的语义色与切换按钮的文案都是运行时赋的，切主题后按新状态重刷一次。
        ActualThemeVariantChanged += (_, _) =>
        {
            UpdateThemeButton();
            UpdateResultText();
        };
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        Loc.LanguageChanged -= ApplyLocalization;
        Loc.LanguageChanged += ApplyLocalization;
        ApplyLocalization();
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e) =>
        Loc.LanguageChanged -= ApplyLocalization;

    /// <summary>程序集版本，取 Major.Minor.Build，与旧页显示的一致。</summary>
    private static string CurrentVersion =>
        typeof(AboutTab).Assembly.GetName().Version is { } version
            ? $"{version.Major}.{version.Minor}.{version.Build}"
            : "0.0.0";

    // ================================================================
    //  文案
    // ================================================================

    private void ApplyLocalization()
    {
        AboutTitle.Text = Loc.T("AboutTitle");
        AboutVersion.Text = Loc.T("AboutVersion", CurrentVersion);
        AboutAuthor.Text = Loc.T("AboutAuthor");
        AboutRepositoryLink.Text = UpdateChecker.RepositoryUrl;
        AboutDescription.Text = Loc.T("AboutDescription");
        LanguageLabel.Text = Loc.T("AboutLanguage");
        ExportConfigButton.Content = Loc.T("BtnExportConfig");
        ImportConfigButton.Content = Loc.T("BtnImportConfig");
        CheckUpdateButton.Content = Loc.T("BtnCheckUpdate");

        UpdateThemeButton();
        UpdateResultText();
        SyncLanguageSelection();
    }

    /// <summary>按钮上写的是下一步要做的事：现在是深色，就提示切到浅色。</summary>
    private void UpdateThemeButton() =>
        ThemeButton.Content = AppSettings.DarkMode ? Loc.T("BtnLightMode") : Loc.T("BtnDarkMode");

    /// <summary>一条待显示的结果文案。存键与实参而不是成品字串，切语言时才能重算。</summary>
    private sealed record Message(string Key, object[] Args, bool Warning);

    private void SetResult(string key, params object[] args) =>
        SetResult(new Message(key, args, Warning: false));

    private void SetWarning(string key, params object[] args) =>
        SetResult(new Message(key, args, Warning: true));

    private void SetResult(Message? message)
    {
        _result = message;
        UpdateResultText();
    }

    private void UpdateResultText()
    {
        if (_result is not { } result)
        {
            ResultLabel.Text = string.Empty;
            ResultLabel.ClearValue(TextBlock.ForegroundProperty);
            return;
        }

        ResultLabel.Text = Loc.T(result.Key, result.Args);
        if (TryBrush(result.Warning ? "WarningFg" : "SuccessFg") is { } brush)
            ResultLabel.Foreground = brush;
        else
            ResultLabel.ClearValue(TextBlock.ForegroundProperty);
    }

    /// <summary>
    /// 取资源里的画刷。语义色在 ThemeDictionaries 下，必须带上当前主题变体去查：
    /// 不带变体的重载按 Default 查找，主题字典里的键一个也取不到。
    /// </summary>
    private IBrush? TryBrush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out object? value) && value is IBrush brush
            ? brush
            : null;

    // ================================================================
    //  语言
    // ================================================================

    private void SetupLanguageCombo()
    {
        // 显示名用各自的语言写（简体中文 / 繁體中文 / English），不随界面语言变，
        // 切换时也不会触发重入。
        LanguageCombo.ItemsSource = Loc.SupportedLanguages;
        LanguageCombo.SelectedItem = Loc.SupportedLanguages.First(l => l.Code == Loc.CurrentLang);
        LanguageCombo.SelectionChanged += OnLanguageSelected;
    }

    private void OnLanguageSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (LanguageCombo.SelectedItem is LanguageOption option && option.Code != Loc.CurrentLang)
            Loc.SetLanguage(option.Code);
    }

    /// <summary>把下拉框拨回当前语言。切语言后重刷文案时同样要拨一次。</summary>
    private void SyncLanguageSelection() =>
        LanguageCombo.SelectedItem = Loc.SupportedLanguages.First(l => l.Code == Loc.CurrentLang);

    // ================================================================
    //  深浅切换
    // ================================================================

    private void OnToggleTheme(object? sender, RoutedEventArgs e)
    {
        bool dark = !AppSettings.DarkMode;
        AppSettings.SetDarkMode(dark);

        if (Application.Current is { } app)
            app.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;

        // 按钮文字与结果行画刷跟主题状态走，切完按新状态重刷一遍。
        UpdateThemeButton();
        UpdateResultText();
    }

    // ================================================================
    //  仓库链接
    // ================================================================

    private async void OnOpenRepository(object? sender, PointerPressedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.Launcher is not { } launcher)
            return;

        try
        {
            await launcher.LaunchUriAsync(new Uri(UpdateChecker.RepositoryUrl));
        }
        catch (Exception error)
        {
            // 系统里没有能打开 https 的程序时 Launcher 会抛。这里没有对应的文案键，
            // 系统原文直接报到状态栏，不拿别处的文案顶替。
            ErrorOccurred?.Invoke(error.Message);
        }
    }

    // ================================================================
    //  配置导出 / 导入
    // ================================================================

    private async void OnExportConfig(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            return;

        IReadOnlyList<IStorageFolder> folders = await storage.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = Loc.T("ConfigExportDesc"),
                AllowMultiple = false,
            });

        string? target = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
        if (target is null)
            return;

        try
        {
            string sourceDir = AppContext.BaseDirectory;
            foreach (string name in new[] { ConfigFileName, RulesFileName })
            {
                string source = Path.Combine(sourceDir, name);
                if (File.Exists(source))
                    File.Copy(source, Path.Combine(target, name), overwrite: true);
            }

            SetResult("MsgConfigExported", target);
            StatusChanged?.Invoke(Loc.T("ConfigExported", target));
        }
        catch (Exception error)
        {
            SetWarning("MsgConfigExportFailed", error.Message);
            ErrorOccurred?.Invoke(Loc.T("MsgConfigExportFailed", error.Message));
        }
    }

    private async void OnImportConfig(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            return;

        IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = Loc.T("ConfigImportTitle"),
                AllowMultiple = true,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Config files")
                    {
                        Patterns = new[] { ConfigFileName, RulesFileName },
                    },
                },
            });

        var picked = files
            .Select(file => (Path: file.TryGetLocalPath(), Name: file.Name))
            .Where(item => item.Path is not null && IsConfigFile(item.Name))
            .ToList();

        if (picked.Count == 0)
            return;

        try
        {
            string targetDir = AppContext.BaseDirectory;
            foreach (var item in picked)
                File.Copy(item.Path!, Path.Combine(targetDir, item.Name), overwrite: true);

            // 先出结果，再切语言：切语言会重刷结果行，顺序反了就会先闪一条旧语言的。
            SetResult("MsgConfigImported");

            if (picked.Any(item => item.Name == ConfigFileName))
            {
                string? language = ReadLanguage(Path.Combine(targetDir, ConfigFileName));
                if (!string.IsNullOrEmpty(language))
                    Loc.SetLanguage(language);
            }

            StatusChanged?.Invoke(Loc.T("ConfigImported"));
        }
        catch (Exception error)
        {
            SetWarning("MsgConfigImportFailed", error.Message);
            ErrorOccurred?.Invoke(Loc.T("MsgConfigImportFailed", error.Message));
        }
    }

    /// <summary>只认这两个文件名：选的别的文件绝不会写进程序目录。</summary>
    private static bool IsConfigFile(string name) =>
        name.Equals(ConfigFileName, StringComparison.OrdinalIgnoreCase)
        || name.Equals(RulesFileName, StringComparison.OrdinalIgnoreCase);

    /// <summary>从导入的 app_config.json 里取语言偏好。读不出来就不动界面语言。</summary>
    private static string? ReadLanguage(string configPath)
    {
        try
        {
            if (!File.Exists(configPath))
                return null;

            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(configPath, Encoding.UTF8));
            return doc.RootElement.TryGetProperty("language", out JsonElement value)
                ? value.GetString()
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // ================================================================
    //  检查更新
    // ================================================================

    private async void OnCheckUpdate(object? sender, RoutedEventArgs e)
    {
        if (!CheckUpdateButton.IsEnabled)
            return;

        CheckUpdateButton.IsEnabled = false;
        string current = CurrentVersion;

        try
        {
            using HttpClient client = UpdateClient.Create(TimeSpan.FromSeconds(10));
            string? latest = await UpdateChecker.GetLatestVersionAsync(client);

            if (latest is null)
                SetWarning("UpdateCheckFailed");
            else if (UpdateChecker.IsNewer(current, latest))
                SetResult("UpdateAvailable", current, latest);
            else
                SetResult("UpdateUpToDate", current);
        }
        catch (Exception)
        {
            // 无网络、TLS 被拦、GitHub 不通都落成结果行的一行字，不让异常穿出去。
            SetWarning("UpdateCheckFailed");
        }
        finally
        {
            CheckUpdateButton.IsEnabled = true;
        }
    }
}
