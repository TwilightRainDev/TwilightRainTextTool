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

## 已消解：`texttool lint` 评审的「可留」条目（2026-09-29）

原 `doc/TECH-DEBT.md` §2 里「测试强度」5 条 +「文档措辞」1 条 +「契约边缘」1 条，逐条对照代码处理：

| 条目 | 落地证据 |
|---|---|
| `LintRule` 四个非默认字段无绑定断言 | `LintRuleStoreTests.GetDefaultRules_非默认字段真的绑定` 钉 L2/S2/S3/L7 的实际取值 |
| literal 规则归零时断言空过 | 该用例先 `Assert.NotEmpty(literal)` |
| `Load` 的 19 条计数依赖程序目录 | 改判「内置 19 条 id 全部出现在合并结果里」；精确清单由 `GetDefaultRules_数据规则19条且顺序完整` 在资源层钉住 |
| `Filter` 未断言不改接收者与 File/Chars 透传 | `LintReportTests.Filter_不改接收者且透传File与Chars` |
| `Format` 未断言 Detail 分支；补覆盖只钉文案 | 补 Detail 断言；C2–C5 改钉数值 |
| `Scan_统计项不进Hits` 近似不可失败 | 先证明该文本确实产出 C 组统计项，再断言不进 `Hits` |
| 两个测试的 stream 未 dispose | 改为 `using var` |
| 实现注释「任务名 vs 规则名」漂移 | `AiToneLintService.cs` 注释改为「规则名」 |
| `Filter` 结果与源报告共享元素引用 | `internal Copy()`（`MemberwiseClone`）；`Filter` 两个集合无条件新建并逐元素复制 |

§2 其余条目为评审判定「不可做」（改动会触及输出字节、退出码、JSON 契约或生产结构）或位于
HumanizerZh 技能仓者，当时仍列在活债表；这批共 16 条已随 v3.0.0 全部处置，见下节。

## 已处置：`texttool lint` 评审遗留 16 条（v3.0.0 范围，2026-09-29）

原 `doc/TECH-DEBT.md` §2 的 16 条逐条对照 v3.0.0 实施计划的提交处置；两条「无待改对象」
如实登记，未做任何改动。提交号可用 `git log --oneline` 复核，测试名对应 `TextTool.Tests/`。

| 原条目 | 落地证据 |
|---|---|
| `Position` 每次命中从头重扫；正则构造位置不一致（`P5` 3 个 / `C2` 1 个） | 提交 `51201d5`：`AiToneLintService.LineStarts` 行首索引 + 二分定位（`Position` 已删）；`P5`/`C2` 正则提为静态字段。测试 `AiToneLintServiceTests.Scan_行首索引与逐字符扫描结果一致` |
| `GetDefaultSchemes()` 无缓存；`--only` 路径重复 `Load`；`windowStart` 可外提 | 提交 `51201d5`：`EmbeddedResource.LoadText` 缓存资源文本；`LintRunner` 只 `Load` 一次并复用 `AiToneLintService.AllRuleIds(IEnumerable<LintRule>)`；`windowStart` 提到 Pattern 循环外。测试 `DefaultSchemeTests.GetDefaultSchemes_每次调用返回新实例`、`AiToneLintServiceTests.AllRuleIds_含数据规则与算法规则` |
| `P3` 的 Detail 措辞比字符类窄 | 提交 `3d9c1b9`：`default_lint_rules.json` 里 `P3` 的 `Detail` 改为「半角标点与中文或全角字符相邻」 |
| 计划 internal/public 不一致 | **无待改对象**：已随 `53034f0` 消解，仅登记，未改动 |
| `Format` 输出末尾无换行；单文件输出尾部多一个空行 | 提交 `3d9c1b9` + `069a660`：`TextTool.Cli/Program.cs` 人读分支与 JSON 分支收口为 LF、尾部只留一个换行；多文件时报告之间保留一个空行。测试 `LintTextFormatterTests.Format_末尾不带换行`；多文件空行由 CLI 冒烟（`grep -c '^$'`）钉住 |
| `Visible` 的转义集不含 emoji 等 GBK 编不出的可见符号 | **已消解**：输出编码设为 UTF-8（`TextTool.Cli/Program.cs`），设不上时退化为按宿主编码输出、真编不出的字符由外层 catch 响亮退 2；不扩转义集（扩了会让输出随宿主编码变化）。提交 `185de6e` 把 guard 放宽到捕获所有异常 |
| 历史提交 `d3f0352` 的 `refactor:` 前缀、`adf9270` 提交信息言过其实 | **无待改对象**：本仓不改历史，仅登记 |
| `Merge` 就地替换后不更新索引；`Validate` 信任元素非 null | 提交 `e7efdeb`：`LintRule.Merge` 索引随替换同步、两侧 `Clone`；`Validate` 拦 `null` 元素；新增 `ValidateNoAlgorithmIdCollision`。测试 `LintRuleStoreTests.Merge_同Id外部规则出现两次不抛且以最后一次为准`、`Validate_列表含null元素即抛而非NRE`、`ValidateNoAlgorithmIdCollision_外部Id撞算法Id即抛` |
| `Merge` 拷贝不对称（内置克隆、外部按引用插入） | 提交 `e7efdeb`：外部侧同样 `Clone`。测试 `LintRuleStoreTests.Merge_结果不与任一侧入参共享引用` |
| 跨规则同跨度双报（`。。。。` → P1+P2，`您说得完全正确` → L5+L6） | **维持现状**（用户 2026-09-29 拍板）：两条规则给的建议不同，报告层去重会丢建议并破坏 `--only`；README 启发式边界节写明是有意行为，不再作债条 |
| `UnsafeRelaxedJsonEscaping` 连带不转义 `< > & ' +`；`--json` 到 stdout 是 CRLF；`Hits` 在 raw JSON 里不保证有序 | 提交 `45b8d49` + `3d9c1b9`：编码器换 `JavaScriptEncoder.Create(UnicodeRanges.All)`；`ToJson()` 归一到 LF、CLI 结尾收口；`Scan` 出口按 `(Line, Col)` 稳定排序。测试 `LintReportTests.ToJson_HTML敏感字符转义`、`ToJson_行尾为LF`、`AiToneLintServiceTests.Scan_命中按行列升序`、`Scan_同位置多规则命中保持引擎顺序` |
| `--min-severity` 大小写敏感；`OutputEncoding` 的 guard 只捕 `IOException` | 提交 `185de6e`：校验改 `OrdinalIgnoreCase`；guard 放宽到捕获所有异常。测试 `LintRunnerTests.Run_最小严重度大小写不敏感` |
| `SuggestScheme` 不进人读输出；外部 `lint_rules.json` 可撞算法 Id | 提交 `3d9c1b9`：人读渲染 `<- 方案：<名>`（测试 `LintTextFormatterTests.Format_渲染建议方案`）；提交 `e7efdeb`：撞码守卫加载即抛（测试 `LintRuleStoreTests.ValidateNoAlgorithmIdCollision_外部Id撞算法Id即抛`） |
| `Paragraphs` 把 Markdown 软换行拆开；跨行命中时 Snippet 不含 Match 原文 | 提交 `6a8ca15` + `3c25243`：首 4KB 空行密度探测 + Markdown 空行分段；`Snippet` 改为原文的连续切片。测试 `AiToneLintServiceTests.Scan_空行密度达标判为Markdown口径且软换行同段`、`Scan_跨行命中的Snippet保留原文` |
| 技能侧 `去AI味标点归一` Description 与实测不符；`引号括号统一` 的副作用 | 前者在 HumanizerZh 技能仓，本仓无待改对象，该项随任务 11 在 HumanizerZh 技能仓处置；后者技能侧已写明操作纪律，本轮不改方案数据，其副作用写进 README 启发式边界节 |
| 旧脚本弱序号统计项未迁移；技能 `SKILL.md:97` 的 info 示例漏 L1 | 提交 `0ff4128`：新增 `C6` 显式序号标记统计（测试 `AiToneLintServiceTests.Scan_显式序号标记产出C6统计项`）；技能侧示例在 HumanizerZh 技能仓，本仓无待改对象 |
