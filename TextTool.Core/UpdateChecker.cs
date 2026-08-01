using System.Net.Http;
using System.Text.Json;

namespace TextTool.Services;

/// <summary>
/// 检查 GitHub Releases 最新版本。
/// 通过 GitHub API 获取最新 release tag，与当前版本对比。
/// </summary>
public static class UpdateChecker
{
    /// <summary>仓库地址（用于拼接 API 与 release 页面）。</summary>
    public const string RepositoryUrl = "https://github.com/TwilightRainDev/TextTool";

    /// <summary>
    /// 查询 GitHub 最新 release 的 tag 名。
    /// 无网络/无 release 时返回 null。
    /// </summary>
    public static async Task<string?> GetLatestVersionAsync(HttpClient client)
    {
        string url = "https://api.github.com/repos/TwilightRainDev/TextTool/releases/latest";
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
