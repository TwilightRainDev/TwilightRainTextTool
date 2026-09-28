# 已核实落地的技术债（归档）

> 2026-09-28 从 `doc/TECH-DEBT.md` 迁出。只登记，不再跟踪。活债表见 `../TECH-DEBT.md`。

## 已消解：`H:\` 盘符失真

以下文档与注释中的绝对路径曾写作 `H:\work_zone\...`，但本机不存在 H: 盘（真实凭据目录为
`E:\work_zone\ApiKey\`，实测存在 `TextTool-signing.priv.pem` / `TextTool-signing.pub.pem`）。
**2026-09-26 经用户确认后统一修正为 `E:\`，共 9 处**：

| 位置 | 处数 |
|---|---|
| `doc/UpdateSecurity.md` | 5 |
| `doc/adr/ADR-008-update-trust-model.md` | 1 |
| `doc/specs/2026-08-03-texttool-2.4.5-design.md` | 1 |
| `TextTool.Core/ReleaseSigningPublicKey.cs`（注释） | 1 |
| `tools/ReleaseSigner/Program.cs`（注释） | 1 |

复核方式：`grep -rn "H:[/\\\\]work_zone" --include=*.md --include=*.cs` 全仓只剩 `TECH-DEBT.md` 归档前本节这一处
描述性引用；真实路径已无命中。

### 已消解项

- **P3「README 版本表自动化」**：README 的英文/中文两张版本历史表已删除，改为指向 git tag 与 Releases。
  现在没有需要与 `Directory.Build.props` 同步的手写版本表，该项不再需要自动化。

## 已核实落地（仅登记）

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
| D-1 EncodingDetector 显示串国际化 | `DetectionResult.Kind` + `LocKey`（`EncodingUtf8Bom` 等）；Merge/Replace/VN 经 `Loc.T` |
| D-2 PreviewForm 构造期往返 | `PreviewForm(List<string>)` 直赋 `_lines`，不再 join/split |
| D-3 scripts/publish.ps1 | `scripts/publish.ps1` 存在；2026-09-28 改为 REST，`scripts/release.ps1` 编排 tag |
| D-4 主题遍历器测试 | `ControlsHelperThemeTests` 三例；`InternalsVisibleTo` |
| D-5 内联硬编码兜底 | `EmbeddedResource.cs`；`GetHardcodedSchemes` 已删 |
