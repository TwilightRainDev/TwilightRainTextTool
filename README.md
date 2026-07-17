# TwilightRain Text Tool

<div align="center">

集行合并、文件拼接、中文截断修复、标点截断修复、标点替换于一体的 Windows 文本处理工具。

An all-in-one Windows text processing tool: line merge, file join, CJK truncation fix, punct. truncation fix & punctuation replacement.

---

[English](#english) · [中文](#中文)

</div>

---

<!-- ════════════════════════ ENGLISH ════════════════════════ -->

<div id="english"></div>

## English

A **Windows WinForms (.NET 7)** desktop utility that replaces four legacy batch/PowerShell scripts with a unified GUI.  
Drag-and-drop a text file, pick your options, click **Process** — done.

### Features

| Tab | Description |
| :-- | :---------- |
| **Line Merge** | Merge short lines up to a configurable threshold, with optional post-processing |
| **File Join** | Concatenate all matching files in a directory into one |
| **Punct. Replace** | Freely configurable find-&-replace rules with reordering, persisted as JSON |
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

```
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
- Drag-and-drop `.txt` file support
- Safe output: `*_Processed.*`

#### Tab 4 · About

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

- **.NET 7 Desktop Runtime** (Windows WinForms)
- Download from: [dotnet.microsoft.com/download/dotnet/7.0](https://dotnet.microsoft.com/download/dotnet/7.0)
- Choose **.NET Desktop Runtime 7.0 (x64)**
- Missing runtime shows a guided download dialog

### Running

#### Option A — Direct EXE

```
double-click bin/Release/publish/TextTool.exe
```

#### Option B — From source

```
dotnet run --project TextTool.csproj
```

#### Publishing

```
dotnet publish -c Release -o bin/Release/publish
# Output → bin/Release/publish/
```

### Project Structure

```
TextTool/
├── TextTool.csproj              # .NET 7 WinForms, v1.5.0
├── Program.cs                   # Entry point, registers GBK encoding
├── MainForm.cs                  # Main window (~1060 lines, 4 tabs)
├── Resources/
│   ├── icon.ico                 # App icon
│   └── TwilightRain.jpg         # Avatar in About page
├── Localization/
│   ├── Strings.cs               # Loc singleton (init, detect, switch, persist)
│   ├── zh_CN.json               # Simplified Chinese locale
│   ├── zh_TW.json               # Traditional Chinese locale
│   └── en_US.json               # English locale
├── Services/
│   ├── EncodingDetector.cs      # GBK / UTF-8 / UTF-16 auto-detection (4 KB header)
│   ├── LineMerger.cs            # Threshold-based line merge core
│   ├── FileJoiner.cs            # Multi-file concatenation
│   ├── CjkParagraphMerger.cs    # CJK truncation fix + no-merge support
│   ├── PunctTruncationMerger.cs # Punctuation truncation fix + no-merge support
│   ├── PunctuationReplacer.cs   # Single-line punctuation replacement engine
│   └── ProcessingPipeline.cs    # Orchestrates all steps in one memory pass
├── replace_rules.json           # Runtime-generated rules file (auto)
├── app_config.json              # Language preference (auto)
└── README.md
```

### Tech Stack

| Component | Technology |
|-----------|-----------|
| Framework | .NET 7 WinForms |
| UI construction | Pure C# (programmatic, no Designer files) |
| Encoding | `System.Text.Encoding.CodePages` |
| Output encoding | UTF-8 with BOM |
| Persistence | JSON (`System.Text.Json`) |
| i18n | Custom `Loc` singleton with JSON locale files |

### Legacy Script Mapping

| Script | Equivalent Operation |
|--------|---------------------|
| `MergeLines.bat` | Tab 1, threshold=12, character mode, drag file |
| `mergeline.bat` | Tab 1, threshold=20, byte mode, drag file |
| `mergeline.ps1` | Tab 1, threshold=20, character mode, drag file |
| `combine.bat` | Tab 2, pick folder, enter pattern |

### Version History

| Version | Date | Changes |
|---------|------|---------|
| 1.5.1 | 2026-07-17 | Fixed encoding detector: `IsValidUtf8` no longer falsely rejects files when the 4KB probe boundary cuts a multi-byte UTF-8 character mid-sequence (causing GBK fallback and garbled output) |
| 1.5.0 | 2026-07-17 | Reorderable replace rules (Up/Down); TwilightRain.jpg avatar; language selector moved to About page; all locale keys renamed to PascalCase; punct. truncation fix with custom punctuation; line-ending no-merge rule with custom punctuation; compact About page layout; button sizing fix; publish directory unified; 8f UI font |
| 1.4.0 | 2026-07-17 | Full i18n (zh_CN / zh_TW / en_US); `Loc` singleton with auto-detect + manual switch + JSON persistence; ProcessingPipeline single-pass refactor; CJK merger O(n²)→O(n); EncodingDetector 4KB header scan; replaces 3+2 file I/O with 1 write |
| 1.3.0 | 2026-07-17 | Punctuation replacement tab (CRUD + JSON persistence); About tab with app icon; CJK truncation fix; layout bug fixes |
| 1.0.0 | 2026-07 | Initial release: line merging + file joining |

---

<!-- ════════════════════════ 中文 ════════════════════════ -->

<div id="中文"></div>

## 中文

集行合并、文件拼接、中文截断修复、标点截断修复、标点替换于一体的 **Windows 文本处理桌面工具**，一键替代四个旧版批处理脚本。

### 功能概览

| 页签 | 说明 |
| :-- | :--- |
| **行合并** | 短行自动拼接至指定阈值，可选多重后处理 |
| **文件拼接** | 将目录中所有匹配文件合并为一个 |
| **标点替换** | 自由配置查找/替换规则，支持排序，JSON 持久化 |
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

```
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
- 支持拖放 `.txt` 文件
- 安全输出：`*_Processed.*`

#### Tab 4 · 关于

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

- **.NET 7 Desktop Runtime**（Windows WinForms）
- 从 [dotnet.microsoft.com/download/dotnet/7.0](https://dotnet.microsoft.com/download/dotnet/7.0) 下载安装 **.NET Desktop Runtime 7.0（x64）**
- 缺少运行时会弹出引导对话框

### 运行方式

#### 方式一 · 直接运行 exe

#### 方式二 · 从源码运行
```
dotnet run --project TextTool.csproj
```
#### 发布命令
```
dotnet publish -c Release -o bin/Release/publish
# 输出 → bin/Release/publish/
```
### 项目结构

```
TextTool/
├── TextTool.csproj              # .NET 7 WinForms, v1.5.0
├── Program.cs                   # 入口，注册 GBK 编码支持
├── MainForm.cs                  # 主窗口 (~1060 行, 4 个页签)
├── Resources/
│   ├── icon.ico                 # 程序图标
│   └── TwilightRain.jpg         # 关于页头像
├── Localization/
│   ├── Strings.cs               # Loc 单例（初始化、检测、切换、持久化）
│   ├── zh_CN.json               # 简体中文语言包
│   ├── zh_TW.json               # 繁体中文语言包
│   └── en_US.json               # 英文语言包
├── Services/
│   ├── EncodingDetector.cs      # GBK/UTF-8/UTF-16 自动检测（仅读 4KB 头部）
│   ├── LineMerger.cs            # 阈值行合并核心算法
│   ├── FileJoiner.cs            # 多文件拼接
│   ├── CjkParagraphMerger.cs    # 中文截断修复 + 不合并规则
│   ├── PunctTruncationMerger.cs # 标点截断修复 + 不合并规则
│   ├── PunctuationReplacer.cs   # 单行标点替换引擎
│   └── ProcessingPipeline.cs    # 流水线编排（一次内存遍历，一次写入）
├── replace_rules.json           # 运行时生成的替换规则文件（自动）
├── app_config.json              # 语言偏好设置（自动）
└── README.md                    # 本文件
```

### 技术栈

| 组件 | 技术 |
|------|------|
| 框架 | .NET 7 WinForms |
| UI 构建 | 纯 C# 代码（无 Designer 文件） |
| 编码支持 | `System.Text.Encoding.CodePages` |
| 输出编码 | UTF-8 with BOM |
| 持久化 | JSON（`System.Text.Json`） |
| 国际化 | 自定义 `Loc` 单例 + JSON 语言包 |

### 旧脚本映射

| 旧脚本 | 新工具操作 |
|--------|-----------|
| `MergeLines.bat` | Tab 1 行合并，阈值=12，字符数，拖放文件 |
| `mergeline.bat` | Tab 1 行合并，阈值=20，字节数，拖放文件 |
| `mergeline.ps1` | Tab 1 行合并，阈值=20，字符数，拖放文件 |
| `combine.bat` | Tab 2 文件拼接，选择目录，输入匹配模式 |

### 版本历史

| 版本 | 日期 | 更新内容 |
| :-- | :--- | :------- |
| 1.5.1 | 2026-07-17 | 修复编码检测器：`IsValidUtf8` 因 4KB 探测边界截断多字节 UTF-8 字符而误判为 false，导致回退到 GBK 产生乱码 |
| 1.5.0 | 2026-07-17 | 替换规则可排序（上移/下移）；关于页添加 TwilightRain.jpg 头像；语言选择从状态栏迁移至关于页；所有 Loc 键改为 PascalCase；新增标点截断断段修复（可自定义标点）；新增行尾部不合并规则（可自定义标点）；关于页紧凑布局；按钮尺寸修复；发布目录统一；8f 界面字体 |
| 1.4.0 | 2026-07-17 | 完整 i18n 国际化（zh_CN / zh_TW / en_US）；Loc 单例实现自动检测 + 手动切换 + JSON 持久化；ProcessingPipeline 单次遍历重构；CJK 合并 O(n²)→O(n)；EncodingDetector 4KB 头部扫描；3+2 次文件 I/O 简化为 1 次写入 |
| 1.3.0 | 2026-07-17 | 新增标点替换页签（CRUD + JSON 持久化）；关于页签与程序图标；中文截断修复；布局 bug 修复 |
| 1.0.0 | 2026-07 | 初始版本：行合并 + 文件拼接 |

---

<div align="center">

© 2026 **TwilightRain**

[English](#english) · [中文](#中文)

</div>
