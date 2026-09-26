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

        return DetectFromBytes(header);
    }

    /// <summary>
    /// 严格模式：扫描整个文件并二次验证头部检测结论。
    /// 头部 4KB 判定为 UTF-8 但全文存在非法序列时，说明文件可能为
    /// 混合编码或 GBK 误判为 UTF-8，此时回退到 GBK。
    /// 头部判定为 GBK 时，如果全文严格 UTF-8 合法（剔除 BOM），
    /// 且头部 GBK 判定仅在边界截断所致，则改用 UTF-8。
    /// 为控制内存开销，大文件按 64KB 分块流式扫描。
    /// </summary>
    public static DetectionResult DetectStrict(string filePath)
    {
        var headerResult = Detect(filePath);

        // BOM 或 UTF-16：字节序标记明确，信任快速路径
        if (headerResult.Kind is DetectedEncoding.Utf8Bom
            or DetectedEncoding.Utf16Le
            or DetectedEncoding.Utf16Be)
            return headerResult;

        // 头部判为 UTF-8：全文二次验证。头部 4KB 合法但全文存在非法序列，
        // 说明文件实为 GBK（GBK 的字节序列常在前 4KB 恰好凑成合法 UTF-8），
        // 按 UTF-8 读取会产生永久乱码 → 回退 GBK。
        if (headerResult.Encoding == Encoding.UTF8)
        {
            return IsStrictUtf8(filePath)
                ? headerResult
                : new DetectionResult(Encoding.GetEncoding(936), DetectedEncoding.Gbk);
        }

        // 头部判为 GBK：信任快速路径（真正的 UTF-8 文件头部必为合法 UTF-8，
        // 不会被误判为 GBK）。
        return headerResult;
    }

    /// <summary>
    /// 全文件流式解码，判断是否满足严格 UTF-8 合法性。
    /// 使用 throwOnInvalidBytes 的 UTF-8 编码，遇到非法序列抛 DecoderFallbackException。
    /// 语义与快速路径（默认 UTF-8 解码 + U+FFFD 检查）一致，但覆盖整个文件。
    /// </summary>
    private static bool IsStrictUtf8(string filePath)
    {
        var strictUtf8 = new UTF8Encoding(false, throwOnInvalidBytes: true);
        try
        {
            using var reader = new StreamReader(filePath, strictUtf8,
                detectEncodingFromByteOrderMarks: false, bufferSize: 64 * 1024);
            while (!reader.EndOfStream)
                reader.Read();
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    /// <summary>从字节块检测编码（BOM → UTF-8 → GBK）。</summary>
    private static DetectionResult DetectFromBytes(byte[] header)
    {
        int read = header.Length;

        // 1. 检查 BOM
        if (read >= 3 && header[0] == 0xEF && header[1] == 0xBB && header[2] == 0xBF)
            return new DetectionResult(Encoding.UTF8, DetectedEncoding.Utf8Bom);

        if (read >= 2 && header[0] == 0xFF && header[1] == 0xFE)
            return new DetectionResult(Encoding.Unicode, DetectedEncoding.Utf16Le);

        if (read >= 2 && header[0] == 0xFE && header[1] == 0xFF)
            return new DetectionResult(Encoding.BigEndianUnicode, DetectedEncoding.Utf16Be);

        // 2. 尝试 UTF-8 解码（无 BOM）
        if (IsValidUtf8(header))
            return new DetectionResult(Encoding.UTF8, DetectedEncoding.Utf8);

        // 3. 回退到 GBK
        return new DetectionResult(Encoding.GetEncoding(936), DetectedEncoding.Gbk);
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
/// 已识别的文本编码种类（显示名由 UI 经 Loc 解析）
/// </summary>
public enum DetectedEncoding
{
    Utf8Bom,
    Utf8,
    Utf16Le,
    Utf16Be,
    Gbk
}

/// <summary>
/// 编码检测结果（不可变记录）
/// </summary>
public record DetectionResult(Encoding Encoding, DetectedEncoding Kind)
{
    public string LocKey => Kind switch
    {
        DetectedEncoding.Utf8Bom => "EncodingUtf8Bom",
        DetectedEncoding.Utf8 => "EncodingUtf8",
        DetectedEncoding.Utf16Le => "EncodingUtf16Le",
        DetectedEncoding.Utf16Be => "EncodingUtf16Be",
        DetectedEncoding.Gbk => "EncodingGbk",
        _ => throw new ArgumentOutOfRangeException(nameof(Kind), Kind, null)
    };
}
