using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace TextTool.Services;

/// <summary>
/// CLI 自更新：查询最新 release → 下载 CLI zip + .sha256 → 校验 → 解压 staging → 延迟替换并重启。
/// 运行中的 exe 无法覆盖自身，因此排定一个批处理脚本在本进程退出后执行替换。
/// </summary>
public static class SelfUpdater
{
    public const string LatestReleaseUrl = "https://api.github.com/repos/TwilightRainDev/TextTool/releases/latest";

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

    /// <summary>构建延迟替换批处理脚本：等本进程退出 → 覆盖安装目录 → 启动新 exe → 清理。</summary>
    public static string BuildUpdateScript(string installDir, string stagingDir, string exePath, int parentPid)
    {
        string target = installDir.TrimEnd('\\');
        return $$"""
            @echo off
            setlocal
            :waitloop
            tasklist /fi "PID eq {{parentPid}}" >nul 2>&1
            if %errorlevel%==0 (
                timeout /t 1 /nobreak >nul
                goto waitloop
            )
            robocopy "{{stagingDir}}" "{{target}}" /e /is /it /nfl /ndl /njh /njs /nc /ns /np
            start "" /d "{{target}}" "{{exePath}}"
            rd /s /q "{{stagingDir}}"
            del "%~f0"
            """;
    }

    /// <summary>仅检查：返回是否有新版本。网络失败时 Error 非空。</summary>
    public static async Task<UpdateResult> CheckAsync(HttpClient client, string currentVersion)
    {
        string? latest = await UpdateChecker.GetLatestVersionAsync(client);
        if (latest is null)
            return new UpdateResult(false, Error: "无法查询最新版本（无网络或仓库无 release）");
        if (!UpdateChecker.IsNewer(currentVersion, latest))
            return new UpdateResult(false, LatestVersion: latest);
        return new UpdateResult(true, LatestVersion: latest);
    }

    /// <summary>执行更新：下载 → 校验 → 解压 → 排定替换脚本。返回后本进程应立即退出。</summary>
    public static async Task<UpdateResult> UpdateAsync(HttpClient client, string currentVersion)
    {
        var check = await CheckAsync(client, currentVersion);
        if (check.Error is not null || !check.HasUpdate)
            return check;

        using var release = await GetReleaseAsync(client);
        if (release is null)
            return new UpdateResult(false, Error: "无法查询最新版本（无网络或仓库无 release）");

        string version = check.LatestVersion!;
        string? assetUrl = FindCliAssetUrl(release, version);
        if (assetUrl is null)
            return new UpdateResult(false, Error: $"未找到 CLI 安装包资产（{GetCliAssetName(version)}）");

        string tempDir = Path.Combine(Path.GetTempPath(), "texttool-update", version);
        Directory.CreateDirectory(tempDir);
        string zipPath = Path.Combine(tempDir, GetCliAssetName(version));

        byte[] zipBytes = await client.GetByteArrayAsync(assetUrl);
        string shaContent = await client.GetStringAsync(assetUrl + ".sha256");
        if (!TryParseSha256(shaContent, out string expected) ||
            !Sha256Hex(zipBytes).Equals(expected, StringComparison.OrdinalIgnoreCase))
            return new UpdateResult(false, Error: "安装包校验失败（SHA256 不匹配），已中止更新");

        await File.WriteAllBytesAsync(zipPath, zipBytes);

        string staging = Path.Combine(tempDir, "staging");
        if (Directory.Exists(staging)) Directory.Delete(staging, true);
        ZipFile.ExtractToDirectory(zipPath, staging);

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

        return new UpdateResult(true, LatestVersion: version);
    }

    private static async Task<JsonDocument?> GetReleaseAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
        request.Headers.Add("User-Agent", "TextTool-Updater");
        request.Headers.Add("Accept", "application/vnd.github+json");
        using var response = await client.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            return null;
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static string Sha256Hex(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
}
