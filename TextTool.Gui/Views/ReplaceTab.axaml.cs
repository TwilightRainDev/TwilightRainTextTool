using System.Collections.ObjectModel;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using TextTool.Gui.Localization;
using TextTool.Services;

namespace TextTool.Gui.Views;

/// <summary>
/// 标点替换页签：管理替换规则，然后按规则批量改写文件。
///
/// 与行合并页一样，重活在 Core 里（规则引擎、批量执行、原子写盘），这里只管界面。
/// 刻意与 WinForms 版不同的四处，都是同一类问题：界面上说的和磁盘上发生的对不上。
///   1. 列表重建后清空编辑框。旧版保留上一条的内容，改完一条再点「添加/更新」
///      会在毫无提示的情况下多出一条重复规则。
///   2. 保存失败要说出来。旧版落盘链路没有异常处理，失败了状态栏照样显示「已保存」。
///   3. 方案弹窗点「取消」什么都不写。旧版取消也会把方案列表整份盖回磁盘。
///   4. 配置读不出来时在页面顶部挂一条横条，不再只报到状态栏。
/// </summary>
public partial class ReplaceTab : UserControl
{
    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);

    private readonly List<string> _selectedFiles = new();
    private readonly ObservableCollection<string> _ruleTexts = new();

    private List<ReplaceRule> _rules = new();
    private DetectionResult? _lastDetection;
    private CancellationTokenSource? _cts;

    /// <summary>动态文案记下键与参数，切语言时按新语言重刷，不留旧语言的残句。</summary>
    private Message? _banner;
    private Message? _warning;
    private Message? _result;

    /// <summary>状态栏文字。由 MainWindow 订阅。</summary>
    public event Action<string>? StatusChanged;

    /// <summary>需要让用户知道出了错。由 MainWindow 显示。</summary>
    public event Action<string>? ErrorOccurred;

    public ReplaceTab()
    {
        InitializeComponent();
        RuleList.ItemsSource = _ruleTexts;
        ApplyLocalization();
        Loc.LanguageChanged += ApplyLocalization;

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
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

    /// <summary>
    /// 离屏验收工具用：把文件直接交给页签，等同一次拖放或浏览选择。
    /// 真实界面没有别的入口，所以只开这一个口子。
    /// </summary>
    internal void UseFilesForCheck(IReadOnlyList<string> paths) => SelectFiles(paths);

    // ================================================================
    //  文案
    // ================================================================

    private void ApplyLocalization()
    {
        SourceLabel.Text = Loc.T("LabelSourceFile");
        BrowseButton.Content = Loc.T("BtnBrowse");
        EncodingLabel.Text = Loc.T("LabelEncoding");
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

        EncodingValue.Text = _lastDetection is null
            ? Loc.T("EncodingNotSelected")
            : Loc.T(_lastDetection.LocKey);

        ConfigErrorText.Text = Format(_banner);
        ConfigErrorBar.IsVisible = _banner is not null;
        InlineWarning.Text = Format(_warning);
        InlineWarning.IsVisible = _warning is not null;
        ResultLabel.Text = Format(_result);
        if (_result is not null && TryBrush(_result.Warning ? "WarningFg" : "SuccessFg") is { } brush)
            ResultLabel.Foreground = brush;
    }

    private static string Format(Message? text) =>
        text is { } t ? Loc.T(t.Key, t.Args) : string.Empty;

    /// <summary>一条待显示的文案。存键与实参而不是成品字串，切语言时才能重算。</summary>
    private sealed record Message(string Key, object[] Args, bool Warning);

    // ================================================================
    //  选文件
    // ================================================================

    private async void OnBrowse(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            return;

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Loc.T("TabReplace"),
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType(Loc.T("FileFilterText")) { Patterns = new[] { "*.txt" } },
                new FilePickerFileType(Loc.T("FileFilterAll")) { Patterns = new[] { "*" } },
            },
        });

        var paths = files
            .Select(f => f.TryGetLocalPath())
            .OfType<string>()
            .ToArray();

        if (paths.Length > 0)
            SelectFiles(paths);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        bool hasFiles = e.Data.Contains(DataFormats.Files);
        e.DragEffects = hasFiles ? DragDropEffects.Copy : DragDropEffects.None;
        if (hasFiles && TryBrush("DragHighlightBg") is { } highlight)
            FileBox.Background = highlight;
    }

    private void OnDragLeave(object? sender, RoutedEventArgs e) => ResetDragHighlight();

    private void OnDrop(object? sender, DragEventArgs e)
    {
        ResetDragHighlight();

        var paths = e.Data.GetFiles()?
            .Select(f => f.TryGetLocalPath())
            .OfType<string>()
            .ToArray();

        if (paths is { Length: > 0 })
            SelectFiles(paths);
    }

    /// <summary>
    /// 取主题画刷。必须带当前主题变体：语义色放在 ThemeDictionaries 里，
    /// 不带变体的重载会按默认变体查、查不到就静默返回 false，
    /// 结果就是颜色悄悄退化成正文色，界面上看不出异常。
    /// </summary>
    private IBrush? TryBrush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out object? value) && value is IBrush brush
            ? brush
            : null;

    private void ResetDragHighlight() => FileBox.ClearValue(BackgroundProperty);

    private void SelectFiles(IReadOnlyList<string> files)
    {
        _selectedFiles.Clear();
        var valid = files.Where(File.Exists).ToList();
        if (valid.Count == 0)
            return;

        _selectedFiles.AddRange(valid);
        FileBox.Text = valid.Count == 1 ? valid[0] : Loc.T("StatusFilesSelectedShort", valid.Count);

        // 编码只按第一个文件检测，作为参考显示；处理时每个文件各自严格检测。
        try
        {
            _lastDetection = EncodingDetector.Detect(valid[0]);
            EncodingValue.Text = Loc.T(_lastDetection.LocKey);
            EncodingValue.Classes.Set("muted", false);
        }
        catch
        {
            _lastDetection = null;
            EncodingValue.Text = Loc.T("EncodingNotSelected");
            EncodingValue.Classes.Set("muted", true);
        }

        ReplaceButton.IsEnabled = true;
        StatusChanged?.Invoke(Loc.T("StatusFilesSelected", valid.Count));
    }

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
    /// 落盘后刷新列表。保存失败不能报成成功：旧版就是在这个位置对用户说了假话。
    /// </summary>
    private void CommitRules(int selectIndex, string statusText)
    {
        string? error = TrySaveRules();
        RefreshRules(selectIndex);

        if (error is null)
        {
            ClearWarning();
            StatusChanged?.Invoke(statusText);
            return;
        }

        ShowBanner("MsgSaveFailed", error);
        StatusChanged?.Invoke(Loc.T("MsgSaveFailed", error));
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

        var result = await SchemeSelectionDialog.ShowAsync(
            owner, new ReplaceSchemeAdapter(), schemes.Cast<object>().ToList());
        if (result is null)
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
            StatusChanged?.Invoke(Loc.T("StatusCancelling"));
            return;
        }

        if (_selectedFiles.Count == 0)
            return;

        if (_rules.Count == 0)
        {
            ShowBanner("MsgAddRule");
            return;
        }

        var rules = _rules.ToList();
        string[] files = _selectedFiles.ToArray();

        _cts = new CancellationTokenSource();
        ReplaceButton.IsEnabled = false;
        ReplaceButton.Content = Loc.T("StatusReplacing");

        var progress = new Progress<(int Done, string Error)>(p =>
        {
            if (string.IsNullOrEmpty(p.Error))
                StatusChanged?.Invoke(Loc.T("StatusBatchProgress", p.Done, files.Length));
            else
                ErrorOccurred?.Invoke(Loc.T("MsgReplaceFailed", $"{Path.GetFileName(files[p.Done - 1])}：{p.Error}"));
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
            ErrorOccurred?.Invoke(Loc.T("MsgReplaceFailed", ex.Message));
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
        }
    }

    // ================================================================
    //  提示
    // ================================================================

    private void ShowResult(string key, bool warning, params object[] args)
    {
        _result = new Message(key, args, warning);
        ResultLabel.Text = Loc.T(key, args);
        if (TryBrush(warning ? "WarningFg" : "SuccessFg") is { } brush)
            ResultLabel.Foreground = brush;
        StatusChanged?.Invoke(ResultLabel.Text);
    }

    private void ShowBanner(string key, params object[] args)
    {
        _banner = new Message(key, args, true);
        ConfigErrorText.Text = Loc.T(key, args);
        ConfigErrorBar.IsVisible = true;
    }

    private void ShowWarning(string key, params object[] args)
    {
        _warning = new Message(key, args, true);
        InlineWarning.Text = Loc.T(key, args);
        if (TryBrush("WarningFg") is { } brush)
            InlineWarning.Foreground = brush;
        InlineWarning.IsVisible = true;
    }

    private void ClearWarning()
    {
        _warning = null;
        InlineWarning.Text = string.Empty;
        InlineWarning.IsVisible = false;
    }
}
