# TextTool 发版自动化实施计划

> **对于执行者：** 可按任务顺序在本会话直接做，不必再开 SubagentDrivenDev。
> 规格：`doc/specs/2026-09-28-texttool-release-automation-design.md`。冲突以规格为准。

**目标：** REST 化 `publish.ps1`；新增 `release.ps1`；CI 对账版本并生成 notes；用它发出 `v2.6.1`。

**全局约束：**

- 行尾 LF、无 BOM、禁 emoji
- locals camel_case（C# 测试）
- 不改 `git config`；提交署名沿用 `TwilightRainDev <122437146+TwilightRainDev@users.noreply.github.com>`
- 提交信息中文前缀 + `Co-Authored-By: Cursor <noreply@cursor.com>`
- 私钥不进仓库 / CI；remote URL 不写 token
- 质量门禁：`dotnet build TextTool.sln -c Release -warnaserror`、`dotnet test TextTool.Tests/TextTool.Tests.csproj -c Release`、`dotnet format TextTool.sln --verify-no-changes`

## 任务

### Task 1：规格已落盘（本文 + 设计文档）

### Task 2：脚本与测试

- 新增 `scripts/github-release.ps1`
- 重写 `scripts/publish.ps1`（参数兼容 D-3）
- 新增 `scripts/release.ps1`
- 改 `PublishScriptTests`；新增 `ReleaseScriptTests`

### Task 3：CI 与文档

- `build-test.yml`：tag 对账 Version；`generate_release_notes`
- `doc/UpdateSecurity.md`、`doc/Publish.md`、README 结构树与 tag 计数

### Task 4：合入并发 `v2.6.1`

- 提交 → 推 `main` → `release.ps1 -Version 2.6.1 -Message "v2.6.1: GUI lint 页签与 D-1–D-5 债清除"`
