using TextTool.Localization;
using TextTool.Services;

namespace TextTool.Controls;

/// <summary>
/// 预设替换方案勾选窗口 — 展示所有方案（内置+自定义），
/// 用户勾选后确定，将所选方案的规则追加到当前规则列表。
/// </summary>
public sealed class SchemeSelectionForm : Form
{
    private TreeView _treeSchemes = null!;
    private Label _lblSummary = null!;
    private Button _btnNewScheme = null!;
    private Button _btnEditScheme = null!;
    private Button _btnDeleteScheme = null!;
    private Button _btnOK = null!;
    private Button _btnCancel = null!;

    private readonly List<ReplaceScheme> _schemes;
    private bool _isChecking; // 防止 TreeView AfterCheck 级联触发死循环

    /// <summary>用户点击确定后，此处为选中的全部规则（去重后）</summary>
    public List<ReplaceRule> SelectedRules { get; private set; } = new();

    public SchemeSelectionForm(List<ReplaceScheme> schemes)
    {
        _schemes = schemes;
        Font = new Font("Microsoft YaHei UI", 10f);
        InitializeComponent();
        BuildTree();
        UpdateSummary();
    }

    private void InitializeComponent()
    {
        Text = Loc.T("SchemeWinTitle");
        Size = new Size(620, 520);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 4,
            Padding = new Padding(12)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        // Row 0 — TreeView (fills)
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _treeSchemes = new TreeView
        {
            CheckBoxes = true,
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle,
            Font = Font
        };
        _treeSchemes.AfterCheck += OnTreeAfterCheck;
        _treeSchemes.DoubleClick += OnTreeDoubleClick;
        layout.SetColumnSpan(_treeSchemes, 3);
        layout.Controls.Add(_treeSchemes, 0, 0);

        // Row 1 — Action buttons (New / Edit / Delete)
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _btnNewScheme = CreateFlatBtn(Loc.T("SchemeNew"));
        _btnNewScheme.Click += OnNewScheme;
        layout.Controls.Add(_btnNewScheme, 0, 1);

        _btnEditScheme = CreateFlatBtn(Loc.T("SchemeEdit"));
        _btnEditScheme.Click += OnEditScheme;
        layout.Controls.Add(_btnEditScheme, 1, 1);

        _btnDeleteScheme = CreateFlatBtn(Loc.T("SchemeDelete"));
        _btnDeleteScheme.Click += OnDeleteScheme;
        layout.Controls.Add(_btnDeleteScheme, 2, 1);

        // Row 2 — Summary line
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _lblSummary = new Label
        {
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 0),
            Font = new Font(Font.Name, 9f)
        };
        layout.SetColumnSpan(_lblSummary, 3);
        layout.Controls.Add(_lblSummary, 0, 2);

        // Row 3 — OK / Cancel
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var bottomPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 8, 0, 0)
        };
        _btnCancel = CreateFlatBtn(Loc.T("SchemeCancel"));
        _btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        _btnOK = CreateFlatBtn(Loc.T("SchemeOK"));
        _btnOK.Click += OnOK;
        bottomPanel.Controls.Add(_btnCancel);
        bottomPanel.Controls.Add(_btnOK);
        layout.SetColumnSpan(bottomPanel, 3);
        layout.Controls.Add(bottomPanel, 2, 3);

        Controls.Add(layout);
        ApplyTheme();
    }

    // ================================================================
    //  Theme
    // ================================================================

    private void ApplyTheme()
    {
        BackColor = ThemeManager.Bg;
        ForeColor = ThemeManager.Fg;
        _treeSchemes.BackColor = ThemeManager.ControlBg;
        _treeSchemes.ForeColor = ThemeManager.Fg;
        _lblSummary.ForeColor = ThemeManager.MutedFg;

        ApplyButtonThemeRecursive(this);
    }

    private static void ApplyButtonThemeRecursive(Control parent)
    {
        foreach (Control ctl in parent.Controls)
        {
            if (ctl is Button btn)
            {
                btn.BackColor = ControlsHelper.ButtonBg;
                btn.ForeColor = ControlsHelper.ButtonFg;
            }
            if (ctl.HasChildren)
                ApplyButtonThemeRecursive(ctl);
        }
    }

    // ================================================================
    //  Tree construction
    // ================================================================

    private void BuildTree()
    {
        _treeSchemes.Nodes.Clear();
        foreach (var scheme in _schemes)
        {
            string prefix = scheme.IsBuiltIn ? "" : "* ";
            var node = new TreeNode($"{prefix}{scheme.Name}  —  {scheme.Description}")
            {
                Tag = scheme,
                Checked = false
            };

            foreach (var rule in scheme.Rules)
            {
                var ruleNode = new TreeNode($"  {rule.Find}  →  {rule.Replace}")
                {
                    Tag = null // 子节点仅用于展示
                };
                node.Nodes.Add(ruleNode);
            }

            _treeSchemes.Nodes.Add(node);
            node.Expand(); // 默认展开，方便查看规则
        }
    }

    /// <summary>重新构建树（增删方案后调用）</summary>
    private void RebuildTree()
    {
        // 记住当前各方案的勾选状态
        var checkedNames = new HashSet<string>();
        foreach (TreeNode node in _treeSchemes.Nodes)
        {
            if (node.Checked && node.Tag is ReplaceScheme s)
                checkedNames.Add(s.Name);
        }

        BuildTree();

        // 恢复勾选状态
        foreach (TreeNode node in _treeSchemes.Nodes)
        {
            if (node.Tag is ReplaceScheme s && checkedNames.Contains(s.Name))
                node.Checked = true;
        }
    }

    // ================================================================
    //  Events
    // ================================================================

    private void OnTreeAfterCheck(object? sender, TreeViewEventArgs e)
    {
        if (_isChecking) return;
        _isChecking = true;

        var node = e.Node;
        if (node == null) { _isChecking = false; return; }

        // 级联：父节点 ↔ 所有子节点
        if (node.Nodes.Count > 0)
        {
            foreach (TreeNode child in node.Nodes)
                child.Checked = node.Checked;
        }

        _isChecking = false;
        UpdateSummary();
    }

    private void OnTreeDoubleClick(object? sender, EventArgs e)
    {
        if (_treeSchemes.SelectedNode?.Tag is ReplaceScheme scheme)
            EditScheme(scheme);
    }

    private void OnNewScheme(object? sender, EventArgs e)
    {
        using var dlg = new EditSchemeDialog(new ReplaceScheme
        {
            Name = "",
            Description = "",
            IsBuiltIn = false,
            Rules = new List<ReplaceRule>()
        });
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _schemes.Add(dlg.Scheme);
            RebuildTree();
            UpdateSummary();
        }
    }

    private void OnEditScheme(object? sender, EventArgs e)
    {
        if (_treeSchemes.SelectedNode?.Tag is ReplaceScheme scheme)
            EditScheme(scheme);
        else
            MessageBox.Show(this, Loc.T("SchemeSelectHint"), "", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void EditScheme(ReplaceScheme scheme)
    {
        using var dlg = new EditSchemeDialog(scheme);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            // 直接在原对象上修改（dlg.Scheme 与 _schemes 中的引用一致）
            var src = dlg.Scheme;
            scheme.Name = src.Name;
            scheme.Description = src.Description;
            scheme.Rules = src.Rules;
            RebuildTree();
            UpdateSummary();
        }
    }

    private void OnDeleteScheme(object? sender, EventArgs e)
    {
        if (_treeSchemes.SelectedNode?.Tag is ReplaceScheme scheme)
        {
            if (MessageBox.Show(this,
                    string.Format(Loc.T("SchemeDeleteConfirm"), scheme.Name),
                    "", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                _schemes.Remove(scheme);
                RebuildTree();
                UpdateSummary();
            }
        }
        else
        {
            MessageBox.Show(this, Loc.T("SchemeSelectHint"), "", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void OnOK(object? sender, EventArgs e)
    {
        SelectedRules = CollectSelectedRules();
        DialogResult = DialogResult.OK;
        Close();
    }

    // ================================================================
    //  Helpers
    // ================================================================

    private List<ReplaceRule> CollectSelectedRules()
    {
        var rules = new List<ReplaceRule>();
        foreach (TreeNode node in _treeSchemes.Nodes)
        {
            if (node.Checked && node.Tag is ReplaceScheme scheme)
            {
                rules.AddRange(scheme.Rules);
            }
        }
        return rules;
    }

    private void UpdateSummary()
    {
        int schemeCount = 0;
        int ruleCount = 0;
        foreach (TreeNode node in _treeSchemes.Nodes)
        {
            if (node.Checked && node.Tag is ReplaceScheme scheme)
            {
                schemeCount++;
                ruleCount += scheme.Rules.Count;
            }
        }
        _lblSummary.Text = string.Format(Loc.T("SchemeSummary"), schemeCount, ruleCount);
    }

    private static ThemedFlatButton CreateFlatBtn(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Margin = new Padding(0, 6, 6, 0),
        FlatAppearance = { BorderSize = 0 }
    };
}

// ================================================================
//  方案编辑对话框
// ================================================================

/// <summary>
/// 编辑单个方案的对话框（名称 + 规则列表）
/// </summary>
public sealed class EditSchemeDialog : Form
{
    private TextBox _txtName = null!;
    private TextBox _txtDescription = null!;
    private ListBox _lstRules = null!;
    private TextBox _txtFind = null!;
    private TextBox _txtReplace = null!;
    private Button _btnAddRule = null!;
    private Button _btnDeleteRule = null!;
    private Button _btnOK = null!;
    private Button _btnCancel = null!;

    /// <summary>编辑后的方案</summary>
    public ReplaceScheme Scheme { get; }

    public EditSchemeDialog(ReplaceScheme scheme)
    {
        Scheme = scheme;
        Font = new Font("Microsoft YaHei UI", 10f);
        InitializeComponent();
        LoadScheme();
        ApplyTheme();
    }

    private void InitializeComponent()
    {
        Text = Loc.T("SchemeEditTitle");
        Size = new Size(520, 420);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 6,
            Padding = new Padding(12)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // Row 0 — Name
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.Controls.Add(ControlsHelper.MakeLabel(Loc.T("SchemeEditName")), 0, 0);
        _txtName = new TextBox
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Font = Font
        };
        layout.Controls.Add(_txtName, 1, 0);

        // Row 1 — Description
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.Controls.Add(ControlsHelper.MakeLabel(Loc.T("SchemeEditDesc")), 0, 1);
        _txtDescription = new TextBox
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Font = Font
        };
        layout.Controls.Add(_txtDescription, 1, 1);

        // Row 2 — Rules list header
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var lblRulesHeader = new Label
        {
            Text = Loc.T("SchemeEditRules"),
            AutoSize = true,
            Margin = new Padding(0, 6, 0, 2),
            Font = new Font(Font.Name, 10f, FontStyle.Bold)
        };
        layout.SetColumnSpan(lblRulesHeader, 2);
        layout.Controls.Add(lblRulesHeader, 0, 2);

        // Row 3 — Rules list
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _lstRules = new ListBox
        {
            Dock = DockStyle.Fill,
            IntegralHeight = false,
            Font = new Font("Consolas", 9f)
        };
        _lstRules.SelectedIndexChanged += OnRuleSelected;
        layout.SetColumnSpan(_lstRules, 2);
        layout.Controls.Add(_lstRules, 0, 3);

        // Row 4 — Find / Replace + Add/Delete buttons
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var editorPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            Margin = new Padding(0, 4, 0, 0)
        };
        editorPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        editorPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        editorPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        editorPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        editorPanel.Controls.Add(new Label
        {
            Text = Loc.T("SchemeEditFind"),
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Anchor = AnchorStyles.Left
        }, 0, 0);
        _txtFind = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right, Font = new Font("Consolas", 9f) };
        editorPanel.Controls.Add(_txtFind, 1, 0);

        editorPanel.Controls.Add(new Label
        {
            Text = Loc.T("SchemeEditReplace"),
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(8, 0, 0, 0)
        }, 2, 0);
        _txtReplace = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right, Font = new Font("Consolas", 9f) };
        editorPanel.Controls.Add(_txtReplace, 3, 0);

        layout.SetColumnSpan(editorPanel, 2);
        layout.Controls.Add(editorPanel, 0, 4);

        // Row 5 — Add/Delete + OK/Cancel
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var btnRow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 4, 0, 0)
        };
        _btnAddRule = new ThemedFlatButton
        {
            Text = Loc.T("SchemeEditAdd"),
            AutoSize = true,
            FlatAppearance = { BorderSize = 0 }
        };
        _btnAddRule.Click += OnAddRule;
        btnRow.Controls.Add(_btnAddRule);

        _btnDeleteRule = new ThemedFlatButton
        {
            Text = Loc.T("SchemeEditDelete"),
            AutoSize = true,
            Margin = new Padding(8, 0, 0, 0),
            FlatAppearance = { BorderSize = 0 }
        };
        _btnDeleteRule.Click += OnDeleteRule;
        btnRow.Controls.Add(_btnDeleteRule);

        btnRow.Controls.Add(new Label { AutoSize = true, Text = "" });

        _btnCancel = new ThemedFlatButton
        {
            Text = Loc.T("SchemeCancel"),
            AutoSize = true,
            FlatAppearance = { BorderSize = 0 }
        };
        _btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        btnRow.Controls.Add(_btnCancel);

        _btnOK = new ThemedFlatButton
        {
            Text = Loc.T("SchemeOK"),
            AutoSize = true,
            Margin = new Padding(6, 0, 0, 0),
            FlatAppearance = { BorderSize = 0 }
        };
        _btnOK.Click += OnOK;
        btnRow.Controls.Add(_btnOK);

        layout.SetColumnSpan(btnRow, 2);
        layout.Controls.Add(btnRow, 0, 5);

        Controls.Add(layout);
    }

    private void ApplyTheme()
    {
        BackColor = ThemeManager.Bg;
        ForeColor = ThemeManager.Fg;
        _txtName.BackColor = ThemeManager.ControlBg;
        _txtName.ForeColor = ThemeManager.Fg;
        _txtDescription.BackColor = ThemeManager.ControlBg;
        _txtDescription.ForeColor = ThemeManager.Fg;
        _lstRules.BackColor = ThemeManager.ControlBg;
        _lstRules.ForeColor = ThemeManager.Fg;
        _txtFind.BackColor = ThemeManager.ControlBg;
        _txtFind.ForeColor = ThemeManager.Fg;
        _txtReplace.BackColor = ThemeManager.ControlBg;
        _txtReplace.ForeColor = ThemeManager.Fg;

        ApplyButtonThemeRecursive(this);
    }

    private static void ApplyButtonThemeRecursive(Control parent)
    {
        foreach (Control ctl in parent.Controls)
        {
            if (ctl is ThemedFlatButton btn)
            {
                btn.BackColor = ControlsHelper.ButtonBg;
                btn.ForeColor = ControlsHelper.ButtonFg;
            }
            if (ctl.HasChildren)
                ApplyButtonThemeRecursive(ctl);
        }
    }

    // ================================================================
    //  Data loading
    // ================================================================

    private void LoadScheme()
    {
        _txtName.Text = Scheme.Name;
        _txtDescription.Text = Scheme.Description;
        RefreshRuleList();
    }

    private void RefreshRuleList()
    {
        _lstRules.Items.Clear();
        foreach (var rule in Scheme.Rules)
            _lstRules.Items.Add($"{rule.Find}  →  {rule.Replace}");
    }

    // ================================================================
    //  Events
    // ================================================================

    private void OnRuleSelected(object? sender, EventArgs e)
    {
        if (_lstRules.SelectedIndex >= 0 && _lstRules.SelectedIndex < Scheme.Rules.Count)
        {
            var rule = Scheme.Rules[_lstRules.SelectedIndex];
            _txtFind.Text = rule.Find;
            _txtReplace.Text = rule.Replace;
        }
    }

    private void OnAddRule(object? sender, EventArgs e)
    {
        string find = _txtFind.Text;
        string replace = _txtReplace.Text;
        if (string.IsNullOrEmpty(find))
        {
            MessageBox.Show(this, Loc.T("MsgEnterFind"), "", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_lstRules.SelectedIndex >= 0 && _lstRules.SelectedIndex < Scheme.Rules.Count)
        {
            // 更新现有规则
            Scheme.Rules[_lstRules.SelectedIndex] = new ReplaceRule { Find = find, Replace = replace };
        }
        else
        {
            // 添加新规则
            Scheme.Rules.Add(new ReplaceRule { Find = find, Replace = replace });
        }

        RefreshRuleList();
        _txtFind.Clear();
        _txtReplace.Clear();
    }

    private void OnDeleteRule(object? sender, EventArgs e)
    {
        if (_lstRules.SelectedIndex < 0 || _lstRules.SelectedIndex >= Scheme.Rules.Count)
            return;

        Scheme.Rules.RemoveAt(_lstRules.SelectedIndex);
        RefreshRuleList();
        _txtFind.Clear();
        _txtReplace.Clear();
    }

    private void OnOK(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_txtName.Text))
        {
            MessageBox.Show(this, Loc.T("SchemeEditNameEmpty"), "", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Scheme.Name = _txtName.Text.Trim();
        Scheme.Description = _txtDescription.Text.Trim();
        DialogResult = DialogResult.OK;
        Close();
    }
}
