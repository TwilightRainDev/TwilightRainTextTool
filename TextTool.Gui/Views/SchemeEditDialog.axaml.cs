using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using TextTool.Gui.Localization;
using TextTool.Services;

namespace TextTool.Gui.Views;

/// <summary>
/// 单个方案的编辑窗：名称、说明、规则列表，以及底部一条规则的添加、更新、删除。
///
/// 在副本上编辑：入口处深拷贝传入的方案，取消时调用方手里的对象一个字段都不动。
/// 查找为空、名称为空都写在规则列表下方的行内文字里，不弹系统对话框。
/// 规则保持原有顺序，界面不提供排序。
/// </summary>
public sealed partial class SchemeEditDialog : Window
{
    /// <summary>正在编辑的副本，确定时原样交回。</summary>
    private readonly ReplaceScheme _scheme;

    private readonly List<ReplaceRule> _rules;

    private readonly ObservableCollection<string> _ruleTexts = new();

    /// <summary>XAML 运行时加载器要求一个公开的无参构造；真正的入口是 <see cref="ShowAsync"/>。</summary>
    public SchemeEditDialog() : this(new ReplaceScheme())
    {
    }

    /// <summary>公开构造供离屏验收工具带真实方案构造；界面流程走 <see cref="ShowAsync"/>。</summary>
    public SchemeEditDialog(ReplaceScheme scheme)
    {
        _scheme = Clone(scheme);
        _rules = _scheme.Rules;
        InitializeComponent();
        RulesList.ItemsSource = _ruleTexts;
        Loc.LanguageChanged += ApplyLocalization;
        Closed += (_, _) => Loc.LanguageChanged -= ApplyLocalization;
        ApplyLocalization();

        NameBox.Text = _scheme.Name;
        DescBox.Text = _scheme.Description;
        RefreshRules();
    }

    /// <summary>打开编辑窗。返回 null 表示取消，调用方此时不写盘。</summary>
    public static async Task<ReplaceScheme?> ShowAsync(Window owner, ReplaceScheme scheme)
    {
        var dialog = new SchemeEditDialog(scheme);
        return await dialog.ShowDialog<ReplaceScheme>(owner);
    }

    // ================================================================
    //  文案
    // ================================================================

    private void ApplyLocalization()
    {
        Title = Loc.T("SchemeEditTitle");
        NameLabel.Text = Loc.T("SchemeEditName");
        DescLabel.Text = Loc.T("SchemeEditDesc");
        RulesLabel.Text = Loc.T("SchemeEditRules");
        FindLabel.Text = Loc.T("SchemeEditFind");
        ReplaceLabel.Text = Loc.T("SchemeEditReplace");
        AddUpdateButton.Content = Loc.T("SchemeEditAdd");
        DeleteRuleButton.Content = Loc.T("SchemeEditDelete");
        CancelButton.Content = Loc.T("SchemeCancel");
        OkButton.Content = Loc.T("SchemeOK");
    }

    // ================================================================
    //  规则列表
    // ================================================================

    /// <summary>按当前规则重建列表，并清掉选中，列表与输入框回到互不相干的状态。</summary>
    private void RefreshRules()
    {
        _ruleTexts.Clear();
        foreach (ReplaceRule rule in _rules)
            _ruleTexts.Add(rule.Find + "  →  " + rule.Replace);

        RulesList.SelectedIndex = -1;
        DeleteRuleButton.IsEnabled = false;
    }

    private void OnRuleSelected(object? sender, SelectionChangedEventArgs e)
    {
        int index = RulesList.SelectedIndex;
        if (index < 0 || index >= _rules.Count)
        {
            DeleteRuleButton.IsEnabled = false;
            return;
        }

        FindBox.Text = _rules[index].Find;
        ReplaceBox.Text = _rules[index].Replace;
        DeleteRuleButton.IsEnabled = true;
    }

    // ================================================================
    //  规则的增删改
    // ================================================================

    private void OnAddOrUpdate(object? sender, RoutedEventArgs e)
    {
        string find = FindBox.Text ?? "";
        string replace = ReplaceBox.Text ?? "";

        if (find.Length == 0)
        {
            ShowWarning(Loc.T("MsgEnterFind"));
            return;
        }

        ClearWarning();

        int index = RulesList.SelectedIndex;
        if (index >= 0 && index < _rules.Count)
            _rules[index] = new ReplaceRule { Find = find, Replace = replace };
        else
            _rules.Add(new ReplaceRule { Find = find, Replace = replace });

        RefreshRules();
        FindBox.Text = "";
        ReplaceBox.Text = "";
    }

    private void OnDeleteRule(object? sender, RoutedEventArgs e)
    {
        int index = RulesList.SelectedIndex;
        if (index < 0 || index >= _rules.Count)
            return;

        _rules.RemoveAt(index);
        RefreshRules();
        FindBox.Text = "";
        ReplaceBox.Text = "";
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

    private void ClearWarning()
    {
        WarningLabel.Text = "";
        WarningLabel.IsVisible = false;
    }

    // ================================================================
    //  数据
    // ================================================================

    /// <summary>深拷贝。json 里可能缺字段，这里一并补成空串与空表。</summary>
    private static ReplaceScheme Clone(ReplaceScheme scheme) => new()
    {
        Name = Text(scheme.Name),
        Description = Text(scheme.Description),
        IsBuiltIn = scheme.IsBuiltIn,
        Rules = scheme.Rules is { Count: > 0 } rules
            ? rules.Select(rule => new ReplaceRule
            {
                Find = Text(rule.Find),
                Replace = Text(rule.Replace),
            }).ToList()
            : new List<ReplaceRule>(),
    };

    private static string Text(string? value) => value ?? "";
}
