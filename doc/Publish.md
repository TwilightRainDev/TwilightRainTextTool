# Publish Guide — TextTool

> 记录项目推送到 GitHub 以及编译发布的完整流程，供后续维护者参考。

---

## 一、项目结构

目录树与各文件职责以根目录 `README.md`「项目结构」节为准（单一权威，此处不再重复）。
拆分规则见 `doc/ArchitectureGuide.md` §8：UI 相邻代码在 `Services/`、`Controls/`，
可脱离 UI 测试的逻辑在 `TextTool.Core/`。

**不纳入版本控制**（见 `.gitignore`）：`bin/`、`obj/`、`.codegraph/`、`*.user`、`*.lnk`。
另有本地 `publish/` 输出目录，以及运行时自动生成的 `replace_rules.json`、`app_config.json`。

---

## 二、推送源码到 GitHub

### 远程仓库

```text
https://github.com/TwilightRainDev/TwilightRainTextTool.git
```

默认分支：`main`

### 首次推送（新克隆/新目录）

```bash
cd TextTool

# 如果还没有 .git（全新克隆不需要）
git init

# 如果远程未添加
git remote add origin https://github.com/TwilightRainDev/TwilightRainTextTool.git

# 添加文件并提交
git add .
git commit -m "vX.Y.Z: 更新说明"

# 推送到 GitHub（首次用 --force 确保覆盖）
git push -u origin main --force
```

### 日常推送（已有远程跟踪）

```bash
git add .
git commit -m "vX.Y.Z: 更新说明"
git push
```

### 分支名相关

- 本地分支为 `main`（已改名，旧版 `master` 已删除）
- 如果推送被拒绝（远程有本地没有的提交），可用 `--force` 覆盖

### 认证方式

用 **GitHub Personal Access Token (PAT)** 代替密码：

1. 访问 https://github.com/settings/tokens
2. 点 **Generate new token (classic)**
3. Note 随意填写，Scopes 勾选 `repo`
4. 生成后复制 token
5. 推送时 username 填 GitHub 用户名，password 粘贴 token

> **安全提醒**：token 用完即删，不要在源码或文档中留下 token。

---

## 三、编译发布

### 发布方式（以 CI 为准）

**发源码到 GitHub 并打 tag，CI 会自动构建、测试并发布 GitHub Release（zip + sha256）。`.sig` 必须在开发机离线签，私钥不进 CI。一条命令：**

```powershell
git push origin main
.\scripts\release.ps1 -Version X.Y.Z
```

`release.ps1`：对账版本 → annotated tag → 推 tag → 等 CI → 调 `publish.ps1` 签名上传。本机没有 `gh`，GitHub 走 REST + PAT。对照手工：`git tag -a vX.Y.Z` 后推 `refs/tags/vX.Y.Z`。

发布产物（`TextTool-GUI-*-win-x64.zip` 与 `TextTool-CLI-*-win-x64.zip`）由 CI 自动上传到 GitHub Release，**不要本地构建后再手动上传 zip**。`.sig` 例外，只由 `publish.ps1` 上传。

### 本地验证 / 需要本地 exe 时

当前采用 **依赖框架部署（Framework-dependent）**，用户需安装 .NET 8 Desktop Runtime。

```bash
dotnet build TextTool.sln -c Release   # 编译（输出 bin/Release/net8.0-windows/）
dotnet test TextTool.sln -c Release    # 跑测试

# 需要本地发布物时（命令与 CI 一致）
dotnet publish TextTool.csproj -c Release -r win-x64 -o publish/TextTool
# 输出 → publish/TextTool/
```

> 注：`-r win-x64` 构建的中间产物在 `bin/Release/net8.0-windows/win-x64/`，这是 SDK 标准输出目录（publish 从这里复制到 `-o`），git 已忽略，无需手动删除。

### 发布产物内容（CI zip 内）

| 文件 | 说明 |
|------|------|
| `TextTool.exe` | 原生可执行文件（双击运行） |
| `TextTool.dll` | 托管程序集 |
| `TextTool.Core.dll` | 核心库 |
| `TextTool.deps.json` | 依赖清单 |
| `TextTool.runtimeconfig.json` | 运行时配置 |
| `TextTool.pdb` / `TextTool.Core.pdb` | 调试符号（可删除） |
| `Localization/*.json` | 3 个语言文件 |

> 框架依赖版本体积约 **600 KB**，GBK 编码支持由 .NET 8 共享框架内置（无 CodePages NuGet 包）。

### 发布前的版本号更新

每次发布前，修改 `Directory.Build.props` 中的版本号：

```xml
<Version>X.Y.Z</Version>
```

csproj 中无需再设置 `<Version>`，所有项目自动继承此版本。

---

## 三·五、代码签名（可选）

tag 发布时，CI 会对 publish 产物中的 `.exe` / `.dll` 做 Authenticode 签名。**未配置证书时自动跳过**，发布流程照常。

### 配置证书（GitHub Secrets）

在仓库 `Settings → Secrets and variables → Actions` 添加：

| Secret | 内容 |
|--------|------|
| `CODE_SIGN_CERT` | Base64 编码的 PFX 证书文件 |
| `CODE_SIGN_PASSWORD` | PFX 证书密码 |
| `TIMESTAMP_URL`（可选） | RFC3161 时间戳服务器，如 `http://timestamp.digicert.com` |

PFX 转 Base64：

```powershell
[Convert]::ToBase64String([IO.File]::ReadAllBytes("C:\path\cert.pfx"))
```

### 本地验证签名

```powershell
signtool verify /pa /v TextTool.exe
```

---

## 三·六、CLI 自更新

`texttool update` / `texttool update --check` 的信任模型、校验链与离线签名流程见 [`UpdateSecurity.md`](UpdateSecurity.md)（单一权威，此处不重复）。

---

## 四、完整操作流程（速查）

```bash
# 1. 更新版本号
#    编辑 Directory.Build.props → Version

# 2. 本地验证（可选，CI 也会跑）
dotnet build TextTool.sln -c Release   # 编译
dotnet test TextTool.sln -c Release    # 跑测试

# 3. 提交到 Git
git add .
git commit -m "vX.Y.Z: 更新说明"

# 4. 推送到 GitHub（自动触发 CI 构建 + 测试）
git push

# 5. 打 tag 并完成签名发布（无需本地构建 zip）
.\scripts\release.ps1 -Version X.Y.Z
# 对账 Version → tag → CI 打包 zip → 本地签 .sig → 上传

# 6. 需要本地 exe 时（可选，以 CI 产物为准）
dotnet publish TextTool.csproj -c Release -r win-x64 -o publish/TextTool
# 输出 → publish/TextTool/
```
