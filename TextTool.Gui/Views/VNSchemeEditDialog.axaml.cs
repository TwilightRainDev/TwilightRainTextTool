using Avalonia.Controls;
using Avalonia.Interactivity;
using TextTool.Gui.Localization;
using TextTool.Services;

namespace TextTool.Gui.Views;

/// <summary>
/// 单个角色方案的编辑窗：名称、说明、场景正则、角色列表、路线名列表。
/// 角色与路线名都用多行文本框，一行一个。
///
/// 在副本上编辑：入口处深拷贝传入的方案，取消时调用方手里的对象一个字段都不动。
/// 名称为空写在底部那行行内文字里，不弹系统对话框。
/// </summary>
public sealed partial class VNSchemeEditDialog : Window
{
    /// <summary>正在编辑的副本，确定时原样交回。</summary>
    private readonly VNCharacterScheme _scheme;

    /// <summary>XAML 运行时加载器要求一个公开的无参构造；真正的入口是 <see cref="ShowAsync"/>。</summary>
    public VNSchemeEditDialog() : this(new VNCharacterScheme())
    {
    }

    /// <summary>公开构造供离屏验收工具带真实方案构造；界面流程走 <see cref="ShowAsync"/>。</summary>
    public VNSchemeEditDialog(VNCharacterScheme scheme)
    {
        _scheme = Copy(scheme);
        InitializeComponent();
        Loc.LanguageChanged += ApplyLocalization;
        Closed += (_, _) => Loc.LanguageChanged -= ApplyLocalization;
        ApplyLocalization();

        NameBox.Text = _scheme.Name;
        DescBox.Text = _scheme.Description;
        ScenePatternBox.Text = _scheme.ScenePattern ?? "";
        CharactersBox.Text = string.Join(Environment.NewLine, _scheme.Characters);
        RouteNamesBox.Text = string.Join(Environment.NewLine, _scheme.RouteNames);
    }

    /// <summary>打开编辑窗。返回 null 表示取消，调用方此时不写盘。</summary>
    public static async Task<VNCharacterScheme?> ShowAsync(Window owner, VNCharacterScheme scheme)
    {
        var dialog = new VNSchemeEditDialog(scheme);
        return await dialog.ShowDialog<VNCharacterScheme>(owner);
    }

    // ================================================================
    //  文案
    // ================================================================

    private void ApplyLocalization()
    {
        Title = Loc.T("VnSchemeEditTitle");
        NameLabel.Text = Loc.T("VnSchemeEditName");
        DescLabel.Text = Loc.T("VnSchemeEditDesc");
        SceneLabel.Text = Loc.T("VnSchemeEditScenePattern");
        CharsLabel.Text = Loc.T("VnSchemeEditChars");
        RoutesLabel.Text = Loc.T("VnSchemeEditRoutes");
        CancelButton.Content = Loc.T("VnSchemeCancel");
        OkButton.Content = Loc.T("VnSchemeOK");
    }

    // ================================================================
    //  确定 / 取消
    // ================================================================

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        string name = (NameBox.Text ?? "").Trim();
        if (name.Length == 0)
        {
            ShowWarning(Loc.T("SchemeEditNameEmpty"));
            return;
        }

        _scheme.Name = name;
        _scheme.Description = (DescBox.Text ?? "").Trim();
        _scheme.Characters = SplitLines(CharactersBox.Text);
        _scheme.RouteNames = SplitLines(RouteNamesBox.Text);

        // 场景正则空着就走引擎的通用模式，所以空串要落成 null。
        string pattern = (ScenePatternBox.Text ?? "").Trim();
        _scheme.ScenePattern = pattern.Length > 0 ? pattern : null;

        Close(_scheme);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);

    // ================================================================
    //  行内提示
    // ================================================================

    private void ShowWarning(string text)
    {
        WarningLabel.Text = text;
        WarningLabel.IsVisible = true;
    }

    // ================================================================
    //  数据
    // ================================================================

    /// <summary>多行文本按行拆成列表：去掉空行，每行去掉首尾空白。</summary>
    private static List<string> SplitLines(string? text) =>
        (text ?? "")
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();

    /// <summary>深拷贝。json 里可能缺字段，这里一并补成空串、空表与空正则。</summary>
    private static VNCharacterScheme Copy(VNCharacterScheme scheme) => new()
    {
        Name = Text(scheme.Name),
        Description = Text(scheme.Description),
        IsBuiltIn = scheme.IsBuiltIn,
        Characters = scheme.Characters is { Count: > 0 } characters
            ? characters.Select(Text).ToList()
            : new List<string>(),
        RouteNames = scheme.RouteNames is { Count: > 0 } routes
            ? routes.Select(Text).ToList()
            : new List<string>(),
        ScenePattern = string.IsNullOrEmpty(scheme.ScenePattern) ? null : scheme.ScenePattern,
    };

    private static string Text(string? value) => value ?? "";
}
