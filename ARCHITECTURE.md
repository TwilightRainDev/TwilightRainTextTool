# 代码结构

原生版的文本工具：一套引擎，命令行与图形界面各自接在它上面。C++20，界面用 FLTK 1.4.5，两个平台共用一份源码。

## 目录与依赖

| 目录 | 作用 | 依赖 |
| --- | --- | --- |
| `src/engine/` | 规则解析、检查判据、标定、回复检查、四个处理功能、读稿写盘、配置包、多步链 | 不链接界面库 |
| `src/ui/` | 图形界面，按页一个文件 | 引擎 |
| `src/cli/` | 命令行入口 | 引擎 |
| `tests/` | doctest 单测与对照驱动 | 引擎 |
| `fixtures/` | 对照基准：规则表、检查命令、四个处理命令、多步链 | 无 |
| `third_party/` | doctest 2.4.11、nlohmann/json 3.11.3 | 无 |
| `assets/` | 内置规则表、内置角色表、随包字体与图片 | 无 |

一条硬约束：`src/engine/` 里不出现 `FL/` 头，CMake 的 engine 目标也不链界面库。命令行与界面都只连引擎，算法与内存账只记在一处。

## 处理流程

### 读稿与编码

- 编码按 BOM 定案；没有 BOM 时先看开头 4KB 是否是合法 UTF-8，再按块把全篇复验一遍，不合法就改判 GBK。判完之后写回也用同一个编码。
- `TextFileReader` 一份稿只判一次编码，之后按 64KB 一块读、边读边解成 UTF-8，块尾不切在半个字上。需要读两遍盘的步骤（换标点）第二遍用 `rewind()` 跳过重判。
- `TextWriter` 先写同名 `.tmp`，收尾改名成目标；Windows 上改名用系统的替换语义（`MoveFileExW`），别的平台用 `std::rename`。写到一半失败时临时文件删掉，目标文件不动。

### 四个单步功能

| 功能 | 管线 | 说明 |
| --- | --- | --- |
| 行合并 | `MergePipeline` | 按字数或字节数接回断行，四个后处理开关：中文截断、标点截断、去行首逗号、遇句末标点分段 |
| 文件拼接 | 逐块搬字节 | 按名字排序，文件之间补一个换行，输出 UTF-8 带 BOM |
| 标点替换 | `ReplacePipeline` | 先判这份稿算不算博文，再逐段遮、逐段换、逐段写 |
| 剧本排版 | `VnPipeline` | 一行判完并进当前段，一段攒够就排好交出去 |

四个功能都只做「文本进、文本出」，不碰盘；读文件、判编码、备份、写回由调用方接起来。管道逐段吃、逐段吐，内存里只有一块读缓冲、一块正文与管线自己的上下文，与稿件长短无关。

### 多步链

`engine/chain` 把接回、换标点、排版按调用方给的次序串起来。每一步的产物写成目标目录里的临时文件（正式跑 `.texttool-tmp-<进程号>-<步序号>`，预览 `.texttool-preview-...`），下一步重新读它；上一步的整份结果不留在内存里。最后一步改名成目标，目标已存在时先按设置留 `.bak`。

四条规矩：按停止只在两步之间收手，正在写的那一步写完再停；目标已存在时覆盖并留备份；强杀留下的临时文件下次启动按名字与进程探活清理；预览与正式跑用两套前缀，互不覆盖。

## 检查

- 规则表是数据：内置表在 `assets/default_lint_rules.json`，界面改过的条目写在程序目录的 `lint_rules.json`，按编号整条覆盖内置，读入后统一校验（取值、必填、正则能编、算法名存在）。写回时以盘上那项原文为底逐个字段覆盖，这一版不认识的字段原样保留。
- 命中表默认按每条规则 200 处封顶，报告只列前 200 处，超出只计数；报告排版只有一处（`engine/report`），命令行与界面的报告面板用同一个函数。
- 一条规则常带几十个字面量词。能证明「命中起点必然落在这几串字面量上」的模式，编译时收成一张三字节前缀索引，全文只扫一趟，候选位置再交给原匹配器确认；证不出来的模式照旧逐条扫。规则表与报告不受影响，新增的是 `src/engine/needle_scan` 与 `regex` 里的起点证明。
- 正则匹配层在跑完整匹配前先排除不可能的位置（字面量前缀、首字节范围、必含串）；只排除，不判定，命中仍由原匹配器定。
- 数据规则按条分给最多 8 条线程，各扫各的，最后按规则表原次序把命中收进结果。行索引、博文遮罩、空格副本、段落切分只建一份，线程只借不写；算法规则仍串行，因为它们会替排在前面规则剔命中。短于 128KB 或只点了一两条规则时走单线程。
- 要写命中日志时（命令行 `--log`，或界面「文字体检」页的「记下检查命中」勾选框）这一趟不封顶，先按全部命中写日志，再把报告裁到 200 处。日志与命令行 `--log` 的格式相同，界面那份写到用户状态目录的 `texttool/hits.jsonl`。这一路峰值会比平时高（真稿上约 45MB，超过 40MB 线），默认关闭，验收不碰它。
- 博文稿的结构区域先写成等长的空格副本再判，头部字段、围栏代码块、行内代码、公式、链接地址、引用定义、HTML 注释都不参与检查与替换；判定按行状态机走，换标点时不必为整份稿建一张表。
- 标定（`calibrate`）与回复检查（`reply-check`）各占一个模块，判定与输出与检查命令同一套。
- 性能与内存：命令行检查、处理、界面的内存都按整进程最高值量，上限 40MB；`tools/measure_peak.py` 取 20 毫秒采样最高与 `VmHWM` 里大的那个，`tools/acceptance_check.py` 用它判过线。

## 界面

### 页与页之间

`app.cpp` 只建窗口、页签条、状态条，加载规则表，然后建七页：处理、关于，以及「高级」下拉里的接回断行、合并多份、换标点、剧本排版、文字体检。每页一个文件，页面只依赖 `AppContext`（窗口、页签条、规则表、页登记表）与 `widgets`，不摸别人的控件。

页与页之间按页标识查登记表，不存全局页指针。页面基类负责收拖放、按当时尺寸重排；所有位置写在 `layout()` 里，窗口缩放时逐页重排一次。

### 处理页

处理页是唯一收稿、写盘、开跑的一页：

1. 从源文件行取稿子或文件夹；勾选项与高级页的参数拼成一条链。
2. 合并多份在链前单独做：按名字模式挑稿，逐块接进系统临时目录里的一个文件，再把这一份交给链。
3. 接回、换标点、排版的次序固定；检查不进链，在产物上另跑一遍并排成人读报告。
4. 结果写在源文件旁边（名字加 `_Processed`）或写回原文件；覆盖且不留备份时动手前确认一次。

预览面板默认收起，按「先看效果」展开：读同一条链产物的开头两万字；面板里的「保存」按同一组参数再跑一遍，把完整结果写到用户挑的位置。预览本身不写目标文件。

### 外观

- 颜色只在 `theme.cpp` 的配色表里给值，控件记颜色槽位；浅色与深色两套，切换后整个界面立刻重画。
- Windows 上标题栏由系统画，换底色时给每个窗口设一次深色或浅色标题栏，弹窗显示之后也设一次；Linux 上标题栏归窗口管理器。
- 随包字体按「与程序同目录」找，注册进本进程再用；找不到或注册不上才退回系统字体。
- 界面默认 1000x620，最小 900x560，上限交给屏幕；屏幕更大时按 DPI 与可用区域放大到 1、1.25、1.5、1.75、2 中的一档。
- 同时最多四份界面，第五份不开。

### 只给开发与验收用的开关

| 环境变量 | 作用 |
| --- | --- |
| `TEXTTOOL_UI_SCALE` | 固定缩放比例，截图对照时用 1 |
| `TEXTTOOL_UI_SIZE` | 指定启动尺寸，同时把最小尺寸放成同一个数 |
| `TEXTTOOL_START_TAB` | 启动翻到第几页（页签层的扁平序号） |
| `TEXTTOOL_LAYOUT_CHECK=1` | 三档尺寸逐页查控件越界与重叠；通过时退出码 0 |
| `TEXTTOOL_SELF_CHECK=<稿件>` | 自检模式：把这份稿填进处理页并按勾选处理、写盘，标准输出报字数与耗时 |
| `TEXTTOOL_SELF_CHECK_PAGE=1..4` | 自检只跑接回、合并、换标点、排版其中一项 |

## 配置

配置文件与程序同目录，整个目录可以拷走。读与写用同一份：

| 文件 | 内容 |
| --- | --- |
| `replace_rules.json` | 换标点的规则列表 |
| `replace_schemes.json` | 有名字的替换方案 |
| `vn_schemes.json` | 角色表与路线名 |
| `lint_rules.json` | 改过的检查项，按编号覆盖内置 |
| `default_lint_rules.json` / `default_vn_schemes.json` | 随包发的内置表，只读 |

环境变量可以把某一份指到别处：`TEXTTOOL_REPLACE_RULES_JSON`、`TEXTTOOL_SCHEMES_JSON`、`TEXTTOOL_VN_SCHEMES_JSON`、`TEXTTOOL_LINT_RULES_JSON`；内置检查表本身用 `TEXTTOOL_RULES_JSON`。

界面偏好（上次浏览的目录、深浅色）放在用户配置目录的 `ui.conf`：Linux 是 `~/.config/texttool/ui.conf`，Windows 是 `%APPDATA%\texttool\ui.conf`。

所有写盘走同一个写法：先写临时文件，收尾替换目标；覆盖前按设置留 `.bak`，历史最多三份。配置包是单个 JSON，四样内容各占一项（替换规则、替换方案、角色方案、底色），导入先整份读通再写，读不通一个字不写。

路径在两处要留意：递归收目录、检查清单、真实路径与绝对路径一律用斜杠分隔，两个平台的报告与排序才一致；文件路径按系统码页解释，中文 Windows 的码页是 936，中文路径没问题，码页表示不了的字符打不开。

## 命令行契约

七个命令：`merge`、`replace`、`vn`、`join`、`lint`、`calibrate`、`reply-check`。这些不随界面改动：子命令名与参数、`lint --json` 的顶层 `Version: 4` 与字段语义、退出码（0 正常、1 有问题或命中、2 用法或读取错误）、人读报告排版、`--log` 一行一条的写法、`.bak` 轮换。

输入可以给多个文件、目录或 `-`（标准输入）；目录递归收 `.txt` 与 `.md`，按路径排序，软链接跟到真实目标、一份稿只算一次。检查还支持 `--only`、`--min-severity`、`--genre`、`--baseline`。

## 构建

### Linux

```bash
export PATH=/home/twilightrain/Tools/cmake-env/bin:$PATH
cmake -S . -B build-linux -DCMAKE_BUILD_TYPE=Release \
      -DTEXTTOOL_FLTK_PREFIX=/home/twilightrain/Tools/fltk-1.4.5-linux
cmake --build build-linux -j
```

### Windows

Windows 工具不接受网络共享路径当工作目录，构建目录要落在 Win 盘上：

```bat
call E:\CodeApps\Cursor\cpp\Root\VC\Auxiliary\Build\vcvars64.bat
E:\Tools\cpp\cmake-3.30.5-windows-x86_64\bin\cmake.exe ^
  -S "\\wsl.localhost\Ubuntu\home\twilightrain\WorkZone\HumanProject\TextTool.Native" ^
  -B "E:\WorkZone\build\tt-win-gui2" ^
  -G "NMake Makefiles" -DCMAKE_BUILD_TYPE=Release ^
  -DTEXTTOOL_FLTK_PREFIX=E:/WorkZone/build/fltk-win/install
E:\Tools\cpp\cmake-3.30.5-windows-x86_64\bin\cmake.exe --build E:\WorkZone\build\tt-win-gui2 --config Release
```

Windows 侧 `/MT`、Linux 侧静态链 libstdc++ 与 libgcc，交付机器不需要另装运行库。界面程序的子系统是 Windows GUI，双击不弹控制台窗口。

## 验证

每条命令各管一类证据，任一条红了都算没做完：

```bash
./build-linux/engine_tests              # 引擎单测
bash tests/compare_cli.sh               # 检查、标定、回复检查的逐字节对照
bash tests/compare_processing.sh        # 四个处理命令的逐字节对照
bash tests/compare_chain.sh             # 多步链的逐字节对照
TEXTTOOL_LAYOUT_CHECK=1 ./build-linux/texttool-gui   # 控件位置
python3 tools/acceptance_check.py --windows E:/WorkZone/build/tt-win-gui2/texttool.exe
```

Windows 上另跑 `engine_tests.exe` 与界面自检（自检的开关见上面那张表）。

对照基准不手改：检查与两个只读命令的基准由归档的桌面版生成（`fixtures/cli/make_goldens.sh`），处理命令在 `fixtures/processing/cli/cases.tsv` 里现跑参考实现比对，引擎层的处理用例由 `fixtures/processing/merge_join_replace/make_goldens.sh` 出。表里标 `known` 的用例是有意不同：写盘保持原稿编码、读不到文件时两边报错措辞不同、CRLF 与空行的处理各有取舍。多步链的基准由原生命令行按同一顺序跑两遍生成（`fixtures/processing/chain/make_goldens.sh`），`build-linux/chain_runner` 只给对照脚本用。

## 归档的桌面版

桌面版（3.1，.NET 8 + Avalonia）归档在 `Config/Archive/TextTool`，自带 README 与构建方法，只用来生成对照基准与查历史行为。现在的程序与文档都指原生版；归档目录不参与构建，也不随发布包发。
