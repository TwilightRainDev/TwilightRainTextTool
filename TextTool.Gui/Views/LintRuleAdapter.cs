using Avalonia.Controls;
using TextTool.Gui.Localization;
using TextTool.Services;

namespace TextTool.Gui.Views;

/// <summary>
/// 判据在通用勾选窗里的差异点：顶层是规则组，一行一条判据，勾选的规则交回调用方
/// 当作这次检查的 onlyIds。
///
/// 这一类自己的规矩：
///   - 没有导入导出：判据直接写规则文件，不需要一份 json 来回搬；
///   - 一条都不勾不放行：检查总得跑点什么；
///   - 内置判据不能删，只能取消勾选；自定义判据（Id 不在内置表里）可以删；
///   - 当前档位下不适用的判据置灰，不参与父项的汇总。
///
/// 新建、编辑、删除都直接写盘，所以窗关掉之后调用方重读一次规则表即可。
/// </summary>
public sealed class LintRuleAdapter : ICheckListAdapter
{
    /// <summary>本页要传给引擎的档位，用来置灰不适用本档的判据。</summary>
    private readonly string? _genre;

    /// <summary>第一次打开时带进来的勾选，null 表示全部按默认勾上。</summary>
    private readonly IReadOnlyCollection<string>? _initialChecked;

    /// <summary>判据全集，取自规则文件，含自定义。</summary>
    private List<LintRule> _rules = new();

    /// <summary>上一次读盘时的判据 Id。新出现的判据默认勾上，用户取消过的保持取消。</summary>
    private HashSet<string> _known = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>内置表里的 Id，用来判断一条判据能不能删。</summary>
    private HashSet<string> _builtIn = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>规则文件里出现过的 Id，用来在标题上标「已改」。</summary>
    private HashSet<string> _overridden = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>勾选表，<see cref="Attach"/> 时由窗交过来。</summary>
    private CheckState _state = new();

    public LintRuleAdapter(IReadOnlyCollection<string>? checkedIds, string? genre)
    {
        _initialChecked = checkedIds;
        _genre = genre;
    }

    public string? Attach(CheckState state)
    {
        _state = state;
        return LoadRules(_initialChecked);
    }

    // ================================================================
    //  文案
    // ================================================================

    public string Title => Loc.T("DlgLintRulePreset");

    public string NewText => Loc.T("BtnNewLintRule");

    public string EditText => Loc.T("BtnEditLintRule");

    public string DeleteText => Loc.T("BtnDeleteLintRule");

    public string CancelText => Loc.T("BtnCancel");

    public string OkText => Loc.T("BtnOK");

    /// <summary>零勾点确定时挡下并显示这句话：检查总得跑点什么。</summary>
    public string? NothingCheckedWarning => Loc.T("MsgLintNeedOneRule");

    public string Summary =>
        Loc.T("LintRulePresetSummary", _state.CheckedCount, _rules.Count(Applies), _rules.Count);

    public string DeleteConfirmText(object selected) =>
        selected is LintRule rule ? Loc.T("LintRuleDeleteConfirm", rule.Title) : string.Empty;

    /// <summary>选中一条判据不改选组节点：组节点的勾是汇总出来的，选中与否是另一件事。</summary>
    public bool PromotesLineSelection => false;

    // ================================================================
    //  读盘
    // ================================================================

    /// <summary>
    /// 读规则表。initialChecked 为空表示第一次打开：全部勾上。
    /// 每次读盘都按 Id 保留勾选状态，规则表里新出现的判据默认勾上。
    /// 读不出来时返回要写进警告区的那句话，勾选留空。
    /// </summary>
    private string? LoadRules(IReadOnlyCollection<string>? initialChecked = null)
    {
        HashSet<string> previous = new(_known, StringComparer.OrdinalIgnoreCase);
        string? failure = null;

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
            failure = ex.Message;
        }

        var wanted = new List<object>();
        if (initialChecked is null)
        {
            foreach (LintRule rule in _rules)
            {
                // 上次勾过就沿用；新出现的判据按默认走，标了默认不勾选的先不勾
                if (previous.Contains(rule.Id) || !rule.DefaultOff)
                    wanted.Add(rule.Id);
            }
        }
        else
        {
            foreach (string id in initialChecked)
                wanted.Add(id);
        }

        _state.Reset(wanted);

        // 勾选集合只保留规则表里还在的 Id，否则「已勾几条」会与实际对不上
        var alive = new HashSet<string>(_rules.Select(rule => rule.Id), StringComparer.OrdinalIgnoreCase);
        _state.Prune(key => key is string id && alive.Contains(id));
        _known = alive;

        return failure;
    }

    /// <summary>重新读盘，保留当前勾选；check 非空时先把它勾上，读完选中 select。</summary>
    private CheckOutcome Reload(object? select, string? check = null)
    {
        if (check is not null)
            _state.Set(check, true);

        string? failure = LoadRules(CheckedIds());
        CheckOutcome outcome = CheckOutcome.Rebuilt(select);
        return failure is null ? outcome : outcome.WithWarning(failure);
    }

    /// <summary>当前勾中的判据 Id，还回规则表时用。</summary>
    private List<string> CheckedIds() => _state.Checked.Cast<string>().ToList();

    // ================================================================
    //  树
    // ================================================================

    public IReadOnlyList<CheckTreeNode> Nodes(object? select)
    {
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

        var nodes = new List<CheckTreeNode>();
        foreach (string group in order)
        {
            var rows = new List<CheckTreeLine>();
            foreach (LintRule rule in byGroup[group])
            {
                rows.Add(new CheckTreeLine(
                    rule.Id,
                    LineText(rule),
                    rule,
                    Applies(rule),
                    select is string id && Same(rule.Id, id)));
            }

            nodes.Add(new CheckTreeNode(group, GroupTitle(group), rows));
        }

        return nodes;
    }

    /// <summary>子节点文字：判据标题，改过的那条带「已改」。</summary>
    private string LineText(LintRule rule) =>
        _overridden.Contains(rule.Id) ? Loc.T("LintRuleOverridden", rule.Title) : rule.Title;

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

    /// <summary>这条判据在当前档位下跑不跑。规则自己的 Genres 为空表示各档通用。</summary>
    private bool Applies(LintRule rule) =>
        _genre is null
        || rule.Genres.Count == 0
        || rule.Genres.Contains(_genre, StringComparer.OrdinalIgnoreCase);

    private static bool Same(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    public bool CanEdit(object? selected) => selected is LintRule;

    /// <summary>自定义判据：Id 不在内置表里，规则文件是它唯一的家，可以整条删掉。</summary>
    public bool CanDelete(object? selected) => selected is LintRule rule && !_builtIn.Contains(rule.Id);

    // ================================================================
    //  新建 / 编辑 / 删除
    // ================================================================

    /// <summary>
    /// 新建一条自定义判据。字段从选中的判据抄一份形状（组、范围、字段名都不空），
    /// 词条留空交给编辑窗填：猜一条词表出来只会让人以为是自己写的。
    /// </summary>
    public async Task<CheckOutcome> NewAsync(Window owner, object? selected)
    {
        LintRule? template = selected as LintRule ?? _rules.FirstOrDefault();
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
        await dialog.ShowDialog(owner);

        if (!dialog.Changed)
            return CheckOutcome.None;

        return Reload(created.Id, created.Id).WithHint(Loc.T("LintRuleCreated", created.Title));
    }

    public async Task<CheckOutcome> EditAsync(Window owner, object selected)
    {
        if (selected is not LintRule rule)
            return CheckOutcome.None;

        var dialog = new LintRuleEditDialog(rule, _overridden.Contains(rule.Id));
        await dialog.ShowDialog(owner);

        if (!dialog.Changed)
            return CheckOutcome.None;

        return Reload(rule.Id).WithHint(Loc.T("LintRuleSaved", rule.Title));
    }

    public CheckOutcome Delete(object selected)
    {
        if (selected is not LintRule rule)
            return CheckOutcome.None;

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
            return CheckOutcome.None.WithWarning(Loc.T("MsgLintRulesSaveFailed", ex.Message));
        }

        _state.Set(rule.Id, false);
        return Reload(null).WithHint(Loc.T("LintRuleDeleted", rule.Title));
    }

    /// <summary>自定义判据的 Id：前缀 user- 加八位十六进制，撞上已有 Id 时再抽一次。</summary>
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
    //  确定
    // ================================================================

    /// <summary>按规则表顺序交回，调用方与引擎都不关心勾的次序。</summary>
    public object Result() => _rules
        .Where(rule => _state.IsChecked(rule.Id))
        .Select(rule => rule.Id)
        .ToList();
}
