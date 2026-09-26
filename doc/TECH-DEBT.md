# TECH-DEBT — TextTool

> 未落地优化项、残留项与已知技术债的**单一权威清单**。
> 每条均已对照当前代码核实（核实项附文件:行号）；核对不实的标注「待核」。
> 已落地项不再跟踪，见文末「已核实落地」节（不含细节，仅登记）。

---

## 1. 未落地项（核实确认）

### D-1 `EncodingDetector` 显示串未国际化

- 现状：`TextTool.Core/EncodingDetector.cs:176` 的 `DetectionResult.DisplayName` 直接产出英文串
  （`"UTF-8 (BOM)"` / `"UTF-16 LE"` / `"UTF-16 BE"` / `"GBK"` / `"UTF-8"` 等），
  `Controls/ReplaceTabControl.cs:315` 把它原样写入 `_lblReplaceEncoding.Text`。
- 影响：切换到简体/繁体中文时，替换页签的编码标签仍显示英文。
- 建议方向：`DetectionResult` 改为携带枚举/键，UI 侧经 `Loc.T("EncodingUtf8")` 等键翻译。
- 状态：未落地（`EncodingUtf8` / `EncodingGbk` 等键在 `Localization/*.json` 中不存在）。

### D-2 `PreviewForm` 构造期字符串往返（H3-1 残留）

- 已落地部分：`Controls/PreviewForm.cs` 持有 `List<string> _lines`，`OnSave` 经 `AtomicFile.WriteAllLines(_lines, ...)` 直写，保存路径不再往返。
- 残留：`PreviewForm(List<string>)` 仍委托 `: this(string.Join(Environment.NewLine, lines), ...)`，
  而 `string` 重载又 `text.Split('\n')` 还原成 `_lines` —— 每次打开预览多做一次 join + split。
- 建议方向：两个重载共用私有构造，避免 List→string→List。
- 状态：残留（非功能缺陷，仅冗余分配）

### D-3 `scripts/publish.ps1` 不存在（P2 未落地）

- 核实：仓库内无 `scripts/` 目录，全仓无任何 `.ps1` 文件（`find . -name "*.ps1"` 为空）。
- 当前发布仍为手工步骤（下载 CI 产物 → 离线签名 → 上传 `.sig` → 自检），见 `doc/UpdateSecurity.md`。
- 该脚本已在**在途计划**中设计：`doc/specs/2026-08-03-texttool-2.4.5-design.md` 的 B1 项
  （目标路径 `scripts/publish.ps1`，含 `-Version` / `-SkipUpload` / `-Force` 参数）。
- 状态：未落地，已在在途计划中排期

### D-4 主题遍历器无单元测试覆盖（H1-1 测试配套）

- 现状：`ControlsHelper.ApplyTheme(Control root)` 是全部 5 个页签 + 对话框的主题入口，
  `TextTool.Tests/` 下无任何引用 `ApplyTheme` / `IThemedTab` 的测试文件。
- 原计划要求补 3 项（覆盖 Label/Button/TextBox/CheckBox/RadioButton/ComboBox 类型分派）。
- 状态：未落地

### D-5 内置方案的内联硬编码兜底待删（R0 遗留）

- 现状：R0 已把资源名修正为 `TextTool.default_schemes.json`（`TextTool.Core/ReplaceScheme.cs:27`）
  与 `TextTool.default_vn_schemes.json`（`TextTool.Core/VNCharacterScheme.cs:35`），资源解析成功即不再走兜底，
  于是 `ReplaceScheme.cs:65-253` 的 `GetHardcodedSchemes()`（189 行）与 `VNCharacterScheme.cs:69-139`
  的同名方法（71 行）成为**纯冗余**：实测两份内联数据与对应 `default_*.json` 的条目数相同
  （替换方案各 9 方案 / 176 规则，VN 方案各 4 方案）。
- 影响：同一份数据两处真相。改 JSON 已生效，但代码内的副本会随时间漂移，且给人「改哪里才对」的错误暗示；
  `GetDefaultSchemes()` 的 `catch { }`（`ReplaceScheme.cs:57`）还会把嵌入式资源解析失败一并吞掉，静默降级到兜底。
- 建议方向：删除两份内联数据，让资源缺失像 `TextTool.Core/PinnedRoots.cs:19-20` 那样
  `throw new InvalidOperationException`，而不是静默兜底。
- 状态：未落地（规格 `doc/specs/2026-09-26-texttool-lint-design.md` §2 明确列为范围之外，属独立重构）

---

## 2. 待人工确认：`H:\` 盘符失真

以下文档中的绝对路径写作 `H:\work_zone\...`，但本机**不存在 H: 盘**
（`ls H:/` → `No such file or directory`），实际凭据目录为 `E:\work_zone\ApiKey\`（实测存在
`TextTool-signing.priv.pem` / `TextTool-signing.pub.pem`）。属可机械化替换的失真，等待确认后统一修正。

| 位置 | 内容 |
|---|---|
| `doc/UpdateSecurity.md:24` | 私钥路径 `H:\work_zone\ApiKey\TextTool-signing.priv.pem` |
| `doc/UpdateSecurity.md:25` | 公钥路径 `H:\work_zone\ApiKey\TextTool-signing.pub.pem` |
| `doc/UpdateSecurity.md:28` | keygen 命令参数 `H:\work_zone\ApiKey` |
| `doc/UpdateSecurity.md:39` | sign 命令 `-k H:\work_zone\ApiKey\...priv.pem` |
| `doc/UpdateSecurity.md:48` | verify 命令 `-k H:\work_zone\ApiKey\...pub.pem` |
| `doc/adr/ADR-008:16` | 私钥路径 |
| `doc/specs/2026-08-03-texttool-2.4.5-design.md:112` | 私钥默认路径 |

**另有一处在代码中，本次未改（不可改代码）：**

| 位置 | 内容 |
|---|---|
| `TextTool.Core/ReleaseSigningPublicKey.cs:6` | 注释中的 `H:\work_zone\ApiKey\TextTool-signing.pub.pem` |

### 已消解项

- **P3「README 版本表自动化」**：README 的英文/中文两张版本历史表已删除，改为指向 git tag 与 Releases。
  现在没有需要与 `Directory.Build.props` 同步的手写版本表，该项不再需要自动化。

---

## 3. 已核实落地（仅登记，不再跟踪）

以下原 `doc/OptimizationPlan.md` 条目已对照代码确认落地，不再作为债项：

| 条目 | 落地证据 |
|---|---|
| H1-1 集中式主题遍历器 | `Services/ControlsHelper.ApplyTheme(Control)`；5 个页签统一调用 |
| H1-2 泛型 JSON 存储 | `TextTool.Core/JsonFileStore.cs` 存在；`JsonFileStoreTests` 覆盖 |
| H1-3 统一配置管理 | `ThemeManager.CurrentLanguage` 单点读取；`InitialLanguage` / `LoadLanguageFromConfig` 已不存在 |
| H2-1 拖放事件统一 | `ControlsHelper.SetupFileDragEnter/Over`、`ResetFileDragLeave` |
| H2-2 批量处理编排 | `ControlsHelper.RunBatchAsync`；Merge/Replace 页签共用 |
| H2-3 `StringBuilder.Replace` | `PunctuationReplacer.Apply` 用 `StringBuilder.Replace`，无逐规则中间串 |
| H2-4 内置方案迁 JSON | `default_schemes.json` / `default_vn_schemes.json` 嵌入式资源；`DefaultSchemeTests` 覆盖 |
| H2-5 `IStatusSource` | `Services/IStatusSource.cs`；4 个页签实现 |
| H3-2 `_Processed` 路径提取 | `TextTool.Core/PathHelper.GetProcessedPath` |
| H3-3 死代码 `SelectSingleFile` | 全仓已无该符号 |
| H3-4 `FileJoiner` 冗余条件 | 现仅 `if (!content.EndsWith("\n"))` |
| H3-5 `UTF8Encoding` 静态化 | `ProcessingPipeline.cs:29` `static readonly UTF8Encoding Utf8Bom` |
| H3-6/H3-7 方案表单简化 | `_isChecking` 标志已移除 |
| H3-9 `CreateTextFileDialog` 复用 | `MergeTabControl.cs:305`、`ReplaceTabControl.cs:342`、`VNTabControl.cs:330` 均经 `ControlsHelper` |
| H3-10 `ApplyTheme` 统一派发 | `MainForm.cs:174-178` `ApplyToAllTabs` 经 `IThemedTab` 强类型派发 |
| P1 `.editorconfig` + 分析器 | `.editorconfig` 存在；`Directory.Build.props` 启用 `EnableNETAnalyzers` |
| P4 提交前检查 | `.githooks/pre-commit` 存在 |
| .NET 8 迁移 | `Directory.Build.props` + 各 csproj 目标 `net8.0` / `net8.0-windows` |

---

## 4. 验证锚点

```bash
# 当前版本号（唯一权威）
grep -oP '(?<=<Version>)[^<]+' Directory.Build.props        # 期望 2.5.0

# 测试用例数（[Fact] + [Theory]）
grep -roE '\[(Fact|Theory)' TextTool.Tests --include=*.cs | wc -l   # 期望 182

# ADR 份数
ls doc/adr/ | wc -l                                          # 期望 9

# MainForm 行数
wc -l MainForm.cs                                            # 期望 207

# Services/ 文件数（UI 相邻基础设施）
ls Services/ | wc -l                                         # 期望 4

# 全仓无 PowerShell 脚本（D-3 的复现条件）
find . -name "*.ps1" -not -path "./.git/*"                   # 期望无输出

# 构建 + 测试
dotnet build TextTool.sln -c Release
dotnet test TextTool.sln -c Release
```

行尾约定：本仓库所有文本文件为 LF（`.gitattributes` 强制），文档不得含 CRLF。
