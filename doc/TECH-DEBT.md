# TECH-DEBT — TextTool

> 未落地优化项与已知遗留的**单一权威清单**。
> 已落地项见 `doc/archive/tech-debt-landed.md`，不再跟踪。
> 每条均已对照当前代码核实；核对不实的标注「待核」。

---

## 1. 未落地项（核实确认）

暂无。下一轮候选（v3.0 评审分诊、未排期）见 §5。

---

## 2. 已知遗留（`texttool lint` 特性）

暂无。

本节原有 16 条随 v3.0.0 全部处置：逐条去向与证据见 `doc/archive/tech-debt-landed.md`；
原特性评审明细在 `doc/archive/plans/2026-09-26-texttool-lint-reviews/`。会改可观察输出
（输出字节、退出码、JSON 契约）的按破坏性变更处理，README 契约节与发布说明已同步写明。

---

## 3. 在途规格（不排队）

暂无。

`2026-08-03-texttool-2.4.5-design.md` 的四项全部处置完毕，设计文档收录在 `doc/archive/specs/`：

| 项 | 处置 |
|---|---|
| B1 `scripts/publish.ps1` 发布自动化 | 落地于 v2.6.1 的 `scripts/release.ps1` |
| A1 下载超时分级 | 随 v2.6.2 发版 |
| A2 替换脚本 wait-loop 健壮化 | 随 v2.6.2 发版；实际根因与规格假设不同，见该文档 §7 |
| D7 私钥加密 | **不做**（用户决定），条目删除，不再跟踪 |

**规格漂移（登记）**：该规格标题写的是「2.4.5 发版收口设计」，A1/A2 本被定为 2.4.5 的客户端变更，
实际跨过 2.4.5 / 2.5.0 / 2.6.0 / 2.6.1 四个版本，到 v2.6.2 才发到用户端。标题里的版本号与现状
脱节，作为历史记录保留、不再重命名。

---

## 4. 验证锚点

```bash
# 当前版本号（唯一权威）
grep -oP '(?<=<Version>)[0-9.]+(?=<)' Directory.Build.props  # 期望 3.0.0

# 测试用例数（[Fact] + [Theory] 属性条数，非运行条数）
grep -roE '\[(Fact|Theory)' TextTool.Tests --include=*.cs | wc -l   # 期望 245

# ADR 份数
ls doc/adr/ | wc -l                                          # 期望 9

# 发布脚本
test -f scripts/publish.ps1                                  # 期望存在
test -f scripts/release.ps1                                  # 期望存在

# 构建 + 测试
dotnet build TextTool.sln -c Release
dotnet test TextTool.sln -c Release
```

---

## 5. 下一轮候选（v3.0 评审分诊，未排期）

v3.0.0 的终审与两轮修复波判为「留到下一轮」的条目，都不影响 v3.0.0 的正确性，属加固与整洁项。
逐条论证（含实测输出与 file:line）在当轮 SDD 报告里；`sdd/` 是运行时产物，不进版本库。

**测试强度**

- CLI 层输出契约（`TextTool.Cli/Program.cs` 的人读与 JSON 两分支：LF 行尾、报告之间一个空行、尾部单个换行）没有自动化测试——要补得先把分隔与 EOL 归一提到 `TextTool.Core` 的纯函数。
- 多文件的「报告之间空行」当前只有 CLI 冒烟（`grep -c '^$'`）作证。
- `LintTextFormatterTests.Format_渲染建议方案` 无反例：没有断言 `SuggestScheme` 为空时不得出现 `方案：` 后缀。
- `LintRunnerTests.Run_最小严重度非法值仍抛` 与改后的既有 `Run_非法minSeverity即抛` 同义，零增量覆盖。
- `LintRunnerTests.Run_最小严重度大小写不敏感` 的 `Assert.All` 在现夹具上近乎恒真（该文本只有 warn 命中，真正干活的是 `Assert.NotEmpty`）。
- `LintReportTests.ToJson_HTML敏感字符转义` 的 `DoesNotContain("\\u8D4B")` 在纯 ASCII 夹具上恒真；同文件没有 U+0022 的单独断言。
- `C6` 的阈值上界（恰好 2 处不报）没有单测。
- `PublishScriptTests` 的两条新断言只钉「脚本文本里出现过 `UpgradeNotes.md` / `Set-GithubReleaseBody`」，发现不了调用点被挪到 `-SkipUpload` 之前。

**代码健壮性与一致性**

- `Program.cs` 两个输出分支的 EOL 防御不对称：人读侧做 `Replace("\r\n", "\n")`，JSON 侧依赖 `ToJson()` 恒出 LF 这一不变量。
- `stderr`（`Console.Error.WriteLine`）仍输出 CRLF，重定向到文件会带进 CR。
- `Program.cs` 设置输出编码处的 `catch (Exception)` 宽于 setter 的实际异常集（`.editorconfig` 已关 CA1031，属备查）。
- 规则 Id 大小写口径不对称：`LintRuleStore.ValidateNoAlgorithmIdCollision` 用 `OrdinalIgnoreCase`，而 `Merge`/`Validate` 的 Id 唯一性用 `Ordinal`——`L1` 与 `l1` 算两条规则，到 `--only`/`Filter`（`OrdinalIgnoreCase`）却塌缩成同一选择子；无出货数据触发。
- `LintRuleStore.Merge` 的 `result.IndexOf(existing)` 是映射循环里的线性扫描，可让索引字典直接存位置。
- `EmbeddedResource.LoadText` 与 `LoadJsonList` 有 5 行逐字重复的流读取。
- `Directory.Build.props:7-8` 是派生值的二手副本，每次升版要改三处（可改为指向构建产物自述）。

**发布脚本**

- `scripts/publish.ps1` 的幂等判据取 `doc/UpgradeNotes.md` 首行的 sentinel：改标题行、或工作区被归一成 CRLF，都会失配而重复追加（可改固定标记行，或判「正文包含整份提示」）。
- `doc/UpgradeNotes.md` 缺失或改名时静默跳过（`if (Test-Path …)` 没有 `else`）——建议补一行 `Write-Warning`，发布照跑但出声。
- release 原正文为空时尾部留悬空 `---`（本仓 CI 的 `generate_release_notes` 恒产生非空正文，该分支不可达）。

**口径边界（重建口径的既定边界，不是缺陷）**

- `C6` 不覆盖全角数字 `１、`、`1）`、`[1]` 等变体；行首 `3.14` 会被计入（需连续 3 行才越阈值）。
- `doc/archive/tech-debt-landed.md:96` 的「首 4KB」与实现的 4096 **字符**不符。

仓库外的工作区项（`Docs\` 里指向已删 `Docs\History\` 的悬空指针、`fact\projects\cc-switch.md` 的 `verify` 失败）不在本表，随工作区整理进度处理。

---

行尾约定：本仓库所有文本文件为 LF（`.gitattributes` 强制），文档不得含 CRLF。
