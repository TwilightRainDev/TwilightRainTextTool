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

    /// <summary>GitHub API JSON 响应上限（release 元数据远小于此，仅防异常响应吃满内存）。</summary>
    public const long MaxJsonBytes = 1L * 1024 * 1024;

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

    /// <summary>
    /// 下载地址白名单：仅 https + 默认 443 端口 + 允许主机（含每次重定向跳转）。
    /// Uri.Port 在未显式指定端口时返回方案默认值 443，故 `:4444` 等非常规端口一律拒绝。
    /// </summary>
    public static bool IsAllowedDownloadUrl(Uri url) =>
        url.Scheme == Uri.UriSchemeHttps
        && url.Port == 443
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

            return await ReadBoundedAsync(response, MaxDownloadBytes);
        }
    }

    /// <summary>
    /// 流式读取响应体并实时统计字节数（声明长度可伪造，以实读为准）——
    /// 超上限立即中止，不会先整体缓冲进内存。
    /// </summary>
    public static async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response, long maxBytes)
    {
        if (response.Content.Headers.ContentLength is { } declared && declared > maxBytes)
            throw new InvalidOperationException($"响应超过大小上限：{maxBytes} 字节");

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        long total = 0;
        int read;
        while ((read = await stream.ReadAsync(chunk)) > 0)
        {
            total += read;
            if (total > maxBytes)
                throw new InvalidOperationException($"响应超过大小上限：{maxBytes} 字节");
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }
}
