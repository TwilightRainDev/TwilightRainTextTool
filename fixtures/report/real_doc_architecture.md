# 代码结构

TextTool 是 .NET 8 工具，一套引擎供 Avalonia 图形界面与命令行使用。

| 目录 | 作用 | 依赖 |
| --- | --- | --- |
| `TextTool.Core/` | 引擎、配置读写、方案与规则模型、更新与签名校验 | 不引用任何界面框架 |
| `TextTool.Gui/` | 桌面界面 | `TextTool.Core` |
| `TextTool.Cli/` | 命令行入口，程序集名 `texttool` | `TextTool.Core` |
| `TextTool.Tests/` | xUnit 测试，Core 单测与发布脚本行为 | `TextTool.Core` |

`tools/GuiSnapshot/`（离屏验收）与 `tools/ReleaseSigner/`（包签名）不进解决方案，不随应用发布。

## 分层

一条标准：能不能脱离界面测试，能的进 Core。文件名里不出现 Form、Window、Control；文本算法、批量循环、落盘都在 Core；Cli 只解析参数、调 Core、打印、返回退出码，不复制引擎逻辑，也没有自己的配置格式。依赖单向，Core 不引用界面。

界面不上 MVVM：视图状态就是控件状态，点「处理」那一刻读一次交给 Core。代价是视图逻辑不能单测，能测的行为都推进了 Core。

## 数据文件与位置

配置与程序同目录，取 `AppContext.BaseDirectory`，整个目录可以直接拷走。每个程序读自己所在目录的那一份，两个程序装在不同目录时互不相干：发版是四份 zip，界面与命令行分开下载、分别解压，不要求装到一处；只有解压到同一个目录，两边才共用同一个文件。

所以在界面里改完替换规则、方案或检查规则，命令行版要跟着生效，两条路：把两个程序放在同一目录，或者把改过的 json 拷到命令行版旁边。界面「导出配置」只带 `app_config.json` 与 `replace_rules.json`，方案与检查规则得自己拷。

| 文件 | 内容 | 读写方 |
| --- | --- | --- |
| `app_config.json` | 界面偏好：语言、深浅色 | Gui 读写自己目录那份 |
| `replace_rules.json` | 标点替换规则列表 | Gui 写，Gui 与 Cli 各读自己目录那份 |
| `replace_schemes.json` | 命名的替换方案 | Gui 写，Gui 与 Cli（`--scheme`）各读自己目录那份 |
| `vn_schemes.json` | 角色与路线方案，含可选场景正则 | Gui 写，Gui 与 Cli（`--scheme`）各读自己目录那份 |
| `lint_rules.json` | 外部检查规则，按 Id 覆盖内置 | Gui 写，Gui 与 Cli 各读自己目录那份 |
| `last_known_version.txt` | 最近一次成功安装的版本，挡降级重放 | Cli 更新流程 |

内置默认值编在 Core 里，不走文件：`default_schemes.json`、`default_vn_schemes.json`、`default_lint_rules.json`、`pinned_roots.txt`。

关于页的程序图标与作者头像是作者本人约的稿，可以随产品一起分发，不必再查授权；两个文件在 `Resources/`（`icon.ico`、`TwilightRain.jpg`），界面里引用的是 `TextTool.Gui/Assets/icon.png` 与链过去的头像。原生版要用同一组素材，出处同上。

## 文本处理管道

`ProcessingPipeline.Run` 一次内存操作走完：读取、行合并、中文截断修复、标点截断修复、去行首逗号、标点替换、写出。

写盘由 `WriteMode` 决定：`NewFile` 写 `*_Processed.*`（源文件不动），`OverwriteWithBackup` 覆盖前备份，`Overwrite` 直接覆盖，默认 `NewFile`。

编码检测只读头部 4096 字节，按 BOM、UTF-8 合法性、GBK 回退判断；判为无 BOM 的 UTF-8 时全文流式再扫一遍，有非法序列就改判 GBK。输出统一 UTF-8 with BOM。

## 博文稿的结构跳过

`BlogMarkdownMask` 先认稿子是不是博文，认出来才把非正文区域标成遮罩，检查与标点替换都碰不到。

- 认法：头部字段里同时有 `title` 与 `date`，或者出现过以 `::` 开头的行。只看「有头部字段」不够，VuePress、Docusaurus 那类文档站也带头部字段，它们的行内代码是证据。认不出就完全不遮。
- 遮哪些：`---` 包起来的头部字段、以 `::` 开头的整行、围栏代码块与行内代码、`$...$` 与 `$$...$$`、链接与图片地址、引用式链接定义行、HTML 注释。`:::fold`、`:::admon` 包着的散文照常检查，只跳过指令行。
- 遮罩与原文等长、逐位对齐，被遮字符在规则眼里是空格，换行不动，所以行列号与 `Match`、`Snippet` 仍从原文取；`LintReport.Chars` 扣掉被遮部分，密度按正文字数算，被遮的整行在段落切分里当空行。
- 标点替换按遮罩切段，遮住的地方原样拼回，横跨代码块边界的规则不生效。行合并逐行调用，看不到整份稿子，那里传 `protectBlogRegions: false`。

两个字段决定判据跑不跑：`SkipInBlog` 只在没指定档位的博文稿上生效，标在叙述过去的五条（`no-debt-declaration`、`completion-log`、`supersession`、`tombstone-of-deletion`、`re-report-suppression`）与 `char-face` 上；`SkipInMarkdown` 在段落口径判成 markdown 时跳过，标在 `fiction-bold-template` 与 `inline-title-list` 上。

## 检查命令的对外面

`lint` 给整份稿件复检，命中按段内密度判并带统计项；`reply-check` 只回位置，出现即算，围栏整块跳过；`calibrate` 把同一套规则跑在人写与模型写两组文本上，出按模型侧密度排序的表，判定可分、弱、不可分。同一条规则在两个入口的命中数因此不同。规则表的 `BlocksReply` 只给闸用，`lint` 不看。

两个外部消费者：回答闸（`Config/Extensions/guard/reply/`）调 `reply-check`；护栏写侧（`guard/write/history.ts`）调 `lint <写后全文> --genre doc --baseline <写前全文> --json`，超时两秒，只取五条规则的 Id 与 `Notes` 里第一个整数，路径豁免留在护栏。工具起不来、超时、退出码 2，这条判据停用并提示一次。

回答闸那边有两样已经由测试守住，改了会红：`reply/rules.ts` 里每个 `projectIds` 都必须被 `ReplyShapeScanner.RuleIds` 覆盖；`host.ts` 里 `TOOL_BUILD` 的 dll 名与目标框架目录必须与命令行程序集一致。这条校验从测试程序集目录向上找 `Config/Extensions/guard/reply/rules.ts`，找到才跑，找不到（只有本仓库的 CI）带原因跳过，跳过等于这一轮没查。

仍靠人对齐：命令行参数的名字与取值、`--json` 各字段的含义、退出码怎么用、写侧取哪五条规则与 `Notes` 里那个整数的位置。改这些要自己通知护栏，测试不会报。

人读输出与退出码这两件事是脚本能看见的面，规则如下。一次给多份输入时，人读报告每份前面一行文件名（整行就是那份稿的路径），只有一份时不印，单文件输出与从前一致。`lint` 的输入按真实路径去重，软链接跟到目标，指回上级的链接不会把一棵树展开两次。子目录读不了只报那一条路径，其余照常检查。一份稿读不了不打断整批：原因连同路径进 stderr，报告只覆盖读得到的部分，跑完退 2。`--baseline` 只收一份输入，份数按目录展开后的算。

`lint --json` 契约：

- 外层 `{"Version": 4, "Reports": [...]}`，一份输入一个报告；报告带 `File`、`Chars`（正文字数）、`ParagraphMode`（`line` 或 `markdown`，按首 4096 字符判）、`Hits`、`Notes`。
- 命中带 `Id`、`Group`、`Title`、`Severity`、`Line`、`Col`、`Length`、`Match`、`Snippet`、`Detail`、`Hint`、`SuggestScheme`；按 `Line`、`Col` 排序，同位置保持规则表与模式顺序；`Match` 与 `Snippet` 是原文连续切片，跨行保留换行。
- `--baseline` 只报净增：`Hits` 是新增位置，`BaselineHits` 是基线命中数，碰过任一份文本的规则各得一条 `Notes` 写「相对基线新增 N 处」；只收一份输入，基线必须是路径。
- 行尾 LF；`"`、`&`、`'`、`+`、`<`、`>`、反引号写成 `\uXXXX`，反斜杠写成 `\\`；`--log <文件>` 另存一行一条的 JSON，只留 ASCII、汉字与全角标点，默认不写。
- `calibrate --json` 另算一份契约：`Version` 为 1，带 `Genre`、两侧 `Files`/`Chars`/`Hits`/`PerKChar`、可分规则数与逐条明细。

`repeated-punctuation` 与 `ellipsis-style` 会故意在同一段重复标点上各报一条，提示不同；要一处一条的消费者自己按位置去重。

## 检查的并发与速度

多份一起扫走 `LintBatch`：同一时刻最多八份，读与扫在后台线程上并行，结果按传进来的文件顺序交回调用方的线程。顺序是契约：人读报告的文件顺序、命中日志的行顺序、失败提示的位置都靠它，并行只压缩等待。一份稿读不了仍原位报出来，其余照常往下出。

走并行的只有多文件的 `lint`，以及 `calibrate` 两侧的扫描。单份输入、带 `-` 的调用逐份跑：stdin 只有一条流，`--baseline` 只收一份输入。

引擎（`LintSession`）一次建好、逐份复用，扫描不改自身状态，并发扫与逐份扫得到同一份结果。`calibrate` 两侧各建一次引擎，文件再多也只有这一次开销。

速度基线：引擎内扫三十五万字的稿件约 0.2 秒，命令行一次调用另有约 0.1 秒的进程启动与规则装载。护栏给这个工具两秒，`LintSpeedTests` 拿两秒当上限盯着这条底线，慢到一个数量级就会红。

## 判据的边界

判据看词形、段内密度、句式位置，以及这一轮有没有调工具这类载体事实；语义不判。同形的句子可以意思相反（「我不猜，我去查」是好句子），按字面判实测误判两成，误掉的恰好是唯一带信息的那处。密度按段算，一段两处以上才算数；判一份稿子看总体密度，别拿单条规则的强弱当结论。

判据是数据不是代码：界面能改，改动落到 `lint_rules.json` 按 Id 覆盖内置。写盘前先按内置合并再校验，坏正则会被挡下，理由是写进去就让下次启动直接抛。

## 界面、本地化与写盘

- 视图是手写 `.axaml`，行为在同目录的 `.axaml.cs`。颜色与字号只在 `TextTool.Gui/Styles/` 与 `App.axaml` 定义一次，视图里只写 `{DynamicResource 键名}`；代码里取画刷必须带当前主题变体，不带就取不到主题里的键，颜色悄悄退化成正文色。
- 错误、警告、结果都写行内，不弹系统对话框（抢焦点、打断批处理、盖住上一次结果）。状态栏归主窗口，页签之间不互相引用。标点替换规则由 `MainWindow` 读一次，同一个列表交给两个页面。
- 文案在三份 json 里，编进程序集，装载顺序固定：读偏好、设主题、`Loc.Init`、建窗口，反了首帧全是方括号。缺键返回 `[键名]`，语言文件解析失败得到空字典（满屏方括号但不崩）。`Loc.T` 实参个数与三份 json 的键由测试守住。
- 写文件走 `AtomicFile`（先写 `.tmp` 再整体替换）。配置不存在视为首次使用；存在但读不出或解析失败抛 `InvalidDataException`，消息带文件名与恢复建议。`AppSettings` 例外：读失败回落默认，写失败静默。

## 更新链

只有命令行版自动替换，图形界面只提示有没有新版本。

- `UpdateClient`：主机白名单固定、禁自动重定向、响应有字节上限；链根不在 `PinnedRoots` 时记一次「检测到中间代理」，设了 `TEXTTOOL_UPDATE_STRICT_TLS=1` 直接拒绝。
- `SelfUpdater`：查最新 release、挡降级、下载 zip 与校验文件、比对 SHA256、验 ECDsa P-256 签名、解压、生成替换脚本待进程退出后执行。验签失败一律中止，没有只比 SHA256 的降级路径。替换脚本是 Windows 批处理，Linux 只保留 `update --check`。

## 测试与构建

`dotnet test TextTool.Tests/TextTool.Tests.csproj` 跑这几样：`Services/` 的单测（文件相关用 `TempFile`、`TempDir` 夹具）、`Scripts/` 真跑两个发版脚本并钉住资产名单，外加一条跨仓契约校验（读护栏的规则表比对 `RuleIds`，护栏不在场时带原因跳过）、`Localization*.cs` 守三份 json 的键与占位符。离屏验收不进 CI：`dotnet run --project tools/GuiSnapshot -c Release -- <输出目录>`。

## 技术栈与约定

| 部分 | 技术 |
| --- | --- |
| 运行时 | .NET 8 |
| 图形界面 | Avalonia 11.2.3，手写 axaml，不上 MVVM |
| 命令行 | 控制台入口，不引参数解析库 |
| 持久化 | `System.Text.Json` |
| 编码支持 | `System.Text.Encoding.CodePages`（GBK），随共享框架提供 |
| 测试 | xUnit 2.4.2，界面验收用 Avalonia headless |

- 版本号只有一个来源，`Directory.Build.props` 的 `<Version>`；关于页显示前三段，命令行输出 `v` 加前三段，发布脚本不传参数时也读这里。

## 原生版工程

客户要求 15MB 稿件干活峰值不超过 40MB，.NET 版做不到，所以有一份全量重写的原生版。位置 `HumanProject/TextTool.Native`，与 .NET 版同仓库：规则表、对照稿件、参考实现都在一起，比对不用来回搬文件。

| 目录 | 作用 | 依赖 |
| --- | --- | --- |
| `src/engine/` | 规则解析、判据、四个处理功能 | 不链接界面库，命令行与界面都只连它 |
| `src/ui/` | 图形界面，FLTK 1.4.5，两个平台共用 | 引擎 |
| `src/cli/` | 命令行入口 | 引擎 |
| `tests/` | doctest 单测与对照用例 | 引擎 |
| `fixtures/` | 对照基准，目前是规则表基准 `rules_3_1.json` | 无 |
| `tools/RuleDump/` | 生成规则表基准的 .NET 工具，不发布、不进 `TextTool.sln` | `TextTool.Core` |
| `third_party/` | 单头文件库：doctest 2.4.11、nlohmann/json 3.11.3 | 无 |

**界面版式**：照客户已经看惯的桌面版排，白底、无边框页签、按钮在左下角、最底下一浅灰状态条；页签名与每页控件顺序也照那一版。界面库自带的风格都是九十年代的样子，页签条与主按钮是自己画的，控件外观借用 gtk+ 风格的圆角与浅色边框。两张对照图在 `tools/probes/`：`desktop-ui-merge-windows.png` 与 `native-ui-merge.png`。

**内存账**：验收按整进程峰值算。Linux 上量过，带中文界面的空窗口 16.3MB，其中程序自申请 4.3MB、映射的文件页 11.5MB。剩下的二十来兆要同时装下引擎与稿件，所以稿子在内存里只存一份 UTF-8，任何情况下不整份转成宽字符：转一份 15MB 的稿子就是 30MB，单这一项就超线。

**构建**：Linux 与 Windows 各一条命令，工具装在哪个路径见工作区的 TextTool 变更记录。Linux 那条已经跑通；Windows 那条同形，但 `vcvarsall` 只能在 Windows 里执行，第一次在 Windows 上构建时要照着验一遍，验之前不算数。

```
# Linux，CMake 在 /home/twilightrain/Tools/cmake-env/bin
cmake -S . -B build-linux -DCMAKE_BUILD_TYPE=Release && cmake --build build-linux -j
./build-linux/engine_tests

# Windows，先 vcvarsall.bat x64 初始化环境
cmake -S . -B build-win -DCMAKE_BUILD_TYPE=Release && cmake --build build-win
build-win\engine_tests.exe
```

Windows 侧 `/MT` 由 `CMakeLists.txt` 统一设定，Linux 侧对应的是静态链接 libstdc++ 与 libgcc，交付机器不需要另装运行库。

**对照基准怎么来**：规则表的基准由 3.1 的解析器导出，不是手抄。

```
dotnet build tools/RuleDump -c Release
dotnet run --project tools/RuleDump -c Release --no-build > fixtures/rules_3_1.json
```

测试拿基准比的是解析后的结构，不是文件字节：同一份 JSON，3.1 与原生版读出来的每条规则、每个字段都要一样，包括没写字段时的默认值（严重程度默认 info、适用范围默认 document、档位默认空）。

**内存怎么量**：`tools/measure_peak.py` 启动程序、每 20 毫秒采一次，报整进程峰值并拆成程序自申请与映射文件页两项。给 `--limit 40` 时超线退出码为 2，可以直接当验收脚本用。
