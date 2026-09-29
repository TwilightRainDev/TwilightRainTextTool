# TECH-DEBT — TextTool

> 未落地优化项与已知遗留的**单一权威清单**。
> 已落地项见 `doc/archive/tech-debt-landed.md`，不再跟踪。
> 每条均已对照当前代码核实；核对不实的标注「待核」。

---

## 1. 未落地项（核实确认）

§2 的 16 条列入 v3.0 范围，逐条处置见 `doc/plans/2026-09-29-texttool-v3.md`；
条目落地后移入 `doc/archive/tech-debt-landed.md`。

---

## 2. 已知遗留（`texttool lint` 特性）

> 来源：`texttool lint` 特性的最终整分支评审。明细在
> `doc/archive/plans/2026-09-26-texttool-lint-reviews/`。
> 全部列入 v3.0 范围（`doc/plans/2026-09-29-texttool-v3.md`）：会改可观察输出
> （输出字节、退出码、JSON 契约）的按破坏性变更处理，README 契约节与发布说明同步写明。

**性能**

- `Position` 每次命中从头重扫；正则构造位置不一致（`P5` 每次 3 个、`C2` 每次 1 个）——`Scan` 级缓存即可。
- `GetDefaultSchemes()` 每次重读资源、无缓存；`--only` 路径重复 `Load` 一次；`windowStart` 可提到 Pattern 循环外。

**文档措辞**

- `P3` 的 Detail 措辞比字符类窄（也会命中 `,。`、`,Ａ`）；报告精度小疵（ledger 未细化）。
- 计划 internal/public 不一致随 53034f0 消解，仅存记录（无待改对象）。
- `Format` 输出末尾无换行（由 `Program.cs` 追加 `Environment.NewLine`）；单文件输出尾部多一个空行。
- `Visible` 的转义集不含 emoji 等 GBK 编不出的可见符号（已由 `Console.OutputEncoding = UTF8` 处理）。
- 历史提交 `d3f0352` 的 `refactor:` 前缀、`adf9270` 提交信息言过其实（提交历史不改，仅登记）。

**契约边缘**

- `Merge` 就地替换后不更新索引（external 同 Id 两次会 `ArgumentOutOfRangeException`），生产路径先被 `Validate(external)` 挡住；`Validate` 仍信任列表元素非 null（`[null]` 走 NRE，CLI 仍退 2）。
- `Merge` 拷贝不对称（内置克隆、外部按引用插入）——无调用方就地改外部对象。
- 跨规则同跨度双报（`。。。。` → P1+P2，`您说得完全正确` → L5+L6）：报告层去重会丢建议且破坏 `--only` 契约；若要收敛只在数据层删 `P2` 的 `。{3,}`。
- `UnsafeRelaxedJsonEscaping` 连带不转义 `< > & ' +`；`--json` 到 stdout 的行尾是 CRLF；`Hits` 在 raw JSON 里不保证有序（人读渲染已排序）。
- `--min-severity` 大小写敏感（与 `--only` 不对称，但是响亮退 2）；`OutputEncoding` 的 guard 只捕 `IOException`。
- `SuggestScheme` 不进人读输出（只在 JSON 里）；外部 `lint_rules.json` 可撞算法 Id（无出货文件触发）。
- `Paragraphs` 把 Markdown 软换行拆开（S2/S3 少报，计划自陈的简化）；跨行命中时 Snippet 不含 Match 原文。

**外部数据**

- 技能侧 `去AI味标点归一` 的 Description 与实测不符（重复标点折叠先行，单遍跑不出 `……`）；`引号括号统一` 把全角括号转半角后 `P3` 命中反升（实测 2→6），技能侧已写明操作纪律。
- 旧脚本的弱序号统计项未迁移（技能映射表如实标注「未迁移」，补它属新增 `C6` 能力）；技能 `SKILL.md:97` 的 info 示例漏 L1。

---

## 3. 在途规格（不排队）

暂无。

`2026-08-03-texttool-2.4.5-design.md` 的四项全部处置完毕，设计文档收录在 `doc/archive/specs/`：

| 项 | 处置 |
|---|---|
| B1 `scripts/publish.ps1` 发布自动化 | 落地于 v2.6.1 的 `scripts/release.ps1` |
| A1 下载超时分级 | 随 v2.6.2 发版 |
| A2 替换脚本 wait-loop 健壮化 | 随 v2.6.2 发版；实际根因与规格假设不同，见该文档 §7 |
| D7 私钥加密 | **不做**（用户决定），条目删除，不再跟踪 |

**规格漂移（登记）**：该规格标题写的是「2.4.5 发版收口设计」，A1/A2 本被定为 2.4.5 的客户端变更，
实际跨过 2.4.5 / 2.5.0 / 2.6.0 / 2.6.1 四个版本，到 v2.6.2 才发到用户端。标题里的版本号与现状
脱节，作为历史记录保留、不再重命名。

---

## 4. 验证锚点

```bash
# 当前版本号（唯一权威）
grep -oP '(?<=<Version>)[0-9.]+(?=<)' Directory.Build.props  # 期望 2.6.2

# 测试用例数（[Fact] + [Theory] 属性条数，非运行条数）
grep -roE '\[(Fact|Theory)' TextTool.Tests --include=*.cs | wc -l   # 期望 210

# ADR 份数
ls doc/adr/ | wc -l                                          # 期望 9

# 发布脚本
test -f scripts/publish.ps1                                  # 期望存在
test -f scripts/release.ps1                                  # 期望存在

# 构建 + 测试
dotnet build TextTool.sln -c Release
dotnet test TextTool.sln -c Release
```

---

行尾约定：本仓库所有文本文件为 LF（`.gitattributes` 强制），文档不得含 CRLF。
