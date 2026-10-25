using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using TextTool.Gui.Localization;
using TextTool.Services;

namespace TextTool.Gui.Views;

/// <summary>
/// AI 味检查页签：文件或粘贴 → LintRunner → 只读人读报告。不写文件。
///
/// 报告里每一个字都由 <see cref="ILintText"/> 现取，本页不拼接报告正文，
/// 所以换语言后旧报告就是旧语言，必须按新语言重扫一遍（见 <see cref="RerenderReportAsync"/>）。
/// </summary>
public partial class LintTab : UserControl
{
    /// <summary>
    /// 粘贴内容的来源名，与命令行一致。人读报告不打印文件名，它只落在报告模型的 File 上，
    /// 与按文件跑出来的报告保持同一种形状。
    /// </summary>
    private const string PasteSource = "(paste)";

    private readonly List<string> _selectedFiles = new();

    /// <summary>上一次扫过的输入与选项。换语言时拿它重扫，不重新读盘。</summary>
    private List<(string File, string Text)>? _lastInputs;
    private string? _lastMinSeverity;

    /// <summary>动态文案记下键与参数，切语言时按新语言重刷。</summary>
    private Message? _result;
    private Message? _warning;

    private bool _running;

    /// <summary>每发起一次新扫描或重渲染就加一，用来丢弃已经过期的后台结果。</summary>
    private int _generation;

    /// <summary>状态栏文字。由 MainWindow 订阅。</summary>
    public event Action<string>? StatusChanged;

    /// <summary>需要让用户知道出了错。由 MainWindow 显示。</summary>
    public event Action<string>? ErrorOccurred;

    public LintTab()
    {
        InitializeComponent();
        ApplyLocalization();

        // 订阅挂在 Loaded 上、退订挂在 Unloaded 上：页签被移出视觉树后不再收语言切换的通知，
        // 回来时重新挂一次，避免同一处回调挂两遍。
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        Loc.LanguageChanged -= ApplyLocalization;
        Loc.LanguageChanged += ApplyLocalization;
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e) =>
        Loc.LanguageChanged -= ApplyLocalization;

    // ================================================================
    //  文案
    // ================================================================

    private void ApplyLocalization()
    {
        SourceLabel.Text = Loc.T("LabelSourceFile");
        BrowseButton.Content = Loc.T("BtnBrowse");
        PasteLabel.Text = Loc.T("LabelLintPaste");
        SeverityLabel.Text = Loc.T("LabelLintMinSeverity");
        SeverityAllItem.Content = Loc.T("LintSeverityAll");
        SeverityWarnItem.Content = Loc.T("LintSeverityWarn");
        BtnLint.Content = _running ? Loc.T("StatusLintRunning") : Loc.T("BtnLint");
        HintLabel.Text = Loc.T("HintLint");

        InlineWarning.Text = Format(_warning);
        InlineWarning.IsVisible = _warning is not null;
        if (_warning is null)
            InlineWarning.ClearValue(TextBlock.ForegroundProperty);
        else if (TryBrush("WarningFg") is { } warningBrush)
            InlineWarning.Foreground = warningBrush;

        ResultLabel.Text = Format(_result);
        if (_result is null)
            ResultLabel.ClearValue(TextBlock.ForegroundProperty);
        else if (TryBrush("SuccessFg") is { } successBrush)
            ResultLabel.Foreground = successBrush;

        _ = RerenderReportAsync();
    }

    private static string Format(Message? text) =>
        text is { } t ? Loc.T(t.Key, t.Args) : string.Empty;

    /// <summary>一条待显示的文案。存键与实参而不是成品字串，切语言时才能重算。</summary>
    private sealed record Message(string Key, object[] Args);

    // ================================================================
    //  选文件
    // ================================================================

    private async void OnBrowse(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            return;

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Loc.T("TabLint"),
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Text files") { Patterns = new[] { "*.txt" } },
                new FilePickerFileType("All files") { Patterns = new[] { "*" } },
            },
        });

        var paths = files
            .Select(f => f.TryGetLocalPath())
            .OfType<string>()
            .ToArray();

        if (paths.Length > 0)
            SelectFiles(paths);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        bool hasFiles = e.Data.Contains(DataFormats.Files);
        e.DragEffects = hasFiles ? DragDropEffects.Copy : DragDropEffects.None;
        if (hasFiles && TryBrush("DragHighlightBg") is { } highlight)
            FileBox.Background = highlight;
    }

    private void OnDragLeave(object? sender, RoutedEventArgs e) => ResetDragHighlight();

    private void OnDrop(object? sender, DragEventArgs e)
    {
        ResetDragHighlight();

        var paths = e.Data.GetFiles()?
            .Select(f => f.TryGetLocalPath())
            .OfType<string>()
            .ToArray();

        if (paths is { Length: > 0 })
            SelectFiles(paths);
    }

    /// <summary>
    /// 取资源里的画刷。语义色在 ThemeDictionaries 下，必须带上当前主题变体去查：
    /// 不带变体的重载按 Default 查找，主题字典里的键一个也取不到。
    /// 资源缺席时返回 null，调用方保持控件原有颜色。
    /// </summary>
    private IBrush? TryBrush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out object? value) && value is IBrush brush
            ? brush
            : null;

    private void ResetDragHighlight() => FileBox.ClearValue(TextBox.BackgroundProperty);

    private void SelectFiles(IReadOnlyList<string> files)
    {
        _selectedFiles.Clear();
        var valid = files.Where(File.Exists).ToList();
        if (valid.Count == 0)
            return;

        _selectedFiles.AddRange(valid);
        FileBox.Text = valid.Count == 1 ? valid[0] : $"[{valid.Count} files selected]";
        ClearWarning();
        StatusChanged?.Invoke(Loc.T("StatusFilesSelected", valid.Count));
    }

    // ================================================================
    //  检查
    // ================================================================

    private async void OnLint(object? sender, RoutedEventArgs e)
    {
        if (_running)
            return;

        string paste = PasteBox.Text ?? string.Empty;
        string[] files = _selectedFiles.ToArray();

        if (files.Length == 0 && string.IsNullOrWhiteSpace(paste))
        {
            ShowWarning("MsgLintNeedInput");
            return;
        }

        // 选中的文件可能在这之后被移走。先在界面上说清楚，别让它在后台线程里变成一个裸异常。
        string? missing = files.FirstOrDefault(path => !File.Exists(path));
        if (missing is not null)
        {
            ShowWarning("MsgFileNotFound", missing);
            return;
        }

        string? minSeverity = SeverityBox.SelectedIndex == 1 ? "warn" : null;
        string languageAtStart = Loc.CurrentLang;

        _generation++;
        _running = true;
        BtnLint.IsEnabled = false;
        BtnLint.Content = Loc.T("StatusLintRunning");
        StatusChanged?.Invoke(Loc.T("StatusLintRunning"));

        try
        {
            var scan = await Task.Run(() => Run(files, paste, minSeverity));

            _lastInputs = scan.Inputs;
            _lastMinSeverity = minSeverity;
            ReportBox.Text = scan.Text;
            ClearWarning();
            ShowResult(scan.Hits, scan.Notes);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(ex.Message);
        }
        finally
        {
            _running = false;
            BtnLint.IsEnabled = true;
            BtnLint.Content = Loc.T("BtnLint");
        }

        // 跑的这几秒里换过语言：刚出的报告是旧语言的，按新语言重扫一遍。
        if (languageAtStart != Loc.CurrentLang)
            await RerenderReportAsync();
    }

    /// <summary>一次检查的产物：报告正文、命中与统计项计数、以及这次真正扫过的输入。</summary>
    private sealed record Scan(string Text, int Hits, int Notes, List<(string File, string Text)> Inputs);

    private static Scan Run(string[] files, string paste, string? minSeverity)
    {
        var inputs = new List<(string File, string Text)>();
        foreach (string path in files)
        {
            var detection = EncodingDetector.DetectStrict(path);
            inputs.Add((Path.GetFileName(path), File.ReadAllText(path, detection.Encoding)));
        }

        if (!string.IsNullOrWhiteSpace(paste))
            inputs.Add((PasteSource, paste));

        return Render(inputs, minSeverity);
    }

    /// <summary>扫一遍并渲染成人读报告。文案出处随当前界面语言，缺译时退回引擎里的原文。</summary>
    private static Scan Render(List<(string File, string Text)> inputs, string? minSeverity)
    {
        var reportSet = LintRunner.Run(inputs, onlyIds: null, minSeverity, LintTextSource.Instance);
        string text = LintOutput.Render(reportSet, asJson: false, LintTextSource.Instance);

        int hits = 0, notes = 0;
        foreach (var report in reportSet.Reports)
        {
            hits += report.Hits.Count;
            notes += report.Notes.Count;
        }

        return new Scan(text, hits, notes, inputs);
    }

    /// <summary>
    /// 换语言后按新语言重扫上一次的输入。留着旧报告就是留着旧语言，所以这里必须重跑，
    /// 而不是只把某一处标题刷一下。扫描期间又换了语言时，过期的结果直接丢掉。
    /// </summary>
    private async Task RerenderReportAsync()
    {
        if (_running || _lastInputs is not { } inputs || string.IsNullOrEmpty(ReportBox.Text))
            return;

        int generation = ++_generation;
        string? minSeverity = _lastMinSeverity;

        try
        {
            var scan = await Task.Run(() => Render(inputs, minSeverity));
            if (generation != _generation)
                return;

            ReportBox.Text = scan.Text;
            ShowResult(scan.Hits, scan.Notes);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(ex.Message);
        }
    }

    // ================================================================
    //  提示
    // ================================================================

    private void ShowResult(int hits, int notes)
    {
        _result = new Message("StatusLintDone", new object[] { hits, notes });
        ResultLabel.Text = Loc.T("StatusLintDone", hits, notes);
        if (TryBrush("SuccessFg") is { } brush)
            ResultLabel.Foreground = brush;
        StatusChanged?.Invoke(ResultLabel.Text);
    }

    private void ShowWarning(string key, params object[] args)
    {
        _warning = new Message(key, args);
        InlineWarning.Text = Loc.T(key, args);
        if (TryBrush("WarningFg") is { } brush)
            InlineWarning.Foreground = brush;
        InlineWarning.IsVisible = true;
        StatusChanged?.Invoke(InlineWarning.Text);
    }

    private void ClearWarning()
    {
        _warning = null;
        InlineWarning.Text = string.Empty;
        InlineWarning.IsVisible = false;
    }
}
