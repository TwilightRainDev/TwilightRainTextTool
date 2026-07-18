using System.Text;
using TextTool.Localization;
using TextTool.Services;

namespace TextTool.Controls;

/// <summary>
/// "标点替换" 页签：规则管理 + 对单个文件执行替换。
/// </summary>
public sealed class ReplaceTabControl : UserControl
{
    public event Action<string>? StatusChanged;
    public event Action<string>? ErrorOccurred;

    /// <summary>与外部分享的规则列表引用（MainForm → MergeTabControl）</summary>
    public List<ReplaceRule> Rules => _rules;

    private readonly List<ReplaceRule> _rules;

    private Label _lblReplaceFile = null!;
    private TextBox _txtReplaceFile = null!;
    private Button _btnBrowseReplaceFile = null!;
    private Label _lblReplaceEncodingTag = null!;
    private Label _lblReplaceEncoding = null!;
    private Label _lblRulesHeader = null!;
    private ListBox _lstRules = null!;
    private Label _lblFind = null!;
    private TextBox _txtFind = null!;
    private Label _lblReplaceWith = null!;
    private TextBox _txtReplaceWith = null!;
    private Button _btnAddRule = null!;
    private Button _btnDeleteRule = null!;
    private Button _btnUp = null!;
    private Button _btnDown = null!;
    private Button _btnReplaceProcess = null!;
    private Label _lblReplaceOutput = null!;
    private Label _lblHint = null!;

    private DetectionResult? _lastReplaceDetection;

    public ReplaceTabControl(List<ReplaceRule> rules)
    {
        _rules = rules;
        InitializeComponent();
        RefreshRuleList();
    }

    private void InitializeComponent()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 6,
            Padding = new Padding(16, 16, 16, 8)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        // Row 0 — File selection
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        _lblReplaceFile = MakeLabel("Source:");
        layout.Controls.Add(_lblReplaceFile, 0, 0);
        _txtReplaceFile = new TextBox
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            ReadOnly = true,
            BackColor = Color.White,
            AllowDrop = true
        };
        _txtReplaceFile.DragEnter += OnFileDragEnter;
        _txtReplaceFile.DragOver += OnFileDragOver;
        _txtReplaceFile.DragLeave += OnFileDragLeave;
        _txtReplaceFile.DragDrop += OnReplaceFileDragDrop;
        layout.Controls.Add(_txtReplaceFile, 1, 0);

        _btnBrowseReplaceFile = new Button { Text = "Browse...", AutoSize = true };
        _btnBrowseReplaceFile.Click += OnBrowseReplaceFile;
        layout.Controls.Add(_btnBrowseReplaceFile, 2, 0);

        // Row 1 — Encoding hint
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        _lblReplaceEncodingTag = MakeLabel("Encoding:");
        layout.Controls.Add(_lblReplaceEncodingTag, 0, 1);
        _lblReplaceEncoding = new Label
        {
            Text = "(No file selected)",
            ForeColor = Color.Gray,
            Anchor = AnchorStyles.Left
        };
        layout.SetColumnSpan(_lblReplaceEncoding, 2);
        layout.Controls.Add(_lblReplaceEncoding, 1, 1);

        // Row 2 — Rule list + editor
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var ruleSplit = new SplitContainer
        {
            Orientation = Orientation.Vertical,
            SplitterDistance = 220,
            FixedPanel = FixedPanel.Panel1,
            Dock = DockStyle.Fill
        };

        // Left: rule list + reorder buttons
        var leftPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3
        };
        leftPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        leftPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        leftPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _lblRulesHeader = new Label { Text = "Replace Rules:", AutoSize = true, Dock = DockStyle.Top };
        _lstRules = new ListBox
        {
            Dock = DockStyle.Fill,
            IntegralHeight = false
        };
        _lstRules.SelectedIndexChanged += OnRuleSelected;

        var reorderPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            Margin = new Padding(0, 4, 0, 0)
        };
        _btnUp = new Button { Text = "Up", Size = new Size(60, 20), FlatStyle = FlatStyle.Flat, Font = new Font("Microsoft YaHei UI", 8f) };
        _btnUp.FlatAppearance.BorderSize = 0;
        _btnUp.Click += OnMoveRuleUp;
        _btnDown = new Button { Text = "Down", Size = new Size(60, 20), FlatStyle = FlatStyle.Flat, Font = new Font("Microsoft YaHei UI", 8f), Margin = new Padding(4, 0, 0, 0) };
        _btnDown.FlatAppearance.BorderSize = 0;
        _btnDown.Click += OnMoveRuleDown;
        reorderPanel.Controls.Add(_btnUp);
        reorderPanel.Controls.Add(_btnDown);

        leftPanel.Controls.Add(_lblRulesHeader, 0, 0);
        leftPanel.Controls.Add(_lstRules, 0, 1);
        leftPanel.Controls.Add(reorderPanel, 0, 2);

        // Right: rule editor
        var rightPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            Padding = new Padding(8, 0, 0, 0)
        };
        rightPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        rightPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        _lblFind = MakeLabel("Find:");
        rightPanel.Controls.Add(_lblFind, 0, 0);
        _txtFind = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right, Width = 150 };
        rightPanel.Controls.Add(_txtFind, 1, 0);

        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        _lblReplaceWith = MakeLabel("Replace:");
        rightPanel.Controls.Add(_lblReplaceWith, 0, 1);
        _txtReplaceWith = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right, Width = 150 };
        rightPanel.Controls.Add(_txtReplaceWith, 1, 1);

        rightPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var btnPanel = new FlowLayoutPanel
        {
            Anchor = AnchorStyles.Left,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true
        };
        _btnAddRule = new Button { Text = "➕ Add/Update", AutoSize = true, BackColor = Color.LightGreen, FlatStyle = FlatStyle.Flat };
        _btnAddRule.FlatAppearance.BorderSize = 0;
        _btnAddRule.Click += OnAddOrUpdateRule;
        _btnDeleteRule = new Button { Text = "🗑 Delete", AutoSize = true, BackColor = Color.LightCoral, FlatStyle = FlatStyle.Flat, Margin = new Padding(8, 0, 0, 0) };
        _btnDeleteRule.FlatAppearance.BorderSize = 0;
        _btnDeleteRule.Click += OnDeleteRule;
        btnPanel.Controls.Add(_btnAddRule);
        btnPanel.Controls.Add(_btnDeleteRule);
        rightPanel.SetColumnSpan(btnPanel, 2);
        rightPanel.Controls.Add(btnPanel, 0, 2);

        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        _lblHint = new Label
        {
            Text = "Hint: select a rule to edit it",
            ForeColor = Color.Gray,
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 8f)
        };
        rightPanel.SetColumnSpan(_lblHint, 2);
        rightPanel.Controls.Add(_lblHint, 0, 3);

        ruleSplit.Panel1.Controls.Add(leftPanel);
        ruleSplit.Panel2.Controls.Add(rightPanel);
        layout.SetColumnSpan(ruleSplit, 3);
        layout.Controls.Add(ruleSplit, 0, 2);

        // Row 3 — Execute button
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        _btnReplaceProcess = MakePrimaryButton("Execute Replace");
        _btnReplaceProcess.Enabled = false;
        _btnReplaceProcess.Click += OnReplaceProcess;
        layout.Controls.Add(_btnReplaceProcess, 1, 3);

        // Row 4 — Output
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        _lblReplaceOutput = new Label { Text = "", ForeColor = Color.Green, Anchor = AnchorStyles.Left };
        layout.SetColumnSpan(_lblReplaceOutput, 2);
        layout.Controls.Add(_lblReplaceOutput, 1, 4);

        Controls.Add(layout);
    }

    public void ApplyLocalization()
    {
        _lblReplaceFile.Text = Loc.T("LabelSourceFile");
        _btnBrowseReplaceFile.Text = Loc.T("BtnBrowse");
        _lblReplaceEncodingTag.Text = Loc.T("LabelEncoding");
        if (_lastReplaceDetection == null) _lblReplaceEncoding.Text = Loc.T("EncodingNotSelected");
        _lblRulesHeader.Text = Loc.T("LabelRules");
        _lblFind.Text = Loc.T("LabelFind");
        _lblReplaceWith.Text = Loc.T("LabelReplaceWith");
        _btnAddRule.Text = Loc.T("BtnAddUpdate");
        _btnDeleteRule.Text = Loc.T("BtnDelete");
        _btnUp.Text = Loc.T("BtnUp");
        _btnDown.Text = Loc.T("BtnDown");
        _lblHint.Text = Loc.T("HintRuleEdit");
        _btnReplaceProcess.Text = Loc.T("BtnExecuteReplace");
    }

    // ================================================================
    //  Drag & Drop
    // ================================================================

    private void OnFileDragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
        {
            e.Effect = DragDropEffects.Copy;
            _txtReplaceFile.BackColor = Color.LemonChiffon;
        }
        else
        {
            e.Effect = DragDropEffects.None;
        }
    }

    private void OnFileDragOver(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
            e.Effect = DragDropEffects.Copy;
    }

    private void OnFileDragLeave(object? sender, EventArgs e)
    {
        _txtReplaceFile.BackColor = Color.White;
    }

    private void OnReplaceFileDragDrop(object? sender, DragEventArgs e)
    {
        _txtReplaceFile.BackColor = Color.White;
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            SelectReplaceFile(files[0]);
    }

    private void SelectReplaceFile(string path)
    {
        if (!File.Exists(path)) { ErrorOccurred?.Invoke(Loc.T("MsgFileNotFound", path)); return; }
        _txtReplaceFile.Text = path;
        try
        {
            _lastReplaceDetection = EncodingDetector.Detect(path);
            _lblReplaceEncoding.Text = _lastReplaceDetection.DisplayName;
            _lblReplaceEncoding.ForeColor = SystemColors.ControlText;
            _btnReplaceProcess.Enabled = true;
        }
        catch (Exception ex) { ErrorOccurred?.Invoke(Loc.T("MsgReadFailed", ex.Message)); }
    }

    // ================================================================
    //  Button events
    // ================================================================

    private void OnBrowseReplaceFile(object? sender, EventArgs e)
    {
        using var dlg = OpenTextFileDialog(Loc.T("TabReplace"));
        if (dlg.ShowDialog(this) == DialogResult.OK)
            SelectReplaceFile(dlg.FileName);
    }

    private void OnRuleSelected(object? sender, EventArgs e)
    {
        if (_lstRules.SelectedIndex >= 0 && _lstRules.SelectedIndex < _rules.Count)
        {
            var rule = _rules[_lstRules.SelectedIndex];
            _txtFind.Text = rule.Find;
            _txtReplaceWith.Text = rule.Replace;
        }
    }

    private void OnMoveRuleUp(object? sender, EventArgs e)
    {
        int idx = _lstRules.SelectedIndex;
        if (idx <= 0 || idx >= _rules.Count) return;
        (_rules[idx], _rules[idx - 1]) = (_rules[idx - 1], _rules[idx]);
        ReplaceRuleStore.Save(_rules);
        SaveAndRefresh("StatusRulesSaved", _rules.Count);
        _lstRules.SelectedIndex = idx - 1;
    }

    private void OnMoveRuleDown(object? sender, EventArgs e)
    {
        int idx = _lstRules.SelectedIndex;
        if (idx < 0 || idx >= _rules.Count - 1) return;
        (_rules[idx], _rules[idx + 1]) = (_rules[idx + 1], _rules[idx]);
        ReplaceRuleStore.Save(_rules);
        SaveAndRefresh("StatusRulesSaved", _rules.Count);
        _lstRules.SelectedIndex = idx + 1;
    }

    private void OnAddOrUpdateRule(object? sender, EventArgs e)
    {
        string find = _txtFind.Text;
        string replace = _txtReplaceWith.Text;
        if (string.IsNullOrEmpty(find)) { ErrorOccurred?.Invoke(Loc.T("MsgEnterFind")); return; }

        if (_lstRules.SelectedIndex >= 0 && _lstRules.SelectedIndex < _rules.Count)
            _rules[_lstRules.SelectedIndex] = new ReplaceRule { Find = find, Replace = replace };
        else
            _rules.Add(new ReplaceRule { Find = find, Replace = replace });

        ReplaceRuleStore.Save(_rules);
        SaveAndRefresh("StatusRulesSaved", _rules.Count);
    }

    private void OnDeleteRule(object? sender, EventArgs e)
    {
        if (_lstRules.SelectedIndex < 0 || _lstRules.SelectedIndex >= _rules.Count) return;

        _rules.RemoveAt(_lstRules.SelectedIndex);
        ReplaceRuleStore.Save(_rules);
        SaveAndRefresh("StatusRuleDeleted", _rules.Count);
    }

    private void SaveAndRefresh(string statusKey, int ruleCount)
    {
        RefreshRuleList();
        _txtFind.Clear();
        _txtReplaceWith.Clear();
        StatusChanged?.Invoke(Loc.T(statusKey, ruleCount));
    }

    private void OnReplaceProcess(object? sender, EventArgs e)
    {
        if (_lastReplaceDetection == null || string.IsNullOrWhiteSpace(_txtReplaceFile.Text)) return;
        if (_rules.Count == 0) { ErrorOccurred?.Invoke(Loc.T("MsgAddRule")); return; }

        try
        {
            _btnReplaceProcess.Enabled = false;
            _btnReplaceProcess.Text = Loc.T("StatusReplacing");
            StatusChanged?.Invoke(Loc.T("StatusReplacing"));

            string dir = Path.GetDirectoryName(_txtReplaceFile.Text) ?? ".";
            string name = Path.GetFileNameWithoutExtension(_txtReplaceFile.Text);
            string ext = Path.GetExtension(_txtReplaceFile.Text);
            string outputPath = Path.Combine(dir, $"{name}_Processed{ext}");

            string content = File.ReadAllText(_txtReplaceFile.Text, _lastReplaceDetection.Encoding);
            string replaced = PunctuationReplacer.Apply(content, _rules);
            File.WriteAllText(outputPath, replaced, new UTF8Encoding(true));

            _lblReplaceOutput.Text = Loc.T("LabelOutput", outputPath);
            _lblReplaceOutput.ForeColor = Color.Green;
            StatusChanged?.Invoke(Loc.T("StatusReplaceComplete", outputPath));

            if (MessageBox.Show(this,
                Loc.T("MsgReplaceBody", outputPath),
                Loc.T("MsgReplaceTitle"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
            {
                RevealInExplorer(outputPath);
            }
        }
        catch (Exception ex) { ErrorOccurred?.Invoke(Loc.T("MsgReplaceFailed", ex.Message)); }
        finally { _btnReplaceProcess.Enabled = true; _btnReplaceProcess.Text = Loc.T("BtnExecuteReplace"); }
    }

    // ================================================================
    //  Rule list refresh
    // ================================================================

    private void RefreshRuleList()
    {
        _lstRules.Items.Clear();
        foreach (var rule in _rules)
            _lstRules.Items.Add($"{rule.Find}  →  {rule.Replace}");
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
