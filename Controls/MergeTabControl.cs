using System.Text;
using TextTool.Localization;
using TextTool.Services;

namespace TextTool.Controls;

/// <summary>
/// "行合并" 页签：文件选择 → 阈值设置 → 后处理选项 → 执行流水线。
/// </summary>
public sealed class MergeTabControl : UserControl
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
    private Label _lblOutput = null!;
    private Label _lblPunctChars = null!;
    private TextBox _txtPunctChars = null!;
    private Label _lblNoMergeChars = null!;
    private TextBox _txtNoMergeChars = null!;
    private CheckBox _chkTrimLeadingComma = null!;

    private DetectionResult? _lastDetection;

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
            RowCount = 9,
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
            BackColor = Color.White,
            AllowDrop = true
        };
        _txtFilePath.DragEnter += OnFileDragEnter;
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
            Minimum = 1,
            Maximum = 1000,
            Value = 20,
            Width = 60
        };
        thresholdPanel.Controls.Add(_numThreshold);

        _rbByte = new RadioButton
        {
            Text = "By Bytes",
            Checked = true,
            AutoSize = true,
            Margin = new Padding(16, 0, 0, 0)
        };
        _rbChar = new RadioButton
        {
            Text = "By Chars",
            AutoSize = true,
            Margin = new Padding(8, 0, 0, 0)
        };
        thresholdPanel.Controls.Add(_rbByte);
        thresholdPanel.Controls.Add(_rbChar);
        layout.Controls.Add(thresholdPanel, 1, 1);

        // Row 2 — Encoding
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        _lblEncodingTag = MakeLabel("Encoding:");
        layout.Controls.Add(_lblEncodingTag, 0, 2);
        _lblEncoding = new Label
        {
            Text = "(No file selected)",
            ForeColor = Color.Gray,
            Anchor = AnchorStyles.Left
        };
        layout.Controls.Add(_lblEncoding, 1, 2);

        // Row 3 — Post: Fix CJK truncation
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        _lblPostProcess = MakeLabel("Post:");
        layout.Controls.Add(_lblPostProcess, 0, 3);
        _chkFixCjk = new CheckBox
        {
            Text = "Fix CJK truncation",
            Checked = true,
            AutoSize = true,
            Anchor = AnchorStyles.Left
        };
        layout.SetColumnSpan(_chkFixCjk, 2);
        layout.Controls.Add(_chkFixCjk, 1, 3);

        // Row 4 — Fix punct. truncation + custom punct textbox
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        _chkFixPunct = new CheckBox
        {
            Text = "Fix punct. truncation",
            Checked = false,
            AutoSize = true,
            Anchor = AnchorStyles.Left
        };
        _lblPunctChars = new Label
        {
            Text = "Custom punct.:",
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(8, 0, 4, 0)
        };
        _txtPunctChars = new TextBox
        {
            Text = "，",
            Width = 100,
            Anchor = AnchorStyles.Left
        };
        var punctFixRow = new FlowLayoutPanel
        {
            Anchor = AnchorStyles.Left,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true
        };
        punctFixRow.Controls.Add(_chkFixPunct);
        punctFixRow.Controls.Add(_lblPunctChars);
        punctFixRow.Controls.Add(_txtPunctChars);
        layout.SetColumnSpan(punctFixRow, 2);
        layout.Controls.Add(punctFixRow, 1, 4);

        // Row 5 — Apply punct. replace rules
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        _chkApplyReplace = new CheckBox
        {
            Text = "Apply punct. replace rules",
            Checked = false,
            AutoSize = true,
            Anchor = AnchorStyles.Left
        };
        layout.SetColumnSpan(_chkApplyReplace, 2);
        layout.Controls.Add(_chkApplyReplace, 1, 5);

        // Row 6 — Line-ending punct. no-merge + custom punct textbox
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        _chkNoMerge = new CheckBox
        {
            Text = "Line-ending punct. no-merge",
            Checked = false,
            AutoSize = true,
            Anchor = AnchorStyles.Left
        };
        _lblNoMergeChars = new Label
        {
            Text = "Custom punct.:",
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(8, 0, 4, 0)
        };
        _txtNoMergeChars = new TextBox
        {
            Text = "。！？",
            Width = 100,
            Anchor = AnchorStyles.Left
        };
        var noMergeRow = new FlowLayoutPanel
        {
            Anchor = AnchorStyles.Left,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true
        };
        noMergeRow.Controls.Add(_chkNoMerge);
        noMergeRow.Controls.Add(_lblNoMergeChars);
        noMergeRow.Controls.Add(_txtNoMergeChars);
        layout.SetColumnSpan(noMergeRow, 2);
        layout.Controls.Add(noMergeRow, 1, 6);

        // Row 7 — Trim leading comma
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        _chkTrimLeadingComma = new CheckBox
        {
            Text = "Trim leading comma",
            Checked = true,
            AutoSize = true,
            Anchor = AnchorStyles.Left
        };
        layout.SetColumnSpan(_chkTrimLeadingComma, 2);
        layout.Controls.Add(_chkTrimLeadingComma, 1, 7);

        // Row 8 — Process button
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        _btnProcess = MakePrimaryButton("Process");
        _btnProcess.Enabled = false;
        _btnProcess.Click += OnProcess;
        layout.Controls.Add(_btnProcess, 1, 8);

        // Row 9 — Output
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        _lblOutput = new Label
        {
            Text = "",
            ForeColor = Color.Green,
            Anchor = AnchorStyles.Left
        };
        layout.SetColumnSpan(_lblOutput, 2);
        layout.Controls.Add(_lblOutput, 1, 9);

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
        _btnProcess.Text = Loc.T("BtnProcess");
    }

    // ================================================================
    //  Drag & Drop
    // ================================================================

    private void OnFileDragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
            e.Effect = DragDropEffects.Copy;
        else
            e.Effect = DragDropEffects.None;
    }

    private void OnFileDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            SelectFile(files[0]);
    }

    private void SelectFile(string path)
    {
        if (!File.Exists(path)) { ErrorOccurred?.Invoke(Loc.T("MsgFileNotFound", path)); return; }
        _txtFilePath.Text = path;
        try
        {
            _lastDetection = EncodingDetector.Detect(path);
            _lblEncoding.Text = _lastDetection.DisplayName;
            _lblEncoding.ForeColor = SystemColors.ControlText;
            _btnProcess.Enabled = true;
            StatusChanged?.Invoke(Loc.T("StatusSelected", Path.GetFileName(path), _lastDetection.DisplayName));
        }
        catch (Exception ex) { ErrorOccurred?.Invoke(Loc.T("MsgReadFailed", ex.Message)); }
    }

    // ================================================================
    //  Button events
    // ================================================================

    private void OnBrowseFile(object? sender, EventArgs e)
    {
        using var dlg = OpenTextFileDialog(Loc.T("TabMerge"));
        if (dlg.ShowDialog(this) == DialogResult.OK)
            SelectFile(dlg.FileName);
    }

    private void OnProcess(object? sender, EventArgs e)
    {
        if (_lastDetection == null || string.IsNullOrWhiteSpace(_txtFilePath.Text)) return;

        try
        {
            _btnProcess.Enabled = false;
            _btnProcess.Text = Loc.T("StatusProcessing");
            StatusChanged?.Invoke(Loc.T("StatusProcessing"));

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

            var result = ProcessingPipeline.Run(
                _txtFilePath.Text, _lastDetection.Encoding, options, postProcess);

            var extras = new List<string>();
            if (result.CjkFixed) extras.Add(Loc.T("ExtraCjk"));
            if (result.PunctFixed) extras.Add(Loc.T("ExtraPunct"));
            if (result.Trimmed) extras.Add(Loc.T("ExtraTrimLeadingComma"));
            if (result.Replaced) extras.Add(Loc.T("ExtraReplace"));
            string extraMsg = extras.Count > 0
                ? $"({string.Join(", ", extras)})"
                : "";

            _lblOutput.Text = Loc.T("LabelOutput", result.OutputPath);
            _lblOutput.ForeColor = Color.Green;
            StatusChanged?.Invoke(Loc.T("StatusComplete", result.OutputPath, extraMsg));

            if (MessageBox.Show(this,
                Loc.T("MsgProcessBody", extraMsg, result.OutputPath),
                Loc.T("MsgProcessTitle"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
            {
                RevealInExplorer(result.OutputPath);
            }
        }
        catch (Exception ex) { ErrorOccurred?.Invoke(Loc.T("MsgProcessFailed", ex.Message)); }
        finally { _btnProcess.Enabled = true; _btnProcess.Text = Loc.T("BtnProcess"); }
    }

    // ================================================================
    //  Helpers
    // ================================================================

    private static Label MakeLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        TextAlign = ContentAlignment.MiddleRight,
        Anchor = AnchorStyles.Right
    };

    private static Button MakePrimaryButton(string text)
    {
        var btn = new Button
        {
            Text = text,
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold),
            BackColor = Color.SteelBlue,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Padding = new Padding(24, 6, 24, 6)
        };
        btn.FlatAppearance.BorderSize = 0;
        return btn;
    }

    private static void RevealInExplorer(string filePath)
    {
        System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{filePath}\"");
    }

    private static OpenFileDialog OpenTextFileDialog(string title) => new()
    {
        Title = title,
        Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
        RestoreDirectory = true
    };
}
