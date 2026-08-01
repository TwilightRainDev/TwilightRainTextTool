using TextTool.Localization;
using TextTool.Services;

namespace TextTool.Controls;

/// <summary>
/// 预设替换方案勾选窗口 — 展示所有方案（内置+自定义），
/// 用户勾选后确定，将所选方案的规则追加到当前规则列表。
/// </summary>
public sealed class SchemeSelectionForm : SchemeSelectionFormBase<ReplaceScheme>
{
    /// <summary>用户点击确定后，此处为选中的全部规则（去重后）</summary>
    public List<ReplaceRule> SelectedRules { get; private set; } = new();

    public SchemeSelectionForm(List<ReplaceScheme> schemes)
        : base(schemes)
    {
    }

    protected override string KeyPrefix => "Scheme";

    protected override bool IsBuiltIn(ReplaceScheme scheme) => scheme.IsBuiltIn;

    protected override string GetNodeText(ReplaceScheme scheme, string prefix) =>
        $"{prefix}{scheme.Name}  —  {scheme.Description}";

    protected override IEnumerable<string> GetChildTexts(ReplaceScheme scheme) =>
        scheme.Rules.Select(rule => $"  {rule.Find}  →  {rule.Replace}");

    protected override string GetSchemeName(ReplaceScheme scheme) => scheme.Name;

    protected override int GetItemCount(ReplaceScheme scheme) => scheme.Rules.Count;

    protected override ReplaceScheme CreateNewScheme() => new()
    {
        Name = "",
        Description = "",
        IsBuiltIn = false,
        Rules = new List<ReplaceRule>()
    };

    protected override bool TryEditScheme(ReplaceScheme scheme)
    {
        using var dlg = new EditSchemeDialog(scheme);
        return dlg.ShowDialog(this) == DialogResult.OK;
    }

    protected override void OnConfirm(List<ReplaceScheme> selected)
    {
        SelectedRules = selected.SelectMany(s => s.Rules).ToList();
    }
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
        ControlsHelper.ApplyTheme(this);
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
