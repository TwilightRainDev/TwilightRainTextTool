using System.Text;

namespace TextTool.Services;

/// <summary>
/// 自动检测文本文件编码（GBK / UTF-8 / UTF-16）
/// </summary>
public static class EncodingDetector
{
    // 编码检测只需扫描文件头部即可可靠判断
    private const int ProbeSize = 4096;

    /// <summary>
    /// 检测文件编码。优先级：BOM > UTF-8 合法性 > GBK
    /// 只读取文件头部以提升大文件性能。
    /// </summary>
    public static DetectionResult Detect(string filePath)
    {
        byte[] header = new byte[ProbeSize];
        int read;
        using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
        {
            read = fs.Read(header, 0, ProbeSize);
        }

        // 截取实际读取到的长度
        if (read < header.Length)
            Array.Resize(ref header, read);

        // 1. 检查 BOM
        if (read >= 3 && header[0] == 0xEF && header[1] == 0xBB && header[2] == 0xBF)
            return new DetectionResult(Encoding.UTF8, "UTF-8 (BOM)");

        if (read >= 2 && header[0] == 0xFF && header[1] == 0xFE)
            return new DetectionResult(Encoding.Unicode, "UTF-16 LE");

        if (read >= 2 && header[0] == 0xFE && header[1] == 0xFF)
            return new DetectionResult(Encoding.BigEndianUnicode, "UTF-16 BE");

        // 2. 尝试 UTF-8 解码（无 BOM）
        if (IsValidUtf8(header))
            return new DetectionResult(Encoding.UTF8, "UTF-8");

        // 3. 回退到 GBK
        return new DetectionResult(Encoding.GetEncoding(936), "GBK (ANSI)");
    }

    /// <summary>
    /// 尝试验证 UTF-8 解码是否无错误（只检查头部）。
    /// </summary>
    private static bool IsValidUtf8(byte[] bytes)
    {
        try
        {
            string text = Encoding.UTF8.GetString(bytes);
            // � = replacement character，说明有无效序列
            return !text.Contains('�');
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// 编码检测结果（不可变记录）
/// </summary>
public record DetectionResult(Encoding Encoding, string DisplayName);
