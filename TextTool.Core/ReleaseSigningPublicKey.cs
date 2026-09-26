namespace TextTool.Services;

/// <summary>
/// 发布签名公钥（SPKI base64，ECDsa P-256）。
/// 与 E:\work_zone\ApiKey\TextTool-signing.pub.pem 一致；私钥仅存于开发机，绝不进仓库/CI。
/// 轮换：生成新密钥对后更新此常量，并保留旧公钥至过渡期结束（详见 doc/UpdateSecurity.md）。
/// </summary>
public static class ReleaseSigningPublicKey
{
    public const string SpkiBase64 =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEIzWVU8dpX9CjEDUiVO5HtQ+c+hL6jJbOwfG1kkxyfWJronzXnLwXzDbk4TFdzHM7HVFVS+qdWHdqdBd4zky6fg==";
}
