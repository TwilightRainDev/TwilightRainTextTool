using System.Text;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using TextTool.Gui.Localization;

namespace TextTool.Gui.Views;

/// <summary>
/// 方案这一类的通用流程：在副本上编辑、勾选记忆、新建与删除、导入导出 json、点确定交回什么。
///
/// 一类方案自己的差异（顶层文字、行、编辑窗、序列化）由 <see cref="ISchemeAdapter"/> 给出，
/// 子类只实现那几项，窗里看不见方案之间的区别。
///
/// 在副本上编辑：构造时记下调用方给的方案，<see cref="Attach"/> 时逐个深拷贝，
/// 取消时调用方手里的对象一个字段都不动。
/// </summary>
public abstract class SchemeAdapterBase : ISchemeAdapter, IImportExportAdapter
{
    private static readonly FilePickerFileType JsonFileType = new("JSON") { Patterns = new[] { "*.json" } };

    /// <summary>调用方给的方案，深拷贝一份之后才动它。</summary>
    private readonly IReadOnlyList<object> _source;

    /// <summary>本窗自有的方案副本。</summary>
    private List<object> _schemes = new();

    /// <summary>勾选表，<see cref="Attach"/> 时由窗交过来。</summary>
    private CheckState _state = new();

    private protected SchemeAdapterBase(IEnumerable<object> schemes) => _source = schemes.ToList();

    public string? Attach(CheckState state)
    {
        _state = state;
        _schemes = _source.Select(Clone).ToList();
        return null;
    }

    // ================================================================
    //  差异点：一类方案与另一类的区别，由子类给，说明在 ISchemeAdapter 上
    // ================================================================

    public abstract string KeyPrefix { get; }

    public abstract string Describe(object scheme);

    public abstract IReadOnlyList<(string Text, object Payload)> Lines(object scheme);

    public abstract bool IsBuiltIn(object scheme);

    public abstract object CreateNew();

    public abstract Task<object?> OpenEditorAsync(Window owner, object scheme);

    public abstract object Clone(object scheme);

    public abstract string Serialize(IEnumerable<object> schemes);

    public abstract List<object>? Deserialize(string json);

    // ================================================================
    //  文案
    // ================================================================

    public string Title => Loc.T(KeyPrefix + "WinTitle");

    public string NewText => Loc.T(KeyPrefix + "New");

    public string EditText => Loc.T(KeyPrefix + "Edit");

    public string DeleteText => Loc.T(KeyPrefix + "Delete");

    public string CancelText => Loc.T(KeyPrefix + "Cancel");

    public string OkText => Loc.T(KeyPrefix + "OK");

    public string ImportText => Loc.T(KeyPrefix + "Import");

    public string ExportText => Loc.T(KeyPrefix + "Export");

    /// <summary>方案窗零勾也放行：回调用方一个空列表，含义是不套用任何预设。</summary>
    public string? NothingCheckedWarning => null;

    public string Summary => Loc.T(KeyPrefix + "Summary", InvolvedSchemes().Count, _state.CheckedCount);

    public string DeleteConfirmText(object selected) => Loc.T(KeyPrefix + "DeleteConfirm", Describe(selected));

    // ================================================================
    //  树
    // ================================================================

    /// <summary>勾中任意一行，它所属的方案就算选中了，所以选中子行要挪到父节点上。</summary>
    public bool PromotesLineSelection => true;

    public IReadOnlyList<CheckTreeNode> Nodes(object? select)
    {
        var nodes = new List<CheckTreeNode>();

        foreach (object scheme in _schemes)
        {
            IReadOnlyList<(string Text, object Payload)> lines = Lines(scheme);

            var rows = new List<CheckTreeLine>();
            for (int index = 0; index < lines.Count; index++)
                rows.Add(new CheckTreeLine(new SchemeLineKey(scheme, index), lines[index].Text));

            nodes.Add(new CheckTreeNode(scheme, Describe(scheme), rows, ReferenceEquals(scheme, select)));
        }

        return nodes;
    }

    public bool CanEdit(object? selected) => selected is not null;

    /// <summary>内置方案只能取消勾选，不能删。</summary>
    public bool CanDelete(object? selected) => selected is not null && !IsBuiltIn(selected);

    // ================================================================
    //  新建 / 编辑 / 删除
    // ================================================================

    public async Task<CheckOutcome> NewAsync(Window owner, object? selected)
    {
        object? edited = await OpenEditorAsync(owner, CreateNew());
        if (edited is null)
            return CheckOutcome.None;

        _schemes.Add(edited);
        return CheckOutcome.Rebuilt(edited);
    }

    public async Task<CheckOutcome> EditAsync(Window owner, object selected)
    {
        int index = _schemes.IndexOf(selected);
        if (index < 0)
            return CheckOutcome.None;

        object? edited = await OpenEditorAsync(owner, selected);
        if (edited is null)
            return CheckOutcome.None;

        // 编辑返回的是新副本，原来的子项对象都没了。原来勾过的方案，
        // 编辑后整条勾上，否则用户改一下名称就发现勾没了。
        bool wasInvolved = IsInvolved(selected);
        if (wasInvolved)
        {
            int lineCount = Lines(edited).Count;
            for (int line = 0; line < lineCount; line++)
                _state.Set(new SchemeLineKey(edited, line), true);

            if (lineCount == 0)
                _state.SetRoot(edited, true);
        }

        _schemes[index] = edited;
        PruneChecked();
        return CheckOutcome.Rebuilt(edited);
    }

    public CheckOutcome Delete(object selected)
    {
        if (!_schemes.Remove(selected))
            return CheckOutcome.None;

        PruneChecked();
        return CheckOutcome.Rebuilt(null);
    }

    /// <summary>勾选表里只留方案还在的键。删方案、换副本之后都要走一遍，否则计数会多算。</summary>
    private void PruneChecked()
    {
        var alive = new HashSet<object>(_schemes, ReferenceEqualityComparer.Instance);
        _state.Prune(key => key is SchemeLineKey line ? alive.Contains(line.Scheme) : alive.Contains(key));
    }

    // ================================================================
    //  导入 / 导出
    // ================================================================

    public async Task<CheckOutcome> ImportAsync(Window owner)
    {
        IReadOnlyList<IStorageFile> files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = ImportText,
            AllowMultiple = false,
            FileTypeFilter = new[] { JsonFileType },
        });

        if (files.Count == 0)
            return CheckOutcome.None;

        List<object>? imported;
        try
        {
            await using Stream stream = await files[0].OpenReadAsync();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            imported = Deserialize(await reader.ReadToEndAsync());
        }
        catch (Exception ex)
        {
            return CheckOutcome.None.WithWarning(Loc.T(KeyPrefix + "ImportFailed", ex.Message));
        }

        if (imported is null || imported.Count == 0)
            return CheckOutcome.None.WithHint(Loc.T(KeyPrefix + "ImportEmpty"));

        var added = imported.Select(Clone).ToList();
        _schemes.AddRange(added);
        return CheckOutcome.Rebuilt(null).WithHint(Loc.T(KeyPrefix + "ImportDone", added.Count));
    }

    public async Task<CheckOutcome> ExportAsync(Window owner)
    {
        List<object> selected = InvolvedSchemes();
        if (selected.Count == 0)
            return CheckOutcome.None.WithHint(Loc.T(KeyPrefix + "ExportHint"));

        IStorageFile? file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = ExportText,
            SuggestedFileName = "schemes.json",
            DefaultExtension = "json",
            FileTypeChoices = new[] { JsonFileType },
        });

        if (file is null)
            return CheckOutcome.None;

        try
        {
            byte[] bytes = new UTF8Encoding(false).GetBytes(Serialize(selected));
            await using Stream stream = await file.OpenWriteAsync();
            stream.SetLength(0);
            await stream.WriteAsync(bytes);
        }
        catch (Exception ex)
        {
            return CheckOutcome.None.WithWarning(Loc.T(KeyPrefix + "ExportFailed", ex.Message));
        }

        return CheckOutcome.None.WithHint(
            Loc.T(KeyPrefix + "ExportDone", selected.Count, file.TryGetLocalPath() ?? file.Name));
    }

    // ================================================================
    //  确定
    // ================================================================

    public object Result() => new SchemeSelectionResult(_schemes, SelectedPayloads());

    // ================================================================
    //  勾选状态
    // ================================================================

    /// <summary>一个方案是否有任意一行被勾中；没有行的方案看它自己的勾。</summary>
    private bool IsInvolved(object scheme)
    {
        IReadOnlyList<(string Text, object Payload)> lines = Lines(scheme);
        if (lines.Count == 0)
            return _state.IsRootChecked(scheme);

        for (int index = 0; index < lines.Count; index++)
        {
            if (_state.IsChecked(new SchemeLineKey(scheme, index)))
                return true;
        }

        return false;
    }

    /// <summary>勾中过任意一行的方案，按方案顺序。导出与摘要都用它。</summary>
    private List<object> InvolvedSchemes() => _schemes.Where(IsInvolved).ToList();

    /// <summary>勾中的载荷，按方案顺序、行顺序排列，这正是调用方要喂给处理引擎的次序。</summary>
    private List<object> SelectedPayloads()
    {
        var payloads = new List<object>();

        foreach (object scheme in _schemes)
        {
            IReadOnlyList<(string Text, object Payload)> lines = Lines(scheme);
            for (int index = 0; index < lines.Count; index++)
            {
                if (_state.IsChecked(new SchemeLineKey(scheme, index)))
                    payloads.Add(lines[index].Payload);
            }
        }

        return payloads;
    }
}
