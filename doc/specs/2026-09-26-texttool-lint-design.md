# TextTool 中文 AI 味检查（lint）设计

> 日期：2026-09-26 ｜ 状态：待实施 ｜ 类型：设计文档（仅方案，不实施）
> 来源：HumanizerZh 技能的方法论目录（cc-switch 快照 `humanizer-zh`，24 条模式）与技能侧 `tone_lint.py`（10 个命中码 + 7 类统计项）的合并诉求
> 前置事实：本仓已有 9 个内置替换方案（`TextTool.Core/default_schemes.json`，176 条规则）；技能侧 `references/humanize-scheme.json` 有 108 条规则，其中 74 条与内置逐字节重复

## 1. 背景与目标

工具现有能力全是**改写**：合并、标点修复、字面替换、VN 排版。缺一类能力——**只报不改的检查**。技能侧虽然已有 `tone_lint.py` 干这件事，但它是 Python 脚本：无测试、无 CI、规则写死在代码里、改一个词要改 `py`，且与工具是两套代码。

同时，去 AI 味的方法论主体（24 条模式目录）停留在技能文档与 cc-switch 快照里，没有被任何可执行物消费。

**目标**：把中文 AI 味检查做成一等能力——**只报不改**，输出稳定 JSON 契约供 Claude 与技能消费；规则数据化，改词不改代码；`texttool` 成为唯一引擎，技能侧 Python 退休。

**成功标准**：给一段有 AI 味的文本，`texttool lint` 能报出命中位置与类别，JSON 可由 Claude 直接读来逐条改写；给一段干净的中文，误报在可接受范围内（靠 dogfood 校准）。

## 2. 范围

### In scope

| 编号 | 项 | 归属 |
|---|---|---|
| R0 | 修正两处嵌入式资源名（相邻缺陷，本模块复用同一机制，必须一起修） | Core + Tests |
| R1 | 规则模型与数据（`LintRule.cs` + `default_lint_rules.json` + 外部覆盖） | Core |
| R2 | 检查引擎与报告模型（`AiToneLintService.cs`） | Core |
| R3 | `texttool lint` 子命令（`--json` / `--only` / `--min-severity`） | CLI |
| R4 | 技能侧收编：`SKILL.md` 改调 CLI，`tone_lint.py` 退休 | 技能（仓外） |
| R5 | 文档与版本：README 双语功能表与结构树、TECH-DEBT 验证锚点、版本号 | 仓库 |

### Out of scope（明确不做）

- **GUI 第 6 个 Tab**：先用 CLI 跑顺，按需再评估（本设计保留 GUI 接入点：引擎是纯 Core 服务，无 UI 依赖）
- **目录递归批量**：本次只接受文件列表与 stdin
- **`--fix` 与任何自动改写**：只报不改是本模块的身份
- **删除 `ReplaceScheme.cs` / `VNCharacterScheme.cs` 里的内联硬编码兜底**（约 200 行重复数据）：R0 修好资源名后它们成为纯冗余，但删除属独立重构
- **`humanize-scheme.json` 与内置方案重复的 74 条收编**：动内置方案影响面大，单列待办
- **i18n**：CLI 现有输出为硬编码中文（`TextTool.Cli/Program.cs`），本模块保持一致，不新增 `Localization/*.json` 键
- **上游两条中文不适用项**：标题大写（#16）、弯引号（#18）——上游 SKILL.md 自己标注"此模式在中文中不太适用"

## 3. 方案选型

**选定：混合分层**——词表/句式类规则全数据化（JSON），统计/段落算法留 C# 服务。

理由：本次的核心收益是"改词不改代码"。凡是"一个字面串或正则 + 一句说明"的规则都能无损数据化；而变异系数、段首重复、括号全半角混用判定这类是算法，硬塞进 JSON 就得造一门带阈值、切片、聚合的规则 DSL——引擎复杂度陡增，收益为零。

备选（不推荐）：

- **全硬编码**（`tone_lint.py`、`VNReformatterService` 的路子）：最简单最快，但改一个词要改代码 + 重编译 + 跑全测，对一份持续演化的 AI 味词表，摩擦会迅速超过收益。
- **全数据驱动**：形式统一，实际要造解释器。YAGNI。

数据与算法分层在本仓已有先例：`default_schemes.json` 是数据，`VNReformatterService` 是算法。

## 4. 各项设计

### R0 相邻缺陷：嵌入式资源名写错（顺手修）

**现状（已实测）**：`TextTool.Core/ReplaceScheme.cs:41` 查 `"TextTool.Core.default_schemes.json"`，`TextTool.Core/VNCharacterScheme.cs:47` 查 `"TextTool.Core.default_vn_schemes.json"`。而已构建程序集里的真实资源名是 `TextTool.default_schemes.json` / `TextTool.default_vn_schemes.json` / `TextTool.pinned_roots.txt`——前缀取 `RootNamespace`（`TextTool`），不是程序集名。

查不到 → `stream` 为 `null` → 走 `GetHardcodedSchemes()` 内联兜底。两份数据**逐条完全相同**（9 方案 / 176 规则，已脚本比对），所以今天**没有行为差异**——但代价是 `default_schemes.json` 与 `default_vn_schemes.json` 成了死文件：改它们不会有任何效果，而 `doc/TECH-DEBT.md` 把 H2-4「内置方案迁 JSON」记为已落地。

**为什么本设计必须处理**：R1 复用同一机制，而"改词不改代码"正是本模块的核心卖点。照抄错误资源名 = 新功能静默失效，且症状是最难查的那种"改了 JSON 没反应"。

**为什么现有测试没拦住**：`TextTool.Tests/Services/DefaultSchemeTests.cs` 只断言"有内置方案且规则数 > 0"，而内联兜底同样满足——断言无法区分两条路径，注释里写的"验证资源路径正确"实际没验到。

**修复（实施中据实测修正过）**：两处资源名改为 `TextTool.*` 前缀，并提为 store 上的 `internal const string ResourceName`（对齐 `TextTool.Core/PinnedRoots.cs:15` 的 `ResourceName` 范式）；`TextTool.Core.csproj` 向 `TextTool.Tests` 开 `InternalsVisibleTo`，两个测试改为**引用该常量**并断言其可解析。

> **原设计的"测试里直查字面资源名"不成立**，已由实施实测证伪：资源名是否存在由 csproj 的 `EmbeddedResource` 决定，与 store 里的查询串**解耦**——那种测试改前改后都通过，拦不住"查询串被改回错值"。改为引用常量后测试与生产代码耦合：把常量临时设回旧错值，两个测试均以 `Assert.NotNull() Failure` 失败，改回即通过（真实 RED/GREEN 已取证）。

内联兜底**保留不删**——它现在是双份真相的源头，但删除约 200 行重复数据是独立的重构，列入范围之外。`PinnedRoots` 仍是参照范式：名字对，且资源缺失时抛 `InvalidOperationException` 而非静默兜底。

### R1 规则模型与数据

**两个来源，一套 Id。**

- 内置：`TextTool.Core/default_lint_rules.json`，嵌入式资源。**资源名常量必须写 `TextTool.default_lint_rules.json`**（`RootNamespace` 决定前缀，不是程序集名）——`TextTool.Core/ReplaceScheme.cs:41` 与 `VNCharacterScheme.cs:47` 写成了 `TextTool.Core.*`，查不到后**静默走内联硬编码兜底**，两个 `default_*.json` 实际是死文件（详见 R0）
- 覆盖：`lint_rules.json`，与 `replace_schemes.json`（含 `replace_rules.json`）同级，生产路径为**程序目录**（`AppContext.BaseDirectory`，ADR-009 单目录哲学），按 `id` 覆盖同名、追加新 id、其余保留

> **取舍**：`ReplaceSchemeStore.Load()` 的既有语义是"外部文件非空即整体替换内置"（`TextTool.Core/ReplaceScheme.cs:23-27`）。本模块改用**按 id 合并**——词表类规则的价值就在"只写你想改的那条"，要求用户把 19 条内置规则整体复制出去维护，等于把改词成本原样推回去。技能侧 `install_scheme.py` 对方案做合并，取向一致。

**规则 schema**：文件是**裸数组**（与 `replace_schemes.json` 同形），直接复用 `JsonFileStore` + `AtomicFile` 拿到原子写与"文件损坏即响亮抛 `InvalidDataException`"的既有错误策略（`TextTool.Core/JsonFileStore.cs:31-35`）。**不写版本字段**——当前无迁移需求，YAGNI。字段名用 **PascalCase**，与 `default_schemes.json` 及 `System.Text.Json` 默认绑定一致（默认区分大小写，写成 camelCase 会静默绑不上）。

```json
[
  {
    "Id": "S3",
    "Group": "S",
    "Title": "结尾升华",
    "Kind": "regex",
    "Patterns": ["不仅仅", "总而言之", "综上", "由此可见", "未来可期"],
    "Scope": "paragraph",
    "TailChars": 30,
    "MinCount": 1,
    "Severity": "warn",
    "Detail": "段末拔高收尾",
    "Hints": [],
    "SuggestScheme": null
  }
]
```

| 字段 | 取值 | 说明 |
|---|---|---|
| `Id` | `P1`–`P8` / `L1`–`L7` / `S1`–`S6` | 稳定标识，也是 `--only` 的取值 |
| `Group` | `P` / `L` / `S` | 报告里的分组。数据规则只有这三组；`P4`/`P5` 与 `C` 组是算法规则，元数据在代码内 |
| `Title` | 字符串 | 报告里给人看的中文名 |
| `Kind` | `literal` / `regex` | 无第三条——算法规则不进 JSON |
| `Patterns` | 字符串数组 | **数组而非单串**：P1 有 2 条正则、L1 有 26 条词，单字段装不下。`literal` 时为字面串，`regex` 时为 .NET 正则 |
| `Scope` | `document` / `paragraph` | 默认 `document` |
| `TailChars` | 整数或省略 | 仅 `Scope=paragraph` 有效，只检查段末 N 字（S3 用） |
| `MinCount` | 整数，默认 1 | **每条 Pattern 各自**在作用域内的出现次数达到才报；`Scope=paragraph` 时按**段各自**判定（同段凑够次数才算，分散在多段的单次出现合计不算）。用于压误报（L2、S2 用） |
| `Severity` | `info` / `warn` | `warn` = 高度可疑值得改；`info` = 可能是合法用法，报给你判断 |
| `Detail` | 字符串 | 报告里给 Claude 的一句话说明（规则级，回答"这是什么毛病"） |
| `Hints` | 字符串数组或省略 | 与 `Patterns` **下标对齐**的建议改法，报告里随命中给出（L7 用）；长度不匹配即校验失败 |
| `SuggestScheme` | 字符串或 `null` | 机械项对应的替换方案名，报告里直接告诉用户该跑哪条 |

**加载即校验**，任一条不合法（id 重复、kind 非法、regex 语法错、scope 非法）→ 抛 `ArgumentException` → CLI 报错退 2。**不静默降级**，与 `RegexGuard` 文档里"宁可响亮失败，不可静默卡死"的既有立场一致。所有 `regex` 必须经 `TextTool.Core/RegexGuard.cs` 的 `Create` 构造（2s 超时，防 ReDoS）——规则来自可编辑的 JSON，这条尤其必要。

**规则表**（四组 26 条 = 数据规则 19 条进 JSON + 算法规则 7 条 `P4`/`P5`/`C1`–`C5` 在代码内）：

P 组——机械层（标点与字符，8 条）：

| Id | 名称 | 形态 | 严重度 | 来源 | suggestScheme |
|---|---|---|---|---|---|
| P1 | 重复标点 | regex ×2 | warn | tone_lint P1 | 重复矛盾标点整理 |
| P2 | 省略号风格 | regex ×3 | warn | tone_lint P2 | 去AI味标点归一（注） |
| P3 | 全半角混用 | regex ×2 | warn | tone_lint P3 | 去AI味标点归一（注） |
| P4 | 引号风格混用 | algorithm | info | tone_lint 统计项 | 引号括号统一 |
| P5 | 括号全半角混用 | algorithm | info | tone_lint 统计项 | 引号括号统一 |
| P6 | 破折号 / 波浪号 | regex ×4 | info | tone_lint P6 | 去AI味标点归一（注） |
| P7 | 隐形字符 | regex ×8（不可用 literal，见 R1 脚注） | warn | tone_lint P7 | 去AI味标点归一（注） |
| P8 | 表情与装饰符号 | regex | info | 24 条 #17 | 特殊符号清除 |

> 注：`去AI味标点归一` 不是内置方案，由技能侧 `scripts/install_scheme.py` 装到 exe 目录；方案名照实输出，解析不到时报告仍给出名字（见 R3）。
>
> 注（P7 为何不用 `literal`）：`literal` 在引擎里走 `Regex.Escape`，而 JSON 里的 `\\u200B` 解码后是**六个普通字符**（反斜杠 u 2 0 0 B），再被 Escape 转义一次，于是只会匹配文本里字面的 `\u200B` 序列——真实文本里的 U+200B 永远匹配不到，规则静默失效。写成 `regex` 则由正则引擎把 `\u200B` 解释为真字符，且文件里仍看得见转义（优于在 JSON 里嵌真实不可见字符）。推论成一条不变量并由测试守住：**`literal` 规则的 Patterns 不得含反斜杠**。`L2`/`L7` 是纯中文文本，用 `literal` 正确。

L 组——词汇层（7 条，全部数据化）：

| Id | 名称 | 形态 | 严重度 | 来源 |
|---|---|---|---|---|
| L1 | 套话词 | regex ×26 | info | tone_lint S5（原码迁入） |
| L2 | AI 高频词 | literal，minCount 阈值 | info | 24 条 #7 |
| L3 | 模糊归因 | regex | warn | 24 条 #5 |
| L4 | 知识截止免责 | regex | warn | 24 条 #20 |
| L5 | 协作交流痕迹 | regex | warn | 24 条 #19 |
| L6 | 谄媚语气 | regex | warn | 24 条 #21 |
| L7 | 填充短语 | literal + hint | info | 24 条 #22 |

S 组——句式层（6 条）：

| Id | 名称 | 形态 | 严重度 | 来源 |
|---|---|---|---|---|
| S1 | 对举句式 | regex ×6 | warn | tone_lint S1 |
| S2 | 段内序数词 | regex + scope=paragraph + minCount=2 | warn | tone_lint S2 |
| S3 | 结尾升华 | regex + scope=paragraph + tailChars=30 | warn | tone_lint S3 |
| S4 | 定语堆叠 | regex | info | tone_lint S4 |
| S5 | 系动词回避 | regex | info | 24 条 #8 |
| S6 | 内联标题列表 | regex | info | 24 条 #15 |

C 组——篇章统计（5 条，全部是代码里的算法规则，产出进 `notes` 不进 `hits`）：

| Id | 名称 | 判据 |
|---|---|---|
| C1 | 段落长度过于均一 | 段数 ≥5 且变异系数 <0.25 |
| C2 | 句长分布过于整齐 | 句数 ≥10 且变异系数 <0.35 |
| C3 | 段首两字高度重复 | 段数 ≥4 且同一词头 ≥3 次 |
| C4 | 叹号密度偏高 | 字数 ≥400 且 >3‰ |
| C5 | 序数词骨架密度 | 全文 `首先/其次/最后` 类 ≥2 次 |

**新旧码映射**（技能侧文档需同步）：`P1/P2/P3/P6/P7`、`S1/S2/S3/S4` 原义保留；`S5 套话词` → `L1`；原先只作统计项输出的引号风格、括号全半角提升为 `P4/P5` 命中；其余统计项 → `C1`–`C5`。

**未纳入的条目与去向**（诚实记录覆盖率）：

| 上游条目 | 去向 |
|---|---|
| #1 过度强调意义、#2 知名度罗列、#4 宣传语言、#6 挑战展望段、#10 三段式、#11 同义词循环、#12 虚假范围 | 属句间/篇章语义，机械化必大量误报。留在技能侧 `references/` 供模型判断 |
| #24 通用积极结论 | 段末作结的那一类由 `S3 结尾升华` 覆盖；句中的乐观套话靠模型判断 |
| #3 句末动宾尾缀（中文对应 `-ing` 肤浅分析） | 建议后续以 `S7` 加入；本次不纳入，因它与 `S3 结尾升华` 的边界需先 dogfood 校准，避免同一处重复报 |
| #14 粗体过度 | 纯文本稿无粗体标记；Markdown 场景再加 |
| #16 标题大写、#18 弯引号 | 中文不适用（上游自注） |

### R2 引擎

新增 `TextTool.Core/AiToneLintService.cs`（纯 Core，零 UI 依赖，可无 UI 测试——满足 `ArchitectureGuide.md` 的分层规则）与 `TextTool.Core/LintRule.cs`（模型 + `LintRuleStore`）。

**执行顺序**：按 `document` 作用域规则 → `paragraph` 作用域规则 → 算法检出器。每个命中记录：`id`、`group`、`title`、`severity`、`line`、`col`、`length`、`snippet`（前后各 8 字，换行截断）、`detail`、`suggestScheme`。

**位置计算**：沿用 `tone_lint.py` 的做法——全文偏移量换算行列（`line = 该偏移之前换行数 + 1`），逐行累计偏移定位段落，**不用"在全文里找第一次出现"**（重复段落会定位错，这是 `tone_lint.py:193-203` 注释里已记录的坑）。

**算法命中的定位**：`P4`/`P5` 这类"多处样本共同构成一个结论"的规则，报告**首处**出现的位置，混用的规模写进 `detail`（如 `半角 12 个 / 全角 37 个`）——位置用于跳转，规模用于判断严重程度。

**不可见字符的呈现**：隐形字符不能直接打印（Windows 控制台 GBK 码页编码不出 U+200B，会抛异常），报告里转义为 `\u200b` 形式。JSON 输出同理。

**报告契约**（多文件统一包装，消费者不必按文件数分支）：

```json
{
  "Version": 1,
  "Reports": [
    {
      "File": "chapter1.md",
      "Chars": 12345,
      "Hits": [
        {
          "Id": "S1", "Group": "S", "Title": "对举句式", "Severity": "warn",
          "Line": 12, "Col": 5, "Length": 7,
          "Match": "不是数据不够，而是口径不同",
          "Snippet": "…不是数据不够，而是口径不同…",
          "Detail": "不是A而是B", "Hint": null, "SuggestScheme": null
        }
      ],
      "Notes": [{ "Id": "C1", "Text": "段落长度过于均一（变异系数 0.18，12 段）" }]
    }
  ]
}
```

字段同样用 PascalCase。`Match` 是被命中的原文——词表类规则靠它告诉 Claude 到底命中了哪个词；`Snippet` 是带上下文的展示片段；`Hint` 是下标对齐出来的建议改法（无则 `null`）。

`Notes` 是统计观察，**不计入退出码**——统计项不该让 CI 变红。

**退出码**：`0` 无命中 / `1` 有命中 / `2` 用法或读取错误（含规则加载失败、正则超时）。多文件时任一文件有命中即 `1`。

命中判定基于**过滤后**的集合——`--only` 与 `--min-severity` 生效后再判空。于是 `--min-severity warn` 的语义就是"只在有 `warn` 命中时退 1"，这是 CI 关卡的调参旋钮（`notes` 始终不参与）。

### R3 CLI

```
texttool lint <文件...> [--json] [--only S1,L1] [--min-severity warn]
```

- 文件参数支持多个；`-` 表示从 stdin 读（沿用工具批量处理与管道两种用法）
- `--only` 只跑指定 id（逗号分隔，大小写不敏感），同时作用于 hits 与 notes
- `--min-severity warn` 只输出 `warn` 及以上的命中，`notes` 不受影响
- `--json` 输出上述契约
- 编码识别沿用 `EncodingDetector`（BOM → UTF-8 → GBK），与其它子命令一致

**人读输出**：

```text
[命中] S1 对举句式（3 处）
       第 12 行 第 5 列  …不是数据不够，而是口径不同…  <- 不是A而是B
[统计]
       - C1 段落长度过于均一（变异系数 0.18，12 段）
[NOTE] 全文 12345 字，命中 3 处，统计项 1 条；本命令只报位置，改写由判断层完成
```

**与其它子命令的差异必须写进 help**：工具现有子命令是"0 = 成功、1 = 失败"，而 `lint` 用"1 = 有命中"——这是为 CI 关卡与技能编排保留的语义，照抄 `tone_lint.py` 的既有契约。

### R4 技能侧收编（仓外）

路径：`E:\work_zone\ClaudeCode\home\.claude\skills\HumanizerZh\`

- `SKILL.md` 重写为三步：跑 `texttool lint --json` 拿清单 → 按 `suggestScheme` 跑机械层方案 → 读 hits 逐条判断改写。新旧码映射表（见 R1）写进去
- 删除 `scripts/tone_lint.py` 与空目录 `scripts/__pycache__`
- 保留 `scripts/install_scheme.py` 与 `references/humanize-scheme.json`（机械层仍要装方案）
- 技能从此依赖已构建的 exe——**现状已如此**（`install_scheme.py` 段落本就要求先 `dotnet build`），非新增依赖

### R5 文档与版本

- `README.md`：双语功能表加 `lint` 一行、项目结构树补 Core 三个新文件与 CLI 命令表、用法段落
- `doc/TECH-DEBT.md`：验证锚点的测试数期望值（现 139）随新增测试更新
- `Directory.Build.props`：`2.4.4` → `2.5.0`（新增功能，minor）

## 5. 实施顺序与验证

```text
R0 修资源名（先做，本模块复用同一机制）→ R1 模型与数据 → R2 引擎（先写测试）→ R3 CLI
  → R5 文档与版本 → R4 技能侧收编
```

- **质量门禁**（pre-commit + CI 自动跑）：`dotnet build -warnaserror`、全测试通过、`dotnet format --verify-no-changes`（CI 的 `code-quality` job）
- **测试**（`TextTool.Tests/Services/AiToneLintServiceTests.cs`、`LintRuleStoreTests.cs`，先写测试后实现）：
  - 四组规则各至少一正例一负例（命中 / 不命中）
  - `minCount` 阈值生效：L2 单词出现一次不报、达阈值才报
  - `scope=paragraph` + `tailChars` 生效：段末命中、段中不命中
  - 位置准确性：重复段落中第二处的行列号正确（覆盖 `tone_lint.py` 记录过的坑）
  - store：外部 `lint_rules.json` 按 id 覆盖内置、追加新 id、其余保留
  - 规则非法（regex 语法错、id 重复）→ 响亮抛错
  - 隐形字符在文本输出与 JSON 中均不抛异常
- **dogfood 验证**：拿本仓 `README.md`、`doc/*.md` 与一篇真实中文稿跑一遍，人工判读误报，据此调 `severity` 与 `minCount`——这一步是调参，不是改算法
- **技能侧验证**：`SKILL.md` 改完后跑一次真实文本，确认 lint 报告可读、`suggestScheme` 指向的方案名能解析

## 6. 风险

| 项 | 风险 | 缓解 |
|---|---|---|
| 词表误报（套话词在技术文/小说里合法） | 中 | 分级 `warn`/`info` + `MinCount` 阈值 + **只报不改**；dogfood 校准 |
| R0 修好资源名后，内置方案改由 JSON 提供 | 低 | 已实测两份数据逐条相同，行为不变；R0 的测试覆盖资源可达性 |
| 正则性能 | 低 | 全部经 `RegexGuard`（2s 超时），超时响亮报错不挂起 |
| 外部规则被改坏导致不可用 | 低 | 加载即校验，非法即抛错退 2；内置默认始终可用 |
| 规则散在数据与代码两处，边界模糊 | 低 | 规格写明：JSON 只放 `literal`/`regex`；算法规则元数据在代码内且只产 `notes` |
| 技能侧断链（exe 未构建） | 低 | 现状已依赖 exe，`SKILL.md` 保留"先 `dotnet build`"的前置说明 |
| 报告被当判决书（命中即全改） | 中 | `notes` 与 `hits` 分离；`SKILL.md` 与输出尾行都写明"只报位置，命中是候选不是判决" |
