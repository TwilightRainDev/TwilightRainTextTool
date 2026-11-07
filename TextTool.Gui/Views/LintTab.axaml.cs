using Avalonia.Controls;
using Avalonia.Interactivity;
using TextTool.Gui.Localization;
using TextTool.Services;

namespace TextTool.Gui.Views;

/// <summary>
/// AI 味检查页签：文件或粘贴 → LintRunner → 只读人读报告。不写文件。
///
/// 报告里每一个字都由 <see cref="ILintText"/> 现取，本页不拼接报告正文，
/// 所以换语言后旧报告就是旧语言，必须按新语言重扫一遍（见 <see cref="RerenderReportAsync"/>）。
/// </summary>
public partial class LintTab : FileTabBase
{
    /// <summary>
    /// 粘贴内容的来源名，与命令行一致。人读报告不打印文件名，它只落在报告模型的 File 上，
    /// 与按文件跑出来的报告保持同一种形状。
    /// </summary>
    private const string PasteSource = "(paste)";

    /// <summary>规则清单。勾中的那几条对应 <see cref="LintRunner.Run"/> 的 onlyIds。</summary>
    private List<LintRule> _rules = new();

    /// <summary>上一次扫过的输入与选项。换语言时拿它重扫，不重新读盘、不重新取选中项。</summary>
    private List<(string File, string Text)>? _lastInputs;
    private string? _lastMinSeverity;
    private string? _lastGenre;
    private IReadOnlyCollection<string>? _lastOnlyIds;

    /// <summary>勾中的规则 Id。等于整张表时检查那一步传 null，理由见 SelectedOnlyIds。</summary>
    private HashSet<string> _selectedRuleIds = new(StringComparer.OrdinalIgnoreCase);

    private bool _running;

    /// <summary>每发起一次新扫描或重渲染就加一，用来丢弃已经过期的后台结果。</summary>
    private int _generation;

    public LintTab()
    {
        InitializeComponent();
        // 先取规则再刷文案：配置读不出来时，提示是在 LoadRules 里记下的，由 ApplyLocalization 显示。
        LoadRules();
        ApplyLocalization();
    }

    // ================================================================
    //  文案
    // ================================================================

    protected override TextBox FileBoxControl => FileBox;

    protected override Button BrowseButtonControl => BrowseButton;

    protected override TextBlock SourceLabelControl => SourceLabel;

    protected override TextBlock ResultLabelControl => ResultLabel;

    protected override TextBlock InlineWarningControl => InlineWarning;

    protected override string PickerTitleKey => "TabLint";

    /// <summary>本页不显示编码，选文件时不检测。</summary>
    protected override bool DetectsEncoding => false;

    /// <summary>行内警告同时报到状态栏：缺输入、文件不见了这类事不能只挂在页面里。</summary>
    protected override bool WarningReachesStatusBar => true;

    /// <summary>载入后不重刷文案：重刷会顺带重扫报告，把结果写回状态栏。</summary>
    protected override bool ApplyLocalizationOnLoad => false;

    protected override void ApplyLocalization()
    {
        ApplyCommonLocalization();

        PasteLabel.Text = Loc.T("LabelLintPaste");
        OnlyLabel.Text = Loc.T("LabelLintOnly");
        SeverityLabel.Text = Loc.T("LabelLintMinSeverity");
        SeverityAllItem.Content = Loc.T("LintSeverityAll");
        SeverityWarnItem.Content = Loc.T("LintSeverityWarn");
        GenreLabel.Text = Loc.T("LabelLintGenre");
        GenreAllItem.Content = Loc.T("LintGenreAll");
        GenreDocItem.Content = Loc.T("LintGenreDoc");
        GenreFictionItem.Content = Loc.T("LintGenreFiction");
        GenreAgentItem.Content = Loc.T("LintGenreAgent");
        BtnLint.Content = _running ? Loc.T("StatusLintRunning") : Loc.T("BtnLint");
        RulePresetButton.Content = Loc.T("BtnLintRulePreset");
        HintLabel.Text = Loc.T("HintLint");

        RefreshOnlySummary();
        _ = RerenderReportAsync();
    }

    // ================================================================
    //  规则
    // ================================================================

    /// <summary>
    /// 取规则清单。keepIds 为 null 表示全部勾上（程序启动与首次读表），
    /// 否则只勾这些 Id，表里没有的丢掉。配置读不出来时清单留空、按全跑处理，
    /// 并用现有文案在这里提示一次；检查那一步会把同一个错误再抛一次，走 ErrorOccurred。
    /// </summary>
    private void LoadRules(IReadOnlyCollection<string>? keepIds = null)
    {
        try
        {
            _rules = LintRuleStore.Load();
        }
        catch (Exception ex)
        {
            _rules = new();
            _selectedRuleIds = new(StringComparer.OrdinalIgnoreCase);
            ShowWarning("MsgConfigBroken", ex.Message);
            return;
        }

        var keep = keepIds is null
            ? null
            : new HashSet<string>(keepIds, StringComparer.OrdinalIgnoreCase);
        _selectedRuleIds = new HashSet<string>(
            _rules.Where(rule => keep is null || keep.Contains(rule.Id)).Select(rule => rule.Id),
            StringComparer.OrdinalIgnoreCase);

        RefreshOnlySummary();
    }

    /// <summary>
    /// 打开预设规则勾选窗。窗里可以新建、编辑、删除判据，都直接写规则文件，
    /// 所以回来重读一次表；勾选按窗里交回来的那批算，报告按新规则重放。
    /// </summary>
    private async void OnOpenRulePreset(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner)
            return;

        if (await CheckListDialog.ShowAsync(owner, new LintRuleAdapter(_selectedRuleIds, SelectedGenre()))
            is not List<string> selected)
            return;

        LoadRules(selected);
        await RerenderReportAsync();
    }

    /// <summary>侧栏摘要：本档实际会跑几条，全部规则共几条。</summary>
    private void RefreshOnlySummary()
    {
        // XAML 解析期间下拉框的初始选中项也会触发档位变更，那时侧栏还没建出来
        if (OnlySummaryLabel is null)
            return;

        int applicable = _rules.Count(AppliesToGenre);
        int selected = _rules.Count(rule => _selectedRuleIds.Contains(rule.Id) && AppliesToGenre(rule));
        OnlySummaryLabel.Text = Loc.T("LintRuleSelectedSummary", selected, applicable);
    }

    /// <summary>这条规则在当前档位下跑不跑。规则自己的 Genres 为空表示各档通用。</summary>
    private bool AppliesToGenre(LintRule rule) =>
        SelectedGenre() is not { } genre
        || rule.Genres.Count == 0
        || rule.Genres.Contains(genre, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 切档位：报告与摘要都按新档重算。不适用的规则本来就不会跑，
    /// 勾选集合不去动它，切回来还是原来那批。
    /// </summary>
    private async void OnGenreChanged(object? sender, SelectionChangedEventArgs e)
    {
        RefreshOnlySummary();
        if (_rules.Count == 0 || _running || string.IsNullOrEmpty(ReportBox.Text))
            return;

        // 报告是上一次档位算的，换了档就得按新档重放，留着旧结果等于骗人
        _lastGenre = SelectedGenre();
        await RerenderReportAsync();
    }

    /// <summary>
    /// 勾中的就是 onlyIds，全选时返回 null。列表里是整张规则表，算法规则也在其中，
    /// 返回 null 等于不给 --only，与命令行是同一种说法，默认不勾选的判据因此不会被点名。
    /// 返回一份等长的 Id 清单则相反：那等于把每一条都点了名。清单读不出来时同样返回 null。
    /// </summary>
    private IReadOnlyCollection<string>? SelectedOnlyIds()
    {
        var selected = _rules
            .Where(rule => _selectedRuleIds.Contains(rule.Id) && AppliesToGenre(rule))
            .Select(rule => rule.Id)
            .ToList();

        return selected.Count == 0 || selected.Count == _rules.Count ? null : selected;
    }

    /// <summary>
    /// 下拉选中项 → <see cref="LintRunner.Run"/> 的档位参数。
    /// 选「全部规则」时传 null：档位是过滤器，传 null 等于不过滤，与命令行不给 --genre 一致。
    /// </summary>
    private string? SelectedGenre() => GenreBox.SelectedIndex switch
    {
        1 => "doc",
        2 => "fiction",
        3 => "agent",
        _ => null,
    };

    // ================================================================
    //  选文件
    // ================================================================

    protected override void OnFilesSelected(IReadOnlyList<string> files) => ClearWarning();

    // ================================================================
    //  检查
    // ================================================================

    private async void OnLint(object? sender, RoutedEventArgs e)
    {
        if (_running)
            return;

        string paste = PasteBox.Text ?? string.Empty;
        string[] files = SelectedFiles.ToArray();

        if (files.Length == 0 && string.IsNullOrWhiteSpace(paste))
        {
            ShowWarning("MsgLintNeedInput");
            return;
        }

        // 选中的文件可能在这之后被移走。先在界面上说清楚，别让它在后台线程里变成一个裸异常。
        string? missing = files.FirstOrDefault(path => !File.Exists(path));
        if (missing is not null)
        {
            ShowWarning("MsgFileNotFound", missing);
            return;
        }

        string? minSeverity = SeverityBox.SelectedIndex == 1 ? "warn" : null;
        string? genre = SelectedGenre();
        IReadOnlyCollection<string>? onlyIds = SelectedOnlyIds();
        string languageAtStart = Loc.CurrentLang;

        _generation++;
        _running = true;
        BtnLint.IsEnabled = false;
        BtnLint.Content = Loc.T("StatusLintRunning");
        ReportStatus(Loc.T("StatusLintRunning"));

        try
        {
            var scan = await Task.Run(() => Run(files, paste, minSeverity, genre, onlyIds));

            _lastInputs = scan.Inputs;
            _lastMinSeverity = minSeverity;
            _lastGenre = genre;
            // 选中项可能在跑完之后又变了，重渲染必须照这一次的原样重放，不能读当前的列表。
            _lastOnlyIds = onlyIds;
            ReportBox.Text = scan.Text;
            ClearWarning();
            ShowResult("StatusLintDone", warning: false, scan.Hits, scan.Notes);
        }
        catch (Exception ex)
        {
            ReportError(ex.Message);
        }
        finally
        {
            _running = false;
            BtnLint.IsEnabled = true;
            BtnLint.Content = Loc.T("BtnLint");
        }

        // 跑的这几秒里换过语言：刚出的报告是旧语言的，按新语言重扫一遍。
        if (languageAtStart != Loc.CurrentLang)
            await RerenderReportAsync();
    }

    /// <summary>一次检查的产物：报告正文、命中与统计项计数、以及这次真正扫过的输入。</summary>
    private sealed record Scan(string Text, int Hits, int Notes, List<(string File, string Text)> Inputs);

    private static Scan Run(
        string[] files, string paste, string? minSeverity, string? genre,
        IReadOnlyCollection<string>? onlyIds)
    {
        var inputs = new List<(string File, string Text)>();
        foreach (string path in files)
        {
            var detection = EncodingDetector.DetectStrict(path);
            inputs.Add((Path.GetFileName(path), File.ReadAllText(path, detection.Encoding)));
        }

        if (!string.IsNullOrWhiteSpace(paste))
            inputs.Add((PasteSource, paste));

        return Render(inputs, minSeverity, genre, onlyIds);
    }

    /// <summary>扫一遍并渲染成人读报告。文案出处随当前界面语言，缺译时退回引擎里的原文。</summary>
    private static Scan Render(
        List<(string File, string Text)> inputs, string? minSeverity, string? genre,
        IReadOnlyCollection<string>? onlyIds)
    {
        var reportSet = LintRunner.Run(inputs, onlyIds, minSeverity, genre, LintTextSource.Instance);
        string text = LintOutput.Render(reportSet, asJson: false, LintTextSource.Instance);

        int hits = 0, notes = 0;
        foreach (var report in reportSet.Reports)
        {
            hits += report.Hits.Count;
            notes += report.Notes.Count;
        }

        return new Scan(text, hits, notes, inputs);
    }

    /// <summary>
    /// 换语言后按新语言重扫上一次的输入。留着旧报告就是留着旧语言，所以这里必须重跑，
    /// 而不是只把某一处标题刷一下。扫描期间又换了语言时，过期的结果直接丢掉。
    /// </summary>
    private async Task RerenderReportAsync()
    {
        if (_running || _lastInputs is not { } inputs || string.IsNullOrEmpty(ReportBox.Text))
            return;

        int generation = ++_generation;
        string? minSeverity = _lastMinSeverity;
        string? genre = _lastGenre;
        IReadOnlyCollection<string>? onlyIds = _lastOnlyIds;

        try
        {
            var scan = await Task.Run(() => Render(inputs, minSeverity, genre, onlyIds));
            if (generation != _generation)
                return;

            ReportBox.Text = scan.Text;
            ShowResult("StatusLintDone", warning: false, scan.Hits, scan.Notes);
        }
        catch (Exception ex)
        {
            ReportError(ex.Message);
        }
    }
}
