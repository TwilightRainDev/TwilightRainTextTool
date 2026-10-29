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
/// 两条硬约束：
///   1. 确定之前先校验，校验不过不落盘。写进一条非法正则，下次启动整个程序起不来。
///   2. 恢复内置与恢复全部内置都是立即写盘的动作，写完窗口不关，表单显示内置值。
/// </summary>
public partial class LintRuleEditDialog : Window
{
    private LintRule _rule;
    private bool _overridden;

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
        TitleBox.Text = rule.Title;
        KindBox.SelectedIndex = rule.Kind == "literal" ? 0 : 1;
        GenreDocBox.IsChecked = rule.Genres.Contains("doc", StringComparer.OrdinalIgnoreCase);
        GenreFictionBox.IsChecked = rule.Genres.Contains("fiction", StringComparer.OrdinalIgnoreCase);
        GenreAgentBox.IsChecked = rule.Genres.Contains("agent", StringComparer.OrdinalIgnoreCase);
        SeverityBox.SelectedIndex = rule.Severity == "info" ? 0 : 1;
        MinCountBox.Text = rule.MinCount.ToString();
        PatternsBox.Text = string.Join(Environment.NewLine, rule.Patterns);
        DetailBox.Text = rule.Detail;
        ClearWarning();
        UpdateState();
    }

    /// <summary>
    /// 表单 → 规则。只挑界面上开放的字段，其余照抄原规则：
    /// 范围、段末字数、打断权这些不是本窗能改的，抄错一处就是悄悄改了判据行为。
    /// </summary>
    private LintRule? ReadForm(out string? error)
    {
        error = null;

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
        if (patterns.Count == 0)
        {
            error = "MsgLintRulePatternsEmpty";
            return null;
        }

        if (!int.TryParse((MinCountBox.Text ?? string.Empty).Trim(), out int minCount) || minCount < 1)
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

        // 建议改法与词条一一对齐，词条被改过之后它就对不上了，
        // 所以改过的规则一律不带建议：宁可少一条，也不要一条错位的。
        return new LintRule
        {
            Id = _rule.Id,
            Group = _rule.Group,
            Title = title,
            Kind = KindBox.SelectedIndex == 0 ? "literal" : "regex",
            Patterns = patterns,
            Scope = _rule.Scope,
            Genres = genres,
            TailChars = _rule.TailChars,
            MinCount = minCount,
            Severity = SeverityBox.SelectedIndex == 0 ? "info" : "warn",
            BlocksReply = _rule.BlocksReply,
            ReplyMinCount = _rule.ReplyMinCount,
            SkipInMarkdown = _rule.SkipInMarkdown,
            Detail = (DetailBox.Text ?? string.Empty).Trim(),
            Hints = null,
            SuggestScheme = _rule.SuggestScheme,
        };
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
