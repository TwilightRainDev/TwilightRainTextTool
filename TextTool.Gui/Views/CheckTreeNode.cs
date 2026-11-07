namespace TextTool.Gui.Views;

/// <summary>
/// 树上的一个顶层节点：它自己的键、文字，和它下面可单独勾的行。
/// Selected 为真表示重建后要选中它。
/// </summary>
public sealed record CheckTreeNode(
    object Key, string Text, IReadOnlyList<CheckTreeLine> Lines, bool Selected = false);

/// <summary>
/// 顶层节点下的一行。Key 是它在勾选表里的身份，重建树之后靠它认回同一行；
/// Tag 是选中这一行时交给适配器的对象，null 表示交 Key；
/// Enabled 为 false 的行置灰、不能勾，也不参与父项的汇总。
/// </summary>
public sealed record CheckTreeLine(
    object Key, string Text, object? Tag = null, bool Enabled = true, bool Selected = false);
