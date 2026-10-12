# 升级提示（随 GitHub Release 说明发布）

`v2.6.1` 及更早版本的客户端**无法自动升级**：内置的替换脚本缺 CRLF 落盘、绝对路径、
等待次数上限三项修正，等待循环里的 `tasklist` 对已退出的进程也返回 0，替换会卡在等待环节而不报错。
替换脚本由客户端在运行时自己写出，修复无法回溯到已出货的旧客户端。

**这类用户请手动升级**：从本 Release 下载 `TextTool-GUI-<版本>-win-x64.zip`（或 CLI 包），
解压覆盖原目录即可；`texttool update` 在这批版本上不可用。

`v2.6.2` 及之后版本的自动更新正常（替换脚本已含三项修正）。

## v3.0.0 的对外契约变更

- `--json` 契约版本由 `1` 升到 `2`：行尾归一为 LF（此前缩进换行是 CRLF）；HTML 敏感字符
  （`"` `&` `'` `+` `<` `>` 与反引号）转义为 `\uXXXX`，中文仍不转义；`Hits` 保证按 `(Line, Col)`
  升序、同位置保持引擎顺序；新增 `ParagraphMode` 字段（`line` / `markdown`）。
- `Match` 与 `Snippet` 是原文的连续切片：命中跨行时含换行；命中本身在一行内、但前后各 8 字的
  窗口跨到行边界时同样含换行（对行距密的文本，后者是常见情形）。CRLF 输入下含 `\r`
  （读盘不做行尾归一化）。
- 人读报告：行尾统一 LF；命中行新增 `<- 方案：<名>` 提示；分组顺序改为按各组首命中位置。
- `lint` 的段落口径按首 4096 字符的空行占比自动判定（≥10%、有空行且非全空判 Markdown，空行分段、
  软换行同段、列表项与编号行自成段），`ParagraphMode` 随报告输出，人读 `[NOTE]` 行也点明。
- `--min-severity` 大小写不敏感。

## v3.0.0 的配置面收紧

`lint_rules.json` 里若定义了 `P4` / `P5` / `C1`–`C6` 这些算法规则 Id，加载时会直接报错（此前静默接受，
会让同一个 Id 既有算法命中又有数据命中，报告出现重复 Id）。这批 Id 由程序内产出，数据文件不应用它们；
删掉冲突条目即可。

## 规则 Id 改为语义化标识（`--json` 契约 Version 2 → 3）

- `Hits[].Id` 与 `Notes[].Id` 由 `P1`/`S5`/`C1` 这类短码换成语义化 slug
  （`repeated-punctuation`、`parallel-contrast`、`paragraph-length-uniform` …），
  `Group` 由 `P`/`L`/`S` 换成 `punctuation` / `lexis` / `syntax`（统计项为 `statistics`）。
  人读报告同时打出中文标题，消费者不必自带码表。
- **旧报告、旧脚本、旧笔记里的码全部作废**，对照表见技能侧的
  `WriteText/subskills/HumanizerZh/references/rule-map.md`。
- **外部 `lint_rules.json` 按 Id 合并**：里面若仍按旧码写条目，升级后不会再覆盖内置规则，
  而会当成新规则追加。把条目的 `Id` 换成现行标识即可。
- `--only` 的取值同样改为现行标识，例如 `--only parallel-contrast,cliche-words`。
