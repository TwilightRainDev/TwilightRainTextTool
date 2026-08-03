using System.Security.Cryptography;

namespace TextTool.Services;

/// <summary>
/// 发布包签名验证：zip 由发布者私钥离线签名（<zip>.sig），客户端仅持有公钥。
/// 这是更新链的信任根——比 zip 与 .sha256 同信道下载的校验强得多：
/// 中间人/仓库被攻破都无法伪造签名。
/// </summary>
public static class ReleaseVerifier
{
    /// <summary>
    /// 验证 zip 字节与 DER 签名的 ECDsa P-256 签名。
    /// publicKeySpki 参数供测试注入临时公钥；生产路径使用内嵌公钥。
    /// </summary>
    public static bool VerifyZipSignature(byte[] zipBytes, byte[] signature, string? publicKeySpki = null)
    {
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(
                Convert.FromBase64String(publicKeySpki ?? ReleaseSigningPublicKey.SpkiBase64), out _);
            return ecdsa.VerifyData(zipBytes, signature, HashAlgorithmName.SHA256);
        }
        catch (Exception e) when (e is FormatException or CryptographicException or ArgumentException)
        {
            return false; // 签名/公钥格式非法一律视为校验失败
        }
    }
}
