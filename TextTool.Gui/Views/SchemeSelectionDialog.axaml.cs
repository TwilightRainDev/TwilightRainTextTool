using System.Runtime.CompilerServices;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using TextTool.Gui.Localization;

namespace TextTool.Gui.Views;

/// <summary>
/// 预设方案勾选窗：列全部方案（内置与自定义），勾选的子项按方案顺序拍平成一份列表交回调用方。
///
/// 方案之间的差异全在 <see cref="ISchemeAdapter"/> 里：替换方案的一行是一条规则，
/// 角色方案的一行是一个角色名或路线名。窗口只认「方案」与「可勾选的行」两层概念。
///
/// 在副本上编辑：入口处用 adapter.Clone 深拷贝传入的方案，取消时调用方手里的对象一个字段都不动。
/// 界面上一切提示都是行内文字：缺输入、导入失败、导出为空、删除确认，全都不弹系统对话框。
/// </summary>
public sealed partial class SchemeSelectionDialog : Window
{
    /// <summary>确定时交回的结果：全部方案（含本次增删改）与勾中的子项载荷，按方案顺序。</summary>
    public sealed record Result(List<object> Schemes, List<object> SelectedPayloads);

    private static readonly FilePickerFileType JsonFileType = new("JSON") { Patterns = new[] { "*.json" } };

    /// <summary>决定顶层节点文字、子项行、编辑窗与导入导出格式的那一类方案。</summary>
    private readonly ISchemeAdapter _adapter;

    /// <summary>本窗自有的方案副本，调用方传到入口的那一份不会被写。</summary>
    private readonly List<object> _schemes;

    /// <summary>
    /// 勾中的子项，按「方案对象 + 行号」记，重建树后不掉勾。
    /// 勾选粒度是行，不是方案：父项的勾是三态的，全勾、未勾、勾了一部分。
    /// 不用载荷本身做键：角色方案的一行是一个字符串，两个方案里同名的角色会被当成同一行。
    /// </summary>
    private readonly HashSet<LineKey> _selectedLines = new();

    /// <summary>没有可勾选行的方案没法用行表示勾选状态，只能单独记。</summary>
    private readonly HashSet<object> _emptyChecked = new();

    /// <summary>删除已点过一次，界面上正显示确认与取消两个按钮。</summary>
    private bool _deletePending;

    /// <summary>XAML 运行时加载器要求一个公开的无参构造；真正的入口是 <see cref="ShowAsync"/>。</summary>
    public SchemeSelectionDialog() : this(new ReplaceSchemeAdapter(), Array.Empty<object>())
    {
    }

    /// <summary>公开构造供离屏验收工具带真实方案构造；界面流程走 <see cref="ShowAsync"/>。</summary>
    public SchemeSelectionDialog(ISchemeAdapter adapter, IReadOnlyList<object> schemes)
    {
        _adapter = adapter;
        _schemes = schemes.Select(adapter.Clone).ToList();
        InitializeComponent();
        Loc.LanguageChanged += ApplyLocalization;
        Closed += (_, _) => Loc.LanguageChanged -= ApplyLocalization;
        ApplyLocalization();
        RebuildTree(null);
    }

    /// <summary>打开勾选窗。返回 null 表示取消，调用方此时不写盘。</summary>
    public static async Task<Result?> ShowAsync(
        Window owner, ISchemeAdapter adapter, IReadOnlyList<object> schemes)
    {
        var dialog = new SchemeSelectionDialog(adapter, schemes);
        return await dialog.ShowDialog<Result>(owner);
    }

    // ================================================================
    //  文案
    // ================================================================

    private void ApplyLocalization()
    {
        Title = Loc.T(_adapter.KeyPrefix + "WinTitle");
        NewButton.Content = Loc.T(_adapter.KeyPrefix + "New");
        EditButton.Content = Loc.T(_adapter.KeyPrefix + "Edit");
        DeleteButton.Content = Loc.T(_adapter.KeyPrefix + "Delete");
        ConfirmDeleteButton.Content = Loc.T(_adapter.KeyPrefix + "Delete");
        CancelDeleteButton.Content = Loc.T(_adapter.KeyPrefix + "Cancel");
        ImportButton.Content = Loc.T(_adapter.KeyPrefix + "Import");
        ExportButton.Content = Loc.T(_adapter.KeyPrefix + "Export");
        CancelButton.Content = Loc.T(_adapter.KeyPrefix + "Cancel");
        OkButton.Content = Loc.T(_adapter.KeyPrefix + "OK");
        UpdateSummary();
    }

    // ================================================================
    //  树
    // ================================================================

    /// <summary>
    /// 重建整棵树。select 给定时重建后选中该方案。
    /// 勾选状态记在 _selectedLines 里，与控件无关，所以重建不掉勾。
    /// </summary>
    private void RebuildTree(object? select)
    {
        var alive = new HashSet<object>(_schemes);
        _selectedLines.RemoveWhere(key => !alive.Contains(key.Scheme));
        _emptyChecked.RemoveWhere(scheme => !alive.Contains(scheme));

        SchemesTree.Items.Clear();

        foreach (object scheme in _schemes)
        {
            var check = new CheckBox();
            var item = new TreeViewItem
            {
                Header = MakeHeader(check, _adapter.Describe(scheme), null),
                Tag = scheme,
                IsExpanded = true,
            };
            var entry = new SchemeEntry(scheme, item, check);

            IReadOnlyList<(string Text, object Payload)> lines = _adapter.Lines(scheme);
            for (int index = 0; index < lines.Count; index++)
                item.Items.Add(CreateChildItem(entry, index, lines[index].Text));

            check.Click += (_, _) => OnSchemeCheckChanged(entry);

            // 子节点建完再同步：父项的勾是由子项汇总出来的。
            SyncEntry(entry);

            SchemesTree.Items.Add(item);

            if (ReferenceEquals(scheme, select))
                item.IsSelected = true;
        }

        UpdateSummary();
        UpdateButtons();
    }

    /// <summary>
    /// 子节点：一个可单独勾选的行。勾选框是真能点的，勾一行就只带这一行，
    /// 父项的勾选框随之变成全勾、未勾或半选。
    /// </summary>
    private TreeViewItem CreateChildItem(SchemeEntry entry, int index, string text)
    {
        var key = new LineKey(entry.Scheme, index);
        var check = new CheckBox
        {
            IsChecked = _selectedLines.Contains(key),
            VerticalAlignment = VerticalAlignment.Center,
        };

        check.Click += (_, _) => OnLineCheckChanged(entry, key, check);

        entry.Children.Add(new ChildLine(key, check));
        return new TreeViewItem { Header = MakeHeader(check, text, "muted"), Tag = entry.Item };
    }

    /// <summary>
    /// 节点的一行：勾选框在左，文字在右。文字放在 Grid 的第二列，那个列会拿到
    /// 剩余宽度，长说明才会真的换行；横向 StackPanel 给子控件的是无限宽，Wrap 不生效。
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

    /// <summary>选中的方案。子节点的 Tag 是它的父节点，所以要把它挡掉。</summary>
    private object? SelectedScheme =>
        SchemesTree.SelectedItem is TreeViewItem { Tag: object tag } && tag is not TreeViewItem ? tag : null;

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

    /// <summary>
    /// 父项点击：勾上就带上它全部子项，取消就全部去掉。
    /// CheckBox 先切换自己的状态再发 Click，所以这里读到的就是用户想要的状态；
    /// 半选状态下点一下按「全勾」处理。
    /// </summary>
    private void OnSchemeCheckChanged(SchemeEntry entry)
    {
        bool wantChecked = entry.Check.IsChecked != false;

        if (entry.Children.Count == 0)
        {
            if (wantChecked)
                _emptyChecked.Add(entry.Scheme);
            else
                _emptyChecked.Remove(entry.Scheme);
        }
        else if (wantChecked)
        {
            foreach (ChildLine line in entry.Children)
                _selectedLines.Add(line.Key);
        }
        else
        {
            foreach (ChildLine line in entry.Children)
                _selectedLines.Remove(line.Key);
        }

        SyncEntry(entry);
        UpdateSummary();
    }

    private void OnLineCheckChanged(SchemeEntry entry, LineKey key, CheckBox check)
    {
        if (check.IsChecked == true)
            _selectedLines.Add(key);
        else
            _selectedLines.Remove(key);

        SyncEntry(entry);
        UpdateSummary();
    }

    /// <summary>把子项的勾连同父项的汇总状态一起刷新。</summary>
    private void SyncEntry(SchemeEntry entry)
    {
        int checkedCount = 0;
        foreach (ChildLine line in entry.Children)
        {
            bool on = _selectedLines.Contains(line.Key);
            line.Check.IsChecked = on;
            if (on)
                checkedCount++;
        }

        if (entry.Children.Count == 0)
            entry.Check.IsChecked = _emptyChecked.Contains(entry.Scheme);
        else if (checkedCount == 0)
            entry.Check.IsChecked = false;
        else if (checkedCount == entry.Children.Count)
            entry.Check.IsChecked = true;
        else
            entry.Check.IsChecked = null; // 半选：勾了一部分
    }

    private void UpdateSummary()
    {
        int schemeCount = InvolvedSchemes().Count;
        SummaryLabel.Text = Loc.T(_adapter.KeyPrefix + "Summary", schemeCount, _selectedLines.Count);
    }

    private void UpdateButtons()
    {
        object? selected = SelectedScheme;
        EditButton.IsEnabled = selected is not null;
        DeleteButton.IsEnabled = selected is not null && !_adapter.IsBuiltIn(selected);
    }

    // ================================================================
    //  新建 / 编辑 / 删除
    // ================================================================

    private async void OnNew(object? sender, RoutedEventArgs e)
    {
        ClearInline();
        ExitDeleteConfirm();

        object created = _adapter.CreateNew();
        object? edited = await _adapter.EditAsync(this, created);
        if (edited is null)
            return;

        _schemes.Add(edited);
        RebuildTree(edited);
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

        object? edited = await _adapter.EditAsync(this, scheme);
        if (edited is null)
            return;

        // 编辑返回的是新副本，原来的子项对象都没了。原来勾过的方案，
        // 编辑后整条勾上，否则用户改一下名称就发现勾没了。
        bool wasSelected = IsInvolved(scheme);
        if (wasSelected)
        {
            int lineCount = _adapter.Lines(edited).Count;
            for (int line = 0; line < lineCount; line++)
                _selectedLines.Add(new LineKey(edited, line));

            if (lineCount == 0)
                _emptyChecked.Add(edited);
        }

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
        ShowWarning(Loc.T(_adapter.KeyPrefix + "DeleteConfirm", _adapter.Describe(scheme)));
    }

    private void OnDeleteConfirm(object? sender, RoutedEventArgs e)
    {
        object? scheme = SelectedScheme;
        ExitDeleteConfirm();

        if (scheme is null)
            return;

        _schemes.Remove(scheme);
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
            Title = Loc.T(_adapter.KeyPrefix + "Import"),
            AllowMultiple = false,
            FileTypeFilter = new[] { JsonFileType },
        });

        if (files.Count == 0)
            return;

        List<object>? imported;
        try
        {
            await using Stream stream = await files[0].OpenReadAsync();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            imported = _adapter.Deserialize(await reader.ReadToEndAsync());
        }
        catch (Exception ex)
        {
            ShowWarning(Loc.T(_adapter.KeyPrefix + "ImportFailed", ex.Message));
            return;
        }

        if (imported is null || imported.Count == 0)
        {
            ShowHint(Loc.T(_adapter.KeyPrefix + "ImportEmpty"));
            return;
        }

        var added = imported.Select(_adapter.Clone).ToList();
        _schemes.AddRange(added);
        RebuildTree(null);
        ShowHint(Loc.T(_adapter.KeyPrefix + "ImportDone", added.Count));
    }

    private async void OnExport(object? sender, RoutedEventArgs e)
    {
        ClearInline();
        ExitDeleteConfirm();

        List<object> selected = InvolvedSchemes();
        if (selected.Count == 0)
        {
            ShowHint(Loc.T(_adapter.KeyPrefix + "ExportHint"));
            return;
        }

        IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Loc.T(_adapter.KeyPrefix + "Export"),
            SuggestedFileName = "schemes.json",
            DefaultExtension = "json",
            FileTypeChoices = new[] { JsonFileType },
        });

        if (file is null)
            return;

        try
        {
            byte[] bytes = new UTF8Encoding(false).GetBytes(_adapter.Serialize(selected));
            await using Stream stream = await file.OpenWriteAsync();
            stream.SetLength(0);
            await stream.WriteAsync(bytes);
        }
        catch (Exception ex)
        {
            ShowWarning(Loc.T(_adapter.KeyPrefix + "ExportFailed", ex.Message));
            return;
        }

        ShowHint(Loc.T(_adapter.KeyPrefix + "ExportDone", selected.Count, file.TryGetLocalPath() ?? file.Name));
    }

    // ================================================================
    //  确定 / 取消
    // ================================================================

    private void OnOk(object? sender, RoutedEventArgs e) => Close(new Result(_schemes, SelectedPayloads()));

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
    //  勾选状态
    // ================================================================

    /// <summary>一个方案是否有任意一行被勾中；没有行的方案看它自己的勾。</summary>
    private bool IsInvolved(object scheme)
    {
        if (_adapter.Lines(scheme).Count == 0)
            return _emptyChecked.Contains(scheme);

        return _selectedLines.Any(key => ReferenceEquals(key.Scheme, scheme));
    }

    /// <summary>勾中过任意一行的方案，按 _schemes 的顺序。导出与摘要都用它。</summary>
    private List<object> InvolvedSchemes() => _schemes.Where(IsInvolved).ToList();

    /// <summary>勾中的载荷，按方案顺序、行顺序排列，这正是调用方要喂给处理引擎的次序。</summary>
    private List<object> SelectedPayloads()
    {
        var payloads = new List<object>();

        foreach (object scheme in _schemes)
        {
            IReadOnlyList<(string Text, object Payload)> lines = _adapter.Lines(scheme);
            for (int index = 0; index < lines.Count; index++)
            {
                if (_selectedLines.Contains(new LineKey(scheme, index)))
                    payloads.Add(lines[index].Payload);
            }
        }

        return payloads;
    }

    /// <summary>一个可勾选行的身份：哪个方案的第几行。方案按对象身份比，行按序号比。</summary>
    private sealed class LineKey(object scheme, int index)
    {
        public object Scheme { get; } = scheme;

        public int Index { get; } = index;

        public override bool Equals(object? obj) =>
            obj is LineKey other && ReferenceEquals(other.Scheme, Scheme) && other.Index == Index;

        public override int GetHashCode() => HashCode.Combine(RuntimeHelpers.GetHashCode(Scheme), Index);
    }

    /// <summary>一个顶层方案节点，连同它的勾选框与子节点勾选框。</summary>
    private sealed class SchemeEntry(object scheme, TreeViewItem item, CheckBox check)
    {
        public object Scheme { get; } = scheme;

        public TreeViewItem Item { get; } = item;

        public CheckBox Check { get; } = check;

        public List<ChildLine> Children { get; } = new();
    }

    /// <summary>子节点的一行：勾选框与它对应的行号。</summary>
    private sealed record ChildLine(LineKey Key, CheckBox Check);
}
