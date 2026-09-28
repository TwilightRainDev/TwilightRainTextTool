# TextTool GUI 接入中文 AI 味检查（lint）设计

> 日期：2026-09-26 ｜ 状态：已批准开工 ｜ 类型：设计文档
> 来源：用户确认 CLI `texttool lint` 与 WinForms GUI 功能未对齐；上一轮规格把 GUI 第 6 个 Tab 列为范围外
> 前置事实：引擎在 `TextTool.Core`（`AiToneLintService` / `LintReport.Filter` / `LintTextFormatter.Format`），无 UI 依赖；`MainForm` 现为 5 个页签；CLI 子命令已发布于 `2.5.0`

## 1. 背景与目标

工具的去 AI 味检查只活在 CLI。桌面用户打开 GUI，五个页签全是改写（合并、拼接、替换、VN），没有「只报不改」的入口。上一轮刻意先把 CLI 与技能跑顺；引擎已是纯 Core 服务，GUI 接入不需要再改规则或扫描算法。

**目标**：GUI 与 CLI 共用同一引擎，桌面端能对文件或粘贴文本做出与 `texttool lint` 同义的人读报告。只报不改。

**成功标准**：在 GUI 第 6 个页签里打开一份有 AI 味的文本，能看到与 CLI 人读输出同一套命中（规则 Id、行列、片段）；干净文本显示无命中。不写出 `*_Processed.*`，不改源文件。

## 2. 范围

### In scope

| 编号 | 项 | 归属 |
|---|---|---|
| G1 | `LintRunner`：把「输入文本 + 过滤 → `LintReportSet`」从 CLI 抽到 Core，校验 `--only` / `--min-severity` 与 CLI 同义 | Core + Tests |
| G2 | CLI `RunLint` 改为调用 `LintRunner`（参数解析、读文件、stdout、退出码仍在 CLI） | CLI |
| G3 | 三个语言包补齐页签与控件键 | Localization |
| G4 | 第 6 个页签 `LintTabControl`：文件拖放/浏览、粘贴、过滤、只读报告 | GUI |
| G5 | `MainForm` 接入该页签（插在 VN 与关于之间）；文档与版本 `2.6.0` | GUI + 仓库 |

### Out of scope（明确不做）

- **`--json`**：机器契约仍只走 CLI / 技能
- **`--fix` 与任何自动改写**、写出 `*_Processed.*`
- **目录递归批量**
- **改 `default_lint_rules.json` 或算法规则**
- **翻译报告正文**：`Format()` 与规则 Title/Detail 保持中文（与 CLI 一致）。页签铬（按钮、标签、状态栏）走 `Loc`
- **D-5 内联兜底删除**、D-1–D-4
- **给 GUI 加单元测试**：本仓测试只覆盖 Core；页签与现有五个页签一样靠人工点验
- **CLI 退出码语义**、`--min-severity` 大小写敏感（已知遗留，不顺手改）

## 3. 方案选型

**选定：Core 编排器 + 第 6 页签。**

- 扫描、过滤、渲染已经存在。缺的是「多段输入 + 过滤校验」这一层，今天写在 `TextTool.Cli/Program.cs` 的 `RunLint` 里。抽成 `LintRunner`，CLI 与 GUI 各做自己的 I/O。
- 入口用第 6 个 `IThemedTab` 页签，对齐上一轮规格留下的接入点，也对齐现有五个页签的主题/本地化派发。
- 不在关于页塞一个按钮，不弹独立窗口：检查是一等能力，不是附属对话框。

备选（不采用）：

- **GUI 直接复制 `RunLint`**：两处校验会漂。
- **只接文件、不接粘贴**：桌面去 AI 味经常对着一段未落盘的稿。粘贴是 GUI 对 CLI stdin 的对等物。

## 4. 各项设计

### G1 `LintRunner`

```csharp
public static class LintRunner
{
    public static LintReportSet Run(
        IReadOnlyList<(string File, string Text)> inputs,
        IReadOnlyCollection<string>? onlyIds,
        string? minSeverity)
}
```

行为（与现行 CLI 校验对齐，不是新契约）：

| 条件 | 行为 |
|---|---|
| `inputs` 为 null 或 Count=0 | `ArgumentException`（「lint 需要至少一个输入」） |
| `minSeverity` 不是 `null` / `"info"` / `"warn"`（大小写敏感） | `ArgumentException`（「--min-severity 只接受 info 或 warn」） |
| `onlyIds` 非 null 且 Count=0 | `ArgumentException`（「--only 需要至少一个规则 Id」） |
| `onlyIds` 含未知 Id（相对 `AiToneLintService.AllRuleIds()`，大小写不敏感） | `ArgumentException`（「未知规则 Id：…」） |
| 合法 | 对每条 input 调用 `new AiToneLintService(LintRuleStore.Load()).Scan(text, file).Filter(onlyIds, minSeverity)`，按输入顺序装进 `LintReportSet.Reports` |

不读盘、不写盘、不碰控制台。同一 `LintRunner.Run` 调用里可以共用一个 `AiToneLintService` 实例（规则只 `Load` 一次）。

### G2 CLI

`RunLint` 仍负责：UTF-8 输出、argv、`--help`、`--json`、读文件 / stdin、退出码 0/1/2。

读完输入后调用 `LintRunner.Run`。原先写在 `RunLint` 里的 `onlyIds` / `minSeverity` / 未知 Id 校验删掉，避免双份。`files.Count == 0` 的 argv 检查保留（文案仍是「lint 需要至少一个输入文件（- 表示 stdin）」）。

stdin 的 `File` 名仍是 `(stdin)`。

### G3 / G4 页签

页签标题键 `TabLint`。插在视觉小说与关于之间。关于保持最后一页。

输入（至少一种）：

- **文件**：拖放 + 浏览，多选。编码用 `EncodingDetector.DetectStrict`，与 CLI `ReadFileForLint` 相同。报告里的 `File` 用 `Path.GetFileName`。
- **粘贴**：多行文本框。有内容则追加一条 `File = "(paste)"` 的输入（ASCII，与 CLI 的 `(stdin)` 并列，不随语言变）。

过滤：

- 「只跑规则」文本框：空白 → `onlyIds = null`；否则按逗号拆（`RemoveEmptyEntries | TrimEntries`），交给 `LintRunner`。
- 「最低严重度」下拉两档：全部 → `minSeverity = null`；warn 及以上 → `"warn"`。不提供单独的 `"info"` 档（与默认全部等价）。

动作：主按钮「检查」。后台 `Task.Run` 调 `LintRunner`，避免大文件卡 UI。只读多行文本框展示每人读报告：对 `Reports` 逐份 `LintTextFormatter.Format(report)`，份与份之间空一行。无保存、无覆盖、无预览对话框。

错误：`ArgumentException` / 读文件失败走 `ErrorOccurred`（`MainForm` 弹窗），文案用异常消息或 `MsgLintNeedInput`。

铬走 `Loc`。报告正文不翻译。

主题：`ControlsHelper.ApplyTheme(this)`，实现 `IThemedTab` 与 `IStatusSource`。

### G5 文档与版本

- `Directory.Build.props`：`2.5.0` → `2.6.0`
- README 功能表：lint 从「仅 CLI」改为 CLI + GUI 页签都有
- `ArchitectureGuide.md` 结构树补页签
- `TECH-DEBT.md` 验证锚点：版本、测试数、`MainForm` 行数按实测更新
- 关于页 `AboutDescription` 补一句检查能力

## 5. 实施顺序

```text
G1 LintRunner + 测试 → G2 CLI 改调 LintRunner → G3 语言键 → G4 页签 + MainForm → G5 文档与版本
```

质量门禁与上一轮相同：`dotnet build TextTool.sln -c Release -warnaserror`、全量测试、`dotnet format TextTool.sln --verify-no-changes`。

## 6. 风险

| 项 | 风险 | 缓解 |
|---|---|---|
| CLI 重构改变退出码 | 中 | 校验文案与条件逐字对齐现行 `RunLint`；不改 Filter / Format |
| 英文 UI 里报告仍是中文 | 低 | 规格写明；与 CLI 一致，避免本轮翻译规则 JSON |
| 页签偏挤（700x560） | 低 | 报告区吃剩余高度，可滚；不放大窗口 |
| 把检查做成改写入口 | 中 | 无保存按钮；状态栏写「只报不改」 |

## 7. 与上一轮规格的关系

`doc/specs/2026-09-26-texttool-lint-design.md` 仍是引擎与 JSON 契约的权威。本文只补 GUI 壳与 Core 编排器。冲突时：引擎字段 / 退出码 / `--only` 语义以上一轮为准；页签行为以本文为准。
