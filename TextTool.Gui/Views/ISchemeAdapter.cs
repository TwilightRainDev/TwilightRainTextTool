using Avalonia.Controls;

namespace TextTool.Gui.Views;

/// 一类方案在通用勾选窗里的差异点。
public interface ISchemeAdapter
{
    /// 文案键前缀："Scheme" 或 "VnScheme"。
    string KeyPrefix { get; }
    /// 树顶层节点文字。
    string Describe(object scheme);
    /// 子节点：每一行代表一个可单独勾选的东西。
    IReadOnlyList<(string Text, object Payload)> Lines(object scheme);
    bool IsBuiltIn(object scheme);
    /// 新建一个空方案。
    object CreateNew();
    /// 打开对应编辑窗，返回编辑后的新副本；取消返回 null。
    Task<object?> EditAsync(Window owner, object scheme);
    object Clone(object scheme);
    string Serialize(IEnumerable<object> schemes);
    List<object>? Deserialize(string json);
}
