using System.Security.Cryptography.X509Certificates;

namespace TextTool.Services;

/// <summary>
/// TLS 证书固定锚点：公共根 CA 公钥白名单（SPKI base64，每行一条）。
/// 数据来自 certifi（Mozilla 公共根清单，texttool-core/pinned_roots.txt 嵌入式资源）。
///
/// 作用：阻断 Steamcommunity302/SteamTools/企业代理/杀软等"私有根 CA 型"中间人——
/// 这类 MITM 的链根不在白名单内，必然拒绝。合法公共 CA 全部放行，不怕 GitHub 换 CA。
/// 注意：本机若运行 Steamcommunity302，其劫持的 GitHub 更新将因锚点不匹配而失败（预期行为）。
/// </summary>
public static class PinnedRoots
{
    private const string ResourceName = "TextTool.pinned_roots.txt";

    private static readonly Lazy<HashSet<string>> Anchors = new(() =>
    {
        using var stream = typeof(PinnedRoots).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"缺少嵌入式资源 {ResourceName}，请检查 csproj 的 EmbeddedResource 配置");
        using var reader = new StreamReader(stream);
        var set = new HashSet<string>(StringComparer.Ordinal);
        while (reader.ReadLine() is { Length: > 0 } line)
            set.Add(line);
        return set;
    });

    /// <summary>判断证书链的根证书公钥（SPKI base64）是否在白名单内。</summary>
    public static bool IsChainRootPinned(X509Chain? chain)
    {
        if (chain is null || chain.ChainElements.Count == 0)
            return false;
        var root = chain.ChainElements[^1].Certificate;
        byte[]? spki = root.GetRSAPublicKey()?.ExportSubjectPublicKeyInfo()
            ?? root.GetECDsaPublicKey()?.ExportSubjectPublicKeyInfo();
        return spki is not null && Anchors.Value.Contains(Convert.ToBase64String(spki));
    }
}
