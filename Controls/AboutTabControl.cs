using System.Text;
using System.Text.Json;
using TextTool.Localization;

namespace TextTool.Controls;

/// <summary>
/// "关于" 页签：应用信息、作者、链接、语言选择、深色模式、配置导入导出。
/// </summary>
public sealed class AboutTabControl : UserControl
{
    // ===== 暗色/亮色主题色 =====
    private static readonly Color DarkBg = Color.FromArgb(30, 30, 30);
    private static readonly Color DarkFg = Color.FromArgb(220, 220, 220);
    private static readonly Color DarkControlBg = Color.FromArgb(45, 45, 45);
    private static readonly Color LightBg = Color.White;
    private static readonly Color LightFg = Color.Black;
    private static readonly Color LightGrayFg = Color.Gray;

    // ===== 控件 =====
    private Label _lblAboutName = null!;
    private Label _lblAboutVersion = null!;
    private PictureBox _pbAvatar = null!;
    private Label _lblAboutAuthor = null!;
    private Label _lblAboutDesc = null!;
    private Label _lblAboutLanguage = null!;
    private ComboBox _cmbAboutLanguage = null!;
    private Button _btnDarkMode = null!;
    private Button _btnExportConfig = null!;
    private Button _btnImportConfig = null!;

    private bool _darkMode;

    public AboutTabControl()
    {
        _darkMode = LoadDarkModePref();
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        BackColor = _darkMode ? DarkBg : LightBg;

        var mainPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 1
        };
        mainPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var centerPanel = new TableLayoutPanel
        {
            AutoSize = true, Anchor = AnchorStyles.None,
            ColumnCount = 1, RowCount = 7,
            Padding = new Padding(20)
        };
        centerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        // Row 0 — Icons
        var iconBox = new PictureBox { Size = new Size(64, 64), SizeMode = PictureBoxSizeMode.Zoom, Anchor = AnchorStyles.None };
        try { iconBox.Image = Icon.ExtractAssociatedIcon(Application.ExecutablePath)?.ToBitmap(); } catch { }

        _pbAvatar = new PictureBox { Size = new Size(64, 64), SizeMode = PictureBoxSizeMode.Zoom, Anchor = AnchorStyles.None, Margin = new Padding(12, 0, 0, 0) };
        try
        {
            string avatarPath = Path.Combine(AppContext.BaseDirectory, "Resources", "TwilightRain.jpg");
            if (File.Exists(avatarPath)) _pbAvatar.Image = Image.FromFile(avatarPath);
        }
        catch { }

        var iconRow = new FlowLayoutPanel { AutoSize = true, Anchor = AnchorStyles.None, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 0, 0, 8) };
        iconRow.Controls.Add(iconBox);
        iconRow.Controls.Add(_pbAvatar);
        centerPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        centerPanel.Controls.Add(iconRow, 0, 0);

        // Row 1 — Program name + Version
        _lblAboutName = new Label { Text = "TwilightRain's Text Tool", Font = new Font("Microsoft YaHei UI", 16f, FontStyle.Bold), AutoSize = true };
        var initialVer = typeof(AboutTabControl).Assembly.GetName().Version;
        _lblAboutVersion = new Label
        {
            Text = initialVer != null ? $"Version {initialVer.Major}.{initialVer.Minor}.{initialVer.Build}" : "",
            Font = new Font("Microsoft YaHei UI", 10f),
            ForeColor = _darkMode ? DarkFg : Color.Gray,
            AutoSize = true, Margin = new Padding(12, 5, 0, 0)
        };

        var nameRow = new FlowLayoutPanel { AutoSize = true, Anchor = AnchorStyles.None, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 0, 0, 12) };
        nameRow.Controls.Add(_lblAboutName);
        nameRow.Controls.Add(_lblAboutVersion);
        centerPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        centerPanel.Controls.Add(nameRow, 0, 1);

        // Row 2 — Author + URL
        _lblAboutAuthor = new Label { Text = "Author: TwilightRain", Font = new Font("Microsoft YaHei UI", 10f), AutoSize = true };

        var lblUrl = new LinkLabel
        {
            Text = "https://github.com/TwilightRainDev/TextTool",
            Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Underline),
            LinkColor = _darkMode ? Color.LightBlue : Color.SteelBlue,
            ActiveLinkColor = _darkMode ? Color.DeepSkyBlue : Color.DarkBlue,
            AutoSize = true, Margin = new Padding(16, 0, 0, 0),
            Tag = "https://github.com/TwilightRainDev/TextTool"
        };
        lblUrl.LinkClicked += (_, _) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = lblUrl.Tag?.ToString() ?? "", UseShellExecute = true }); } catch { }
        };

        var authorRow = new FlowLayoutPanel { AutoSize = true, Anchor = AnchorStyles.None, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 0, 0, 16) };
        authorRow.Controls.Add(_lblAboutAuthor);
        authorRow.Controls.Add(lblUrl);
        centerPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        centerPanel.Controls.Add(authorRow, 0, 2);

        // Row 3 — Description
        _lblAboutDesc = new Label
        {
            Text = "An all-in-one text processing tool:\nLine Merge, File Join, CJK Fix, Punct. Replace",
            Font = new Font("Microsoft YaHei UI", 9f),
            ForeColor = _darkMode ? DarkFg : Color.DimGray,
            AutoSize = true, Anchor = AnchorStyles.None,
            TextAlign = ContentAlignment.MiddleCenter,
            Margin = new Padding(0, 0, 0, 16)
        };
        centerPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        centerPanel.Controls.Add(_lblAboutDesc, 0, 3);

        // Row 4 — Language
        _lblAboutLanguage = new Label { Text = "Language:", Font = new Font("Microsoft YaHei UI", 9f), AutoSize = true, TextAlign = ContentAlignment.MiddleLeft };
        _cmbAboutLanguage = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140, Margin = new Padding(8, 0, 0, 0) };
        _cmbAboutLanguage.SelectedIndexChanged += OnLanguageChanged;

        var langRow = new FlowLayoutPanel { AutoSize = true, Anchor = AnchorStyles.None, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 0, 0, 12) };
        langRow.Controls.Add(_lblAboutLanguage);
        langRow.Controls.Add(_cmbAboutLanguage);
        centerPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        centerPanel.Controls.Add(langRow, 0, 4);

        // Row 5 — Dark mode toggle
        _btnDarkMode = new Button
        {
            Text = _darkMode ? "☀ Light Mode" : "🌙 Dark Mode",
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            BackColor = _darkMode ? DarkControlBg : SystemColors.Control,
            ForeColor = _darkMode ? DarkFg : LightFg,
            Font = new Font("Microsoft YaHei UI", 9f),
            Padding = new Padding(12, 4, 12, 4),
            Margin = new Padding(0, 0, 0, 8)
        };
        _btnDarkMode.FlatAppearance.BorderSize = 0;
        _btnDarkMode.Click += OnToggleDarkMode;
        centerPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        centerPanel.Controls.Add(_btnDarkMode, 0, 5);

        // Row 6 — Config import/export
        var configRow = new FlowLayoutPanel { AutoSize = true, Anchor = AnchorStyles.None, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        _btnExportConfig = new Button { Text = "⬆ Export Config", AutoSize = true, FlatStyle = FlatStyle.Flat, Font = new Font("Microsoft YaHei UI", 9f), Padding = new Padding(12, 4, 12, 4), Margin = new Padding(0, 0, 8, 0) };
        _btnExportConfig.FlatAppearance.BorderSize = 0;
        _btnExportConfig.Click += OnExportConfig;
        _btnImportConfig = new Button { Text = "⬇ Import Config", AutoSize = true, FlatStyle = FlatStyle.Flat, Font = new Font("Microsoft YaHei UI", 9f), Padding = new Padding(12, 4, 12, 4) };
        _btnImportConfig.FlatAppearance.BorderSize = 0;
        _btnImportConfig.Click += OnImportConfig;
        configRow.Controls.Add(_btnExportConfig);
        configRow.Controls.Add(_btnImportConfig);
        centerPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        centerPanel.Controls.Add(configRow, 0, 6);

        mainPanel.Controls.Add(centerPanel, 0, 0);
        Controls.Add(mainPanel);

        ApplyTheme();
    }

    // ================================================================
    //  深色模式
    // ================================================================

    private void OnToggleDarkMode(object? sender, EventArgs e)
    {
        _darkMode = !_darkMode;
        SaveDarkModePref(_darkMode);
        ApplyTheme();
        _btnDarkMode.Text = _darkMode ? "☀ Light Mode" : "🌙 Dark Mode";
    }

    private void ApplyTheme()
    {
        BackColor = _darkMode ? DarkBg : LightBg;

        foreach (Control c in Controls)
            ApplyThemeToControl(c);
    }

    private void ApplyThemeToControl(Control control)
    {
        if (control is TableLayoutPanel tlp)
        {
            foreach (Control child in tlp.Controls)
                ApplyThemeToControlRecursive(child);
        }
    }

    private void ApplyThemeToControlRecursive(Control ctl)
    {
        if (ctl is Label lbl)
        {
            lbl.ForeColor = _darkMode ? DarkFg : Color.Black;
            if (lbl == _lblAboutVersion)
                lbl.ForeColor = _darkMode ? DarkFg : Color.Gray;
            if (lbl == _lblAboutDesc)
                lbl.ForeColor = _darkMode ? DarkFg : Color.DimGray;
        }
        else if (ctl is LinkLabel link)
        {
            link.LinkColor = _darkMode ? Color.LightBlue : Color.SteelBlue;
            link.ActiveLinkColor = _darkMode ? Color.DeepSkyBlue : Color.DarkBlue;
        }
        else if (ctl is ComboBox combo)
        {
            combo.BackColor = _darkMode ? DarkControlBg : LightBg;
            combo.ForeColor = _darkMode ? DarkFg : LightFg;
        }
        else if (ctl is Button btn)
        {
            btn.BackColor = _darkMode ? DarkControlBg : SystemColors.Control;
            btn.ForeColor = _darkMode ? DarkFg : LightFg;
        }
        else if (ctl is FlowLayoutPanel flp)
        {
            foreach (Control child in flp.Controls)
                ApplyThemeToControlRecursive(child);
        }
        else if (ctl is TableLayoutPanel tp)
        {
            foreach (Control child in tp.Controls)
                ApplyThemeToControlRecursive(child);
        }
    }

    // ================================================================
    //  配置导入 / 导出
    // ================================================================

    private void OnExportConfig(object? sender, EventArgs e)
    {
        using var dlg = new FolderBrowserDialog { Description = Loc.T("ConfigExportDesc"), UseDescriptionForTitle = true };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            string srcDir = AppContext.BaseDirectory;
            string dstDir = dlg.SelectedPath;

            // 导出替换规则
            string rulesSrc = Path.Combine(srcDir, "replace_rules.json");
            if (File.Exists(rulesSrc))
                File.Copy(rulesSrc, Path.Combine(dstDir, "replace_rules.json"), overwrite: true);

            // 导出应用配置
            string configSrc = Path.Combine(srcDir, "app_config.json");
            if (File.Exists(configSrc))
                File.Copy(configSrc, Path.Combine(dstDir, "app_config.json"), overwrite: true);

            StatusChanged?.Invoke(Loc.T("ConfigExported", dstDir));
            MessageBox.Show(this, Loc.T("MsgConfigExported", dstDir),
                Loc.T("MsgSuccessTitle"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { ErrorOccurred?.Invoke(Loc.T("MsgConfigExportFailed", ex.Message)); }
    }

    private void OnImportConfig(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Title = Loc.T("ConfigImportTitle"),
            Filter = "Config files (app_config.json, replace_rules.json)|app_config.json;replace_rules.json|All files (*.*)|*.*",
            Multiselect = true,
            RestoreDirectory = true
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            string dstDir = AppContext.BaseDirectory;
            foreach (string srcPath in dlg.FileNames)
            {
                string name = Path.GetFileName(srcPath);
                if (name == "app_config.json" || name == "replace_rules.json")
                    File.Copy(srcPath, Path.Combine(dstDir, name), overwrite: true);
            }

            // 如果导入了 app_config.json，重新加载语言偏好
            if (dlg.FileNames.Any(f => Path.GetFileName(f) == "app_config.json"))
            {
                string lang = LoadLanguageFromConfig();
                if (!string.IsNullOrEmpty(lang))
                    Loc.SetLanguage(lang);
            }

            MessageBox.Show(this, Loc.T("MsgConfigImported"),
                Loc.T("MsgSuccessTitle"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            StatusChanged?.Invoke(Loc.T("ConfigImported"));
        }
        catch (Exception ex) { ErrorOccurred?.Invoke(Loc.T("MsgConfigImportFailed", ex.Message)); }
    }

    private static string LoadLanguageFromConfig()
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "app_config.json");
            if (File.Exists(path))
            {
                var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
                if (doc.RootElement.TryGetProperty("language", out var lang))
                    return lang.GetString() ?? "";
            }
        }
        catch { }
        return "";
    }

    // ================================================================
    //  深色模式持久化
    // ================================================================

    private static string ConfigPath => Path.Combine(AppContext.BaseDirectory, "app_config.json");

    private static bool LoadDarkModePref()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var doc = JsonDocument.Parse(File.ReadAllText(ConfigPath, Encoding.UTF8));
                if (doc.RootElement.TryGetProperty("darkMode", out var dm))
                    return dm.GetBoolean();
            }
        }
        catch { }
        return false;
    }

    private static void SaveDarkModePref(bool dark)
    {
        try
        {
            var dict = new Dictionary<string, object>
            {
                ["language"] = Loc.CurrentLang,
                ["darkMode"] = dark
            };
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(dict), Encoding.UTF8);
        }
        catch { }
    }

    // ================================================================
    //  事件
    // ================================================================

    public event Action<string>? StatusChanged;
    public event Action<string>? ErrorOccurred;

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (_cmbAboutLanguage.SelectedItem is LanguageInfo lang && lang.Code != Loc.CurrentLang)
        {
            Loc.SetLanguage(lang.Code);
            // 切换语言后重新持久化（包含 darkMode）
            SaveDarkModePref(_darkMode);
        }
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
        _btnDarkMode.Text = _darkMode ? Loc.T("BtnLightMode") : Loc.T("BtnDarkMode");
        _btnExportConfig.Text = Loc.T("BtnExportConfig");
        _btnImportConfig.Text = Loc.T("BtnImportConfig");

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
