# 修复批复评 — texttool lint（fbf4d6a..b4a8e46）

复评者：修复批复评（只读）
复评基准：修复前 HEAD = fbf4d6a，修复后 HEAD = b4a8e46（工作区干净，`git status --short` 空）
被复评对象：`sdd/2026-09-26-texttool-lint/final-fix-report.md` 与 `review-fbf4d6a..b4a8e46.diff`
规格（权威）：`doc/specs/2026-09-26-texttool-lint-design.md`
验证方式：逐条 Read 修复 diff 涉及的文件 + 实跑 HEAD 二进制（`TextTool.Cli/bin/Release/net8.0/texttool.exe`）+ 实跑测试套件；
退出码一律在未接管道的命令上取（`> log 2>&1; echo $?`）。本次复评未修改任何仓内文件。

---

## 1. 逐条 verdict

| # | 项 | 裁决 | 证据（file:line）与实跑检查 |
|---|---|---|---|
| 1 | I-1 `--only` 同时过滤 `Notes` | **ADDRESSED** | `TextTool.Core/LintReport.cs:52` 改为 `Notes = only is null ? Notes : Notes.Where(n => only.Contains(n.Id)).ToList()`，注释 `:37-40` 同步。实跑：`lint uni.txt --only C1` → `[统计]` 仅 C1、exit=0；`--only C5` → 仅 C5、exit=0；`--only S1` → `统计项 0 条`、exit=0。JSON 侧 `Reports[0].Notes` 的 Id 序列分别为 `["C1"]` / `["C5"]` / 不加 `--only` 时 `["C1","C5"]`（对照组证明区分力）。修前 `--only C1` 打出 C5，故新行为确实换过。新用例 `TextTool.Tests/Services/LintReportTests.cs:39-51` 钉住 `S1`→Notes 空 与 `c1`（小写）→保留 C1。 |
| 2 | I-2 `--json` 转义隐形字符 | **ADDRESSED** | `TextTool.Core/LintTextFormatter.cs:20-26`（新增 `EscapeInvisible`）、`:11`（`Escaped` 改 internal，转义集不再有第二份）、`TextTool.Core/LintReport.cs:74`。字节级实测：`od -c zwsp.json` 显示 `"Match": "\u200b"` 为 `92,117,50,48,48,98`；`raw.split(backslash+'u200b').length-1 = 2`（`Match` 与 `Snippet` 各一次）；JSON 文本内真 U+200B 计数 **0**；`JSON.parse` 后 `Match` 码点 `200b`（语义未变），`Snippet` 长度 5。`EscapeInvisible` 未碰 JSON 自身的结构行尾（od 中 `13,10` 仍在），M-8 的 CRLF 行为未被放大。 |
| 3 | README 功能表表头（双语） | **ADDRESSED** | `README.md:19` = `\| Tab / CLI \| Description \|`、`:310` = `\| 页签 / CLI \| 说明 \|`；其下纯 CLI 行在 `:26` / `:317`。 |
| 4 | README info 级主语（双语） | **ADDRESSED** | `README.md:182` = `` `P4`/`P5` are all `info` level ``、`:464` = `` `P4`/`P5` 一律 `info` 级 ``；C 组「不进退出码」句仍在其前，未被削弱。 |
| 5 | `doc/TECH-DEBT.md` 版本锚点 | **ADDRESSED** | `doc/TECH-DEBT.md:118`。新旧两条命令均实跑：旧式 `grep -oP '(?<=<Version>)[^<]+'` 输出 **2 行**（`，程序集版本自动从此继承。` + `2.5.0`，坐实原缺陷），新式 `[0-9.]+(?=<)` 输出 1 行 `2.5.0`。 |
| 6 | `doc/ArchitectureGuide.md` §8 结构树 | **ADDRESSED** | `doc/ArchitectureGuide.md:182-185` 补 4 个 Core 文件、`:188` 嵌入资源行含 `default_lint_rules.json`、`:196` CLI 行含 `lint`。把磁盘 `TextTool.Core/*.cs`（29 个）与 4 个资源跟该树逐项比对：无遗漏、无多余；与 `README.md:226-239` / `:507-520` 两棵树一致。 |
| 7 | `doc/plans/2026-09-26-texttool-lint.md` Task 4 `Filter` 代码块 | **ADDRESSED** | `doc/plans/2026-09-26-texttool-lint.md:700-722` 与 `TextTool.Core/LintReport.cs:37-59` 机械 `diff` 输出为空（IDENTICAL），含 XML 注释与 `Notes` 行。 |
| 8 | `doc/TECH-DEBT.md` 新增遗留登记节 | **ADDRESSED（有遗漏）** | `doc/TECH-DEBT.md:145-186` 五类已建；抽查 T3-1、T1-6、M-5、M-7、M-8、M-9、T5-2、T6-3、T11-g/h/j 与评审原文一致，无走样。**未登记的「可留」项 8 条**：T1-1（测试钉常量而非调用点）、T1-4（两处 store 常量与注释重复）、T2-4（`Validate(builtIn)` 无测试执行到）、T4-5（`LintReport` 类型级 XML 注释）、T6-2（`Paragraphs` 行首空白/CRLF 分支零覆盖）、T7-2（`AlgorithmRuleIds` 暴露底层数组）、T11-c/T11-d（技能侧体例）。T4-1、T7-1、T9-2 已被本批实际修掉，缺席正确。 |
| 9 | 顺带：`AiToneLintService` 中文 XML 注释 | **ADDRESSED** | `TextTool.Core/AiToneLintService.cs:32`（`AlgorithmRuleIds`）、`:35`（`AllRuleIds()`）各一行中文 summary，措辞与实现相符（并集、`--only` 校验集）。 |
| 10 | 顺带：`Program.cs` 类头 API 清单 | **ADDRESSED** | `TextTool.Cli/Program.cs:16` 补 `texttool lint <file...> [--only <id...>] [--min-severity warn] [--json]`，与 `:322` / `:337-346` 实际接受的开关一致。 |
| 11 | 技能侧同步（仓外） | **ADDRESSED** | `E:\work_zone\ClaudeCode\home\.claude\skills\HumanizerZh\SKILL.md:95`（`--only` 同时过滤 `Hits` 与 `Notes`）、`:172`（常见错误表改写）、`:183`（两种输出都已转义；解析方解回原字符）。三条均与实跑吻合：`:95` 两例见第 1 条，`:183` 的人读侧实测为 `第 1 行 第 3 列 甲乙\u200b丙。`。`SKILL.md:96` 的「`--only C5` 退出码仍是 0」在新行为下仍成立（实测 exit=0）。落盘 13,214 B、无 BOM、CR=0、LF=195。 |
| 12 | 修复自认的额外项复核（`[Fact]/[Theory]` 182→183） | **ADDRESSED** | `grep -roE '\[(Fact\|Theory)' TextTool.Tests --include=*.cs \| wc -l` = **183**，与 `doc/TECH-DEBT.md:121`、`README.md:261`、`README.md:542` 三处自洽。 |

---

## 2. New Breakage in the Fix Diff

- **Low** — `doc/plans/2026-09-26-texttool-lint.md:609` 仍是改名前的 `Filter_按严重度保留warn及以上_统计项不受影响()`，而实现已改名为 `..._统计项不受严重度影响`（`TextTool.Tests/Services/LintReportTests.cs:18`）。改名发生在本次修复提交内，故计划同一 Task 4 的测试块比修复前更旧。仓内仅此一处（`sdd/` 除外，将被清理）。非阻断。
- 其余无。检查：`dotnet test TextTool.Tests/TextTool.Tests.csproj -c Release` → `失败: 0，通过: 221，已跳过: 0`（exit=0），新增与改名用例连同全量用例通过；复跑后 `git status --short` 为空，跟踪文件未被改动。

---

## 3. Out-of-Scope Observations

- `doc/plans/2026-09-26-texttool-lint.md:1707` 与 `TextTool.Cli/Program.cs:397` 的「统计项不受影响」是 `--min-severity` 的说明（严重度轴），在新行为下依然成立，不是残留。
- 未复验的旧锚点：`MainForm.cs 207 行`、`Services/ 4 文件`、`ADR 9 份`、`tag 10 个`，与本批变更无关。
- `sdd/` 尚未清理，`final-review.md` / `final-fix-report.md` / ledger 仍在工作区（`git status --short` 空，即被 ignore）。

---

## 4. Verdict

**All findings addressed, no new Critical/Important breakage** — 8 项 finding + 两处顺带项 + 技能侧三行全部落地并经实跑复验。两条非阻断记录（不影响收官，但 sdd/ 清理后无别处留档）：(1) `doc/TECH-DEBT.md` §5 未登记 8 条评审判为「可留」的 micro 项（其中 T6-2、T7-2 相对实质）；(2) `doc/plans/2026-09-26-texttool-lint.md:609` 的测试名停留在改名前的写法。
