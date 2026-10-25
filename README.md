# TwilightRain Text Tool

A text tool for Chinese manuscripts: line merge, file join, punctuation replacement,
visual novel reformatting and an AI-tone lint pass. Desktop app plus a command line version
sharing the same engine.

一个面向中文稿件的文本工具：行合并、文件拼接、标点替换、视觉小说排版与 AI 味检查。
图形界面与命令行共用同一套引擎。

[English](#english) \| [中文](#中文)

## English

### What it does

| Tab | What it does |
| --- | --- |
| Line merge | Appends short lines until they reach a length threshold, then optionally fixes truncated paragraphs and applies replacement rules |
| File join | Concatenates every file matching a pattern in one folder into one output file |
| Punctuation replace | Keeps a list of find and replace rules, saves them to disk, and applies them to one or many files |
| Visual novel | Rebuilds hard-wrapped scripts into natural paragraphs and completes dialogue punctuation |
| AI-tone lint | Reports wording that reads like generated Chinese; never rewrites the file |
| About | Version, author, repository link, language and theme switches, config import and export, update check |

### Tabs

#### Tab 1: Line merge

- Pick files or drag them onto the window; several files are processed as one batch.
- Threshold from 1 to 1000, counted in characters (default 20) or in bytes.
- The detected encoding is shown under the source row.
- Post-processing after the threshold merge, in this order:
  - Fix CJK truncation: if a line ends with a CJK character, join the next line (on by default).
  - Fix punctuation truncation: join the next line when the current one ends with any character of a custom set (default `，`).
  - Trim leading comma (on by default).
  - Apply the replacement rules from Tab 3.
- Line-end no-merge is part of the merge itself: with a custom set (default `。！？`), a line ending with one of those characters is never joined. It overrides both truncation fixes.
- Output goes to `*_Processed.*` next to the source file; the source is not touched.
- Overwriting the source file is a separate opt-in. With the backup box checked, the previous content is kept as `{name}.bak` and older copies rotate to `.bak.1` through `.bak.3`.
- Preview runs the current options and shows the result in a separate window; single file only.

#### Tab 2: File join

- Folder plus a glob pattern (default `*.txt`) plus an output name (default `combined.txt`).
- Files are sorted by name before joining.
- Each file is read with its own detected encoding.
- Output is always UTF-8 with BOM.

#### Tab 3: Punctuation replace

- Rule list with add, update, delete and move up or down.
- Rules are saved to `replace_rules.json` right after each change.
- The rule list is the same list the line merge tab uses, so a change is visible there immediately.
- One or many files per run; each file is read with its own detected encoding.
- Output goes to `*_Processed.*`.
- Preset schemes: pick built-in or custom schemes, tick single rules, add, rename or delete schemes, import and export scheme files as JSON.

#### Tab 4: Visual novel

- Rebuilds fixed-width hard-wrapped lines into paragraphs; classifies dialogue, narration, scene markers and route headings.
- Three modes: reformat plus punctuation, reformat only, punctuation only.
- Character and route schemes, with an optional scene marker regex per scheme.
- Maximum paragraph length from 100 to 2000 (default 450).
- A single file opens the preview window, and nothing is written until you save there. A batch writes directly and reports progress on the status bar.

#### Tab 5: AI-tone lint

- Input is one or more files, pasted text, or both.
- Rule checklist: leave everything unchecked to run all rules.
- Severity filter for `warn` and above.
- Report only. Nothing is written, and the report follows the interface language.
- The CLI subcommand `lint` runs the same engine.

#### Tab 6: About

- Program version, author, repository link.
- Language switch: Simplified Chinese, Traditional Chinese, English. The choice is saved to `app_config.json` and applies without restarting.
- Light and dark theme toggle.
- Export and import of `app_config.json` and `replace_rules.json`.
- Check for updates: reports whether a newer release exists, nothing else.

### Supported platforms

| Platform | Desktop app | Command line |
| --- | --- | --- |
| Windows x64 | `TextTool-GUI-<version>-win-x64.zip` | `TextTool-CLI-<version>-win-x64.zip` |
| Linux x64 | `TextTool-GUI-<version>-linux-x64.zip` | `TextTool-CLI-<version>-linux-x64.zip` |

All four packages are framework-dependent builds: they do not carry a .NET runtime.

Requirements:

- Windows: .NET 8 runtime (x64), from
  [dotnet.microsoft.com/download/dotnet/8.0](https://dotnet.microsoft.com/download/dotnet/8.0).
- Linux: .NET 8 runtime, not the SDK. On Debian and Ubuntu the package is
  `dotnet-runtime-8.0`; the official steps are in the
  [Linux install docs](https://learn.microsoft.com/dotnet/core/install/linux).
- Linux: a CJK font, for example `fonts-noto-cjk`. Without one the desktop app and the terminal
  show Chinese as boxes. The packages do not bundle any font.

### Download and install

Desktop app:

1. Download `TextTool-GUI-<version>-win-x64.zip` or `TextTool-GUI-<version>-linux-x64.zip` from
   [GitHub Releases](https://github.com/TwilightRainDev/TwilightRainTextTool/releases).
2. Unzip it anywhere. The folder holds everything except the .NET runtime; configuration and
   output files are written next to the program.
3. Run the program in that folder. The app does not start without the .NET 8 runtime,
   so install it first if needed. On Linux the zip keeps no executable bit, so run
   `chmod +x TextTool.Gui` before starting it.

Command line:

1. Download `TextTool-CLI-<version>-win-x64.zip` or `TextTool-CLI-<version>-linux-x64.zip`.
2. Unzip it anywhere.
3. On Linux the zip does not keep the executable bit, so run `chmod +x texttool` first.

```bash
unzip TextTool-CLI-<version>-linux-x64.zip -d TextTool-CLI
chmod +x TextTool-CLI/texttool
./TextTool-CLI/texttool --help
```

Each zip has a `.sha256` file with the same name. Compare it if you want to rule out a broken
download:

```bash
sha256sum TextTool-CLI-<version>-linux-x64.zip
```

### Build from source

Requires the .NET 8 SDK (8.0.425 was used for the current builds).

```bash
dotnet build TextTool.sln -c Release
dotnet run --project TextTool.Gui
dotnet run --project TextTool.Cli -- lint --help
dotnet test TextTool.Tests/TextTool.Tests.csproj
```

Build output of the desktop app is `TextTool.Gui/bin/Release/net8.0/TextTool.Gui.exe` on Windows.

Publish a framework-dependent build:

```bash
dotnet publish TextTool.Gui/TextTool.Gui.csproj -c Release -r win-x64 --self-contained false -o publish/TextTool.Gui/win-x64
dotnet publish TextTool.Cli/TextTool.Cli.csproj -c Release -r win-x64 -o publish/TextTool.Cli/win-x64
dotnet publish TextTool.Gui/TextTool.Gui.csproj -c Release -r linux-x64 --self-contained false -o publish/TextTool.Gui/linux-x64
dotnet publish TextTool.Cli/TextTool.Cli.csproj -c Release -r linux-x64 -o publish/TextTool.Cli/linux-x64
```

Do not point `-o` at `bin/Release`: the SDK does not clean the target folder, so stale files
from older builds stay there and the host may load the wrong assembly.

There is also an offline UI check tool, not part of the solution:

```bash
dotnet run --project tools/GuiSnapshot -c Release -- snapshots
```

### Command line

```text
texttool merge <file...>     Line merge plus post-processing
texttool replace <file...>   Punctuation replacement
texttool vn <file...>        Visual novel reformatting
texttool join <directory>    File joining
texttool lint <file...>      AI-tone check, report only
texttool update [--check]    Self update; --check only queries
```

Options per subcommand:

| Subcommand | Options |
| --- | --- |
| `merge` | `--threshold <N>` (default 20), `--chars` or `--bytes`, `--overwrite`, `--backup`, `--no-cjk`, `--fix-punct`, `--punct-chars <chars>`, `--no-trim-comma` |
| `replace` | `--scheme <name>`; without it the rules in `replace_rules.json` are used |
| `vn` | `--scheme <name>` (repeatable), `--max-para <N>` (default 450), `--reformat-only`, `--punct-only` |
| `join` | `--pattern <glob>` (default `*.txt`), `--output <name>` (default `joined.txt`) |
| `lint` | `--only <id,...>`, `--min-severity <info\|warn>`, `--json`; `-` reads stdin |
| `update` | `--check` |

Every subcommand prints its own help with `--help`.

Exit codes: `merge`, `replace`, `vn`, `join` and `update` return 0 on success and 1 on failure.
`lint` is different: 0 means no hits, 1 means hits, 2 means a usage or read error. The hit test
runs after filtering, so `--min-severity warn` exits 1 only when a `warn` hit exists.

`lint --json` writes one wrapper for any number of inputs, so consumers never branch on file
count:

```json
{
  "Version": 3,
  "Reports": [
    {
      "File": "chapter1.md",
      "Chars": 12345,
      "ParagraphMode": "markdown",
      "Hits": [
        {
          "Id": "parallel-contrast", "Group": "syntax", "Title": "对举句式", "Severity": "warn",
          "Line": 12, "Col": 5, "Length": 7,
          "Match": "不是数据不够，而是口径不同",
          "Snippet": "…不是数据不够，而是口径不同…",
          "Detail": "不是A而是B", "Hint": null, "SuggestScheme": null
        }
      ],
      "Notes": [{ "Id": "paragraph-length-uniform", "Text": "段落长度过于均一（变异系数 0.18，12 段）" }]
    }
  ]
}
```

The contract:

- `Version` is 3. A consumer that cannot handle the field set should refuse the payload.
- Fields are PascalCase.
- Line endings are LF in both the human-readable report and the JSON output.
- `Hits` is sorted by `Line` then `Col`. Hits sharing a position keep engine order: rule table
  order, then pattern order.
- `Match` is the matched source text, `Snippet` the surrounding context, `Hint` the suggested
  rewrite (`null` when there is none). Both `Match` and `Snippet` are contiguous slices of the
  source, so a hit spanning a line break keeps the newline.
- `ParagraphMode` is `line` or `markdown`, decided per file from its first 4096 characters.
- `Notes` carries statistical observations and never affects the exit code.
- `"`, `&`, `'`, `+`, `<`, `>` and the backtick are escaped as `\uXXXX`; a backslash becomes
  `\\`; Chinese is written as is. Invisible characters are escaped the same way in lowercase
  hex, for example `\u200b`.

Two rules may report the same span on purpose: `repeated-punctuation` and `ellipsis-style` both
fire on `。。。。` and carry different suggestions. Consumers that want one hit per span have to
de-duplicate on their side.

`update` on Linux only queries. Run `texttool update --check` to see whether a newer release
exists, then download the zip and overwrite the folder by hand.

### Where files are written

Configuration, rules and schemes live in the program folder:

| File | Content |
| --- | --- |
| `app_config.json` | Interface language and theme |
| `replace_rules.json` | Punctuation replacement rules |
| `replace_schemes.json` | Named replacement schemes |
| `vn_schemes.json` | Character and route schemes |
| `lint_rules.json` | Extra lint rules, keyed by rule id |
| `last_known_version.txt` | Last installed version, used to refuse replayed older packages |

Processed files are written next to their source as `{name}_Processed{ext}`. Backups are
`{name}.bak`, with older copies rotated to `.bak.1` through `.bak.3`.

### FAQ

**Which encoding is used?** Input encoding is detected in this order: BOM, valid UTF-8, GBK
fallback. When the header looks like UTF-8, the whole file is checked again before the decision
is kept. Output is always written as UTF-8 with BOM, whatever the input was.

**Where did my original file go?** It stays where it was. Results go to a new `*_Processed.*`
file unless you tick the overwrite box. With overwrite ticked and the backup box also ticked,
the previous content is kept as `{name}.bak`.

**How do updates work?** The desktop app only reports whether a newer release exists, on both
platforms. The command line can replace itself on Windows: it downloads the new zip, checks the
SHA256 and the publisher signature, then swaps files after the process exits. A missing or
invalid signature stops the update. On Linux only `texttool update --check` is available.

**Why does the update say the version went backwards?** The updater records the last installed
version in `last_known_version.txt` and refuses a release whose version is lower. Delete that
file if the release was rolled back on purpose.

**A config file got corrupted.** The program reports the file name instead of silently running
with empty rules. Restore it from an export made on the About tab, or delete the file to fall
back to defaults.

### Project structure

```text
TextTool/
├── TextTool.sln
├── Directory.Build.props         Single source of the version number
├── TextTool.Core/                Engine, config stores, update and signature checks
│   ├── default_schemes.json      Embedded default replacement schemes
│   ├── default_vn_schemes.json   Embedded default character schemes
│   ├── default_lint_rules.json   Embedded default lint rules
│   └── pinned_roots.txt          Embedded public root CA list for updates
├── TextTool.Gui/                 Desktop app (Avalonia)
│   ├── App.axaml                 Font size, font family, theme include
│   ├── Views/                    One .axaml plus one .axaml.cs per tab and dialog
│   ├── Styles/                   Theme.axaml, Theme.Light.axaml, Theme.Dark.axaml, AppStyles.axaml
│   ├── Localization/             Loc.cs, LintTextSource.cs
│   └── Services/                 AppSettings.cs
├── TextTool.Cli/                 Command line entry, assembly name texttool
├── TextTool.Tests/               xUnit tests, Core plus release scripts
├── Localization/                 zh_CN.json, zh_TW.json, en_US.json
├── Resources/                    icon.ico and the avatar used on the About tab
├── tools/
│   ├── GuiSnapshot/              Offscreen UI check tool
│   └── ReleaseSigner/            Release package signing tool
├── scripts/
│   ├── release.ps1               Tag, wait for CI, then call publish.ps1
│   ├── publish.ps1               Sign the release assets and upload the signatures
│   └── github-release.ps1        Shared GitHub REST helpers
├── doc/ReleaseNotes.md           Upgrade notice merged into each release body
└── .github/workflows/build-test.yml
```

### Tech stack

| Part | Technology |
| --- | --- |
| Runtime | .NET 8 |
| Desktop UI | Avalonia 11.2.3, hand-written axaml, no MVVM |
| Command line | Plain console entry, no argument parser package |
| Persistence | JSON through `System.Text.Json` |
| Encoding support | `System.Text.Encoding.CodePages` for GBK, built into the shared framework |
| Tests | xUnit 2.4.2 |
| Version | Single `<Version>` in `Directory.Build.props` |

### Conventions

No emoji in code, docs or commit messages. Status markers in terminal output are plain bracket
text such as `[成功]`, never symbols. Functional characters used by the visual novel formatter
are content, not decoration.

---

## 中文

### 功能概览

| 页签 | 说明 |
| --- | --- |
| 行合并 | 把短行拼到指定长度为止，可选修复截断段落、套用替换规则 |
| 文件拼接 | 把一个文件夹里所有匹配的文件拼成一个 |
| 标点替换 | 维护查找替换规则并存盘，按规则批量改写文件 |
| 视觉小说 | 把硬换行的脚本排成自然段，顺带补全对话标点 |
| AI 味检查 | 报告读起来像生成文本的措辞，只报不改 |
| 关于 | 版本、作者、仓库链接、语言与主题切换、配置导入导出、检查更新 |

### 各页签

#### 页签 1：行合并

- 选择文件或把文件拖到窗口上；选多个文件即按批处理。
- 阈值 1 到 1000，可按字符数（默认 20）或按字节数计。
- 来源行下面显示检测到的编码。
- 阈值合并之后的后处理，按这个顺序执行：
  - 修复中文截断：行末是汉字时拼接下一行（默认勾选）。
  - 修复标点截断：行末是自定义字符集里的字符时拼接下一行（默认字符集 `，`）。
  - 去掉行首逗号（默认勾选）。
  - 应用页签 3 的标点替换规则。
- 行末标点不合并属于合并这一步：给了自定义字符集（默认 `。！？`），行末是其中任一字符就不拼接。它的优先级高于上面两条截断修复。
- 结果写在源文件旁边的 `*_Processed.*`，源文件不动。
- 覆盖原文件需要另行勾选。同时勾选备份时，原内容留作 `{原名}.bak`，更早的副本轮换为 `.bak.1` 到 `.bak.3`。
- 单文件可以点预览，按当前选项跑一遍，在单独的窗口里看结果。

#### 页签 2：文件拼接

- 文件夹、匹配模式（默认 `*.txt`）、输出文件名（默认 `combined.txt`）。
- 拼接前按文件名排序。
- 每个文件按自身检测到的编码读取。
- 输出统一为 UTF-8 with BOM。

#### 页签 3：标点替换

- 规则列表支持添加、更新、删除、上移、下移。
- 每次改动后立即写入 `replace_rules.json`。
- 这份规则列表与行合并页共用，改动在那里立刻生效。
- 一次可以处理一个或多个文件，每个文件按自身检测到的编码读取。
- 结果写在 `*_Processed.*`。
- 预设方案：勾选内置或自定义方案，按规则粒度勾选，支持新建、改名、删除方案，以及导入导出方案文件（JSON）。

#### 页签 4：视觉小说

- 把固定宽度硬换行的文本排成自然段，区分为对话、叙述、场景标记、路线标题。
- 三种模式：排版加补标点、仅排版、仅补标点。
- 角色与路线方案，每个方案可以带一条场景标记正则。
- 段落最大字数 100 到 2000（默认 450）。
- 单文件先开预览窗，在那里点保存才写盘；批量直接写盘，进度报到状态栏。

#### 页签 5：AI 味检查

- 输入可以是一个或多个文件，也可以直接粘贴文本，两者可以同时给。
- 规则清单可以逐条勾选；全部不勾等于跑全部规则。
- 严重度过滤可选只看 `warn` 及以上。
- 只出报告，不写任何文件；报告文字跟随界面语言。
- 命令行子命令 `lint` 用的是同一套引擎。

#### 页签 6：关于

- 程序版本、作者、仓库链接。
- 语言切换：简体中文、繁體中文、English。选择存到 `app_config.json`，切换不需要重启。
- 深浅主题切换。
- 导出、导入 `app_config.json` 与 `replace_rules.json`。
- 检查更新：只查有没有新版本，不下载也不替换。

### 支持的平台

| 平台 | 图形界面 | 命令行 |
| --- | --- | --- |
| Windows x64 | `TextTool-GUI-<版本>-win-x64.zip` | `TextTool-CLI-<版本>-win-x64.zip` |
| Linux x64 | `TextTool-GUI-<版本>-linux-x64.zip` | `TextTool-CLI-<版本>-linux-x64.zip` |

四份包都是框架依赖发布，包内不含 .NET 运行时。

前置依赖：

- Windows：.NET 8 运行时（x64），下载地址
  [dotnet.microsoft.com/download/dotnet/8.0](https://dotnet.microsoft.com/download/dotnet/8.0)。
- Linux：.NET 8 运行时（不是 SDK）。Debian 与 Ubuntu 上是 `dotnet-runtime-8.0` 包，
  具体步骤见[官方安装文档](https://learn.microsoft.com/dotnet/core/install/linux)。
- Linux：需要中文字体，例如 `fonts-noto-cjk`。没有的话图形界面与终端里的中文显示成方块，
  发布包不带字体。

### 下载与安装

图形界面：

1. 从 [GitHub Releases](https://github.com/TwilightRainDev/TwilightRainTextTool/releases)
   下载 `TextTool-GUI-<版本>-win-x64.zip` 或 `TextTool-GUI-<版本>-linux-x64.zip`。
2. 解压到任意目录。除 .NET 运行时之外的东西都在这个目录里，配置与输出文件也写在程序目录。
3. 运行解压目录里的程序。缺 .NET 8 运行时时程序起不来，先按上面的链接装好。
   Linux 上 zip 不保留可执行位，启动前先 `chmod +x TextTool.Gui`。

命令行版：

1. 下载 `TextTool-CLI-<版本>-win-x64.zip` 或 `TextTool-CLI-<版本>-linux-x64.zip`。
2. 解压到任意目录。
3. Linux 上 zip 不保留可执行位，先 `chmod +x texttool`。

```bash
unzip TextTool-CLI-<版本>-linux-x64.zip -d TextTool-CLI
chmod +x TextTool-CLI/texttool
./TextTool-CLI/texttool --help
```

每个 zip 旁边有一个同名 `.sha256` 文件，想排除下载损坏时对一下：

```bash
sha256sum TextTool-CLI-<版本>-linux-x64.zip
```

### 从源码构建

需要 .NET 8 SDK（当前构建用的是 8.0.425）。

```bash
dotnet build TextTool.sln -c Release
dotnet run --project TextTool.Gui
dotnet run --project TextTool.Cli -- lint --help
dotnet test TextTool.Tests/TextTool.Tests.csproj
```

Windows 上图形界面的构建产物在 `TextTool.Gui/bin/Release/net8.0/TextTool.Gui.exe`。

在本地发布框架依赖版本：

```bash
dotnet publish TextTool.Gui/TextTool.Gui.csproj -c Release -r win-x64 --self-contained false -o publish/TextTool.Gui/win-x64
dotnet publish TextTool.Cli/TextTool.Cli.csproj -c Release -r win-x64 -o publish/TextTool.Cli/win-x64
dotnet publish TextTool.Gui/TextTool.Gui.csproj -c Release -r linux-x64 --self-contained false -o publish/TextTool.Gui/linux-x64
dotnet publish TextTool.Cli/TextTool.Cli.csproj -c Release -r linux-x64 -o publish/TextTool.Cli/linux-x64
```

不要把 `-o` 指到 `bin/Release`：SDK 不会清理目标目录，旧版本的文件会留在里面，宿主可能加载到错的程序集。

另外有一个不进解决方案的离屏界面验收工具：

```bash
dotnet run --project tools/GuiSnapshot -c Release -- snapshots
```

### 命令行

```text
texttool merge <文件...>     行合并加后处理
texttool replace <文件...>   标点替换
texttool vn <文件...>        视觉小说排版
texttool join <目录>         文件拼接
texttool lint <文件...>      AI 味检查，只报不改
texttool update [--check]    自更新，--check 只查询
```

各子命令的选项：

| 子命令 | 选项 |
| --- | --- |
| `merge` | `--threshold <N>`（默认 20）、`--chars` 或 `--bytes`、`--overwrite`、`--backup`、`--no-cjk`、`--fix-punct`、`--punct-chars <字符>`、`--no-trim-comma` |
| `replace` | `--scheme <名称>`；不给就用 `replace_rules.json` 里的规则 |
| `vn` | `--scheme <名称>`（可多次）、`--max-para <N>`（默认 450）、`--reformat-only`、`--punct-only` |
| `join` | `--pattern <通配符>`（默认 `*.txt`）、`--output <文件名>`（默认 `joined.txt`） |
| `lint` | `--only <Id,...>`、`--min-severity <info\|warn>`、`--json`；传 `-` 从 stdin 读 |
| `update` | `--check` |

每个子命令都能用 `--help` 看自己的说明。

退出码：`merge`、`replace`、`vn`、`join`、`update` 是 0 表示成功，1 表示失败。
`lint` 不同：0 表示无命中，1 表示有命中，2 表示用法或读取错误。命中判定发生在过滤之后，
所以 `--min-severity warn` 只在有 `warn` 命中时返回 1。

`lint --json` 无论几个输入都用同一个外层包装，消费方不必按文件数分支：

```json
{
  "Version": 3,
  "Reports": [
    {
      "File": "chapter1.md",
      "Chars": 12345,
      "ParagraphMode": "markdown",
      "Hits": [
        {
          "Id": "parallel-contrast", "Group": "syntax", "Title": "对举句式", "Severity": "warn",
          "Line": 12, "Col": 5, "Length": 7,
          "Match": "不是数据不够，而是口径不同",
          "Snippet": "…不是数据不够，而是口径不同…",
          "Detail": "不是A而是B", "Hint": null, "SuggestScheme": null
        }
      ],
      "Notes": [{ "Id": "paragraph-length-uniform", "Text": "段落长度过于均一（变异系数 0.18，12 段）" }]
    }
  ]
}
```

契约：

- `Version` 是 3。处理不了这批字段的消费方应当拒收。
- 字段名是 PascalCase。
- 人读报告与 JSON 的行尾都是 LF。
- `Hits` 先按 `Line` 再按 `Col` 升序。同一位置的多条命中保持引擎顺序：规则表顺序，然后模式顺序。
- `Match` 是命中的原文，`Snippet` 是上下文的展示片段，`Hint` 是建议改法（没有时为 `null`）。
  `Match` 与 `Snippet` 都是源文本的连续切片，跨行的命中会保留换行。
- `ParagraphMode` 是 `line` 或 `markdown`，按每个文件前 4096 个字符判断。
- `Notes` 是统计观察，不影响退出码。
- `"`、`&`、`'`、`+`、`<`、`>` 和反引号转义成 `\uXXXX`；反斜杠写成 `\\`；中文照原样输出。
  隐形字符同样转义，但用小写十六进制，例如 `\u200b`。

有两条规则会故意报同一段文字：`repeated-punctuation` 与 `ellipsis-style` 都会在 `。。。。`
上命中，给出的建议不同。想要一段只留一条的，由消费方自己去重。

`update` 在 Linux 上只能查询：`texttool update --check` 看有没有新版，
下载与覆盖目录要手动做。

### 文件写在哪里

配置、规则与方案都在程序目录里：

| 文件 | 内容 |
| --- | --- |
| `app_config.json` | 界面语言与主题 |
| `replace_rules.json` | 标点替换规则 |
| `replace_schemes.json` | 命名的替换方案 |
| `vn_schemes.json` | 角色与路线方案 |
| `lint_rules.json` | 额外检查规则，按规则 Id 覆盖内置规则 |
| `last_known_version.txt` | 最近安装的版本，用于拒绝回放的旧包 |

处理结果写在源文件旁边，名字是 `{原名}_Processed{扩展名}`。备份是 `{原名}.bak`，
更早的副本轮换为 `.bak.1` 到 `.bak.3`。

### 常见问题

**用的是哪种编码？** 输入编码按这个顺序判断：BOM、合法 UTF-8、GBK 回退。头部像 UTF-8 时，
会再全文核一遍才保留这个结论。输出一律写成 UTF-8 with BOM，与输入编码无关。

**原文件去哪了？** 原地不动。结果写到新的 `*_Processed.*`，除非你勾了覆盖。勾了覆盖又勾了备份，
原内容留作 `{原名}.bak`。

**更新是怎么做的？** 图形界面在两个平台都只告诉你有没有新版本。命令行在 Windows 上能自己替换：
下载新 zip、核对 SHA256 与发布者签名、在本进程退出后换掉安装目录里的文件。签名缺失或验签失败
一律中止。Linux 上只有 `texttool update --check` 可用。

**为什么更新说版本倒退了？** 更新器把最近安装的版本记在 `last_known_version.txt` 里，
遇到版本号更低的发布包会拒绝。如果确实是有意回滚，删掉这个文件再试。

**配置文件坏了怎么办？** 程序会报出是哪个文件，不会静默按空规则跑。用关于页导出的副本覆盖回来，
或者删掉该文件回到默认值。

### 项目结构

```text
TextTool/
├── TextTool.sln
├── Directory.Build.props         版本号唯一来源
├── TextTool.Core/                引擎、配置读写、更新与签名校验
│   ├── default_schemes.json      内置默认替换方案
│   ├── default_vn_schemes.json   内置默认角色方案
│   ├── default_lint_rules.json   内置默认检查规则
│   └── pinned_roots.txt          更新链路用的公共根 CA 清单
├── TextTool.Gui/                 图形界面（Avalonia）
│   ├── App.axaml                 字号、字体、主题引用
│   ├── Views/                    每个页签与弹窗一对 .axaml 与 .axaml.cs
│   ├── Styles/                   Theme.axaml、Theme.Light.axaml、Theme.Dark.axaml、AppStyles.axaml
│   ├── Localization/             Loc.cs、LintTextSource.cs
│   └── Services/                 AppSettings.cs
├── TextTool.Cli/                 命令行入口，程序集名 texttool
├── TextTool.Tests/               xUnit 测试，覆盖 Core 与发布脚本
├── Localization/                 zh_CN.json、zh_TW.json、en_US.json
├── Resources/                    icon.ico 与关于页用的头像
├── tools/
│   ├── GuiSnapshot/              离屏界面验收工具
│   └── ReleaseSigner/            发布包签名工具
├── scripts/
│   ├── release.ps1               打 tag、等 CI、再调 publish.ps1
│   ├── publish.ps1               给发布资产签名并上传签名
│   └── github-release.ps1        GitHub REST 公共函数
├── doc/ReleaseNotes.md           升级提示，随每个 Release 正文发布
└── .github/workflows/build-test.yml
```

### 技术栈

| 部分 | 技术 |
| --- | --- |
| 运行时 | .NET 8 |
| 图形界面 | Avalonia 11.2.3，手写 axaml，不上 MVVM |
| 命令行 | 控制台入口，不引参数解析库 |
| 持久化 | `System.Text.Json` 写 JSON 文件 |
| 编码支持 | `System.Text.Encoding.CodePages`（GBK），随共享框架提供 |
| 测试 | xUnit 2.4.2 |
| 版本号 | `Directory.Build.props` 里单一 `<Version>` |

### 约定

代码、文档、提交信息里不用 emoji 做装饰。终端状态标记用纯文本。视觉小说排版用到的符号
属于业务字符，不算装饰。

---

© 2026 TwilightRain

## 终端字符

本仓的源码与文档只写 ASCII 与汉字、全角标点。这套东西要分发到还停在 Windows XP 那代的机器上，
那代控制台用点阵字体，字形来自当时的代码页：别的符号画出来是问号或空格，宽度也对不上，
整屏跟着错位。

口径：业务字符之外的符号不写进源码与文档；状态标记用纯文本，宽度与字形不依赖控制台字体。
