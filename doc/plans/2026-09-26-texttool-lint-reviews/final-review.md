# 最终整分支评审 — texttool lint（abc56fb..fbf4d6a，34 个提交）

评审者：最终整分支评审（跨任务视角）
评审基准：HEAD = fbf4d6a，工作区干净（`git status --short` 空）
规格（权威）：`doc/specs/2026-09-26-texttool-lint-design.md`
验证方式：静态通读实现产物 + 6 次单条 CLI 实跑（退出码一律 `> log 2>&1; echo $?`，未接管道）。
未重跑测试套件（按派单要求）；220/220 与门禁以 ledger 中实施者与 controller 的独立复跑为准。

### Branch Verdict

[FAIL] issues that block —— 3 组阻塞项：2 组在代码（均为"规格承诺未落地"的对外契约缺口），1 组是文档一致性收口。
三组都是小改动（代码约 15 行 + 测试 2 条，文档约 12 行），改完即可视为 done，无需重做或回改历史。

### Cross-Task Coherence

**接口贯通（对得上）**

- `LintRule`/`LintRuleStore` → `AiToneLintService` → `LintReport.Filter` → `LintTextFormatter` / `LintReportSet.ToJson` → `Program.RunLint` 的字段与签名逐项对齐；规格 §R2 的报告契约（Version/Reports/File/Chars/Hits/Notes + 11 个命中字段、PascalCase）在 `LintReport.cs:7-71` 完整落地。
- **Id 命名空间贯通**：`AllRuleIds()`（`AiToneLintService.cs:35-41`）= 算法 7 条 + 数据 19 条，正是引擎实际产出的 Id 全集；`--only` 的校验集与产出集一致（`Program.cs:342-346`）。实测 `--only p7`（小写）被接受、`--only NOPE` 退 2 ✓
- **退出码三方一致**：规格 :222 ↔ `Program.cs:400` 的 help ↔ README:173-176（中英各一份）。实测三条路径：干净文本 0、有命中 1、`--only ""` 2 ✓
- **info 级与退出码**：README:182-183 与实测一致（info 命中默认退 1；`--min-severity warn` 退 0），Task 10 对 brief 错误说法的订正已落进最终产物（fbf4d6a）✓
- **P7 静默失效的订正已落地**：`default_lint_rules.json:26-29` 为 `Kind=regex`，实测 U+200B 命中（`--json` 输出 P7 命中，退 1）✓；配套不变量测试见 `LintRuleStoreTests.cs:104-117`
- 顺序契约：`Scan` = document → paragraph → 算法检出器（`AiToneLintService.cs:20-29`）与规格 §R2 一致 ✓

**规格承诺 vs 实现（两处不一致，均见 Issues）**

- 规格 :233「`--only` 同时作用于 hits 与 notes」— 未落地。实测 `--only C1` 打出的是 **C5** 的统计项，`--only S1` 仍带出 C5；`LintReport.cs:49` 把 `Notes` 原样透传，`Notes` 只按 Id 过滤这一条从未实现。
- 规格 :192「报告里转义为 `\u200b` 形式。**JSON 输出同理**」— 未落地。实测 `--json` 的 `Match`/`Snippet` 是**原字符**（od 实测字节 `342 200 213`），人读路径（`LintTextFormatter.Visible`）才转义。

**文档与行为（一处互相矛盾 + 一处锚点不实）**

- 两份结构树已经不一致：README:187-282（已更新，含 4 个新 Core 文件与 `default_lint_rules.json`）vs `doc/ArchitectureGuide.md:164-192`（未更新，Core 清单漏 5 项、CLI 行仍写 `merge/replace/vn/join/update`）。
- `doc/TECH-DEBT.md:118` 的"唯一权威"版本锚点命令实测输出**两行**：`，程序集版本自动从此继承。` + `2.5.0`（`Directory.Build.props:5` 的注释里也有一个 `<Version>`）。
- 其余锚点实测全对：`[Fact]/[Theory]` = 182、`MainForm.cs` = 207 行、`Services/` = 4 文件、ADR = 9、tag = 10（最新 v2.4.4，README:300 正确）。

**与仓外技能侧的互相矛盾（各一处，均为"技能记实际、规格记应有"）**

- `SKILL.md:95`/`:172`「`--only` 只过滤 `Hits`，`Notes` 照常输出」↔ 规格 :233。技能侧写的是实测真相。
- `SKILL.md:183`「`--json` 的 `Match` 字段是原字符」↔ 规格 :192。同样是实测真相。
  两处的裁决都是"改代码对齐规格"（理由见 Issues），因此这两处技能文字需要同步改写（各 1-2 行）。

### Deferred-Minor Triage

分诊规则（写在前面，便于复核）：**必须修 = 对外可观察的陈述或行为与本分支自己的契约相矛盾，且最小改法不超过十来行、不引入行为风险**；其余一律"可留作已知遗留"，理由逐条给出。分诊结论 ≠ 严重度（下文 Issues 里同一项可能标 Minor）。

**必须收官前修（4 条 ledger 项 + 2 组本次新发现）**

| 项 | 条目 | 最小改法 |
|---|---|---|
| Task 10 M-3 | 规格 :233 与 `LintReport.Filter` 不一致；`--only C1` 会打出 C5 的统计项 | 改码：`LintReport.cs:49` 改为按 Id 过滤 Notes（`only is null ? Notes : Notes.Where(n => only.Contains(n.Id)).ToList()`），补 1 条测试；计划 Task 4 的代码块同步 |
| Task 10 M-1 | README 功能表表头是「Tab」「页签」，而 lint 是纯 CLI（新行写 `**CLI · lint**`） | 表头改 `Tab / CLI`、`页签 / CLI`（README:19、:310） |
| Task 10 M-2 | 「These rules are all info level」/「这些规则一律 info 级」把没有 severity 概念的 C 组也扫了进去 | 主语写明 `P4`/`P5`（README:182、:464） |
| Task 10 M-4 | TECH-DEBT:118 版本锚点匹配到文件自身注释，实测输出两行 | 正则加数字限定：`grep -oP '(?<=<Version>)[0-9.]+(?=<)'` |
| 本次新发现 | 规格 :192 的 JSON 转义未落地（见 Issues-2） | 见 Issues-2 的改法 |
| 本次新发现 | `doc/ArchitectureGuide.md` §8 结构树与 README 互相矛盾（漏 4 个 Core 文件 + `default_lint_rules.json` + CLI 的 lint） | §8 补 5 行（ArchitectureGuide.md:181-190 区段）、:192 的括号里加 `lint` |

**可留作已知遗留（逐条）**

| 项 | 条目 | 裁决 | 理由 |
|---|---|---|---|
| T1-1 | 测试钉常量而非调用点 | 留 | 调用点现已引用常量（`LintRule.cs:59`）；残余风险是"有人把查询串改回字面量"，改动本身显眼 |
| T1-2 | d3f0352 用了仓史仅一例的 `refactor:` 前缀 | 留 | 提交历史不改是本机既有纪律；前缀非强制 |
| T1-3 | adf9270 提交信息"不再掩盖 JSON 读取失败"言过其实 | 留 | 同上，历史不改 |
| T1-4 | 两处 store 的常量与注释文字重复 | 留 | 3 行重复；抽公共常量反而把两个不相干的 store 绑在一起 |
| T1-5 | 新增两个测试未 dispose 取到的 stream | 留 | 测试进程短命；不影响结果 |
| T1-6 | `GetDefaultSchemes()` 每次重读资源、无缓存 | 留 | GUI 启动一次性、CLI 一次性；lint 走的是 `LintRuleStore`，与此无关 |
| T2-1 | Merge 就地替换后不更新索引（external 同 Id 两次 → `ArgumentOutOfRangeException`） | 留 | 生产路径被 `Validate(external)` 先挡（`LintRule.cs:50`）；属"未来新调用方"陷阱，建议随 TECH-DEBT 登记 |
| T2-2 | `Validate` 仍信任列表元素非 null（`[null]` → NRE） | 留 | 同一 JSON 下 CLI 仍退 2（`Program.cs:368-372` 兜住），只是错误信息难看，不是错误判定 |
| T2-3 | Merge 拷贝不对称（内置克隆、外部按引用插入） | 留 | 当前无调用方就地改外部对象 |
| T2-4 | 新增的 `Validate(builtIn)` 无测试执行到 | 留 | 内建数据的合法性与顺序由 `GetDefaultRules_数据规则19条且顺序完整` 间接覆盖 |
| T2-5 | 计划文档 internal/public 不一致 | 留 | 已随 53034f0 消解，仅存记录 |
| T3-1 | 未断言四个非默认字段真的绑定（L2.MinCount / S2.Scope+MinCount / S3.Scope+TailChars / L7.Hints） | 留（推荐） | 当前数据已由 controller 逐项复核；风险是**将来**手改 JSON 拼错键名被静默回落（`Validate` 不拦未知键）。这是本清单里最值得补的一条：4 行断言即可；更彻底的做法是只对 LintRule 的读取开 `JsonUnmappedMemberHandling.Disallow`，属设计变更，建议另开 |
| T3-2 | P3 的 Detail 措辞比字符类窄（也会命中 `,。`、`,Ａ`） | 留 | Detail 是人类提示；命中本身没有误报。想改就把措辞写成"半角标点与中文/全角字符相邻" |
| T3-3 | 跨规则重叠：L5 完全包含 L6 的「您说得完全正确」，P1 包含 P2 的 `。{3,}` | 留 | 实测同跨度双报（`。。。。` → P1+P2；`您说得完全正确` → L5+L6），但两条的 Detail 与 SuggestScheme 不同：P1 指向`重复矛盾标点整理`、P2 指向`去AI味标点归一`。**报告层去重是错的**——必然丢掉一条建议，且会让 `--only L6` 因"L5 wins"而报不出东西，破坏按规则选择的契约。若要收敛，只在数据层删 `P2` 的 `。{3,}`（被 P1 完全覆盖），代价是丢掉"这是省略号写法"的解释，见 Open Questions |
| T3-4 | 不变量测试在 literal 规则归零时空过；`Load` 的 19 条计数依赖程序目录无 `lint_rules.json` | 留 | 均为测试强度/环境依赖。实测 exe 目录确有 `replace_schemes.json`（技能装方案留下），同类文件若被放进测试输出目录会弄红该用例，属可接受的脆弱 |
| T4-1 | 「隐形字符不抛异常」测试是空转的 | 留 | 已由 Snippet 代理对两组用例实质取代 |
| T4-2 | 未钉"Filter 不改接收者"，未断言 File/Chars 传递 | 留 | 本次修 M-3 时会顺带补 Notes 断言；其余仍可从简 |
| T4-3 | Filter 结果的 Notes/命中元素与原报告共享引用 | 留 | 无调用方就地改写；修 M-3 后 Notes 在 `--only` 路径上会变成新列表 |
| T4-4 | `UnsafeRelaxedJsonEscaping` 连带不转义 `< > & ' +` | 留 | 仓内没有把该 JSON 嵌进 HTML/Markdown 的路径；真要做就换 `JavaScriptEncoder.Create(UnicodeRanges.All)`，但那是**另一个**问题（中文不转义与 HTML 安全本就要取舍） |
| T4-5 | `LintReport` 是唯一没有 XML 注释的类型 | 留 | 1 行可补，纯风格 |
| T5-1 | `Position` 每次命中从头重扫（O(长度×命中数)） | 留 | 章节级文件可接受，且是计划逐字指定的实现 |
| T5-2 | 跨行命中时 Snippet 不含 Match 原文 | 留 | 只有 L4 的 `\s` 能跨行；Snippet 是展示片段，Line/Col 仍准 |
| T6-1 | 实现注释措辞与 brief 漂移（「任务名」vs「规则名」） | 留 | 纯文字 |
| T6-2 | `Paragraphs` 的行首空白与 CRLF 分支零测试覆盖 | 留 | 评审已手推验算；CRLF 下 `TrimEnd('\r')` 与 `Position` 只认 `\n` 的组合是正确的 |
| T6-3 | 「非空行即一段」把 Markdown 软换行拆开，S2/S3 少报 | 留 | 计划自陈的简化，技能侧已知边界也写明；改口径是产品决策 |
| T6-4 | `windowStart` 可提到 Pattern 循环外 | 留 | 微不足道 |
| T7-1 | `AlgorithmRuleIds`/`AllRuleIds` 缺中文 XML 注释 | 留 | 仓内其它 public 成员都有，属风格一致性；1-2 行可补 |
| T7-2 | `AlgorithmRuleIds` 把底层数组当只读表暴露 | 留 | 需要先转成 `string[]` 才能改，实际无人这么做 |
| T7-3 | 正则构造位置不一致（P5 每次 3 个、C2 每次 1 个） | 留 | 单文件扫描可忽略；若要优化，`Scan` 级缓存即可 |
| T7-4 | `Scan_统计项不进Hits` 近似不可失败 | 留 | 测试强度；C 组结构与退出码已由 CLI 冒烟覆盖 |
| T8-1 | `Format` 未断言 Detail 的渲染分支 | 留 | 与 Hint 同一行代码，Hint 已被断言 |
| T8-2 | 5 条补覆盖只钉文案不钉数值（如 C4 的密度） | 留 | 阈值逻辑另有针对性用例 |
| T8-3 | `Format` 输出末尾无换行 | 留 | 已由 `Program.cs:359` 的 `+ Environment.NewLine` 补上 |
| T8-4 | `Visible` 的转义集不含 emoji 等 GBK 编不出的可见符号 | 留 | 已由 `Console.OutputEncoding = UTF8`（`Program.cs:310`）处理 |
| T9-1 | `--min-severity` 大小写敏感（与 `--only` 不敏感不对称） | 留 | 是**响亮**退 2（`Program.cs:330`），不是静默丢弃；纯不对称的小瑕疵 |
| T9-2 | 类头 XML 注释的用法清单缺 lint | 留（推荐） | `Program.cs:11-17` 是文件自述 API 清单，新增子命令没进去；1 行可补 |
| T9-3 | `--only` 路径重复 `Load` | 留 | 一次进程两次 Load，可忽略 |
| T9-4 | 单文件输出尾部多空行 | 留 | `Format` 不带尾换行 + `WriteLine` 追加，纯外观 |
| T9-5 | `OutputEncoding` 的 guard 只捕 `IOException` | 留 | 罕见宿主上会整体退 2，方向是响亮失败 |
| T9-6 | 报告精度小疵 | 留 | ledger 未细化，无实例可判 |
| T10-1..4 | 见上表（M-1/M-2/M-4 修，M-3 修） | — | — |
| T11-a | `SKILL.md:97` info 示例漏 L1 | 留 | 仓外、示例不全，不影响行为 |
| T11-b | `:34` 未说明 U+200C/U+200D 方案不修但 P7 会报 | 留 | 该行**已**写明（Task 11 修复轮已补），ledger 该条已消解 |
| T11-c | `:142`/`:180` 缺交叉引用、`:47` 前向引用 | 留 | 仓外体例问题 |
| T11-d | `:181` 的「只」过宽、末句复核粒度不足 | 留 | 仓外措辞 |
| T11-e | `SKILL.md:95`/`:172` 与规格 :233 矛盾 | **修** | 随 M-3 的代码改动同步（2 行），否则技能描述的是旧行为 |
| T11-f | `SKILL.md:183` 与规格 :192 矛盾 | **修** | 随 Issues-2 的裁决同步（1 行） |
| T11-g | **C2（中）**：`引号括号统一` 把全角括号转半角，跑完 `P3` 反升；而 `P4`/`P5` 的 `SuggestScheme` 正指向它 | 留（推荐加一句） | 这个指针**本身没错**（该方案确实管引号/括号），副作用属于方案数据，而 `default_schemes.json:16-38` 不在本分支 diff 内（未改）。实测机制：`（`→`(` 之后，`(` 紧邻中文正好命中 P3 的两条模式，实测 `甲（乙）丙,丁。` 跑方案后 P3 由 2 条升到 6 条。技能侧「已知边界」「红旗信号」已写全这条并给出操作纪律（每跑一条方案就重跑一次 `lint`），而仓内没有任何把 lint 的 SuggestScheme 与 replace 串起来的自动流程，README 也没承诺"跑方案即修好"——所以仓内不存在假陈述。可留；若要补，只需在 README「启发式规则的边界」段加一句（成本 1 行，见 Issues-Minor） |
| T11-h | **C3（低）**：旧脚本的弱序号统计项未迁移 | 留 | 技能映射表如实标注「未迁移」、未编造对应；要补就另开 `C6`，属新增能力而非本分支缺口 |
| T11-i | **C4（低）**：`P3` 的命中按「先全部 NEXT、再全部 PREV」分组，同一行内 `Col` 非单调 | 留 | 与旧脚本一致、非本分支回归；人读渲染已按 (行,列) 排序（`LintTextFormatter.cs:27`）；规格未对 `Hits` 顺序作承诺。可选收口：`Scan` 结束前对 Hits 按 (Line,Col) 排一次，两条输出就都单调了 |
| T11-j | 连带发现：方案 `去AI味标点归一` 的 Description 自称「省略号统一」，实测重复标点折叠先行、单遍跑不出 `……` | 留 | 被描述的对象是**技能自带**方案（`references/humanize-scheme.json:3-5`，仓外数据，装到 exe 目录），仓内的 `default_lint_rules.json:7-10` 只承诺 "P2 建议跑哪条"，不承诺跑完的结果；技能 `SKILL.md:36`/`:181` 已按实测写明两条省略号路径的区别。建议技能侧把 Description 改成"重复标点折叠、点号串转省略号、隐形字符清理…"，以免自述继续失真（仓外 1 行） |

**parked 项复核**：Task 6 的 Co-Authored-By trailer 不回改历史 —— 赞同（本机既有"历史提交不改"，为署名元数据 rebase 7 个已提交并各自重跑全量测试，代价与收益不成比例；向前收口已由 40b19ac 完成）。Task 9 的 `--only ""` —— 复验通过：`Program.cs:338-339` 的守卫 + `RunLint` 自身 catch 退 2。

### Open Questions for the Human

1. **Issues-2 的方向**：`--json` 的 `Match`/`Snippet` 该带转义文本（规格 :192 的写法，我推荐）还是保留原字符（技能 `SKILL.md:183` 已如实记录的现状）？注意规格自身有张力：§R2 :192 要求转义，§5 的测试清单 :280 只要求"不抛异常"。我按"设计要求 > 测试最低线"裁决为转义。
2. **P2 的 `。{3,}` 是否从数据里删掉**：它被 P1 的 `([。，、？！；：～])\1+` 完全覆盖，是 T3-3 那对重复命中的根源。删掉后 `。。。` 只剩 P1 的"重复标点"解释（丢掉"这是省略号写法（标准为 ……）"这一层提示）；我倾向保留双报。
3. **是否在 README 的"启发式规则的边界"段加一句 P4/P5 的副作用**（跑 `引号括号统一` 会把 `（括号）` 转半角、`P3` 命中反而上升，实测 2→6）。技能侧"已知边界""红旗信号"已写全，仓内用户看不到这句；我判"可留"，但加一句成本极低。
4. **谁来做技能侧 3 行同步**：M-3 与 Issues-2 一旦改码，`SKILL.md:95`/`:172`/`:183` 三处失真，而技能是仓外产物、刚经独立评审判 clean，需要指派执行者并走一次 scoped re-review。

### Issues

#### Critical

无。

#### Important

**I-1（= Task 10 M-3）`--only` 不过滤统计项，导致"只跑指定规则"对 notes 不成立。**
证据（实跑）：
- `lint ord.txt --only S1` → 除 `[OK]` 外仍打出 `- C5 序数词骨架…`；`--json` 的 `Notes` 数组含 `"Id": "C5"`。
- `lint ord.txt --only C1` → 打出的是 **C5** 的统计项，退 0。用户显式选中的是 C1，报出来的却是 C5。
位置：`TextTool.Core/LintReport.cs:37`（注释"过滤命中（Notes 不受影响）"）与 `:49`（`Notes = Notes`）；调用点 `TextTool.Cli/Program.cs:355`。
裁决：**改码对齐规格 :233**。理由三条：(a) 规格是权威；(b) `--only` 的语义是"规则选择"，C1–C5 是货真价实的规则 Id（`AllRuleIds()` 接受它们，见 `AiToneLintService.cs:32-41`）——当前实现对 C 组 Id"接受但无任何效果"，`--only C1` 印 C5 尤其站不住；(c) `--min-severity` 走的是**严重度**轴，Notes 没有 severity 所以不受它影响，两条 flag 各管一轴，语义自洽。
最小改法：`LintReport.cs:49` →

```csharp
            // --only 是"只跑指定规则"（C 组也是 Id，--only C1 就只报 C1 统计项）；
            // --min-severity 是严重度轴，Notes 无 severity，故不受它影响（规格 R3）
            Notes = only is null ? Notes : Notes.Where(n => only.Contains(n.Id)).ToList(),
```

并同步：`LintReport.cs:37` 的注释、`doc/plans/2026-09-26-texttool-lint.md:700-712` 的代码块、`LintReportTests.cs` 加一条（`Filter(new[]{"S1"}, null)` → `Hits` 单条且 `Notes` 空；`Filter(new[]{"c1"}, null)` → 保留 C1 统计项，顺带钉住大小写不敏感）。仓外的 `SKILL.md:95`/`:172` 必须同批改。

**I-2（本次新发现）规格 :192 要求的"JSON 也转义隐形字符"未落地。**
证据（实跑 + 字节级）：文本 `甲乙<U+200B>丙。` → `lint --json` 的 `Match` 字段是三个原始字节 `342 200 213`（U+200B 的 UTF-8），不是 `\u200b`；`Snippet` 同样内嵌原字符。
影响：`--json` 是本特性的招牌产物、也是技能的工作通道。消费方（Claude / 技能）看到的是 `"Match": "<看不见的字符>"`，等于空串；只能退到 `Detail` 反推。人读路径已正确转义，两条输出对同一件事给出了不同可读性。
位置：`TextTool.Core/LintReport.cs:64-70`（`Encoder = UnsafeRelaxedJsonEscaping`）——该 encoder **不会**转义 U+200B 这类合法 JSON 字符；`LintTextFormatter.cs:11-14` 的转义集是私有的，JSON 路径用不上。
裁决：**改码对齐规格 :192**。最小改法（约 8 行，无行为风险；JSON 解析方会把 `\u200b` 解回原字符，只有在"看 JSON 文本"时才是转义，正是本意）：

```csharp
// LintTextFormatter.cs：Escaped 改 internal static；新增
    /// <summary>只替换隐形字符集本身（不碰 JSON 自身的换行等控制字符）。</summary>
    internal static string EscapeInvisible(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char c in text) sb.Append(Escaped.Contains(c) ? $"\\u{(int)c:x4}" : c);
        return sb.ToString();
    }

// LintReport.cs：ToJson 改为先序列化再整体转义（u200b 等字符只可能出现在字符串字面量里，替换结构安全）
    public string ToJson() => LintTextFormatter.EscapeInvisible(JsonSerializer.Serialize(this, Options));
```

并把 `LintReportTests.ToJson_命中隐形字符不抛异常` 的断言从"含 P7"升级为 `Assert.Contains("\\u200b", json)`。仓外 `SKILL.md:183` 同批改写。

#### Minor

- **M-1 README 功能表表头不诚实**：`README.md:19`（`| Tab | Description |`）与 `:310`（`| 页签 | 说明 |`）之下新增的是纯 CLI 行 `**CLI · lint**`（`:26`/`:317`）。行本身诚实（没伪造页签），表头没跟上。改表头为 `Tab / CLI`、`页签 / CLI`。
- **M-2 README 的 info 级主语过宽**：`README.md:182`「The `C` group (`C1`-`C5`) is statistical observation and never affects the exit code. These rules are all `info` level…」与 `:463-464` 中文同构。C 组没有 severity 字段，"These rules"读起来把 C 组也算了进去。把主语写成 `P4`/`P5`。
- **M-3 `doc/TECH-DEBT.md:118` 的锚点命令输出两行**（实测：`，程序集版本自动从此继承。` + `2.5.0`），因为 `Directory.Build.props:5` 的注释里也有 `<Version>`。锚点号称"唯一权威"，写成 `grep -oP '(?<=<Version>)[0-9.]+(?=<)'` 即可。
- **M-4 `doc/ArchitectureGuide.md` §8 结构树陈旧**：`:181-190` 的 Core 清单缺 `LintRule.cs`/`LintReport.cs`/`AiToneLintService.cs`/`LintTextFormatter.cs` 与 `default_lint_rules.json`；`:192` 写 `texttool.exe entry (merge/replace/vn/join/update)`，缺 `lint`。与 README 的树直接打架。改法是机械补 5 行。
- **M-5 `SuggestScheme` 不进人读输出**：`LintTextFormatter.cs:29-31` 只渲染 Hint 与 Detail，从不渲染 `SuggestScheme`；规格 §R1 :114 说它"报告里直接告诉用户该跑哪条"。技能走 `--json` 不受影响，但直接读人读报告的 CLI 用户学不到该跑哪条方案。可留（要么在渲染里加 `<- 方案：X`，要么在 README 说明方案只在 JSON 里）。
- **M-6 跨规则同跨度双报**（T3-3）：`。。。。` → P1+P2（同 L1C5、len 4，但建议方案不同）；`您说得完全正确` → L5+L6（同 L2C1、len 7）。实测与 ledger 一致。判"留"，见 Triage 理由。
- **M-7 外部规则可撞算法 Id**：`lint_rules.json` 里若定义 `P4`/`P5`/`C1`–`C5`，`Merge` 会把它当新 Id 追加、`Validate` 不报错，于是同一 Id 既有代码内算法命中又有数据命中，报告出现重复 Id。无出货文件触发；若要收口，`LintRuleStore.Load()` 里对 external 加一条 3 行守卫（Id ∈ `AlgorithmRuleIds` 即抛 `ArgumentException`），与"加载即校验"的既有立场一致。
- **M-8 `--json` 到 stdout 的行尾是 CRLF**：实测 26 CR / 26 LF（.NET 8 的缩进 JSON 写出器用 CRLF）。解析方不受影响，但本工作区有"文件一律 LF"的约定，技能若把 `--json` 重定向成文件会带进 CRLF。可留，知会即可。
- **M-9 `Hits` 在 raw JSON 里不保证有序**（T11-i）：同一规则的多条 Pattern 各扫一遍，`P3` 的命中会是「先全部 NEXT、再全部 PREV」，同一行内 `Col` 非单调。人读渲染已排序（`LintTextFormatter.cs:27`）。规格未承诺顺序，故可留；若想收口，在 `Scan` 返回前按 (Line, Col) 排一次即可（两种输出同时受益）。
- **M-10 `P4`/`P5` 的指针正确但下游会互相打架**（T11-g）：`引号括号统一` 转半角后制造新的 `P3` 命中（实测 2→6）。仓内无假陈述、技能已写明纪律，故可留；建议 README 的"启发式规则的边界"段补一句（1 行），顺带把 `SuggestScheme` 只在 JSON 里这一点也说明（M-5）。

### Strengths

- **数据/算法分层是真的，不是口号**：19 条数据规则 + 7 条算法规则共用一套 Id 与报告字段，`--only` 跨两层都能用（`AiToneLintService.cs:35-41` 是两层的唯一汇合点）；改词不需要动代码，这一点的实现代价被压到了最低。
- **"宁可响亮失败"被落成了测试，而不只是注释**：`Validate` 拦 null/空串 Patterns（9fb9314）、literal 规则不得含反斜杠的不变量测试（`LintRuleStoreTests.cs:104`）、未知 `--only` 退 2、`--only ""` 退 2。本分支发现的**三条假阴性路径都是静默型**，三条都被补上了能真正失败的测试。
- **P7 的处置是教科书式的**：发现"literal + JSON 转义 = 永不命中"后，没有只改数据了事，而是改 Kind + 立不变量 + 补端到端冒烟测试（`AiToneLintServiceTests.cs:120-134` 的注释把这条路径的由来写清楚了）。
- **规格承诺的边角几乎都落到了文档**：退出码与兄弟子命令不同这件事，在规格、CLI help、README 中英四处一致；"启发式边界"段落主动写出了 P4 的 `'` 误报与 C 组不进退出码，且 `README:182-183` 的措辞与实测一致。
- **过程诚实**：实施者主动上报自己测试集的弱点（Task 7 的"5 条规则零正向覆盖"）、主动上报与 brief 的有意偏差（Task 8 补 `git add`）；controller 发现"格式化退出码=0"是管道假象后，回头复验了前三任务而不是只修当下。ledger 里没有把问题写成"已妥善处理"。
- **仓内数据零污染**：技能侧装方案、实跑替换都没把 `replace_schemes.json` 写回仓库（我核对过 diff，`default_schemes.json` 未在本分支变更）。

### Assessment

**Branch quality:** 高 —— 34 个提交、一个自洽的新能力，分层清晰、测试能真失败、文档诚实；但对外契约有两处"规格写了、实现没做"（`--only` 对 notes、JSON 隐形字符转义），两处都在机器消费面上，都属必须收官前收口的小改动，外加一次文档一致性扫尾。

**Reasoning:** 逐任务评审按各自的 plan/brief 判，永远看不到"plan 与 spec 的分叉"（M-3 的分叉正是在写 plan 时引入的，此后每一级都拿 plan 当基准），也看不到跨仓的 `SKILL.md` 与规格互相矛盾；这两类只能在本层发现，且都不能算"可留的已知遗留"——它们是本分支自己承诺的对外行为。修完这 3 组（代码约 15 行 + 文档约 12 行），本分支可判 done。
