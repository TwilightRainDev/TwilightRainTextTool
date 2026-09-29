using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace ReleaseSigner;

/// <summary>
/// 发布签名工具（仅开发机离线使用，不随应用发布）。
///
/// 用法：
///   ReleaseSigner keygen &lt;outDir&gt; [--passphrase &lt;p&gt;]   生成 ECDsa P-256 密钥对
///   ReleaseSigner sign &lt;zip&gt; -k &lt;priv.pem&gt; [--passphrase &lt;p&gt;]   对 zip 离线签名，输出 &lt;zip&gt;.sig
///   ReleaseSigner verify &lt;zip&gt; &lt;sig&gt; -k &lt;pub.pem&gt;        验签
///   ReleaseSigner fingerprint &lt;pub.pem&gt;                   打印 SPKI base64（用于嵌入客户端源码）
///
/// 安全约定：私钥只留在开发机（建议 E:\WorkZone\ApiKey\TextTool），绝不进入 GitHub/CI。
/// </summary>
public static class Program
{
    private const string PrivFileName = "TextTool-signing.priv.pem";
    private const string PubFileName = "TextTool-signing.pub.pem";
    private const string KeyEnv = "TEXTTOOL_SIGN_KEY";
    private const string PassphraseEnv = "TEXTTOOL_SIGN_PASSPHRASE";

    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0) return PrintUsage();
            return args[0].ToLowerInvariant() switch
            {
                "keygen" => KeyGen(args[1..]),
                "sign" => Sign(args[1..]),
                "verify" => Verify(args[1..]),
                "fingerprint" => Fingerprint(args[1..]),
                _ => PrintUsage(),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"错误：{ex.Message}");
            return 1;
        }
    }

    private static int KeyGen(string[] args)
    {
        string outDir = args.Length > 0 ? args[0] : ".";
        string? passphrase = GetOption(args, "--passphrase");

        Directory.CreateDirectory(outDir);
        string privPath = Path.Combine(outDir, PrivFileName);
        string pubPath = Path.Combine(outDir, PubFileName);
        if (File.Exists(privPath) || File.Exists(pubPath))
            throw new InvalidOperationException($"目标目录已存在密钥文件：{privPath}（如需重建请先手动备份删除）");

        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        string pubPem = ecdsa.ExportSubjectPublicKeyInfoPem();
        string privPem = passphrase is null
            ? ecdsa.ExportPkcs8PrivateKeyPem()
            : ecdsa.ExportEncryptedPkcs8PrivateKeyPem(passphrase,
                new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 200_000));

        File.WriteAllText(pubPath, pubPem);
        File.WriteAllText(privPath, privPem);

        Console.WriteLine($"公钥：{pubPath}");
        Console.WriteLine($"私钥：{privPath}");
        if (passphrase is null)
            Console.WriteLine("警告：私钥未加密存储，请确保该目录仅本人可访问（或用 --passphrase 重建）。");
        Console.WriteLine($"SPKI base64（用于嵌入客户端源码）：{Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo())}");
        return 0;
    }

    private static int Sign(string[] args)
    {
        string zipPath = GetPositional(args, 0, "sign <zip>");
        string keyPath = GetOption(args, "-k") ?? Environment.GetEnvironmentVariable(KeyEnv)
            ?? throw new ArgumentException($"缺少 -k <私钥路径>（或设置环境变量 {KeyEnv}）");
        string? passphrase = GetOption(args, "--passphrase") ?? Environment.GetEnvironmentVariable(PassphraseEnv);

        using var ecdsa = ECDsa.Create();
        ImportPrivateKey(ecdsa, File.ReadAllText(keyPath), passphrase);

        byte[] zipBytes = File.ReadAllBytes(zipPath);
        byte[] signature = ecdsa.SignData(zipBytes, HashAlgorithmName.SHA256);
        string sigPath = zipPath + ".sig";
        File.WriteAllBytes(sigPath, signature);

        Console.WriteLine($"已签名：{sigPath}（{signature.Length} 字节）");
        return 0;
    }

    private static int Verify(string[] args)
    {
        string zipPath = GetPositional(args, 0, "verify <zip> <sig>");
        string sigPath = GetPositional(args, 1, "verify <zip> <sig>");
        string keyPath = GetOption(args, "-k") ?? throw new ArgumentException("缺少 -k <公钥路径>");

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(File.ReadAllText(keyPath));

        bool ok = ecdsa.VerifyData(File.ReadAllBytes(zipPath), File.ReadAllBytes(sigPath), HashAlgorithmName.SHA256);
        Console.WriteLine(ok ? "签名有效" : "签名无效");
        return ok ? 0 : 1;
    }

    private static int Fingerprint(string[] args)
    {
        string keyPath = GetPositional(args, 0, "fingerprint <pub.pem>");
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(File.ReadAllText(keyPath));
        byte[] spki = ecdsa.ExportSubjectPublicKeyInfo();
        byte[] fp = SHA256.HashData(spki);
        Console.WriteLine($"SPKI base64：{Convert.ToBase64String(spki)}");
        Console.WriteLine($"SHA256 指纹：{Convert.ToHexString(fp).ToLowerInvariant()}");
        return 0;
    }

    private static void ImportPrivateKey(ECDsa ecdsa, string pem, string? passphrase)
    {
        if (pem.Contains("ENCRYPTED PRIVATE KEY", StringComparison.Ordinal))
        {
            if (passphrase is null)
                throw new ArgumentException($"私钥已加密，需要 --passphrase（或环境变量 {PassphraseEnv}）");
            ecdsa.ImportFromEncryptedPem(pem, passphrase);
        }
        else
        {
            ecdsa.ImportFromPem(pem);
        }
    }

    private static string GetPositional(string[] args, int index, string usage)
    {
        if (index >= args.Length || args[index].StartsWith('-'))
            throw new ArgumentException($"用法：ReleaseSigner {usage}");
        return args[index];
    }

    private static string? GetOption(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static int PrintUsage()
    {
        Console.WriteLine("""
            ReleaseSigner — TextTool 发布签名工具（离线）

              keygen <outDir> [--passphrase <p>]       生成 ECDsa P-256 密钥对
              sign <zip> -k <priv.pem> [--passphrase]  对 zip 签名，输出 <zip>.sig
              verify <zip> <sig> -k <pub.pem>          验签
              fingerprint <pub.pem>                    打印 SPKI base64（嵌入客户端）
            """);
        return 1;
    }
}
