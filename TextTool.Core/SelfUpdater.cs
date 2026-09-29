using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TextTool.Services;

/// <summary>
/// CLI 自更新：查询最新 release → 下载 CLI zip + .sha256 + .sig → 校验（SHA256 + 发布者签名）→ 解压 staging → 延迟替换并重启。
/// 运行中的 exe 无法覆盖自身，因此排定一个批处理脚本在本进程退出后执行替换。
///
/// 信任模型：zip 与 .sha256 同信道下载仅作完整性检查；真正的信任根是 .sig 签名
/// （ECDsa P-256，私钥仅存于开发机离线签名）。TLS 层由 UpdateClient 固定公共根 CA。
/// </summary>
public static class SelfUpdater
{
    /// <summary>解压安全上限：防止恶意/异常安装包以 zip 炸弹形式打爆磁盘。</summary>
    private const long MaxExtractBytes = 500L * 1024 * 1024;
    private const int MaxEntryCount = 20_000;

    /// <summary>
    /// 下载阶段的超时，比查询阶段宽松得多：release 元数据很小、无网时应当秒级失败，
    /// 而 zip 安装包要大得多（S302 类代理下实测接近 30s，正好撞上查询用的 30s 超时）。
    /// </summary>
    internal static readonly TimeSpan DownloadTimeout = TimeSpan.FromSeconds(90);

    public sealed record UpdateResult(bool HasUpdate, string? LatestVersion = null, string? Error = null);

    /// <summary>CLI 安装包资产名，如 TextTool-CLI-2.4.0-win-x64.zip。</summary>
    public static string GetCliAssetName(string version) =>
        $"TextTool-CLI-{version.TrimStart('v')}-win-x64.zip";

    /// <summary>在 release JSON 的 assets 中查找 CLI zip 下载地址；找不到返回 null。</summary>
    public static string? FindCliAssetUrl(JsonDocument releaseDoc, string version)
    {
        string want = GetCliAssetName(version);
        if (!releaseDoc.RootElement.TryGetProperty("assets", out var assets))
            return null;
        foreach (var asset in assets.EnumerateArray())
        {
            if (asset.TryGetProperty("name", out var name) && name.GetString() == want &&
                asset.TryGetProperty("browser_download_url", out var url))
                return url.GetString();
        }
        return null;
    }

    /// <summary>解析 .sha256 文件内容（"hash  filename"），返回小写 hash；格式非法返回 false。</summary>
    public static bool TryParseSha256(string content, out string hash)
    {
        hash = "";
        string? first = content.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (first is null || first.Length != 64 || !first.All(Uri.IsHexDigit))
            return false;
        hash = first.ToLowerInvariant();
        return true;
    }

    /// <summary>等待父进程退出的循环上限（次，约合秒）；父 PID 被复用时不至于永久空转。</summary>
    internal const int MaxWaitTries = 30;

    /// <summary>内置命令一律走绝对路径：PATH 上有同名程序时裸命令会被遮蔽（本机 Git 的 GNU timeout 即在列）。</summary>
    private const string System32 = @"%SystemRoot%\System32";

    /// <summary>
    /// 构建延迟替换批处理脚本：等本进程退出 → 覆盖安装目录 → 启动新 exe → 清理。
    /// 所有内插路径经 cmd 转义（%→%%，cmd 中 % 在引号内仍会展开，是唯一可注入字符）；
    /// robocopy 失败（exit code ≥8）时保留 staging 并明确报错，不静默删掉唯一一致版本。
    /// </summary>
    public static string BuildUpdateScript(string installDir, string stagingDir, string exePath, int parentPid) =>
        BuildUpdateScript(installDir, stagingDir, exePath, parentPid, MaxWaitTries);

    /// <summary>同上，但显式指定等待上限——便于用极小上限验证循环行为。</summary>
    internal static string BuildUpdateScript(
        string installDir, string stagingDir, string exePath, int parentPid, int maxWaitTries)
    {
        string target = EscapeCmd(installDir.TrimEnd('\\'));
        string staging = EscapeCmd(stagingDir);
        string exe = EscapeCmd(exePath);
        return ToCrlf($$"""
            @echo off
            setlocal DisableDelayedExpansion
            {{BuildWaitLoop(parentPid, maxWaitTries)}}
            robocopy "{{staging}}" "{{target}}" /e /is /it /r:1 /w:1 /nfl /ndl /njh /njs /nc /ns /np
            if %errorlevel% GEQ 8 (
                echo [TextTool] robocopy 复制失败（exit code %errorlevel%），已保留暂存目录 "{{staging}}"。 >&2
                start "" /d "{{target}}" "{{exe}}"
                exit /b 1
            )
            start "" /d "{{target}}" "{{exe}}"
            rd /s /q "{{staging}}"
            del "%~f0"
            """);
    }

    /// <summary>
    /// 生成"等父进程退出"的循环片段（由 BuildUpdateScript 内嵌）。三处加固，每一处都对应一个实测缺陷：
    /// 一是存活判据不取 tasklist 的退出码——实测它对不存在的 PID 也返回 0，条件恒真；改为用 find 看
    /// 输出里有没有该 PID；二是循环有次数上限——Windows 复用 PID，父进程退出后该 PID 若被别的进程
    /// 占用，无上限的循环会永久空转、替换永不发生；三是 timeout/tasklist/find 走绝对路径——PATH 上有
    /// 同名程序时裸命令会被遮蔽（GNU timeout 收到 /t 立刻报错返回，循环退化成忙等）。
    /// 结构上用 goto 标签而非括号块：脚本已 DisableDelayedExpansion，块内 %tries% 不会重估。
    /// </summary>
    internal static string BuildWaitLoop(int parentPid, int maxWaitTries) => $$"""
        set /a tries=0
        :waitloop
        {{System32}}\tasklist.exe /fi "PID eq {{parentPid}}" /nh | {{System32}}\find.exe "{{parentPid}}" >nul
        if errorlevel 1 goto proceed
        set /a tries+=1
        if %tries% GEQ {{maxWaitTries}} goto proceed
        {{System32}}\timeout.exe /t 1 /nobreak >nul
        goto waitloop
        :proceed
        """;

    /// <summary>
    /// 批处理必须以 CRLF 落盘：cmd 按字节偏移推进解析，LF 行尾叠加多字节字符时会错位，
    /// 把行从中间劈开（实测 `setlocal DisableDelayedExpansion` 被拆成 `'xpansion'`），
    /// 脚本会跳过加固语句甚至中途跌出循环。
    /// </summary>
    private static string ToCrlf(string script) =>
        script.Replace("\r\n", "\n").Replace("\n", "\r\n");

    private static string EscapeCmd(string value) => value.Replace("%", "%%");

    /// <summary>仅检查：返回是否有新版本。网络失败/版本格式异常/版本回退时 Error 非空。</summary>
    public static async Task<UpdateResult> CheckAsync(HttpClient client, string currentVersion)
    {
        string? latest = await UpdateChecker.GetLatestVersionAsync(client);
        if (latest is null)
            return new UpdateResult(false, Error: "无法查询最新版本（无网络或仓库无 release）");
        if (!UpdateChecker.IsStrictVersion(latest))
            return new UpdateResult(false, Error: $"最新发布版本号格式异常：{latest}");
        string? recorded = LastKnownVersion.Read();
        if (IsDowngradeAttempt(latest, recorded))
            return new UpdateResult(false, Error: DowngradeErrorMessage(latest, recorded!));
        if (!UpdateChecker.IsNewer(currentVersion, latest))
            return new UpdateResult(false, LatestVersion: latest);
        return new UpdateResult(true, LatestVersion: latest);
    }

    /// <summary>执行更新：下载 → 校验（SHA256 + 签名）→ 解压 → 排定替换脚本。返回后本进程应立即退出。</summary>
    public static Task<UpdateResult> UpdateAsync(HttpClient client, string currentVersion) =>
        UpdateAsync(client, currentVersion, UpdateClient.Create);

    /// <summary>同上，但由调用方给定下载 client 的工厂（按超时构造）——测试据此注入假 handler，不打真实网络。</summary>
    internal static async Task<UpdateResult> UpdateAsync(
        HttpClient client, string currentVersion, Func<TimeSpan, HttpClient> downloadClientFactory)
    {
        // 单次抓取 release（tag_name 与 assets 同源，消除双重请求 TOCTOU）
        using var release = await GetReleaseAsync(client);
        if (release is null)
            return new UpdateResult(false, Error: "无法查询最新版本（无网络或仓库无 release）");

        string? version = release.RootElement.TryGetProperty("tag_name", out var tag) ? tag.GetString() : null;
        if (version is null || !UpdateChecker.IsStrictVersion(version))
            return new UpdateResult(false, Error: $"最新发布版本号格式异常：{version ?? "(无 tag_name)"}");
        string? recorded = LastKnownVersion.Read();
        if (IsDowngradeAttempt(version, recorded))
            return new UpdateResult(false, Error: DowngradeErrorMessage(version, recorded!));
        if (!UpdateChecker.IsNewer(currentVersion, version))
            return new UpdateResult(false, LatestVersion: version);

        string? assetUrl = FindCliAssetUrl(release, version);
        if (assetUrl is null)
            return new UpdateResult(false, Error: $"未找到 CLI 安装包资产（{GetCliAssetName(version)}）");

        // —— 下载：TLS 公共根固定 + 主机白名单，失败即中止 ——
        // 查询沿用调用方传入的短超时 client（无网时快速失败），下载另起长超时 client：
        // 同一工厂产出，TLS 固定/主机白名单/禁重定向策略天然一致，只有超时放宽。
        using var downloadClient = downloadClientFactory(DownloadTimeout);
        byte[] zipBytes;
        string shaContent;
        byte[] sigBytes;
        try
        {
            zipBytes = await UpdateClient.FetchBytesAsync(downloadClient, assetUrl);
            shaContent = Encoding.UTF8.GetString(await UpdateClient.FetchBytesAsync(downloadClient, assetUrl + ".sha256"));
            sigBytes = await UpdateClient.FetchBytesAsync(downloadClient, assetUrl + ".sig");
        }
        catch (Exception ex)
        {
            return new UpdateResult(false, Error: $"下载失败：{ex.Message}");
        }

        // —— 校验：SHA256 防传输损坏；签名是信任根，缺失/无效一律中止（无 sha256-only 降级） ——
        if (!TryParseSha256(shaContent, out string expected) ||
            !Sha256Hex(zipBytes).Equals(expected, StringComparison.OrdinalIgnoreCase))
            return new UpdateResult(false, Error: "安装包校验失败（SHA256 不匹配），已中止更新");

        if (!ReleaseVerifier.VerifyZipSignature(zipBytes, sigBytes))
            return new UpdateResult(false, Error: "安装包签名校验失败（未签名或签名无效），已中止更新");

        // —— 落盘与解压：随机临时目录 + 落盘后重读复验，消除可预测路径与写盘后替换面 ——
        string tempDir = Path.Combine(Path.GetTempPath(), "texttool-update", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string zipPath = Path.Combine(tempDir, GetCliAssetName(version));
        await File.WriteAllBytesAsync(zipPath, zipBytes);

        byte[] onDisk = await File.ReadAllBytesAsync(zipPath);
        if (!Sha256Hex(onDisk).Equals(expected, StringComparison.OrdinalIgnoreCase))
            return new UpdateResult(false, Error: "安装包落盘校验失败，已中止更新");

        string staging = Path.Combine(tempDir, "staging");
        try
        {
            ExtractWithLimits(zipPath, staging, MaxExtractBytes, MaxEntryCount);
        }
        catch (InvalidOperationException ex)
        {
            return new UpdateResult(false, Error: ex.Message);
        }

        string exePath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "texttool.exe");
        string installDir = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory;

        string scriptPath = Path.Combine(tempDir, "apply-update.cmd");
        await File.WriteAllTextAsync(scriptPath, BuildUpdateScript(installDir, staging, exePath, Environment.ProcessId));

        Process.Start(new ProcessStartInfo
        {
            FileName = scriptPath,
            UseShellExecute = true,
            WorkingDirectory = tempDir,
            WindowStyle = ProcessWindowStyle.Hidden,
        });

        // 安装流程已排定（签名校验全过），记录本次成功安装的版本
        LastKnownVersion.Write(version);

        return new UpdateResult(true, LatestVersion: version);
    }

    /// <summary>
    /// 最新发布版本低于本机记录版本 = 疑似降级重放
    /// （中间人把官方旧版伪装成"最新"，把用户永久钉在旧版本上）。
    /// </summary>
    public static bool IsDowngradeAttempt(string latestVersion, string? recordedVersion) =>
        recordedVersion is not null
        && UpdateChecker.IsStrictVersion(recordedVersion)
        && UpdateChecker.IsNewer(latestVersion, recordedVersion);

    private static string DowngradeErrorMessage(string latestVersion, string recordedVersion) =>
        $"检测到版本回退：最新发布 {latestVersion} 低于本机已记录版本 {recordedVersion}。" +
        "可能遭中间人降级或发布回滚；确认是正常回滚时，删除程序目录下的 last_known_version.txt 后重试。";

    /// <summary>
    /// 解压 zip 到 staging，边解压边统计实际写出的字节数——
    /// zip 条目声明长度可伪造，不能只信 entry.Length；同时拒绝路径穿越（zip slip）。
    /// 任一超限立即中止并抛出，不留下部分解压的 staging。
    /// </summary>
    public static void ExtractWithLimits(string zipPath, string stagingDir, long maxBytes, int maxEntries)
    {
        Directory.CreateDirectory(stagingDir);
        string stagingRoot = Path.GetFullPath(stagingDir);

        using var archive = ZipFile.OpenRead(zipPath);
        if (archive.Entries.Count > maxEntries)
            throw new InvalidOperationException($"安装包条目数超过安全上限（{maxEntries}），已中止更新");

        long total = 0;
        foreach (var entry in archive.Entries)
        {
            string dest = Path.GetFullPath(Path.Combine(stagingDir, entry.FullName));
            if (!dest.StartsWith(stagingRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("安装包包含非法路径条目（zip slip），已中止更新");

            if (entry.Name.Length == 0)  // 目录条目
            {
                Directory.CreateDirectory(dest);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            long written = 0;
            using (var src = entry.Open())
            using (var dst = File.Create(dest))
            {
                var chunk = new byte[81920];
                int read;
                while ((read = src.Read(chunk, 0, chunk.Length)) > 0)
                {
                    written += read;
                    if (total + written > maxBytes)
                        throw new InvalidOperationException(
                            $"安装包解压内容超过安全上限（{maxBytes} 字节），已中止更新");
                    dst.Write(chunk, 0, read);
                }
            }
            total += written;
        }
    }

    private static async Task<JsonDocument?> GetReleaseAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, UpdateChecker.LatestReleaseUrl);
        request.Headers.Add("User-Agent", "TextTool-Updater");
        request.Headers.Add("Accept", "application/vnd.github+json");
        using var response = await client.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            return null;
        byte[] json = await UpdateClient.ReadBoundedAsync(response, UpdateClient.MaxJsonBytes);
        return JsonDocument.Parse(json);
    }

    private static string Sha256Hex(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
}
