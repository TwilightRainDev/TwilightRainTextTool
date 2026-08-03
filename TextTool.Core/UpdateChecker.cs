using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TextTool.Services;

/// <summary>
/// 检查 GitHub Releases 最新版本。
/// 通过 GitHub API 获取最新 release tag，与当前版本对比。
/// </summary>
public static class UpdateChecker
{
    /// <summary>仓库地址（唯一事实来源）。改名只需改这里。</summary>
    public const string RepositoryUrl = "https://github.com/TwilightRainDev/TwilightRainTextTool";

    /// <summary>GitHub API 最新 release 端点（由 RepositoryUrl 推导，避免 URL 多处硬编码）。</summary>
    public static string LatestReleaseUrl =>
        RepositoryUrl.Replace("https://github.com/", "https://api.github.com/repos/") + "/releases/latest";

    /// <summary>严格版本白名单：v 前缀可选、二至三段数字。拒绝负数/后缀垃圾/路径字符。</summary>
    private static readonly Regex StrictVersionRegex =
        new(@"^v?\d+\.\d+(\.\d+)?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// 严格版本校验（进入任何路径/脚本构造前的强制门禁）：
    /// 格式白名单 + 分量 ≤9999（拒绝溢出，如 9999999999.0.0 这类 tag 无法再永久瘫痪更新通道）。
    /// </summary>
    public static bool IsStrictVersion(string version) =>
        StrictVersionRegex.IsMatch(version)
        && TryParse(version, out var parsed)
        && parsed.Major <= 9999 && parsed.Minor <= 9999 && parsed.Patch <= 9999;

    /// <summary>
    /// 查询 GitHub 最新 release 的 tag 名。
    /// 无网络/无 release 时返回 null。
    /// </summary>
    public static async Task<string?> GetLatestVersionAsync(HttpClient client)
    {
        string url = LatestReleaseUrl;
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("User-Agent", "TextTool-Updater");
        request.Headers.Add("Accept", "application/vnd.github+json");

        using var response = await client.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            return null;

        string json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("tag_name", out var tag) ? tag.GetString() : null;
    }

    /// <summary>
    /// 比较两个版本字符串（v2.3.0 → 2.3.0）。返回 true 表示 latest 更新。
    /// 任意解析失败时保守返回 false（不打扰用户）。
    /// </summary>
    public static bool IsNewer(string currentVersion, string latestVersion)
    {
        if (!TryParse(currentVersion, out var cur) || !TryParse(latestVersion, out var lat))
            return false;
        return Compare(cur, lat) < 0;
    }

    private static int Compare((int Major, int Minor, int Patch) a, (int Major, int Minor, int Patch) b)
    {
        if (a.Major != b.Major) return a.Major.CompareTo(b.Major);
        if (a.Minor != b.Minor) return a.Minor.CompareTo(b.Minor);
        return a.Patch.CompareTo(b.Patch);
    }

    private static bool TryParse(string version, out (int Major, int Minor, int Patch) parsed)
    {
        parsed = default;
        string v = version.Trim().TrimStart('v');
        string[] parts = v.Split('.');
        if (parts.Length < 2) return false;

        if (!int.TryParse(parts[0], out int major)) return false;
        if (!int.TryParse(parts[1], out int minor)) return false;
        int patch = parts.Length >= 3 && int.TryParse(parts[2], out int p) ? p : 0;
        parsed = (major, minor, patch);
        return true;
    }
}
