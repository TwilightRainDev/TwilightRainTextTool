using System.Text;
using TextTool.Localization;
using TextTool.Services;

namespace TextTool.Controls;

/// <summary>
/// AI 味检查页签：文件或粘贴 → LintRunner → 只读人读报告。不写文件。
/// </summary>
public sealed class LintTabControl : UserControl, IStatusSource, IThemedTab
{
    public event Action<string>? StatusChanged;
    public event Action<string>? ErrorOccurred;

    private Label _lblSourceFile = null!;
    private TextBox _txtFilePath = null!;
    private Button _btnBrowse = null!;
    private Label _lblOnly = null!;
    private TextBox _txtOnly = null!;
    private Label _lblSeverity = null!;
    private ComboBox _cmbSeverity = null!;
    private Label _lblHint = null!;
    private Button _btnLint = null!;
    private Label _lblPaste = null!;
    private TextBox _txtPaste = null!;
    private TextBox _txtReport = null!;

    private readonly List<string> _selectedFiles = new();

    public LintTabControl()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 7,
            Padding = new Padding(16, 16, 16, 8)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        _lblSourceFile = ControlsHelper.MakeLabel("Source:");
        _txtFilePath = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right, ReadOnly = true };
        _btnBrowse = new Button { Text = "Browse...", AutoSize = true };
        _btnBrowse.Click += OnBrowse;
        layout.Controls.Add(_lblSourceFile, 0, 0);
        layout.Controls.Add(_txtFilePath, 1, 0);
        layout.Controls.Add(_btnBrowse, 2, 0);

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        _lblOnly = ControlsHelper.MakeLabel("Only:");
        _txtOnly = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right, PlaceholderText = "S1,L1" };
        layout.Controls.Add(_lblOnly, 0, 1);
        layout.Controls.Add(_txtOnly, 1, 1);

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        _lblSeverity = ControlsHelper.MakeLabel("Severity:");
        _cmbSeverity = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Anchor = AnchorStyles.Left,
            Width = 180
        };
        _cmbSeverity.Items.Add("All");
        _cmbSeverity.Items.Add("warn");
        _cmbSeverity.SelectedIndex = 0;
        layout.Controls.Add(_lblSeverity, 0, 2);
        layout.Controls.Add(_cmbSeverity, 1, 2);

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        _lblHint = new Label { AutoSize = true, Anchor = AnchorStyles.Left };
        layout.SetColumnSpan(_lblHint, 3);
        layout.Controls.Add(_lblHint, 0, 3);

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        _btnLint = ControlsHelper.MakePrimaryButton("Check");
        _btnLint.Click += OnLint;
        layout.Controls.Add(_btnLint, 1, 4);

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
        _lblPaste = ControlsHelper.MakeLabel("Paste:");
        _txtPaste = new TextBox
        {
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            AcceptsReturn = true,
            Dock = DockStyle.Fill
        };
        layout.Controls.Add(_lblPaste, 0, 5);
        layout.Controls.Add(_txtPaste, 1, 5);
        layout.SetColumnSpan(_txtPaste, 2);

        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _txtReport = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 9f)
        };
        layout.SetColumnSpan(_txtReport, 3);
        layout.Controls.Add(_txtReport, 0, 6);

        AllowDrop = true;
        DragEnter += OnFileDragEnter;
        DragOver += OnFileDragOver;
        DragLeave += OnFileDragLeave;
        DragDrop += OnFileDragDrop;
        _txtFilePath.AllowDrop = true;
        _txtFilePath.DragEnter += OnFileDragEnter;
        _txtFilePath.DragOver += OnFileDragOver;
        _txtFilePath.DragLeave += OnFileDragLeave;
        _txtFilePath.DragDrop += OnFileDragDrop;

        Controls.Add(layout);
    }

    public void ApplyTheme() => ControlsHelper.ApplyTheme(this);

    public void ApplyLocalization()
    {
        _lblSourceFile.Text = Loc.T("LabelSourceFile");
        _btnBrowse.Text = Loc.T("BtnBrowse");
        _lblOnly.Text = Loc.T("LabelLintOnly");
        _lblSeverity.Text = Loc.T("LabelLintMinSeverity");
        int severityIndex = _cmbSeverity.SelectedIndex;
        _cmbSeverity.Items.Clear();
        _cmbSeverity.Items.Add(Loc.T("LintSeverityAll"));
        _cmbSeverity.Items.Add(Loc.T("LintSeverityWarn"));
        _cmbSeverity.SelectedIndex = severityIndex < 0 ? 0 : severityIndex;
        _btnLint.Text = Loc.T("BtnLint");
        _lblPaste.Text = Loc.T("LabelLintPaste");
        _lblHint.Text = Loc.T("HintLint");
        _txtReport.PlaceholderText = Loc.T("HintLint");
    }

    private void OnFileDragEnter(object? sender, DragEventArgs e) =>
        ControlsHelper.SetupFileDragEnter(e, _txtFilePath);

    private void OnFileDragOver(object? sender, DragEventArgs e) =>
        ControlsHelper.SetupFileDragOver(e);

    private void OnFileDragLeave(object? sender, EventArgs e) =>
        ControlsHelper.ResetFileDragLeave(_txtFilePath);

    private void OnFileDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            SelectFiles(files);
    }

    private void OnBrowse(object? sender, EventArgs e)
    {
        using var dlg = ControlsHelper.CreateTextFileDialog(Loc.T("TabLint"));
        if (dlg.ShowDialog(this) == DialogResult.OK)
            SelectFiles(dlg.FileNames);
    }

    private void SelectFiles(string[] files)
    {
        var valid = files.Where(File.Exists).ToList();
        if (valid.Count == 0) return;
        _selectedFiles.Clear();
        _selectedFiles.AddRange(valid);
        _txtFilePath.Text = valid.Count == 1 ? valid[0] : $"[{valid.Count}]";
        StatusChanged?.Invoke(Loc.T("StatusFilesSelected", valid.Count));
    }

    private async void OnLint(object? sender, EventArgs e)
    {
        var files = _selectedFiles.ToList();
        string paste = _txtPaste.Text;
        string onlyRaw = _txtOnly.Text;
        string? minSeverity = _cmbSeverity.SelectedIndex == 1 ? "warn" : null;

        if (files.Count == 0 && string.IsNullOrWhiteSpace(paste))
        {
            ErrorOccurred?.Invoke(Loc.T("MsgLintNeedInput"));
            return;
        }

        IReadOnlyCollection<string>? onlyIds = string.IsNullOrWhiteSpace(onlyRaw)
            ? null
            : onlyRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        _btnLint.Enabled = false;
        StatusChanged?.Invoke(Loc.T("StatusLintRunning"));
        try
        {
            var reportSet = await Task.Run(() =>
            {
                var inputs = new List<(string File, string Text)>();
                foreach (var path in files)
                {
                    if (!File.Exists(path))
                        throw new ArgumentException(Loc.T("MsgFileNotFound", path));
                    var encoding = EncodingDetector.DetectStrict(path);
                    inputs.Add((Path.GetFileName(path), File.ReadAllText(path, encoding.Encoding)));
                }
                if (!string.IsNullOrWhiteSpace(paste))
                    inputs.Add(("(paste)", paste));
                return LintRunner.Run(inputs, onlyIds, minSeverity);
            });

            var sb = new StringBuilder();
            int hits = 0, notes = 0;
            foreach (var report in reportSet.Reports)
            {
                if (sb.Length > 0) sb.AppendLine();
                sb.AppendLine(LintTextFormatter.Format(report));
                hits += report.Hits.Count;
                notes += report.Notes.Count;
            }
            _txtReport.Text = sb.ToString();
            StatusChanged?.Invoke(Loc.T("StatusLintDone", hits, notes));
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(ex.Message);
        }
        finally
        {
            _btnLint.Enabled = true;
        }
    }
}
