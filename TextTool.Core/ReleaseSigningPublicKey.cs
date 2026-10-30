namespace TextTool.Services;

/// <summary>
/// 发布签名公钥（SPKI base64，ECDsa P-256）。
/// 与开发机保存的 `TextTool-signing.pub.pem` 一致；私钥仅存于开发机，绝不进仓库/CI。
/// 客户端只认这一个常量（ReleaseVerifier 的默认公钥）。旧客户端验不过用新私钥签的包，
/// 自动升级会直接失败，只能让用户手动换包。要让轮换不打扰用户，客户端得同时信任
/// 新旧两把公钥：把这里改成公钥集合，发一版两边都认的，过渡期结束再删旧公钥。
/// </summary>
public static class ReleaseSigningPublicKey
{
    public const string SpkiBase64 =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEIzWVU8dpX9CjEDUiVO5HtQ+c+hL6jJbOwfG1kkxyfWJronzXnLwXzDbk4TFdzHM7HVFVS+qdWHdqdBd4zky6fg==";
}
