using System.Text;
using TextTool.Localization;
using TextTool.Services;

namespace TextTool.Controls;

/// <summary>
/// "行合并" 页签：文件选择 → 阈值设置 → 后处理选项 → 执行流水线。
/// 支持拖放多文件（批量处理）和预览。
/// </summary>
public sealed class MergeTabControl : UserControl, IStatusSource, IThemedTab
{
    public event Action<string>? StatusChanged;
    public event Action<string>? ErrorOccurred;

    /// <summary>替换规则列表引用（由 MainForm 在构建后注入）</summary>
    public List<ReplaceRule>? ReplaceRules { get; set; }

    private Label _lblSourceFile = null!;
    private TextBox _txtFilePath = null!;
    private Button _btnBrowse = null!;
    private Label _lblThreshold = null!;
    private NumericUpDown _numThreshold = null!;
    private RadioButton _rbByte = null!;
    private RadioButton _rbChar = null!;
    private Label _lblEncodingTag = null!;
    private Label _lblEncoding = null!;
    private Label _lblPostProcess = null!;
    private CheckBox _chkFixCjk = null!;
    private CheckBox _chkFixPunct = null!;
    private CheckBox _chkApplyReplace = null!;
    private CheckBox _chkNoMerge = null!;
    private Button _btnProcess = null!;
    private Button _btnPreview = null!;
    private Label _lblOutput = null!;
    private Label _lblPunctChars = null!;
    private TextBox _txtPunctChars = null!;
    private Label _lblNoMergeChars = null!;
    private TextBox _txtNoMergeChars = null!;
    private CheckBox _chkTrimLeadingComma = null!;
    private CheckBox _chkOverwrite = null!;   // 危险覆盖模式

    private readonly List<string> _selectedFiles = new();
    private DetectionResult? _lastDetection;
    private CancellationTokenSource? _cts;   // 批处理取消令牌

    public MergeTabControl()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 11,
            Padding = new Padding(16, 16, 16, 8)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        // Row 0 — Source file
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        _lblSourceFile = MakeLabel("Source File:");
        layout.Controls.Add(_lblSourceFile, 0, 0);
        _txtFilePath = new TextBox
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            ReadOnly = true,
            BackColor = ThemeManager.ControlBg,
            ForeColor = ThemeManager.Fg,
            AllowDrop = true
        };
        _txtFilePath.DragEnter += OnFileDragEnter;
        _txtFilePath.DragOver += OnFileDragOver;
        _txtFilePath.DragLeave += OnFileDragLeave;
        _txtFilePath.DragDrop += OnFileDragDrop;
        layout.Controls.Add(_txtFilePath, 1, 0);

        _btnBrowse = new Button { Text = "Browse...", AutoSize = true };
        _btnBrowse.Click += OnBrowseFile;
        layout.Controls.Add(_btnBrowse, 2, 0);

        // Row 1 — Threshold
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        _lblThreshold = MakeLabel("Threshold:");
        layout.Controls.Add(_lblThreshold, 0, 1);

        var thresholdPanel = new FlowLayoutPanel
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        _numThreshold = new NumericUpDown
        {
            Minimum = 1, Maximum = 1000, Value = 20, Width = 60
        };
        thresholdPanel.Controls.Add(_numThreshold);

        _rbByte = new RadioButton { Text = "By Bytes", Checked = true, AutoSize = true, Margin = new Padding(16, 0, 0, 0) };
        _rbChar = new RadioButton { Text = "By Chars", AutoSize = true, Margin = new Padding(8, 0, 0, 0) };
        thresholdPanel.Controls.Add(_rbByte);
        thresholdPanel.Controls.Add(_rbChar);
        layout.Controls.Add(thresholdPanel, 1, 1);

        // Row 2 — Encoding
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        _lblEncodingTag = MakeLabel("Encoding:");
        layout.Controls.Add(_lblEncodingTag, 0, 2);
        _lblEncoding = new Label { Text = "(No file selected)", ForeColor = Color.Gray, Anchor = AnchorStyles.Left };
        layout.Controls.Add(_lblEncoding, 1, 2);

        // Row 3 — Post: Fix CJK truncation
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        _lblPostProcess = MakeLabel("Post:");
        layout.Controls.Add(_lblPostProcess, 0, 3);
        _chkFixCjk = new CheckBox { Text = "Fix CJK truncation", Checked = true, AutoSize = true, Anchor = AnchorStyles.Left };
        layout.SetColumnSpan(_chkFixCjk, 2);
        layout.Controls.Add(_chkFixCjk, 1, 3);

        // Row 4 — Fix punct. truncation
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        _chkFixPunct = new CheckBox { Text = "Fix punct. truncation", Checked = false, AutoSize = true, Anchor = AnchorStyles.Left };
        _lblPunctChars = new Label { Text = "Custom punct.:", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(8, 0, 4, 0) };
        _txtPunctChars = new TextBox { Text = "，", Width = 100, Anchor = AnchorStyles.Left };
        var punctFixRow = new FlowLayoutPanel { Anchor = AnchorStyles.Left, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true };
        punctFixRow.Controls.Add(_chkFixPunct);
        punctFixRow.Controls.Add(_lblPunctChars);
        punctFixRow.Controls.Add(_txtPunctChars);
        layout.SetColumnSpan(punctFixRow, 2);
        layout.Controls.Add(punctFixRow, 1, 4);

        // Row 5 — Apply punct. replace rules
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        _chkApplyReplace = new CheckBox { Text = "Apply punct. replace rules", Checked = false, AutoSize = true, Anchor = AnchorStyles.Left };
        layout.SetColumnSpan(_chkApplyReplace, 2);
        layout.Controls.Add(_chkApplyReplace, 1, 5);

        // Row 6 — Line-ending punct. no-merge
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        _chkNoMerge = new CheckBox { Text = "Line-ending punct. no-merge", Checked = false, AutoSize = true, Anchor = AnchorStyles.Left };
        _lblNoMergeChars = new Label { Text = "Custom punct.:", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(8, 0, 4, 0) };
        _txtNoMergeChars = new TextBox { Text = "。！？", Width = 100, Anchor = AnchorStyles.Left };
        var noMergeRow = new FlowLayoutPanel { Anchor = AnchorStyles.Left, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true };
        noMergeRow.Controls.Add(_chkNoMerge);
        noMergeRow.Controls.Add(_lblNoMergeChars);
        noMergeRow.Controls.Add(_txtNoMergeChars);
        layout.SetColumnSpan(noMergeRow, 2);
        layout.Controls.Add(noMergeRow, 1, 6);

        // Row 7 — Trim leading comma
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        _chkTrimLeadingComma = new CheckBox { Text = "Trim leading comma", Checked = true, AutoSize = true, Anchor = AnchorStyles.Left };
        layout.SetColumnSpan(_chkTrimLeadingComma, 2);
        layout.Controls.Add(_chkTrimLeadingComma, 1, 7);

        // Row 8 — Dangerous overwrite mode
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        _chkOverwrite = new CheckBox { Text = Loc.T("ChkOverwrite"), Checked = false, AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = Color.OrangeRed };
        _chkOverwrite.CheckedChanged += OnOverwriteChecked;
        layout.SetColumnSpan(_chkOverwrite, 2);
        layout.Controls.Add(_chkOverwrite, 1, 8);

        // Row 9 — Buttons (Process + Preview)
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        var btnRow = new FlowLayoutPanel
        {
            Anchor = AnchorStyles.Left,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true
        };
        _btnProcess = MakePrimaryButton("Process");
        _btnProcess.Enabled = false;
        _btnProcess.Click += OnProcess;
        btnRow.Controls.Add(_btnProcess);

        _btnPreview = new ThemedFlatButton
        {
            Text = "Preview",
            AutoSize = true,
            Enabled = false,
            Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold),
            BackColor = ControlsHelper.ButtonBg,
            ForeColor = ControlsHelper.ButtonFg,
            Padding = new Padding(24, 6, 24, 6),
            Margin = new Padding(8, 0, 0, 0)
        };
        _btnPreview.Click += OnPreview;
        btnRow.Controls.Add(_btnPreview);
        layout.Controls.Add(btnRow, 1, 9);

        // Row 10 — Output
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        _lblOutput = new Label { Text = "", ForeColor = Color.Green, Anchor = AnchorStyles.Left };
        layout.SetColumnSpan(_lblOutput, 2);
        layout.Controls.Add(_lblOutput, 1, 10);

        Controls.Add(layout);
    }

    public void ApplyLocalization()
    {
        _lblSourceFile.Text = Loc.T("LabelSourceFile");
        _btnBrowse.Text = Loc.T("BtnBrowse");
        _lblThreshold.Text = Loc.T("LabelThreshold");
        _rbByte.Text = Loc.T("RadioByte");
        _rbChar.Text = Loc.T("RadioChar");
        _lblEncodingTag.Text = Loc.T("LabelEncoding");
        if (_lastDetection == null) _lblEncoding.Text = Loc.T("EncodingNotSelected");
        _lblPostProcess.Text = Loc.T("LabelPostProcess");
        _chkFixCjk.Text = Loc.T("ChkFixCjk");
        _chkFixPunct.Text = Loc.T("ChkFixPunct");
        _lblPunctChars.Text = Loc.T("LabelPunctChars");
        _chkApplyReplace.Text = Loc.T("ChkApplyReplace");
        _chkNoMerge.Text = Loc.T("ChkNoMerge");
        _lblNoMergeChars.Text = Loc.T("LabelNoMergeChars");
        _chkTrimLeadingComma.Text = Loc.T("ChkTrimLeadingComma");
        _chkOverwrite.Text = Loc.T("ChkOverwrite");
        _btnProcess.Text = Loc.T("BtnProcess");
        _btnPreview.Text = Loc.T("BtnPreview");
    }

    /// <summary>公开给 MainForm 调用以应用当前主题</summary>
    public void ApplyTheme()
    {
        ControlsHelper.ApplyTheme(this);

        // 特殊覆盖（统一遍历器无法处理的逻辑）
        _chkOverwrite.ForeColor = Color.OrangeRed;
        _lblEncoding.ForeColor = ThemeManager.MutedFg;
    }

    // ================================================================
    //  Drag & Drop — 带拖放高亮反馈
    // ================================================================

    private void OnFileDragEnter(object? sender, DragEventArgs e)
    {
        ControlsHelper.SetupFileDragEnter(e, _txtFilePath);
    }

    private void OnFileDragOver(object? sender, DragEventArgs e)
    {
        ControlsHelper.SetupFileDragOver(e);
    }

    private void OnFileDragLeave(object? sender, EventArgs e)
    {
        ControlsHelper.ResetFileDragLeave(_txtFilePath);
    }

    private void OnFileDragDrop(object? sender, DragEventArgs e)
    {
        _txtFilePath.BackColor = Color.White;
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            SelectFiles(files);
    }

    private void SelectFiles(string[] files)
    {
        _selectedFiles.Clear();
        var valid = new List<string>();
        foreach (var f in files)
        {
            if (File.Exists(f)) valid.Add(f);
        }
        if (valid.Count == 0) return;

        _selectedFiles.AddRange(valid);
        _txtFilePath.Text = valid.Count == 1
            ? valid[0]
            : $"[{valid.Count} files selected]";

        // 用第一个文件检测编码（显示参考）
        try
        {
            _lastDetection = EncodingDetector.Detect(valid[0]);
            _lblEncoding.Text = _lastDetection.DisplayName;
            _lblEncoding.ForeColor = SystemColors.ControlText;
        }
        catch
        {
            // 检测失败时清空状态，避免以错误的编码继续处理
            _lastDetection = null;
            _lblEncoding.Text = Loc.T("EncodingNotSelected");
            _lblEncoding.ForeColor = ThemeManager.MutedFg;
        }

        _btnProcess.Enabled = true;
        _btnPreview.Enabled = valid.Count == 1; // 预览只对单文件有意义
        StatusChanged?.Invoke(Loc.T("StatusFilesSelected", valid.Count));
    }

    // ================================================================
    //  Button events
    // ================================================================

    private void OnBrowseFile(object? sender, EventArgs e)
    {
        using var dlg = ControlsHelper.CreateTextFileDialog(Loc.T("TabMerge"));
        if (dlg.ShowDialog(this) == DialogResult.OK)
            SelectFiles(dlg.FileNames);
    }

    private async void OnPreview(object? sender, EventArgs e)
    {
        if (_lastDetection == null || _selectedFiles.Count != 1) return;
        string path = _selectedFiles[0];

        // UI 线程捕获选项，避免后台线程访问控件
        var options = new MergeOptions
        {
            Threshold = (int)_numThreshold.Value,
            Mode = _rbChar.Checked ? MergeMode.CharCount : MergeMode.ByteCount
        };
        var postProcess = new PostProcessOptions(
            FixCjk: _chkFixCjk.Checked,
            FixPunct: _chkFixPunct.Checked,
            PunctChars: _txtPunctChars.Text,
            NoMerge: _chkNoMerge.Checked,
            NoMergeChars: _txtNoMergeChars.Text,
            ApplyReplace: _chkApplyReplace.Checked,
            TrimLeadingComma: _chkTrimLeadingComma.Checked,
            Rules: ReplaceRules ?? new List<ReplaceRule>());

        try
        {
            // 预览始终不覆盖原文件（即使危险模式已勾选）
            var result = await Task.Run(() => RunPipeline(path, options, postProcess, overwrite: false));
            using var preview = new PreviewForm(result.Lines, result.OutputPath);
            preview.ShowDialog(this);
        }
        catch (Exception ex) { ErrorOccurred?.Invoke(Loc.T("MsgProcessFailed", ex.Message)); }
    }

    private void OnOverwriteChecked(object? sender, EventArgs e)
    {
        if (_chkOverwrite.Checked)
        {
            var result = MessageBox.Show(this,
                Loc.T("MsgOverwriteBody"),
                Loc.T("MsgOverwriteTitle"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (result != DialogResult.Yes)
                _chkOverwrite.Checked = false; // 取消勾选
        }
    }

    private async void OnProcess(object? sender, EventArgs e)
    {
        if (_lastDetection == null || _selectedFiles.Count == 0) return;

        // 运行中再点按钮 = 取消
        if (_cts != null)
        {
            _cts.Cancel();
            _btnProcess.Enabled = false;
            _btnProcess.Text = Loc.T("StatusCancelling");
            StatusChanged?.Invoke(Loc.T("StatusCancelling"));
            return;
        }

        // 在 UI 线程捕获全部选项，避免后台线程访问控件
        var options = new MergeOptions
        {
            Threshold = (int)_numThreshold.Value,
            Mode = _rbChar.Checked ? MergeMode.CharCount : MergeMode.ByteCount
        };
        var postProcess = new PostProcessOptions(
            FixCjk: _chkFixCjk.Checked,
            FixPunct: _chkFixPunct.Checked,
            PunctChars: _txtPunctChars.Text,
            NoMerge: _chkNoMerge.Checked,
            NoMergeChars: _txtNoMergeChars.Text,
            ApplyReplace: _chkApplyReplace.Checked,
            TrimLeadingComma: _chkTrimLeadingComma.Checked,
            Rules: ReplaceRules ?? new List<ReplaceRule>());
        bool overwrite = _chkOverwrite.Checked;
        var files = _selectedFiles.ToArray();

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _btnProcess.Enabled = false;
        _btnProcess.Text = Loc.T("StatusProcessing");

        var progress = new Progress<(int Done, string Error)>(
            p =>
            {
                if (string.IsNullOrEmpty(p.Error))
                    StatusChanged?.Invoke(Loc.T("StatusBatchProgress", p.Done, files.Length));
                else
                    ErrorOccurred?.Invoke(Loc.T("MsgProcessFailed", Path.GetFileName(files[p.Done - 1]), p.Error));
            });

        var result = await ControlsHelper.RunBatchAsync(
            files,
            (path, _) => RunPipeline(path, options, postProcess, overwrite),
            progress,
            token);

        try
        {
            if (_cts.IsCancellationRequested)
            {
                _btnProcess.Enabled = true;
                _btnProcess.Text = Loc.T("BtnProcess");
                string cmsg = Loc.T("StatusBatchCancelled", result.Success);
                _lblOutput.Text = cmsg;
                _lblOutput.ForeColor = Color.DarkOrange;
                StatusChanged?.Invoke(cmsg);
            }
            else
            {
                _btnProcess.Enabled = true;
                _btnProcess.Text = Loc.T("BtnProcess");
                string msg = result.Success == files.Length
                    ? Loc.T("StatusBatchComplete", result.Success)
                    : Loc.T("StatusBatchPartial", result.Success, files.Length);
                _lblOutput.Text = msg;
                _lblOutput.ForeColor = result.Success == files.Length ? Color.Green : Color.DarkOrange;
                StatusChanged?.Invoke(msg);

                if (result.Success > 0 && MessageBox.Show(this,
                    Loc.T("MsgBatchBody", result.Success),
                    Loc.T("MsgProcessTitle"),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                {
                    ControlsHelper.RevealFolder(Path.GetDirectoryName(files[0])!);
                }
            }
        }
        catch (Exception ex)
        {
            _btnProcess.Enabled = true;
            _btnProcess.Text = Loc.T("BtnProcess");
            ErrorOccurred?.Invoke(Loc.T("MsgProcessFailed", ex.Message));
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
        }
    }

    private ProcessingResult RunPipeline(string path, MergeOptions options, PostProcessOptions postProcess, bool overwrite)
    {
        var encoding = EncodingDetector.DetectStrict(path);
        return ProcessingPipeline.Run(path, encoding.Encoding, options, postProcess, overwrite);
    }

    // ================================================================
    //  Helpers
    // ================================================================

    private static Label MakeLabel(string text) => ControlsHelper.MakeLabel(text);

    private static ThemedFlatButton MakePrimaryButton(string text) => ControlsHelper.MakePrimaryButton(text);
}
