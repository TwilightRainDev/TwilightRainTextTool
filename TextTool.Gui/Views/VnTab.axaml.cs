using System.Text;
using System.Text.RegularExpressions;
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
/// 视觉小说页签：把固定宽度硬换行的脚本排成自然段，可选补全对话标点。
///
/// 与行合并页一样，重活在 Core 里（VNReformatterService、PunctFixerService、批量执行、原子写盘），
/// 这里只管界面。输出路径走 PathHelper.GetProcessedPath，与旧页一致：
/// 处理结果写成同目录下的「*_Processed.*」，原文件不动。
/// 界面上刻意不同的两处：
///   1. 处理前不再弹预览窗，结果直接落盘，进度分条报到状态栏，跑起来再点一次就是取消。
///   2. 处理完成后不弹「是否打开输出文件夹」，输出路径写在结果行上。
/// </summary>
public partial class VnTab : UserControl
{
    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);

    /// <summary>旧页 NumericUpDown 的默认值（100 到 2000 之间的 450）。</summary>
    private const int DefaultMaxPara = 450;

    private readonly List<string> _selectedFiles = new();

    /// <summary>方案勾选窗里勾中的角色名，一行一个，按方案顺序与行顺序。</summary>
    private readonly List<string> _selectedCharacters = new();

    /// <summary>这些角色名来自哪些方案：路线名与场景正则跟着方案走，旧页也是这么喂引擎的。</summary>
    private List<VNCharacterScheme> _involvedSchemes = new();

    private DetectionResult? _lastDetection;
    private CancellationTokenSource? _cts;

    /// <summary>结果行的文案。存键与实参，切语言时按新语言重刷。</summary>
    private Message? _result;

    /// <summary>状态栏文字。由 MainWindow 订阅。</summary>
    public event Action<string>? StatusChanged;

    /// <summary>需要让用户知道出了错。由 MainWindow 显示。</summary>
    public event Action<string>? ErrorOccurred;

    public VnTab()
    {
        InitializeComponent();
        MaxParaBox.Value = DefaultMaxPara;
        ApplyLocalization();

        // 切页签会把本页从可视树上摘下来，切回来再挂上，所以订阅跟着挂载走。
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;

        // 结果行的语义色是运行时赋的，切主题后按新主题重取一次。
        ActualThemeVariantChanged += (_, _) => UpdateResultText();

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        Loc.LanguageChanged -= ApplyLocalization;
        Loc.LanguageChanged += ApplyLocalization;
        ApplyLocalization();
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e) =>
        Loc.LanguageChanged -= ApplyLocalization;

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
        SourceLabel.Text = Loc.T("LabelVNFile");
        BrowseButton.Content = Loc.T("BtnBrowse");
        EncodingLabel.Text = Loc.T("LabelVNEncoding");
        ModeLabel.Text = Loc.T("LabelVNMode");
        AllRadio.Content = Loc.T("RadioVNAll");
        ReformatRadio.Content = Loc.T("RadioVNReformat");
        FixPunctRadio.Content = Loc.T("RadioVNFixPunct");
        MaxParaLabel.Text = Loc.T("LabelVNMaxPara");
        PresetSchemesButton.Content = Loc.T("BtnVNPresetSchemes");
        ProcessButton.Content = _cts is null ? Loc.T("BtnVNProcess") : Loc.T("StatusVNProcessing");

        UpdateEncodingValue();
        UpdatePresetSummary();
        UpdateResultText();
    }

    /// <summary>编码未选中时用次要色，选中后回到正文色。</summary>
    private void UpdateEncodingValue()
    {
        EncodingValue.Text = _lastDetection is null
            ? Loc.T("EncodingNotSelected")
            : Loc.T(_lastDetection.LocKey);
        EncodingValue.Classes.Set("muted", _lastDetection is null);
    }

    private void UpdatePresetSummary()
    {
        PresetSummary.Text = _selectedCharacters.Count == 0
            ? Loc.T("StatusVNPresetNone")
            : Loc.T("StatusVNPresetSummary", _involvedSchemes.Count, _selectedCharacters.Count);
    }

    private void UpdateResultText()
    {
        if (_result is not { } result)
        {
            ResultLabel.Text = string.Empty;
            ResultLabel.ClearValue(TextBlock.ForegroundProperty);
            return;
        }

        ResultLabel.Text = Loc.T(result.Key, result.Args);
        if (TryBrush(result.Warning ? "WarningFg" : "SuccessFg") is { } brush)
            ResultLabel.Foreground = brush;
        else
            ResultLabel.ClearValue(TextBlock.ForegroundProperty);
    }

    /// <summary>一条待显示的结果文案。存键与实参而不是成品字串，切语言时才能重算。</summary>
    private sealed record Message(string Key, object[] Args, bool Warning);

    /// <summary>
    /// 取资源里的画刷。语义色在 ThemeDictionaries 下，必须带上当前主题变体去查：
    /// 不带变体的重载按 Default 查找，主题字典里的键一个也取不到。
    /// </summary>
    private IBrush? TryBrush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out object? value) && value is IBrush brush
            ? brush
            : null;

    // ================================================================
    //  选文件
    // ================================================================

    private async void OnBrowse(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            return;

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Loc.T("TabVN"),
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

    private void ResetDragHighlight() => FileBox.ClearValue(TextBox.BackgroundProperty);

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
        }
        catch
        {
            _lastDetection = null;
        }

        UpdateEncodingValue();
        ProcessButton.IsEnabled = true;
        StatusChanged?.Invoke(Loc.T("StatusFilesSelected", valid.Count));
    }

    // ================================================================
    //  预设角色方案
    // ================================================================

    private async void OnPresetSchemes(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner)
            return;

        List<VNCharacterScheme> schemes;
        try
        {
            schemes = VNCharacterSchemeStore.Load();
        }
        catch (Exception ex)
        {
            // 方案读不出来就没有可勾的东西；此时也不能让用户新建后盖掉原文件。
            ShowResult("MsgConfigBroken", warning: true, ex.Message);
            return;
        }

        var result = await SchemeSelectionDialog.ShowAsync(
            owner, new VnSchemeAdapter(), schemes.Cast<object>().ToList());

        // 取消：磁盘与页面上的勾选都保持原样。
        if (result is null)
            return;

        List<VNCharacterScheme> edited = result.Schemes.Cast<VNCharacterScheme>().ToList();

        // 方案本身的编辑结果先落盘，再谈应用；两件事失败互不牵连。
        if (TrySaveSchemes(edited) is { } saveError)
            ShowResult("MsgSaveFailed", warning: true, saveError);

        List<string> selected = result.SelectedPayloads.Cast<string>().Distinct().ToList();
        if (selected.Count == 0)
        {
            _selectedCharacters.Clear();
            _involvedSchemes.Clear();
            UpdatePresetSummary();
            return;
        }

        _selectedCharacters.Clear();
        _selectedCharacters.AddRange(selected);

        // 勾选窗回传的是拍平的角色名，反查哪些方案被勾过：勾选窗只认行，
        // 方案层的路线名与场景正则得从这里补回去，否则选项只生效一半。
        _involvedSchemes = edited.Where(scheme => HasSelectedLine(scheme, selected)).ToList();

        UpdatePresetSummary();
        ShowResult("StatusVNPresetApplied", warning: false, _involvedSchemes.Count, selected.Count);
    }

    private static bool HasSelectedLine(VNCharacterScheme scheme, List<string> selected) =>
        scheme.Characters.Any(selected.Contains) || scheme.RouteNames.Any(selected.Contains);

    private static string? TrySaveSchemes(List<VNCharacterScheme> schemes)
    {
        try
        {
            VNCharacterSchemeStore.Save(schemes);
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    // ================================================================
    //  执行
    // ================================================================

    private async void OnProcess(object? sender, RoutedEventArgs e)
    {
        // 跑着的时候再点一次就是取消，和行合并页一致。
        if (_cts is { } running)
        {
            running.Cancel();
            ProcessButton.IsEnabled = false;
            ProcessButton.Content = Loc.T("StatusCancelling");
            StatusChanged?.Invoke(Loc.T("StatusCancelling"));
            return;
        }

        if (_selectedFiles.Count == 0)
            return;

        // 全部选项在点击的这一刻读一次，跑起来之后控件再改也不影响本次处理。
        string[] files = _selectedFiles.ToArray();
        int maxPara = (int)(MaxParaBox.Value ?? DefaultMaxPara);
        bool doReformat = AllRadio.IsChecked == true || ReformatRadio.IsChecked == true;
        bool doFixPunct = AllRadio.IsChecked == true || FixPunctRadio.IsChecked == true;

        CharacterOptions options;
        try
        {
            options = BuildCharacterOptions();
        }
        catch (ArgumentException ex)
        {
            // 方案里的场景正则非法（配置被改坏），RegexGuard 在这里抛。
            ShowResult("MsgProcessFailed", warning: true, ex.Message);
            return;
        }

        _cts = new CancellationTokenSource();
        ProcessButton.IsEnabled = false;
        ProcessButton.Content = Loc.T("StatusVNProcessing");

        var progress = new Progress<(int Done, string Error)>(p =>
        {
            if (string.IsNullOrEmpty(p.Error))
                StatusChanged?.Invoke(Loc.T("StatusBatchProgress", p.Done, files.Length));
            else
                ErrorOccurred?.Invoke(Loc.T("MsgProcessFailed", $"{Path.GetFileName(files[p.Done - 1])}：{p.Error}"));
        });

        try
        {
            var (success, cancelled) = await BatchRunner.RunAsync(
                files,
                (path, _) =>
                {
                    var detection = EncodingDetector.DetectStrict(path);
                    string content = File.ReadAllText(path, detection.Encoding);
                    if (doReformat)
                        content = Reformat(content, options, maxPara);
                    if (doFixPunct)
                        content = new PunctFixerService().Fix(content);
                    AtomicFile.WriteAllText(PathHelper.GetProcessedPath(path), content, Utf8WithBom);
                },
                progress,
                _cts.Token);

            ProcessButton.IsEnabled = true;
            ProcessButton.Content = Loc.T("BtnVNProcess");

            if (cancelled)
                ShowResult("StatusBatchCancelled", warning: true, success);
            else if (success == files.Length)
                ShowResult("StatusVNComplete", warning: false, OutputTarget(files));
            else
                ShowResult("StatusBatchPartial", warning: true, success, files.Length);
        }
        catch (Exception ex)
        {
            ProcessButton.IsEnabled = true;
            ProcessButton.Content = Loc.T("BtnVNProcess");
            ShowResult("MsgProcessFailed", warning: true, ex.Message);
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
        }
    }

    /// <summary>跑完一行结果：单文件报具体输出文件，多文件报共同目录。</summary>
    private static string OutputTarget(IReadOnlyList<string> files) =>
        files.Count == 1
            ? PathHelper.GetProcessedPath(files[0])
            : Path.GetDirectoryName(files[0]) ?? files[0];

    /// <summary>喂给排版引擎的角色集、路线名与场景正则。</summary>
    private sealed record CharacterOptions(
        HashSet<string> Characters, List<string> RouteNames, Regex? ScenePattern);

    private CharacterOptions BuildCharacterOptions()
    {
        if (_selectedCharacters.Count == 0)
            return DefaultOptions();

        var routeNames = new List<string>();
        var scenePatterns = new List<string>();
        foreach (VNCharacterScheme scheme in _involvedSchemes)
        {
            routeNames.AddRange(scheme.RouteNames);
            if (!string.IsNullOrWhiteSpace(scheme.ScenePattern))
                scenePatterns.Add(scheme.ScenePattern!);
        }

        return new CharacterOptions(
            new HashSet<string>(_selectedCharacters), routeNames, CombineScenePattern(scenePatterns));
    }

    /// <summary>
    /// 没勾任何角色时的角色集：内置默认方案里的 Steins;Gate 那一份，
    /// 与 StatusVNPresetNone 的提示一致。内置资源读不出来时退回空集合，处理照常进行。
    /// </summary>
    private static CharacterOptions DefaultOptions()
    {
        try
        {
            List<VNCharacterScheme> schemes = VNCharacterSchemeStore.GetDefaultSchemes();
            VNCharacterScheme? scheme =
                schemes.FirstOrDefault(s => s.Name.Contains("Steins;Gate", StringComparison.Ordinal))
                ?? schemes.FirstOrDefault();

            if (scheme is null)
                return EmptyOptions;

            var scenePatterns = new List<string>();
            if (!string.IsNullOrWhiteSpace(scheme.ScenePattern))
                scenePatterns.Add(scheme.ScenePattern!);

            return new CharacterOptions(
                new HashSet<string>(scheme.Characters), scheme.RouteNames.ToList(),
                CombineScenePattern(scenePatterns));
        }
        catch
        {
            return EmptyOptions;
        }
    }

    private static readonly CharacterOptions EmptyOptions =
        new(new HashSet<string>(), new List<string>(), null);

    /// <summary>多个方案各带一条场景正则时合成一条；经 RegexGuard 构造，超时可控。</summary>
    private static Regex? CombineScenePattern(List<string> patterns) =>
        patterns.Count > 0 ? RegexGuard.Create(string.Join("|", patterns)) : null;

    /// <summary>排版；场景正则超时（ReDoS 防御）时翻成一句能照着改的说明。</summary>
    private static string Reformat(string content, CharacterOptions options, int maxPara)
    {
        try
        {
            return new VNReformatterService(
                characters: options.Characters,
                routeNames: options.RouteNames,
                maxParaLength: maxPara,
                scenePattern: options.ScenePattern).Reformat(content);
        }
        catch (RegexMatchTimeoutException)
        {
            throw new InvalidOperationException("场景标记正则执行超时，请编辑方案简化规则后重试");
        }
    }

    // ================================================================
    //  提示
    // ================================================================

    private void ShowResult(string key, bool warning, params object[] args)
    {
        _result = new Message(key, args, warning);
        UpdateResultText();
        StatusChanged?.Invoke(Loc.T(key, args));
    }
}
