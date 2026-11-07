using Avalonia.Controls;

namespace TextTool.Gui.Views;

/// <summary>
/// 勾选窗里某一类清单的差异点：树上有哪些节点和行、哪些能编辑删除、点确定交回什么。
///
/// 窗只管画树、三态勾、删除确认与行内提示；上面这些全由适配器回答，
/// 所以替换方案、角色方案、检查判据共用一个窗口类、一份布局。
/// 适配器自己掌管自己的数据：改了什么它清楚，窗不去动它。
/// </summary>
public interface ICheckListAdapter
{
    /// <summary>
    /// 把勾选表交给适配器，并按自己的数据填好初始勾选。
    /// 之后两边读写的是同一份：窗这一边点一下勾，适配器那边立刻看得见，重建树也不掉勾。
    /// 返回非空表示读数据失败，窗把它写进警告区。
    /// </summary>
    string? Attach(CheckState state);

    /// <summary>选中子行是否等同于选中它所属的顶层节点。方案窗是，判据窗不是。</summary>
    bool PromotesLineSelection { get; }

    // 下面这几条每次访问现读文案表，切语言后窗重刷一遍就能跟上。

    string Title { get; }

    string NewText { get; }

    string EditText { get; }

    string DeleteText { get; }

    string CancelText { get; }

    string OkText { get; }

    /// <summary>一条都没勾时点确定的提示。null 表示零勾也放行。</summary>
    string? NothingCheckedWarning { get; }

    /// <summary>摘要那一行：勾了几条、适用几条、一共几条。</summary>
    string Summary { get; }

    /// <summary>
    /// 树上的全部顶层节点与它们下面的行。select 给定时由适配器决定重建后选中哪个节点或哪一行。
    /// </summary>
    IReadOnlyList<CheckTreeNode> Nodes(object? select);

    /// <summary>选中的那个能不能编辑、能不能删。没选中时传 null。</summary>
    bool CanEdit(object? selected);

    bool CanDelete(object? selected);

    /// <summary>删除确认那一句，带上是哪一个。</summary>
    string DeleteConfirmText(object selected);

    // 三个动作返回重建后要选中的东西与要写的行内提示，数据由适配器自己改。

    /// <summary>新建。selected 是当前选中的那个，新建时当模板用，没有就随便挑一个。</summary>
    Task<CheckOutcome> NewAsync(Window owner, object? selected);

    Task<CheckOutcome> EditAsync(Window owner, object selected);

    CheckOutcome Delete(object selected);

    /// <summary>点确定交回调用方的东西，形状由各适配器定。</summary>
    object Result();
}

/// <summary>
/// 能在窗里导入导出的那一类。方案是一份 json 文件，导进来变成新方案；
/// 判据直接写规则文件，没有这一项，所以它不实现本接口，窗里也就不出这两个按钮。
/// </summary>
public interface IImportExportAdapter : ICheckListAdapter
{
    string ImportText { get; }

    string ExportText { get; }

    /// <summary>导入导出整段由适配器走：文件选择、格式、提示文字都是它的事。</summary>
    Task<CheckOutcome> ImportAsync(Window owner);

    Task<CheckOutcome> ExportAsync(Window owner);
}

/// <summary>
/// 适配器干完一件事的回话。
/// Rebuild 为真时窗重画整棵树，Selected 是重画后要选中的东西；
/// Hint 写进提示区，Warning 写进警告区，两条互不清空，可以同时显示。
/// </summary>
public sealed record CheckOutcome(
    object? Selected = null, bool Rebuild = false, string? Hint = null, string? Warning = null)
{
    /// <summary>什么都没变，比如用户取消了编辑窗。</summary>
    public static readonly CheckOutcome None = new();

    /// <summary>数据变了，重画树，顺带选中 select。</summary>
    public static CheckOutcome Rebuilt(object? select) => new(select, Rebuild: true);

    public CheckOutcome WithHint(string text) => this with { Hint = text };

    public CheckOutcome WithWarning(string text) => this with { Warning = text };
}
