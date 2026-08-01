using System.Text;
using System.Text.Json;
using TextTool.Localization;
using TextTool.Services;

namespace TextTool.Controls;

/// <summary>
/// 方案勾选窗口泛型基类 — 展示所有方案（内置+自定义），用户勾选后确定。
/// SchemeSelectionForm（替换方案）与 VNCharacterSchemeForm（角色方案）
/// 共用此基类；差异点（i18n 键前缀、节点文本、子节点、编辑对话框、结果收集）由子类实现。
/// </summary>
public abstract class SchemeSelectionFormBase<T> : Form where T : class
{
    private TreeView _treeSchemes = null!;
    private Label _lblSummary = null!;
    private Button _btnNewScheme = null!;
    private Button _btnEditScheme = null!;
    private Button _btnDeleteScheme = null!;
    private Button _btnImport = null!;
    private Button _btnExport = null!;
    private Button _btnOK = null!;
    private Button _btnCancel = null!;

    /// <summary>方案列表（子类构造时传入）。</summary>
    protected readonly List<T> _schemes;

    /// <summary>i18n 键前缀：替换方案用 "Scheme"，VN 方案用 "VnScheme"。</summary>
    protected abstract string KeyPrefix { get; }

    /// <summary>是否为内置方案（内置不显示 "* " 前缀）。</summary>
    protected abstract bool IsBuiltIn(T scheme);

    /// <summary>节点主文本（含前缀与统计信息）。</summary>
    protected abstract string GetNodeText(T scheme, string prefix);

    /// <summary>子节点文本（仅展示用途）。</summary>
    protected abstract IEnumerable<string> GetChildTexts(T scheme);

    /// <summary>方案名称（用于删除确认等提示）。</summary>
    protected abstract string GetSchemeName(T scheme);

    /// <summary>方案内条目数（摘要统计：规则数 / 角色数）。</summary>
    protected abstract int GetItemCount(T scheme);

    /// <summary>创建空的新方案。</summary>
    protected abstract T CreateNewScheme();

    /// <summary>打开编辑对话框；确认返回 true。</summary>
    protected abstract bool TryEditScheme(T scheme);

    /// <summary>用户点击确定：收集选中方案并交给子类转换。</summary>
    protected abstract void OnConfirm(List<T> selected);

    /// <summary>从 JSON 反序列化方案列表。</summary>
    protected abstract List<T>? Deserialize(string json);

    /// <summary>将方案列表序列化为 JSON。</summary>
    protected abstract string Serialize(List<T> schemes);

    protected SchemeSelectionFormBase(List<T> schemes)
    {
        _schemes = schemes;
        Font = new Font("Microsoft YaHei UI", 10f);
        InitializeComponent();
        BuildTree();
        UpdateSummary();
    }

    private string L(string key) => Loc.T(KeyPrefix + key);

    private void InitializeComponent()
    {
        Text = L("WinTitle");
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

        // Row 1 — 新建 / 编辑 / 删除 / 导入 / 导出
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var actionRow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            Dock = DockStyle.Fill,
            WrapContents = false
        };
        _btnNewScheme = CreateFlatBtn(L("New"));
        _btnNewScheme.Click += OnNewScheme;
        _btnEditScheme = CreateFlatBtn(L("Edit"));
        _btnEditScheme.Click += OnEditScheme;
        _btnDeleteScheme = CreateFlatBtn(L("Delete"));
        _btnDeleteScheme.Click += OnDeleteScheme;
        _btnImport = CreateFlatBtn(L("Import"));
        _btnImport.Click += OnImport;
        _btnExport = CreateFlatBtn(L("Export"));
        _btnExport.Click += OnExport;
        actionRow.Controls.AddRange(new Control[] { _btnNewScheme, _btnEditScheme, _btnDeleteScheme, _btnImport, _btnExport });
        layout.SetColumnSpan(actionRow, 3);
        layout.Controls.Add(actionRow, 0, 1);

        // Row 2 — 摘要
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
        _btnCancel = CreateFlatBtn(L("Cancel"));
        _btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        _btnOK = CreateFlatBtn(L("OK"));
        _btnOK.Click += OnOK;
        bottomPanel.Controls.Add(_btnCancel);
        bottomPanel.Controls.Add(_btnOK);
        layout.SetColumnSpan(bottomPanel, 3);
        layout.Controls.Add(bottomPanel, 2, 3);

        Controls.Add(layout);
        ApplyTheme();
    }

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
            string prefix = IsBuiltIn(scheme) ? "" : "* ";
            var node = new TreeNode(GetNodeText(scheme, prefix))
            {
                Tag = scheme,
                Checked = false
            };

            foreach (var child in GetChildTexts(scheme))
                node.Nodes.Add(new TreeNode(child) { Tag = null });

            _treeSchemes.Nodes.Add(node);
            node.Expand();
        }
    }

    private void RebuildTree()
    {
        // 记住当前各方案的勾选状态
        var checkedNames = new HashSet<string>();
        foreach (TreeNode node in _treeSchemes.Nodes)
        {
            if (node.Checked && node.Tag is T s)
                checkedNames.Add(GetSchemeName(s));
        }

        BuildTree();

        // 恢复勾选状态
        foreach (TreeNode node in _treeSchemes.Nodes)
        {
            if (node.Tag is T s && checkedNames.Contains(GetSchemeName(s)))
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

        // 临时取消订阅避免级联触发
        _treeSchemes.AfterCheck -= OnTreeAfterCheck;
        foreach (TreeNode child in node.Nodes)
            child.Checked = node.Checked;
        _treeSchemes.AfterCheck += OnTreeAfterCheck;

        UpdateSummary();
    }

    private void OnTreeDoubleClick(object? sender, EventArgs e)
    {
        if (_treeSchemes.SelectedNode?.Tag is T scheme)
            EditScheme(scheme);
    }

    private void OnNewScheme(object? sender, EventArgs e)
    {
        T newScheme = CreateNewScheme();
        if (TryEditScheme(newScheme))
        {
            _schemes.Add(newScheme);
            RebuildTree();
            UpdateSummary();
        }
    }

    private void OnEditScheme(object? sender, EventArgs e)
    {
        if (_treeSchemes.SelectedNode?.Tag is T scheme)
            EditScheme(scheme);
        else
            MessageBox.Show(this, L("SelectHint"), "", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void EditScheme(T scheme)
    {
        if (TryEditScheme(scheme))
        {
            // 编辑对话框直接修改传入的 scheme 对象
            RebuildTree();
            UpdateSummary();
        }
    }

    private void OnDeleteScheme(object? sender, EventArgs e)
    {
        if (_treeSchemes.SelectedNode?.Tag is T scheme)
        {
            if (MessageBox.Show(this,
                    string.Format(L("DeleteConfirm"), GetSchemeName(scheme)),
                    "", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                _schemes.Remove(scheme);
                RebuildTree();
                UpdateSummary();
            }
        }
        else
        {
            MessageBox.Show(this, L("SelectHint"), "", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void OnOK(object? sender, EventArgs e)
    {
        OnConfirm(CollectSelectedSchemes());
        DialogResult = DialogResult.OK;
        Close();
    }

    // ================================================================
    //  导入 / 导出
    // ================================================================

    private void OnExport(object? sender, EventArgs e)
    {
        var selected = CollectSelectedSchemes();
        if (selected.Count == 0)
        {
            MessageBox.Show(this, L("ExportHint"), "", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dlg = new SaveFileDialog
        {
            Title = L("Export"),
            Filter = "JSON files (*.json)|*.json",
            FileName = "schemes.json",
            RestoreDirectory = true
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            File.WriteAllText(dlg.FileName, Serialize(selected), new UTF8Encoding(false));
            MessageBox.Show(this, Loc.T(KeyPrefix + "ExportDone", selected.Count, dlg.FileName),
                "", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, Loc.T(KeyPrefix + "ExportFailed", ex.Message),
                "", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnImport(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Title = L("Import"),
            Filter = "JSON files (*.json)|*.json",
            RestoreDirectory = true
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        List<T>? imported;
        try
        {
            imported = Deserialize(File.ReadAllText(dlg.FileName));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, Loc.T(KeyPrefix + "ImportFailed", ex.Message),
                "", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (imported == null || imported.Count == 0)
        {
            MessageBox.Show(this, L("ImportEmpty"), "", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _schemes.AddRange(imported);
        RebuildTree();
        UpdateSummary();
        MessageBox.Show(this, Loc.T(KeyPrefix + "ImportDone", imported.Count),
            "", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // ================================================================
    //  Helpers
    // ================================================================

    private List<T> CollectSelectedSchemes()
    {
        var result = new List<T>();
        foreach (TreeNode node in _treeSchemes.Nodes)
        {
            if (node.Checked && node.Tag is T scheme)
                result.Add(scheme);
        }
        return result;
    }

    private void UpdateSummary()
    {
        int schemeCount = 0;
        int itemCount = 0;
        foreach (TreeNode node in _treeSchemes.Nodes)
        {
            if (node.Checked && node.Tag is T scheme)
            {
                schemeCount++;
                itemCount += GetItemCount(scheme);
            }
        }
        _lblSummary.Text = string.Format(L("Summary"), schemeCount, itemCount);
    }

    private static ThemedFlatButton CreateFlatBtn(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Margin = new Padding(0, 6, 6, 0),
        FlatAppearance = { BorderSize = 0 }
    };
}
