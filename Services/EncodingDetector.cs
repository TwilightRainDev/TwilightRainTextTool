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
    ///
    /// 注意：FileStream 在 ProbeSize 边界可能截断多字节字符。
    /// 解码前先修剪末尾不完整的 UTF-8 序列，避免误判。
    /// </summary>
    private static bool IsValidUtf8(byte[] bytes)
    {
        // 修剪末尾不完整的 UTF-8 序列
        int length = bytes.Length;
        length = TrimTrailingIncompleteUtf8(bytes, length);

        try
        {
            string text = Encoding.UTF8.GetString(bytes, 0, length);
            // � = replacement character，说明有无效序列
            return !text.Contains('�');
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 从末尾向前扫描，去掉不完整的多字节 UTF-8 尾部。
    /// 例如：若末尾 3 字节是 [e5 86]（缺少第三个字节），则截断到该序列之前。
    /// </summary>
    private static int TrimTrailingIncompleteUtf8(byte[] bytes, int length)
    {
        if (length == 0) return length;

        // 从末尾找到第一个非 continuation byte (10xxxxxx) 的位置
        int i = length - 1;
        while (i >= 0 && (bytes[i] & 0xC0) == 0x80) // 10xxxxxx
            i--;

        if (i < 0)
        {
            // 全是 continuation byte — 全部截断
            return 0;
        }

        // 检查从 i 开始的序列需要几个 continuation byte
        int expectedTotal;
        if ((bytes[i] & 0x80) == 0)          // 0xxxxxxx → ASCII，1 byte
            expectedTotal = 1;
        else if ((bytes[i] & 0xE0) == 0xC0)  // 110xxxxx → 2 bytes
            expectedTotal = 2;
        else if ((bytes[i] & 0xF0) == 0xE0)  // 1110xxxx → 3 bytes
            expectedTotal = 3;
        else if ((bytes[i] & 0xF8) == 0xF0)  // 11110xxx → 4 bytes
            expectedTotal = 4;
        else
            expectedTotal = 1; // 无效起始字节，保留（解码时会正常产生 U+FFFD）

        int actualSegmentLen = length - i;
        if (actualSegmentLen < expectedTotal)
            return i; // 序列不完整，截断

        return length; // 序列完整，保留
    }
}

/// <summary>
/// 编码检测结果（不可变记录）
/// </summary>
public record DetectionResult(Encoding Encoding, string DisplayName);
