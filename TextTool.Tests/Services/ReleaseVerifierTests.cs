using System.Security.Cryptography;
using System.Text;

namespace TextTool.Tests.Services;

public class ReleaseVerifierTests
{
    private static (byte[] Zip, byte[] Signature, string SpkiBase64) CreateSignedZip()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] zip = Encoding.UTF8.GetBytes("fake zip content for signature test");
        byte[] sig = ecdsa.SignData(zip, HashAlgorithmName.SHA256);
        return (zip, sig, Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo()));
    }

    [Fact]
    public void VerifyZipSignature_ValidSignature_ReturnsTrue()
    {
        var (zip, sig, spki) = CreateSignedZip();
        Assert.True(ReleaseVerifier.VerifyZipSignature(zip, sig, spki));
    }

    [Fact]
    public void VerifyZipSignature_TamperedZip_ReturnsFalse()
    {
        var (zip, sig, spki) = CreateSignedZip();
        zip[0] ^= 0xFF;
        Assert.False(ReleaseVerifier.VerifyZipSignature(zip, sig, spki));
    }

    [Fact]
    public void VerifyZipSignature_WrongKey_ReturnsFalse()
    {
        var (zip, sig, spki) = CreateSignedZip();
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string otherSpki = Convert.ToBase64String(other.ExportSubjectPublicKeyInfo());
        Assert.NotEqual(spki, otherSpki);
        Assert.False(ReleaseVerifier.VerifyZipSignature(zip, sig, otherSpki));
    }

    [Theory]
    [InlineData(new byte[] { })]                       // 空签名
    [InlineData(new byte[] { 0x30, 0x03, 0x02, 0x01 })] // 截断的 DER
    [InlineData(new byte[] { 0x01, 0x02, 0x03 })]       // 任意垃圾
    public void VerifyZipSignature_MalformedSignature_ReturnsFalse(byte[] sig)
    {
        var (zip, _, spki) = CreateSignedZip();
        Assert.False(ReleaseVerifier.VerifyZipSignature(zip, sig, spki));
    }

    [Fact]
    public void VerifyZipSignature_DefaultKey_RejectsRandomSignature()
    {
        // 不注入测试密钥：用内嵌生产公钥验证随机签名必然失败（生产公钥格式有效）
        byte[] zip = Encoding.UTF8.GetBytes("payload");
        Assert.False(ReleaseVerifier.VerifyZipSignature(zip, new byte[] { 0x30, 0x06, 0x02, 0x01, 0x00, 0x02, 0x01, 0x00 }));
    }
}
