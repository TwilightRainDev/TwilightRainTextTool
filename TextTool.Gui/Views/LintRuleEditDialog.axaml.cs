using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using TextTool.Gui.Localization;
using TextTool.Services;

namespace TextTool.Gui.Views;

/// <summary>
/// 单条规则的编辑窗。骨架与方案编辑窗一致：左标签右输入、行内警告、右下角取消与确定。
///
/// 改动写进程序目录的 lint_rules.json，只存改过的条目，按 Id 覆盖内置。
/// 三条硬约束：
///   1. 确定之前先校验，校验不过不落盘。写进一条非法正则，下次启动整个程序起不来。
///   2. 恢复内置与恢复全部内置都是立即写盘的动作，写完窗口不关，表单显示内置值。
///   3. 由代码计算的规则（Kind=algorithm）只开放文案与适用范围：命不命中由引擎那段算法算，
///      词表、匹配方式与段内门限归代码，界面上没有这三样可改。
/// </summary>
public partial class LintRuleEditDialog : Window
{
    private LintRule _rule;
    private bool _overridden;

    /// <summary>这条规则命不命中由代码算：词表、匹配方式、段内门限都不归界面管。</summary>
    private static bool IsAlgorithm(LintRule rule) => rule.Kind == LintRule.KindAlgorithm;

    /// <summary>规则表被写过时为真，调用方据此重读列表。</summary>
    public bool Changed { get; private set; }

    public LintRuleEditDialog() : this(new LintRule(), false) { }

    public LintRuleEditDialog(LintRule rule, bool overridden)
    {
        InitializeComponent();
        _rule = rule;
        _overridden = overridden;

        ApplyLocalization();
        Loc.LanguageChanged += ApplyLocalization;
        Closed += (_, _) => Loc.LanguageChanged -= ApplyLocalization;

        LoadForm(_rule);
    }

    // ================================================================
    //  文案
    // ================================================================

    private void ApplyLocalization()
    {
        TitleLabel.Text = Loc.T("LabelLintRuleTitle");
        KindLabel.Text = Loc.T("LabelLintRuleKind");
        KindLiteralItem.Content = Loc.T("LintRuleKindLiteral");
        KindRegexItem.Content = Loc.T("LintRuleKindRegex");
        KindAlgorithmItem.Content = Loc.T("LintRuleKindAlgorithm");
        GenresLabel.Text = Loc.T("LabelLintRuleGenres");
        GenreDocBox.Content = Loc.T("LintGenreDoc");
        GenreFictionBox.Content = Loc.T("LintGenreFiction");
        GenreAgentBox.Content = Loc.T("LintGenreAgent");
        SeverityLabel.Text = Loc.T("LabelLintRuleSeverity");
        SeverityInfoItem.Content = Loc.T("LintSeverityInfoOnly");
        SeverityWarnItem.Content = Loc.T("LintSeverityWarnOnly");
        MinCountLabel.Text = Loc.T("LabelLintRuleMinCount");
        PatternsLabel.Text = Loc.T("LabelLintRulePatterns");
        DetailLabel.Text = Loc.T("LabelLintRuleDetail");
        DefaultOffLabel.Text = Loc.T("LabelLintRuleDefaultOff");
        AlgorithmHintLabel.Text = Loc.T("HintLintRuleAlgorithmRule");
        RestoreButton.Content = Loc.T("BtnRestoreLintRule");
        RestoreAllButton.Content = Loc.T("BtnRestoreAllLintRules");
        CancelButton.Content = Loc.T("BtnCancel");
        OkButton.Content = Loc.T("BtnOK");
        HintLabel.Text = Loc.T("HintLintRuleEdit");
        UpdateState();
    }

    private void UpdateState()
    {
        StateLabel.Text = Loc.T(_overridden ? "LintRuleOverridden" : "LintRuleBuiltIn", _rule.Title);
        RestoreButton.IsEnabled = _overridden;
    }

    // ================================================================
    //  表单
    // ================================================================

    private void LoadForm(LintRule rule)
    {
        bool algorithm = IsAlgorithm(rule);
        TitleBox.Text = rule.Title;
        // 由代码计算的规则在下拉里只能落在第三项，而且不许改：换一项就是换一段代码算
        KindBox.SelectedIndex = algorithm ? 2 : rule.Kind == "literal" ? 0 : 1;
        KindBox.IsEnabled = !algorithm;
        GenreDocBox.IsChecked = rule.Genres.Contains("doc", StringComparer.OrdinalIgnoreCase);
        GenreFictionBox.IsChecked = rule.Genres.Contains("fiction", StringComparer.OrdinalIgnoreCase);
        GenreAgentBox.IsChecked = rule.Genres.Contains("agent", StringComparer.OrdinalIgnoreCase);
        SeverityBox.SelectedIndex = rule.Severity == "info" ? 0 : 1;
        DefaultOffBox.IsChecked = rule.DefaultOff;
        // 段内门限也只对数据规则有意义：算法规则的报不报由代码里的门限决定
        MinCountLabel.IsVisible = !algorithm;
        MinCountBox.IsVisible = !algorithm;
        MinCountBox.Text = rule.MinCount.ToString();
        // 没有词表可编的规则不摆空框：这一行显示说明，写清能改哪几项
        PatternsLabel.IsVisible = !algorithm;
        PatternsBox.IsVisible = !algorithm;
        AlgorithmHintLabel.IsVisible = algorithm;
        PatternsBox.Text = string.Join(Environment.NewLine, rule.Patterns);
        DetailBox.Text = rule.Detail;
        ClearWarning();
        UpdateState();
    }

    /// <summary>
    /// 表单 → 规则。以原规则为底只能改界面上开放的那几项（见 <see cref="LintRuleEdit"/>）：
    /// 词面表、范围、段末字数、密度门限、打断权不是本窗能改的，一律原样带回去。
    /// 由代码计算的规则交上来的词表是空的：那几个框在这类规则上隐去，
    /// 万一有值也不在这里挡，写盘前那道规则表校验会点名说这条规则不吃词表。
    /// </summary>
    private LintRule? ReadForm(out string? error)
    {
        error = null;
        bool algorithm = IsAlgorithm(_rule);

        string title = (TitleBox.Text ?? string.Empty).Trim();
        if (title.Length == 0)
        {
            error = "MsgLintRuleTitleEmpty";
            return null;
        }

        List<string> patterns = (PatternsBox.Text ?? string.Empty)
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();
        if (!algorithm && patterns.Count == 0)
        {
            error = "MsgLintRulePatternsEmpty";
            return null;
        }

        // 段内门限同理：这类规则的门限在代码里，界面上没有这一栏可填，原值带回去
        int minCount = _rule.MinCount;
        if (!algorithm
            && (!int.TryParse((MinCountBox.Text ?? string.Empty).Trim(), out minCount) || minCount < 1))
        {
            error = "MsgLintRuleMinCount";
            return null;
        }

        var genres = new List<string>();
        if (GenreDocBox.IsChecked == true)
            genres.Add("doc");
        if (GenreFictionBox.IsChecked == true)
            genres.Add("fiction");
        if (GenreAgentBox.IsChecked == true)
            genres.Add("agent");

        return LintRuleEdit.Rebuild(_rule, edited =>
        {
            edited.Title = title;
            edited.Genres = genres;
            edited.Severity = SeverityBox.SelectedIndex == 0 ? "info" : "warn";
            edited.DefaultOff = DefaultOffBox.IsChecked == true;
            edited.Detail = (DetailBox.Text ?? string.Empty).Trim();
            // 词条照表单交上去：数据规则正常写，算法规则只有空表能过校验
            edited.Patterns = patterns;
            // 由代码计算的规则到此为止：匹配方式与段内门限照抄原规则
            if (algorithm)
                return;
            edited.Kind = KindBox.SelectedIndex == 0 ? "literal" : "regex";
            edited.MinCount = minCount;
            // 建议改法与词条一一对齐，词条被改过之后它就对不上了，
            // 所以改过的规则一律不带建议：宁可少一条，也不要一条错位的。
            edited.Hints = null;
        });
    }

    // ================================================================
    //  动作
    // ================================================================

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        LintRule? edited = ReadForm(out string? error);
        if (edited is null)
        {
            ShowWarning(error!);
            return;
        }

        List<LintRule> next = OverridesWith(edited);
        if (!TrySave(next))
            return;

        Changed = true;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    private void OnRestore(object? sender, RoutedEventArgs e)
    {
        List<LintRule> next = OverridesWithout(_rule.Id);
        if (!TrySave(next))
            return;

        Changed = true;
        ReloadRule();
    }

    private void OnRestoreAll(object? sender, RoutedEventArgs e)
    {
        if (!TrySave(new List<LintRule>()))
            return;

        Changed = true;
        ReloadRule();
    }

    /// <summary>恢复之后把表单换回内置值，并把状态行改成「内置」。</summary>
    private void ReloadRule()
    {
        try
        {
            LintRule? fresh = LintRuleStore.Load()
                .FirstOrDefault(rule => string.Equals(rule.Id, _rule.Id, StringComparison.OrdinalIgnoreCase));
            if (fresh is not null)
                _rule = fresh;
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException)
        {
            ShowBanner(ex.Message);
        }

        _overridden = false;
        LoadForm(_rule);
    }

    private List<LintRule> OverridesWith(LintRule edited)
    {
        List<LintRule> next = LoadOverridesOrEmpty();
        int at = next.FindIndex(rule => string.Equals(rule.Id, edited.Id, StringComparison.OrdinalIgnoreCase));
        if (at >= 0)
            next[at] = edited;
        else
            next.Add(edited);
        return next;
    }

    private List<LintRule> OverridesWithout(string id) =>
        LoadOverridesOrEmpty()
            .Where(rule => !string.Equals(rule.Id, id, StringComparison.OrdinalIgnoreCase))
            .ToList();

    private List<LintRule> LoadOverridesOrEmpty()
    {
        try
        {
            return LintRuleStore.LoadOverrides();
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException)
        {
            // 文件坏了仍然要让用户能存回一份能用的：从空表起步，坏文件在下一步被整体覆盖
            ShowBanner(ex.Message);
            return new List<LintRule>();
        }
    }

    /// <summary>
    /// 落盘。校验在 SaveOverrides 里做：非法正则、空词条、拼错的档位都走它，
    /// 这里只负责把错误显示出来。
    /// </summary>
    private bool TrySave(List<LintRule> rules)
    {
        try
        {
            LintRuleStore.SaveOverrides(rules);
            ClearBanner();
            return true;
        }
        catch (Exception ex)
            when (ex is ArgumentException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            ShowBanner(ex.Message);
            return false;
        }
    }

    private void ShowWarning(string key, params object[] args)
    {
        WarningLabel.Text = Loc.T(key, args);
        WarningLabel.IsVisible = true;
        if (TryBrush("WarningFg") is { } brush)
            WarningLabel.Foreground = brush;
    }

    private void ClearWarning()
    {
        WarningLabel.Text = string.Empty;
        WarningLabel.IsVisible = false;
    }

    private void ShowBanner(string message)
    {
        BannerText.Text = Loc.T("MsgLintRulesSaveFailed", message);
        BannerBar.IsVisible = true;
    }

    private void ClearBanner()
    {
        BannerText.Text = string.Empty;
        BannerBar.IsVisible = false;
    }

    private IBrush? TryBrush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out object? value) && value is IBrush brush
            ? brush
            : null;
}
