# TextTool 发版自动化设计

> 日期：2026-09-28 ｜ 状态：已批准开工 ｜ 类型：设计文档
> 来源：用户指令「开始打 tag，再做一个自动化 release 流程」
> 前置：`main` = `2.6.1`（D-1–D-5 已推）。权威约束：`doc/adr/ADR-008-update-trust-model.md`、`doc/UpdateSecurity.md`、`fact:github.account`（本机 PATH 无 `gh`）。

## 1. 背景与目标

CI 在推 `v*` tag 时已经会构建、打包 zip、写 `.sha256`、创建 GitHub Release。缺口是：

- 本机没有 `gh`，D-3 的 `publish.ps1` 调不通
- 打 tag、等 CI、签名、上传 `.sig` 仍是手敲
- `Directory.Build.props` 与 tag 没有门禁，可能发错版本号

**目标**：一条本地命令走完「对账版本 → 打 annotated tag → 推 tag → 等 CI Release → 离线签名 → 上传 `.sig`」。本轮用它发出 `v2.6.1`。

**成功标准**：`scripts/release.ps1` 在干净 `main` 上可发版；`publish.ps1` 不再依赖 `gh`；CI 在 tag 与 `<Version>` 不一致时失败；私钥仍只在开发机；脚本正文不含 token / PEM。

## 2. 范围

### In scope

| 项 | 归属 |
|---|---|
| `publish.ps1` 改为 GitHub REST + `curl.exe` | `scripts/` |
| 新 `release.ps1` 编排 tag / 等待 / 调 publish | `scripts/` |
| 共享 REST 辅助（点源，不单独当入口） | `scripts/github-release.ps1` |
| CI：tag 对账 `<Version>`；生成 Release notes | `.github/workflows/build-test.yml` |
| 文档与脚本测试 | `doc/`、`TextTool.Tests/Scripts/` |

### Out of scope

- 把私钥或签名放进 GitHub Actions（ADR-008）
- 安装 `gh` / SSH
- 打 `v2.6.0`（从未打过；`v2.6.1` 覆盖 GUI lint + 债清除）
- Authenticode 证书（CI 未配 secret 时继续跳过）
- 真机 `texttool update` 冒烟（清单仍打印，人工做）
- 加密签名私钥

## 3. 方案选型

**选定：本地编排器 + 现有 tag CI + REST 签名上传。**

备选（不采用）：

- **装 `gh` 再包一层**：`fact:github.account` 写明本机没有 `gh`；GitHubOps 禁止把它当捷径
- **CI 里签名**：私钥进 secret 违反 ADR-008
- **本地构建 zip 再 `gh release create`**：与 `doc/Publish.md`「以 CI 产物为准」冲突，且仍要 `gh`

## 4. 设计

### 4.1 认证

| 通道 | 做法 |
|---|---|
| git 推 tag | `git -c http.extraheader="Authorization: Basic …"`，remote URL 保持干净 |
| REST | `Authorization: Bearer`，`curl.exe`，不走 `Invoke-WebRequest`（S302 MITM 下更稳） |

令牌来源（按序）：环境变量 `TEXTTOOL_GITHUB_TOKEN`；否则读 `E:\work_zone\ApiKey\` 下既有 PAT 文件。`publish.ps1` **正文**仍禁止出现 `GithubApiToken` / `ghp_`（沿用 D-3 测试）。

### 4.2 `publish.ps1`

参数与 D-3 兼容：`-Version` / `-SkipUpload` / `-Force` / `-KeyPath` / `-Repo`。

下载改为 `GET /repos/{repo}/releases/tags/{tag}`，按资产名拉取 `TextTool-*-{raw}-win-x64.zip` 与对应 `.sha256`。上传 `.sig` 走 `uploads.github.com`。已有同名资产且未 `-Force` 则 exit 1；`-Force` 先删再传。HTTP 502/503 重试最多 3 次。

签名 / verify / 私钥路径与 D-3 相同。临时目录在 `finally` 删除。

### 4.3 `release.ps1`

```
.\scripts\release.ps1 [-Version x.y.z] [-SkipUpload] [-SkipTag] [-DryRun] [-Force]
                      [-KeyPath …] [-Repo …] [-Message …]
```

| 条件 | 行为 |
|---|---|
| `-Version` 缺省 | 读 `Directory.Build.props` 的 `<Version>` |
| 值不匹配 `^v?\d+\.\d+\.\d+$` | stderr + exit 2 |
| `-DryRun` | 打印 version / tag / HEAD / dirty，exit 0，不碰网、不打 tag |
| 工作树脏 / 不在 `main` / 与 `origin/main` 不一致 | exit 1（`-SkipTag` 时不检查「与 origin 一致」中的 ahead，但仍要求干净） |
| props 与即将打的 tag 不一致 | exit 1 |
| 本地或远端已有该 tag | exit 1，提示 `-SkipTag` 只走等待+签名 |
| 默认 | annotated tag → 推 `refs/tags/vX.Y.Z` → 轮询该 sha 上 `head_branch=vX.Y.Z` 的 workflow run 至 success → 轮询 Release 直到两 zip + 两 sha256 → 调 `publish.ps1` |

等待超时 30 分钟，间隔 20 秒。workflow `conclusion` 非 success 则 exit 1，不签名。

默认 tag 说明：`v2.6.1: GUI lint 页签与 D-1–D-5 债清除`（仅本轮发版时由调用方 `-Message` 传入；脚本默认 `v$raw`）。

### 4.4 CI

tag 构建开始时核对 `<Version>` 等于 tag 去 `v` 后的数字。`softprops/action-gh-release` 打开 `generate_release_notes: true`。

### 4.5 测试（不打网）

沿用 D-3：起 PowerShell 跑脚本，不调真实 GitHub。

- `publish.ps1 -Version abc` → 2；缺 `-Version` → 非 0
- 正文含 `SkipUpload`、`Force`、`ReleaseSigner`、`verify`、`api.github.com`；不含 `gh release`、`GithubApiToken`、`ghp_`、`BEGIN`
- `release.ps1 -Version abc` → 2；`-DryRun` → 0 且 stdout 含 `v` 与 props 版本
- `release.ps1` 正文含 `SkipTag`、`DryRun`、`Directory.Build.props`；不含 `ghp_`、`BEGIN`

## 5. 本轮发 `v2.6.1`

自动化合入 `main` 并推 origin 之后，在该 HEAD 打 annotated tag `v2.6.1` 并走完整流程。不打 `v2.6.0`。
