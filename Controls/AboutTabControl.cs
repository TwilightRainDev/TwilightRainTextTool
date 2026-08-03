using System.Text;
using System.Text.Json;
using TextTool.Localization;
using TextTool.Services;

namespace TextTool.Controls;

/// <summary>
/// "关于" 页签：应用信息、作者、链接、语言选择 + 深色模式切换、配置导入导出。
/// </summary>
public sealed class AboutTabControl : UserControl, IThemedTab
{
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
    private Button _btnCheckUpdate = null!;

    public AboutTabControl()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        var mainPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 1
        };
        mainPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var centerPanel = new TableLayoutPanel
        {
            AutoSize = true, Anchor = AnchorStyles.None,
            ColumnCount = 1, RowCount = 6,
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
            ForeColor = ThemeManager.IsDarkMode ? ThemeManager.DarkFg : Color.Gray,
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
            Text = UpdateChecker.RepositoryUrl,
            Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Underline),
            LinkColor = ThemeManager.IsDarkMode ? Color.LightBlue : Color.SteelBlue,
            ActiveLinkColor = ThemeManager.IsDarkMode ? Color.DeepSkyBlue : Color.DarkBlue,
            AutoSize = true, Margin = new Padding(16, 0, 0, 0),
            Tag = UpdateChecker.RepositoryUrl
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
            Text = "An all-in-one text processing tool:\nLine Merge, File Join, CJK Fix, Punct. Replace, VN Reformat",
            Font = new Font("Microsoft YaHei UI", 9f),
            ForeColor = ThemeManager.IsDarkMode ? ThemeManager.DarkFg : Color.DimGray,
            AutoSize = true, Anchor = AnchorStyles.None,
            TextAlign = ContentAlignment.MiddleCenter,
            Margin = new Padding(0, 0, 0, 16)
        };
        centerPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        centerPanel.Controls.Add(_lblAboutDesc, 0, 3);

        // Row 4 — Language selector + Dark mode toggle (并置)
        _lblAboutLanguage = new Label { Text = "Language:", Font = new Font("Microsoft YaHei UI", 9f), AutoSize = true, TextAlign = ContentAlignment.MiddleLeft };
        _cmbAboutLanguage = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140, Margin = new Padding(8, 0, 0, 0) };
        _cmbAboutLanguage.SelectedIndexChanged += OnLanguageChanged;

        _btnDarkMode = new Button
        {
            Text = ThemeManager.IsDarkMode ? "Light Mode" : "Dark Mode",
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            BackColor = ThemeManager.IsDarkMode ? ThemeManager.DarkControlBg : SystemColors.Control,
            ForeColor = ThemeManager.Fg,
            Font = new Font("Microsoft YaHei UI", 9f),
            Padding = new Padding(12, 4, 12, 4),
            Margin = new Padding(16, 0, 0, 0)
        };
        _btnDarkMode.FlatAppearance.BorderSize = 0;
        _btnDarkMode.Click += OnToggleDarkMode;

        var langRow = new FlowLayoutPanel { AutoSize = true, Anchor = AnchorStyles.None, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 0, 0, 12) };
        langRow.Controls.Add(_lblAboutLanguage);
        langRow.Controls.Add(_cmbAboutLanguage);
        langRow.Controls.Add(_btnDarkMode);
        centerPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        centerPanel.Controls.Add(langRow, 0, 4);

        // Row 5 — Config import/export + Check update
        var configRow = new FlowLayoutPanel { AutoSize = true, Anchor = AnchorStyles.None, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        _btnExportConfig = new Button { Text = "Export Config", AutoSize = true, FlatStyle = FlatStyle.Flat, Font = new Font("Microsoft YaHei UI", 9f), Padding = new Padding(12, 4, 12, 4), Margin = new Padding(0, 0, 8, 0) };
        _btnExportConfig.FlatAppearance.BorderSize = 0;
        _btnExportConfig.Click += OnExportConfig;
        _btnImportConfig = new Button { Text = "Import Config", AutoSize = true, FlatStyle = FlatStyle.Flat, Font = new Font("Microsoft YaHei UI", 9f), Padding = new Padding(12, 4, 12, 4), Margin = new Padding(0, 0, 8, 0) };
        _btnImportConfig.FlatAppearance.BorderSize = 0;
        _btnImportConfig.Click += OnImportConfig;
        _btnCheckUpdate = new Button { Text = "Check Update", AutoSize = true, FlatStyle = FlatStyle.Flat, Font = new Font("Microsoft YaHei UI", 9f), Padding = new Padding(12, 4, 12, 4) };
        _btnCheckUpdate.FlatAppearance.BorderSize = 0;
        _btnCheckUpdate.Click += OnCheckUpdate;
        configRow.Controls.Add(_btnExportConfig);
        configRow.Controls.Add(_btnImportConfig);
        configRow.Controls.Add(_btnCheckUpdate);
        centerPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        centerPanel.Controls.Add(configRow, 0, 5);

        mainPanel.Controls.Add(centerPanel, 0, 0);
        Controls.Add(mainPanel);

        ApplyTheme();
    }

    // ================================================================
    //  主题 — 深色 / 浅色
    // ================================================================

    private void OnToggleDarkMode(object? sender, EventArgs e)
    {
        ThemeManager.Toggle();
        // ThemeManager.Toggle() 触发 ThemeChanged → MainForm.ApplyTheme() → 所有页签主题更新
    }

    /// <summary>公开给 MainForm 调用以应用当前主题</summary>
    public void ApplyTheme()
    {
        ControlsHelper.ApplyTheme(this);

        // 特殊覆盖
        _btnDarkMode.Text = ThemeManager.IsDarkMode ? Loc.T("BtnLightMode") : Loc.T("BtnDarkMode");
        _btnDarkMode.BackColor = ThemeManager.IsDarkMode ? ThemeManager.DarkControlBg : SystemColors.Control;
        _lblAboutVersion.ForeColor = ThemeManager.MutedFg;
        _lblAboutDesc.ForeColor = ThemeManager.IsDarkMode ? Color.FromArgb(180, 180, 180) : Color.DimGray;
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

            string rulesSrc = Path.Combine(srcDir, "replace_rules.json");
            if (File.Exists(rulesSrc))
                File.Copy(rulesSrc, Path.Combine(dstDir, "replace_rules.json"), overwrite: true);

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

            if (dlg.FileNames.Any(f => Path.GetFileName(f) == "app_config.json"))
            {
                // 从新导入的 app_config.json 中读取语言偏好
                string importedConfig = Path.Combine(dstDir, "app_config.json");
                string? lang = null;
                try
                {
                    if (File.Exists(importedConfig))
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(importedConfig, Encoding.UTF8));
                        if (doc.RootElement.TryGetProperty("language", out var langProp))
                            lang = langProp.GetString();
                    }
                }
                catch { }
                if (!string.IsNullOrEmpty(lang))
                    Loc.SetLanguage(lang);
            }

            MessageBox.Show(this, Loc.T("MsgConfigImported"),
                Loc.T("MsgSuccessTitle"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            StatusChanged?.Invoke(Loc.T("ConfigImported"));
        }
        catch (Exception ex) { ErrorOccurred?.Invoke(Loc.T("MsgConfigImportFailed", ex.Message)); }
    }

    // ================================================================
    //  检查更新
    // ================================================================

    private async void OnCheckUpdate(object? sender, EventArgs e)
    {
        _btnCheckUpdate.Enabled = false;
        string currentVersion = GetCurrentVersion();

        try
        {
            using var client = UpdateClient.Create(TimeSpan.FromSeconds(10));
            string? latest = await UpdateChecker.GetLatestVersionAsync(client);

            if (latest == null)
            {
                MessageBox.Show(this, Loc.T("UpdateCheckFailed"),
                    Loc.T("MsgSuccessTitle"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else if (UpdateChecker.IsNewer(currentVersion, latest))
            {
                var result = MessageBox.Show(this,
                    Loc.T("UpdateAvailable", currentVersion, latest),
                    Loc.T("MsgSuccessTitle"),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (result == DialogResult.Yes)
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = UpdateChecker.RepositoryUrl + "/releases",
                        UseShellExecute = true
                    });
                }
            }
            else
            {
                MessageBox.Show(this, Loc.T("UpdateUpToDate", currentVersion),
                    Loc.T("MsgSuccessTitle"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch
        {
            MessageBox.Show(this, Loc.T("UpdateCheckFailed"),
                Loc.T("MsgSuccessTitle"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        finally
        {
            _btnCheckUpdate.Enabled = true;
        }
    }

    private static string GetCurrentVersion()
    {
        var v = typeof(AboutTabControl).Assembly.GetName().Version;
        return v != null ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";
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
            // SetLanguage → SaveLanguage() 已通过 ThemeManager.SaveAll() 附带 darkMode
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
        _btnDarkMode.Text = ThemeManager.IsDarkMode ? Loc.T("BtnLightMode") : Loc.T("BtnDarkMode");
        _btnExportConfig.Text = Loc.T("BtnExportConfig");
        _btnImportConfig.Text = Loc.T("BtnImportConfig");
        _btnCheckUpdate.Text = Loc.T("BtnCheckUpdate");

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
