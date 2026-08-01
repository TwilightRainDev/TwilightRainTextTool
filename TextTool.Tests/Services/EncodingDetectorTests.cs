namespace TextTool.Tests.Services;

public class EncodingDetectorTests
{
    // Register GBK support once per test class
    static EncodingDetectorTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    // ================================================================
    //  BOM detection
    // ================================================================

    [Fact]
    public void Detect_Utf8Bom_ReturnsUtf8WithBom()
    {
        using var tf = new TempFile();
        File.WriteAllBytes(tf.Path, new byte[] { 0xEF, 0xBB, 0xBF, 0x68, 0x65, 0x6C, 0x6C, 0x6F });
        var result = EncodingDetector.Detect(tf.Path);

        Assert.Equal("UTF-8 (BOM)", result.DisplayName);
        Assert.Equal(Encoding.UTF8, result.Encoding);
    }

    [Fact]
    public void Detect_Utf16LeBom_ReturnsUnicode()
    {
        using var tf = new TempFile();
        File.WriteAllBytes(tf.Path, new byte[] { 0xFF, 0xFE, 0x68, 0x00, 0x65, 0x00 });
        var result = EncodingDetector.Detect(tf.Path);

        Assert.Equal("UTF-16 LE", result.DisplayName);
    }

    [Fact]
    public void Detect_Utf16BeBom_ReturnsBigEndianUnicode()
    {
        using var tf = new TempFile();
        File.WriteAllBytes(tf.Path, new byte[] { 0xFE, 0xFF, 0x00, 0x68, 0x00, 0x65 });
        var result = EncodingDetector.Detect(tf.Path);

        Assert.Equal("UTF-16 BE", result.DisplayName);
    }

    // ================================================================
    //  UTF-8 detection (no BOM) — write raw bytes to avoid WriteAllText BOM
    // ================================================================

    [Fact]
    public void Detect_ValidUtf8NoBom_ReturnsUtf8()
    {
        using var tf = new TempFile();
        byte[] utf8NoBom = { 0x48, 0x65, 0x6C, 0x6C, 0x6F,  // "Hello"
                             0xE4, 0xB8, 0x96, 0xE7, 0x95, 0x8C }; // "世界"
        File.WriteAllBytes(tf.Path, utf8NoBom);
        var result = EncodingDetector.Detect(tf.Path);

        Assert.Equal("UTF-8", result.DisplayName);
        Assert.Equal(Encoding.UTF8, result.Encoding);
    }

    [Fact]
    public void Detect_AsciiOnly_ReturnsUtf8()
    {
        using var tf = new TempFile();
        File.WriteAllBytes(tf.Path, new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F }); // "Hello"
        var result = EncodingDetector.Detect(tf.Path);

        Assert.Equal("UTF-8", result.DisplayName);
    }

    // ================================================================
    //  GBK fallback
    // ================================================================

    [Fact]
    public void Detect_GbkContent_ReturnsGbk()
    {
        using var tf = new TempFile();
        // GBK encoded "你好" (0xC4 0xE3 0xBA 0xC3)
        byte[] gbkBytes = { 0xC4, 0xE3, 0xBA, 0xC3 };
        File.WriteAllBytes(tf.Path, gbkBytes);
        var result = EncodingDetector.Detect(tf.Path);

        Assert.Equal("GBK (ANSI)", result.DisplayName);
        Assert.Equal(Encoding.GetEncoding(936), result.Encoding);
    }

    // ================================================================
    //  Edge cases
    // ================================================================

    [Fact]
    public void Detect_EmptyFile_ReturnsUtf8()
    {
        using var tf = new TempFile();
        File.WriteAllBytes(tf.Path, Array.Empty<byte>());
        var result = EncodingDetector.Detect(tf.Path);

        Assert.NotNull(result);
        Assert.Equal(Encoding.UTF8, result.Encoding);
    }

    [Fact]
    public void Detect_LargeAsciiFile_FitsInProbe_Ok()
    {
        using var tf = new TempFile();
        var content = new string('a', 10000); // 10KB of ASCII
        // Write without BOM
        File.WriteAllBytes(tf.Path, Encoding.UTF8.GetBytes(content));
        var result = EncodingDetector.Detect(tf.Path);

        Assert.Equal("UTF-8", result.DisplayName);
    }

    [Fact]
    public void Detect_Utf8SequenceSplitAtProbeBoundary_HandlesGracefully()
    {
        using var tf = new TempFile();
        var bytes = new List<byte>();
        bytes.AddRange(Enumerable.Repeat((byte)0x61, 4094)); // 4094 'a's
        bytes.Add(0xE4); // Start of 3-byte UTF-8 char (U+4E00 "一")
        bytes.Add(0xB8); // continuation byte
        // The 4096th byte would be 0x80, but cutoff at 4095 — boundary split!
        File.WriteAllBytes(tf.Path, bytes.ToArray());
        var result = EncodingDetector.Detect(tf.Path);

        // Should not crash, trimmed incomplete seq → valid UTF-8
        Assert.Equal("UTF-8", result.DisplayName);
    }

    // ================================================================
    //  DetectStrict — 全文件二次验证
    // ================================================================

    [Fact]
    public void DetectStrict_LargeGbkWithUtf8Header_ReturnsGbk()
    {
        using var tf = new TempFile();
        // 前 5000 字节 ASCII（头部 4KB 完全合法 UTF-8）→ 快速路径误判 UTF-8
        var bytes = new List<byte>(Enumerable.Repeat((byte)0x61, 5000));
        // 尾部 0x81 0x40：合法 GBK 字符，但 0x81 是非法 UTF-8 起始字节（裸续接）
        bytes.Add(0x81);
        bytes.Add(0x40);
        File.WriteAllBytes(tf.Path, bytes.ToArray());

        Assert.Equal("UTF-8", EncodingDetector.Detect(tf.Path).DisplayName);
        Assert.Equal("GBK (ANSI)", EncodingDetector.DetectStrict(tf.Path).DisplayName);
    }

    [Fact]
    public void DetectStrict_LargeValidUtf8_ReturnsUtf8()
    {
        using var tf = new TempFile();
        // 5KB ASCII + 一个中文 UTF-8 序列（跨越快速路径之外）
        var bytes = new List<byte>(Enumerable.Repeat((byte)0x61, 5000));
        bytes.AddRange(new byte[] { 0xE4, 0xB8, 0x96, 0xE7, 0x95, 0x8C }); // "世界"
        File.WriteAllBytes(tf.Path, bytes.ToArray());

        Assert.Equal("UTF-8", EncodingDetector.DetectStrict(tf.Path).DisplayName);
    }

    [Fact]
    public void DetectStrict_Utf8Bom_TrustsQuickPath()
    {
        using var tf = new TempFile();
        File.WriteAllBytes(tf.Path, new byte[] { 0xEF, 0xBB, 0xBF, 0x68, 0x65, 0x6C, 0x6C, 0x6F });
        var result = EncodingDetector.DetectStrict(tf.Path);

        Assert.Equal("UTF-8 (BOM)", result.DisplayName);
    }

    [Fact]
    public void DetectStrict_Utf8SeqSplitAcrossBlockBoundary_IsValid()
    {
        using var tf = new TempFile();
        // 构造一个 3 字节 UTF-8 序列恰好跨越 64KB 块边界：前块末尾放起始字节，
        // 后块开头放续接字节。
        var bytes = new List<byte>(Enumerable.Repeat((byte)0x61, 64 * 1024 - 1));
        bytes.Add(0xE4); // 起始字节在块末尾
        bytes.Add(0xB8); // 续接在下一块
        bytes.Add(0x96); // 续接在下一块
        File.WriteAllBytes(tf.Path, bytes.ToArray());

        Assert.Equal("UTF-8", EncodingDetector.DetectStrict(tf.Path).DisplayName);
    }

    [Fact]
    public void DetectStrict_TruncatedUtf8AtEof_ReturnsGbk()
    {
        using var tf = new TempFile();
        // 5KB ASCII + 末尾孤立的起始字节（不完整 UTF-8 序列）
        var bytes = new List<byte>(Enumerable.Repeat((byte)0x61, 5000));
        bytes.Add(0xE4); // 缺少两个续接字节
        File.WriteAllBytes(tf.Path, bytes.ToArray());

        Assert.Equal("UTF-8", EncodingDetector.Detect(tf.Path).DisplayName);
        Assert.Equal("GBK (ANSI)", EncodingDetector.DetectStrict(tf.Path).DisplayName);
    }

    // ================================================================
    //  Helper
    // ================================================================

    private sealed class TempFile : IDisposable
    {
        public string Path { get; } = System.IO.Path.GetTempFileName() + ".bin";
        public void Dispose()
        {
            if (File.Exists(Path)) File.Delete(Path);
        }
    }
}
