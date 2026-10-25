using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using TextTool.Gui.Localization;
using TextTool.Services;

namespace TextTool.Gui.Views;

/// <summary>
/// 预览窗：只读显示处理结果，点「保存」才写盘。
///
/// 保存后不关窗，结果行报出路径，用户可以接着点保存；关窗就是放弃，一个字节都不写。
/// 编码与旧版一致：UTF-8 带 BOM，原子写盘。所有提示都是行内文字，不弹系统对话框。
/// </summary>
public partial class PreviewWindow : Window
{
    /// <summary>与旧版同编码：UTF-8 带 BOM。</summary>
    private static readonly UTF8Encoding Utf8Bom = new(encoderShouldEmitUTF8Identifier: true);

    private string _outputPath = string.Empty;

    /// <summary>本窗口里用户点过保存并且真的写成功了。ShowAsync 的返回值读它。</summary>
    private bool _saved;

    /// <summary>结果行的文案。存键与实参，切语言时按新语言重刷。</summary>
    private Message? _result;

    /// <summary>
    /// XAML 加载器用的无参构造。界面文字与订阅都在这里挂好，
    /// 内容由 SetContent 补上，两种打开方式共用同一套初始化。
    /// </summary>
    public PreviewWindow()
    {
        InitializeComponent();

        Loc.LanguageChanged += ApplyLocalization;
        Closed += OnClosed;

        // 语义色是运行时赋的，换主题后按新主题重取一次。
        ActualThemeVariantChanged += (_, _) => UpdateResultText();

        ApplyLocalization();
    }

    /// <summary>
    /// 直接构造：合并页的预览是非模态的（边看边改，不占用主窗口），走的是这条。
    /// 需要知道用户到底存了没有的调用方走 ShowAsync。
    /// </summary>
    internal PreviewWindow(string text, string outputPath) : this() => SetContent(text, outputPath);

    /// <summary>打开预览并等用户关窗。返回值表示用户点过保存且写盘成功。</summary>
    public static async Task<bool> ShowAsync(Window owner, string text, string outputPath)
    {
        var window = new PreviewWindow(text, outputPath);
        await window.ShowDialog(owner);
        return window._saved;
    }

    private void SetContent(string text, string outputPath)
    {
        _outputPath = outputPath;
        ContentBox.Text = text;
        ApplyLocalization();
    }

    // ================================================================
    //  文案
    // ================================================================

    private void ApplyLocalization()
    {
        HintLabel.Text = Loc.T("PreviewHint");
        BtnSavePreview.Content = Loc.T("BtnSavePreview");
        BtnClose.Content = Loc.T("BtnClose");
        OutputPathLabel.Text = _outputPath;

        string fileName = Path.GetFileName(_outputPath);
        Title = string.IsNullOrEmpty(fileName)
            ? Loc.T("PreviewTitle")
            : $"{Loc.T("PreviewTitle")} - {fileName}";

        UpdateResultText();
    }

    private void UpdateResultText()
    {
        if (_result is not { } result)
        {
            ResultLabel.Text = string.Empty;
            ResultLabel.IsVisible = false;
            ResultLabel.ClearValue(TextBlock.ForegroundProperty);
            return;
        }

        ResultLabel.Text = Loc.T(result.Key, result.Args);
        ResultLabel.IsVisible = true;
        if (TryBrush(result.Warning ? "WarningFg" : "SuccessFg") is { } brush)
            ResultLabel.Foreground = brush;
        else
            ResultLabel.ClearValue(TextBlock.ForegroundProperty);
    }

    /// <summary>一条待显示的结果文案。存键与实参而不是成品字串，切语言时才能重算。</summary>
    private sealed record Message(string Key, object[] Args, bool Warning);

    /// <summary>
    /// 取资源里的画刷。语义色在 ThemeDictionaries 下，必须带上当前主题变体去查：
    /// 不带变体的重载按 Default 查找，主题字典里的键一个也取不到，
    /// 结果就是颜色悄悄退化成正文色，界面上看不出异常。
    /// </summary>
    private IBrush? TryBrush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out object? value) && value is IBrush brush
            ? brush
            : null;

    // ================================================================
    //  保存与关闭
    // ================================================================

    /// <summary>
    /// 写盘。失败不抛到界面外，也不关窗：原因写在结果行上，用户改完路径设置还能再点。
    /// 反复点保存只重写同一个文件，正文不回填，滚动位置和选中位置都不动。
    /// </summary>
    private void OnSave(object? sender, RoutedEventArgs e)
    {
        try
        {
            AtomicFile.WriteAllText(_outputPath, ContentBox.Text ?? string.Empty, Utf8Bom);
            _saved = true;
            ShowResult("MsgSavedTo", warning: false, _outputPath);
        }
        catch (Exception ex)
        {
            _saved = false;
            ShowResult("MsgSaveFailed", warning: true, ex.Message);
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    /// <summary>关窗即放弃：此时没写过盘，_saved 仍是 false。</summary>
    private void OnClosed(object? sender, EventArgs e) => Loc.LanguageChanged -= ApplyLocalization;

    private void ShowResult(string key, bool warning, params object[] args)
    {
        _result = new Message(key, args, warning);
        UpdateResultText();
    }
}
