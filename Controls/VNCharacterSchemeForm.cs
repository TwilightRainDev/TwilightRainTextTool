using TextTool.Localization;
using TextTool.Services;

namespace TextTool.Controls;

/// <summary>
/// 视觉小说角色预设方案勾选窗口 — 展示所有方案（内置+自定义），
/// 用户勾选后确定，将所选方案的角色名合并到处理引擎中。
/// 对标 ReplaceTabControl 的 SchemeSelectionForm。
/// </summary>
public sealed class VNCharacterSchemeForm : Form
{
    private TreeView _treeSchemes = null!;
    private Label _lblSummary = null!;
    private Button _btnNewScheme = null!;
    private Button _btnEditScheme = null!;
    private Button _btnDeleteScheme = null!;
    private Button _btnOK = null!;
    private Button _btnCancel = null!;

    private readonly List<VNCharacterScheme> _schemes;

    /// <summary>用户点击确定后，此处为选中的全部方案列表</summary>
    public List<VNCharacterScheme> SelectedSchemes { get; private set; } = new();

    public VNCharacterSchemeForm(List<VNCharacterScheme> schemes)
    {
        _schemes = schemes;
        Font = new Font("Microsoft YaHei UI", 10f);
        InitializeComponent();
        BuildTree();
        UpdateSummary();
    }

    private void InitializeComponent()
    {
        Text = Loc.T("VnSchemeWinTitle");
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

        // Row 0 — TreeView
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

        // Row 1 — Action buttons
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _btnNewScheme = CreateFlatBtn(Loc.T("VnSchemeNew"));
        _btnNewScheme.Click += OnNewScheme;
        layout.Controls.Add(_btnNewScheme, 0, 1);

        _btnEditScheme = CreateFlatBtn(Loc.T("VnSchemeEdit"));
        _btnEditScheme.Click += OnEditScheme;
        layout.Controls.Add(_btnEditScheme, 1, 1);

        _btnDeleteScheme = CreateFlatBtn(Loc.T("VnSchemeDelete"));
        _btnDeleteScheme.Click += OnDeleteScheme;
        layout.Controls.Add(_btnDeleteScheme, 2, 1);

        // Row 2 — Summary
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
        _btnCancel = CreateFlatBtn(Loc.T("VnSchemeCancel"));
        _btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        _btnOK = CreateFlatBtn(Loc.T("VnSchemeOK"));
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
        ControlsHelper.ApplyTheme(this);
        _lblSummary.ForeColor = ThemeManager.MutedFg;
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
            string info = scheme.Characters.Count > 0
                ? $"{scheme.Characters.Count} 个角色"
                : "无角色";
            if (scheme.RouteNames.Count > 0)
                info += $"，{scheme.RouteNames.Count} 条路线";

            var node = new TreeNode($"{prefix}{scheme.Name}  —  {scheme.Description}  ({info})")
            {
                Tag = scheme,
                Checked = false
            };

            // 子节点：角色列表
            foreach (var ch in scheme.Characters)
            {
                node.Nodes.Add(new TreeNode($"  {ch}") { Tag = null });
            }
            // 路线标题
            if (scheme.RouteNames.Count > 0)
            {
                if (scheme.Characters.Count > 0 && scheme.RouteNames.Count > 0)
                {
                    // 分隔
                }
                foreach (var rn in scheme.RouteNames)
                {
                    node.Nodes.Add(new TreeNode($"  [路线] {rn}") { Tag = null });
                }
            }

            _treeSchemes.Nodes.Add(node);
            node.Expand();
        }
    }

    private void RebuildTree()
    {
        var checkedNames = new HashSet<string>();
        foreach (TreeNode node in _treeSchemes.Nodes)
        {
            if (node.Checked && node.Tag is VNCharacterScheme s)
                checkedNames.Add(s.Name);
        }

        BuildTree();

        foreach (TreeNode node in _treeSchemes.Nodes)
        {
            if (node.Tag is VNCharacterScheme s && checkedNames.Contains(s.Name))
                node.Checked = true;
        }
    }

    // ================================================================
    //  Events
    // ================================================================

    private void OnTreeAfterCheck(object? sender, TreeViewEventArgs e)
    {
        var node = e.Node;
        if (node == null || node.Nodes.Count == 0) return;

        _treeSchemes.AfterCheck -= OnTreeAfterCheck;
        foreach (TreeNode child in node.Nodes)
            child.Checked = node.Checked;
        _treeSchemes.AfterCheck += OnTreeAfterCheck;

        UpdateSummary();
    }

    private void OnTreeDoubleClick(object? sender, EventArgs e)
    {
        if (_treeSchemes.SelectedNode?.Tag is VNCharacterScheme scheme)
            EditScheme(scheme);
    }

    private void OnNewScheme(object? sender, EventArgs e)
    {
        using var dlg = new VNEditCharacterSchemeDialog(new VNCharacterScheme
        {
            Name = "",
            Description = "",
            IsBuiltIn = false,
            Characters = new List<string>(),
            RouteNames = new List<string>()
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
        if (_treeSchemes.SelectedNode?.Tag is VNCharacterScheme scheme)
            EditScheme(scheme);
        else
            MessageBox.Show(this, Loc.T("VnSchemeSelectHint"), "", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void EditScheme(VNCharacterScheme scheme)
    {
        using var dlg = new VNEditCharacterSchemeDialog(scheme);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            RebuildTree();
            UpdateSummary();
        }
    }

    private void OnDeleteScheme(object? sender, EventArgs e)
    {
        if (_treeSchemes.SelectedNode?.Tag is VNCharacterScheme scheme)
        {
            if (MessageBox.Show(this,
                    string.Format(Loc.T("VnSchemeDeleteConfirm"), scheme.Name),
                    "", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                _schemes.Remove(scheme);
                RebuildTree();
                UpdateSummary();
            }
        }
        else
        {
            MessageBox.Show(this, Loc.T("VnSchemeSelectHint"), "", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void OnOK(object? sender, EventArgs e)
    {
        SelectedSchemes = CollectSelectedSchemes();
        DialogResult = DialogResult.OK;
        Close();
    }

    // ================================================================
    //  Helpers
    // ================================================================

    private List<VNCharacterScheme> CollectSelectedSchemes()
    {
        var result = new List<VNCharacterScheme>();
        foreach (TreeNode node in _treeSchemes.Nodes)
        {
            if (node.Checked && node.Tag is VNCharacterScheme scheme)
                result.Add(scheme);
        }
        return result;
    }

    private void UpdateSummary()
    {
        int schemeCount = 0;
        int charCount = 0;
        foreach (TreeNode node in _treeSchemes.Nodes)
        {
            if (node.Checked && node.Tag is VNCharacterScheme scheme)
            {
                schemeCount++;
                charCount += scheme.Characters.Count;
            }
        }
        _lblSummary.Text = string.Format(Loc.T("VnSchemeSummary"), schemeCount, charCount);
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
//  角色方案编辑对话框
// ================================================================

/// <summary>
/// 编辑单个角色方案的对话框（名称 + 角色列表 + 路线名）。
/// 对标 EditSchemeDialog 但用多行文本框替代规则编辑器。
/// </summary>
public sealed class VNEditCharacterSchemeDialog : Form
{
    private TextBox _txtName = null!;
    private TextBox _txtDescription = null!;
    private Label _lblCharsHeader = null!;
    private TextBox _txtCharacters = null!;
    private Label _lblRoutesHeader = null!;
    private TextBox _txtRouteNames = null!;
    private Button _btnOK = null!;
    private Button _btnCancel = null!;

    /// <summary>编辑后的方案</summary>
    public VNCharacterScheme Scheme { get; }

    public VNEditCharacterSchemeDialog(VNCharacterScheme scheme)
    {
        Scheme = scheme;
        Font = new Font("Microsoft YaHei UI", 10f);
        InitializeComponent();
        LoadScheme();
        ApplyTheme();
    }

    private void InitializeComponent()
    {
        Text = Loc.T("VnSchemeEditTitle");
        Size = new Size(520, 460);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 7,
            Padding = new Padding(12)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // Row 0 — Name
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.Controls.Add(ControlsHelper.MakeLabel(Loc.T("VnSchemeEditName")), 0, 0);
        _txtName = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right, Font = Font };
        layout.Controls.Add(_txtName, 1, 0);

        // Row 1 — Description
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.Controls.Add(ControlsHelper.MakeLabel(Loc.T("VnSchemeEditDesc")), 0, 1);
        _txtDescription = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right, Font = Font };
        layout.Controls.Add(_txtDescription, 1, 1);

        // Row 2 — Characters header
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _lblCharsHeader = new Label
        {
            Text = Loc.T("VnSchemeEditChars"),
            AutoSize = true,
            Margin = new Padding(0, 6, 0, 2),
            Font = new Font(Font.Name, 10f, FontStyle.Bold)
        };
        layout.SetColumnSpan(_lblCharsHeader, 2);
        layout.Controls.Add(_lblCharsHeader, 0, 2);

        // Row 3 — Characters textbox (multi-line)
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
        _txtCharacters = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Microsoft YaHei UI", 9f),
            AcceptsReturn = true,
            WordWrap = false
        };
        layout.SetColumnSpan(_txtCharacters, 2);
        layout.Controls.Add(_txtCharacters, 0, 3);

        // Row 4 — Route names header
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _lblRoutesHeader = new Label
        {
            Text = Loc.T("VnSchemeEditRoutes"),
            AutoSize = true,
            Margin = new Padding(0, 6, 0, 2),
            Font = new Font(Font.Name, 10f, FontStyle.Bold)
        };
        layout.SetColumnSpan(_lblRoutesHeader, 2);
        layout.Controls.Add(_lblRoutesHeader, 0, 4);

        // Row 5 — Route names textbox (multi-line)
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        _txtRouteNames = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Microsoft YaHei UI", 9f),
            AcceptsReturn = true,
            WordWrap = false
        };
        layout.SetColumnSpan(_txtRouteNames, 2);
        layout.Controls.Add(_txtRouteNames, 0, 5);

        // Row 6 — OK / Cancel
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var btnRow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 8, 0, 0)
        };
        _btnCancel = new ThemedFlatButton
        {
            Text = Loc.T("VnSchemeCancel"),
            AutoSize = true,
            FlatAppearance = { BorderSize = 0 }
        };
        _btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        btnRow.Controls.Add(_btnCancel);

        _btnOK = new ThemedFlatButton
        {
            Text = Loc.T("VnSchemeOK"),
            AutoSize = true,
            Margin = new Padding(6, 0, 0, 0),
            FlatAppearance = { BorderSize = 0 }
        };
        _btnOK.Click += OnOK;
        btnRow.Controls.Add(_btnOK);

        layout.SetColumnSpan(btnRow, 2);
        layout.Controls.Add(btnRow, 0, 6);

        Controls.Add(layout);
    }

    private void ApplyTheme()
    {
        ControlsHelper.ApplyTheme(this);
    }

    // ================================================================
    //  Data
    // ================================================================

    private void LoadScheme()
    {
        _txtName.Text = Scheme.Name;
        _txtDescription.Text = Scheme.Description;
        _txtCharacters.Text = string.Join("\r\n", Scheme.Characters);
        _txtRouteNames.Text = string.Join("\r\n", Scheme.RouteNames);
    }

    private void OnOK(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_txtName.Text))
        {
            MessageBox.Show(this, Loc.T("VnSchemeEditNameEmpty"), "", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Scheme.Name = _txtName.Text.Trim();
        Scheme.Description = _txtDescription.Text.Trim();

        // 解析角色（每行一个，去空）
        Scheme.Characters = _txtCharacters.Text
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .Distinct()
            .ToList();

        // 解析路线名（每行一个，去空）
        Scheme.RouteNames = _txtRouteNames.Text
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .Distinct()
            .ToList();

        if (Scheme.Characters.Count == 0)
        {
            MessageBox.Show(this, Loc.T("VnSchemeEditCharsEmpty"), "", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}
