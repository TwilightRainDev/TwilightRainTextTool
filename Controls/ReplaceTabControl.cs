using System.Text;
using TextTool.Localization;
using TextTool.Services;

namespace TextTool.Controls;

/// <summary>
/// "标点替换" 页签：规则管理 + 对单个文件执行替换。
/// </summary>
public sealed class ReplaceTabControl : UserControl, IStatusSource, IThemedTab
{
    public event Action<string>? StatusChanged;
    public event Action<string>? ErrorOccurred;

    /// <summary>规则列表发生变化时触发（ReplaceTabControl 不再直接调持久化）</summary>
    public event Action? RulesChanged;

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
    private Button _btnPresetSchemes = null!;

    private readonly List<string> _selectedFiles = new();
    private CancellationTokenSource? _cts;   // 批处理取消令牌

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
            RowCount = 7,
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
            BackColor = ThemeManager.ControlBg,
            ForeColor = ThemeManager.Fg,
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
        _btnAddRule = new ThemedFlatButton { Text = "Add/Update", AutoSize = true, BackColor = ControlsHelper.ButtonBg, ForeColor = ControlsHelper.ButtonFg, Margin = new Padding(0, 0, 8, 0) };
        _btnAddRule.Click += OnAddOrUpdateRule;
        _btnDeleteRule = new ThemedFlatButton { Text = "Delete", AutoSize = true, BackColor = ControlsHelper.ButtonBg, ForeColor = ControlsHelper.ButtonFg };
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

        // Row 3 — 预设替换方案勾选按钮
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        _btnPresetSchemes = new ThemedFlatButton
        {
            Text = "预设替换方案勾选",
            AutoSize = true,
            BackColor = ControlsHelper.ButtonBg,
            ForeColor = ControlsHelper.ButtonFg,
            Font = new Font("Microsoft YaHei UI", 9f),
            FlatStyle = FlatStyle.Flat
        };
        _btnPresetSchemes.FlatAppearance.BorderSize = 0;
        _btnPresetSchemes.Click += OnPresetSchemes;
        layout.Controls.Add(_btnPresetSchemes, 1, 3);

        // Row 4 — Execute button
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        _btnReplaceProcess = MakePrimaryButton("Execute Replace");
        _btnReplaceProcess.Enabled = false;
        _btnReplaceProcess.Click += OnReplaceProcess;
        layout.Controls.Add(_btnReplaceProcess, 1, 4);

        // Row 5 — Output
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        _lblReplaceOutput = new Label { Text = "", ForeColor = Color.Green, Anchor = AnchorStyles.Left };
        layout.SetColumnSpan(_lblReplaceOutput, 2);
        layout.Controls.Add(_lblReplaceOutput, 1, 5);

        Controls.Add(layout);
    }

    public void ApplyLocalization()
    {
        _lblReplaceFile.Text = Loc.T("LabelSourceFile");
        _btnBrowseReplaceFile.Text = Loc.T("BtnBrowse");
        _lblReplaceEncodingTag.Text = Loc.T("LabelEncoding");
        if (_selectedFiles.Count == 0) _lblReplaceEncoding.Text = Loc.T("EncodingNotSelected");
        _lblRulesHeader.Text = Loc.T("LabelRules");
        _lblFind.Text = Loc.T("LabelFind");
        _lblReplaceWith.Text = Loc.T("LabelReplaceWith");
        _btnAddRule.Text = Loc.T("BtnAddUpdate");
        _btnDeleteRule.Text = Loc.T("BtnDelete");
        _btnUp.Text = Loc.T("BtnUp");
        _btnDown.Text = Loc.T("BtnDown");
        _lblHint.Text = Loc.T("HintRuleEdit");
        _btnPresetSchemes.Text = Loc.T("BtnPresetSchemes");
        _btnReplaceProcess.Text = Loc.T("BtnExecuteReplace");
    }

    /// <summary>公开给 MainForm 调用以应用当前主题</summary>
    public void ApplyTheme()
    {
        ControlsHelper.ApplyTheme(this);

        // 特殊覆盖（暗淡提示文字）
        _lblReplaceEncoding.ForeColor = ThemeManager.MutedFg;
        _lblHint.ForeColor = ThemeManager.MutedFg;
    }

    // ================================================================
    //  Drag & Drop
    // ================================================================

    private void OnFileDragEnter(object? sender, DragEventArgs e)
    {
        ControlsHelper.SetupFileDragEnter(e, _txtReplaceFile);
    }

    private void OnFileDragOver(object? sender, DragEventArgs e)
    {
        ControlsHelper.SetupFileDragOver(e);
    }

    private void OnFileDragLeave(object? sender, EventArgs e)
    {
        ControlsHelper.ResetFileDragLeave(_txtReplaceFile);
    }

    private void OnReplaceFileDragDrop(object? sender, DragEventArgs e)
    {
        _txtReplaceFile.BackColor = Color.White;
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
        {
            var files = (string[])e.Data!.GetData(DataFormats.FileDrop)!;
            SelectReplaceFiles(files);
        }
    }

    private void SelectReplaceFiles(string[] files)
    {
        _selectedFiles.Clear();
        var valid = new List<string>();
        foreach (var f in files)
        {
            if (File.Exists(f)) valid.Add(f);
        }
        if (valid.Count == 0) return;

        _selectedFiles.AddRange(valid);

        if (valid.Count == 1)
        {
            _txtReplaceFile.Text = valid[0];
            try
            {
                var detection = EncodingDetector.Detect(valid[0]);
                _lblReplaceEncoding.Text = detection.DisplayName;
                _lblReplaceEncoding.ForeColor = SystemColors.ControlText;
            }
            catch
            {
                // 检测失败时清空状态，避免以错误的编码继续处理
                _lblReplaceEncoding.Text = Loc.T("EncodingNotSelected");
                _lblReplaceEncoding.ForeColor = ThemeManager.MutedFg;
            }
        }
        else
        {
            _txtReplaceFile.Text = $"[{valid.Count} files selected]";
            _lblReplaceEncoding.Text = Loc.T("StatusFilesSelected", valid.Count);
            _lblReplaceEncoding.ForeColor = SystemColors.ControlText;
        }

        _btnReplaceProcess.Enabled = true;
        StatusChanged?.Invoke(Loc.T("StatusFilesSelected", valid.Count));
    }

    // ================================================================
    //  Button events
    // ================================================================

    private void OnBrowseReplaceFile(object? sender, EventArgs e)
    {
        using var dlg = ControlsHelper.CreateTextFileDialog(Loc.T("TabReplace"));
        if (dlg.ShowDialog(this) == DialogResult.OK)
            SelectReplaceFiles(dlg.FileNames);
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
        RulesChanged?.Invoke();
        SaveAndRefresh("StatusRulesSaved", _rules.Count);
        _lstRules.SelectedIndex = idx - 1;
    }

    private void OnMoveRuleDown(object? sender, EventArgs e)
    {
        int idx = _lstRules.SelectedIndex;
        if (idx < 0 || idx >= _rules.Count - 1) return;
        (_rules[idx], _rules[idx + 1]) = (_rules[idx + 1], _rules[idx]);
        RulesChanged?.Invoke();
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

        RulesChanged?.Invoke();
        SaveAndRefresh("StatusRulesSaved", _rules.Count);
    }

    private void OnDeleteRule(object? sender, EventArgs e)
    {
        if (_lstRules.SelectedIndex < 0 || _lstRules.SelectedIndex >= _rules.Count) return;

        _rules.RemoveAt(_lstRules.SelectedIndex);
        RulesChanged?.Invoke();
        SaveAndRefresh("StatusRuleDeleted", _rules.Count);
    }

    private void SaveAndRefresh(string statusKey, int ruleCount)
    {
        RefreshRuleList();
        _txtFind.Clear();
        _txtReplaceWith.Clear();
        StatusChanged?.Invoke(Loc.T(statusKey, ruleCount));
    }

    private async void OnReplaceProcess(object? sender, EventArgs e)
    {
        if (_selectedFiles.Count == 0 || string.IsNullOrWhiteSpace(_txtReplaceFile.Text)) return;
        if (_rules.Count == 0) { ErrorOccurred?.Invoke(Loc.T("MsgAddRule")); return; }

        // 运行中再点按钮 = 取消
        if (_cts != null)
        {
            _cts.Cancel();
            _btnReplaceProcess.Enabled = false;
            _btnReplaceProcess.Text = Loc.T("StatusCancelling");
            StatusChanged?.Invoke(Loc.T("StatusCancelling"));
            return;
        }

        // UI 线程捕获快照，避免后台线程访问控件
        var rules = _rules.ToList();
        var files = _selectedFiles.ToArray();

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _btnReplaceProcess.Enabled = false;
        _btnReplaceProcess.Text = Loc.T("StatusReplacing");

        var progress = new Progress<(int Done, string Error)>(
            p =>
            {
                if (string.IsNullOrEmpty(p.Error))
                    StatusChanged?.Invoke(Loc.T("StatusBatchProgress", p.Done, files.Length));
                else
                    ErrorOccurred?.Invoke(Loc.T("MsgReplaceFailed", Path.GetFileName(files[p.Done - 1]), p.Error));
            });

        var result = await ControlsHelper.RunBatchAsync(
            files,
            (path, _) =>
            {
                var encoding = EncodingDetector.DetectStrict(path);
                string outputPath = PathHelper.GetProcessedPath(path);
                string content = File.ReadAllText(path, encoding.Encoding);
                string replaced = PunctuationReplacer.Apply(content, rules);
                AtomicFile.WriteAllText(outputPath, replaced, new UTF8Encoding(true));
            },
            progress,
            token);

        try
        {
            if (_cts.IsCancellationRequested)
            {
                _btnReplaceProcess.Enabled = true;
                _btnReplaceProcess.Text = Loc.T("BtnExecuteReplace");
                string cmsg = Loc.T("StatusBatchCancelled", result.Success);
                _lblReplaceOutput.Text = cmsg;
                _lblReplaceOutput.ForeColor = Color.DarkOrange;
                StatusChanged?.Invoke(cmsg);
            }
            else
            {
                _btnReplaceProcess.Enabled = true;
                _btnReplaceProcess.Text = Loc.T("BtnExecuteReplace");
                string msg = result.Success == files.Length
                    ? Loc.T("StatusBatchComplete", result.Success)
                    : Loc.T("StatusBatchPartial", result.Success, files.Length);
                _lblReplaceOutput.Text = msg;
                _lblReplaceOutput.ForeColor = result.Success == files.Length ? Color.Green : Color.DarkOrange;
                StatusChanged?.Invoke(msg);

                if (result.Success > 0 && MessageBox.Show(this,
                    Loc.T("MsgBatchBody", result.Success),
                    Loc.T("MsgReplaceTitle"),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                {
                    RevealInExplorer(Path.GetDirectoryName(files[0])!);
                }
            }
        }
        catch (Exception ex)
        {
            _btnReplaceProcess.Enabled = true;
            _btnReplaceProcess.Text = Loc.T("BtnExecuteReplace");
            ErrorOccurred?.Invoke(Loc.T("MsgReplaceFailed", ex.Message));
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
        }
    }

    // ================================================================
    //  Preset schemes
    // ================================================================

    private void OnPresetSchemes(object? sender, EventArgs e)
    {
        List<ReplaceScheme> schemes;
        try
        {
            schemes = ReplaceSchemeStore.Load();
        }
        catch (InvalidDataException ex)
        {
            MessageBox.Show(ex.Message, "", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        using var dlg = new SchemeSelectionForm(schemes);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            var selected = dlg.SelectedRules;
            if (selected.Count == 0) return;

            int before = _rules.Count;
            _rules.AddRange(selected);
            RulesChanged?.Invoke();
            SaveAndRefresh("StatusRulesSaved", _rules.Count);

            StatusChanged?.Invoke(Loc.T("StatusSchemeApplied", selected.Count, before, _rules.Count));
        }

        // 用户可能修改了方案内容，需要持久化
        ReplaceSchemeStore.Save(schemes);
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

    private static Label MakeLabel(string text) => ControlsHelper.MakeLabel(text);

    private static ThemedFlatButton MakePrimaryButton(string text) => ControlsHelper.MakePrimaryButton(text);

    private static void RevealInExplorer(string filePath) => ControlsHelper.RevealFolder(filePath);

}
