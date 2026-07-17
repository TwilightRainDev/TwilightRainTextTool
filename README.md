# TwilightRain Text Tool

<p align="center">
  <a href="#english">English</a> · <a href="#中文">中文</a>
</p>

---

<!-- ════════════════════════ ENGLISH ════════════════════════ -->

<div id="english"></div>

## English

A Windows desktop text processing tool — line merging, file joining, CJK truncation fix & punctuation replacement.

### Features

| Tab | Description |
| :-- | :---------- |
| Tab 1 · Line Merger | Merge short lines up to a configurable threshold |
| Tab 2 · File Joiner | Concatenate all matching files in a directory into one |
| Tab 3 · Punctuation Replacement | Freely configurable find-&-replace rules, persisted as JSON |
| Tab 4 · About | Version, author, GitHub link |

#### Tab 1 · Line Merger

- Merge short lines up to a configurable threshold (bytes or characters, 1–1000 range)
- Dual mode: byte count or character count
- Auto-detect GBK / UTF-8 / UTF-16 encoding
- Drag-drop `.txt` files onto the window
- Safe output: saves as `_Processed.ext` — **never overwrites** originals
- CJK truncation fix: auto-merge when a line ends with a CJK character
- Optional punctuation replacement (syncs with Tab 3 rules)

#### Tab 2 · File Joiner

- Merge all matching files in a directory into one
- Files sorted by name before merging
- Each file read with its own encoding
- Unified output as UTF-8 with BOM

#### Tab 3 · Punctuation Replacement

- Freely configurable find-&-replace rules
- Rule list with live preview (e.g. `。。 → 。`)
- Add, update, and delete rules
- JSON persistence — rules survive restarts
- Drag-drop `.txt` file support
- Safe output: `_Processed.ext`

#### Tab 4 · About

- App icon, name, version, author info
- GitHub link

### Requirements

- **.NET 7 Desktop Runtime** (Windows WinForms)
- Download from: [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/7.0)
- Missing runtime shows a guided dialog

### Running

#### Option A — Direct EXE

```
double-click publish/TextTool.exe
```

#### Option B — From source

```
dotnet run --project TextTool.csproj
```

#### Publishing

```
dotnet publish -c Release -o publish
```

### Project Structure

```
TextTool/
├── TextTool.csproj              # .NET 7 WinForms project
├── Program.cs                   # Entry point, registers GBK encoding
├── MainForm.cs                  # Main window (4 tabs)
├── Resources/
│   └── icon.ico                 # App icon
├── Services/
│   ├── EncodingDetector.cs      # GBK / UTF-8 auto-detection
│   ├── LineMerger.cs            # Line-merge core algorithm
│   ├── FileJoiner.cs            # File concatenation
│   ├── CjkParagraphMerger.cs    # CJK truncation fix
│   └── PunctuationReplacer.cs   # Punctuation replacement engine
├── publish/                     # Published output
├── replace_rules.json           # Runtime-generated rules file
└── README.md
```

### Tech Stack

- **.NET 7 WinForms**
- `System.Text.Encoding.CodePages`
- UTF-8 with BOM output
- JSON persistence

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
| 1.3.0 | 2026-07-17 | Punctuation replacement tab (CRUD + JSON), About tab, CJK truncation fix, app icon; fix layout bug |
| 1.0.0 | 2026-07 | Initial release: line merging + file joining |

---

<p align="center"><a href="#中文">中文</a></p>

---

<!-- ════════════════════════ 中文 ════════════════════════ -->

<div id="中文"></div>

## 中文

集行合并、文件拼接、中文截断修复、标点替换于一体的 Windows 文本处理桌面工具。

### 功能概览

| Tab | 说明 |
| :-- | :--- |
| Tab 1 · 行合并 | 短行自动拼接至指定阈值，修复断句文本 |
| Tab 2 · 文件拼接 | 将目录中所有匹配文件合并为一个 |
| Tab 3 · 标点替换 | 自由配置查找/替换规则，JSON 持久化 |
| Tab 4 · 关于 | 版本号、作者、GitHub 链接 |

#### Tab 1 · 行合并

- 短行自动拼接至可调阈值（字节数或字符数，1–1000）
- 双模式：字节计数 / 字符计数
- 自动检测 GBK / UTF-8 / UTF-16 编码
- 支持拖放 `.txt` 文件到窗口
- 安全输出：保存为 `_Processed.ext`，**绝不覆盖**原始文件
- 中文截断修复：行末为汉字时自动拼接下行
- 可选标点替换：可联动 Tab 3 中的规则

#### Tab 2 · 文件拼接

- 将目录中所有匹配文件合并为一个
- 按文件名排序后合并
- 每个文件按自身编码读取
- 统一输出为 UTF-8 with BOM

#### Tab 3 · 标点替换

- 自由配置查找/替换规则
- 左侧规则列表，右侧编辑（如 `。。 → 。`）
- 支持添加、更新、删除规则
- JSON 持久化保存，重启不丢失
- 支持拖放 `.txt` 文件
- 安全输出：`_Processed.ext`

#### Tab 4 · 关于

- 展示程序图标、名称、版本号、作者信息
- 提供 GitHub 链接

### 系统要求

- **.NET 7 Desktop Runtime**（Windows WinForms）
- 从 [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/7.0) 下载安装 **.NET Desktop Runtime 7.0（x64）**
- 缺少运行时会弹出引导对话框

### 运行方式

#### 方式一 · 直接运行 exe

```
双击 publish/TextTool.exe
```

#### 方式二 · 从源码运行

```
dotnet run --project TextTool.csproj
```

#### 发布命令

```
dotnet publish -c Release -o publish
```

### 项目结构

```
TextTool/
├── TextTool.csproj              # .NET 7 WinForms 项目文件
├── Program.cs                   # 入口，注册 GBK 编码支持
├── MainForm.cs                  # 主窗口界面（四页签）
├── Resources/
│   └── icon.ico                 # 程序图标
├── Services/
│   ├── EncodingDetector.cs      # GBK/UTF-8 自动检测
│   ├── LineMerger.cs            # 行合并核心算法
│   ├── FileJoiner.cs            # 文件拼接
│   ├── CjkParagraphMerger.cs    # 中文截断修复
│   └── PunctuationReplacer.cs   # 标点替换引擎
├── publish/                     # 发布输出目录
├── replace_rules.json           # 运行时生成的规则文件
└── README.md                    # 本文件
```

### 技术栈

- **.NET 7 WinForms**
- `System.Text.Encoding.CodePages`
- UTF-8 with BOM 输出
- JSON 持久化

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
| 1.3.0 | 2026-07-17 | 新增标点替换页签（CRUD + JSON 持久化）、关于页签、中文截断修复、程序图标；修复布局 bug |
| 1.0.0 | 2026-07 | 初始版本：行合并 + 文件拼接 |

---

<p align="center"><a href="#english">English</a></p>

---

© 2026 **TwilightRain**
