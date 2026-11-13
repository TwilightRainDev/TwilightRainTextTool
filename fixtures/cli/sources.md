# 命令行的对照样本

`cases.tsv` 是用例表：用例名、类型、参数。`make_goldens.sh` 照它跑参考实现出基准，
`tests/compare_cli.sh` 照它跑原生程序比对。加用例只改那一张表。

## 基准从哪来

两个构建各管一半，与人读报告层那一批样本同一套口径：

- 人读报告、帮助文本、stderr、plain 用例的退出码，用 4.0 的构建（用词以 4.0 为准）；
- `--json` 输出与 lint 用例第二次运行的退出码，用 3.1 的构建（提交 `207dd38`，JSON 契约不动）。

4.0 的构建由脚本开头那三行命令从标签 `v4.0.0-draft` 导到 `/tmp/tt40` 再编，参考仓库不动。
不要手改 `.expected.*`：手改等于把基准改成自己的输出，重跑脚本即可。

只有一个例外：`merge_help` 那一份由原生程序自己出。原生版有三个参考实现没有的开关
（`--no-merge`、`--no-merge-chars`、`--apply-replace`），帮助文本因此不同，参考实现
生成不出这一份。改了 merge 的帮助就重出：

```bash
build-linux/texttool merge --help > merge_help.expected.txt
```

## 基准文件

用例名就是前缀。`<用例>.expected.txt` 是 stdout，`.expected.err` 是 stderr，
`.expected.exit` 是退出码（lint 用例两行，plain 用例一行），`.expected.json` 是 `--json` 的输出。

## 稿件来源

| 稿件 | 来源 |
| --- | --- |
| `real_doc.md` | 工作区 `Docs/ChangeLog/README.md` 原样复制 |
| `short.txt` | 手写短样本：警告级与提示级混排、带建议的命中、带方案的命中、两条规则咬同一处 |
| `long.txt` | 从工作区各处文档取不重复段落拼成，13k 字、77 处命中、17 条规则 |
| `clean.txt` | 手写，零命中：退出码 0 那条路 |
| `gbk.txt` | `short.txt` 转 GBK：`iconv -f UTF-8 -t GBK short.txt > gbk.txt` |

`real_doc.md` 只做复制，要更新就从原文件重新复制再跑脚本。

稿件挑的是「算法规则一个都不咬」的文本：参考实现里有 15 条由算法算的规则（见
`src/engine/lint_runner.h`），引擎这一层不跑它们，命中和统计项都产不出来。
拿 `Docs/Project/TextTool/README.md` 试一下就看得出来：参考 40 处命中、2 条统计，
原生 39 处、0 条，差的正是 `mixed-bracket-width` 与两条统计。算法层接上之后，
把这类稿件加进 `cases.tsv` 即可。

## 怎么跑

```sh
cmake --build build-linux -j
bash tests/compare_cli.sh        # 全一致退 0，有分歧退 1 并打印第一处 diff
```

程序位置由 `TEXTTOOL_BIN` 覆盖，默认 `build-linux/texttool`。脚本在 `fixtures/cli` 里跑，
稿件名不带目录：报告里 `File` 字段就是它，基准换台机器也不会失效。

规则表在运行时找：环境变量 `TEXTTOOL_RULES_JSON` 优先，其次程序所在目录，
再从那里逐层往上看 `assets/default_lint_rules.json`；找不到就退 2 报错。
