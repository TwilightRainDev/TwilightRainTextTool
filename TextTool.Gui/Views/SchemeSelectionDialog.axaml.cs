using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using TextTool.Gui.Localization;
using TextTool.Services;

namespace TextTool.Gui.Views;

/// <summary>
/// 预设替换方案勾选窗：列全部方案（内置与自定义），勾选的方案按顺序拍平成规则给人用。
///
/// 在副本上编辑：入口处深拷贝传入的方案，取消时调用方手里的对象一个字段都不动。
/// 界面上一切提示都是行内文字：缺输入、导入失败、导出为空、删除确认，全都不弹系统对话框。
/// </summary>
public sealed partial class SchemeSelectionDialog : Window
{
    /// <summary>确定时交回的结果：全部方案（含本次增删改）与勾选方案拍平后的规则。</summary>
    public sealed record Result(List<ReplaceScheme> Schemes, List<ReplaceRule> SelectedRules);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private static readonly FilePickerFileType JsonFileType = new("JSON") { Patterns = new[] { "*.json" } };

    /// <summary>本窗自有的方案副本，调用方传到入口的那一份不会被写。</summary>
    private readonly List<ReplaceScheme> _schemes;

    /// <summary>勾选中的方案，按对象身份记，重建树后不掉勾。</summary>
    private readonly HashSet<ReplaceScheme> _checked = new();

    private readonly List<SchemeEntry> _entries = new();

    /// <summary>删除已点过一次，界面上正显示确认与取消两个按钮。</summary>
    private bool _deletePending;

    /// <summary>XAML 运行时加载器要求一个公开的无参构造；真正的入口是 <see cref="ShowAsync"/>。</summary>
    public SchemeSelectionDialog() : this(Array.Empty<ReplaceScheme>())
    {
    }

    /// <summary>公开构造供离屏验收工具带真实方案构造；界面流程走 <see cref="ShowAsync"/>。</summary>
    public SchemeSelectionDialog(IReadOnlyList<ReplaceScheme> schemes)
    {
        _schemes = schemes.Select(Clone).ToList();
        InitializeComponent();
        Loc.LanguageChanged += ApplyLocalization;
        Closed += (_, _) => Loc.LanguageChanged -= ApplyLocalization;
        ApplyLocalization();
        RebuildTree(null);
    }

    /// <summary>打开勾选窗。返回 null 表示取消，调用方此时不写盘。</summary>
    public static async Task<Result?> ShowAsync(Window owner, IReadOnlyList<ReplaceScheme> schemes)
    {
        var dialog = new SchemeSelectionDialog(schemes);
        return await dialog.ShowDialog<Result>(owner);
    }

    // ================================================================
    //  文案
    // ================================================================

    private void ApplyLocalization()
    {
        Title = Loc.T("SchemeWinTitle");
        NewButton.Content = Loc.T("SchemeNew");
        EditButton.Content = Loc.T("SchemeEdit");
        DeleteButton.Content = Loc.T("SchemeDelete");
        ConfirmDeleteButton.Content = Loc.T("SchemeDelete");
        CancelDeleteButton.Content = Loc.T("SchemeCancel");
        ImportButton.Content = Loc.T("SchemeImport");
        ExportButton.Content = Loc.T("SchemeExport");
        CancelButton.Content = Loc.T("SchemeCancel");
        OkButton.Content = Loc.T("SchemeOK");
        UpdateSummary();
    }

    // ================================================================
    //  树
    // ================================================================

    /// <summary>
    /// 重建整棵树。select 给定时重建后选中该方案。
    /// 勾选状态记在 _checked 里，与控件无关，所以重建不掉勾。
    /// </summary>
    private void RebuildTree(ReplaceScheme? select)
    {
        _checked.RemoveWhere(scheme => !_schemes.Contains(scheme));

        SchemesTree.Items.Clear();
        _entries.Clear();

        foreach (ReplaceScheme scheme in _schemes)
        {
            var check = new CheckBox
            {
                IsChecked = _checked.Contains(scheme),
                VerticalAlignment = VerticalAlignment.Center,
            };

            var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            header.Children.Add(check);
            header.Children.Add(new TextBlock
            {
                Text = SchemeText(scheme),
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
            });

            var item = new TreeViewItem { Header = header, Tag = scheme, IsExpanded = true };
            var entry = new SchemeEntry(scheme, item, check);

            foreach (ReplaceRule rule in scheme.Rules)
                item.Items.Add(CreateChildItem(entry, rule));

            check.Click += (_, _) => OnSchemeCheckChanged(entry);

            _entries.Add(entry);
            SchemesTree.Items.Add(item);

            if (ReferenceEquals(scheme, select))
                item.IsSelected = true;
        }

        UpdateSummary();
        UpdateButtons();
    }

    /// <summary>
    /// 子节点：一条规则的只读说明。勾选框跟着方案走，不接受单独点击，
    /// 因为勾选的粒度就是方案本身。
    /// </summary>
    private static TreeViewItem CreateChildItem(SchemeEntry entry, ReplaceRule rule)
    {
        var check = new CheckBox
        {
            IsChecked = entry.Check.IsChecked == true,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Focusable = false,
        };

        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        header.Children.Add(check);
        header.Children.Add(new TextBlock
        {
            Text = rule.Find + "  →  " + rule.Replace,
            Classes = { "muted" },
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        });

        entry.ChildChecks.Add(check);
        return new TreeViewItem { Header = header, Tag = entry.Item };
    }

    private static string SchemeText(ReplaceScheme scheme)
    {
        string name = scheme.IsBuiltIn ? scheme.Name : "* " + scheme.Name;
        return string.IsNullOrEmpty(scheme.Description) ? name : name + " : " + scheme.Description;
    }

    private ReplaceScheme? SelectedScheme =>
        SchemesTree.SelectedItem is TreeViewItem { Tag: ReplaceScheme scheme } ? scheme : null;

    private void OnSchemeSelected(object? sender, SelectionChangedEventArgs e)
    {
        // 选中子节点等同于选中它的父方案，否则下方按钮会因为「没选中」而全部变灰。
        if (SchemesTree.SelectedItem is TreeViewItem { Tag: TreeViewItem parent })
        {
            parent.IsSelected = true;
            return;
        }

        ExitDeleteConfirm();
        UpdateButtons();
    }

    /// <summary>勾上就带上全部子节点，取消同理。</summary>
    private void OnSchemeCheckChanged(SchemeEntry entry)
    {
        bool isChecked = entry.Check.IsChecked == true;

        if (isChecked)
            _checked.Add(entry.Scheme);
        else
            _checked.Remove(entry.Scheme);

        foreach (CheckBox child in entry.ChildChecks)
            child.IsChecked = isChecked;

        UpdateSummary();
    }

    private void UpdateSummary()
    {
        int schemeCount = 0;
        int ruleCount = 0;

        foreach (ReplaceScheme scheme in _schemes)
        {
            if (!_checked.Contains(scheme))
                continue;

            schemeCount++;
            ruleCount += scheme.Rules.Count;
        }

        SummaryLabel.Text = Loc.T("SchemeSummary", schemeCount, ruleCount);
    }

    private void UpdateButtons()
    {
        ReplaceScheme? selected = SelectedScheme;
        EditButton.IsEnabled = selected is not null;
        DeleteButton.IsEnabled = selected is not null && !selected.IsBuiltIn;
    }

    // ================================================================
    //  新建 / 编辑 / 删除
    // ================================================================

    private async void OnNew(object? sender, RoutedEventArgs e)
    {
        ClearInline();
        ExitDeleteConfirm();

        ReplaceScheme? created = await SchemeEditDialog.ShowAsync(this, new ReplaceScheme());
        if (created is null)
            return;

        _schemes.Add(created);
        RebuildTree(created);
    }

    private async void OnEdit(object? sender, RoutedEventArgs e) => await EditSelectedAsync();

    /// <summary>双击方案节点与点「编辑方案」同效，旧界面就有这个手势。</summary>
    private async void OnTreeDoubleTapped(object? sender, TappedEventArgs e) => await EditSelectedAsync();

    private async Task EditSelectedAsync()
    {
        if (SelectedScheme is not { } scheme)
            return;

        int index = _schemes.IndexOf(scheme);
        if (index < 0)
            return;

        ClearInline();
        ExitDeleteConfirm();

        ReplaceScheme? edited = await SchemeEditDialog.ShowAsync(this, scheme);
        if (edited is null)
            return;

        // 编辑返回的是新副本，勾选状态按身份跟着搬过去。
        if (_checked.Remove(scheme))
            _checked.Add(edited);

        _schemes[index] = edited;
        RebuildTree(edited);
    }

    /// <summary>第一次点删除：按钮原地换成确认与取消，删除确认文字写在行内。</summary>
    private void OnDeleteRequest(object? sender, RoutedEventArgs e)
    {
        if (SelectedScheme is not { } scheme)
            return;

        _deletePending = true;
        DeleteButton.IsVisible = false;
        DeleteConfirmPanel.IsVisible = true;
        ShowWarning(Loc.T("SchemeDeleteConfirm", scheme.Name));
    }

    private void OnDeleteConfirm(object? sender, RoutedEventArgs e)
    {
        ReplaceScheme? scheme = SelectedScheme;
        ExitDeleteConfirm();

        if (scheme is null)
            return;

        _schemes.Remove(scheme);
        _checked.Remove(scheme);
        RebuildTree(null);
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

    // ================================================================
    //  导入 / 导出
    // ================================================================

    private async void OnImport(object? sender, RoutedEventArgs e)
    {
        ClearInline();
        ExitDeleteConfirm();

        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Loc.T("SchemeImport"),
            AllowMultiple = false,
            FileTypeFilter = new[] { JsonFileType },
        });

        if (files.Count == 0)
            return;

        List<ReplaceScheme>? imported;
        try
        {
            await using Stream stream = await files[0].OpenReadAsync();
            imported = await JsonSerializer.DeserializeAsync<List<ReplaceScheme>>(stream, JsonOptions);
        }
        catch (Exception ex)
        {
            ShowWarning(Loc.T("SchemeImportFailed", ex.Message));
            return;
        }

        if (imported is null || imported.Count == 0)
        {
            ShowHint(Loc.T("SchemeImportEmpty"));
            return;
        }

        var added = imported.Select(Clone).ToList();
        _schemes.AddRange(added);
        RebuildTree(null);
        ShowHint(Loc.T("SchemeImportDone", added.Count));
    }

    private async void OnExport(object? sender, RoutedEventArgs e)
    {
        ClearInline();
        ExitDeleteConfirm();

        var selected = _schemes.Where(_checked.Contains).ToList();
        if (selected.Count == 0)
        {
            ShowHint(Loc.T("SchemeExportHint"));
            return;
        }

        IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Loc.T("SchemeExport"),
            SuggestedFileName = "schemes.json",
            DefaultExtension = "json",
            FileTypeChoices = new[] { JsonFileType },
        });

        if (file is null)
            return;

        try
        {
            byte[] bytes = new UTF8Encoding(false).GetBytes(JsonSerializer.Serialize(selected, JsonOptions));
            await using Stream stream = await file.OpenWriteAsync();
            stream.SetLength(0);
            await stream.WriteAsync(bytes);
        }
        catch (Exception ex)
        {
            ShowWarning(Loc.T("SchemeExportFailed", ex.Message));
            return;
        }

        ShowHint(Loc.T("SchemeExportDone", selected.Count, file.TryGetLocalPath() ?? file.Name));
    }

    // ================================================================
    //  确定 / 取消
    // ================================================================

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        var rules = _schemes.Where(_checked.Contains).SelectMany(scheme => scheme.Rules).ToList();
        Close(new Result(_schemes, rules));
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
        HintLabel.Text = "";
        HintLabel.IsVisible = false;
        ClearWarning();
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

    /// <summary>一个顶层方案节点，连同它的勾选框与子节点勾选框。</summary>
    private sealed class SchemeEntry(ReplaceScheme scheme, TreeViewItem item, CheckBox check)
    {
        public ReplaceScheme Scheme { get; } = scheme;

        public TreeViewItem Item { get; } = item;

        public CheckBox Check { get; } = check;

        public List<CheckBox> ChildChecks { get; } = new();
    }
}
