using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using TextTool.Gui.Localization;
using TextTool.Services;

namespace TextTool.Gui.Views;

/// <summary>
/// 预设规则勾选窗：列全部判据，按组展开，勾选的规则交回调用方当作这次检查的 onlyIds。
///
/// 结构与预设方案勾选窗一致：顶层节点是规则组，子节点是一条判据，勾选粒度是判据，
/// 父项的勾由子项汇总成三态。底部一行是新建、编辑、删除，
/// 删除与新建都直接写规则文件，所以本窗关掉之后页签重读一次列表即可。
///
/// 内置判据不能删，只能取消勾选；自定义判据（Id 不在内置表里）可以删。
/// 界面上一切提示都是行内文字：至少要勾一条、删除确认、写盘失败，全都不弹系统对话框。
/// </summary>
public sealed partial class LintRuleSelectionDialog : Window
{
    /// <summary>本页要传给引擎的档位，用来置灰不适用本档的判据。</summary>
    private readonly string? _genre;

    /// <summary>判据全集，取自规则文件，含自定义。</summary>
    private List<LintRule> _rules = new();

    /// <summary>勾中的判据 Id。按 Id 记，重建树之后不掉勾。</summary>
    private HashSet<string> _checked = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>上一次读盘时的判据 Id。新出现的判据默认勾上，用户取消过的保持取消。</summary>
    private HashSet<string> _known = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>内置表里的 Id，用来判断一条判据能不能删。</summary>
    private HashSet<string> _builtIn = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>规则文件里出现过的 Id，用来在标题上标「已改」。</summary>
    private HashSet<string> _overridden = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>删除已点过一次，界面上正显示确认与取消两个按钮。</summary>
    private bool _deletePending;

    /// <summary>XAML 运行时加载器要求一个公开的无参构造；真正的入口是 <see cref="ShowAsync"/>。</summary>
    public LintRuleSelectionDialog() : this(null, null)
    {
    }

    /// <summary>公开构造供离屏验收工具带真实数据打开；界面流程走 <see cref="ShowAsync"/>。</summary>
    public LintRuleSelectionDialog(IReadOnlyCollection<string>? checkedIds, string? genre)
    {
        _genre = genre;
        InitializeComponent();
        Loc.LanguageChanged += ApplyLocalization;
        Closed += (_, _) => Loc.LanguageChanged -= ApplyLocalization;

        LoadRules(initialChecked: checkedIds);
        ApplyLocalization();
        RebuildTree(null);
    }

    /// <summary>打开勾选窗。返回 null 表示取消，调用方此时不改这次的勾选。</summary>
    public static async Task<List<string>?> ShowAsync(
        Window owner, IReadOnlyCollection<string>? checkedIds, string? genre)
    {
        var dialog = new LintRuleSelectionDialog(checkedIds, genre);
        return await dialog.ShowDialog<List<string>?>(owner);
    }

    // ================================================================
    //  文案
    // ================================================================

    private void ApplyLocalization()
    {
        Title = Loc.T("DlgLintRulePreset");
        NewButton.Content = Loc.T("BtnNewLintRule");
        EditButton.Content = Loc.T("BtnEditLintRule");
        DeleteButton.Content = Loc.T("BtnDeleteLintRule");
        ConfirmDeleteButton.Content = Loc.T("BtnDeleteLintRule");
        CancelDeleteButton.Content = Loc.T("BtnCancel");
        CancelButton.Content = Loc.T("BtnCancel");
        OkButton.Content = Loc.T("BtnOK");
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        int applicable = _rules.Count(Applies);
        SummaryLabel.Text = Loc.T("LintRulePresetSummary", _checked.Count, applicable, _rules.Count);
    }

    /// <summary>组名。组是规则文件里的分类，翻不出来时按原样显示，比空着强。</summary>
    private static string GroupTitle(string group) => group switch
    {
        "punctuation" => Loc.T("LintGroupPunctuation"),
        "lexis" => Loc.T("LintGroupLexis"),
        "syntax" => Loc.T("LintGroupSyntax"),
        "structure" => Loc.T("LintGroupStructure"),
        "statistics" => Loc.T("LintGroupStatistics"),
        _ => group,
    };

    // ================================================================
    //  读盘
    // ================================================================

    /// <summary>
    /// 读规则表。initialChecked 为空表示第一次打开：全部勾上。
    /// 每次读盘都按 Id 保留勾选状态，规则表里新出现的判据默认勾上。
    /// </summary>
    private void LoadRules(IReadOnlyCollection<string>? initialChecked = null)
    {
        HashSet<string> previous = new(_known, StringComparer.OrdinalIgnoreCase);

        try
        {
            _rules = LintRuleStore.Load();
            _overridden = new HashSet<string>(
                LintRuleStore.LoadOverrides().Select(rule => rule.Id), StringComparer.OrdinalIgnoreCase);
            _builtIn = new HashSet<string>(
                LintRuleStore.GetDefaultRules().Select(rule => rule.Id), StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or IOException
                                       or System.Text.Json.JsonException)
        {
            _rules = new();
            _overridden = new(StringComparer.OrdinalIgnoreCase);
            _builtIn = new(StringComparer.OrdinalIgnoreCase);
            ShowWarning(ex.Message);
        }

        if (initialChecked is null)
        {
            foreach (LintRule rule in _rules)
            {
                if (previous.Count == 0 || !previous.Contains(rule.Id))
                    _checked.Add(rule.Id);
            }
        }
        else
        {
            _checked = new HashSet<string>(initialChecked, StringComparer.OrdinalIgnoreCase);
        }

        // 勾选集合只保留规则表里还在的 Id，否则「已勾几条」会与实际对不上
        var alive = new HashSet<string>(_rules.Select(rule => rule.Id), StringComparer.OrdinalIgnoreCase);
        _checked.RemoveWhere(id => !alive.Contains(id));
        _known = alive;
    }

    private void ReloadRules(string? selectId, string? checkId = null)
    {
        if (checkId is not null)
            _checked.Add(checkId);

        LoadRules(new List<string>(_checked));
        RebuildTree(selectId);
    }

    // ================================================================
    //  树
    // ================================================================

    /// <summary>重建整棵树，select 给定时选中该判据。勾选记在 _checked 里，与控件无关。</summary>
    private void RebuildTree(string? selectId)
    {
        RulesTree.Items.Clear();

        var order = new List<string>();
        var byGroup = new Dictionary<string, List<LintRule>>(StringComparer.OrdinalIgnoreCase);

        foreach (LintRule rule in _rules)
        {
            if (!byGroup.TryGetValue(rule.Group, out List<LintRule>? bucket))
            {
                bucket = new List<LintRule>();
                byGroup[rule.Group] = bucket;
                order.Add(rule.Group);
            }

            bucket.Add(rule);
        }

        foreach (string group in order)
        {
            var check = new CheckBox();
            var item = new TreeViewItem
            {
                Header = MakeHeader(check, GroupTitle(group), null),
                Tag = group,
                IsExpanded = true,
            };
            var entry = new GroupEntry(group, item, check);

            foreach (LintRule rule in byGroup[group])
            {
                bool applies = Applies(rule);
                var lineCheck = new CheckBox
                {
                    IsChecked = applies && _checked.Contains(rule.Id),
                    IsEnabled = applies,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                lineCheck.Click += (_, _) => OnRuleCheckChanged(entry, rule, lineCheck);

                var child = new TreeViewItem
                {
                    Header = MakeHeader(lineCheck, LineText(rule), "muted"),
                    Tag = rule,
                };
                item.Items.Add(child);
                entry.Children.Add(new ChildLine(rule, lineCheck));

                if (selectId is not null && Same(rule.Id, selectId))
                    child.IsSelected = true;
            }

            check.Click += (_, _) => OnGroupCheckChanged(entry);

            // 子节点建完再同步：父项的勾是由子项汇总出来的。
            SyncEntry(entry);
            RulesTree.Items.Add(item);
        }

        UpdateSummary();
        UpdateButtons();
    }

    /// <summary>
    /// 节点的一行：勾选框在左，文字在右。文字放在 Grid 的第二列，那个列会拿到
    /// 剩余宽度，长标题才会真的换行；横向 StackPanel 给子控件的是无限宽，Wrap 不生效。
    /// </summary>
    private static Grid MakeHeader(CheckBox check, string text, string? mutedClass)
    {
        check.VerticalAlignment = VerticalAlignment.Center;

        var label = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0),
        };

        if (mutedClass is not null)
            label.Classes.Add(mutedClass);

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        header.Children.Add(check);
        Grid.SetColumn(label, 1);
        header.Children.Add(label);
        return header;
    }

    /// <summary>子节点文字：判据标题，改过的那条带「已改」。</summary>
    private string LineText(LintRule rule) =>
        _overridden.Contains(rule.Id) ? Loc.T("LintRuleOverridden", rule.Title) : rule.Title;

    /// <summary>这条判据在当前档位下跑不跑。规则自己的 Genres 为空表示各档通用。</summary>
    private bool Applies(LintRule rule) =>
        _genre is null
        || rule.Genres.Count == 0
        || rule.Genres.Contains(_genre, StringComparer.OrdinalIgnoreCase);

    private static bool Same(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    /// <summary>选中的判据。选中组节点时不是一条判据，编辑与删除都置灰。</summary>
    private LintRule? SelectedRule => RulesTree.SelectedItem is TreeViewItem { Tag: LintRule rule } ? rule : null;

    private void OnRuleSelected(object? sender, SelectionChangedEventArgs e)
    {
        ExitDeleteConfirm();
        UpdateButtons();
    }

    /// <summary>
    /// 组节点点击：勾上就带上它全部子项，取消就全部去掉。
    /// CheckBox 先切换自己的状态再发 Click，所以这里读到的就是用户想要的状态；
    /// 半选状态下点一下按「全勾」处理。本档不适用的判据不参与。
    /// </summary>
    private void OnGroupCheckChanged(GroupEntry entry)
    {
        bool wantChecked = entry.Check.IsChecked != false;

        foreach (ChildLine line in entry.Children)
        {
            if (!line.Check.IsEnabled)
                continue;

            if (wantChecked)
                _checked.Add(line.Rule.Id);
            else
                _checked.Remove(line.Rule.Id);
        }

        SyncEntry(entry);
        UpdateSummary();
    }

    private void OnRuleCheckChanged(GroupEntry entry, LintRule rule, CheckBox check)
    {
        if (check.IsChecked == true)
            _checked.Add(rule.Id);
        else
            _checked.Remove(rule.Id);

        SyncEntry(entry);
        UpdateSummary();
    }

    /// <summary>把子项的勾连同父项的汇总状态一起刷新。</summary>
    private void SyncEntry(GroupEntry entry)
    {
        int checkedCount = 0;
        int applicable = 0;

        foreach (ChildLine line in entry.Children)
        {
            bool on = line.Check.IsEnabled && _checked.Contains(line.Rule.Id);
            line.Check.IsChecked = on;
            if (line.Check.IsEnabled)
            {
                applicable++;
                if (on)
                    checkedCount++;
            }
        }

        if (applicable == 0)
            entry.Check.IsChecked = false;
        else if (checkedCount == 0)
            entry.Check.IsChecked = false;
        else if (checkedCount == applicable)
            entry.Check.IsChecked = true;
        else
            entry.Check.IsChecked = null; // 半选：勾了一部分
    }

    private void UpdateButtons()
    {
        LintRule? selected = SelectedRule;
        EditButton.IsEnabled = selected is not null;
        DeleteButton.IsEnabled = selected is not null && IsCustom(selected);
    }

    /// <summary>自定义判据：Id 不在内置表里，规则文件是它唯一的家，可以整条删掉。</summary>
    private bool IsCustom(LintRule rule) => !_builtIn.Contains(rule.Id);

    // ================================================================
    //  新建 / 编辑 / 删除
    // ================================================================

    /// <summary>
    /// 新建一条自定义判据。字段从选中的判据抄一份形状（组、范围、字段名都不空），
    /// 词条留空交给编辑窗填：猜一条词表出来只会让人以为是自己写的。
    /// </summary>
    private async void OnNew(object? sender, RoutedEventArgs e)
    {
        ClearInline();
        ExitDeleteConfirm();

        LintRule? template = SelectedRule ?? _rules.FirstOrDefault();
        var created = new LintRule
        {
            Id = NextCustomId(),
            Group = template?.Group ?? "lexis",
            Title = Loc.T("LintRuleNewTitle"),
            Kind = "literal",
            Patterns = new List<string>(),
            Genres = new List<string>(),
            Scope = "document",
            MinCount = 1,
            Severity = "info",
            Detail = string.Empty,
        };

        var dialog = new LintRuleEditDialog(created, overridden: false);
        await dialog.ShowDialog(this);

        if (!dialog.Changed)
            return;

        ReloadRules(created.Id, created.Id);
        ShowHint(Loc.T("LintRuleCreated", Loc.T("LintRuleNewTitle")));
    }

    private async void OnEdit(object? sender, RoutedEventArgs e) => await EditSelectedAsync();

    /// <summary>双击判据与点「编辑」同效，与方案窗的手势一致。</summary>
    private async void OnTreeDoubleTapped(object? sender, TappedEventArgs e) => await EditSelectedAsync();

    private async Task EditSelectedAsync()
    {
        if (SelectedRule is not { } rule)
            return;

        ClearInline();
        ExitDeleteConfirm();

        var dialog = new LintRuleEditDialog(rule, _overridden.Contains(rule.Id));
        await dialog.ShowDialog(this);

        if (!dialog.Changed)
            return;

        ReloadRules(rule.Id);
        ShowHint(Loc.T("LintRuleSaved", rule.Title));
    }

    /// <summary>第一次点删除：按钮原地显示确认与取消，删除确认文字写在行内。</summary>
    private void OnDeleteRequest(object? sender, RoutedEventArgs e)
    {
        if (SelectedRule is not { } rule)
            return;

        _deletePending = true;
        DeleteButton.IsVisible = false;
        DeleteConfirmPanel.IsVisible = true;
        ShowWarning(Loc.T("LintRuleDeleteConfirm", rule.Title));
    }

    private void OnDeleteConfirm(object? sender, RoutedEventArgs e)
    {
        LintRule? rule = SelectedRule;
        ExitDeleteConfirm();

        if (rule is null)
            return;

        try
        {
            List<LintRule> kept = LintRuleStore.LoadOverrides()
                .Where(item => !Same(item.Id, rule.Id))
                .ToList();
            LintRuleStore.SaveOverrides(kept);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException or IOException
                                       or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            ShowWarning(Loc.T("MsgLintRulesSaveFailed", ex.Message));
            return;
        }

        _checked.Remove(rule.Id);
        ReloadRules(null);
        ShowHint(Loc.T("LintRuleDeleted", rule.Title));
    }

    private void OnDeleteCancel(object? sender, RoutedEventArgs e) => ExitDeleteConfirm();

    private void ExitDeleteConfirm()
    {
        if (!_deletePending)
            return;

        _deletePending = false;
        DeleteButton.IsVisible = true;
        DeleteConfirmPanel.IsVisible = false;
        ClearWarning();
    }

    /// <summary>自定义判据的 Id：前缀 user- 加八位十六进制，撞上内置 Id 时再抽一次。</summary>
    private string NextCustomId()
    {
        while (true)
        {
            string id = "user-" + Guid.NewGuid().ToString("N")[..8];
            if (!_rules.Any(rule => Same(rule.Id, id)))
                return id;
        }
    }

    // ================================================================
    //  确定 / 取消
    // ================================================================

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        if (_checked.Count == 0)
        {
            ShowWarning(Loc.T("MsgLintNeedOneRule"));
            return;
        }

        // 按规则表顺序交回，与勾选先后无关，调用方与引擎都不关心勾的次序
        List<string> selected = _rules
            .Where(rule => _checked.Contains(rule.Id))
            .Select(rule => rule.Id)
            .ToList();

        Close(selected);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);

    // ================================================================
    //  行内提示
    // ================================================================

    private void ShowHint(string text)
    {
        HintLabel.Text = text;
        HintLabel.IsVisible = true;
    }

    private void ShowWarning(string text)
    {
        WarningLabel.Text = text;
        WarningLabel.IsVisible = true;
    }

    private void ClearInline()
    {
        HintLabel.Text = string.Empty;
        HintLabel.IsVisible = false;
        ClearWarning();
    }

    private void ClearWarning()
    {
        WarningLabel.Text = string.Empty;
        WarningLabel.IsVisible = false;
    }

    /// <summary>一个组节点，连同它的勾选框与子节点。</summary>
    private sealed class GroupEntry(string group, TreeViewItem item, CheckBox check)
    {
        public string Group { get; } = group;

        public TreeViewItem Item { get; } = item;

        public CheckBox Check { get; } = check;

        public List<ChildLine> Children { get; } = new();
    }

    /// <summary>组内的一条判据：判据本体与它的勾选框。</summary>
    private sealed record ChildLine(LintRule Rule, CheckBox Check);
}
