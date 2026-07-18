using TextTool.Localization;

namespace TextTool.Controls;

/// <summary>
/// "关于" 页签：应用信息、作者、链接、语言选择。
/// </summary>
public sealed class AboutTabControl : UserControl
{
    private Label _lblAboutName = null!;
    private Label _lblAboutVersion = null!;
    private PictureBox _pbAvatar = null!;
    private Label _lblAboutAuthor = null!;
    private Label _lblAboutDesc = null!;
    private Label _lblAboutLanguage = null!;
    private ComboBox _cmbAboutLanguage = null!;

    public AboutTabControl()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        BackColor = Color.White;

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

        // Row 0 — Icons
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

        // Row 1 — Program name + Version
        _lblAboutName = new Label
        {
            Text = "TwilightRain's Text Tool",
            Font = new Font("Microsoft YaHei UI", 16f, FontStyle.Bold),
            AutoSize = true
        };
        var initialVer = typeof(AboutTabControl).Assembly.GetName().Version;
        _lblAboutVersion = new Label
        {
            Text = initialVer != null ? $"Version {initialVer.Major}.{initialVer.Minor}.{initialVer.Build}" : "",
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

        // Row 2 — Author + URL
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

        // Row 3 — Description
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

        // Row 4 — Language
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
        Controls.Add(mainPanel);
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (_cmbAboutLanguage.SelectedItem is LanguageInfo lang && lang.Code != Loc.CurrentLang)
            Loc.SetLanguage(lang.Code);
    }

    public void ApplyLocalization()
    {
        _lblAboutName.Text = Loc.T("AboutTitle");
        var asmVer = typeof(AboutTabControl).Assembly.GetName().Version;
        _lblAboutVersion.Text = Loc.T("AboutVersion",
            asmVer != null ? $"{asmVer.Major}.{asmVer.Minor}.{asmVer.Build}" : "?");
        _lblAboutAuthor.Text = Loc.T("AboutAuthor");
        _lblAboutDesc.Text = Loc.T("AboutDescription");
        _lblAboutLanguage.Text = Loc.T("AboutLanguage");

        RebuildLanguageCombo();
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
}
