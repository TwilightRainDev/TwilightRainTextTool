# 自更新安全：签名发布流程与轮换手册

> 配套 ADR：[ADR-008-update-trust-model](adr/ADR-008-update-trust-model.md)、[ADR-009-single-folder-layout](adr/ADR-009-single-folder-layout.md)

## 信任模型（三层）

| 层 | 机制 | 防御目标 |
|---|---|---|
| 信任根 | ECDsa P-256 签名（`<zip>.sig`，私钥仅存开发机） | 中间人/仓库被攻破均无法伪造更新 |
| 完整性 | `.sha256` 同信道校验（保留，防传输损坏） | 意外损坏/CDN 不一致 |
| 传输 | TLS 默认 OS 信任（链根非公共 CA 时**仅告警**）+ 主机/443 端口白名单 + 禁自动重定向 + 响应大小上限（zip 256MB / API JSON 1MB，流式边读边限） | 阻止任意主机下载/降级；暴露 MITM 存在 |

缺失任何一层都不降级：**`.sig` 缺失或验签失败 → 更新中止**，不退回 sha256-only。

### TLS 策略（默认不强制固定）

签名落地后，中间人已无法伪造更新内容（签名兜底），因此 TLS 公共根固定从"必要防线"降级为**可选的加固/检测手段**，默认关闭——国内 DNS 污染下 S302 类加速器/企业代理是主流连法，强制固定会让这部分用户无法更新：

- **默认**：OS 信任校验 TLS，若链根非公共 CA（中间代理存在）CLI 打印警告但不中断；`.sig` 签名仍保证安装包安全
- **严格模式**：`TEXTTOOL_UPDATE_STRICT_TLS=1` 时链根必须为公共 CA（`pinned_roots.txt`），任何 MITM 代理直接拒绝连接（更新失败是预期行为）

## 密钥管理

- **私钥**：`E:\work_zone\ApiKey\TextTool-signing.priv.pem`（ECDsa P-256，PKCS#8 PEM）
- **公钥**：`E:\work_zone\ApiKey\TextTool-signing.pub.pem`（SPKI PEM，与客户端内嵌常量一致）
- 生成/重建（私钥文件存在时会拒绝覆盖，需先手动备份）：
  ```
  dotnet run --project tools/ReleaseSigner -- keygen E:\work_zone\ApiKey [--passphrase <p>]
  ```
- 私钥当前未加密存储（与 ApiKey 目录内其他明文 token 同一保护水平）。若需要加密：`--passphrase` 重建，签名时用 `--passphrase` 或环境变量 `TEXTTOOL_SIGN_PASSPHRASE`
- **绝不**把私钥放进 GitHub/CI（仓库被攻破时 secret 会一起被偷）

## 发布流程（每次发版）

1. CI 构建 + 打包 zip + 生成 `.sha256`（现有 build-test.yml 流程不变）
2. 下载 CI 产物 zip 到本地（例如 `gh run download`）
3. 离线签名：
   ```
   dotnet run --project tools/ReleaseSigner -- sign TextTool-CLI-2.4.4-win-x64.zip -k E:\work_zone\ApiKey\TextTool-signing.priv.pem
   ```
   （或先 `cd tools/ReleaseSigner && dotnet build -c Release`，再直接运行 exe）
4. 上传 `.sig` 到 release：
   ```
   gh release upload v2.4.4 TextTool-CLI-2.4.4-win-x64.zip.sig TextTool-GUI-2.4.4-win-x64.zip.sig
   ```
5. 自检：
   ```
   dotnet run --project tools/ReleaseSigner -- verify TextTool-CLI-2.4.4-win-x64.zip TextTool-CLI-2.4.4-win-x64.zip.sig -k E:\work_zone\ApiKey\TextTool-signing.pub.pem
   texttool update --check   # 或在新版本上跑一次真实 update
   ```

**检查清单**：□ zip 存在 □ `.sha256` 存在 □ `.sig` 存在且 verify 通过 □ 本地真实 update 成功

## 公钥轮换（两步发布）

旧客户端只认识旧公钥，直接用新私钥签名会卡住所有旧客户端。标准流程：

1. 生成新密钥对，把新公钥 SPKI 写进 `ReleaseSigningPublicKey.cs`，用**旧私钥**签名发布此版本 → 旧客户端正常升级，新客户端已内置新公钥
2. 后续版本改用新私钥签名 → 新客户端正常升级
3. （可选）一个过渡期后，为需要兼容极旧客户端的版本保留旧私钥备份

## TLS 锚点更新

锚点清单 `TextTool.Core/pinned_roots.txt` 由 certifi（Mozilla 公共根）生成：

- 从 https://raw.githubusercontent.com/certifi/python-certifi/master/certifi/cacert.pem 获取
- 按证书拆分后用 openssl 提取 SPKI base64（每行一条），覆盖写回 `pinned_roots.txt`
- 重编译发布即可生效（嵌入式资源）

轮换判断：当用户报"证书校验失败"且错误信息里根证书不是公共 CA 时——先确认是否是 S302 类工具（见下），再考虑更新清单。

## 已知影响：Steamcommunity302 类加速器（国内用户）

本机实测：Steamcommunity302 会把 `github.com`/`api.github.com`/`objects.githubusercontent.com` 等域名劫持到本地代理，并将自签 CA 装入系统根存储。开启时：

- 浏览器/curl 均"正常"（OS 信任），但全部 GitHub 流量可被该工具篡改
- **默认模式下更新可正常进行**（OS 信任 + 签名兜底，CLI 会打印一条中间代理警告）
- 严格模式（`TEXTTOOL_UPDATE_STRICT_TLS=1`）下更新会失败——这是预期行为（拒绝代理）
- 同类：SteamTools、企业 TLS 审计代理、部分杀软的 HTTPS 扫描

判断是否被中间代理拦截：CLI 输出含"检测到本机 GitHub 流量经中间代理"即为开启状态。

## 降级重放检测

签名（`.sig`）不覆盖版本号，中间人理论上可把任一官方旧版安装包"重放"为最新版（签名全合法）。客户端在程序目录维护 `last_known_version.txt`（单目录哲学，ADR-009），记录最近一次**成功安装**的版本；当 GitHub 声称的"最新版"低于该记录时，检查/更新一律报错中止（提示"检测到版本回退"）。首次使用无记录文件，不触发；确认是开发者正常回滚时，删除该文件后重试。

## 已知问题

2.4.4 发布实测暴露的 3 条收口问题（旧客户端 404、S302 代理下下载超时、替换脚本 wait-loop 脆弱）
及其修复设计见 [`specs/2026-08-03-texttool-2.4.5-design.md`](specs/2026-08-03-texttool-2.4.5-design.md)
（在途计划，单一权威，此处不重复）。

## 相关文件

| 文件 | 作用 |
|---|---|
| `TextTool.Core/ReleaseSigningPublicKey.cs` | 内嵌公钥（轮换改这里） |
| `TextTool.Core/ReleaseVerifier.cs` | 验签逻辑 |
| `TextTool.Core/PinnedRoots.cs` + `pinned_roots.txt` | TLS 锚点 |
| `TextTool.Core/UpdateClient.cs` | HttpClient 工厂（固定/白名单/重定向） |
| `TextTool.Core/SelfUpdater.cs` | 更新主流程（含解压安全上限、降级检测） |
| `TextTool.Core/UpdateChecker.cs` | 严格版本校验 |
| `TextTool.Core/LastKnownVersion.cs` | 降级重放检测记录（`last_known_version.txt`） |
| `TextTool.Core/RegexGuard.cs` | 正则统一超时（ReDoS 防御，2s） |
| `TextTool.Core/AtomicFile.cs` | 原子文件写入（临时文件 + Move） |
| `tools/ReleaseSigner/` | 离线签名工具（keygen/sign/verify/fingerprint） |
