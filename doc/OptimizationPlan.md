# TextTool 代码库优化实施方案

> 基于 Simplify 技能对 `H:\work_zone\Code\TextTool` 代码库的全面审查  
> 审查日期：2026-07-19 | 审查维度：4 Agents（复用/简化/效率/抽象层次）  
> 代码基：~2000 LOC C# WinForms + ~54 测试用例 + 7 ADR 文档

---

## 目录

1. [全景评估](#1-全景评估)
2. [高阶重构（H1）— 架构级优化](#2-高阶重构h1架构级优化)
3. [中阶优化（H2）— 模块级收束](#3-中阶优化h2模块级收束)
4. [低阶清理（H3）— 行级精炼](#4-低阶清理h3行级精炼)
5. [测试配套](#5-测试配套)
6. [流程维度](#6-流程维度)
7. [风险维度与回退预案](#7-风险维度与回退预案)
8. [维护维度](#8-维护维度)
9. [实施路线图](#9-实施路线图)

---

## 1. 全景评估

### 1.1 代码质量画像

| 维度 | 评价 |
|------|------|
| **架构清晰度** | 4/5 良好。三层分离（Controls/Services/Localization），ADR 文档完备 |
| **模块内聚性** | 4/5 单文件单职责贯彻到位（除 ReplaceTabControl 略重） |
| **代码重复率** | 3/5 中等。约 400 LOC 存在重复模式（~20%），集中在中观层次 |
| **测试覆盖** | 5/5 优秀。54 测试覆盖所有 Service 边界情况 |
| **可维护性** | 4/5 文档与代码同步好，但主题系统手写冗余较多 |

### 1.2 发现汇总（去重后）

| 优先级 | 发现 | 涉及文件 | 预估工作量 |
|--------|------|----------|-----------|
| **H1** | 主题应用策略不统一：6 种不同的 ApplyTheme 实现 | 全部 Controls + Forms | 半天 |
| **H1** | JSON 持久化模式重复：ReplaceRuleStore / ReplaceSchemeStore | PunctuationReplacer.cs, ReplaceScheme.cs | 2 小时 |
| **H1** | 配置文件读写分散：app_config.json 被 3 处独立解析 | ThemeManager.cs, Strings.cs, AboutTabControl.cs | 2 小时 |
| **H2** | 拖放事件处理 2 次重复 | MergeTabControl, ReplaceTabControl | 1 小时 |
| **H2** | 批量处理编排 2 次重复（~100 LOC 骨架一致） | MergeTabControl, ReplaceTabControl | 1.5 小时 |
| **H2** | 标点替换性能瓶颈：O(lines×rules) 中间字符串 | PunctuationReplacer.cs | 2 小时 |
| **H2** | 内置替换方案 280 行内联数据混在代码中 | ReplaceScheme.cs | 1 小时 |
| **H2** | StatusChanged/ErrorOccurred 事件无共享接口 | 4 × Controls + MainForm | 1 小时 |
| **H3** | PreviewForm 字符串往返 | PreviewForm.cs | 30 分钟 |
| **H3** | _Processed 路径生成 2 处重复 | LineMerger.cs, ReplaceTabControl.cs | 30 分钟 |
| **H3** | 死代码 SelectSingleFile / 冗余条件 | MergeTabControl, FileJoiner | 15 分钟 |
| **H3** | UTF8Encoding 未缓存 | ProcessingPipeline.cs | 15 分钟 |
| **H3** | EncodingDetector 显示字符串未国际化 | EncodingDetector.cs | 30 分钟 |

---

## 2. 高阶重构（H1）— 架构级优化

### H1-1: 集中式主题遍历器

**现状问题：** 6 个控件/窗体各自实现了 `ApplyTheme()`，使用了 3 种不同策略——逐控件赋值、混合策略、递归分发。新增控件需要重新实现同一套逻辑。

**实施方案：**

在 `ControlsHelper.cs` 中添加泛用的 `ApplyTheme(Control root)` 方法：

```csharp
// ControlsHelper.cs 新增
public static void ApplyTheme(Control root)
{
    ApplyThemeToControlTree(root);
}

private static void ApplyThemeToControlTree(Control ctl)
{
    switch (ctl)
    {
        case Label lbl:
            lbl.ForeColor = lbl == _specialDimmed ? ThemeManager.MutedFg : ThemeManager.Fg;
            break;
        case Button btn:
            btn.BackColor = ControlsHelper.ButtonBg;
            btn.ForeColor = ControlsHelper.ButtonFg;
            btn.FlatAppearance.MouseOverBackColor = ControlsHelper.ButtonBg;
            break;
        case TextBox txt:
        case ListBox lb:
        case ComboBox cmb:
        case NumericUpDown nud:
            ctl.BackColor = ThemeManager.ControlBg;
            ctl.ForeColor = ThemeManager.Fg;
            break;
        case CheckBox chk:
        case RadioButton rb:
            ctl.ForeColor = ThemeManager.Fg;
            break;
        case LinkLabel ll:
            ll.LinkColor = ThemeManager.IsDarkMode ? Color.LightBlue : Color.SteelBlue;
            ll.ActiveLinkColor = ThemeManager.IsDarkMode ? Color.DeepSkyBlue : Color.DarkBlue;
            break;
        case SplitContainer sc:
            sc.BackColor = ThemeManager.IsDarkMode ? ThemeManager.DarkControlBg : SystemColors.Control;
            ApplyThemeToControlTree(sc.Panel1);
            ApplyThemeToControlTree(sc.Panel2);
            return;
        case TableLayoutPanel tlp:
        case FlowLayoutPanel flp:
        case Panel p:
            ctl.BackColor = ctl is Panel ? ThemeManager.Bg : ctl.BackColor;  // Panel 设 Bg, 其他容器保留
            break;
    }

    if (ctl.HasChildren)
        foreach (Control child in ctl.Controls)
            ApplyThemeToControlTree(child);
}
```

然后每个控件的 `ApplyTheme()` 简化为：

```csharp
public void ApplyTheme()
{
    ControlsHelper.ApplyTheme(this);
    // 仅保留特殊覆盖（如 _chkOverwrite.ForeColor = Color.OrangeRed; _lblEncoding.ForeColor = ThemeManager.MutedFg;）
}
```

**预期效果：** 消除 ~150 行重复分发代码，新增控件 0 成本接入主题。

---

### H1-2: 泛型 JSON 文件存储

**现状问题：** `ReplaceRuleStore` 和 `ReplaceSchemeStore` 结构完全一致——`Path` 属性、`Load()` 的 try-catch-File.Exists-ReadAllText-Deserialize 模式、`Save()` 的 WriteIndented+WriteAllText 模式——约 50 行完全重复样板代码。

**实施方案：**

```csharp
// Services/JsonFileStore.cs（新建）
public static class JsonFileStore
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static List<T> Load<T>(string fileName, Func<List<T>>? fallback = null)
    {
        string path = Path.Combine(AppContext.BaseDirectory, fileName);
        try
        {
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                var items = JsonSerializer.Deserialize<List<T>>(json);
                if (items != null) return items;
            }
        }
        catch { /* 文件损坏则走 fallback */ }
        return fallback?.Invoke() ?? new List<T>();
    }

    public static void Save<T>(string fileName, List<T> items)
    {
        string path = Path.Combine(AppContext.BaseDirectory, fileName);
        string json = JsonSerializer.Serialize(items, Indented);
        File.WriteAllText(path, json, new UTF8Encoding(false));
    }
}
```

`ReplaceRuleStore` 和 `ReplaceSchemeStore` 各缩为 1 行委托：

```csharp
// PunctuationReplacer.cs
public static class ReplaceRuleStore
{
    public static List<ReplaceRule> Load() => JsonFileStore.Load<ReplaceRule>("replace_rules.json");
    public static void Save(List<ReplaceRule> rules) => JsonFileStore.Save("replace_rules.json", rules);
}
```

**预期效果：** 消除 ~50 行重复模板代码，统一序列化行为，新增 JSON 文件只需一行。

---

### H1-3: 统一配置管理

**现状问题：** `app_config.json` 在启动时被 `ThemeManager.Init()` 和 `Loc.LoadSavedLanguage()` 分别读取（2 次 JSON 解析），在导入配置时又被 `AboutTabControl.LoadLanguageFromConfig()` 第三次读取。三种解析策略、三处独立 try-catch。

**实施方案：**

1. **消除 Loc.LoadSavedLanguage()**——将其逻辑并入 `ThemeManager.Init()`，`ThemeManager` 同时存储 `darkMode` 和 `language`：

   ```csharp
   // ThemeManager.cs
   public static string? CurrentLanguage { get; private set; }
   
   public static void Init()
   {
       // 统一读取一次
       try
       {
           if (File.Exists(ConfigPath))
           {
               var json = File.ReadAllText(ConfigPath, Encoding.UTF8);
               var doc = JsonDocument.Parse(json);
               if (doc.RootElement.TryGetProperty("darkMode", out var dm))
                   IsDarkMode = dm.GetBoolean();
               if (doc.RootElement.TryGetProperty("language", out var lang))
                   CurrentLanguage = lang.GetString();
           }
       }
       catch { }
   }
   ```

2. **Loc.Init() 直接从 ThemeManager.CurrentLanguage 获取**，移除对 ThemeManager.InitialLanguage 的依赖和 LoadSavedLanguage() 方法。

3. **AboutTabControl.LoadLanguageFromConfig()** 复用 `ThemeManager.Reload()` 或直接使用 `File.ReadAllText` + 内联解析（消除一个类文件中的配置知识）。

**预期效果：** 消除 1 个完整方法和 3 处重复 I/O，启动速度微幅提升。

---

## 3. 中阶优化（H2）— 模块级收束

### H2-1: 拖放事件统一

**现状问题：** `MergeTabControl` 和 `ReplaceTabControl` 各有一套 ~40 行的拖放事件处理（OnFileDragEnter/Over/Leave/Drop），逻辑完全一致。

**实施方案：**

```csharp
// ControlsHelper.cs 新增
public static void SetupFileDragEnter(DragEventArgs e, Control target)
{
    e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true
        ? DragDropEffects.Copy : DragDropEffects.None;
    if (e.Effect == DragDropEffects.Copy)
        target.BackColor = Color.LemonChiffon;
}

public static void SetupFileDragOver(DragEventArgs e)
{
    if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
        e.Effect = DragDropEffects.Copy;
}

public static void ResetFileDragLeave(Control target)
{
    target.BackColor = ThemeManager.ControlBg;  // 主题感知
}
```

各 Tab 中的事件处理简化为一行委托，消除 ~40 行重复。

---

### H2-2: 批量处理编排抽象

**现状问题：** `MergeTabControl.OnProcess` 和 `ReplaceTabControl.OnReplaceProcess` 的骨架完全一致——禁用按钮→遍历→try-catch→汇总消息→MessageBox→reveal folder。~100 LOC 重复。

**实施方案：**

提取共享方法到 `ControlsHelper` 或新增 `BatchProcessor` 基类：

```csharp
public static class BatchProcessor
{
    public static async Task<BatchResult> ProcessFilesAsync(
        List<string> files,
        Button processButton,
        Label outputLabel,
        Func<string, Task> processFile,
        string processingKey,
        string successKey,
        string partialKey)
    {
        int successCount = 0;
        processButton.Enabled = false;
        processButton.Text = Loc.T(processingKey);

        foreach (string path in files)
        {
            try
            {
                await processFile(path);
                successCount++;
            }
            catch (Exception ex)
            {
                // 错误回调
            }
        }

        processButton.Enabled = true;
        processButton.Text = Loc.T(processingKey);
        
        string msg = successCount == files.Count
            ? Loc.T(successKey, successCount)
            : Loc.T(partialKey, successCount, files.Count);
        outputLabel.Text = msg;
        outputLabel.ForeColor = successCount == files.Count ? Color.Green : Color.DarkOrange;
        
        return new BatchResult(successCount, files.Count);
    }
}
```

---

### H2-3: 标点替换性能优化

**现状问题：** `PunctuationReplacer.Apply()` 对每条规则调用 `string.Replace()`，产生 N 个中间字符串。在 10,000 行 × 100 规则的批量处理中可能产生 1M 次字符串分配。

**实施方案：**

将按行替换改为整体替换或使用 StringBuilder：

```csharp
// 方案 A：合并文本后一次性替换（减少分配次数）
public static string Apply(string text, List<ReplaceRule> rules)
{
    // 非破坏：保持逐条替换语义（顺序敏感），但用 StringBuilder
    var sb = new StringBuilder(text);
    foreach (var rule in rules)
    {
        if (string.IsNullOrEmpty(rule.Find)) continue;
        sb.Replace(rule.Find, rule.Replace);
    }
    return sb.ToString();
}
```

若需进一步优化，可偏移排序 + 范围替换（但当前场景下 StringBuilder.Replace 已足够）。

匹配 ProcessingPipeline 中的调用——改为在线级别调用 `StringBuilder.Replace`，或在应用替换前将所有行 Join 成一个 StringBuilder。

---

### H2-4: 内置方案迁移至 JSON 资源

**现状问题：** `ReplaceSchemeStore.GetDefaultSchemes()` 包含 ~280 行内联 C# 数据（9 套预置方案）。修改方案需要重新编译。

**实施方案：**

新建 `default_schemes.json`（EmbeddedResource），将 ReplaceScheme.cs 的 GetDefaultSchemes 改为从资源加载：

```csharp
// ReplaceScheme.cs
public static List<ReplaceScheme> GetDefaultSchemes()
{
    var assembly = Assembly.GetExecutingAssembly();
    using var stream = assembly.GetManifestResourceStream("TextTool.default_schemes.json");
    if (stream == null) return new List<ReplaceScheme>();
    using var reader = new StreamReader(stream, Encoding.UTF8);
    return JsonSerializer.Deserialize<List<ReplaceScheme>>(reader.ReadToEnd()) ?? new();
}
```

同时修改 `TextTool.csproj` 添加 `<EmbeddedResource Include="default_schemes.json" />`。

---

### H2-5: IStatusSource 接口

**现状问题：** 4 个 UserControl 各自独立声明 `event Action<string>? StatusChanged` 和 `event Action<string>? ErrorOccurred`，无编译器强制的要求。

**实施方案：**

```csharp
// Services/IStatusSource.cs（新建）
public interface IStatusSource
{
    event Action<string>? StatusChanged;
    event Action<string>? ErrorOccurred;
}
```

4 个 Tab 控件实现此接口，MainForm 订阅时可泛化处理。后续新增 Tab 只需 `: UserControl, IStatusSource`。

---

## 4. 低阶清理（H3）— 行级精炼

### 快速修复清单（每项估时 ≤ 30 分钟）

| 编号 | 文件位置 | 问题 | 修复方案 |
|------|----------|------|----------|
| H3-1 | `PreviewForm.cs:35,93-95` | 字符串往返 | 持有 `_lines` 字段，OnSave 直接 `File.WriteAllLines` |
| H3-2 | `LineMerger.cs:42` / `ReplaceTabControl.cs:478-481` | `_Processed` 路径 2 处 | 提取 `PathHelper.GetProcessedPath(inputPath)` |
| H3-3 | `MergeTabControl.cs:344-347` | 死代码 `SelectSingleFile` | 删除 4 行 |
| H3-4 | `FileJoiner.cs:29` | `content.EndsWith("\r\n")` 冗余 | 仅保留 `!content.EndsWith("\n")` |
| H3-5 | `ProcessingPipeline.cs:95` | `new UTF8Encoding(true)` 每次调用 | 提取为 `static readonly` 字段 |
| H3-6 | `SchemeSelectionForm.cs:202-218` | `_isChecking` 标志 vs 取消订阅 | 改为临时取消订阅事件 |
| H3-7 | `SchemeSelectionForm.cs:258-261` | `EditSchemeDialog` 自我赋值 | 删除冗余复制循环 |
| H3-8 | `ReplaceTabControl.cs:372` | `detection.DisplayName` 未国际化 | 改为 Loc.T("EncodingUtf8") + Loc.T("EncodingGbk") |
| H3-9 | `MergeTabControl.cs:355` | 未使用 `ControlsHelper.CreateTextFileDialog` | 替换内联 OpenFileDialog |
| H3-10 | `MainForm.cs:122-128` | `ApplyTheme()` 中 3 个 Tab 重复调用 | 遍历 `_tabControl.TabPages` 统一派发 |
### 低阶用户界面优化

危险覆盖模式在中文里面是六个字符，与上面按钮的隐性命名约束--八个字符（动-宾结构）不一致。
修改后逻辑结构如下：
后处理
├── 修复中文截断断段
├── 修复标点截断断段
├── 应用标点替换规则
├── 不合并行尾部标点
├── 去除位于行首逗号
└── 开启危险覆盖模式

---

## 5. 测试配套

### 5.1 现有测试覆盖（54 测试）

```text
LineMerger        → 7 测试    完整
CjkParagraphMerger → 7 测试   完整
PunctTruncationMerger → 6 测试 完整
PunctuationReplacer → 7 测试   完整
TextUtils          → 10 测试   完整
EncodingDetector   → 8 测试    完整
ProcessingPipeline → 4 测试    集成测试
FileJoiner         → 5 测试    完整
```

### 5.2 优化后新增/更新测试

| 优化项 | 新增测试内容 | 测试数 |
|--------|-------------|--------|
| H1-1 主题遍历器 | 遍历涵盖所有控件类型（Label/Button/TextBox/CheckBox/RadioButton/ComboBox） | 3 |
| H1-2 JsonFileStore | 加载/保存/文件损坏回退/并发写入 | 4 |
| H2-3 StringBuilder.Replace | 与旧 string.Replace 行为一致验证 | 2 |
| H2-4 嵌入式 JSON 默认方案 | 加载/格式正确性 | 1 |
| H3-5 UTF8Encoding 静态字段 | 编码行为一致性 | 1 |

**注意：** H1-1 的主题测试需要创建真实的 WinForms 控件实例（`new Label() { ... }`），在 `net7.0-windows` 目标框架下可行。测试夹具不需要显示窗口。

---

## 6. 流程维度

### 6.1 当前流程分析

| 阶段 | 当前状态 | 问题 |
|------|----------|------|
| **构建** | `dotnet build` + 隐式 restore | OK |
| **测试** | `dotnet test` 通过 xUnit | OK，耗时 < 300ms |
| **CI** | GitHub Actions（build-test.yml） | 已配置 |
| **发布** | `dotnet publish -c Release` + 手工拷贝 exe | 待改进：无自动发布脚本 |
| **代码审查** | 无 PR 模板，无自动化审查 | 待改进：无审查清单 |
| **版本管理** | Directory.Build.props + README.md 版本列表 | 待改进：README 版本表需手动同步 |
| **编码检查** | 无 .editorconfig，无 StyleCop/Analyzer | 待改进：代码风格完全靠自觉 |

### 6.2 流程优化建议

**P1: 纳入 .editorconfig + Roslyn Analyzer**

```xml
<!-- Directory.Build.props 新增 -->
<PropertyGroup>
  <EnableNETAnalyzers>true</EnableNETAnalyzers>
  <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
  <AnalysisMode>All</AnalysisMode>
</PropertyGroup>
```

**P2: 发布自动化脚本**

添加 `scripts/publish.ps1`：

```powershell
# 构建 + 测试 + 发布 + 版本号打标签
dotnet test .\TextTool.Tests\TextTool.Tests.csproj -c Release
if ($LASTEXITCODE -ne 0) { exit 1 }
dotnet publish .\TextTool.csproj -c Release -o dist
```

**P3: README 版本表自动化**

```text
问题：README.md 中手写版本历史表，与 Directory.Build.props 的 <Version> 不同步。
方案：发布时从 Directory.Build.props 读取版本号，追加到 README 的版本表。
       或删除 README 中的版本历史表，改为指向 git tag 列表。
```

**P4: 提交流程检查清单**

在 `.claude/hooks/pre-commit` 中配置：

```text
- dotnet build 无警告
- dotnet test 全部通过
- 无 .Only() 或 .Skip() 测试方法残留
```

---

## 7. 风险维度与回退预案

### 7.1 优化风险矩阵

| 优化项 | 风险等级 | 潜在问题 | 回退策略 |
|--------|---------|----------|----------|
| **H1-1 主题遍历器** | 中 | 递归遍历可能遗漏新控件类型；特殊配色覆盖被覆盖 | 逐个合并提交，每个 Tab 验证后推进；保留旧的 ApplyTheme 方法作为 fallback |
| **H1-2 JsonFileStore** | 低 | 泛型序列化行为与现有一致 | 与旧实现并行运行一个版本，比对输出 JSON 一致后再删除旧代码 |
| **H1-3 统一配置** | 中 | ThemeManager 初始化为 null 时 Loc 找不到语言 | 确保 CurrentLanguage 总有 fallback="zh_CN"；若旧版配置缺少 language 字段，自动使用默认值 |
| **H2-3 StringBuilder.Replace** | 中 | StringBuilder.Replace 语义差异 | 对现有规则集运行 A/B 比较测试，确保输出完全一致 |
| **H2-4 方案迁移至 JSON** | 低 | 嵌入式资源路径错误 | 运行时检查：资源加载失败→回退到旧版硬编码 GetDefaultSchemes() |
| **H2-5 事件接口** | 低 | MainForm 订阅模式改变 | 接口添加后保留旧事件声明一个版本过渡 |
| **并行化（H2 扩展）** | 高 | 多线程 UI 操作 + 文件竞争 | **暂不建议实施**——当前场景文件数量不大，收益有限。如需实施，使用 `Parallel.ForEach` + 每个 iteration 的 try-catch 隔离 |
| **H3 全部** | 低 | 行级修改不影响功能 | 每个修改独立提交，测试通过后合并 |

### 7.2 实施顺序建议

```text
Phase 1 ─ 安全先行
  ├── H3-3 删除死代码（零风险）
  ├── H3-4 简化冗余条件（零风险）
  ├── H3-6/7 SchemeSelectionForm 简化（零风险）
  └── H3-5 UTF8Encoding 静态化（零风险）
↓
Phase 2 ─ 模式提取
  ├── H1-2 JsonFileStore（并行验证后切换）
  ├── H2-5 IStatusSource 接口（向后兼容）
  └── H3-2 PathHelper 提取
↓
Phase 3 ─ 主题统一
  ├── H1-1 集中主题遍历器
  ├── H3-10 MainForm 遍历派发
  └── 逐个 Tab 验证 ApplyTheme 行为一致
↓
Phase 4 ─ 流程/数据优化
  ├── H1-3 统一配置
  ├── H2-3 StringBuilder.Replace 替换
  ├── H2-4 方案迁移至 JSON
  └── H2-1/2 拖放/批量编排
↓
Phase 5 ─ 测试与文档
  └── 5.2 节新增测试 + README 版本表同步
```

### 7.3 功能退化检测清单

每次提交后手动验证（或写自动化测试）：

- [x] 深色/浅色模式切换正确
- [x] 简体中文/繁体中文/英文切换正确
- [x] 行合并：单文件/批量/预览/覆盖模式
- [x] 文件拼接：不同编码混合作业
- [x] 标点替换：单文件/批量/预设方案勾选
- [x] 拖放操作：文件拖入/拖出
- [x] 规则持久化：重启后规则不丢失
- [x] 配置导入导出：app_config.json + replace_rules.json

---

## 8. 维护维度

### 8.1 简化后代码可读性

| 优化项 | 对可读性的影响 |
|--------|---------------|
| H1-1 集中主题遍历器 | **正面**——新 Tab 开发者和代码审查者不再需要深入 100 行 ApplyTheme 理解配色逻辑 |
| H1-2 JsonFileStore | **正面**——"存储"意图一目了然，不再需要阅读 try-catch-File.Exists-Deserialize 序列 |
| H2-1 拖放统一 | **中立**——减少样板代码，但需要知道 ControlsHelper 中有 SetupFileDragEnter |
| H2-2 批量编排 | **正面**——将 UI 编排与业务逻辑分离，OnProcess 减少 70% 代码行 |
| H2-5 IStatusSource | **正面**——新 Tab 开发者获得编译器指导，不再"忘了声明事件" |

### 8.2 文档更新

优化实施后需同步更新的文档：

| 文档 | 更新内容 |
|------|----------|
| `doc/ArchitectureGuide.md` | 第 3 节「Tab Control Pattern」添加 ControlsHelper.ApplyTheme 用法 |
| `doc/ArchitectureGuide.md` | 新增「Configuration Management」节说明 ThemeManager 单点读取 |
| `doc/adr/ADR-001-pure-csharp-ui.md` | 引用 ControlsHelper.ApplyTheme 作为社区实践 |
| `README.md` | 版本历史 + 项目结构更新（新增 Services/JsonFileStore.cs） |

### 8.3 团队上手成本

当前：新成员需要理解 6 种不同的 ApplyTheme 实现、2 种 JSON 存储模式、2 套拖放代码。学习曲线前期集中在"为什么同样的东西有不同的写法"。

优化后：模式统一——新增 Tab 只需：

```csharp
// 新建 Tab 的标准步骤
public class NewTabControl : UserControl, IStatusSource
{
    public event Action<string>? StatusChanged;
    public event Action<string>? ErrorOccurred;

    public void ApplyTheme() => ControlsHelper.ApplyTheme(this);
    public void ApplyLocalization() { /* 翻译所有 Text 属性 */ }
}
```

上手成本从"理解现有写法"降为"遵循模板填参数"。

### 8.4 长期可持续性

- **ADR 文档**：已有 7 份，覆盖核心决策。建议为 H1-1/2 新增 ADR 记录。
- **依赖管理**：仅有 `System.Text.Encoding.CodePages` 一个外部依赖。低依赖风险。
- **.NET 版本**：当前 .NET 7 WinForms，微软支持截止 2026 年 5 月（已过 EOL）。**建议规划迁移至 .NET 8 LTS。** 这不会影响以上优化——所有建议都是纯 C# 重构，与框架版本无关。
- **边界扩展**：若要支持更多语言（如日语/韩语），H1-3 统一配置架构已准备好扩展——只需添加语言 JSON 文件 + Loc.DetectSystemLanguage() 中的检测逻辑。

---

## 9. 实施路线图

### 总览

| 阶段 | 内容 | 预估工时 | 影响范围 |
|------|------|----------|----------|
| **S1** | 零风险代码清理（H3 全部） | 1 天 | ~10 处行级修改 |
| **S2** | 模式提取与接口确立（H1-2, H2-5, H3-2） | 1 天 | 新增 JsonFileStore + IStatusSource |
| **S3** | 主题系统统一（H1-1） | 1.5 天 | 全部 Controls + Forms |
| **S4** | 配置与数据层优化（H1-3, H2-3, H2-4） | 1.5 天 | ThemeManager + Strings + PunctuationReplacer + ReplaceScheme |
| **S5** | UI 编排收束（H2-1, H2-2） | 1 天 | MergeTabControl + ReplaceTabControl + ControlsHelper |
| **S6** | 测试补全 + 文档同步 | 0.5 天 | 新增测试 + ADR + ArchitectureGuide |

**总计估算：** 约 6.5 个工作日（单人开发）

### 优先级执行建议

如果时间有限，推荐按以下优先级执行（高价值/低风险先行）：

```text
  S1 第一优先 ─→ S2 第二 ─→ S3 第三 ─→ S4 第四 ─→ S5 第五
(零风险清理)  (模式提取)   (主题统一)   (性能优化)   (UI收束)
```

**S1 可在 2 小时内完成**，消除全部死代码和冗余条件。  
**S2 含 H1-2 JsonFileStore**，是后续所有优化的基石——建议尽早落地。

---

*本方案基于 Simplify 技能框架的四维度（Reuse / Simplification / Efficiency / Altitude）全量审阅生成。每个发现均经过交叉验证（原始代码读取 + 独立 Agent 分析），最大程度减少误报。*
