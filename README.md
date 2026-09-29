# TwilightRain Text Tool

集行合并、文件拼接、中文截断修复、标点截断修复、标点替换于一体的 Windows 文本处理工具。  
An all-in-one Windows text processing tool: line merge, file join, CJK truncation fix, punct. truncation fix & punctuation replacement.

---

[English](#english) · [中文](#中文)

---

## English

A **Windows WinForms (.NET 8)** desktop utility that replaces four legacy batch/PowerShell scripts with a unified GUI.  
Drag-and-drop a text file, pick your options, click **Process** — done.

### Features

| Tab / CLI | Description |
| :-- | :---------- |
| **Line Merge** | Merge short lines up to a configurable threshold, with optional post-processing |
| **File Join** | Concatenate all matching files in a directory into one |
| **Punct. Replace** | Freely configurable find-&-replace rules with reordering, persisted as JSON |
| **Visual Novel** | Reformat VN scripts to natural paragraphs, complete dialogue punctuation, character/route presets |
| **AI-tone Lint** | GUI tab and `texttool lint` share the same engine — reports only, never rewrites |
| **About** | Version, author, avatar, GitHub link, language selector |

#### Tab 1 · Line Merge

- **Threshold merging** — continuously append short lines until a byte/character threshold is met
- Dual mode: **byte count** or **character count** (1–1000 range)
- **Auto-detect encoding** — BOM → UTF-8 validation → GBK fallback
- **Drag-and-drop** `.txt` files onto the window
- **Safe output** — saves as `*_Processed.*`, never overwrites originals

**Post-processing options (applied in-memory after threshold merge):**

| Option | Description |
|--------|-------------|
| **Fix CJK truncation** | Merge next line if current line ends with a CJK character (e.g., `Hello\nWorld` → `HelloWorld`) |
| **Fix punct. truncation** | Merge next line if current line ends with any custom punctuation (e.g., `回家吧，\n孩子` → `回家吧，孩子`). Customizable punctuation set. |
| **Apply punct. replace rules** | Run the punctuation replacement rules configured in Tab 3 |
| **Line-ending no-merge** | Prevent merging if current line ends with any custom punctuation — overrides both CJK and punct. truncation rules. Useful for keeping chapter titles (`章`, `节`, etc.) or sentence terminators (`.!?。！？`) independent. |

**Processing pipeline** (single-pass, one file write):

```text
Read file → Threshold merge → CJK fix → Punct. truncation fix → Punct. replace → Write output
```

#### Tab 2 · File Joiner

- Merge all files matching a glob pattern in a directory into one output file
- Files sorted by name before merging
- Each file re-read with its own detected encoding
- Unified output as **UTF-8 with BOM**
- Safe output: `*_Processed.*` naming

#### Tab 3 · Punctuation Replacement

- **Freely configurable** find-&-replace rules (e.g., `。。` → `。`)
- **Reordering** — move rules up/down with dedicated buttons
- Rule list with live preview
- Add, update, and delete rules
- **JSON persistence** — `replace_rules.json` at runtime, survives restarts
- Drag-and-drop `.txt` file support (single or batch)
- **Batch processing** — select multiple files, process all at once
- Safe output: `*_Processed.*`

#### Tab 4 · Visual Novel

- **Reformat** — rebuild VN scripts from fixed-width hard-wrapped lines into natural paragraphs
- Auto-classifies lines into **dialogue / narrative / scene markers / route headings**
- Recognizes `「角色」「台词」` and `角色名「台词」` / `角色名，「台词」` dialogue patterns
- **Dialogue punctuation completion** — appends `。` to dialogue lines missing sentence-end punctuation
- **Character & route presets** — bundled Steins;Gate / Attack on Titan / Kara no Kyoukai / Subahibi schemes, editable via dialog
- Max paragraph length adjustable (100–2000)
- Long narrative paragraphs auto-split at sentence boundaries
- Three modes: **All-in-One** (reformat + fix punct.), **Reformat only**, **Fix Punct. only**
- Safe output: `*_Processed.*`

#### Tab 5 · About

- Program icon (64×64) + **TwilightRain avatar** (64×64) side by side
- App name + version side by side
- Author + GitHub link side by side
- Description
- **Language selector** — switch between 简体中文 / 繁體中文 / English on the fly
- Language preference persisted to `app_config.json`

### i18n / Localization

Three built-in languages, auto-detected from system UI culture:

| Language | Code | File |
|----------|------|------|
| 简体中文 | `zh_CN` | `Localization/zh_CN.json` |
| 繁體中文 | `zh_TW` | `Localization/zh_TW.json` |
| English | `en_US` | `Localization/en_US.json` |

- System language auto-detection matches `zh-CN`, `zh-TW`, `zh-HK`, `zh-MO`, `en-*`
- Manual override via the About page language combo
- Choice saved in `app_config.json`

### Requirements

- **.NET 8 Desktop Runtime** (Windows WinForms)
- Download from: [dotnet.microsoft.com/download/dotnet/8.0](https://dotnet.microsoft.com/download/dotnet/8.0)
- Choose **.NET Desktop Runtime 8.0 (x64)**
- Missing runtime shows a guided download dialog

### Installation (end users)

Download the latest `TextTool-GUI-*-win-x64.zip` from the
[GitHub Releases](https://github.com/TwilightRainDev/TwilightRainTextTool/releases),
extract it, and double-click `TextTool.exe`. Requires .NET 8 Desktop Runtime
(see Requirements above); a missing runtime shows a guided download dialog.

Coming from `v2.6.1` or earlier, the in-app updater cannot take you here —
download the zip and unzip over the old folder by hand
(see [`doc/UpgradeNotes.md`](doc/UpgradeNotes.md)).

### Running

#### Option A — Build then run

```bash
dotnet build TextTool.sln -c Release
# Output → bin/Release/net8.0-windows/TextTool.exe
double-click bin/Release/net8.0-windows/TextTool.exe
```

#### Option B — From source

```bash
dotnet run --project TextTool.csproj
```

#### Publishing (same command as CI)

```bash
dotnet publish TextTool.csproj -c Release -r win-x64 -o publish/TextTool
# Output → publish/TextTool/   (official release artifact is the CI-built zip)
```

> Note: do not point `-o` at `bin/Release` — the SDK never cleans old files, so
> publish output would mix with build artifacts and leave stale-version files
> behind (the .NET host may then load the wrong assembly). To clean local build
> output, delete the `bin/` and `obj/` directories and rebuild.

> Note: a RID build (`-r win-x64`) writes its intermediate output to
> `bin/Release/net8.0-windows/win-x64/` — that is the SDK's standard output
> location for RID builds (publish copies from there to `-o`), not a backup
> copy. It is already gitignored; no need to delete it, the next publish
> reuses it incrementally.

#### Command Line (CLI)

`texttool` — the same engine, scriptable:

```bash
texttool merge <file...> [options]     Line merge + post-processing
texttool replace <file...> [options]   Punctuation replacement
texttool vn <file...> [options]        Visual novel reformatting
texttool join <dir> [options]          File joining
texttool lint <file...> [options]      AI-tone check, report only
texttool update [--check]              Self-update (--check = check only)
```

Run `texttool <command> --help` for each command's options. `update` downloads the new CLI zip from the GitHub Release, verifies its SHA-256, and swaps itself in (with `--check` you can check for a new version without updating). It cannot upgrade a client on `v2.6.1` or earlier — those installs have to be replaced by hand (see [`doc/UpgradeNotes.md`](doc/UpgradeNotes.md)).

`lint` takes one or more files (`-` reads stdin) and changes nothing: it reports where
Chinese AI-flavored wording appears, with rule id, line and column. `--json` emits the
machine-readable contract, `--only S1,L1` runs selected rule ids only, `--min-severity warn`
keeps the output to `warn` hits. Rule ids `P4`/`P5` and `C1`-`C6` come from the built-in
detectors, so an external `lint_rules.json` that defines one of them is rejected when rules
load and the command exits `2` — see [`doc/UpgradeNotes.md`](doc/UpgradeNotes.md).

**Exit codes differ from the other subcommands.** `merge` / `replace` / `vn` / `join` /
`update` use `0` = success, `1` = failure; `lint` uses `0` = no hits, `1` = hits found,
`2` = usage or read error. The hit test runs after filtering, so `--min-severity warn`
means "exit 1 only when a `warn` hit exists".

**`--json` contract.** One wrapper regardless of how many files are passed, so consumers
never branch on file count:

```json
{
  "Version": 2,
  "Reports": [
    {
      "File": "chapter1.md",
      "Chars": 12345,
      "ParagraphMode": "markdown",
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

Fields are PascalCase. The wrapper's `Version` is `2` — the gate for this contract, since
version 1 did not promise the line endings, the escape set, the hit ordering or
`ParagraphMode`. `Match` is the matched source text — word-list rules use it to name
the word that fired; `Snippet` is the surrounding context; `Hint` is the index-aligned
suggested rewrite, `null` when there is none. `ParagraphMode` is the paragraph model the
scan used, `line` or `markdown` (see Heuristic limits below). `Notes` carries statistical
observations and never affects the exit code. Invisible characters are escaped as `\u200b`
in both the human-readable and the JSON output.

What the contract guarantees:

- **Line endings are LF** in both the human-readable report and `--json`.
- **Escaping.** `"`, `&`, `'`, `+`, `<`, `>` and the backtick are escaped as `\uXXXX`
  (`\u0022` `\u0026` `\u0027` `\u002B` `\u003C` `\u003E` `\u0060`); a backslash becomes `\\`;
  Chinese is emitted as-is, as are `/ ~ % @ = | !`. Invisible characters are escaped the
  same way but by a second code path, which writes **lowercase** hex (`\u200b`), while the
  JSON encoder writes uppercase (`\u002B`) — both parse identically.
- **`Hits` is sorted by `(Line, Col)` ascending**, and hits sharing a position keep engine
  order (rule table order, then pattern order).
- **`Match` and `Snippet` are contiguous slices of the source text.** A hit that spans a
  line break keeps the newline in both: the human-readable report renders it as `\u000a`,
  while the JSON text holds `\n`. Input is not line-ending-normalized on read, so with CRLF
  input these fields can contain a CR too (`\u000d` in the report, `\r` in the JSON text).

The human-readable report groups hits by rule; groups appear in the order of each group's
first hit position, which is not rule-table order.

**Heuristic limits.** `P4` (quote style) and `P5` (bracket width) match characters without
context: the `'` in an English word such as `don't` counts as a quote style, so a Chinese
quote elsewhere in the same text is reported as mixed, and ASCII brackets written inside a
code sample count towards bracket-width mixing. The `C` group (`C1`-`C6`) is statistical
observation and never affects the exit code. `P4`/`P5` are all `info` level — reported by
default, and excluded from a gate by `--min-severity warn`.

**Paragraph model.** The model is detected per file, from the first 4096 characters only: when blank
lines make up at least 10% of the lines counted there (a trailing newline does not count as
a line), the file is read as Markdown — paragraphs split on blank lines, soft-wrapped lines
join into one paragraph, and list items, block quotes and numbered lines each form their own
paragraph. Otherwise every non-blank line is one paragraph. Measured samples: real Markdown
files land at 7.7%–53% blank lines, line-oriented text at 0%. The threshold is deliberately
conservative, so a miss degrades to the line model rather than misfiring on line-oriented
text. The model used is reported as `ParagraphMode`, and in the `[NOTE]` line of the
human-readable report. Under the line model a soft-wrapped paragraph is split per line, so
the paragraph-scoped rules `S2` (ordinals inside one paragraph) and `S3` (elevated ending)
can under-report — a known boundary.

**Two rules may report the same span.** `。。。。` fires both `P1` and `P2`, and other
overlapping pairs behave the same way. That is **intentional**: the two rules carry
different suggestions, and de-duplicating in the report layer would drop advice and break
the `--only` contract (the surviving id decides whether the hit is emitted at all).
Consumers that want one hit per span have to de-duplicate on their side.

**One side effect of a suggested scheme.** `引号括号统一` rewrites full-width brackets to
half-width, and a half-width bracket next to Chinese is exactly what `P3` reports (measured:
`（中文）` scores no hit, `(中文)` scores two). Re-lint after applying it; it is not a `P3`
remedy.

### Project Structure

```text
TextTool/
├── TextTool.sln                  # Solution file
├── TextTool.csproj               # .NET 8 WinForms, v3.0.0
├── Directory.Build.props         # Centralized version (3.0.0)
├── Program.cs                    # Entry point, registers GBK encoding
├── MainForm.cs                   # Main window (214 lines, hosts 6 tabs)
│
├── Controls/                     # Tab pages (extracted from MainForm)
│   ├── MergeTabControl.cs        # Tab 1: Line merge (drag-drop, batch, preview)
│   ├── JoinTabControl.cs         # Tab 2: File join (directory + pattern)
│   ├── ReplaceTabControl.cs      # Tab 3: Punct. replace (CRUD, batch processing)
│   ├── VNTabControl.cs           # Tab 4: Visual novel (reformat, punct. fix, presets)
│   ├── LintTabControl.cs         # Tab 5: AI-tone lint (report only, shared engine)
│   ├── VNCharacterSchemeForm.cs  # Character/route preset selection & editing
│   ├── SchemeSelectionForm.cs    # Punct.-replace scheme selection dialog
│   ├── SchemeSelectionFormBase.cs# Shared base for the two scheme dialogs
│   ├── IThemedTab.cs             # Tab interface for theme traversal
│   ├── AboutTabControl.cs        # Tab 6: About, language, dark mode, config I/O
│   └── PreviewForm.cs            # Preview dialog (merge result before saving)
│
├── TextTool.Core/                 # Pure logic class library (no UI deps)
│   ├── EncodingDetector.cs        # BOM → UTF-8 → GBK auto-detection (+ strict mode)
│   ├── LineMerger.cs              # Threshold-based line merge algorithm
│   ├── FileJoiner.cs              # Multi-file directory concatenation
│   ├── CjkParagraphMerger.cs      # CJK truncation fix + no-merge support
│   ├── PunctTruncationMerger.cs   # Punctuation truncation fix + no-merge support
│   ├── PunctuationReplacer.cs     # Find-&-replace engine + ReplaceRuleStore
│   ├── ProcessingPipeline.cs      # Single-pass pipeline orchestration
│   ├── VNReformatterService.cs    # VN script reformatting engine (lines→paragraphs)
│   ├── PunctFixerService.cs       # Dialogue punctuation completion
│   ├── VNCharacterScheme.cs       # Character/route preset scheme model & store
│   ├── ReplaceScheme.cs           # Replace-rule scheme model & store
│   ├── BackupHelper.cs            # Auto-backup before overwrite (with rotation)
│   ├── AtomicFile.cs              # Atomic output write (temp file + replace)
│   ├── DialogueLine.cs            # Shared dialogue-line regex detection
│   ├── RegexGuard.cs              # Regex construction with ReDoS timeout
│   ├── JsonFileStore.cs           # Generic JSON file persistence
│   ├── PathHelper.cs              # Shared file path utilities
│   ├── TextUtils.cs               # Extension methods (EndsWithAny, etc.)
│   ├── LintRule.cs                # AI-tone rule model + store (per-id merge with external file)
│   ├── LintReport.cs              # Lint report model and JSON contract
│   ├── AiToneLintService.cs       # AI-tone check engine (data rules + algorithmic detectors)
│   ├── LintTextFormatter.cs       # Human-readable report rendering (invisible chars escaped)
│   ├── LintRunner.cs              # Shared CLI/GUI lint orchestration (validate + scan + filter)
│   ├── UpdateChecker.cs           # GitHub latest-release version check
│   ├── UpdateClient.cs            # Update-only HttpClient (pinned roots, no redirects)
│   ├── ReleaseVerifier.cs         # Release zip signature verification (ECDsa P-256)
│   ├── ReleaseSigningPublicKey.cs # Publisher public key constant (SPKI base64)
│   ├── PinnedRoots.cs             # Public root CA allow-list (SPKI)
│   ├── LastKnownVersion.cs        # Last installed version (downgrade-replay detection)
│   ├── SelfUpdater.cs              # CLI self-update: download, verify, staged swap
│   ├── default_schemes.json       # Embedded default replace schemes
│   ├── default_vn_schemes.json    # Embedded default VN schemes
│   ├── default_lint_rules.json    # Embedded default AI-tone rules
│   └── pinned_roots.txt           # Embedded root CA list (certifi)
│
├── TextTool.Cli/                  # Command-line entry (texttool.exe)
│   └── Program.cs                 # merge / replace / vn / join / lint / update subcommands
│
├── Services/                      # UI-adjacent services
│   ├── ThemeManager.cs            # Semantic color palette (dark/light mode)
│   ├── ThemedFlatButton.cs        # Flat button with disabled-state ForeColor fix
│   ├── ControlsHelper.cs          # Shared UI factories (buttons, labels, dialogs)
│   └── IStatusSource.cs           # Status event interface for tabs
│
├── Localization/                  # i18n
│   ├── Strings.cs                # Loc singleton (auto-detect, switch, persist)
│   ├── zh_CN.json                # Simplified Chinese locale
│   ├── zh_TW.json                # Traditional Chinese locale
│   └── en_US.json                # English locale
│
├── Resources/
│   ├── icon.ico                  # App icon
│   └── TwilightRain.jpg          # Avatar in About page
│
├── TextTool.Tests/               # Unit tests (xUnit, 245 [Fact]/[Theory])
│   ├── TextTool.Tests.csproj
│   ├── TestHelpers.cs
│   └── Services/                 # One test file per service
│
├── doc/                          # Documentation
│   ├── ArchitectureGuide.md      # Developer architecture guide
│   ├── Publish.md                # Release checklist
│   ├── UpdateSecurity.md         # Update trust model (TLS pinning, signing)
│   ├── ReplaceSchemesDesign.md   # Replace-scheme design note
│   ├── TECH-DEBT.md              # Open optimizations and known debt
│   ├── adr/                      # Architecture Decision Records (9 ADRs)
│   ├── specs/                    # Living / in-flight specs
│   └── archive/                  # Closed specs, plans, landed debt
│
├── scripts/
│   ├── github-release.ps1        # GitHub REST helpers (curl; no gh)
│   ├── publish.ps1               # Sign an existing Release and upload .sig
│   └── release.ps1               # Tag, wait for CI, then publish.ps1
│
├── .github/workflows/
│   └── build-test.yml            # CI: build + test + format + publish on release
│
├── replace_rules.json            # Runtime-generated rules (auto)
├── app_config.json               # Language & theme preferences (auto)
├── LICENSE                       # MIT License
└── README.md                     # This file
```

### Tech Stack

| Component | Technology |
|-----------|-----------|
| Framework | .NET 8 WinForms |
| UI construction | Pure C# (programmatic, no Designer files) |
| Encoding (GBK) | `System.Text.Encoding.CodePages`, built into the .NET 8 shared framework (no NuGet package) |
| Output encoding | UTF-8 with BOM |
| Persistence | JSON (`System.Text.Json`) |
| i18n | Custom `Loc` singleton with JSON locale files |

### Version History

Per-version changelog is carried by git tags and GitHub Releases — see
[Tags](https://github.com/TwilightRainDev/TwilightRainTextTool/tags) and
[Releases](https://github.com/TwilightRainDev/TwilightRainTextTool/releases)
(14 tags, latest `v3.0.0`). This file does not duplicate it.

---

## 中文

集行合并、文件拼接、中文截断修复、标点截断修复、标点替换于一体的 **Windows 文本处理桌面工具**，一键替代四个旧版批处理脚本。

### 功能概览

| 页签 / CLI | 说明 |
| :-- | :--- |
| **行合并** | 短行自动拼接至指定阈值，可选多重后处理 |
| **文件拼接** | 将目录中所有匹配文件合并为一个 |
| **标点替换** | 自由配置查找/替换规则，支持排序，JSON 持久化 |
| **视觉小说** | 将 VN 脚本重排为自然段落、补全对话标点、角色/路线预设方案 |
| **AI 味检查** | GUI 页签与 `texttool lint` 共用引擎，只报不改 |
| **关于** | 版本、作者、头像、GitHub 链接、语言切换 |

#### Tab 1 · 行合并

- **阈值合并** — 短行持续合并，直到字节数/字符数达到阈值为止
- 双模式：**字节计数** / **字符计数**（1–1000 可调）
- **编码自动检测** — BOM 判断 → UTF-8 验证 → GBK 回退
- **拖放支持** — 拖入 `.txt` 文件即可
- **安全输出** — 保存为 `*_Processed.*`，绝不覆盖源文件

**后处理选项（阈值合并后在内存中依次处理）：**

| 选项 | 说明 |
|------|------|
| **修复中文截断断段** | 行末为汉字时拼接下行（如 `你好\n吗？` → `你好吗？`） |
| **修复标点截断断段** | 行末为指定标点时拼接下行（如 `回家吧，\n孩子` → `回家吧，孩子`）。支持自定义标点集。 |
| **应用标点替换规则** | 对每行执行 Tab 3 中配置的替换规则 |
| **行尾部标点不合并** | 行末为指定字符时**阻止合并**——优先级高于 CJK 和标点截断规则。适合让章节标题（`章`、`节`）、句末标点（`.!?。！？`）独立成段。 |

**处理流水线**（一次内存传递，最后一次性写入硬盘）：

```text
读取文件 → 阈值合并 → 中文截断修复 → 标点截断修复 → 标点替换 → 写出文件
```

#### Tab 2 · 文件拼接

- 将目录中所有匹配 Glob 模式的文件合并为一个
- 按文件名排序后合并
- 每个文件按自身编码独立读取
- 统一输出为 **UTF-8 with BOM**
- 安全输出：`*_Processed.*`

#### Tab 3 · 标点替换

- **自由配置** 查找/替换规则（如 `。。` → `。`）
- **可排序** — 通过上移/下移按钮调整规则顺序
- 左侧规则列表实时预览
- 支持添加、更新、删除规则
- **JSON 持久化** — 自动保存到 `replace_rules.json`，重启不丢失
- 支持拖放 `.txt` 文件（单文件或批量）
- **批量处理** — 选择多个文件，一键处理所有
- 安全输出：`*_Processed.*`

#### Tab 4 · 视觉小说

- **排版** — 将 VN 脚本从固定宽度硬换行重建为自然段落
- 自动识别 **对话 / 叙事 / 场景标记 / 路线标题** 四类行
- 识别 `「角色」「台词」` 与 `角色名「台词」` / `角色名，「台词」` 对话模式
- **对话补标点** — 为缺少句末标点的对话行补全 `。`
- **角色/路线预设方案** — 内置命运石之门 / 进击的巨人 / 空之境界 / 樱之诗方案，支持对话框编辑
- 最大段落字数可调（100–2000）
- 超长叙事段落在句界自动拆分
- 三种模式：**一条龙**（排版+补标点）、**仅排版**、**仅补标点**
- 安全输出：`*_Processed.*`

#### Tab 5 · 关于

- 程序图标 (64×64) + **TwilightRain 头像** (64×64) 并排显示
- 程序名称 + 版本号并排
- 作者 + GitHub 链接并排
- 功能介绍
- **语言选择器** — 在 简体中文 / 繁體中文 / English 三者间即时切换
- 语言偏好保存到 `app_config.json`，下次启动自动恢复

### 国际化 / i18n

内置三种语言，自动检测系统 UI 语言：

| 语言 | 代码 | 文件 |
|------|------|------|
| 简体中文 | `zh_CN` | `Localization/zh_CN.json` |
| 繁體中文 | `zh_TW` | `Localization/zh_TW.json` |
| English | `en_US` | `Localization/en_US.json` |

- 系统语言自动适配 `zh-CN`、`zh-TW`、`zh-HK`、`zh-MO`、`en-*`
- 可在关于页手动切换，偏好自动持久化

### 系统要求

- **.NET 8 Desktop Runtime**（Windows WinForms）
- 从 [dotnet.microsoft.com/download/dotnet/8.0](https://dotnet.microsoft.com/download/dotnet/8.0) 下载安装 **.NET Desktop Runtime 8.0（x64）**
- 缺少运行时会弹出引导对话框

### 安装（最终用户）

从 [GitHub Releases](https://github.com/TwilightRainDev/TwilightRainTextTool/releases)
下载最新的 `TextTool-GUI-*-win-x64.zip`，解压后双击 `TextTool.exe` 即可运行。
需先安装 .NET 8 Desktop Runtime（见上方系统要求）；缺少运行时会弹出引导对话框。

若你手上是 `v2.6.1` 及更早的版本，程序内自动更新到不了这里——请手动下载 zip 解压覆盖原目录
（见 [`doc/UpgradeNotes.md`](doc/UpgradeNotes.md)）。

### 运行方式

#### 方式一 · 构建后运行

```bash
dotnet build TextTool.sln -c Release
# 输出 → bin/Release/net8.0-windows/TextTool.exe
double-click bin/Release/net8.0-windows/TextTool.exe
```

#### 方式二 · 从源码运行

```bash
dotnet run --project TextTool.csproj
```

#### 发布命令（与 CI 一致）

```bash
dotnet publish TextTool.csproj -c Release -r win-x64 -o publish/TextTool
# 输出 → publish/TextTool/   （正式发布物为 CI 生成的 zip）
```

> 注意：不要将 `-o` 指向 `bin/Release` 下 —— SDK 从不清理旧文件，publish
> 输出会与构建产物混居，残留旧版本文件（.NET host 可能因此加载到错误版本的程序集）。
> 清理本地构建产物：删除 `bin/`、`obj/` 目录后重新构建即可。

> 注意：带 `-r win-x64` 的构建会把中间产物写入 `bin/Release/net8.0-windows/win-x64/` ——
> 这是 SDK 对 RID 构建的标准输出目录（publish 从这里复制到 `-o`，并非备份副本），
> 已被 git 忽略，无需手动删除，下次 publish 会增量复用。

#### 命令行（CLI）

`texttool` — 同一引擎，可脚本化：

```bash
texttool merge <文件...> [选项]      行合并 + 后处理
texttool replace <文件...> [选项]    标点替换
texttool vn <文件...> [选项]        视觉小说排版
texttool join <目录> [选项]         文件拼接
texttool lint <文件...> [选项]      AI 味检查（只报不改）
texttool update [--check]           自更新（--check 仅检查）
```

运行 `texttool <命令> --help` 查看各命令选项。`update` 从 GitHub Release 下载新 CLI zip，校验 SHA-256 后自动替换自身（`--check` 可只检查不更新）。`v2.6.1` 及更早的客户端用不了 `update`，需手动下载替换（见 [`doc/UpgradeNotes.md`](doc/UpgradeNotes.md)）。

`lint` 接受一个或多个文件（`-` 表示从 stdin 读），不改动任何内容：只报告中文 AI 味出现的位置，
给出规则 Id、行号与列号。`--json` 输出机器可读契约，`--only S1,L1` 只跑指定规则 Id，
`--min-severity warn` 只保留 `warn` 及以上命中。`P4`/`P5` 与 `C1`-`C6` 这批规则 Id 由程序内
探测器产出，外部 `lint_rules.json` 若定义了同 Id，会在加载时报错、命令退 `2`——见
[`doc/UpgradeNotes.md`](doc/UpgradeNotes.md)。

**退出码与其它子命令不同。** `merge` / `replace` / `vn` / `join` / `update` 是
`0` 成功 / `1` 失败；`lint` 是 `0` 无命中 / `1` 有命中 / `2` 用法或读取错误。
命中判定在过滤之后进行，因此 `--min-severity warn` 的语义是「只在有 `warn` 命中时退 1」。

**`--json` 契约。** 无论传几个文件都是同一个包装，消费者不必按文件数分支：

```json
{
  "Version": 2,
  "Reports": [
    {
      "File": "chapter1.md",
      "Chars": 12345,
      "ParagraphMode": "markdown",
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

字段为 PascalCase。包装的 `Version` 为 `2`，是本契约的版本闸门——v1 不承诺行尾、转义面、
命中有序性与 `ParagraphMode`。`Match` 是被命中的原文——词表类规则靠它告诉消费者命中了哪个词；
`Snippet` 是带上下文的展示片段；`Hint` 是下标对齐出来的建议改法，无则 `null`。
`ParagraphMode` 是本次扫描采用的段落口径：`line` 或 `markdown`（见下方启发式规则的边界）。
`Notes` 是统计观察，不计入退出码。隐形字符在人读与 JSON 两种输出里都转义为 `\u200b`。

契约保证：

- **行尾一律 LF**，人读报告与 `--json` 同此。
- **转义面。** `"`、`&`、`'`、`+`、`<`、`>` 与反引号转义为 `\uXXXX`
  （`\u0022` `\u0026` `\u0027` `\u002B` `\u003C` `\u003E` `\u0060`），反斜杠为 `\\`；
  中文按原样输出，`/ ~ % @ = | !` 同样不转义。隐形字符也转义成 `\uXXXX`，但走另一条代码
  路径：它是**小写**十六进制（`\u200b`），JSON 编码器则是大写（`\u002B`）——两者解析无差别。
- **`Hits` 按 `(Line, Col)` 升序**，同一位置的命中保持引擎顺序（规则表顺序，再按 Pattern 顺序）。
- **`Match` 与 `Snippet` 都是原文的连续切片。** 跨行命中的换行会保留：人读报告渲染为 `\u000a`，
  JSON 文本里是 `\n`。读盘不做行尾归一化，因此 CRLF 输入下这两个字段还可能含 CR
  （人读为 `\u000d`，JSON 文本为 `\r`）。

人读报告按规则分组，各组的出现顺序是各组首命中的位置顺序，不再等于规则表顺序。

**启发式规则的边界。** `P4`（引号风格）、`P5`（括号全半角）只看字符不看上下文：
英文词里的 `'`（如 `don't`）会被当作一种引号风格，与文中别处的中文引号并列为「混用」；
代码片段里写的 ASCII 括号同样计入括号混用。`C` 组（`C1`–`C6`）是统计观察，不进退出码。
`P4`/`P5` 一律 `info` 级：默认只报，用 `--min-severity warn` 即可把它们排除在关卡之外。

**段落口径。** 口径按文件探测，只取首 4096 字符：其中空行占行数比例达到 10%（末尾单个换行不算
一行）即判为 Markdown——按空行分段，软换行并入同一段，列表项、引用行与编号行各自成段；
否则每个非空行各成一段。实测样本：真实 Markdown 文件空行占比 7.7%–53%，行式文本为 0%；
阈值取保守方向，漏判只退化成行式口径，不会误判行式文本。当次采用的口径随报告输出
（`ParagraphMode` 字段与人读报告的 `[NOTE]` 行）。行式口径下软换行的段落被拆成多行，
段落作用域的 `S2`（段内序数词）与 `S3`（结尾升华）因此可能少报——属已知边界。

**两条规则可能报同一跨度。** `。。。。` 会同时命中 `P1` 与 `P2`，其它重叠规则同理。
这是**有意行为**：两条规则给的建议不同，报告层去重会丢掉建议，也会破坏 `--only` 契约
（去重后留下的 Id 决定该命中是否输出）。需要「一个跨度只留一条」的消费者请自行去重。

**建议方案的一处副作用。** `引号括号统一` 会把全角括号转成半角，而半角括号紧邻中文正是 `P3`
要报的（实测 `（中文）` 0 处命中、`(中文)` 2 处）。用完请重跑检查，它不是 `P3` 的解法。

### 项目结构

```text
TextTool/
├── TextTool.sln                  # 解决方案文件
├── TextTool.csproj               # .NET 8 WinForms, v3.0.0
├── Directory.Build.props         # 统一版本号 (3.0.0)
├── Program.cs                    # 入口，注册 GBK 编码支持
├── MainForm.cs                   # 主窗口 (214 行，承载 6 个页签)
│
├── Controls/                     # 页签控件（从 MainForm 拆分）
│   ├── MergeTabControl.cs        # Tab 1: 行合并（拖放、批量、预览）
│   ├── JoinTabControl.cs         # Tab 2: 文件拼接（目录 + 匹配模式）
│   ├── ReplaceTabControl.cs      # Tab 3: 标点替换（CRUD、批量处理）
│   ├── VNTabControl.cs           # Tab 4: 视觉小说（排版、补标点、预设方案）
│   ├── LintTabControl.cs         # Tab 5: AI 味检查（只报不改，与 CLI 共用引擎）
│   ├── VNCharacterSchemeForm.cs  # 角色/路线预设方案勾选与编辑
│   ├── SchemeSelectionForm.cs    # 标点替换方案勾选对话框
│   ├── SchemeSelectionFormBase.cs# 两个方案对话框的共享基类
│   ├── IThemedTab.cs             # 主题遍历所需的页签接口
│   ├── AboutTabControl.cs        # Tab 6: 关于、语言切换、深色模式、配置导入导出
│   └── PreviewForm.cs            # 预览对话框（合并结果保存前查看）
│
├── TextTool.Core/                 # 纯逻辑类库（无 UI 依赖）
│   ├── EncodingDetector.cs        # BOM → UTF-8 → GBK 自动检测（含严格模式）
│   ├── LineMerger.cs              # 阈值行合并核心算法
│   ├── FileJoiner.cs              # 多文件拼接
│   ├── CjkParagraphMerger.cs      # 中文截断修复 + 不合并规则
│   ├── PunctTruncationMerger.cs   # 标点截断修复 + 不合并规则
│   ├── PunctuationReplacer.cs     # 查找替换引擎 + ReplaceRuleStore
│   ├── ProcessingPipeline.cs      # 流水线编排（单次遍历，一次写出）
│   ├── VNReformatterService.cs    # VN 脚本排版引擎（行→段落）
│   ├── PunctFixerService.cs       # 对话标点补齐
│   ├── VNCharacterScheme.cs       # 角色/路线预设方案模型与存储
│   ├── ReplaceScheme.cs           # 替换规则方案模型与存储
│   ├── BackupHelper.cs            # 覆盖写前自动备份（含轮换）
│   ├── AtomicFile.cs              # 输出原子写入（临时文件 + 整体替换）
│   ├── DialogueLine.cs            # 共享对话行正则检测
│   ├── RegexGuard.cs              # 带 ReDoS 超时的正则构造入口
│   ├── JsonFileStore.cs           # 泛型 JSON 文件持久化
│   ├── PathHelper.cs              # 共享文件路径工具
│   ├── TextUtils.cs               # 扩展方法（EndsWithAny 等）
│   ├── LintRule.cs                # AI 味规则模型 + 存储（外部文件按 Id 合并覆盖）
│   ├── LintReport.cs              # 检查报告模型与 JSON 契约
│   ├── AiToneLintService.cs       # AI 味检查引擎（数据规则 + 算法检出器）
│   ├── LintTextFormatter.cs       # 人读报告渲染（不可见字符转义）
│   ├── LintRunner.cs              # CLI/GUI 共用检查编排（校验 + 扫描 + 过滤）
│   ├── UpdateChecker.cs           # GitHub 最新 release 版本检查
│   ├── UpdateClient.cs            # 自更新专用 HttpClient（固定根、禁重定向）
│   ├── ReleaseVerifier.cs         # 发布包签名验签（ECDsa P-256）
│   ├── ReleaseSigningPublicKey.cs # 发布者公钥常量（SPKI base64）
│   ├── PinnedRoots.cs             # 公共根 CA 白名单（SPKI）
│   ├── LastKnownVersion.cs        # 最近安装版本（降级重放检测）
│   ├── SelfUpdater.cs             # CLI 自更新：下载、校验、延迟替换
│   ├── default_schemes.json       # 内置默认替换方案
│   ├── default_vn_schemes.json    # 内置默认 VN 方案
│   ├── default_lint_rules.json    # 内置 AI 味规则
│   └── pinned_roots.txt           # 内置根 CA 清单（certifi）
│
├── TextTool.Cli/                  # 命令行入口（texttool.exe）
│   └── Program.cs                 # merge / replace / vn / join / lint / update 子命令
│
├── Services/                      # UI 相关服务
│   ├── ThemeManager.cs            # 语义化色板（深色/浅色模式）
│   ├── ThemedFlatButton.cs        # 扁平按钮 + 禁用状态 ForeColor 修复
│   ├── ControlsHelper.cs          # 共享 UI 工厂（按钮、标签、对话框）
│   └── IStatusSource.cs           # 页签状态事件接口
│
├── Localization/                 # 国际化
│   ├── Strings.cs                # Loc 单例（自动检测、切换、持久化）
│   ├── zh_CN.json                # 简体中文语言包
│   ├── zh_TW.json                # 繁体中文语言包
│   └── en_US.json                # 英文语言包
│
├── Resources/
│   ├── icon.ico                  # 程序图标
│   └── TwilightRain.jpg          # 关于页头像
│
├── TextTool.Tests/               # 单元测试（xUnit，245 个 [Fact]/[Theory]）
│   ├── TextTool.Tests.csproj
│   ├── TestHelpers.cs
│   └── Services/                 # 每个服务对应一个测试文件
│
├── doc/                          # 文档
│   ├── ArchitectureGuide.md      # 开发者架构指南
│   ├── Publish.md                # 发布清单
│   ├── UpdateSecurity.md         # 更新信任模型（TLS 固定、签名验签）
│   ├── ReplaceSchemesDesign.md   # 替换方案设计说明
│   ├── TECH-DEBT.md              # 未落地优化项与技术债
│   ├── adr/                      # 架构决策记录（9 份 ADR）
│   ├── specs/                    # 现行/在途规格
│   └── archive/                  # 已收口规格、计划、已落地债条
│
├── scripts/
│   ├── github-release.ps1        # GitHub REST 辅助（curl，不用 gh）
│   ├── publish.ps1               # 给已有 Release 签名并上传 .sig
│   └── release.ps1               # 打 tag、等 CI、再调 publish.ps1
│
├── .github/workflows/
│   └── build-test.yml            # CI：构建 + 测试 + 格式 + 发布
│
├── replace_rules.json            # 运行时生成的替换规则文件（自动）
├── app_config.json               # 语言与主题偏好（自动）
├── LICENSE                       # MIT 许可证
└── README.md                     # 本文件
```

### 技术栈

| 组件 | 技术 |
|------|------|
| 框架 | .NET 8 WinForms |
| UI 构建 | 纯 C# 代码（无 Designer 文件） |
| 编码支持 | `System.Text.Encoding.CodePages`（.NET 8 共享框架内置，无需 NuGet 包） |
| 输出编码 | UTF-8 with BOM |
| 持久化 | JSON（`System.Text.Json`） |
| 国际化 | 自定义 `Loc` 单例 + JSON 语言包 |

### 版本历史

逐版本变更由 git tag 与 GitHub Releases 承载，见
[Tags](https://github.com/TwilightRainDev/TwilightRainTextTool/tags) 与
[Releases](https://github.com/TwilightRainDev/TwilightRainTextTool/releases)
（共 14 个 tag，最新 `v3.0.0`）。本文件不再重复维护。

### 风格约定

本项目**不使用 emoji** 作为代码、文档、提交信息的装饰。功能性字符除外：

- VN 排版字符集（`★☆♪♭♯` 等）是业务数据，不属于装饰
- 终端状态标记用纯文本 `[成功]` / `[失败]`，不使用 `✓` / `✗`
- 文档中的分级/状态用纯文本表达（如 `4/5`、`完整`、`待改进`）

---

© 2026 **TwilightRain**
