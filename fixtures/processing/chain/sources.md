# 多步链的对照样本

`cases.tsv` 是用例表，`make_goldens.sh` 照它跑命令行出基准，`tests/compare_chain.sh` 照它跑链比对。
加用例只改那一张表。

## 基准从哪来

**参考实现没有多步链，只有单步。** 这几份基准是原生命令行自己按顺序跑两步得到的，不来自第二份实现：
命令行跑一步、产物换个名字、再跑下一步，最后一步的产物存成基准。比得上就是说链没有改动正文，
也没有丢编码与末尾换行这些东西。

单步本身由 `tests/compare_processing.sh` 拿同一个 `merge`、`replace`、`vn` 与参考实现逐字节守着
（参考实现那一侧的命令行基准就是它那一套），那一层对了，串起来的两步才谈得上对。

重新出基准，跑完就重跑对照：

```sh
bash fixtures/processing/chain/make_goldens.sh
```

不要手改 `expected/` 下的文件：手改等于把基准改成自己的输出。

## 人工重出其中一条

以「接回 + 换标点」为例，照下面几条敲一遍，得到的 `out.txt` 就是那份基准：

```sh
export TEXTTOOL_REPLACE_RULES_JSON=$PWD/fixtures/processing/chain/replace_rules.json
work=$(mktemp -d) && cd "$work"
cp /path/to/TextTool.Native/fixtures/processing/chain/inputs/merge_replace.txt in.txt
/path/to/TextTool.Native/build-linux/texttool merge in.txt      # 写 in_Processed.txt
mv in_Processed.txt s1.txt
/path/to/TextTool.Native/build-linux/texttool replace s1.txt    # 写 s1_Processed.txt
mv s1_Processed.txt out.txt
```

其余两条只换步骤与稿件：

| 用例 | 稿件 | 第一步 | 第二步 |
| --- | --- | --- | --- |
| `merge_replace` | `inputs/merge_replace.txt` | `merge` | `replace` |
| `replace_vn` | `inputs/replace_vn.txt` | `replace` | `vn` |
| `merge_lint` | `inputs/merge_lint.txt` | `merge` | 拿产物跑 `lint out.txt --json` |

`merge_lint` 的第二步是检查，它不进链（只读不写）。这一条另存三份：产物是
`<用例名>.expected.txt`，`lint --json` 的标准输出是 `.expected.json`，退出码是 `.expected.exit`。
基准里 `File` 字段是 `out.txt`，所以跑的时候产物得叫这个名字、还得在跑目录里跑。

## 稿件与规则

| 文件 | 内容 |
| --- | --- |
| `inputs/merge_replace.txt` | 硬换行截断的散文、半角标点：接回合行，换标点改标点，两步都看得见 |
| `inputs/replace_vn.txt` | 台词与旁白混排的剧本稿、半角标点：换标点改标点，排版分段落 |
| `inputs/merge_lint.txt` | 硬换行截断、带套话词与段内序数词的段落：接回之后行号变了，检查报告跟着变 |
| `replace_rules.json` | 换标点那一步的规则。`...` 那一条排在 `.` 前面：规则按次序套，`...` 先换掉才不会被拆成三个句点 |

## 怎么跑

```sh
cmake --build build-linux -j
bash tests/compare_chain.sh      # 全一致退 0，有分歧退 1 并打印第一处 diff
```

链还没挂到命令行与界面上，跑它要 `build-linux/chain_runner`（源码在 `tests/chain_runner.cpp`，
只给这份脚本用）。步骤名与命令行子命令同名同参数，`replace` 的规则按与命令行同一条查找路径读，
`TEXTTOOL_REPLACE_RULES_JSON` 指到哪份就用哪份。

程序位置由 `TEXTTOOL_BIN` 覆盖，驱动由 `CHAIN_RUNNER` 覆盖，默认都在 `build-linux/` 下。
