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
/// 载入文件的页签共用的那一部分：选文件、编码显示、拖放高亮、三种行内提示、状态栏事件。
///
/// 各页签自己写的只有：几处文案键、选中文件后要置哪个按钮可用、有哪几处提示控件可用，
/// 以及各自的处理流程。控件本身仍在各页签自己的 axaml 里，基类按下面的属性取。
/// </summary>
public abstract class FileTabBase : UserControl, IStatusReporter
{
    private readonly List<string> _selectedFiles = new();

    /// <summary>三种提示记下键与实参，切语言时按新语言重算，不留旧语言的残句。</summary>
    private Message? _banner;
    private Message? _warning;
    private Message? _result;

    protected FileTabBase()
    {
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);

        // 切页签会把本页从可视树上摘下来，切回来再挂上，所以订阅跟着挂载走。
        Loaded += OnTabLoaded;
        Unloaded += OnTabUnloaded;
    }

    /// <summary>状态栏文字。由 MainWindow 订阅。</summary>
    public event Action<string>? StatusChanged;

    /// <summary>需要让用户知道出了错。由 MainWindow 显示。</summary>
    public event Action<string>? ErrorOccurred;

    /// <summary>把一句话报到状态栏。事件只能在声明它的类里触发，所以各页签走这个方法。</summary>
    protected void ReportStatus(string message) => StatusChanged?.Invoke(message);

    /// <summary>把一句话当成错误报给窗口。</summary>
    protected void ReportError(string message) => ErrorOccurred?.Invoke(message);

    /// <summary>已选中的文件，按用户给的顺序。处理时每个文件各自严格检测编码。</summary>
    protected IReadOnlyList<string> SelectedFiles => _selectedFiles;

    /// <summary>第一个文件的检测结果，没选文件或检测失败时为 null。</summary>
    protected DetectionResult? LastDetection { get; private set; }

    // ================================================================
    //  各页签交上来的控件与文案键
    // ================================================================

    protected abstract TextBox FileBoxControl { get; }

    protected abstract Button BrowseButtonControl { get; }

    protected abstract TextBlock SourceLabelControl { get; }

    protected abstract TextBlock ResultLabelControl { get; }

    /// <summary>编码那一格。不显示编码的页签返回 null。</summary>
    protected virtual TextBlock? EncodingLabelControl => null;

    /// <summary>编码那一格的取值。不显示编码的页签返回 null。</summary>
    protected virtual TextBlock? EncodingValueControl => null;

    /// <summary>行内警告。没有这一格的页签返回 null。</summary>
    protected virtual TextBlock? InlineWarningControl => null;

    /// <summary>页面顶部的横条。没有的页签返回 null。</summary>
    protected virtual Border? BannerBarControl => null;

    /// <summary>顶部横条里的文字。没有的页签返回 null。</summary>
    protected virtual TextBlock? BannerTextControl => null;

    /// <summary>源文件一格的文案键。</summary>
    protected virtual string SourceLabelKey => "LabelSourceFile";

    /// <summary>编码一格的文案键。</summary>
    protected virtual string EncodingLabelKey => "LabelEncoding";

    /// <summary>文件选择器的标题，各页签用自己的页签名。</summary>
    protected abstract string PickerTitleKey { get; }

    /// <summary>编码检测成功后能不能把它切回正文色。替换页始终用次要色，那里返回 false。</summary>
    protected virtual bool EncodingInPrimaryColor => true;

    /// <summary>选文件时要不要顺手检测编码。不显示编码的页签返回 false。</summary>
    protected virtual bool DetectsEncoding => true;

    /// <summary>行内警告要不要同时报到状态栏。规则页报，替换页不报。</summary>
    protected virtual bool WarningReachesStatusBar => false;

    /// <summary>
    /// 挂载后要不要重刷一次文案。规则页要关掉：它重刷文案会顺带重扫报告，
    /// 把结果写回状态栏，盖掉状态栏上别的字。
    /// </summary>
    protected virtual bool ApplyLocalizationOnLoad => true;

    /// <summary>按当前语言刷一遍本页文案。各页签的实现先调 <see cref="ApplyCommonLocalization"/>。</summary>
    protected abstract void ApplyLocalization();

    /// <summary>选中文件后各页签要跟着做的事：置按钮可用、清提示。</summary>
    protected abstract void OnFilesSelected(IReadOnlyList<string> files);

    /// <summary>
    /// 离屏验收工具用：把文件直接交给页签，等同一次拖放或浏览选择。
    /// 真实界面没有别的入口，所以只开这一个口子。
    /// </summary>
    internal void UseFilesForCheck(IReadOnlyList<string> paths) => SelectFiles(paths);

    // ================================================================
    //  文案
    // ================================================================

    /// <summary>标签、编码行与三种提示一起刷。各页签的文案方法从这里开始写。</summary>
    protected void ApplyCommonLocalization()
    {
        SourceLabelControl.Text = Loc.T(SourceLabelKey);
        BrowseButtonControl.Content = Loc.T("BtnBrowse");
        if (EncodingLabelControl is { } encodingLabel)
            encodingLabel.Text = Loc.T(EncodingLabelKey);

        RefreshEncoding();
        RefreshMessages();
    }

    /// <summary>编码一行：没选中或检测失败时写「未选中」并灰显。</summary>
    protected void RefreshEncoding()
    {
        if (EncodingValueControl is not { } encodingValue)
            return;

        if (LastDetection is { } detection)
        {
            encodingValue.Text = Loc.T(detection.LocKey);
            encodingValue.Classes.Set("muted", !EncodingInPrimaryColor);
        }
        else
        {
            encodingValue.Text = Loc.T("EncodingNotSelected");
            encodingValue.Classes.Set("muted", true);
        }
    }

    /// <summary>三种提示按当前语言重刷。语言换了，存下来的键还在，文字重算一遍就对了。</summary>
    protected void RefreshMessages()
    {
        if (BannerBarControl is { } bannerBar && BannerTextControl is { } bannerText)
        {
            bannerText.Text = Format(_banner);
            bannerBar.IsVisible = _banner is not null;
        }

        if (InlineWarningControl is { } warning)
        {
            warning.Text = Format(_warning);
            warning.IsVisible = _warning is not null;
            if (_warning is null)
                warning.ClearValue(TextBlock.ForegroundProperty);
            else if (TryBrush("WarningFg") is { } warningBrush)
                warning.Foreground = warningBrush;
        }

        ResultLabelControl.Text = Format(_result);
        if (_result is null)
            ResultLabelControl.ClearValue(TextBlock.ForegroundProperty);
        else if (TryBrush(_result.Warning ? "WarningFg" : "SuccessFg") is { } resultBrush)
            ResultLabelControl.Foreground = resultBrush;
    }

    private static string Format(Message? text) =>
        text is { } value ? Loc.T(value.Key, value.Args) : string.Empty;

    /// <summary>一条待显示的文案。存键与实参而不是成品字串，切语言时才能重算。</summary>
    private sealed record Message(string Key, object[] Args, bool Warning);

    // ================================================================
    //  提示
    // ================================================================

    /// <summary>结果行。同时把这句话报到状态栏。</summary>
    protected void ShowResult(string key, bool warning, params object[] args)
    {
        _result = new Message(key, args, warning);
        ResultLabelControl.Text = Loc.T(key, args);
        if (TryBrush(warning ? "WarningFg" : "SuccessFg") is { } brush)
            ResultLabelControl.Foreground = brush;

        ReportStatus(ResultLabelControl.Text);
    }

    /// <summary>页面顶部横条：配置读不出来、写盘失败这类不该只落在状态栏的事。</summary>
    protected void ShowBanner(string key, params object[] args)
    {
        _banner = new Message(key, args, true);

        if (BannerBarControl is not { } bannerBar || BannerTextControl is not { } bannerText)
            return;

        bannerText.Text = Loc.T(key, args);
        bannerBar.IsVisible = true;
    }

    /// <summary>行内警告：缺输入、文件不见了这类当场要改的事。</summary>
    protected void ShowWarning(string key, params object[] args)
    {
        _warning = new Message(key, args, true);

        if (InlineWarningControl is { } warning)
        {
            warning.Text = Loc.T(key, args);
            if (TryBrush("WarningFg") is { } brush)
                warning.Foreground = brush;

            warning.IsVisible = true;
        }

        if (WarningReachesStatusBar)
            ReportStatus(Loc.T(key, args));
    }

    /// <summary>清掉行内警告：文字清空、隐藏，存下来的那条文案也一并丢掉。</summary>
    protected void ClearWarning()
    {
        _warning = null;

        if (InlineWarningControl is { } warning)
        {
            warning.Text = string.Empty;
            warning.IsVisible = false;
        }
    }

    // ================================================================
    //  画刷
    // ================================================================

    /// <summary>
    /// 取主题画刷。必须带当前主题变体：语义色放在 ThemeDictionaries 里，
    /// 不带变体的重载会按默认变体查、查不到就静默返回 false，
    /// 结果就是颜色悄悄退化成正文色，界面上看不出异常。
    /// </summary>
    protected IBrush? TryBrush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out object? value) && value is IBrush brush
            ? brush
            : null;

    // ================================================================
    //  选文件
    // ================================================================

    // axaml 里 Click="OnBrowse" 挂的是基类这一个，而生成的调用点访问不到 protected 成员，
    // 所以这里给 internal；页签自己的处理器仍然写 private。
    internal async void OnBrowse(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            return;

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Loc.T(PickerTitleKey),
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType(Loc.T("FileFilterText")) { Patterns = new[] { "*.txt" } },
                new FilePickerFileType(Loc.T("FileFilterAll")) { Patterns = new[] { "*" } },
            },
        });

        var paths = files
            .Select(file => file.TryGetLocalPath())
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
            FileBoxControl.Background = highlight;
    }

    private void OnDragLeave(object? sender, RoutedEventArgs e) => ResetDragHighlight();

    private void OnDrop(object? sender, DragEventArgs e)
    {
        ResetDragHighlight();

        var paths = e.Data.GetFiles()?
            .Select(file => file.TryGetLocalPath())
            .OfType<string>()
            .ToArray();

        if (paths is { Length: > 0 })
            SelectFiles(paths);
    }

    /// <summary>拖放结束后清掉本次赋的底色，让样式里的主题色重新生效。</summary>
    private void ResetDragHighlight() => FileBoxControl.ClearValue(TextBox.BackgroundProperty);

    /// <summary>
    /// 选中一批文件。不存在的路径直接丢掉，一个都不剩时保持原来的选择不动。
    /// </summary>
    protected void SelectFiles(IReadOnlyList<string> files)
    {
        _selectedFiles.Clear();
        var valid = files.Where(File.Exists).ToList();
        if (valid.Count == 0)
            return;

        _selectedFiles.AddRange(valid);
        FileBoxControl.Text = valid.Count == 1
            ? valid[0]
            : Loc.T("StatusFilesSelectedShort", valid.Count);

        // 编码只按第一个文件检测，作为参考显示；处理时每个文件各自严格检测。
        if (DetectsEncoding)
        {
            try
            {
                LastDetection = EncodingDetector.Detect(valid[0]);
            }
            catch
            {
                LastDetection = null;
            }

            RefreshEncoding();
        }

        OnFilesSelected(valid);
        ReportStatus(Loc.T("StatusFilesSelected", valid.Count));
    }

    // ================================================================
    //  语言
    // ================================================================

    private void OnTabLoaded(object? sender, RoutedEventArgs e)
    {
        // 先退订再订阅：同一处回调只挂一遍。
        Loc.LanguageChanged -= ApplyLocalization;
        Loc.LanguageChanged += ApplyLocalization;

        if (ApplyLocalizationOnLoad)
            ApplyLocalization();
    }

    private void OnTabUnloaded(object? sender, RoutedEventArgs e) =>
        Loc.LanguageChanged -= ApplyLocalization;
}
