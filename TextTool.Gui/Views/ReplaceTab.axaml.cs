using System.Collections.ObjectModel;
using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using TextTool.Gui.Localization;
using TextTool.Services;

namespace TextTool.Gui.Views;

/// <summary>
/// 标点替换页签：管理替换规则，然后按规则批量改写文件。
///
/// 与行合并页一样，重活在 Core 里（规则引擎、批量执行、原子写盘），这里只管界面。
/// 四条硬约束，判据是同一条：界面上说的和磁盘上发生的必须对得上。
///   1. 列表重建后清空编辑框：留着上一条的内容，改完再点「添加/更新」会多出一条重复规则。
///   2. 保存失败要说出来：落盘出错时状态栏不能显示「已保存」。
///   3. 方案弹窗点「取消」什么都不写：列表不能因此盖回磁盘。
///   4. 配置读不出来时在页面顶部挂一条横条，不只报到状态栏。
/// </summary>
public partial class ReplaceTab : FileTabBase
{
    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);

    private readonly ObservableCollection<string> _ruleTexts = new();

    private List<ReplaceRule> _rules = new();
    private CancellationTokenSource? _cts;

    public ReplaceTab()
    {
        InitializeComponent();
        RuleList.ItemsSource = _ruleTexts;
        ApplyLocalization();
    }

    /// <summary>
    /// 规则由 MainWindow 注入：它和行合并页共用同一个列表实例，
    /// 本页的增删改对那边立即可见。读取失败的原因一并传进来，挂在顶部横条上。
    /// </summary>
    public void SetRules(List<ReplaceRule> rules, string? loadError = null)
    {
        _rules = rules;
        if (!string.IsNullOrEmpty(loadError))
            ShowBanner("MsgConfigBroken", loadError);

        RefreshRules();
    }

    // ================================================================
    //  文案
    // ================================================================

    protected override TextBox FileBoxControl => FileBox;

    protected override Button BrowseButtonControl => BrowseButton;

    protected override TextBlock SourceLabelControl => SourceLabel;

    protected override TextBlock ResultLabelControl => ResultLabel;

    protected override TextBlock EncodingLabelControl => EncodingLabel;

    protected override TextBlock EncodingValueControl => EncodingValue;

    protected override TextBlock InlineWarningControl => InlineWarning;

    protected override Border BannerBarControl => ConfigErrorBar;

    protected override TextBlock BannerTextControl => ConfigErrorText;

    protected override string PickerTitleKey => "TabReplace";

    /// <summary>本页的编码行从头到尾都用次要色，检测成功也不切回正文色。</summary>
    protected override bool EncodingInPrimaryColor => false;

    protected override void ApplyLocalization()
    {
        ApplyCommonLocalization();

        RulesLabel.Text = Loc.T("LabelRules");
        FindLabel.Text = Loc.T("LabelFind");
        ReplaceWithLabel.Text = Loc.T("LabelReplaceWith");
        AddUpdateButton.Content = Loc.T("BtnAddUpdate");
        DeleteButton.Content = Loc.T("BtnDelete");
        MoveUpButton.Content = Loc.T("BtnUp");
        MoveDownButton.Content = Loc.T("BtnDown");
        PresetSchemesButton.Content = Loc.T("BtnPresetSchemes");
        ReplaceButton.Content = _cts is null
            ? Loc.T("BtnExecuteReplace")
            : Loc.T("StatusReplacing");
        HintLabel.Text = Loc.T("HintRuleEdit");
    }

    // ================================================================
    //  选文件
    // ================================================================

    protected override void OnFilesSelected(IReadOnlyList<string> files) =>
        ReplaceButton.IsEnabled = true;

    // ================================================================
    //  规则编辑
    // ================================================================

    /// <summary>
    /// 重建列表文字。编辑框一律清空：列表一重建，原来的选中项就没有了，
    /// 留着旧内容会让下一次「添加/更新」误当成新增。
    /// </summary>
    private void RefreshRules(int selectIndex = -1)
    {
        _ruleTexts.Clear();
        foreach (var rule in _rules)
            _ruleTexts.Add($"{rule.Find}  →  {rule.Replace}");

        FindBox.Text = string.Empty;
        ReplaceBox.Text = string.Empty;

        RuleList.SelectedIndex = -1;
        if (selectIndex >= 0 && selectIndex < _ruleTexts.Count)
            RuleList.SelectedIndex = selectIndex;

        UpdateRuleButtons();
    }

    private void UpdateRuleButtons()
    {
        int index = RuleList.SelectedIndex;
        DeleteButton.IsEnabled = index >= 0 && index < _rules.Count;
        MoveUpButton.IsEnabled = index > 0;
        MoveDownButton.IsEnabled = index >= 0 && index < _rules.Count - 1;
    }

    private void OnRuleSelected(object? sender, SelectionChangedEventArgs e)
    {
        int index = RuleList.SelectedIndex;
        if (index >= 0 && index < _rules.Count)
        {
            FindBox.Text = _rules[index].Find;
            ReplaceBox.Text = _rules[index].Replace;
        }
        else
        {
            FindBox.Text = string.Empty;
            ReplaceBox.Text = string.Empty;
        }

        UpdateRuleButtons();
    }

    private void OnAddOrUpdate(object? sender, RoutedEventArgs e)
    {
        string find = FindBox.Text ?? string.Empty;
        if (string.IsNullOrEmpty(find))
        {
            ShowWarning("MsgEnterFind");
            return;
        }

        var rule = new ReplaceRule { Find = find, Replace = ReplaceBox.Text ?? string.Empty };
        int index = RuleList.SelectedIndex;

        if (index >= 0 && index < _rules.Count)
            _rules[index] = rule;
        else
            _rules.Add(rule);

        // 提交后一律回到"无选中"：留着选中，用户接着输入下一条再点「添加/更新」
        // 就会改写刚加的那条，界面上看不出区别，这是最难察觉的一种丢数据。
        CommitRules(-1, Loc.T("StatusRulesSaved", _rules.Count));
    }

    private void OnDelete(object? sender, RoutedEventArgs e)
    {
        int index = RuleList.SelectedIndex;
        if (index < 0 || index >= _rules.Count)
            return;

        _rules.RemoveAt(index);
        CommitRules(-1, Loc.T("StatusRuleDeleted", _rules.Count));
    }

    private void OnMoveUp(object? sender, RoutedEventArgs e) => MoveRule(-1);

    private void OnMoveDown(object? sender, RoutedEventArgs e) => MoveRule(1);

    private void MoveRule(int offset)
    {
        int index = RuleList.SelectedIndex;
        int target = index + offset;
        if (index < 0 || index >= _rules.Count || target < 0 || target >= _rules.Count)
            return;

        (_rules[target], _rules[index]) = (_rules[index], _rules[target]);
        CommitRules(target, Loc.T("StatusRulesSaved", _rules.Count));
    }

    /// <summary>
    /// 落盘后刷新列表。保存失败不能报成成功：状态栏说「已保存」就得真的存了。
    /// </summary>
    private void CommitRules(int selectIndex, string statusText)
    {
        string? error = TrySaveRules();
        RefreshRules(selectIndex);

        if (error is null)
        {
            ClearWarning();
            ReportStatus(statusText);
            return;
        }

        ShowBanner("MsgSaveFailed", error);
        ReportStatus(Loc.T("MsgSaveFailed", error));
    }

    private string? TrySaveRules()
    {
        try
        {
            ReplaceRuleStore.Save(_rules);
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    // ================================================================
    //  预设方案
    // ================================================================

    private async void OnPresetSchemes(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner)
            return;

        List<ReplaceScheme> schemes;
        try
        {
            schemes = ReplaceSchemeStore.Load();
        }
        catch (Exception ex)
        {
            ShowBanner("MsgConfigBroken", ex.Message);
            return;
        }

        if (await CheckListDialog.ShowAsync(owner, new ReplaceSchemeAdapter(schemes))
            is not SchemeSelectionResult result)
            return;

        // 方案本身的编辑结果先落盘，再谈应用；两件事失败互不牵连。
        string? schemeError = TrySaveSchemes(result.Schemes.Cast<ReplaceScheme>().ToList());
        if (schemeError is not null)
            ShowBanner("MsgSaveFailed", schemeError);

        List<ReplaceRule> selectedRules = result.SelectedPayloads.Cast<ReplaceRule>().ToList();
        if (selectedRules.Count == 0)
        {
            RefreshRules();
            return;
        }

        int before = _rules.Count;
        _rules.AddRange(selectedRules);
        CommitRules(
            _rules.Count - 1,
            Loc.T("StatusSchemeApplied", selectedRules.Count, before, _rules.Count));
    }

    private static string? TrySaveSchemes(List<ReplaceScheme> schemes)
    {
        try
        {
            ReplaceSchemeStore.Save(schemes);
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    // ================================================================
    //  执行替换
    // ================================================================

    private async void OnReplace(object? sender, RoutedEventArgs e)
    {
        // 跑着的时候再点一次就是取消，和行合并页一致。
        if (_cts is { } running)
        {
            running.Cancel();
            ReplaceButton.IsEnabled = false;
            ReplaceButton.Content = Loc.T("StatusCancelling");
            ReportStatus(Loc.T("StatusCancelling"));
            return;
        }

        if (SelectedFiles.Count == 0)
            return;

        if (_rules.Count == 0)
        {
            ShowBanner("MsgAddRule");
            return;
        }

        var rules = _rules.ToList();
        string[] files = SelectedFiles.ToArray();

        _cts = new CancellationTokenSource();
        ReplaceButton.IsEnabled = false;
        ReplaceButton.Content = Loc.T("StatusReplacing");

        var progress = new Progress<(int Done, string Error)>(p =>
        {
            if (string.IsNullOrEmpty(p.Error))
                ReportStatus(Loc.T("StatusBatchProgress", p.Done, files.Length));
            else
                ReportError(Loc.T("MsgReplaceFailed", $"{Path.GetFileName(files[p.Done - 1])}：{p.Error}"));
        });

        try
        {
            CancellationToken token = _cts.Token;

            var (success, cancelled) = await BatchRunner.RunAsync(
                files,
                (path, _) =>
                {
                    var detection = EncodingDetector.DetectStrict(path);
                    string text = File.ReadAllText(path, detection.Encoding);
                    string replaced = PunctuationReplacer.Apply(text, rules);
                    AtomicFile.WriteAllText(PathHelper.GetProcessedPath(path), replaced, Utf8WithBom);
                },
                progress,
                token);

            ReplaceButton.IsEnabled = true;
            ReplaceButton.Content = Loc.T("BtnExecuteReplace");

            if (cancelled)
                ShowResult("StatusBatchCancelled", true, success);
            else if (success == files.Length)
                ShowResult("StatusBatchComplete", false, success);
            else
                ShowResult("StatusBatchPartial", true, success, files.Length);
        }
        catch (Exception ex)
        {
            ReplaceButton.IsEnabled = true;
            ReplaceButton.Content = Loc.T("BtnExecuteReplace");
            ReportError(Loc.T("MsgReplaceFailed", ex.Message));
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
        }
    }
}
