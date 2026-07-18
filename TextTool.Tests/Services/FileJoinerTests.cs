namespace TextTool.Tests.Services;

public class FileJoinerTests
{
    static FileJoinerTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private static readonly string NL = Environment.NewLine;

    // ================================================================
    //  Basic file joining
    // ================================================================

    [Fact]
    public void Join_SimpleFiles_ConcatenatesInOrder()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "b.txt"), "World");
        File.WriteAllText(Path.Combine(dir.Path, "a.txt"), "Hello");

        var (outputPath, fileCount) = FileJoiner.Join(dir.Path, "*.txt", "combined.txt");

        Assert.Equal(2, fileCount);
        Assert.True(File.Exists(outputPath));
        var content = File.ReadAllText(outputPath, Encoding.UTF8);
        Assert.Equal($"Hello{NL}World{NL}", content);
    }

    [Fact]
    public void Join_SingleFile_ReturnsItsContent()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "test.txt"), "only file");

        var (outputPath, fileCount) = FileJoiner.Join(dir.Path, "*.txt", "result.txt");

        Assert.Equal(1, fileCount);
        Assert.Equal($"only file{NL}", File.ReadAllText(outputPath, Encoding.UTF8));
    }

    // ================================================================
    //  Edge cases
    // ================================================================

    [Fact]
    public void Join_NoMatchingFiles_ReturnsEmptyOutput()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "test.dat"), "data");

        var (outputPath, fileCount) = FileJoiner.Join(dir.Path, "*.txt", "result.txt");

        Assert.Equal(0, fileCount);
        Assert.True(File.Exists(outputPath));
        Assert.Empty(File.ReadAllText(outputPath, Encoding.UTF8));
    }

    [Fact]
    public void Join_FilesWithNewlines_AddsNewlineSeparatorCorrectly()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "a.txt"), $"Hello{NL}");
        File.WriteAllText(Path.Combine(dir.Path, "b.txt"), "World");

        var (outputPath, fileCount) = FileJoiner.Join(dir.Path, "*.txt", "combined.txt");
        var content = File.ReadAllText(outputPath, Encoding.UTF8);

        Assert.Equal($"Hello{NL}World{NL}", content);
    }

    // ================================================================
    //  Different encodings
    // ================================================================

    [Fact]
    public void Join_DifferentEncodings_MergesIntoUtf8Bom()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "gbk.txt"), "你好", Encoding.GetEncoding(936));
        File.WriteAllText(Path.Combine(dir.Path, "utf8.txt"), "世界", Encoding.UTF8);

        var (outputPath, _) = FileJoiner.Join(dir.Path, "*.txt", "merged.txt");
        var bytes = File.ReadAllBytes(outputPath);

        // Check BOM present
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        // Content readable as UTF-8
        var content = Encoding.UTF8.GetString(bytes);
        Assert.Contains("你好", content);
        Assert.Contains("世界", content);
    }

    // Helpers are in TestHelpers.cs (shared TempFile / TempDir)
}
