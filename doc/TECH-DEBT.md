# TECH-DEBT — TextTool

> 未落地优化项、残留项与已知技术债的**单一权威清单**。
> 每条均已对照当前代码核实（核实项附文件:行号）；核对不实的标注「待核」。
> 已落地项不再跟踪，见文末「已核实落地」节（不含细节，仅登记）。

---

## 1. 未落地项（核实确认）

暂无。D-1–D-5 已移入 §3。

---

## 2. 已消解：`H:\` 盘符失真

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

复核方式：`grep -rn "H:[/\\\\]work_zone" --include=*.md --include=*.cs` 全仓只剩本节这一处
描述性引用（上面那行示例），真实路径已无命中；本文件此前对该失真的清单表已随本次订正移除。

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
| D-1 EncodingDetector 显示串国际化 | `DetectionResult.Kind` + `LocKey`（`EncodingUtf8Bom` 等）；Merge/Replace/VN 经 `Loc.T` |
| D-2 PreviewForm 构造期往返 | `PreviewForm(List<string>)` 直赋 `_lines`，不再 join/split |
| D-3 scripts/publish.ps1 | `scripts/publish.ps1` 存在 |
| D-4 主题遍历器测试 | `ControlsHelperThemeTests` 三例；`InternalsVisibleTo` |
| D-5 内联硬编码兜底 | `EmbeddedResource.cs`；`GetHardcodedSchemes` 已删 |

---

## 4. 验证锚点

```bash
# 当前版本号（唯一权威）
grep -oP '(?<=<Version>)[0-9.]+(?=<)' Directory.Build.props  # 期望 2.6.1

# 测试用例数（[Fact] + [Theory]）
grep -roE '\[(Fact|Theory)' TextTool.Tests --include=*.cs | wc -l   # 期望 197

# ADR 份数
ls doc/adr/ | wc -l                                          # 期望 9

# MainForm 行数
wc -l MainForm.cs                                            # 期望 214

# Services/ 文件数（UI 相邻基础设施）
ls Services/ | wc -l                                         # 期望 4

# 发布脚本（D-3 / 2026-09-28 REST）
test -f scripts/publish.ps1                                  # 期望存在
test -f scripts/release.ps1                                  # 期望存在

# 构建 + 测试
dotnet build TextTool.sln -c Release
dotnet test TextTool.sln -c Release
```

---

## 5. 已知遗留（`texttool lint` 特性）

> 来源：`texttool lint` 特性的最终整分支评审。评审明细原在 `sdd/`（该目录随后清理），故按类聚合登记于此。
> 均为评审判定「可留作已知遗留」的项：不阻塞交付，收口成本低但非必须。

**测试强度**

- `LintRule` 的四个非默认字段（`L2.MinCount`、`S2.Scope+MinCount`、`S3.Scope+TailChars`、`L7.Hints`）无绑定断言——手改 JSON 拼错键名会被静默回落（`Validate` 不拦未知键），4 行断言即可。
- 不变量测试在 literal 规则归零时空过；`LintRuleStore.Load` 的 19 条计数依赖 exe 目录没有 `lint_rules.json`。
- `Filter` 未断言「不改接收者」与 File/Chars 传递；`Filter` 结果与原报告共享元素引用（无调用方就地改写）。
- `Format` 未断言 Detail 渲染分支；5 条补覆盖只钉文案不钉数值；`Scan_统计项不进Hits` 近似不可失败。
- 两个新增测试取到的 stream 未 dispose（测试进程短命，不影响结果）。

**性能**

- `Position` 每次命中从头重扫；正则构造位置不一致（`P5` 每次 3 个、`C2` 每次 1 个）——`Scan` 级缓存即可。
- `GetDefaultSchemes()` 每次重读资源、无缓存；`--only` 路径重复 `Load` 一次；`windowStart` 可提到 Pattern 循环外。

**文档措辞**

- `P3` 的 Detail 措辞比字符类窄（也会命中 `,。`、`,Ａ`）；报告精度小疵（ledger 未细化）。
- 实现注释「任务名 vs 规则名」与 brief 漂移；计划 internal/public 不一致已随 53034f0 消解，仅存记录。
- `Format` 输出末尾无换行（由 `Program.cs` 追加 `Environment.NewLine`）；单文件输出尾部多一个空行。
- `Visible` 的转义集不含 emoji 等 GBK 编不出的可见符号（已由 `Console.OutputEncoding = UTF8` 处理）。
- 历史提交 `d3f0352` 的 `refactor:` 前缀、`adf9270` 提交信息言过其实（提交历史不改，仅登记）。

**契约边缘**

- `Merge` 就地替换后不更新索引（external 同 Id 两次会 `ArgumentOutOfRangeException`），生产路径先被 `Validate(external)` 挡住；`Validate` 仍信任列表元素非 null（`[null]` 走 NRE，CLI 仍退 2）。
- `Merge` 拷贝不对称（内置克隆、外部按引用插入）——无调用方就地改外部对象。
- 跨规则同跨度双报（`。。。。` → P1+P2，`您说得完全正确` → L5+L6）：报告层去重会丢建议且破坏 `--only` 契约；若要收敛只在数据层删 `P2` 的 `。{3,}`。
- `UnsafeRelaxedJsonEscaping` 连带不转义 `< > & ' +`；`--json` 到 stdout 的行尾是 CRLF；`Hits` 在 raw JSON 里不保证有序（人读渲染已排序）。
- `--min-severity` 大小写敏感（与 `--only` 不对称，但是响亮退 2）；`OutputEncoding` 的 guard 只捕 `IOException`。
- `SuggestScheme` 不进人读输出（只在 JSON 里）；外部 `lint_rules.json` 可撞算法 Id（无出货文件触发）。
- `Paragraphs` 把 Markdown 软换行拆开（S2/S3 少报，计划自陈的简化）；跨行命中时 Snippet 不含 Match 原文。

**外部数据**

- 技能侧 `去AI味标点归一` 的 Description 与实测不符（重复标点折叠先行，单遍跑不出 `……`）；`引号括号统一` 把全角括号转半角后 `P3` 命中反升（实测 2→6），技能侧已写明操作纪律。
- 旧脚本的弱序号统计项未迁移（技能映射表如实标注「未迁移」，补它属新增 `C6` 能力）；技能 `SKILL.md:97` 的 info 示例漏 L1。

---

行尾约定：本仓库所有文本文件为 LF（`.gitattributes` 强制），文档不得含 CRLF。
