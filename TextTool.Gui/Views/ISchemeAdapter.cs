using System.Runtime.CompilerServices;
using Avalonia.Controls;

namespace TextTool.Gui.Views;

/// <summary>
/// 一类方案在通用勾选窗里的差异点：顶层节点文字、子行是什么、编辑窗、导入导出的格式。
///
/// 方案窗的通用流程（副本上编辑、勾选记忆、新建删除、导入导出、点确定交回什么）在
/// <see cref="SchemeAdapterBase"/> 里，替换方案与角色方案各自只实现本接口这几项。
/// </summary>
public interface ISchemeAdapter : ICheckListAdapter
{
    /// <summary>文案键前缀："Scheme" 或 "VnScheme"。</summary>
    string KeyPrefix { get; }

    /// <summary>树顶层节点文字。</summary>
    string Describe(object scheme);

    /// <summary>子节点：每一行代表一个可单独勾选的东西。</summary>
    IReadOnlyList<(string Text, object Payload)> Lines(object scheme);

    bool IsBuiltIn(object scheme);

    /// <summary>新建一个空方案。</summary>
    object CreateNew();

    /// <summary>打开对应编辑窗，返回编辑后的新副本；取消返回 null。</summary>
    Task<object?> OpenEditorAsync(Window owner, object scheme);

    object Clone(object scheme);

    string Serialize(IEnumerable<object> schemes);

    List<object>? Deserialize(string json);
}

/// <summary>
/// 方案窗交回调用方的东西：方案全表与勾中的行载荷，按方案顺序。
/// 交回的是窗里那份副本，窗里的新建、编辑、删除都已经落在里面。
/// </summary>
public sealed record SchemeSelectionResult(List<object> Schemes, List<object> SelectedPayloads);

/// <summary>
/// 方案窗一行的身份：哪个方案的第几行。方案按对象身份比，行按序号比。
/// 编辑一个方案换的是整条对象，所以这里存对象本身，不存它的散列值。
/// </summary>
public sealed class SchemeLineKey(object scheme, int index) : IEquatable<SchemeLineKey>
{
    public object Scheme { get; } = scheme;

    public int Index { get; } = index;

    public bool Equals(SchemeLineKey? other) =>
        other is not null && ReferenceEquals(other.Scheme, Scheme) && other.Index == Index;

    public override bool Equals(object? obj) => Equals(obj as SchemeLineKey);

    public override int GetHashCode() => HashCode.Combine(RuntimeHelpers.GetHashCode(Scheme), Index);
}
