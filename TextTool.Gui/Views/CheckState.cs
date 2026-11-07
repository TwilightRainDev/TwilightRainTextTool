namespace TextTool.Gui.Views;

/// <summary>
/// 勾选表。窗与适配器共用同一份：窗这一边点一下勾，适配器那边立刻看得见，
/// 重建树也不掉勾。
///
/// 两张表分开记：Checked 是行的键，RootChecked 是没有子行的顶层节点自己那一勾。
/// 分开是为了「勾了几条」这种计数只数行，不会把没有子行的节点算进去。
/// </summary>
public sealed class CheckState
{
    private readonly HashSet<object> _checked = new();

    private readonly HashSet<object> _rootChecked = new();

    /// <summary>勾中的行。这里是集合，顺序没有意义，要按数据顺序交回的一方自己排一遍。</summary>
    public IReadOnlyCollection<object> Checked => _checked;

    /// <summary>勾中的行数。</summary>
    public int CheckedCount => _checked.Count;

    public bool IsChecked(object key) => _checked.Contains(key);

    public bool IsRootChecked(object key) => _rootChecked.Contains(key);

    public void Set(object key, bool on)
    {
        if (on)
            _checked.Add(key);
        else
            _checked.Remove(key);
    }

    public void SetRoot(object key, bool on)
    {
        if (on)
            _rootChecked.Add(key);
        else
            _rootChecked.Remove(key);
    }

    /// <summary>清空全部勾选，再按给定的键重新勾一遍。</summary>
    public void Reset(IEnumerable<object> keys)
    {
        _checked.Clear();
        foreach (object key in keys)
            _checked.Add(key);
    }

    /// <summary>
    /// 丢掉树上已经没有的那几个键。keep 对行与顶层节点自己的键都适用。
    /// 数据里删掉一项、或者换了一份新副本，都要走一遍，否则计数会多算。
    /// </summary>
    public void Prune(Func<object, bool> keep)
    {
        _checked.RemoveWhere(key => !keep(key));
        _rootChecked.RemoveWhere(key => !keep(key));
    }
}
