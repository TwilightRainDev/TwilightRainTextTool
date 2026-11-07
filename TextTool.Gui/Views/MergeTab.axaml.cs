using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
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
public partial class MergeTab : FileTabBase
{
    private CancellationTokenSource? _cts;
    private List<ReplaceRule> _rules = new();

    public MergeTab()
    {
        InitializeComponent();
        ApplyLocalization();

        OverwriteCheck.IsCheckedChanged += (_, _) => UpdateOverwriteWarning();
        UpdateOverwriteWarning();
    }

    /// <summary>替换规则由外部注入，这里只用它决定是否勾选时提示。</summary>
    public void SetRules(List<ReplaceRule> rules) => _rules = rules;

    // ================================================================
    //  文案
    // ================================================================

    protected override TextBox FileBoxControl => FileBox;

    protected override Button BrowseButtonControl => BrowseButton;

    protected override TextBlock SourceLabelControl => SourceLabel;

    protected override TextBlock ResultLabelControl => ResultLabel;

    protected override TextBlock EncodingLabelControl => EncodingLabel;

    protected override TextBlock EncodingValueControl => EncodingValue;

    protected override string PickerTitleKey => "TabMerge";

    protected override void ApplyLocalization()
    {
        ApplyCommonLocalization();

        ThresholdLabel.Text = Loc.T("LabelThreshold");
        ByteRadio.Content = Loc.T("RadioByte");
        CharRadio.Content = Loc.T("RadioChar");
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
    }

    // ================================================================
    //  选文件
    // ================================================================

    protected override void OnFilesSelected(IReadOnlyList<string> files)
    {
        ProcessButton.IsEnabled = true;
        PreviewButton.IsEnabled = files.Count == 1; // 预览只对单文件有意义
    }

    // ================================================================
    //  处理
    // ================================================================

    private async void OnProcess(object? sender, RoutedEventArgs e)
    {
        if (LastDetection is null || SelectedFiles.Count == 0)
            return;

        // 跑着的时候再点一次就是取消，和 WinForms 版一致。
        if (_cts is { } running)
        {
            running.Cancel();
            ProcessButton.IsEnabled = false;
            ProcessButton.Content = Loc.T("StatusCancelling");
            ReportStatus(Loc.T("StatusCancelling"));
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
        string[] files = SelectedFiles.ToArray();

        _cts = new CancellationTokenSource();
        ProcessButton.IsEnabled = false;
        ProcessButton.Content = Loc.T("StatusProcessing");

        var progress = new Progress<(int Done, string Error)>(p =>
        {
            if (string.IsNullOrEmpty(p.Error))
                ReportStatus(Loc.T("StatusBatchProgress", p.Done, files.Length));
            else
                ReportError(Loc.T("MsgProcessFailed", $"{Path.GetFileName(files[p.Done - 1])}：{p.Error}"));
        });

        try
        {
            var (success, cancelled) = await RunBatchAsync(files, options, postProcess, writeMode, progress);

            ProcessButton.IsEnabled = true;
            ProcessButton.Content = Loc.T("BtnProcess");

            if (cancelled)
                ShowResult("StatusBatchCancelled", warning: true, success);
            else if (success == files.Length)
                ShowResult("StatusBatchComplete", warning: false, success);
            else
                ShowResult("StatusBatchPartial", warning: true, success, files.Length);
        }
        catch (Exception ex)
        {
            ProcessButton.IsEnabled = true;
            ProcessButton.Content = Loc.T("BtnProcess");
            ReportError(Loc.T("MsgProcessFailed", ex.Message));
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
        if (LastDetection is null || SelectedFiles.Count != 1)
            return;

        string path = SelectedFiles[0];
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
            ReportError(Loc.T("MsgProcessFailed", ex.Message));
        }
    }

    private sealed record PreviewResult(string Text, string Path);
}
