using System.Text;
using TextTool.Localization;
using TextTool.Services;

namespace TextTool;

/// <summary>
/// TwilightRain的文本工具 v1.4.0
/// - 行合并（阈值 + 中文截断修复 + 标点替换）
/// - 文件拼接
/// - 标点替换规则管理
/// - 关于
/// - i18n 国际化（zh_CN / zh_TW / en_US）
/// </summary>
public sealed class MainForm : Form
{
    // ===== Tab control =====
    private TabControl _tabControl = null!;
    private TabPage _tabMerge = null!;
    private TabPage _tabJoin = null!;
    private TabPage _tabReplace = null!;
    private TabPage _tabAbout = null!;

    // ===== Tab 1 "行合并" 控件 =====
    private Label _lblSourceFile = null!;
    private TextBox _txtFilePath = null!;
    private Button _btnBrowse = null!;
    private Label _lblThreshold = null!;
    private NumericUpDown _numThreshold = null!;
    private RadioButton _rbByte = null!;
    private RadioButton _rbChar = null!;
    private Label _lblEncodingTag = null!;
    private Label _lblEncoding = null!;
    private Label _lblPostProcess = null!;
    private CheckBox _chkFixCjk = null!;
    private CheckBox _chkApplyReplace = null!;
    private Button _btnProcess = null!;
    private Label _lblOutput = null!;

    // ===== Tab 2 "文件拼接" 控件 =====
    private Label _lblFolder = null!;
    private TextBox _txtFolder = null!;
    private Button _btnBrowseFolder = null!;
    private Label _lblPattern = null!;
    private TextBox _txtPattern = null!;
    private Label _lblOutputFile = null!;
    private TextBox _txtOutputName = null!;
    private Button _btnJoin = null!;

    // ===== Tab 3 "标点替换" 控件 =====
    private Label _lblReplaceFile = null!;
    private TextBox _txtReplaceFile = null!;
    private Button _btnBrowseReplaceFile = null!;
    private Label _lblReplaceEncodingTag = null!;
    private Label _lblReplaceEncoding = null!;
    private Label _lblRulesHeader = null!;
    private ListBox _lstRules = null!;
    private Label _lblFind = null!;
    private TextBox _txtFind = null!;
    private Label _lblReplaceWith = null!;
    private TextBox _txtReplaceWith = null!;
    private Button _btnAddRule = null!;
    private Button _btnDeleteRule = null!;
    private Button _btnReplaceProcess = null!;
    private Label _lblReplaceOutput = null!;
    private Label _lblHint = null!;

    // ===== Tab 4 "关于" 控件 =====
    private Label _lblAboutName = null!;
    private Label _lblAboutVersion = null!;
    private Label _lblAboutAuthor = null!;
    private Label _lblAboutDesc = null!;

    // ===== 共用 =====
    private StatusStrip _statusStrip = null!;
    private ToolStripStatusLabel _statusLabel = null!;
    private ToolStripComboBox _cmbLanguage = null!;

    private DetectionResult? _lastDetection;
    private DetectionResult? _lastReplaceDetection;
    private List<ReplaceRule> _rules = null!;

    public MainForm()
    {
        Loc.Init();
        InitializeComponent();
        ApplyLocalization();
        Loc.LanguageChanged += () => BeginInvoke(ApplyLocalization);
    }

    private void InitializeComponent()
    {
        // ---- 窗体基本属性 ----
        Text = "TwilightRain的文本工具";
        Size = new Size(700, 540);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 10f);
        AllowDrop = true;

        try
        {
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }
        catch { /* icon 不可用时忽略 */ }

        // ---- 加载标点替换规则 ----
        _rules = PunctuationReplacer.LoadRules();

        // ---- TabControl ----
        _tabControl = new TabControl
        {
            Dock = DockStyle.Fill,
            Padding = new Point(12, 8)
        };

        // ---- Tab 1: 行合并 ----
        _tabMerge = new TabPage("行合并");
        BuildTabMerge(_tabMerge);
        _tabControl.TabPages.Add(_tabMerge);

        // ---- Tab 2: 文件拼接 ----
        _tabJoin = new TabPage("文件拼接");
        BuildTabJoin(_tabJoin);
        _tabControl.TabPages.Add(_tabJoin);

        // ---- Tab 3: 标点替换 ----
        _tabReplace = new TabPage("标点替换");
        BuildTabReplace(_tabReplace);
        _tabControl.TabPages.Add(_tabReplace);

        // ---- Tab 4: 关于 ----
        _tabAbout = new TabPage("关于");
        BuildTabAbout(_tabAbout);
        _tabControl.TabPages.Add(_tabAbout);

        // ---- StatusStrip ----
        _statusStrip = new StatusStrip();
        _statusLabel = new ToolStripStatusLabel("Ready");
        _statusLabel.Spring = true;
        _statusStrip.Items.Add(_statusLabel);

        // 语言选择器
        var lblLang = new ToolStripStatusLabel { Text = "🌐" };
        _cmbLanguage = new ToolStripComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 110
        };
        foreach (var lang in Loc.GetSupportedLanguages())
            _cmbLanguage.Items.Add(lang);
        _cmbLanguage.ComboBox.DisplayMember = "Name";
        _cmbLanguage.ComboBox.ValueMember = "Code";
        _cmbLanguage.SelectedIndexChanged += OnLanguageChanged;
        _statusStrip.Items.Add(lblLang);
        _statusStrip.Items.Add(_cmbLanguage);

        // ---- 组装 ----
        var mainPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        mainPanel.Controls.Add(_tabControl);

        Controls.Add(mainPanel);
        Controls.Add(_statusStrip);
    }

    // ================================================================
    //  语言切换
    // ================================================================
    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (_cmbLanguage.SelectedItem is LanguageInfo lang && lang.Code != Loc.CurrentLang)
            Loc.SetLanguage(lang.Code);
    }

    /// <summary>
    /// 刷新所有 UI 文本（语言切换时调用）
    /// </summary>
    private void ApplyLocalization()
    {
        // 窗体标题
        Text = Loc.T("app_title");

        // Tab 名称
        _tabMerge.Text = Loc.T("tab_merge");
        _tabJoin.Text = Loc.T("tab_join");
        _tabReplace.Text = Loc.T("tab_replace");
        _tabAbout.Text = Loc.T("tab_about");

        // ---- Tab 1: 行合并 ----
        _lblSourceFile.Text = Loc.T("label_source_file");
        _btnBrowse.Text = Loc.T("btn_browse");
        _lblThreshold.Text = Loc.T("label_threshold");
        _rbByte.Text = Loc.T("radio_byte");
        _rbChar.Text = Loc.T("radio_char");
        _lblEncodingTag.Text = Loc.T("label_encoding");
        if (_lastDetection == null) _lblEncoding.Text = Loc.T("encoding_not_selected");
        _lblPostProcess.Text = Loc.T("label_post_process");
        _chkFixCjk.Text = Loc.T("chk_fix_cjk");
        _chkApplyReplace.Text = Loc.T("chk_apply_replace");
        _btnProcess.Text = Loc.T("btn_process");

        // ---- Tab 2: 文件拼接 ----
        _lblFolder.Text = Loc.T("label_folder");
        _lblPattern.Text = Loc.T("label_pattern");
        _lblOutputFile.Text = Loc.T("label_output_file");
        _btnJoin.Text = Loc.T("btn_join");

        // ---- Tab 3: 标点替换 ----
        _lblReplaceFile.Text = Loc.T("label_source_file");
        _btnBrowseReplaceFile.Text = Loc.T("btn_browse");
        _lblReplaceEncodingTag.Text = Loc.T("label_encoding");
        if (_lastReplaceDetection == null) _lblReplaceEncoding.Text = Loc.T("encoding_not_selected");
        _lblRulesHeader.Text = Loc.T("label_rules");
        _lblFind.Text = Loc.T("label_find");
        _lblReplaceWith.Text = Loc.T("label_replace_with");
        _btnAddRule.Text = Loc.T("btn_add_update");
        _btnDeleteRule.Text = Loc.T("btn_delete");
        _lblHint.Text = Loc.T("hint_rule_edit");
        _btnReplaceProcess.Text = Loc.T("btn_execute_replace");

        // ---- Tab 4: 关于 ----
        _lblAboutName.Text = Loc.T("about_title");
        var asmVer = typeof(MainForm).Assembly.GetName().Version;
        _lblAboutVersion.Text = Loc.T("about_version", asmVer != null ? $"{asmVer.Major}.{asmVer.Minor}.{asmVer.Build}" : "1.4.0");
        _lblAboutAuthor.Text = Loc.T("about_author");
        _lblAboutDesc.Text = Loc.T("about_description");

        // ---- 状态栏 ----
        _statusLabel.Text = Loc.T("status_ready");

        // ---- 语言下拉框 ----
        for (int i = 0; i < _cmbLanguage.Items.Count; i++)
        {
            if (_cmbLanguage.Items[i] is LanguageInfo li && li.Code == Loc.CurrentLang)
            {
                _cmbLanguage.SelectedIndex = i;
                break;
            }
        }
    }

    // ================================================================
    //  Tab 1: 行合并
    // ================================================================
    private void BuildTabMerge(TabPage tab)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 7,
            Padding = new Padding(16, 16, 16, 8)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        // Row 0 — 源文件
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        _lblSourceFile = MakeLabel("源文件：");
        layout.Controls.Add(_lblSourceFile, 0, 0);
        _txtFilePath = new TextBox
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            ReadOnly = true,
            BackColor = Color.White,
            AllowDrop = true
        };
        _txtFilePath.DragEnter += OnFileDragEnter;
        _txtFilePath.DragDrop += OnFileDragDrop;
        layout.Controls.Add(_txtFilePath, 1, 0);

        _btnBrowse = new Button { Text = "浏览...", AutoSize = true };
        _btnBrowse.Click += OnBrowseFile;
        layout.Controls.Add(_btnBrowse, 2, 0);

        // Row 1 — 阈值 + 模式
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        _lblThreshold = MakeLabel("阈值：");
        layout.Controls.Add(_lblThreshold, 0, 1);

        var thresholdPanel = new FlowLayoutPanel
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        _numThreshold = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 1000,
            Value = 20,
            Width = 60
        };
        thresholdPanel.Controls.Add(_numThreshold);

        _rbByte = new RadioButton
        {
            Text = "字节数",
            Checked = true,
            AutoSize = true,
            Margin = new Padding(16, 0, 0, 0)
        };
        _rbChar = new RadioButton
        {
            Text = "字符数",
            AutoSize = true,
            Margin = new Padding(8, 0, 0, 0)
        };
        thresholdPanel.Controls.Add(_rbByte);
        thresholdPanel.Controls.Add(_rbChar);
        layout.Controls.Add(thresholdPanel, 1, 1);

        // Row 2 — 编码检测
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        _lblEncodingTag = MakeLabel("检测编码：");
        layout.Controls.Add(_lblEncodingTag, 0, 2);
        _lblEncoding = new Label
        {
            Text = "（尚未选择文件）",
            ForeColor = Color.Gray,
            Anchor = AnchorStyles.Left
        };
        layout.Controls.Add(_lblEncoding, 1, 2);

        // Row 3 — 中文截断修复
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        _lblPostProcess = MakeLabel("后处理：");
        layout.Controls.Add(_lblPostProcess, 0, 3);
        _chkFixCjk = new CheckBox
        {
            Text = "修复中文截断断段",
            Checked = true,
            AutoSize = true,
            Anchor = AnchorStyles.Left
        };
        layout.SetColumnSpan(_chkFixCjk, 2);
        layout.Controls.Add(_chkFixCjk, 1, 3);

        // Row 4 — 标点替换
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        _chkApplyReplace = new CheckBox
        {
            Text = "应用标点替换规则",
            Checked = false,
            AutoSize = true,
            Anchor = AnchorStyles.Left
        };
        layout.SetColumnSpan(_chkApplyReplace, 2);
        layout.Controls.Add(_chkApplyReplace, 1, 4);

        // Row 5 — 处理按钮
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        _btnProcess = new Button
        {
            Text = "▶  处理",
            Enabled = false,
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold),
            BackColor = Color.SteelBlue,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Padding = new Padding(24, 6, 24, 6)
        };
        _btnProcess.FlatAppearance.BorderSize = 0;
        _btnProcess.Click += OnProcess;
        layout.Controls.Add(_btnProcess, 1, 5);

        // Row 6 — 输出提示
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        _lblOutput = new Label
        {
            Text = "",
            ForeColor = Color.Green,
            Anchor = AnchorStyles.Left
        };
        layout.SetColumnSpan(_lblOutput, 2);
        layout.Controls.Add(_lblOutput, 1, 6);

        tab.Controls.Add(layout);
    }

    // ================================================================
    //  Tab 2: 文件拼接
    // ================================================================
    private void BuildTabJoin(TabPage tab)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 5,
            Padding = new Padding(16, 16, 16, 8)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        _lblFolder = MakeLabel("目录：");
        layout.Controls.Add(_lblFolder, 0, 0);
        _txtFolder = new TextBox
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Text = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
        };
        layout.Controls.Add(_txtFolder, 1, 0);
        _btnBrowseFolder = new Button { Text = "浏览...", AutoSize = true };
        _btnBrowseFolder.Click += OnBrowseFolder;
        layout.Controls.Add(_btnBrowseFolder, 2, 0);

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        _lblPattern = MakeLabel("文件匹配：");
        layout.Controls.Add(_lblPattern, 0, 1);
        _txtPattern = new TextBox { Text = "*.txt", Anchor = AnchorStyles.Left | AnchorStyles.Right };
        layout.Controls.Add(_txtPattern, 1, 1);

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        _lblOutputFile = MakeLabel("输出文件：");
        layout.Controls.Add(_lblOutputFile, 0, 2);
        _txtOutputName = new TextBox { Text = "combined.txt", Anchor = AnchorStyles.Left | AnchorStyles.Right };
        layout.Controls.Add(_txtOutputName, 1, 2);

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        _btnJoin = new Button
        {
            Text = "▶  拼接",
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold),
            BackColor = Color.SteelBlue,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Padding = new Padding(24, 6, 24, 6)
        };
        _btnJoin.FlatAppearance.BorderSize = 0;
        _btnJoin.Click += OnJoin;
        layout.Controls.Add(_btnJoin, 1, 3);

        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tab.Controls.Add(layout);
    }

    // ================================================================
    //  Tab 3: 标点替换
    // ================================================================
    private void BuildTabReplace(TabPage tab)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 6,
            Padding = new Padding(16, 16, 16, 8)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        // Row 0 — 文件选择
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        _lblReplaceFile = MakeLabel("源文件：");
        layout.Controls.Add(_lblReplaceFile, 0, 0);
        _txtReplaceFile = new TextBox
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            ReadOnly = true,
            BackColor = Color.White,
            AllowDrop = true
        };
        _txtReplaceFile.DragEnter += OnFileDragEnter;
        _txtReplaceFile.DragDrop += OnReplaceFileDragDrop;
        layout.Controls.Add(_txtReplaceFile, 1, 0);

        _btnBrowseReplaceFile = new Button { Text = "浏览...", AutoSize = true };
        _btnBrowseReplaceFile.Click += OnBrowseReplaceFile;
        layout.Controls.Add(_btnBrowseReplaceFile, 2, 0);

        // Row 1 — 编码提示
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        _lblReplaceEncodingTag = MakeLabel("检测编码：");
        layout.Controls.Add(_lblReplaceEncodingTag, 0, 1);
        _lblReplaceEncoding = new Label
        {
            Text = "（尚未选择文件）",
            ForeColor = Color.Gray,
            Anchor = AnchorStyles.Left
        };
        layout.SetColumnSpan(_lblReplaceEncoding, 2);
        layout.Controls.Add(_lblReplaceEncoding, 1, 1);

        // Row 2 — 规则列表 + 编辑区
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var ruleSplit = new SplitContainer
        {
            Orientation = Orientation.Vertical,
            SplitterDistance = 220,
            FixedPanel = FixedPanel.Panel1,
            Dock = DockStyle.Fill
        };

        // 左侧：规则列表
        var leftPanel = new Panel { Dock = DockStyle.Fill };
        _lblRulesHeader = new Label { Text = "替换规则：", AutoSize = true, Dock = DockStyle.Top };
        _lstRules = new ListBox
        {
            Dock = DockStyle.Fill,
            IntegralHeight = false
        };
        _lstRules.SelectedIndexChanged += OnRuleSelected;
        leftPanel.Controls.Add(_lstRules);
        leftPanel.Controls.Add(_lblRulesHeader);

        // 右侧：规则编辑器
        var rightPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            Padding = new Padding(8, 0, 0, 0)
        };
        rightPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        rightPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        _lblFind = MakeLabel("查找：");
        rightPanel.Controls.Add(_lblFind, 0, 0);
        _txtFind = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right, Width = 150 };
        rightPanel.Controls.Add(_txtFind, 1, 0);

        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        _lblReplaceWith = MakeLabel("替换为：");
        rightPanel.Controls.Add(_lblReplaceWith, 0, 1);
        _txtReplaceWith = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right, Width = 150 };
        rightPanel.Controls.Add(_txtReplaceWith, 1, 1);

        rightPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var btnPanel = new FlowLayoutPanel
        {
            Anchor = AnchorStyles.Left,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true
        };
        _btnAddRule = new Button { Text = "➕ 添加/更新", AutoSize = true, BackColor = Color.LightGreen, FlatStyle = FlatStyle.Flat };
        _btnAddRule.FlatAppearance.BorderSize = 0;
        _btnAddRule.Click += OnAddOrUpdateRule;
        _btnDeleteRule = new Button { Text = "🗑 删除所选", AutoSize = true, BackColor = Color.LightCoral, FlatStyle = FlatStyle.Flat, Margin = new Padding(8, 0, 0, 0) };
        _btnDeleteRule.FlatAppearance.BorderSize = 0;
        _btnDeleteRule.Click += OnDeleteRule;
        btnPanel.Controls.Add(_btnAddRule);
        btnPanel.Controls.Add(_btnDeleteRule);
        rightPanel.SetColumnSpan(btnPanel, 2);
        rightPanel.Controls.Add(btnPanel, 0, 2);

        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        _lblHint = new Label
        {
            Text = "提示：选择列表中规则可编辑",
            ForeColor = Color.Gray,
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 8f)
        };
        rightPanel.SetColumnSpan(_lblHint, 2);
        rightPanel.Controls.Add(_lblHint, 0, 3);

        ruleSplit.Panel1.Controls.Add(leftPanel);
        ruleSplit.Panel2.Controls.Add(rightPanel);
        layout.SetColumnSpan(ruleSplit, 3);
        layout.Controls.Add(ruleSplit, 0, 2);

        // Row 3 — 处理按钮
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        _btnReplaceProcess = new Button
        {
            Text = "▶  执行替换",
            Enabled = false,
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold),
            BackColor = Color.SteelBlue,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Padding = new Padding(24, 6, 24, 6)
        };
        _btnReplaceProcess.FlatAppearance.BorderSize = 0;
        _btnReplaceProcess.Click += OnReplaceProcess;
        layout.Controls.Add(_btnReplaceProcess, 1, 3);

        // Row 4 — 输出
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        _lblReplaceOutput = new Label { Text = "", ForeColor = Color.Green, Anchor = AnchorStyles.Left };
        layout.SetColumnSpan(_lblReplaceOutput, 2);
        layout.Controls.Add(_lblReplaceOutput, 1, 4);

        // 加载已有规则到列表
        RefreshRuleList();
        tab.Controls.Add(layout);
    }

    // ================================================================
    //  Tab 4: 关于
    // ================================================================
    private void BuildTabAbout(TabPage tab)
    {
        tab.BackColor = Color.White;

        var mainPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 1
        };
        mainPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        // 居中内容面板
        var centerPanel = new TableLayoutPanel
        {
            AutoSize = true,
            Anchor = AnchorStyles.None,
            ColumnCount = 1,
            RowCount = 6
        };
        centerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        // Row 0 — 图标
        var iconBox = new PictureBox
        {
            Size = new Size(64, 64),
            SizeMode = PictureBoxSizeMode.Zoom,
            Anchor = AnchorStyles.None,
            Margin = new Padding(0, 0, 0, 12)
        };
        try
        {
            iconBox.Image = Icon.ExtractAssociatedIcon(Application.ExecutablePath)?.ToBitmap();
        }
        catch { }
        centerPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        centerPanel.Controls.Add(iconBox, 0, 0);

        // Row 1 — 程序名称
        _lblAboutName = new Label
        {
            Text = "TwilightRain的文本工具",
            Font = new Font("Microsoft YaHei UI", 16f, FontStyle.Bold),
            AutoSize = true,
            Anchor = AnchorStyles.None,
            Margin = new Padding(0, 0, 0, 4)
        };
        centerPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        centerPanel.Controls.Add(_lblAboutName, 0, 1);

        // Row 2 — 版本号
        _lblAboutVersion = new Label
        {
            Text = "版本 1.4.0",
            Font = new Font("Microsoft YaHei UI", 10f),
            ForeColor = Color.Gray,
            AutoSize = true,
            Anchor = AnchorStyles.None,
            Margin = new Padding(0, 0, 0, 4)
        };
        centerPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        centerPanel.Controls.Add(_lblAboutVersion, 0, 2);

        // Row 3 — 作者
        _lblAboutAuthor = new Label
        {
            Text = "作者：TwilightRain",
            Font = new Font("Microsoft YaHei UI", 10f),
            AutoSize = true,
            Anchor = AnchorStyles.None,
            Margin = new Padding(0, 0, 0, 4)
        };
        centerPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        centerPanel.Controls.Add(_lblAboutAuthor, 0, 3);

        // Row 4 — 网址（可点击）
        var lblUrl = new LinkLabel
        {
            Text = "https://github.com/TwilightRainDev",
            Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Underline),
            LinkColor = Color.SteelBlue,
            ActiveLinkColor = Color.DarkBlue,
            AutoSize = true,
            Anchor = AnchorStyles.None,
            Margin = new Padding(0, 0, 0, 12),
            Tag = "https://github.com/TwilightRainDev"
        };
        lblUrl.LinkClicked += (_, e) =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = lblUrl.Tag?.ToString() ?? "https://github.com/TwilightRainDev",
                    UseShellExecute = true
                });
            }
            catch { }
        };
        centerPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        centerPanel.Controls.Add(lblUrl, 0, 4);

        // Row 5 — 说明
        _lblAboutDesc = new Label
        {
            Text = "集行合并、文件拼接、中文截断修复、标点替换\n于一体的文本处理工具。",
            Font = new Font("Microsoft YaHei UI", 9f),
            ForeColor = Color.DimGray,
            AutoSize = true,
            Anchor = AnchorStyles.None,
            TextAlign = ContentAlignment.MiddleCenter
        };
        centerPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        centerPanel.Controls.Add(_lblAboutDesc, 0, 5);

        mainPanel.Controls.Add(centerPanel, 0, 0);
        tab.Controls.Add(mainPanel);
    }

    // ================================================================
    //  拖放支持
    // ================================================================
    private void OnFileDragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
            e.Effect = DragDropEffects.Copy;
        else
            e.Effect = DragDropEffects.None;
    }

    private void OnFileDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            SelectFile(files[0]);
    }

    private void OnReplaceFileDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            SelectReplaceFile(files[0]);
    }

    private void SelectFile(string path)
    {
        if (!File.Exists(path)) { ShowError(Loc.T("msg_file_not_found", path)); return; }
        _txtFilePath.Text = path;
        try
        {
            _lastDetection = EncodingDetector.Detect(path);
            _lblEncoding.Text = _lastDetection.DisplayName;
            _lblEncoding.ForeColor = SystemColors.ControlText;
            _btnProcess.Enabled = true;
            SetStatus(Loc.T("status_selected", Path.GetFileName(path), _lastDetection.DisplayName));
        }
        catch (Exception ex) { ShowError(Loc.T("msg_read_failed", ex.Message)); }
    }

    private void SelectReplaceFile(string path)
    {
        if (!File.Exists(path)) { ShowError(Loc.T("msg_file_not_found", path)); return; }
        _txtReplaceFile.Text = path;
        try
        {
            _lastReplaceDetection = EncodingDetector.Detect(path);
            _lblReplaceEncoding.Text = _lastReplaceDetection.DisplayName;
            _lblReplaceEncoding.ForeColor = SystemColors.ControlText;
            _btnReplaceProcess.Enabled = true;
        }
        catch (Exception ex) { ShowError(Loc.T("msg_read_failed", ex.Message)); }
    }

    // ================================================================
    //  行合并 Tab 按钮事件
    // ================================================================
    private void OnBrowseFile(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Title = Loc.T("tab_merge"),
            Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
            RestoreDirectory = true
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
            SelectFile(dlg.FileName);
    }

    private void OnProcess(object? sender, EventArgs e)
    {
        if (_lastDetection == null || string.IsNullOrWhiteSpace(_txtFilePath.Text)) return;

        try
        {
            _btnProcess.Enabled = false;
            _btnProcess.Text = Loc.T("status_processing");
            SetStatus(Loc.T("status_processing"));

            // Step 1: 行合并
            var options = new MergeOptions
            {
                Threshold = (int)_numThreshold.Value,
                Mode = _rbChar.Checked ? MergeMode.CharCount : MergeMode.ByteCount
            };
            string outputPath = LineMerger.Merge(_txtFilePath.Text, options, _lastDetection.Encoding);

            // Step 2: 中文截断修复
            bool appliedCjk = false;
            if (_chkFixCjk.Checked)
            {
                var lines = File.ReadAllLines(outputPath, Encoding.UTF8).ToList();
                var fixed_ = CjkParagraphMerger.Fix(lines);
                if (fixed_.Count != lines.Count)
                {
                    File.WriteAllLines(outputPath, fixed_, new UTF8Encoding(true));
                    appliedCjk = true;
                }
            }

            // Step 3: 标点替换
            bool appliedReplace = false;
            if (_chkApplyReplace.Checked && _rules.Count > 0)
            {
                PunctuationReplacer.ApplyToFile(outputPath, Encoding.UTF8, _rules);
                appliedReplace = true;
            }

            // 汇总
            var extras = new List<string>();
            if (appliedCjk) extras.Add(Loc.T("extra_cjk"));
            if (appliedReplace) extras.Add(Loc.T("extra_replace"));
            string extraMsg = extras.Count > 0
                ? $"（{Loc.T("label_post_process").TrimEnd('：', ':')}：{string.Join("、", extras)}）"
                : "";

            _lblOutput.Text = Loc.T("label_output", outputPath);
            _lblOutput.ForeColor = Color.Green;
            SetStatus(Loc.T("status_complete", outputPath, extraMsg));

            if (MessageBox.Show(this,
                Loc.T("msg_process_body", extraMsg, outputPath),
                Loc.T("msg_process_title"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
            {
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{outputPath}\"");
            }
        }
        catch (Exception ex) { ShowError(Loc.T("msg_process_failed", ex.Message)); }
        finally { _btnProcess.Enabled = true; _btnProcess.Text = Loc.T("btn_process"); }
    }

    // ================================================================
    //  文件拼接 Tab 按钮事件
    // ================================================================
    private void OnBrowseFolder(object? sender, EventArgs e)
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = Loc.T("tab_join"),
            UseDescriptionForTitle = true,
            InitialDirectory = _txtFolder.Text
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
            _txtFolder.Text = dlg.SelectedPath;
    }

    private void OnJoin(object? sender, EventArgs e)
    {
        string folder = _txtFolder.Text.Trim();
        string pattern = _txtPattern.Text.Trim();
        string outputName = _txtOutputName.Text.Trim();
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) { ShowError(Loc.T("msg_invalid_folder")); return; }
        if (string.IsNullOrEmpty(pattern)) { ShowError(Loc.T("msg_empty_pattern")); return; }
        if (string.IsNullOrEmpty(outputName)) { ShowError(Loc.T("msg_empty_output")); return; }

        try
        {
            _btnJoin.Enabled = false; _btnJoin.Text = Loc.T("status_joining");
            SetStatus(Loc.T("status_joining"));
            string outputPath = FileJoiner.Join(folder, pattern, outputName);
            SetStatus(Loc.T("status_join_complete", Directory.GetFiles(folder, pattern).Length, outputPath));

            if (MessageBox.Show(this, Loc.T("msg_join_body", outputPath),
                Loc.T("msg_join_title"),
                MessageBoxButtons.OK, MessageBoxIcon.Information) == DialogResult.OK)
            {
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{outputPath}\"");
            }
        }
        catch (Exception ex) { ShowError(Loc.T("msg_join_failed", ex.Message)); }
        finally { _btnJoin.Enabled = true; _btnJoin.Text = Loc.T("btn_join"); }
    }

    // ================================================================
    //  标点替换 Tab 按钮事件
    // ================================================================
    private void OnBrowseReplaceFile(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Title = Loc.T("tab_replace"),
            Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
            RestoreDirectory = true
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
            SelectReplaceFile(dlg.FileName);
    }

    private void OnRuleSelected(object? sender, EventArgs e)
    {
        if (_lstRules.SelectedIndex >= 0 && _lstRules.SelectedIndex < _rules.Count)
        {
            var rule = _rules[_lstRules.SelectedIndex];
            _txtFind.Text = rule.Find;
            _txtReplaceWith.Text = rule.Replace;
        }
    }

    private void OnAddOrUpdateRule(object? sender, EventArgs e)
    {
        string find = _txtFind.Text;
        string replace = _txtReplaceWith.Text;
        if (string.IsNullOrEmpty(find)) { ShowError(Loc.T("msg_enter_find")); return; }

        if (_lstRules.SelectedIndex >= 0 && _lstRules.SelectedIndex < _rules.Count)
        {
            _rules[_lstRules.SelectedIndex] = new ReplaceRule { Find = find, Replace = replace };
        }
        else
        {
            _rules.Add(new ReplaceRule { Find = find, Replace = replace });
        }

        PunctuationReplacer.SaveRules(_rules);
        RefreshRuleList();
        _txtFind.Clear();
        _txtReplaceWith.Clear();
        SetStatus(Loc.T("status_rules_saved", _rules.Count));
    }

    private void OnDeleteRule(object? sender, EventArgs e)
    {
        if (_lstRules.SelectedIndex < 0 || _lstRules.SelectedIndex >= _rules.Count) return;

        _rules.RemoveAt(_lstRules.SelectedIndex);
        PunctuationReplacer.SaveRules(_rules);
        RefreshRuleList();
        _txtFind.Clear();
        _txtReplaceWith.Clear();
        SetStatus(Loc.T("status_rule_deleted", _rules.Count));
    }

    private void OnReplaceProcess(object? sender, EventArgs e)
    {
        if (_lastReplaceDetection == null || string.IsNullOrWhiteSpace(_txtReplaceFile.Text)) return;
        if (_rules.Count == 0) { ShowError(Loc.T("msg_add_rule")); return; }

        try
        {
            _btnReplaceProcess.Enabled = false;
            _btnReplaceProcess.Text = Loc.T("status_replacing");
            SetStatus(Loc.T("status_replacing"));

            string dir = Path.GetDirectoryName(_txtReplaceFile.Text) ?? ".";
            string name = Path.GetFileNameWithoutExtension(_txtReplaceFile.Text);
            string ext = Path.GetExtension(_txtReplaceFile.Text);
            string outputPath = Path.Combine(dir, $"{name}_Processed{ext}");

            string content = File.ReadAllText(_txtReplaceFile.Text, _lastReplaceDetection.Encoding);
            string replaced = PunctuationReplacer.Apply(content, _rules);
            File.WriteAllText(outputPath, replaced, new UTF8Encoding(true));

            _lblReplaceOutput.Text = Loc.T("label_output", outputPath);
            _lblReplaceOutput.ForeColor = Color.Green;
            SetStatus(Loc.T("status_replace_complete", outputPath));

            if (MessageBox.Show(this, Loc.T("msg_replace_body", outputPath),
                Loc.T("msg_replace_title"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
            {
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{outputPath}\"");
            }
        }
        catch (Exception ex) { ShowError(Loc.T("msg_replace_failed", ex.Message)); }
        finally { _btnReplaceProcess.Enabled = true; _btnReplaceProcess.Text = Loc.T("btn_execute_replace"); }
    }

    // ================================================================
    //  规则列表刷新
    // ================================================================
    private void RefreshRuleList()
    {
        _lstRules.Items.Clear();
        foreach (var rule in _rules)
            _lstRules.Items.Add($"{rule.Find}  →  {rule.Replace}");
    }

    // ================================================================
    //  辅助方法
    // ================================================================
    private static Label MakeLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        TextAlign = ContentAlignment.MiddleRight,
        Anchor = AnchorStyles.Right
    };

    private void SetStatus(string message) => _statusLabel.Text = message;

    private void ShowError(string message)
    {
        _statusLabel.Text = $"❌ {message}";
        MessageBox.Show(this, message, Loc.T("msg_error_title"),
            MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
