using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using TextTool.Gui.Localization;
using TextTool.Services;

namespace TextTool.Gui.Views;

/// <summary>
/// 行合并页签：选文件、设阈值、挑后处理选项、跑一次流水线。
///
/// 代码后半段（编码检测、批量、取消、状态回报）与 WinForms 版逐条对齐；
/// 界面上刻意不同的两处：
///   1. 覆盖原文件的确认改成行内提示，不用弹窗。弹窗会打断拖放和多文件批处理的节奏。
///   2. 所有选项都在点击处理的那一刻从控件读一次，不递增监听，省掉半新半旧的中间态。
/// </summary>
public partial class MergeTab : UserControl
{
    private readonly List<string> _selectedFiles = new();
    private DetectionResult? _lastDetection;
    private CancellationTokenSource? _cts;
    private List<ReplaceRule> _rules = new();

    /// <summary>状态栏文字。由 MainWindow 订阅。</summary>
    public event Action<string>? StatusChanged;

    /// <summary>需要让用户知道出了错。由 MainWindow 显示。</summary>
    public event Action<string>? ErrorOccurred;

    public MergeTab()
    {
        InitializeComponent();
        ApplyLocalization();
        Loc.LanguageChanged += ApplyLocalization;

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);

        OverwriteCheck.IsCheckedChanged += (_, _) => UpdateOverwriteWarning();
        UpdateOverwriteWarning();
    }

    /// <summary>替换规则由外部注入（Step 4 之后从配置读），这里只用它决定是否勾选时提示。</summary>
    public void SetRules(List<ReplaceRule> rules) => _rules = rules;

    // ================================================================
    //  文案
    // ================================================================

    private void ApplyLocalization()
    {
        SourceLabel.Text = Loc.T("LabelSourceFile");
        BrowseButton.Content = Loc.T("BtnBrowse");
        ThresholdLabel.Text = Loc.T("LabelThreshold");
        ByteRadio.Content = Loc.T("RadioByte");
        CharRadio.Content = Loc.T("RadioChar");
        EncodingLabel.Text = Loc.T("LabelEncoding");
        PostProcessLabel.Text = Loc.T("LabelPostProcess");
        FixCjkText.Text = Loc.T("ChkFixCjk");
        FixPunctText.Text = Loc.T("ChkFixPunct");
        PunctCharsLabel.Text = Loc.T("LabelPunctChars");
        ApplyReplaceText.Text = Loc.T("ChkApplyReplace");
        NoMergeText.Text = Loc.T("ChkNoMerge");
        NoMergeCharsLabel.Text = Loc.T("LabelNoMergeChars");
        TrimCommaText.Text = Loc.T("ChkTrimLeadingComma");
        OverwriteText.Text = Loc.T("ChkOverwrite");
        BackupText.Text = Loc.T("ChkBackupBeforeOverwrite");
        OverwriteWarning.Text = Loc.T("MsgOverwriteBody");
        ProcessButton.Content = _cts is null ? Loc.T("BtnProcess") : Loc.T("StatusProcessing");
        PreviewButton.Content = Loc.T("BtnPreview");

        // 编码那一格的状态文字也要跟着重刷，否则切语言后还停在旧语言。
        EncodingValue.Text = _lastDetection is null
            ? Loc.T("EncodingNotSelected")
            : Loc.T(_lastDetection.LocKey);
    }

    // ================================================================
    //  选文件
    // ================================================================

    private async void OnBrowse(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            return;

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Loc.T("TabMerge"),
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
    /// 取资源里的画刷。资源缺席时 FindResource 返回的是 UnsetValue 而不是 null，
    /// 直接强转会在控件构造阶段就抛出来，所以统一走 TryFindResource。
    /// </summary>
    private static IBrush? TryBrush(string key) =>
        Application.Current is { } app && app.TryFindResource(key, out object? value) && value is IBrush brush
            ? brush
            : null;

    /// <summary>拖放结束后清掉本次赋的底色，让样式里的主题色重新生效。</summary>
    private void ResetDragHighlight() => FileBox.ClearValue(BackgroundProperty);

    private void SelectFiles(IReadOnlyList<string> files)
    {
        _selectedFiles.Clear();
        var valid = files.Where(File.Exists).ToList();
        if (valid.Count == 0)
            return;

        _selectedFiles.AddRange(valid);
        FileBox.Text = valid.Count == 1 ? valid[0] : $"[{valid.Count} files selected]";

        // 编码只按第一个文件检测，作为参考显示；处理时每个文件各自严格检测。
        try
        {
            _lastDetection = EncodingDetector.Detect(valid[0]);
            EncodingValue.Text = Loc.T(_lastDetection.LocKey);
            EncodingValue.Classes.Set("muted", false);
        }
        catch
        {
            _lastDetection = null;
            EncodingValue.Text = Loc.T("EncodingNotSelected");
            EncodingValue.Classes.Set("muted", true);
        }

        ProcessButton.IsEnabled = true;
        PreviewButton.IsEnabled = valid.Count == 1; // 预览只对单文件有意义
        StatusChanged?.Invoke(Loc.T("StatusFilesSelected", valid.Count));
    }

    // ================================================================
    //  处理
    // ================================================================

    private async void OnProcess(object? sender, RoutedEventArgs e)
    {
        if (_lastDetection is null || _selectedFiles.Count == 0)
            return;

        // 跑着的时候再点一次就是取消，和 WinForms 版一致。
        if (_cts is { } running)
        {
            running.Cancel();
            ProcessButton.IsEnabled = false;
            ProcessButton.Content = Loc.T("StatusCancelling");
            StatusChanged?.Invoke(Loc.T("StatusCancelling"));
            return;
        }

        var options = new MergeOptions
        {
            Threshold = (int)(ThresholdBox.Value ?? 20),
            Mode = CharRadio.IsChecked == true ? MergeMode.CharCount : MergeMode.ByteCount,
        };
        var postProcess = BuildPostProcess();
        var writeMode = OverwriteCheck.IsChecked == true
            ? (BackupCheck.IsChecked == true ? WriteMode.OverwriteWithBackup : WriteMode.Overwrite)
            : WriteMode.NewFile;
        string[] files = _selectedFiles.ToArray();

        _cts = new CancellationTokenSource();
        ProcessButton.IsEnabled = false;
        ProcessButton.Content = Loc.T("StatusProcessing");

        var progress = new Progress<(int Done, string Error)>(p =>
        {
            if (string.IsNullOrEmpty(p.Error))
                StatusChanged?.Invoke(Loc.T("StatusBatchProgress", p.Done, files.Length));
            else
                ErrorOccurred?.Invoke(Loc.T("MsgProcessFailed", $"{Path.GetFileName(files[p.Done - 1])}：{p.Error}"));
        });

        try
        {
            var (success, cancelled) = await RunBatchAsync(files, options, postProcess, writeMode, progress);

            ProcessButton.IsEnabled = true;
            ProcessButton.Content = Loc.T("BtnProcess");

            if (cancelled)
            {
                ShowResult(Loc.T("StatusBatchCancelled", success), warning: true);
            }
            else if (success == files.Length)
            {
                ShowResult(Loc.T("StatusBatchComplete", success), warning: false);
            }
            else
            {
                ShowResult(Loc.T("StatusBatchPartial", success, files.Length), warning: true);
            }
        }
        catch (Exception ex)
        {
            ProcessButton.IsEnabled = true;
            ProcessButton.Content = Loc.T("BtnProcess");
            ErrorOccurred?.Invoke(Loc.T("MsgProcessFailed", ex.Message));
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
        }
    }

    private async Task<(int Success, bool Cancelled)> RunBatchAsync(
        IReadOnlyList<string> files,
        MergeOptions options,
        PostProcessOptions postProcess,
        WriteMode writeMode,
        IProgress<(int Done, string Error)> progress)
    {
        CancellationToken token = _cts!.Token;

        var (success, cancelled) = await BatchRunner.RunAsync(
            files,
            (path, _) =>
            {
                var detection = EncodingDetector.DetectStrict(path);
                ProcessingPipeline.Run(path, detection.Encoding, options, postProcess, writeMode);
            },
            progress,
            token);

        return (success, cancelled);
    }

    private PostProcessOptions BuildPostProcess() => new(
        FixCjk: FixCjkCheck.IsChecked == true,
        FixPunct: FixPunctCheck.IsChecked == true,
        PunctChars: PunctCharsBox.Text ?? string.Empty,
        NoMerge: NoMergeCheck.IsChecked == true,
        NoMergeChars: NoMergeCharsBox.Text ?? string.Empty,
        ApplyReplace: ApplyReplaceCheck.IsChecked == true,
        TrimLeadingComma: TrimCommaCheck.IsChecked == true,
        Rules: _rules);

    private void ShowResult(string text, bool warning)
    {
        ResultLabel.Text = text;
        if (TryBrush(warning ? "WarningFg" : "SuccessFg") is { } brush)
            ResultLabel.Foreground = brush;
        StatusChanged?.Invoke(text);
    }

    private void UpdateOverwriteWarning()
    {
        bool overwrite = OverwriteCheck.IsChecked == true;
        OverwriteWarning.IsVisible = overwrite;
        if (TryBrush("WarningFg") is { } warningBrush)
            OverwriteWarning.Foreground = warningBrush;
        // 不覆盖就没有备份可言，置灰但不锁死，避免用户以为能勾。
        BackupCheck.Opacity = overwrite ? 1.0 : 0.75;
    }

    // ================================================================
    //  预览
    // ================================================================

    private async void OnPreview(object? sender, RoutedEventArgs e)
    {
        if (_lastDetection is null || _selectedFiles.Count != 1)
            return;

        string path = _selectedFiles[0];
        var options = new MergeOptions
        {
            Threshold = (int)(ThresholdBox.Value ?? 20),
            Mode = CharRadio.IsChecked == true ? MergeMode.CharCount : MergeMode.ByteCount,
        };
        var postProcess = BuildPostProcess();

        try
        {
            // 预览永远不写原文件，覆盖配置在预览里不生效。
            PreviewResult? preview = await Task.Run(() =>
            {
                var detection = EncodingDetector.DetectStrict(path);
                var result = ProcessingPipeline.Run(path, detection.Encoding, options, postProcess, WriteMode.NewFile);
                return new PreviewResult(File.ReadAllText(result.OutputPath), result.OutputPath);
            });

            if (preview is null)
                return;

            var previewWindow = new PreviewWindow(preview.Text, preview.Path);
            if (TopLevel.GetTopLevel(this) is Window owner)
                previewWindow.Show(owner);
            else
                previewWindow.Show();
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(Loc.T("MsgProcessFailed", ex.Message));
        }
    }

    private sealed record PreviewResult(string Text, string Path);
}
