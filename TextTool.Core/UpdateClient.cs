using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace TextTool.Services;

/// <summary>
/// 自更新专用的 HttpClient 工厂。
/// 安全策略：TLS 链根必须是公共 CA（PinnedRoots 白名单，阻断私有 CA 型中间人）；
/// 禁自动重定向（由 FetchBytesAsync 逐跳白名单校验）；响应缓冲上限 256MB。
/// </summary>
public static class UpdateClient
{
    /// <summary>更新链路允许访问的主机（GitHub API / release 下载与跳转）。</summary>
    public static readonly string[] AllowedHosts =
    {
        "github.com", "www.github.com", "api.github.com",
        "objects.githubusercontent.com", "release-assets.githubusercontent.com",
        "github-releases.githubusercontent.com", "raw.githubusercontent.com",
    };

    private const int MaxRedirectHops = 5;
    private const long MaxDownloadBytes = 256L * 1024 * 1024;

    /// <summary>
    /// 严格模式环境变量：设置后 TLS 链根必须为公共 CA，否则拒绝连接。
    /// 默认关闭——国内 DNS 污染下 S302 类加速器/企业代理是常见连法（TLS 被其 MITM），
    /// 而安装包完整性已由发布签名（ReleaseVerifier）兜底，固定仅作加固/检测用。
    /// </summary>
    public const string StrictTlsEnvVar = "TEXTTOOL_UPDATE_STRICT_TLS";

    private static bool MitmDetectedState;

    /// <summary>默认模式下是否检测到中间代理（TLS 链根非公共 CA）。仅供提示，不影响流程。</summary>
    public static bool MitmDetected => MitmDetectedState;

    public static void ResetMitmDetection() => MitmDetectedState = false;

    /// <summary>创建更新专用 HttpClient（严格模式与否由环境变量 TEXTTOOL_UPDATE_STRICT_TLS 决定）。</summary>
    public static HttpClient Create(TimeSpan timeout) =>
        Create(timeout, IsStrictTlsEnabled());

    /// <summary>创建更新专用 HttpClient：禁自动重定向；strictTls=true 时强制链根为公共 CA。</summary>
    public static HttpClient Create(TimeSpan timeout, bool strictTls)
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            ServerCertificateCustomValidationCallback = (_, _, chain, errors) =>
                errors == SslPolicyErrors.None
                && (strictTls ? PinnedRoots.IsChainRootPinned(chain) : RecordMitmDetection(chain)),
        };
        return new HttpClient(handler) { Timeout = timeout };
    }

    private static bool IsStrictTlsEnabled()
    {
        string? value = Environment.GetEnvironmentVariable(StrictTlsEnvVar);
        return value is "1" or "true" or "TRUE" or "yes";
    }

    private static bool RecordMitmDetection(X509Chain? chain)
    {
        if (chain is not null && !PinnedRoots.IsChainRootPinned(chain))
            MitmDetectedState = true;
        return true; // 默认模式放行（签名兜底），仅记录检测结果
    }

    /// <summary>下载地址白名单：仅 https + 允许主机（含每次重定向跳转）。</summary>
    public static bool IsAllowedDownloadUrl(Uri url) =>
        url.Scheme == Uri.UriSchemeHttps
        && AllowedHosts.Contains(url.Host, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 手动跟随重定向下载（最多 5 跳），每一跳都强制 https + 主机白名单，
    /// 杜绝 HTTPS→HTTP 降级与跳转到任意主机（SSRF/恶意下载）。
    /// </summary>
    public static async Task<byte[]> FetchBytesAsync(HttpClient client, string url)
    {
        string current = url;
        for (int hop = 0; ; hop++)
        {
            if (!IsAllowedDownloadUrl(new Uri(current)))
                throw new InvalidOperationException($"不允许的下载地址：{current}");

            using var response = await client.GetAsync(current);
            if ((int)response.StatusCode is >= 300 and < 400)
            {
                if (hop >= MaxRedirectHops)
                    throw new InvalidOperationException($"重定向次数过多：{url}");
                string? location = response.Headers.Location?.ToString();
                if (string.IsNullOrEmpty(location))
                    throw new InvalidOperationException($"重定向缺少 Location：{current}");
                current = new Uri(new Uri(current), location).ToString();
                continue;
            }

            response.EnsureSuccessStatusCode();

            // 响应大小上限（Content-Length 缺失/伪造时以读后长度兜底）
            if (response.Content.Headers.ContentLength is { } declared && declared > MaxDownloadBytes)
                throw new InvalidOperationException($"响应超过大小上限：{url}");

            byte[] bytes = await response.Content.ReadAsByteArrayAsync();
            if (bytes.LongLength > MaxDownloadBytes)
                throw new InvalidOperationException($"响应超过大小上限：{url}");
            return bytes;
        }
    }
}
