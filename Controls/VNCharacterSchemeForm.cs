using System.Text.Json;
using System.Text.RegularExpressions;
using TextTool.Localization;
using TextTool.Services;

namespace TextTool.Controls;

/// <summary>
/// 视觉小说角色预设方案勾选窗口 — 展示所有方案（内置+自定义），
/// 用户勾选后确定，将所选方案的角色名合并到处理引擎中。
/// 复用 SchemeSelectionFormBase 泛型基类，差异点为 i18n 前缀与节点文本。
/// </summary>
public sealed class VNCharacterSchemeForm : SchemeSelectionFormBase<VNCharacterScheme>
{
    /// <summary>用户点击确定后，此处为选中的全部方案列表</summary>
    public List<VNCharacterScheme> SelectedSchemes { get; private set; } = new();

    public VNCharacterSchemeForm(List<VNCharacterScheme> schemes)
        : base(schemes)
    {
    }

    protected override string KeyPrefix => "VnScheme";

    protected override bool IsBuiltIn(VNCharacterScheme scheme) => scheme.IsBuiltIn;

    protected override string GetNodeText(VNCharacterScheme scheme, string prefix)
    {
        string info = scheme.Characters.Count > 0
            ? Loc.T("VnSchemeCharCount", scheme.Characters.Count)
            : Loc.T("VnSchemeCharNone");
        if (scheme.RouteNames.Count > 0)
            info += Loc.T("VnSchemeRouteCount", scheme.RouteNames.Count);
        return $"{prefix}{scheme.Name}  —  {scheme.Description}  ({info})";
    }

    protected override IEnumerable<string> GetChildTexts(VNCharacterScheme scheme)
    {
        foreach (var ch in scheme.Characters)
            yield return $"  {ch}";
        foreach (var rn in scheme.RouteNames)
            yield return $"  {Loc.T("VnSchemeRoutePrefix")}{rn}";
    }

    protected override string GetSchemeName(VNCharacterScheme scheme) => scheme.Name;

    protected override int GetItemCount(VNCharacterScheme scheme) => scheme.Characters.Count;

    protected override VNCharacterScheme CreateNewScheme() => new()
    {
        Name = "",
        Description = "",
        IsBuiltIn = false,
        Characters = new List<string>(),
        RouteNames = new List<string>()
    };

    protected override bool TryEditScheme(VNCharacterScheme scheme)
    {
        using var dlg = new VNEditCharacterSchemeDialog(scheme);
        return dlg.ShowDialog(this) == DialogResult.OK;
    }

    protected override void OnConfirm(List<VNCharacterScheme> selected)
    {
        SelectedSchemes = selected;
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    protected override List<VNCharacterScheme>? Deserialize(string json) =>
        JsonSerializer.Deserialize<List<VNCharacterScheme>>(json);

    protected override string Serialize(List<VNCharacterScheme> schemes) =>
        JsonSerializer.Serialize(schemes, JsonOptions);
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
    private TextBox _txtScenePattern = null!;
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
            RowCount = 8,
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

        // Row 6 — Scene pattern regex
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.Controls.Add(ControlsHelper.MakeLabel(Loc.T("VnSchemeEditScenePattern")), 0, 6);
        _txtScenePattern = new TextBox
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Font = new Font("Consolas", 9f)
        };
        layout.Controls.Add(_txtScenePattern, 1, 6);

        // Row 7 — OK / Cancel
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
        layout.Controls.Add(btnRow, 0, 7);

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
        _txtScenePattern.Text = Scheme.ScenePattern ?? "";
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

        // 场景正则（单行，空则置 null 走引擎通用模式）
        string pattern = _txtScenePattern.Text.Trim();
        if (pattern.Length > 0)
        {
            try { RegexGuard.Create(pattern); }
            catch (Exception ex)
            {
                MessageBox.Show(this, Loc.T("VnSchemeEditPatternInvalid", ex.Message), "", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Scheme.ScenePattern = pattern;
        }
        else
        {
            Scheme.ScenePattern = null;
        }

        if (Scheme.Characters.Count == 0)
        {
            MessageBox.Show(this, Loc.T("VnSchemeEditCharsEmpty"), "", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}
