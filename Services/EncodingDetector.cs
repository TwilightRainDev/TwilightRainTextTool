using System.Text;

namespace TextTool.Services;

/// <summary>
/// 自动检测文本文件编码（GBK / UTF-8 / UTF-16）
/// </summary>
public static class EncodingDetector
{
    /// <summary>
    /// 检测文件编码。优先级：BOM > UTF-8 合法性 > GBK
    /// </summary>
    public static DetectionResult Detect(string filePath)
    {
        byte[] bytes = File.ReadAllBytes(filePath);

        // 1. 检查 BOM
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return new DetectionResult(Encoding.UTF8, "UTF-8 (BOM)");

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return new DetectionResult(Encoding.Unicode, "UTF-16 LE");

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return new DetectionResult(Encoding.BigEndianUnicode, "UTF-16 BE");

        // 2. 尝试 UTF-8 解码（无 BOM）
        bool utf8Valid = TryDecodeUtf8(bytes, out _);
        if (utf8Valid)
        {
            // 进一步判断：如果大部分是多字节序列，偏向 UTF-8
            // 否则对纯 ASCII 也标为 UTF-8（现代文本编辑器默认）
            return new DetectionResult(Encoding.UTF8, "UTF-8");
        }

        // 3. 回退到 GBK
        return new DetectionResult(Encoding.GetEncoding(936), "GBK (ANSI)");
    }

    /// <summary>
    /// 尝试验证 UTF-8 解码是否无错误
    /// </summary>
    private static bool TryDecodeUtf8(byte[] bytes, out string text)
    {
        try
        {
            text = Encoding.UTF8.GetString(bytes);
            // � = replacement character，说明有无效序列
            return !text.Contains('�');
        }
        catch
        {
            text = string.Empty;
            return false;
        }
    }
}

public class DetectionResult
{
    public Encoding Encoding { get; }
    public string DisplayName { get; }

    public DetectionResult(Encoding encoding, string displayName)
    {
        Encoding = encoding;
        DisplayName = displayName;
    }
}
