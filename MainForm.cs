using TextTool.Controls;
using TextTool.Localization;
using TextTool.Services;

namespace TextTool;

/// <summary>
/// 主窗口 — 组装 4 个 UserControl 页签 + 状态栏。
/// 每个页签独立管理自己的 UI、事件和状态。
/// </summary>
public sealed class MainForm : Form
{
    // ===== 页签控件引用（供 ApplyLocalization / ApplyTheme 遍历使用）=====
    private MergeTabControl _mergeTab = null!;
    private JoinTabControl _joinTab = null!;
    private ReplaceTabControl _replaceTab = null!;
    private AboutTabControl _aboutTab = null!;

    // ===== 共享状态 =====
    private TabControl _tabControl = null!;
    private Panel _mainPanel = null!;
    private StatusStrip _statusStrip = null!;
    private ToolStripStatusLabel _statusLabel = null!;
    private List<ReplaceRule> _rules = null!;

    public MainForm()
    {
        ThemeManager.Init();   // 先加载深色模式偏好（供 Loc.SaveLanguage 持久化时使用）
        Loc.Init();
        InitializeComponent();
        ApplyTheme();          // 应用初始主题
        ApplyLocalization();
        Loc.LanguageChanged += () => BeginInvoke(ApplyLocalization);
        ThemeManager.ThemeChanged += _ => BeginInvoke(ApplyTheme);
    }

    private void InitializeComponent()
    {
        // ---- Form 属性 ----
        Text = "TwilightRain's Text Tool";
        Size = new Size(700, 560);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 10f);
        AllowDrop = true;
        try
        {
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }
        catch { }

        // ---- 加载共享规则列表 ----
        _rules = ReplaceRuleStore.Load();

        // ---- TabControl ----
        _tabControl = new TabControl
        {
            Dock = DockStyle.Fill,
            Padding = new Point(12, 8)
        };

        // Tab 1: 行合并
        _mergeTab = new MergeTabControl { ReplaceRules = _rules };
        _mergeTab.StatusChanged += SetStatus;
        _mergeTab.ErrorOccurred += ShowError;
        _tabControl.TabPages.Add(CreateTabPage(_mergeTab, "Line Merge"));

        // Tab 2: 文件拼接
        _joinTab = new JoinTabControl();
        _joinTab.StatusChanged += SetStatus;
        _joinTab.ErrorOccurred += ShowError;
        _tabControl.TabPages.Add(CreateTabPage(_joinTab, "File Join"));

        // Tab 3: 标点替换
        _replaceTab = new ReplaceTabControl(_rules);
        _replaceTab.StatusChanged += SetStatus;
        _replaceTab.ErrorOccurred += ShowError;
        _tabControl.TabPages.Add(CreateTabPage(_replaceTab, "Punct. Replace"));

        // Tab 4: 关于
        _aboutTab = new AboutTabControl();
        _aboutTab.StatusChanged += SetStatus;
        _aboutTab.ErrorOccurred += ShowError;
        _tabControl.TabPages.Add(CreateTabPage(_aboutTab, "About"));

        // ---- StatusStrip ----
        _statusStrip = new StatusStrip();
        _statusLabel = new ToolStripStatusLabel("Ready") { Spring = true };
        _statusStrip.Items.Add(_statusLabel);

        // ---- 组装 ----
        _mainPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        _mainPanel.Controls.Add(_tabControl);

        Controls.Add(_mainPanel);
        Controls.Add(_statusStrip);
    }

    /// <summary>将 UserControl 放入 TabPage 并返回该页</summary>
    private static TabPage CreateTabPage(UserControl control, string title)
    {
        var page = new TabPage(title);
        control.Dock = DockStyle.Fill;
        page.Controls.Add(control);
        return page;
    }

    // ================================================================
    //  主题（深色/浅色 — 由 ThemeManager 统一驱动）
    // ================================================================

    private void ApplyTheme()
    {
        BackColor = ThemeManager.Bg;
        ForeColor = ThemeManager.Fg;

        _mainPanel.BackColor = ThemeManager.Bg;
        _tabControl.BackColor = ThemeManager.Bg;
        foreach (TabPage page in _tabControl.TabPages)
            page.BackColor = ThemeManager.Bg;

        _statusStrip.BackColor = ThemeManager.IsDarkMode ? ThemeManager.DarkControlBg : SystemColors.Control;
        _statusLabel.ForeColor = ThemeManager.Fg;

        _mergeTab.ApplyTheme();
        _joinTab.ApplyTheme();
        _replaceTab.ApplyTheme();
        _aboutTab.ApplyTheme();
    }

    // ================================================================
    //  本地化（统一派发）
    // ================================================================

    private void ApplyLocalization()
    {
        Text = Loc.T("AppTitle");

        _tabControl.TabPages[0].Text = Loc.T("TabMerge");
        _tabControl.TabPages[1].Text = Loc.T("TabJoin");
        _tabControl.TabPages[2].Text = Loc.T("TabReplace");
        _tabControl.TabPages[3].Text = Loc.T("TabAbout");

        _statusLabel.Text = Loc.T("StatusReady");

        // 广播到各页签
        _mergeTab.ApplyLocalization();
        _joinTab.ApplyLocalization();
        _replaceTab.ApplyLocalization();
        _aboutTab.ApplyLocalization();
    }

    // ================================================================
    //  状态栏 & 错误处理（各页签通过事件回调）
    // ================================================================

    private void SetStatus(string message) => _statusLabel.Text = message;

    private void ShowError(string message)
    {
        _statusLabel.Text = $"❌ {message}";
        MessageBox.Show(this, message, Loc.T("MsgErrorTitle"),
            MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
