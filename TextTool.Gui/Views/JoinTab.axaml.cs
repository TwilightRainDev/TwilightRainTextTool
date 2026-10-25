using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using TextTool.Gui.Localization;
using TextTool.Services;

namespace TextTool.Gui.Views;

/// <summary>
/// 文件拼接页签：选一个文件夹，按匹配模式把文件夹里的文件依次拼成一个文件。
///
/// 拼接本身在 Core 的 FileJoiner 里，这里只管校验输入，并把进展与结果说出来。
/// 缺输入、文件夹不存在、拼接失败一律写结果行的行内文字，不弹窗：
/// 弹窗会抢焦点，也会盖掉上一次的结果。
/// </summary>
public partial class JoinTab : UserControl
{
    private const string DefaultPattern = "*.txt";
    private const string DefaultOutputName = "combined.txt";

    private bool _running;

    /// <summary>结果行的文案：存键与实参，切语言时按新语言重刷，不留旧语言的残句。</summary>
    private Message? _result;

    /// <summary>状态栏文字。由 MainWindow 订阅。</summary>
    public event Action<string>? StatusChanged;

    /// <summary>需要让用户知道出了错。由 MainWindow 显示。</summary>
    public event Action<string>? ErrorOccurred;

    public JoinTab()
    {
        InitializeComponent();

        // 这两个默认值是文件名与通配符，不属于界面文案，切语言时不重设，
        // 免得把用户已经改过的内容盖掉。
        PatternBox.Text = DefaultPattern;
        OutputBox.Text = DefaultOutputName;

        ApplyLocalization();

        // 切页签会把本页从可视树上摘下来，切回来再挂上，所以订阅跟着挂载走。
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        Loc.LanguageChanged -= ApplyLocalization;
        Loc.LanguageChanged += ApplyLocalization;
        ApplyLocalization();
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e) =>
        Loc.LanguageChanged -= ApplyLocalization;

    // ================================================================
    //  文案
    // ================================================================

    private void ApplyLocalization()
    {
        FolderLabel.Text = Loc.T("LabelFolder");
        BrowseButton.Content = Loc.T("BtnBrowse");
        PatternLabel.Text = Loc.T("LabelPattern");
        OutputLabel.Text = Loc.T("LabelOutputFile");
        JoinButton.Content = _running ? Loc.T("StatusJoining") : Loc.T("BtnJoin");

        if (_result is { } result)
            ResultLabel.Text = Loc.T(result.Key, result.Args);
    }

    /// <summary>一条待显示的结果文案。存键与实参而不是成品字串，切语言时才能重算。</summary>
    private sealed record Message(string Key, object[] Args);

    // ================================================================
    //  选文件夹
    // ================================================================

    private async void OnBrowse(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            return;

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Loc.T("TabJoin"),
            AllowMultiple = false,
            SuggestedStartLocation = await TryGetStartFolderAsync(storage),
        });

        string? path = folders
            .Select(folder => folder.TryGetLocalPath())
            .OfType<string>()
            .FirstOrDefault();

        if (path is not null)
            FolderBox.Text = path;
    }

    /// <summary>
    /// 对话框的起点取输入框当前那个文件夹。取不到就返回 null，
    /// 对话框自己落到系统默认位置，不为一个提示不出来的起点让浏览失败。
    /// </summary>
    private async Task<IStorageFolder?> TryGetStartFolderAsync(IStorageProvider storage)
    {
        string folder = (FolderBox.Text ?? string.Empty).Trim();
        if (!Directory.Exists(folder))
            return null;

        try
        {
            return await storage.TryGetFolderFromPathAsync(folder);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    // ================================================================
    //  执行拼接
    // ================================================================

    private async void OnJoin(object? sender, RoutedEventArgs e)
    {
        string folder = (FolderBox.Text ?? string.Empty).Trim();
        string pattern = (PatternBox.Text ?? string.Empty).Trim();
        string outputName = (OutputBox.Text ?? string.Empty).Trim();

        // 三项都在开跑前查一遍：报错写结果行，用户的输入原样留着。
        if (!Directory.Exists(folder))
        {
            ShowResult("MsgInvalidFolder", warning: true);
            return;
        }

        if (string.IsNullOrEmpty(pattern))
        {
            ShowResult("MsgEmptyPattern", warning: true);
            return;
        }

        if (string.IsNullOrEmpty(outputName))
        {
            ShowResult("MsgEmptyOutput", warning: true);
            return;
        }

        _running = true;
        JoinButton.IsEnabled = false;
        JoinButton.Content = Loc.T("StatusJoining");
        StatusChanged?.Invoke(Loc.T("StatusJoining"));

        try
        {
            // 拼接要读完整个文件夹，放到线程池里跑，界面不跟着卡。
            var (outputPath, fileCount) = await Task.Run(
                () => FileJoiner.Join(folder, pattern, outputName));

            string message = ShowResult("StatusJoinComplete", warning: false, fileCount, outputPath);
            StatusChanged?.Invoke(message);
        }
        catch (Exception ex)
        {
            string message = ShowResult("MsgJoinFailed", warning: true, ex.Message);
            ErrorOccurred?.Invoke(message);
        }
        finally
        {
            _running = false;
            JoinButton.IsEnabled = true;
            JoinButton.Content = Loc.T("BtnJoin");
        }
    }

    /// <summary>
    /// 写结果行并返回写进去的文字，调用方可以直接拿它发给状态栏。
    /// 成功绿、失败橙，两个类互斥，切换时不会残留上一次的颜色。
    /// </summary>
    private string ShowResult(string key, bool warning, params object[] args)
    {
        _result = new Message(key, args);

        string text = Loc.T(key, args);
        ResultLabel.Text = text;
        ResultLabel.Classes.Set("success", !warning);
        ResultLabel.Classes.Set("warning", warning);
        return text;
    }
}
