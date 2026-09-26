# TextTool GUI 接入 lint 实施计划

> **对于执行者：** 必需子技能：使用 SubagentDrivenDev（推荐）或 ExecutingPlans 逐任务实施此计划。步骤使用复选框（`- [ ]`）语法进行跟踪。
> 规格来源：`doc/specs/2026-09-26-texttool-lint-gui-design.md`（已批准）。引擎契约以 `doc/specs/2026-09-26-texttool-lint-design.md` 为准。本计划与规格冲突时以规格为准，并把冲突回报给用户。

**目标：** GUI 第 6 个页签接入与 `texttool lint` 同一引擎的只报不改检查；CLI 改为走共享 `LintRunner`。

**架构：** `LintRunner` 承接「多段文本 + 过滤 → `LintReportSet`」。CLI 继续做 argv / 读盘 / stdout / 退出码。`LintTabControl` 做拖放、粘贴、过滤控件和只读报告。不改 `Scan` / `Filter` / `Format` / 规则 JSON。

**技术栈：** .NET 8、C#、WinForms、xUnit。

## 问题陈述

去 AI 味只活在 CLI。打开 GUI 的人没有对等入口，只能去开终端。引擎已经在 Core 里，缺的是编排器与页签。

## 解决方案

抽出 `LintRunner`，CLI 与 GUI 共用。第 6 个页签对文件或粘贴文本调用它，用现有人读 `Format` 显示。只报不改。

## 用户故事

1. 作为桌面用户，我想在 GUI 里检查一份文稿的 AI 味，而不必打开终端。
2. 作为桌面用户，我想拖放或浏览一个或多个文件，以便一次看完各文件的报告。
3. 作为桌面用户，我想粘贴尚未存盘的一段文字，以便对着草稿检查。
4. 作为桌面用户，我想只看 `warn` 或只跑指定规则 Id，以便和 CLI 的 `--min-severity` / `--only` 对齐。
5. 作为桌面用户，我不想检查动作改写或另存文件。
6. 作为 CLI 用户，我希望 `texttool lint` 的退出码、`--json`、stdin 在重构后行为不变。
7. 作为切换了英文/繁中的用户，我希望按钮和标签被翻译，并接受报告正文仍是中文。

## 全局约束

- 目标框架：Core / CLI / Tests 为 `net8.0`；GUI 为 `net8.0-windows`。
- 行尾 LF、无 BOM（`.gitattributes` 强制）；禁用 emoji（用 `[OK]` / `[FAIL]` / `[NOTE]`）。
- 命名与风格以现有 Core 为准：file-scoped namespace `TextTool.Services`、私有字段 `_camelCase`、`PascalCase.cs`、XML 文档注释写中文。
- **局部变量与参数一律 camel_case**（`.editorconfig` 的 `locals_should_be_camelcase`）。`dotnet format --verify-no-changes` 会报 `IDE1006`。
- **量门禁退出码时不要接管道**：`cmd > log 2>&1; echo $?`。
- 不改 `AiToneLintService.Scan`、`LintReport.Filter`、`LintTextFormatter.Format`、`default_lint_rules.json` 的契约。
- `--min-severity` 合法值仍是精确的 `null` / `info` / `warn`（大小写敏感），不要「修好」成不敏感。
- 质量门禁（每个任务提交前跑）：`dotnet build TextTool.sln -c Release -warnaserror`、`dotnet test TextTool.Tests/TextTool.Tests.csproj -c Release`、`dotnet format TextTool.sln --verify-no-changes`。
- 提交信息中文，前缀 `feat:` / `fix:` / `docs:` / `test:`。正文另起一行写 `Co-Authored-By: Cursor <noreply@cursor.com>`。署名沿用本仓惯例（`git log -1 --format=%an <%ae>` 现为 `TwilightRainDev <122437146+TwilightRainDev@users.noreply.github.com>`），不要改 `git config`、不要用本机其它身份。
- 在分支 `feat/lint-gui` 上实施，不要提交到 `main`。
- 版本号只在任务 4 改：`Directory.Build.props` 的 `2.5.0` → `2.6.0`。

## 任务

### 任务 1：LintRunner + CLI 改调

**文件：**

- 创建：`TextTool.Core/LintRunner.cs`
- 测试：`TextTool.Tests/Services/LintRunnerTests.cs`
- 修改：`TextTool.Cli/Program.cs`（`RunLint` 主体）

**接口：**

- 消费：`LintRuleStore.Load()`、`AiToneLintService`、`LintReport.Filter`、`LintReportSet`
- 产出：`public static class LintRunner`，方法 `public static LintReportSet Run(IReadOnlyList<(string File, string Text)> inputs, IReadOnlyCollection<string>? onlyIds, string? minSeverity)`

- [ ] **步骤 1：编写失败的测试**

`TextTool.Tests/Services/LintRunnerTests.cs`：

```csharp
namespace TextTool.Tests.Services;

public class LintRunnerTests
{
    [Fact]
    public void Run_空输入即抛()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            LintRunner.Run(Array.Empty<(string, string)>(), null, null));
        Assert.Contains("至少一个输入", ex.Message);
    }

    [Fact]
    public void Run_空only集合即抛()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            LintRunner.Run(new[] { ("a.txt", "干净的一段话。") }, Array.Empty<string>(), null));
        Assert.Contains("--only", ex.Message);
    }

    [Fact]
    public void Run_未知Id即抛()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            LintRunner.Run(new[] { ("a.txt", "干净的一段话。") }, new[] { "NOPE" }, null));
        Assert.Contains("NOPE", ex.Message);
    }

    [Fact]
    public void Run_非法minSeverity即抛()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            LintRunner.Run(new[] { ("a.txt", "干净的一段话。") }, null, "WARN"));
        Assert.Contains("--min-severity", ex.Message);
    }

    [Fact]
    public void Run_多输入按顺序出报告且过滤生效()
    {
        var inputs = new (string File, string Text)[]
        {
            ("hot.txt", "此外，此外，此外。"),
            ("clean.txt", "干净的一段话。"),
        };

        var all = LintRunner.Run(inputs, null, null);
        Assert.Equal(2, all.Reports.Count);
        Assert.Equal("hot.txt", all.Reports[0].File);
        Assert.Equal("clean.txt", all.Reports[1].File);
        Assert.Contains(all.Reports[0].Hits, h => h.Id == "L2");

        var warnOnly = LintRunner.Run(inputs, null, "warn");
        Assert.DoesNotContain(warnOnly.Reports[0].Hits, h => h.Id == "L2");

        var onlyL2 = LintRunner.Run(inputs, new[] { "l2" }, null);
        Assert.All(onlyL2.Reports[0].Hits, h => Assert.Equal("L2", h.Id));
        Assert.Empty(onlyL2.Reports[0].Notes);
    }
}
```

- [ ] **步骤 2：运行测试以确认失败**

运行：`dotnet test TextTool.Tests/TextTool.Tests.csproj -c Release --filter "LintRunnerTests"`
预期：FAIL，提示 `LintRunner` 不存在

- [ ] **步骤 3：编写最小实现并改 CLI**

`TextTool.Core/LintRunner.cs`：

```csharp
namespace TextTool.Services;

/// <summary>
/// lint 编排：多段输入 + 过滤 → 报告集。不读盘、不写盘。
/// </summary>
public static class LintRunner
{
    public static LintReportSet Run(
        IReadOnlyList<(string File, string Text)> inputs,
        IReadOnlyCollection<string>? onlyIds,
        string? minSeverity)
    {
        if (inputs is null || inputs.Count == 0)
            throw new ArgumentException("lint 需要至少一个输入");
        if (minSeverity is not (null or "info" or "warn"))
            throw new ArgumentException("--min-severity 只接受 info 或 warn");
        if (onlyIds is not null && onlyIds.Count == 0)
            throw new ArgumentException("--only 需要至少一个规则 Id");
        if (onlyIds is not null)
        {
            var known = AiToneLintService.AllRuleIds();
            var unknown = onlyIds.Where(id => !known.Contains(id, StringComparer.OrdinalIgnoreCase)).ToList();
            if (unknown.Count > 0)
                throw new ArgumentException($"未知规则 Id：{string.Join(", ", unknown)}");
        }

        var service = new AiToneLintService(LintRuleStore.Load());
        var reportSet = new LintReportSet();
        foreach (var (file, text) in inputs)
            reportSet.Reports.Add(service.Scan(text, file).Filter(onlyIds, minSeverity));
        return reportSet;
    }
}
```

`RunLint` 在解析完 argv、确认 `files.Count > 0`、拆好 `onlyIds` 之后，删掉原先的 `minSeverity` / 空 only / 未知 Id / 手写 `Scan` 循环。改为：

```csharp
            var inputs = new List<(string File, string Text)>();
            foreach (var file in files)
            {
                string text = file == "-" ? ReadStdin() : ReadFileForLint(file);
                inputs.Add((file == "-" ? "(stdin)" : Path.GetFileName(file), text));
            }

            var reportSet = LintRunner.Run(inputs, onlyIds, minSeverity);

            if (!json)
            {
                foreach (var report in reportSet.Reports)
                    Console.WriteLine(LintTextFormatter.Format(report) + Environment.NewLine);
            }
            else
                Console.WriteLine(reportSet.ToJson());

            bool anyHit = reportSet.Reports.Any(r => r.Hits.Count > 0);
            return anyHit ? 1 : 0;
```

保留：UTF-8 `OutputEncoding`、`--help`、`files.Count == 0` 抛「lint 需要至少一个输入文件（- 表示 stdin）」、外层 catch 退 2、`ReadFileForLint` / `ReadStdin`。`onlyIds` 仍由 CLI 从 `--only` 字符串 `Split`；不要在 CLI 里再校验空数组或未知 Id。

- [ ] **步骤 4：运行测试以确认通过**

运行：`dotnet test TextTool.Tests/TextTool.Tests.csproj -c Release --filter "LintRunnerTests"`
预期：PASS（5 个）

再跑全量门禁。

- [ ] **步骤 5：提交**

```bash
git add TextTool.Core/LintRunner.cs TextTool.Tests/Services/LintRunnerTests.cs TextTool.Cli/Program.cs
git commit -m "feat: 抽出 LintRunner 供 CLI 与 GUI 共用" -m "Co-Authored-By: Cursor <noreply@cursor.com>"
```

### 任务 2：三个语言包补 lint 键

**文件：**

- 修改：`Localization/zh_CN.json`
- 修改：`Localization/zh_TW.json`
- 修改：`Localization/en_US.json`

**接口：**

- 消费：无
- 产出：下列键在三个文件里都存在，缺一不可（`Loc.T` 缺键会显示 `[Key]`）

在 `TabVN` 之后插入 `"TabLint"`。在各文件末尾对象闭合前追加其余键（保持合法 JSON、无尾逗号错误）。

简体（`zh_CN.json`）：

```json
  "TabLint": "AI 味检查",
  "LabelLintOnly": "只跑规则：",
  "LabelLintMinSeverity": "最低严重度：",
  "LintSeverityAll": "全部",
  "LintSeverityWarn": "warn 及以上",
  "BtnLint": "检查",
  "LabelLintPaste": "或粘贴文本：",
  "HintLint": "只报不改。可拖放文件，或在下方粘贴。",
  "StatusLintReady": "就绪 — 请拖放/选择文件，或粘贴文本",
  "StatusLintRunning": "正在检查...",
  "StatusLintDone": "检查完成：命中 {0} 处，统计项 {1} 条",
  "MsgLintNeedInput": "请选择至少一个文件，或粘贴文本。"
```

繁体（`zh_TW.json`）：

```json
  "TabLint": "AI 味檢查",
  "LabelLintOnly": "只跑規則：",
  "LabelLintMinSeverity": "最低嚴重度：",
  "LintSeverityAll": "全部",
  "LintSeverityWarn": "warn 及以上",
  "BtnLint": "檢查",
  "LabelLintPaste": "或貼上文字：",
  "HintLint": "只報不改。可拖放檔案，或在下方貼上。",
  "StatusLintReady": "就緒 — 請拖放/選擇檔案，或貼上文字",
  "StatusLintRunning": "正在檢查...",
  "StatusLintDone": "檢查完成：命中 {0} 處，統計項 {1} 條",
  "MsgLintNeedInput": "請選擇至少一個檔案，或貼上文字。"
```

英文（`en_US.json`）：

```json
  "TabLint": "AI-tone Lint",
  "LabelLintOnly": "Rule IDs:",
  "LabelLintMinSeverity": "Min severity:",
  "LintSeverityAll": "All",
  "LintSeverityWarn": "warn and above",
  "BtnLint": "Check",
  "LabelLintPaste": "Or paste text:",
  "HintLint": "Report only, never rewrite. Drop files, or paste below.",
  "StatusLintReady": "Ready — drop or choose files, or paste text",
  "StatusLintRunning": "Checking...",
  "StatusLintDone": "Check complete: {0} hit(s), {1} note(s)",
  "MsgLintNeedInput": "Choose at least one file, or paste some text."
```

本任务不改 `AboutDescription`（任务 4）。

- [ ] **步骤 1：写入三份 JSON**

用 JSON 解析器或 `python -c "import json; json.load(open(r'...'))"` 确认三份都能解析。三个文件的键集合在 `TabLint` 与上列 `LabelLint*` / `BtnLint` / `HintLint` / `StatusLint*` / `MsgLintNeedInput` 上必须一致。

- [ ] **步骤 2：提交**

```bash
git add Localization/zh_CN.json Localization/zh_TW.json Localization/en_US.json
git commit -m "feat: 为 lint 页签补齐三语键" -m "Co-Authored-By: Cursor <noreply@cursor.com>"
```

本任务无新测试。提交前仍跑全量门禁（确认 JSON 复制进输出目录后既有测试不受影响）。

### 任务 3：LintTabControl + MainForm

**文件：**

- 创建：`Controls/LintTabControl.cs`
- 修改：`MainForm.cs`

**接口：**

- 消费：`LintRunner.Run`、`LintTextFormatter.Format`、`EncodingDetector.DetectStrict`、`Loc.T`（任务 2 的键）、`ControlsHelper`、`IThemedTab`、`IStatusSource`
- 产出：第 6 个页签，插在 VN 与关于之间；`ApplyLocalization` 里关于页改为 `TabPages[5]`

- [ ] **步骤 1：实现 `LintTabControl`**

```csharp
using System.Text;
using TextTool.Localization;
using TextTool.Services;

namespace TextTool.Controls;

/// <summary>
/// AI 味检查页签：文件或粘贴 → LintRunner → 只读人读报告。不写文件。
/// </summary>
public sealed class LintTabControl : UserControl, IStatusSource, IThemedTab
{
    public event Action<string>? StatusChanged;
    public event Action<string>? ErrorOccurred;

    private Label _lblSourceFile = null!;
    private TextBox _txtFilePath = null!;
    private Button _btnBrowse = null!;
    private Label _lblOnly = null!;
    private TextBox _txtOnly = null!;
    private Label _lblSeverity = null!;
    private ComboBox _cmbSeverity = null!;
    private Label _lblHint = null!;
    private Button _btnLint = null!;
    private Label _lblPaste = null!;
    private TextBox _txtPaste = null!;
    private TextBox _txtReport = null!;

    private readonly List<string> _selectedFiles = new();

    public LintTabControl()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
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

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        _lblSourceFile = ControlsHelper.MakeLabel("Source:");
        _txtFilePath = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right, ReadOnly = true };
        _btnBrowse = new Button { Text = "Browse...", AutoSize = true };
        _btnBrowse.Click += OnBrowse;
        layout.Controls.Add(_lblSourceFile, 0, 0);
        layout.Controls.Add(_txtFilePath, 1, 0);
        layout.Controls.Add(_btnBrowse, 2, 0);

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        _lblOnly = ControlsHelper.MakeLabel("Only:");
        _txtOnly = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right, PlaceholderText = "S1,L1" };
        layout.Controls.Add(_lblOnly, 0, 1);
        layout.Controls.Add(_txtOnly, 1, 1);

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        _lblSeverity = ControlsHelper.MakeLabel("Severity:");
        _cmbSeverity = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Anchor = AnchorStyles.Left,
            Width = 180
        };
        _cmbSeverity.Items.Add("All");
        _cmbSeverity.Items.Add("warn");
        _cmbSeverity.SelectedIndex = 0;
        layout.Controls.Add(_lblSeverity, 0, 2);
        layout.Controls.Add(_cmbSeverity, 1, 2);

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        _lblHint = new Label { AutoSize = true, Anchor = AnchorStyles.Left };
        layout.SetColumnSpan(_lblHint, 3);
        layout.Controls.Add(_lblHint, 0, 3);

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        _btnLint = ControlsHelper.MakePrimaryButton("Check");
        _btnLint.Click += OnLint;
        layout.Controls.Add(_btnLint, 1, 4);

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
        _lblPaste = ControlsHelper.MakeLabel("Paste:");
        _txtPaste = new TextBox
        {
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            AcceptsReturn = true,
            Dock = DockStyle.Fill
        };
        layout.Controls.Add(_lblPaste, 0, 5);
        layout.Controls.Add(_txtPaste, 1, 5);
        layout.SetColumnSpan(_txtPaste, 2);

        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _txtReport = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 9f)
        };
        layout.SetColumnSpan(_txtReport, 3);
        layout.Controls.Add(_txtReport, 0, 6);

        AllowDrop = true;
        DragEnter += OnFileDragEnter;
        DragOver += OnFileDragOver;
        DragLeave += OnFileDragLeave;
        DragDrop += OnFileDragDrop;
        _txtFilePath.AllowDrop = true;
        _txtFilePath.DragEnter += OnFileDragEnter;
        _txtFilePath.DragOver += OnFileDragOver;
        _txtFilePath.DragLeave += OnFileDragLeave;
        _txtFilePath.DragDrop += OnFileDragDrop;

        Controls.Add(layout);
    }

    public void ApplyTheme() => ControlsHelper.ApplyTheme(this);

    public void ApplyLocalization()
    {
        _lblSourceFile.Text = Loc.T("LabelSourceFile");
        _btnBrowse.Text = Loc.T("BtnBrowse");
        _lblOnly.Text = Loc.T("LabelLintOnly");
        _lblSeverity.Text = Loc.T("LabelLintMinSeverity");
        int severityIndex = _cmbSeverity.SelectedIndex;
        _cmbSeverity.Items.Clear();
        _cmbSeverity.Items.Add(Loc.T("LintSeverityAll"));
        _cmbSeverity.Items.Add(Loc.T("LintSeverityWarn"));
        _cmbSeverity.SelectedIndex = severityIndex < 0 ? 0 : severityIndex;
        _btnLint.Text = Loc.T("BtnLint");
        _lblPaste.Text = Loc.T("LabelLintPaste");
        _lblHint.Text = Loc.T("HintLint");
        _txtReport.PlaceholderText = Loc.T("HintLint");
    }

    private void OnFileDragEnter(object? sender, DragEventArgs e) =>
        ControlsHelper.SetupFileDragEnter(e, _txtFilePath);

    private void OnFileDragOver(object? sender, DragEventArgs e) =>
        ControlsHelper.SetupFileDragOver(e);

    private void OnFileDragLeave(object? sender, EventArgs e) =>
        ControlsHelper.ResetFileDragLeave(_txtFilePath);

    private void OnFileDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            SelectFiles(files);
    }

    private void OnBrowse(object? sender, EventArgs e)
    {
        using var dlg = ControlsHelper.CreateTextFileDialog(Loc.T("TabLint"));
        if (dlg.ShowDialog(this) == DialogResult.OK)
            SelectFiles(dlg.FileNames);
    }

    private void SelectFiles(string[] files)
    {
        var valid = files.Where(File.Exists).ToList();
        if (valid.Count == 0) return;
        _selectedFiles.Clear();
        _selectedFiles.AddRange(valid);
        _txtFilePath.Text = valid.Count == 1 ? valid[0] : $"[{valid.Count}]";
        StatusChanged?.Invoke(Loc.T("StatusFilesSelected", valid.Count));
    }

    private async void OnLint(object? sender, EventArgs e)
    {
        var files = _selectedFiles.ToList();
        string paste = _txtPaste.Text;
        string onlyRaw = _txtOnly.Text;
        string? minSeverity = _cmbSeverity.SelectedIndex == 1 ? "warn" : null;

        if (files.Count == 0 && string.IsNullOrWhiteSpace(paste))
        {
            ErrorOccurred?.Invoke(Loc.T("MsgLintNeedInput"));
            return;
        }

        IReadOnlyCollection<string>? onlyIds = string.IsNullOrWhiteSpace(onlyRaw)
            ? null
            : onlyRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        _btnLint.Enabled = false;
        StatusChanged?.Invoke(Loc.T("StatusLintRunning"));
        try
        {
            var reportSet = await Task.Run(() =>
            {
                var inputs = new List<(string File, string Text)>();
                foreach (var path in files)
                {
                    if (!File.Exists(path))
                        throw new ArgumentException(Loc.T("MsgFileNotFound", path));
                    var encoding = EncodingDetector.DetectStrict(path);
                    inputs.Add((Path.GetFileName(path), File.ReadAllText(path, encoding.Encoding)));
                }
                if (!string.IsNullOrWhiteSpace(paste))
                    inputs.Add(("(paste)", paste));
                return LintRunner.Run(inputs, onlyIds, minSeverity);
            });

            var sb = new StringBuilder();
            int hits = 0, notes = 0;
            foreach (var report in reportSet.Reports)
            {
                if (sb.Length > 0) sb.AppendLine();
                sb.AppendLine(LintTextFormatter.Format(report));
                hits += report.Hits.Count;
                notes += report.Notes.Count;
            }
            _txtReport.Text = sb.ToString();
            StatusChanged?.Invoke(Loc.T("StatusLintDone", hits, notes));
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(ex.Message);
        }
        finally
        {
            _btnLint.Enabled = true;
        }
    }
}
```

`Task.Run` 里不要调用 `Loc.T` 以外、会碰 UI 控件的成员；上面已在 UI 线程捕获 `files` / `paste` / `onlyIds` / `minSeverity`。`Loc.T("MsgFileNotFound")` 只读静态字典，可在后台调用。

- [ ] **步骤 2：接入 `MainForm`**

1. 增加字段 `private LintTabControl _lintTab = null!;`
2. 类头注释改为「6 个 UserControl 页签」（现文写「4 个」是过时的）。
3. 在创建 `_vnTab` 并 `TabPages.Add` 之后、创建 `_aboutTab` 之前：

```csharp
        _lintTab = new LintTabControl();
        _lintTab.StatusChanged += SetStatus;
        _lintTab.ErrorOccurred += ShowError;
        _tabControl.TabPages.Add(CreateTabPage(_lintTab, "AI-tone Lint"));
```

4. `ApplyLocalization` 现为：

```csharp
        _tabControl.TabPages[0].Text = Loc.T("TabMerge");
        _tabControl.TabPages[1].Text = Loc.T("TabJoin");
        _tabControl.TabPages[2].Text = Loc.T("TabReplace");
        _tabControl.TabPages[3].Text = Loc.T("TabVN");
        _tabControl.TabPages[4].Text = Loc.T("TabAbout");
```

改为在 VN 之后插入 lint，关于改为下标 5：

```csharp
        _tabControl.TabPages[0].Text = Loc.T("TabMerge");
        _tabControl.TabPages[1].Text = Loc.T("TabJoin");
        _tabControl.TabPages[2].Text = Loc.T("TabReplace");
        _tabControl.TabPages[3].Text = Loc.T("TabVN");
        _tabControl.TabPages[4].Text = Loc.T("TabLint");
        _tabControl.TabPages[5].Text = Loc.T("TabAbout");
```

不要改窗口 `Size`。`ApplyToAllTabs` 已按 `IThemedTab` 遍历，新页签只要实现接口即可。

- [ ] **步骤 3：构建确认页签进解决方案**

运行：`dotnet build TextTool.sln -c Release -warnaserror`
预期：0 错误。SDK 会编译 `Controls/LintTabControl.cs`，不必改 csproj。

- [ ] **步骤 4：提交**

```bash
git add Controls/LintTabControl.cs MainForm.cs
git commit -m "feat: GUI 第 6 页签接入 AI 味检查" -m "Co-Authored-By: Cursor <noreply@cursor.com>"
```

提交前跑全量门禁。本任务无新单元测试。

### 任务 4：文档、关于页与版本 2.6.0

**文件：**

- 修改：`Directory.Build.props`
- 修改：`README.md`
- 修改：`doc/ArchitectureGuide.md`
- 修改：`doc/TECH-DEBT.md`
- 修改：`Localization/zh_CN.json`、`zh_TW.json`、`en_US.json` 的 `AboutDescription`

**接口：**

- 消费：任务 1–3 的落地形态
- 产出：无代码接口

- [ ] **步骤 1：版本**

`Directory.Build.props`：`<Version>2.5.0</Version>` → `<Version>2.6.0</Version>`。注释里的 `AssemblyVersion = 2.5.0.0` 改为 `2.6.0.0`。

- [ ] **步骤 2：README**

英文与中文功能表：lint 行从「CLI · lint」改为同时写页签与 CLI，例如英文 `**AI-tone Lint**` / 中文 `**AI 味检查**`，说明列写「GUI 页签与 `texttool lint` 共用引擎，只报不改」。项目结构树：`MainForm` 行改为 6 个页签；`Controls/` 补 `LintTabControl.cs`；`TextTool.Core/` 补 `LintRunner.cs`。结构树里的版本行 `v2.5.0` 改为 `v2.6.0`。不要改 git tag「最新 v2.4.4」那句，除非本任务提交时已经打了 `v2.6.0`（不要打 tag）。

- [ ] **步骤 3：ArchitectureGuide 与 TECH-DEBT**

`doc/ArchitectureGuide.md` §8 结构树补 `LintTabControl.cs` 与 `LintRunner.cs`。`doc/TECH-DEBT.md` §4 锚点：

```bash
grep -oP '(?<=<Version>)[0-9.]+(?=<)' Directory.Build.props   # 期望 2.6.0
grep -roE '\[(Fact|Theory)' TextTool.Tests --include=*.cs | wc -l
wc -l MainForm.cs
```

把版本期望改为 `2.6.0`，测试数与 `MainForm` 行数改成这三条命令的实测值（不要沿用 183 / 207）。

- [ ] **步骤 4：关于页描述**

`AboutDescription` 三语各补检查能力，保持换行风格：

- zh_CN：在现有句末前加上「、AI 味检查」或等价，不引入 emoji
- zh_TW：对应「、AI 味檢查」
- en_US：在列举中加上 `AI-tone Lint`

- [ ] **步骤 5：门禁并提交**

```bash
dotnet build TextTool.sln -c Release -warnaserror
dotnet test TextTool.Tests/TextTool.Tests.csproj -c Release
dotnet format TextTool.sln --verify-no-changes
git add Directory.Build.props README.md doc/ArchitectureGuide.md doc/TECH-DEBT.md \
        Localization/zh_CN.json Localization/zh_TW.json Localization/en_US.json
git commit -m "docs: GUI lint 文档与版本升至 2.6.0" -m "Co-Authored-By: Cursor <noreply@cursor.com>"
```

## 决策文档

- 编排进 Core 而不是 GUI：CLI 与 GUI 必须共用校验，避免 `--only` 空集再漂一次。
- 粘贴输入的 `File` 固定为 `(paste)`，与 CLI `(stdin)` 同形，不随 `Loc` 变。
- 报告正文不翻译：规则 JSON 与 `Format` 是中文；本轮只译铬。
- 最低严重度只有「全部 / warn」，不提供第三档 `info`。
- 页签插在关于之前：关于保持末页。
- 不给 GUI 写测试：对齐 ADR-007「测 Core」与现有页签先例。
- 不顺手改 `--min-severity` 大小写，也不删 D-5 兜底。

## 测试决策

- 只测 `LintRunner` 的外部行为：空输入、空 only、未知 Id、非法 severity、多输入顺序、`--min-severity warn` 去掉 L2、`--only l2` 只留 L2 且 Notes 空。
- 先例：`TextTool.Tests/Services/LintReportTests.cs`（xUnit、中文方法名、`GlobalUsings`）。
- 不测 `LintTabControl`，不在本计划加 WinForms 测试项目。
- CLI 不加单元测试；行为由 `LintRunner` 测试 + 既有 lint 测试守住。

## 范围之外

- `--json` 进 GUI、`--fix`、目录递归、改规则数据、D-5、主题遍历器测试、给 `Format` 做 i18n、打 git tag / 发 GitHub Release。

## 进一步说明

- 实施在 `feat/lint-gui`。规格是页签行为的权威；引擎字段以上一轮 lint 规格为准。
- 人工点验（任务 3 之后、最终评审之前由控制器做，不写入实施者任务）：拖一个含「此外」三次的 txt，应见 L2；空页点检查应弹 `MsgLintNeedInput`；粘贴同样文本应见 `(paste)`。
