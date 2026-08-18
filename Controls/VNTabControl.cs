using System.Text;
using System.Text.RegularExpressions;
using TextTool.Localization;
using TextTool.Services;

namespace TextTool.Controls;

/// <summary>
/// "视觉小说" 页签：将视觉小说脚本从固定宽度硬换行排版为自然段落，
/// 并可选地补全对话标点。角色设定通过预设方案管理（对标标点替换的方案系统）。
/// </summary>
public sealed class VNTabControl : UserControl, IStatusSource, IThemedTab
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
    private CancellationTokenSource? _cts;   // 批处理取消令牌

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
        catch
        {
            // 检测失败时清空状态，避免以错误的编码继续处理
            _lastDetection = null;
            _lblEncoding.Text = Loc.T("EncodingNotSelected");
            _lblEncoding.ForeColor = ThemeManager.MutedFg;
        }

        _btnProcess.Enabled = true;
        StatusChanged?.Invoke(Loc.T("StatusFilesSelected", valid.Count));
    }

    // ================================================================
    //  预设角色方案（对标 ReplaceTabControl.OnPresetSchemes）
    // ================================================================

    private void OnPresetSchemes(object? sender, EventArgs e)
    {
        List<VNCharacterScheme> schemes;
        try
        {
            schemes = VNCharacterSchemeStore.Load();
        }
        catch (InvalidDataException ex)
        {
            MessageBox.Show(ex.Message, "", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
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

            UpdatePresetSummary();
            StatusChanged?.Invoke(Loc.T("StatusVNPresetApplied",
                _selectedSchemes.Count,
                _selectedSchemes.Sum(s => s.Characters.Count)));
        }

        // 无论 OK 还是取消都持久化：用户可能在内层编辑对话框中修改了方案内容，
        // 取消外层表单也不应丢失这些修改（与 ReplaceTabControl 行为对齐）。
        VNCharacterSchemeStore.Save(schemes);
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

    private async void OnProcess(object? sender, EventArgs e)
    {
        if (_lastDetection == null || _selectedFiles.Count == 0) return;

        // 运行中再点按钮 = 取消
        if (_cts != null)
        {
            _cts.Cancel();
            _btnProcess.Enabled = false;
            _btnProcess.Text = Loc.T("StatusCancelling");
            StatusChanged?.Invoke(Loc.T("StatusCancelling"));
            return;
        }

        // UI 线程捕获全部选项与方案快照
        var files = _selectedFiles.ToArray();
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

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _btnProcess.Enabled = false;
        _btnProcess.Text = Loc.T("StatusVNProcessing");

        try
        {
            // 场景标记正则：经 RegexGuard 带超时构造，防止配置来源模式触发 ReDoS
            Regex? scenePattern;
            try
            {
                scenePattern = scenePatterns.Count > 0
                    ? RegexGuard.Create(string.Join("|", scenePatterns))
                    : null;
            }
            catch (ArgumentException ex)
            {
                throw new InvalidOperationException($"场景标记正则非法（方案文件损坏或被篡改）：{ex.Message}");
            }

            (int Success, bool Cancelled) result;

            if (files.Length == 1)
            {
                // 单文件：后台处理并用返回值回传内容，避免跨线程共享字段（可见性竞态）
                string path = files[0];
                var encoding = EncodingDetector.DetectStrict(path);
                string outputPath = PathHelper.GetProcessedPath(path);

                string content = await Task.Run(() =>
                {
                    string c = File.ReadAllText(path, encoding.Encoding);
                    if (doReformat)
                        c = ReformatContent(c, characters, routeNames, maxPara, scenePattern);
                    if (doFixPunct)
                        c = new PunctFixerService().Fix(c);
                    return c;
                });

                if (_cts.IsCancellationRequested)
                {
                    result = (0, false);
                }
                else
                {
                    using var preview = new PreviewForm(content, outputPath);
                    result = preview.ShowDialog(this) == DialogResult.OK ? (1, false) : (0, false);
                }
            }
            else
            {
                var progress = new Progress<(int Done, string Error)>(
                    p =>
                    {
                        if (string.IsNullOrEmpty(p.Error))
                            StatusChanged?.Invoke(Loc.T("StatusBatchProgress", p.Done, files.Length));
                        else
                            ErrorOccurred?.Invoke(Loc.T("MsgProcessFailed", Path.GetFileName(files[p.Done - 1]), p.Error));
                    });

                result = await ControlsHelper.RunBatchAsync(
                    files,
                    (path, _) =>
                    {
                        var encoding = EncodingDetector.DetectStrict(path);
                        string outputPath = PathHelper.GetProcessedPath(path);
                        string content = File.ReadAllText(path, encoding.Encoding);
                        if (doReformat)
                            content = ReformatContent(content, characters, routeNames, maxPara, scenePattern);
                        if (doFixPunct)
                            content = new PunctFixerService().Fix(content);
                        AtomicFile.WriteAllText(outputPath, content, new UTF8Encoding(true));
                    },
                    progress,
                    token);
            }

            bool cancelled = _cts.IsCancellationRequested;

            string msg;
            if (cancelled)
            {
                msg = Loc.T("StatusBatchCancelled", result.Success);
                _lblOutput.ForeColor = Color.DarkOrange;
            }
            else
            {
                msg = result.Success == files.Length
                    ? Loc.T("StatusVNComplete", $"{result.Success} file(s)")
                    : Loc.T("StatusVNFailed", $"{result.Success}/{files.Length}");
                _lblOutput.ForeColor = result.Success == files.Length ? Color.Green : Color.DarkOrange;
            }
            _lblOutput.Text = msg;
            StatusChanged?.Invoke(msg);

            if (result.Success > 0 && !cancelled && MessageBox.Show(this,
                Loc.T("MsgVNDoneBody", Path.GetDirectoryName(files[0])!),
                Loc.T("MsgVNDoneTitle"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
            {
                ControlsHelper.RevealFolder(Path.GetDirectoryName(files[0])!);
            }
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(Loc.T("StatusVNFailed", ex.Message));
        }
        finally
        {
            _btnProcess.Enabled = true;
            _btnProcess.Text = Loc.T("BtnVNProcess");
            _cts?.Dispose();
            _cts = null;
        }
    }

    // ================================================================
    //  Helpers
    // ================================================================

    private static Label MakeLabel(string text) => ControlsHelper.MakeLabel(text);

    /// <summary>执行排版；场景正则超时（ReDoS 防御）时翻译为明确的中文错误。</summary>
    private static string ReformatContent(
        string content, HashSet<string> characters, List<string> routeNames, int maxPara, Regex? scenePattern)
    {
        try
        {
            return new VNReformatterService(
                characters: characters,
                routeNames: routeNames,
                maxParaLength: maxPara,
                scenePattern: scenePattern).Reformat(content);
        }
        catch (RegexMatchTimeoutException)
        {
            throw new InvalidOperationException(
                "场景标记正则执行超时——所选方案的规则过于复杂，请编辑方案简化后重试");
        }
    }

    private static ThemedFlatButton MakePrimaryButton(string text) => ControlsHelper.MakePrimaryButton(text);
}
