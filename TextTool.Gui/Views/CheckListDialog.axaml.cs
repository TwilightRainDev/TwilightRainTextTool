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
/// 勾选窗：列一份清单，勾要的那几项，点确定交回调用方。
///
/// 窗口只认两层：顶层节点，和它下面可单独勾的行。顶层节点自己的勾由可勾的行汇总，
/// 汇总成全部勾上、一个没勾、勾了一部分三种状态；没有子行的顶层节点自己就是一勾。
///
/// 换一类清单不改窗口：树上有哪些节点和行、哪些能编辑删除、点确定交回什么，全由
/// <see cref="ICheckListAdapter"/> 回答。替换方案、角色方案、检查判据共用这一个窗口类
/// 和 CheckListDialog.axaml 这一份布局，差别在各自的适配器里。
///
/// 界面上一切提示都是行内文字：缺输入、导入失败、导出为空、删除确认，都不弹系统对话框。
/// </summary>
public sealed partial class CheckListDialog : Window
{
    /// <summary>决定树、按钮与结果形状的那一类清单。</summary>
    private readonly ICheckListAdapter _adapter;

    /// <summary>能导入导出的那一类才有这两个按钮；null 表示这一类没有，按钮不出现在界面上。</summary>
    private readonly IImportExportAdapter? _transfer;

    /// <summary>勾选表，与适配器共用同一份。勾选记在这里，重建树不掉勾。</summary>
    private readonly CheckState _state = new();

    /// <summary>删除已点过一次，界面上正显示确认与取消两个按钮。</summary>
    private bool _deletePending;

    /// <summary>
    /// XAML 运行时加载器要求一个公开的无参构造，它构造出的是一个没数据的空窗；
    /// 界面流程走 <see cref="ShowAsync"/>，验收工具用带适配器的构造。
    /// </summary>
    public CheckListDialog() : this(new ReplaceSchemeAdapter(Array.Empty<ReplaceScheme>()))
    {
    }

    /// <summary>公开构造供离屏验收工具带真实数据构造；界面流程走 <see cref="ShowAsync"/>。</summary>
    public CheckListDialog(ICheckListAdapter adapter)
    {
        _adapter = adapter;
        _transfer = adapter as IImportExportAdapter;

        InitializeComponent();

        if (_adapter.Attach(_state) is { } warning)
            ShowWarning(warning);

        Loc.LanguageChanged += ApplyLocalization;
        Closed += (_, _) => Loc.LanguageChanged -= ApplyLocalization;

        ApplyLocalization();
        Rebuild(null);
    }

    /// <summary>
    /// 打开勾选窗。返回值是适配器给出的结果，形状由它定，调用方按自己的类型认；
    /// null 表示取消，调用方此时什么都不改。
    /// </summary>
    public static async Task<object?> ShowAsync(Window owner, ICheckListAdapter adapter)
    {
        var dialog = new CheckListDialog(adapter);
        return await dialog.ShowDialog<object>(owner);
    }

    // ================================================================
    //  文案
    // ================================================================

    /// <summary>按当前语言刷窗标题、按钮文字与摘要。</summary>
    private void ApplyLocalization()
    {
        Title = _adapter.Title;
        NewButton.Content = _adapter.NewText;
        EditButton.Content = _adapter.EditText;
        DeleteButton.Content = _adapter.DeleteText;
        ConfirmDeleteButton.Content = _adapter.DeleteText;
        CancelDeleteButton.Content = _adapter.CancelText;
        CancelButton.Content = _adapter.CancelText;
        OkButton.Content = _adapter.OkText;

        ImportButton.IsVisible = _transfer is not null;
        ExportButton.IsVisible = _transfer is not null;
        ImportButton.Content = _transfer?.ImportText;
        ExportButton.Content = _transfer?.ExportText;

        UpdateSummary();
    }

    /// <summary>摘要那一行：勾了几条、适用几条、一共几条。</summary>
    private void UpdateSummary() => SummaryLabel.Text = _adapter.Summary;

    /// <summary>按当前选中项刷新按钮可用状态。</summary>
    private void UpdateButtons()
    {
        object? selected = Selected;
        EditButton.IsEnabled = _adapter.CanEdit(selected);
        DeleteButton.IsEnabled = _adapter.CanDelete(selected);
    }

    // ================================================================
    //  树
    // ================================================================

    /// <summary>
    /// 重建整棵树。select 交给适配器，由它决定重建后选中哪个节点或哪一行。
    /// 勾选状态记在 <see cref="CheckState"/> 里，与控件无关，重建不掉勾。
    /// </summary>
    private void Rebuild(object? select)
    {
        CheckTree.Items.Clear();

        foreach (CheckTreeNode node in _adapter.Nodes(select))
        {
            var check = new CheckBox();
            var row = new Row(node.Key, check);
            var item = new TreeViewItem
            {
                Header = MakeHeader(check, node.Text, null),
                Tag = row,
                IsExpanded = true,
            };

            foreach (CheckTreeLine line in node.Lines)
            {
                var lineCheck = new CheckBox
                {
                    IsEnabled = line.Enabled,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                lineCheck.Click += (_, _) => ToggleLine(row, line, lineCheck);

                var child = new TreeViewItem
                {
                    Header = MakeHeader(lineCheck, line.Text, "muted"),
                    Tag = new LineItem(item, row, line),
                };
                item.Items.Add(child);
                row.Lines.Add((line, lineCheck));

                if (line.Selected)
                    child.IsSelected = true;
            }

            check.Click += (_, _) => ToggleNode(row);

            // 子节点建完再同步：父项的勾是由子项汇总出来的。
            SyncRow(row);
            CheckTree.Items.Add(item);

            if (node.Selected)
                item.IsSelected = true;
        }

        UpdateSummary();
        UpdateButtons();
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

    /// <summary>
    /// 顶层节点点击：勾上就带上它全部可勾的行，取消就全部去掉。
    /// CheckBox 先切换自己的状态再发 Click，所以这里读到的就是用户想要的状态；
    /// 半选状态下点一下按「全勾」处理。置灰的行不参与。
    /// </summary>
    private void ToggleNode(Row row)
    {
        bool wantChecked = row.Check.IsChecked != false;

        if (row.Lines.Count == 0)
        {
            _state.SetRoot(row.Key, wantChecked);
        }
        else
        {
            foreach ((CheckTreeLine line, _) in row.Lines)
            {
                if (line.Enabled)
                    _state.Set(line.Key, wantChecked);
            }
        }

        SyncRow(row);
        UpdateSummary();
    }

    private void ToggleLine(Row row, CheckTreeLine line, CheckBox check)
    {
        _state.Set(line.Key, check.IsChecked == true);
        SyncRow(row);
        UpdateSummary();
    }

    /// <summary>把行的勾连同父项的汇总状态一起刷新。</summary>
    private void SyncRow(Row row)
    {
        int checkedCount = 0;
        int applicable = 0;

        foreach ((CheckTreeLine line, CheckBox check) in row.Lines)
        {
            bool on = line.Enabled && _state.IsChecked(line.Key);
            check.IsChecked = on;
            if (line.Enabled)
            {
                applicable++;
                if (on)
                    checkedCount++;
            }
        }

        if (row.Lines.Count == 0)
            row.Check.IsChecked = _state.IsRootChecked(row.Key);
        else if (checkedCount == 0 || applicable == 0)
            row.Check.IsChecked = false;
        else if (checkedCount == applicable)
            row.Check.IsChecked = true;
        else
            row.Check.IsChecked = null; // 半选：勾了一部分
    }

    /// <summary>
    /// 选中的那个：选的是顶层节点就交它自己的键，选的是子行就交那一行给的对象。
    /// 适配器认哪一种由它自己定。
    /// </summary>
    private object? Selected =>
        CheckTree.SelectedItem switch
        {
            TreeViewItem { Tag: Row row } => row.Key,
            TreeViewItem { Tag: LineItem line } => line.Line.Tag ?? line.Line.Key,
            _ => null,
        };

    // ================================================================
    //  选中项
    // ================================================================

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e) => OnTreeSelectionChanged();

    /// <summary>
    /// 选中项变了。子行若等同于它所属的顶层节点，把选中挪到那个节点上；
    /// 否则退出删除确认，并按新选中刷新按钮。
    /// </summary>
    private void OnTreeSelectionChanged()
    {
        if (_adapter.PromotesLineSelection && CheckTree.SelectedItem is TreeViewItem { Tag: LineItem line })
        {
            line.Parent.IsSelected = true;
            return;
        }

        ExitDeleteConfirm();
        UpdateButtons();
    }

    /// <summary>双击节点与点「编辑」同效。</summary>
    private async void OnDoubleTapped(object? sender, TappedEventArgs e) => await EditSelectedAsync();

    // ================================================================
    //  新建 / 编辑 / 删除
    // ================================================================

    private async void OnNew(object? sender, RoutedEventArgs e)
    {
        ClearInline();
        ExitDeleteConfirm();
        Apply(await _adapter.NewAsync(this, Selected));
    }

    private async void OnEdit(object? sender, RoutedEventArgs e) => await EditSelectedAsync();

    private async Task EditSelectedAsync()
    {
        if (Selected is not { } selected)
            return;

        ClearInline();
        ExitDeleteConfirm();
        Apply(await _adapter.EditAsync(this, selected));
    }

    /// <summary>第一次点删除：按钮原地换成确认与取消，确认文字写在行内。</summary>
    private void OnDeleteRequest(object? sender, RoutedEventArgs e)
    {
        if (Selected is not { } selected)
            return;

        BeginDeleteConfirm(_adapter.DeleteConfirmText(selected));
    }

    private void OnDeleteConfirm(object? sender, RoutedEventArgs e)
    {
        object? selected = Selected;
        ExitDeleteConfirm();

        if (selected is null)
            return;

        Apply(_adapter.Delete(selected));
    }

    private void OnDeleteCancel(object? sender, RoutedEventArgs e) => ExitDeleteConfirm();

    // ================================================================
    //  导入 / 导出
    // ================================================================

    private async void OnImport(object? sender, RoutedEventArgs e)
    {
        if (_transfer is null)
            return;

        ClearInline();
        ExitDeleteConfirm();
        Apply(await _transfer.ImportAsync(this));
    }

    private async void OnExport(object? sender, RoutedEventArgs e)
    {
        if (_transfer is null)
            return;

        ClearInline();
        ExitDeleteConfirm();
        Apply(await _transfer.ExportAsync(this));
    }

    /// <summary>照适配器的回话收尾：需要提示就写进提示区，需要重画就带着它要的选中项重画。</summary>
    private void Apply(CheckOutcome outcome)
    {
        if (outcome.Hint is { } hint)
            ShowHint(hint);

        if (outcome.Warning is { } warning)
            ShowWarning(warning);

        if (outcome.Rebuild)
            Rebuild(outcome.Selected);
    }

    // ================================================================
    //  确定 / 取消
    // ================================================================

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        if (_state.CheckedCount == 0 && _adapter.NothingCheckedWarning is { } warning)
        {
            ShowWarning(warning);
            return;
        }

        Close(_adapter.Result());
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);

    // ================================================================
    //  删除确认
    // ================================================================

    private void BeginDeleteConfirm(string text)
    {
        _deletePending = true;
        DeleteButton.IsVisible = false;
        DeleteConfirmPanel.IsVisible = true;
        ShowWarning(text);
    }

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

    // ================================================================
    //  树上的一项
    // ================================================================

    /// <summary>一个顶层节点，连同它的勾选框与子行的勾选框。</summary>
    private sealed class Row(object key, CheckBox check)
    {
        public object Key { get; } = key;

        public CheckBox Check { get; } = check;

        public List<(CheckTreeLine Line, CheckBox Check)> Lines { get; } = new();
    }

    /// <summary>顶层节点下的一行，带着它所属的节点控件：要把它提升成节点选中时用得上。</summary>
    private sealed record LineItem(TreeViewItem Parent, Row Row, CheckTreeLine Line);
}
