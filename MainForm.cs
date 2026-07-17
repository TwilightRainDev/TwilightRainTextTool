using System.Text;
using TextTool.Localization;
using TextTool.Services;

namespace TextTool;

/// <summary>
/// TwilightRain's Text Tool v1.5.0
/// - Line merge (threshold + CJK truncation fix + punctuation replacement)
/// - File join
/// - Punctuation replacement rule management
/// - About page with avatar, version info, language selector
/// - i18n (zh_CN / zh_TW / en_US)
/// </summary>
public sealed class MainForm : Form
{
    // ===== Tab control =====
    private TabControl _tabControl = null!;
    private TabPage _tabMerge = null!;
    private TabPage _tabJoin = null!;
    private TabPage _tabReplace = null!;
    private TabPage _tabAbout = null!;

    // ===== Tab 1 "Line Merge" controls =====
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
    private CheckBox _chkFixPunct = null!;
    private CheckBox _chkApplyReplace = null!;
    private CheckBox _chkNoMerge = null!;
    private Button _btnProcess = null!;
    private Label _lblOutput = null!;
    private Label _lblPunctChars = null!;
    private TextBox _txtPunctChars = null!;
    private Label _lblNoMergeChars = null!;
    private TextBox _txtNoMergeChars = null!;

    // ===== Tab 2 "File Join" controls =====
    private Label _lblFolder = null!;
    private TextBox _txtFolder = null!;
    private Button _btnBrowseFolder = null!;
    private Label _lblPattern = null!;
    private TextBox _txtPattern = null!;
    private Label _lblOutputFile = null!;
    private TextBox _txtOutputName = null!;
    private Button _btnJoin = null!;

    // ===== Tab 3 "Punctuation Replace" controls =====
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
    private Button _btnUp = null!;
    private Button _btnDown = null!;
    private Button _btnReplaceProcess = null!;
    private Label _lblReplaceOutput = null!;
    private Label _lblHint = null!;

    // ===== Tab 4 "About" controls =====
    private Label _lblAboutName = null!;
    private Label _lblAboutVersion = null!;
    private PictureBox _pbAvatar = null!;
    private Label _lblAboutAuthor = null!;
    private Label _lblAboutDesc = null!;
    private Label _lblAboutLanguage = null!;
    private ComboBox _cmbAboutLanguage = null!;

    // ===== Shared =====
    private StatusStrip _statusStrip = null!;
    private ToolStripStatusLabel _statusLabel = null!;

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
        // ---- Form basic properties ----
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

        // ---- Load punctuation replacement rules ----
        _rules = ReplaceRuleStore.Load();

        // ---- TabControl ----
        _tabControl = new TabControl
        {
            Dock = DockStyle.Fill,
            Padding = new Point(12, 8)
        };

        // ---- Tab 1: Line Merge ----
        _tabMerge = new TabPage("Line Merge");
        BuildTabMerge(_tabMerge);
        _tabControl.TabPages.Add(_tabMerge);

        // ---- Tab 2: File Join ----
        _tabJoin = new TabPage("File Join");
        BuildTabJoin(_tabJoin);
        _tabControl.TabPages.Add(_tabJoin);

        // ---- Tab 3: Punctuation Replace ----
        _tabReplace = new TabPage("Punct. Replace");
        BuildTabReplace(_tabReplace);
        _tabControl.TabPages.Add(_tabReplace);

        // ---- Tab 4: About ----
        _tabAbout = new TabPage("About");
        BuildTabAbout(_tabAbout);
        _tabControl.TabPages.Add(_tabAbout);

        // ---- StatusStrip (language selector moved to About page) ----
        _statusStrip = new StatusStrip();
        _statusLabel = new ToolStripStatusLabel("Ready");
        _statusLabel.Spring = true;
        _statusStrip.Items.Add(_statusLabel);

        // ---- Assemble ----
        var mainPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        mainPanel.Controls.Add(_tabControl);

        Controls.Add(mainPanel);
        Controls.Add(_statusStrip);
    }

    // ================================================================
    //  Localization
    // ================================================================
    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (_cmbAboutLanguage.SelectedItem is LanguageInfo lang && lang.Code != Loc.CurrentLang)
            Loc.SetLanguage(lang.Code);
    }

    private void ApplyLocalization()
    {
        Text = Loc.T("AppTitle");

        _tabMerge.Text = Loc.T("TabMerge");
        _tabJoin.Text = Loc.T("TabJoin");
        _tabReplace.Text = Loc.T("TabReplace");
        _tabAbout.Text = Loc.T("TabAbout");

        _lblSourceFile.Text = Loc.T("LabelSourceFile");
        _btnBrowse.Text = Loc.T("BtnBrowse");
        _lblThreshold.Text = Loc.T("LabelThreshold");
        _rbByte.Text = Loc.T("RadioByte");
        _rbChar.Text = Loc.T("RadioChar");
        _lblEncodingTag.Text = Loc.T("LabelEncoding");
        if (_lastDetection == null) _lblEncoding.Text = Loc.T("EncodingNotSelected");
        _lblPostProcess.Text = Loc.T("LabelPostProcess");
        _chkFixCjk.Text = Loc.T("ChkFixCjk");
        _chkFixPunct.Text = Loc.T("ChkFixPunct");
        _lblPunctChars.Text = Loc.T("LabelPunctChars");
        _chkApplyReplace.Text = Loc.T("ChkApplyReplace");
        _chkNoMerge.Text = Loc.T("ChkNoMerge");
        _lblNoMergeChars.Text = Loc.T("LabelNoMergeChars");
        _btnProcess.Text = Loc.T("BtnProcess");

        _lblFolder.Text = Loc.T("LabelFolder");
        _lblPattern.Text = Loc.T("LabelPattern");
        _lblOutputFile.Text = Loc.T("LabelOutputFile");
        _btnJoin.Text = Loc.T("BtnJoin");

        _lblReplaceFile.Text = Loc.T("LabelSourceFile");
        _btnBrowseReplaceFile.Text = Loc.T("BtnBrowse");
        _lblReplaceEncodingTag.Text = Loc.T("LabelEncoding");
        if (_lastReplaceDetection == null) _lblReplaceEncoding.Text = Loc.T("EncodingNotSelected");
        _lblRulesHeader.Text = Loc.T("LabelRules");
        _lblFind.Text = Loc.T("LabelFind");
        _lblReplaceWith.Text = Loc.T("LabelReplaceWith");
        _btnAddRule.Text = Loc.T("BtnAddUpdate");
        _btnDeleteRule.Text = Loc.T("BtnDelete");
        _btnUp.Text = Loc.T("BtnUp");
        _btnDown.Text = Loc.T("BtnDown");
        _lblHint.Text = Loc.T("HintRuleEdit");
        _btnReplaceProcess.Text = Loc.T("BtnExecuteReplace");

        _lblAboutName.Text = Loc.T("AboutTitle");
        var asmVer = typeof(MainForm).Assembly.GetName().Version;
        _lblAboutVersion.Text = Loc.T("AboutVersion",
            asmVer != null ? $"{asmVer.Major}.{asmVer.Minor}.{asmVer.Build}" : "1.5.0");
        _lblAboutAuthor.Text = Loc.T("AboutAuthor");
        _lblAboutDesc.Text = Loc.T("AboutDescription");
        _lblAboutLanguage.Text = Loc.T("AboutLanguage");

        RebuildLanguageCombo();
        _statusLabel.Text = Loc.T("StatusReady");
    }

    private void RebuildLanguageCombo()
    {
        _cmbAboutLanguage.Items.Clear();
        foreach (var lang in Loc.GetSupportedLanguages())
            _cmbAboutLanguage.Items.Add(lang);
        _cmbAboutLanguage.DisplayMember = "Name";
        _cmbAboutLanguage.ValueMember = "Code";

        for (int i = 0; i < _cmbAboutLanguage.Items.Count; i++)
        {
            if (_cmbAboutLanguage.Items[i] is LanguageInfo li && li.Code == Loc.CurrentLang)
            {
                _cmbAboutLanguage.SelectedIndex = i;
                break;
            }
        }
    }

    // ================================================================
    //  Tab 1: Line Merge
    // ================================================================
    private void BuildTabMerge(TabPage tab)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 9,
            Padding = new Padding(16, 16, 16, 8)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        // Row 0 — Source file
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        _lblSourceFile = MakeLabel("Source File:");
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

        _btnBrowse = new Button { Text = "Browse...", AutoSize = true };
        _btnBrowse.Click += OnBrowseFile;
        layout.Controls.Add(_btnBrowse, 2, 0);

        // Row 1 — Threshold
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        _lblThreshold = MakeLabel("Threshold:");
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
            Text = "By Bytes",
            Checked = true,
            AutoSize = true,
            Margin = new Padding(16, 0, 0, 0)
        };
        _rbChar = new RadioButton
        {
            Text = "By Chars",
            AutoSize = true,
            Margin = new Padding(8, 0, 0, 0)
        };
        thresholdPanel.Controls.Add(_rbByte);
        thresholdPanel.Controls.Add(_rbChar);
        layout.Controls.Add(thresholdPanel, 1, 1);

        // Row 2 — Encoding
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        _lblEncodingTag = MakeLabel("Encoding:");
        layout.Controls.Add(_lblEncodingTag, 0, 2);
        _lblEncoding = new Label
        {
            Text = "(No file selected)",
            ForeColor = Color.Gray,
            Anchor = AnchorStyles.Left
        };
        layout.Controls.Add(_lblEncoding, 1, 2);

        // Row 3 — Post: Fix CJK truncation
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        _lblPostProcess = MakeLabel("Post:");
        layout.Controls.Add(_lblPostProcess, 0, 3);
        _chkFixCjk = new CheckBox
        {
            Text = "Fix CJK truncation",
            Checked = true,
            AutoSize = true,
            Anchor = AnchorStyles.Left
        };
        layout.SetColumnSpan(_chkFixCjk, 2);
        layout.Controls.Add(_chkFixCjk, 1, 3);

        // Row 4 — Fix punct. truncation + custom punct textbox
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        _chkFixPunct = new CheckBox
        {
            Text = "Fix punct. truncation",
            Checked = false,
            AutoSize = true,
            Anchor = AnchorStyles.Left
        };
        _lblPunctChars = new Label
        {
            Text = "Custom punct.:",
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(8, 0, 4, 0)
        };
        _txtPunctChars = new TextBox
        {
            Text = "，",
            Width = 100,
            Anchor = AnchorStyles.Left
        };
        var punctFixRow = new FlowLayoutPanel
        {
            Anchor = AnchorStyles.Left,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true
        };
        punctFixRow.Controls.Add(_chkFixPunct);
        punctFixRow.Controls.Add(_lblPunctChars);
        punctFixRow.Controls.Add(_txtPunctChars);
        layout.SetColumnSpan(punctFixRow, 2);
        layout.Controls.Add(punctFixRow, 1, 4);

        // Row 5 — Apply punct. replace rules
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        _chkApplyReplace = new CheckBox
        {
            Text = "Apply punct. replace rules",
            Checked = false,
            AutoSize = true,
            Anchor = AnchorStyles.Left
        };
        layout.SetColumnSpan(_chkApplyReplace, 2);
        layout.Controls.Add(_chkApplyReplace, 1, 5);

        // Row 6 — Line-ending punct. no-merge + custom punct textbox
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        _chkNoMerge = new CheckBox
        {
            Text = "Line-ending punct. no-merge",
            Checked = false,
            AutoSize = true,
            Anchor = AnchorStyles.Left
        };
        _lblNoMergeChars = new Label
        {
            Text = "Custom punct.:",
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(8, 0, 4, 0)
        };
        _txtNoMergeChars = new TextBox
        {
            Text = "。！？",
            Width = 100,
            Anchor = AnchorStyles.Left
        };
        var noMergeRow = new FlowLayoutPanel
        {
            Anchor = AnchorStyles.Left,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true
        };
        noMergeRow.Controls.Add(_chkNoMerge);
        noMergeRow.Controls.Add(_lblNoMergeChars);
        noMergeRow.Controls.Add(_txtNoMergeChars);
        layout.SetColumnSpan(noMergeRow, 2);
        layout.Controls.Add(noMergeRow, 1, 6);

        // Row 7 — Process button
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        _btnProcess = MakePrimaryButton("Process");
        _btnProcess.Enabled = false;
        _btnProcess.Click += OnProcess;
        layout.Controls.Add(_btnProcess, 1, 7);

        // Row 8 — Output
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        _lblOutput = new Label
        {
            Text = "",
            ForeColor = Color.Green,
            Anchor = AnchorStyles.Left
        };
        layout.SetColumnSpan(_lblOutput, 2);
        layout.Controls.Add(_lblOutput, 1, 8);

        tab.Controls.Add(layout);
    }

    // ================================================================
    //  Tab 2: File Join
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
        _lblFolder = MakeLabel("Folder:");
        layout.Controls.Add(_lblFolder, 0, 0);
        _txtFolder = new TextBox
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Text = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
        };
        layout.Controls.Add(_txtFolder, 1, 0);
        _btnBrowseFolder = new Button { Text = "Browse...", AutoSize = true };
        _btnBrowseFolder.Click += OnBrowseFolder;
        layout.Controls.Add(_btnBrowseFolder, 2, 0);

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        _lblPattern = MakeLabel("Pattern:");
        layout.Controls.Add(_lblPattern, 0, 1);
        _txtPattern = new TextBox { Text = "*.txt", Anchor = AnchorStyles.Left | AnchorStyles.Right };
        layout.Controls.Add(_txtPattern, 1, 1);

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        _lblOutputFile = MakeLabel("Output:");
        layout.Controls.Add(_lblOutputFile, 0, 2);
        _txtOutputName = new TextBox { Text = "combined.txt", Anchor = AnchorStyles.Left | AnchorStyles.Right };
        layout.Controls.Add(_txtOutputName, 1, 2);

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        _btnJoin = MakePrimaryButton("Join");
        _btnJoin.Click += OnJoin;
        layout.Controls.Add(_btnJoin, 1, 3);

        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tab.Controls.Add(layout);
    }

    // ================================================================
    //  Tab 3: Punctuation Replace
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

        // Row 0 — File selection
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        _lblReplaceFile = MakeLabel("Source:");
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

        _btnBrowseReplaceFile = new Button { Text = "Browse...", AutoSize = true };
        _btnBrowseReplaceFile.Click += OnBrowseReplaceFile;
        layout.Controls.Add(_btnBrowseReplaceFile, 2, 0);

        // Row 1 — Encoding hint
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        _lblReplaceEncodingTag = MakeLabel("Encoding:");
        layout.Controls.Add(_lblReplaceEncodingTag, 0, 1);
        _lblReplaceEncoding = new Label
        {
            Text = "(No file selected)",
            ForeColor = Color.Gray,
            Anchor = AnchorStyles.Left
        };
        layout.SetColumnSpan(_lblReplaceEncoding, 2);
        layout.Controls.Add(_lblReplaceEncoding, 1, 1);

        // Row 2 — Rule list + editor
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var ruleSplit = new SplitContainer
        {
            Orientation = Orientation.Vertical,
            SplitterDistance = 220,
            FixedPanel = FixedPanel.Panel1,
            Dock = DockStyle.Fill
        };

        // Left: rule list + reorder buttons
        var leftPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3
        };
        leftPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        leftPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        leftPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _lblRulesHeader = new Label { Text = "Replace Rules:", AutoSize = true, Dock = DockStyle.Top };
        _lstRules = new ListBox
        {
            Dock = DockStyle.Fill,
            IntegralHeight = false
        };
        _lstRules.SelectedIndexChanged += OnRuleSelected;

        var reorderPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            Margin = new Padding(0, 4, 0, 0)
        };
        _btnUp = new Button { Text = "Up", Size = new Size(60, 20), FlatStyle = FlatStyle.Flat, Font = new Font("Microsoft YaHei UI", 8f) };
        _btnUp.FlatAppearance.BorderSize = 0;
        _btnUp.Click += OnMoveRuleUp;
        _btnDown = new Button { Text = "Down", Size = new Size(60, 20), FlatStyle = FlatStyle.Flat, Font = new Font("Microsoft YaHei UI", 8f), Margin = new Padding(4, 0, 0, 0) };
        _btnDown.FlatAppearance.BorderSize = 0;
        _btnDown.Click += OnMoveRuleDown;
        reorderPanel.Controls.Add(_btnUp);
        reorderPanel.Controls.Add(_btnDown);

        leftPanel.Controls.Add(_lblRulesHeader, 0, 0);
        leftPanel.Controls.Add(_lstRules, 0, 1);
        leftPanel.Controls.Add(reorderPanel, 0, 2);

        // Right: rule editor
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
        _lblFind = MakeLabel("Find:");
        rightPanel.Controls.Add(_lblFind, 0, 0);
        _txtFind = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right, Width = 150 };
        rightPanel.Controls.Add(_txtFind, 1, 0);

        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        _lblReplaceWith = MakeLabel("Replace:");
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
        _btnAddRule = new Button { Text = "➕ Add/Update", AutoSize = true, BackColor = Color.LightGreen, FlatStyle = FlatStyle.Flat };
        _btnAddRule.FlatAppearance.BorderSize = 0;
        _btnAddRule.Click += OnAddOrUpdateRule;
        _btnDeleteRule = new Button { Text = "🗑 Delete", AutoSize = true, BackColor = Color.LightCoral, FlatStyle = FlatStyle.Flat, Margin = new Padding(8, 0, 0, 0) };
        _btnDeleteRule.FlatAppearance.BorderSize = 0;
        _btnDeleteRule.Click += OnDeleteRule;
        btnPanel.Controls.Add(_btnAddRule);
        btnPanel.Controls.Add(_btnDeleteRule);
        rightPanel.SetColumnSpan(btnPanel, 2);
        rightPanel.Controls.Add(btnPanel, 0, 2);

        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        _lblHint = new Label
        {
            Text = "Hint: select a rule to edit it",
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

        // Row 3 — Execute button
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        _btnReplaceProcess = MakePrimaryButton("Execute Replace");
        _btnReplaceProcess.Enabled = false;
        _btnReplaceProcess.Click += OnReplaceProcess;
        layout.Controls.Add(_btnReplaceProcess, 1, 3);

        // Row 4 — Output
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        _lblReplaceOutput = new Label { Text = "", ForeColor = Color.Green, Anchor = AnchorStyles.Left };
        layout.SetColumnSpan(_lblReplaceOutput, 2);
        layout.Controls.Add(_lblReplaceOutput, 1, 4);

        RefreshRuleList();
        tab.Controls.Add(layout);
    }

    // ================================================================
    //  Tab 4: About
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

        var centerPanel = new TableLayoutPanel
        {
            AutoSize = true,
            Anchor = AnchorStyles.None,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(20)
        };
        centerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        // Row 0 — Icons side-by-side: program icon + avatar
        var iconBox = new PictureBox
        {
            Size = new Size(64, 64),
            SizeMode = PictureBoxSizeMode.Zoom,
            Anchor = AnchorStyles.None
        };
        try { iconBox.Image = Icon.ExtractAssociatedIcon(Application.ExecutablePath)?.ToBitmap(); } catch { }

        _pbAvatar = new PictureBox
        {
            Size = new Size(64, 64),
            SizeMode = PictureBoxSizeMode.Zoom,
            Anchor = AnchorStyles.None,
            Margin = new Padding(12, 0, 0, 0)
        };
        try
        {
            string avatarPath = Path.Combine(AppContext.BaseDirectory, "Resources", "TwilightRain.jpg");
            if (File.Exists(avatarPath))
                _pbAvatar.Image = Image.FromFile(avatarPath);
        }
        catch { }

        var iconRow = new FlowLayoutPanel
        {
            AutoSize = true,
            Anchor = AnchorStyles.None,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 8)
        };
        iconRow.Controls.Add(iconBox);
        iconRow.Controls.Add(_pbAvatar);
        centerPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        centerPanel.Controls.Add(iconRow, 0, 0);

        // Row 1 — Program name + Version (side by side)
        _lblAboutName = new Label
        {
            Text = "TwilightRain's Text Tool",
            Font = new Font("Microsoft YaHei UI", 16f, FontStyle.Bold),
            AutoSize = true
        };
        _lblAboutVersion = new Label
        {
            Text = "Version 1.5.0",
            Font = new Font("Microsoft YaHei UI", 10f),
            ForeColor = Color.Gray,
            AutoSize = true,
            Margin = new Padding(12, 5, 0, 0)
        };

        var nameRow = new FlowLayoutPanel
        {
            AutoSize = true,
            Anchor = AnchorStyles.None,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 12)
        };
        nameRow.Controls.Add(_lblAboutName);
        nameRow.Controls.Add(_lblAboutVersion);
        centerPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        centerPanel.Controls.Add(nameRow, 0, 1);

        // Row 2 — Author + URL (side by side)
        _lblAboutAuthor = new Label
        {
            Text = "Author: TwilightRain",
            Font = new Font("Microsoft YaHei UI", 10f),
            AutoSize = true
        };

        var lblUrl = new LinkLabel
        {
            Text = "https://github.com/TwilightRainDev/TextTool",
            Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Underline),
            LinkColor = Color.SteelBlue,
            ActiveLinkColor = Color.DarkBlue,
            AutoSize = true,
            Margin = new Padding(16, 0, 0, 0),
            Tag = "https://github.com/TwilightRainDev/TextTool"
        };
        lblUrl.LinkClicked += (_, _) =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = lblUrl.Tag?.ToString() ?? "https://github.com/TwilightRainDev/TextTool",
                    UseShellExecute = true
                });
            }
            catch { }
        };

        var authorRow = new FlowLayoutPanel
        {
            AutoSize = true,
            Anchor = AnchorStyles.None,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 16)
        };
        authorRow.Controls.Add(_lblAboutAuthor);
        authorRow.Controls.Add(lblUrl);
        centerPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        centerPanel.Controls.Add(authorRow, 0, 2);

        // Row 3 — Description (full width)
        _lblAboutDesc = new Label
        {
            Text = "An all-in-one text processing tool:\nLine Merge, File Join, CJK Fix, Punct. Replace",
            Font = new Font("Microsoft YaHei UI", 9f),
            ForeColor = Color.DimGray,
            AutoSize = true,
            Anchor = AnchorStyles.None,
            TextAlign = ContentAlignment.MiddleCenter,
            Margin = new Padding(0, 0, 0, 16)
        };
        centerPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        centerPanel.Controls.Add(_lblAboutDesc, 0, 3);

        // Row 4 — Language label + Language selector (side by side)
        _lblAboutLanguage = new Label
        {
            Text = "Language:",
            Font = new Font("Microsoft YaHei UI", 9f),
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft
        };
        _cmbAboutLanguage = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 140,
            Margin = new Padding(8, 0, 0, 0)
        };
        _cmbAboutLanguage.SelectedIndexChanged += OnLanguageChanged;

        var langRow = new FlowLayoutPanel
        {
            AutoSize = true,
            Anchor = AnchorStyles.None,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        langRow.Controls.Add(_lblAboutLanguage);
        langRow.Controls.Add(_cmbAboutLanguage);
        centerPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        centerPanel.Controls.Add(langRow, 0, 4);

        mainPanel.Controls.Add(centerPanel, 0, 0);
        tab.Controls.Add(mainPanel);
    }

    // ================================================================
    //  Drag & Drop
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
        if (!File.Exists(path)) { ShowError(Loc.T("MsgFileNotFound", path)); return; }
        _txtFilePath.Text = path;
        try
        {
            _lastDetection = EncodingDetector.Detect(path);
            _lblEncoding.Text = _lastDetection.DisplayName;
            _lblEncoding.ForeColor = SystemColors.ControlText;
            _btnProcess.Enabled = true;
            SetStatus(Loc.T("StatusSelected", Path.GetFileName(path), _lastDetection.DisplayName));
        }
        catch (Exception ex) { ShowError(Loc.T("MsgReadFailed", ex.Message)); }
    }

    private void SelectReplaceFile(string path)
    {
        if (!File.Exists(path)) { ShowError(Loc.T("MsgFileNotFound", path)); return; }
        _txtReplaceFile.Text = path;
        try
        {
            _lastReplaceDetection = EncodingDetector.Detect(path);
            _lblReplaceEncoding.Text = _lastReplaceDetection.DisplayName;
            _lblReplaceEncoding.ForeColor = SystemColors.ControlText;
            _btnReplaceProcess.Enabled = true;
        }
        catch (Exception ex) { ShowError(Loc.T("MsgReadFailed", ex.Message)); }
    }

    // ================================================================
    //  Tab 1: Line Merge button events
    // ================================================================
    private void OnBrowseFile(object? sender, EventArgs e)
    {
        using var dlg = OpenTextFileDialog(Loc.T("TabMerge"));
        if (dlg.ShowDialog(this) == DialogResult.OK)
            SelectFile(dlg.FileName);
    }

    private void OnProcess(object? sender, EventArgs e)
    {
        if (_lastDetection == null || string.IsNullOrWhiteSpace(_txtFilePath.Text)) return;

        try
        {
            _btnProcess.Enabled = false;
            _btnProcess.Text = Loc.T("StatusProcessing");
            SetStatus(Loc.T("StatusProcessing"));

            var options = new MergeOptions
            {
                Threshold = (int)_numThreshold.Value,
                Mode = _rbChar.Checked ? MergeMode.CharCount : MergeMode.ByteCount
            };

            var postProcess = new PostProcessOptions(
                FixCjk: _chkFixCjk.Checked,
                FixPunct: _chkFixPunct.Checked,
                PunctChars: _txtPunctChars.Text,
                NoMerge: _chkNoMerge.Checked,
                NoMergeChars: _txtNoMergeChars.Text,
                ApplyReplace: _chkApplyReplace.Checked,
                Rules: _rules);

            var result = ProcessingPipeline.Run(
                _txtFilePath.Text, _lastDetection.Encoding, options, postProcess);

            var extras = new List<string>();
            if (result.CjkFixed) extras.Add(Loc.T("ExtraCjk"));
            if (result.PunctFixed) extras.Add(Loc.T("ExtraPunct"));
            if (result.Replaced) extras.Add(Loc.T("ExtraReplace"));
            string extraMsg = extras.Count > 0
                ? $"({string.Join(", ", extras)})"
                : "";

            _lblOutput.Text = Loc.T("LabelOutput", result.OutputPath);
            _lblOutput.ForeColor = Color.Green;
            SetStatus(Loc.T("StatusComplete", result.OutputPath, extraMsg));

            if (MessageBox.Show(this,
                Loc.T("MsgProcessBody", extraMsg, result.OutputPath),
                Loc.T("MsgProcessTitle"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
            {
                RevealInExplorer(result.OutputPath);
            }
        }
        catch (Exception ex) { ShowError(Loc.T("MsgProcessFailed", ex.Message)); }
        finally { _btnProcess.Enabled = true; _btnProcess.Text = Loc.T("BtnProcess"); }
    }

    // ================================================================
    //  Tab 2: File Join button events
    // ================================================================
    private void OnBrowseFolder(object? sender, EventArgs e)
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = Loc.T("TabJoin"),
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
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) { ShowError(Loc.T("MsgInvalidFolder")); return; }
        if (string.IsNullOrEmpty(pattern)) { ShowError(Loc.T("MsgEmptyPattern")); return; }
        if (string.IsNullOrEmpty(outputName)) { ShowError(Loc.T("MsgEmptyOutput")); return; }

        try
        {
            _btnJoin.Enabled = false; _btnJoin.Text = Loc.T("StatusJoining");
            SetStatus(Loc.T("StatusJoining"));
            var (outputPath, fileCount) = FileJoiner.Join(folder, pattern, outputName);
            SetStatus(Loc.T("StatusJoinComplete", fileCount, outputPath));

            if (MessageBox.Show(this, Loc.T("MsgJoinBody", outputPath),
                Loc.T("MsgJoinTitle"),
                MessageBoxButtons.OK, MessageBoxIcon.Information) == DialogResult.OK)
            {
                RevealInExplorer(outputPath);
            }
        }
        catch (Exception ex) { ShowError(Loc.T("MsgJoinFailed", ex.Message)); }
        finally { _btnJoin.Enabled = true; _btnJoin.Text = Loc.T("BtnJoin"); }
    }

    // ================================================================
    //  Tab 3: Punctuation Replace button events
    // ================================================================
    private void OnBrowseReplaceFile(object? sender, EventArgs e)
    {
        using var dlg = OpenTextFileDialog(Loc.T("TabReplace"));
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

    private void OnMoveRuleUp(object? sender, EventArgs e)
    {
        int idx = _lstRules.SelectedIndex;
        if (idx <= 0 || idx >= _rules.Count) return;
        (_rules[idx], _rules[idx - 1]) = (_rules[idx - 1], _rules[idx]);
        ReplaceRuleStore.Save(_rules);
        SaveAndRefresh("StatusRulesSaved", _rules.Count);
        _lstRules.SelectedIndex = idx - 1;
    }

    private void OnMoveRuleDown(object? sender, EventArgs e)
    {
        int idx = _lstRules.SelectedIndex;
        if (idx < 0 || idx >= _rules.Count - 1) return;
        (_rules[idx], _rules[idx + 1]) = (_rules[idx + 1], _rules[idx]);
        ReplaceRuleStore.Save(_rules);
        SaveAndRefresh("StatusRulesSaved", _rules.Count);
        _lstRules.SelectedIndex = idx + 1;
    }

    private void OnAddOrUpdateRule(object? sender, EventArgs e)
    {
        string find = _txtFind.Text;
        string replace = _txtReplaceWith.Text;
        if (string.IsNullOrEmpty(find)) { ShowError(Loc.T("MsgEnterFind")); return; }

        if (_lstRules.SelectedIndex >= 0 && _lstRules.SelectedIndex < _rules.Count)
            _rules[_lstRules.SelectedIndex] = new ReplaceRule { Find = find, Replace = replace };
        else
            _rules.Add(new ReplaceRule { Find = find, Replace = replace });

        ReplaceRuleStore.Save(_rules);
        SaveAndRefresh("StatusRulesSaved", _rules.Count);
    }

    private void OnDeleteRule(object? sender, EventArgs e)
    {
        if (_lstRules.SelectedIndex < 0 || _lstRules.SelectedIndex >= _rules.Count) return;

        _rules.RemoveAt(_lstRules.SelectedIndex);
        ReplaceRuleStore.Save(_rules);
        SaveAndRefresh("StatusRuleDeleted", _rules.Count);
    }

    private void SaveAndRefresh(string statusKey, int ruleCount)
    {
        RefreshRuleList();
        _txtFind.Clear();
        _txtReplaceWith.Clear();
        SetStatus(Loc.T(statusKey, ruleCount));
    }

    private void OnReplaceProcess(object? sender, EventArgs e)
    {
        if (_lastReplaceDetection == null || string.IsNullOrWhiteSpace(_txtReplaceFile.Text)) return;
        if (_rules.Count == 0) { ShowError(Loc.T("MsgAddRule")); return; }

        try
        {
            _btnReplaceProcess.Enabled = false;
            _btnReplaceProcess.Text = Loc.T("StatusReplacing");
            SetStatus(Loc.T("StatusReplacing"));

            string dir = Path.GetDirectoryName(_txtReplaceFile.Text) ?? ".";
            string name = Path.GetFileNameWithoutExtension(_txtReplaceFile.Text);
            string ext = Path.GetExtension(_txtReplaceFile.Text);
            string outputPath = Path.Combine(dir, $"{name}_Processed{ext}");

            string content = File.ReadAllText(_txtReplaceFile.Text, _lastReplaceDetection.Encoding);
            string replaced = PunctuationReplacer.Apply(content, _rules);
            File.WriteAllText(outputPath, replaced, new UTF8Encoding(true));

            _lblReplaceOutput.Text = Loc.T("LabelOutput", outputPath);
            _lblReplaceOutput.ForeColor = Color.Green;
            SetStatus(Loc.T("StatusReplaceComplete", outputPath));

            if (MessageBox.Show(this, Loc.T("MsgReplaceBody", outputPath),
                Loc.T("MsgReplaceTitle"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
            {
                RevealInExplorer(outputPath);
            }
        }
        catch (Exception ex) { ShowError(Loc.T("MsgReplaceFailed", ex.Message)); }
        finally { _btnReplaceProcess.Enabled = true; _btnReplaceProcess.Text = Loc.T("BtnExecuteReplace"); }
    }

    // ================================================================
    //  Rule list refresh
    // ================================================================
    private void RefreshRuleList()
    {
        _lstRules.Items.Clear();
        foreach (var rule in _rules)
            _lstRules.Items.Add($"{rule.Find}  →  {rule.Replace}");
    }

    // ================================================================
    //  Helpers
    // ================================================================
    private static Label MakeLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        TextAlign = ContentAlignment.MiddleRight,
        Anchor = AnchorStyles.Right
    };

    private static Button MakePrimaryButton(string text)
    {
        var btn = new Button
        {
            Text = text,
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold),
            BackColor = Color.SteelBlue,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Padding = new Padding(24, 6, 24, 6)
        };
        btn.FlatAppearance.BorderSize = 0;
        return btn;
    }

    private static void RevealInExplorer(string filePath)
    {
        System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{filePath}\"");
    }

    private static OpenFileDialog OpenTextFileDialog(string title) => new()
    {
        Title = title,
        Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
        RestoreDirectory = true
    };

    private void SetStatus(string message) => _statusLabel.Text = message;

    private void ShowError(string message)
    {
        _statusLabel.Text = $"❌ {message}";
        MessageBox.Show(this, message, Loc.T("MsgErrorTitle"),
            MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
