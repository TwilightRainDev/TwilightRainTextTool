# TECH-DEBT — TextTool

> 未落地优化项与已知遗留的**单一权威清单**。
> 已落地项见 `doc/archive/tech-debt-landed.md`，不再跟踪。
> 每条均已对照当前代码核实；核对不实的标注「待核」。

---

## 1. 未落地项（核实确认）

暂无。

---

## 2. 已知遗留（`texttool lint` 特性）

暂无。

本节原有 16 条随 v3.0.0 全部处置：逐条去向与证据见 `doc/archive/tech-debt-landed.md`；
原特性评审明细在 `doc/archive/plans/2026-09-26-texttool-lint-reviews/`。会改可观察输出
（输出字节、退出码、JSON 契约）的按破坏性变更处理，README 契约节与发布说明已同步写明。

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
grep -oP '(?<=<Version>)[0-9.]+(?=<)' Directory.Build.props  # 期望 3.0.0

# 测试用例数（[Fact] + [Theory] 属性条数，非运行条数）
grep -roE '\[(Fact|Theory)' TextTool.Tests --include=*.cs | wc -l   # 期望 245

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
