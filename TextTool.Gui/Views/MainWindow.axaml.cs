using Avalonia.Controls;

namespace TextTool.Gui.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // 页签只负责干活，状态栏归窗口管。六个页签都按同一个接口连线。
        foreach (IStatusReporter tab in new IStatusReporter[]
                 {
                     MergeTabView, JoinTabView, ReplaceTabView,
                     VnTabView, LintTabView, AboutTabView,
                 })
        {
            tab.StatusChanged += message => StatusText.Text = message;
            tab.ErrorOccurred += ShowError;
        }

        // 两个页签共用同一个规则列表实例：在替换页改了顺序，行合并页立刻看到。
        // 读取失败的原因同时交给替换页，它会在页面顶部挂横条。
        List<TextTool.Services.ReplaceRule> rules = LoadReplaceRules(out string? loadError);
        MergeTabView.SetRules(rules);
        ReplaceTabView.SetRules(rules, loadError);
    }

    /// <summary>
    /// 替换规则来自程序目录的 replace_rules.json。配置坏了不能静悄悄地当空规则跑：
    /// 状态栏报一份，返回值里再带一份给替换页，那边会在页面顶部挂醒目横条。
    /// </summary>
    private List<TextTool.Services.ReplaceRule> LoadReplaceRules(out string? loadError)
    {
        try
        {
            loadError = null;
            return TextTool.Services.ReplaceRuleStore.Load();
        }
        catch (InvalidDataException ex)
        {
            loadError = ex.Message;
            StatusText.Text = ex.Message;
            return new List<TextTool.Services.ReplaceRule>();
        }
    }

    private void ShowError(string message) => StatusText.Text = message;
}
