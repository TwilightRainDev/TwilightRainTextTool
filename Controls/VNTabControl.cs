using System.Text;
using System.Text.RegularExpressions;
using TextTool.Localization;
using TextTool.Services;

namespace TextTool.Controls;

/// <summary>
/// "视觉小说" 页签：将视觉小说脚本从固定宽度硬换行排版为自然段落，
/// 并可选地补全对话标点。角色设定通过预设方案管理（对标标点替换的方案系统）。
/// </summary>
public sealed class VNTabControl : UserControl, IStatusSource
{
    // ===== 事件 =====
    public event Action<string>? StatusChanged;
    public event Action<string>? ErrorOccurred;

    // ===== 控件 =====
    private Label _lblSourceFile = null!;
    private TextBox _txtFilePath = null!;
    private Button _btnBrowse = null!;
    private Label _lblEncodingTag = null!;
    private Label _lblEncoding = null!;
    private Label _lblMode = null!;
    private RadioButton _rbAll = null!;
    private RadioButton _rbReformat = null!;
    private RadioButton _rbFixPunct = null!;
    private Label _lblMaxPara = null!;
    private NumericUpDown _numMaxPara = null!;
    private Button _btnPresetSchemes = null!;
    private Label _lblSelectedPresets = null!;
    private Button _btnProcess = null!;
    private Label _lblOutput = null!;

    // ===== 状态 =====
    private readonly List<string> _selectedFiles = new();
    private DetectionResult? _lastDetection;
    private List<VNCharacterScheme> _selectedSchemes = new();

    public VNTabControl()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 8,
            Padding = new Padding(16, 16, 16, 8)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        // Row 0 — Source file selection
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        _lblSourceFile = MakeLabel("Source File:");
        layout.Controls.Add(_lblSourceFile, 0, 0);
        _txtFilePath = new TextBox
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            ReadOnly = true,
            BackColor = ThemeManager.ControlBg,
            ForeColor = ThemeManager.Fg,
            AllowDrop = true
        };
        _txtFilePath.DragEnter += OnFileDragEnter;
        _txtFilePath.DragOver += OnFileDragOver;
        _txtFilePath.DragLeave += OnFileDragLeave;
        _txtFilePath.DragDrop += OnFileDragDrop;
        layout.Controls.Add(_txtFilePath, 1, 0);

        _btnBrowse = new Button { Text = "Browse...", AutoSize = true };
        _btnBrowse.Click += OnBrowseFile;
        layout.Controls.Add(_btnBrowse, 2, 0);

        // Row 1 — Encoding
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        _lblEncodingTag = MakeLabel("Encoding:");
        layout.Controls.Add(_lblEncodingTag, 0, 1);
        _lblEncoding = new Label
        {
            Text = "(No file selected)",
            ForeColor = Color.Gray,
            Anchor = AnchorStyles.Left
        };
        layout.SetColumnSpan(_lblEncoding, 2);
        layout.Controls.Add(_lblEncoding, 1, 1);

        // Row 2 — Mode selection
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _lblMode = MakeLabel("Mode:");
        layout.Controls.Add(_lblMode, 0, 2);

        var modePanel = new FlowLayoutPanel
        {
            Anchor = AnchorStyles.Left,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true
        };
        _rbAll = new RadioButton { Text = "All-in-One", Checked = true, AutoSize = true };
        _rbReformat = new RadioButton { Text = "Reformat", AutoSize = true, Margin = new Padding(12, 0, 0, 0) };
        _rbFixPunct = new RadioButton { Text = "Fix Punct.", AutoSize = true, Margin = new Padding(12, 0, 0, 0) };
        modePanel.Controls.Add(_rbAll);
        modePanel.Controls.Add(_rbReformat);
        modePanel.Controls.Add(_rbFixPunct);
        layout.SetColumnSpan(modePanel, 2);
        layout.Controls.Add(modePanel, 1, 2);

        // Row 3 — Max para length
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        _lblMaxPara = MakeLabel("Max Para:");
        layout.Controls.Add(_lblMaxPara, 0, 3);

        _numMaxPara = new NumericUpDown
        {
            Minimum = 100, Maximum = 2000, Value = 450, Width = 80
        };
        layout.Controls.Add(_numMaxPara, 1, 3);

        // Row 4 — Preset scheme button + summary
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var presetRow = new FlowLayoutPanel
        {
            Anchor = AnchorStyles.Left,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true
        };
        _btnPresetSchemes = new ThemedFlatButton
        {
            Text = Loc.T("BtnVNPresetSchemes"),
            AutoSize = true,
            BackColor = ControlsHelper.ButtonBg,
            ForeColor = ControlsHelper.ButtonFg,
            Font = new Font("Microsoft YaHei UI", 9f),
            FlatStyle = FlatStyle.Flat
        };
        _btnPresetSchemes.FlatAppearance.BorderSize = 0;
        _btnPresetSchemes.Click += OnPresetSchemes;
        presetRow.Controls.Add(_btnPresetSchemes);

        _lblSelectedPresets = new Label
        {
            Text = Loc.T("StatusVNPresetNone"),
            AutoSize = true,
            ForeColor = Color.Gray,
            Font = new Font("Microsoft YaHei UI", 9f),
            Margin = new Padding(12, 0, 0, 0),
            TextAlign = ContentAlignment.MiddleLeft
        };
        presetRow.Controls.Add(_lblSelectedPresets);

        layout.SetColumnSpan(presetRow, 3);
        layout.Controls.Add(presetRow, 0, 4);

        // Row 5 — Process button
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        _btnProcess = MakePrimaryButton("Process Visual Novel");
        _btnProcess.Enabled = false;
        _btnProcess.Click += OnProcess;
        _btnProcess.Anchor = AnchorStyles.None;
        layout.SetColumnSpan(_btnProcess, 3);
        layout.Controls.Add(_btnProcess, 1, 5);

        // Row 6 — Output status
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        _lblOutput = new Label { Text = "", ForeColor = Color.Green, Anchor = AnchorStyles.Left };
        layout.SetColumnSpan(_lblOutput, 3);
        layout.Controls.Add(_lblOutput, 1, 6);

        // Row 7 — Spacer (fill)
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        Controls.Add(layout);
    }

    // ================================================================
    //  本地化
    // ================================================================

    public void ApplyLocalization()
    {
        _lblSourceFile.Text = Loc.T("LabelVNFile");
        _btnBrowse.Text = Loc.T("BtnBrowse");
        _lblEncodingTag.Text = Loc.T("LabelVNEncoding");
        if (_lastDetection == null)
            _lblEncoding.Text = Loc.T("EncodingNotSelected");
        _lblMode.Text = Loc.T("LabelVNMode");
        _rbAll.Text = Loc.T("RadioVNAll");
        _rbReformat.Text = Loc.T("RadioVNReformat");
        _rbFixPunct.Text = Loc.T("RadioVNFixPunct");
        _lblMaxPara.Text = Loc.T("LabelVNMaxPara");
        _btnPresetSchemes.Text = Loc.T("BtnVNPresetSchemes");
        _btnProcess.Text = Loc.T("BtnVNProcess");

        UpdatePresetSummary();
    }

    // ================================================================
    //  主题
    // ================================================================

    public void ApplyTheme()
    {
        ControlsHelper.ApplyTheme(this);
        _lblEncoding.ForeColor = ThemeManager.MutedFg;
        _lblSelectedPresets.ForeColor = ThemeManager.MutedFg;
    }

    // ================================================================
    //  Drag & Drop
    // ================================================================

    private void OnFileDragEnter(object? sender, DragEventArgs e)
    {
        ControlsHelper.SetupFileDragEnter(e, _txtFilePath);
    }

    private void OnFileDragOver(object? sender, DragEventArgs e)
    {
        ControlsHelper.SetupFileDragOver(e);
    }

    private void OnFileDragLeave(object? sender, EventArgs e)
    {
        ControlsHelper.ResetFileDragLeave(_txtFilePath);
    }

    private void OnFileDragDrop(object? sender, DragEventArgs e)
    {
        _txtFilePath.BackColor = Color.White;
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            SelectFiles(files);
    }

    private void SelectFiles(string[] files)
    {
        _selectedFiles.Clear();
        var valid = new List<string>();
        foreach (var f in files)
        {
            if (File.Exists(f)) valid.Add(f);
        }
        if (valid.Count == 0) return;

        _selectedFiles.AddRange(valid);
        _txtFilePath.Text = valid.Count == 1
            ? valid[0]
            : $"[{valid.Count} files selected]";

        try
        {
            _lastDetection = EncodingDetector.Detect(valid[0]);
            _lblEncoding.Text = _lastDetection.DisplayName;
            _lblEncoding.ForeColor = SystemColors.ControlText;
        }
        catch { }

        _btnProcess.Enabled = true;
        StatusChanged?.Invoke(Loc.T("StatusFilesSelected", valid.Count));
    }

    // ================================================================
    //  预设角色方案（对标 ReplaceTabControl.OnPresetSchemes）
    // ================================================================

    private void OnPresetSchemes(object? sender, EventArgs e)
    {
        var schemes = VNCharacterSchemeStore.Load();
        using var dlg = new VNCharacterSchemeForm(schemes);

        // 恢复上次的勾选状态
        if (_selectedSchemes.Count > 0)
        {
            var checkedNames = _selectedSchemes.Select(s => s.Name).ToHashSet();
            // 通过 Tag 属性匹配 — 但 VNCharacterSchemeForm 内部按名字重建树
            // 我们使用内部 _selectedSchemes 列表引用匹配
        }

        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _selectedSchemes = dlg.SelectedSchemes;

            // 用户可能修改了方案内容，持久化
            VNCharacterSchemeStore.Save(schemes);

            UpdatePresetSummary();
            StatusChanged?.Invoke(Loc.T("StatusVNPresetApplied",
                _selectedSchemes.Count,
                _selectedSchemes.Sum(s => s.Characters.Count)));
        }
    }

    private void UpdatePresetSummary()
    {
        _lblSelectedPresets.Text = _selectedSchemes.Count > 0
            ? Loc.T("StatusVNPresetSummary", _selectedSchemes.Count,
                 _selectedSchemes.Sum(s => s.Characters.Count))
            : Loc.T("StatusVNPresetNone");
    }

    // ================================================================
    //  执行处理
    // ================================================================

    private void OnBrowseFile(object? sender, EventArgs e)
    {
        using var dlg = ControlsHelper.CreateTextFileDialog(Loc.T("LabelVNFile"));
        if (dlg.ShowDialog(this) == DialogResult.OK)
            SelectFiles(dlg.FileNames);
    }

    private void OnProcess(object? sender, EventArgs e)
    {
        if (_lastDetection == null || _selectedFiles.Count == 0) return;

        try
        {
            _btnProcess.Enabled = false;
            _btnProcess.Text = Loc.T("StatusVNProcessing");
            StatusChanged?.Invoke(Loc.T("StatusVNProcessing"));

            // 从选中的预设方案收集角色名
            HashSet<string> characters = new();
            List<string> routeNames = new();
            List<string> scenePatterns = new();
            foreach (var scheme in _selectedSchemes)
            {
                foreach (var ch in scheme.Characters)
                    characters.Add(ch);
                routeNames.AddRange(scheme.RouteNames);
                if (!string.IsNullOrWhiteSpace(scheme.ScenePattern))
                    scenePatterns.Add(scheme.ScenePattern!);
            }

            int maxPara = (int)_numMaxPara.Value;
            bool doReformat = _rbAll.Checked || _rbReformat.Checked;
            bool doFixPunct = _rbAll.Checked || _rbFixPunct.Checked;

            int successCount = 0;
            string? singleContent = null;
            string? singleOutputPath = null;
            foreach (string path in _selectedFiles)
            {
                try
                {
                    var encoding = EncodingDetector.Detect(path);
                    string outputPath = PathHelper.GetProcessedPath(path);

                    string content = File.ReadAllText(path, encoding.Encoding);

                    if (doReformat)
                    {
                        // 合并所有方案提供的场景正则；无则用引擎通用模式
                        Regex? scenePattern = scenePatterns.Count > 0
                            ? new Regex(string.Join("|", scenePatterns))
                            : null;
                        var reformatter = new VNReformatterService(
                            characters: characters,
                            routeNames: routeNames,
                            maxParaLength: maxPara,
                            scenePattern: scenePattern);
                        content = reformatter.Reformat(content);
                    }

                    if (doFixPunct)
                    {
                        var fixer = new PunctFixerService();
                        content = fixer.Fix(content);
                    }

                    // 单文件场景：先预览，用户确认后保存；多文件直接写盘
                    if (_selectedFiles.Count == 1)
                    {
                        singleContent = content;
                        singleOutputPath = outputPath;
                    }
                    else
                    {
                        File.WriteAllText(outputPath, content, new UTF8Encoding(true));
                    }
                    successCount++;
                }
                catch (Exception ex)
                {
                    ErrorOccurred?.Invoke(Loc.T("MsgProcessFailed", Path.GetFileName(path), ex.Message));
                }
            }

            // 单文件预览确认
            if (_selectedFiles.Count == 1 && successCount == 1 && singleContent != null)
            {
                using var preview = new PreviewForm(singleContent, singleOutputPath!);
                if (preview.ShowDialog(this) == DialogResult.OK)
                    successCount = 1;
                else
                    successCount = 0; // 用户取消保存，视为未完成
            }

            string msg = successCount == _selectedFiles.Count
                ? Loc.T("StatusVNComplete", $"{successCount} file(s)")
                : Loc.T("StatusVNFailed", $"{successCount}/{_selectedFiles.Count}");
            _lblOutput.Text = msg;
            _lblOutput.ForeColor = successCount == _selectedFiles.Count ? Color.Green : Color.DarkOrange;
            StatusChanged?.Invoke(msg);

            if (successCount > 0 && MessageBox.Show(this,
                Loc.T("MsgVNDoneBody", Path.GetDirectoryName(_selectedFiles[0])!),
                Loc.T("MsgVNDoneTitle"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
            {
                ControlsHelper.RevealFolder(Path.GetDirectoryName(_selectedFiles[0])!);
            }
        }
        catch (Exception ex) { ErrorOccurred?.Invoke(Loc.T("StatusVNFailed", ex.Message)); }
        finally { _btnProcess.Enabled = true; _btnProcess.Text = Loc.T("BtnVNProcess"); }
    }

    // ================================================================
    //  Helpers
    // ================================================================

    private static Label MakeLabel(string text) => ControlsHelper.MakeLabel(text);

    private static ThemedFlatButton MakePrimaryButton(string text) => ControlsHelper.MakePrimaryButton(text);
}
