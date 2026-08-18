using TextTool.Services;

namespace TextTool.Tests.Services;

/// <summary>
/// JsonFileStore 错误策略测试（ADR-009 单目录哲学）：
/// 文件缺失 = 首次使用（返回 fallback/空列表）；
/// 文件损坏 = 显式抛错，绝不静默降级为无规则运行。
/// </summary>
public class JsonFileStoreTests
{
    [Fact]
    public void Load_MissingFile_ReturnsFallback()
    {
        using var dir = new TempDir();
        Assert.Empty(JsonFileStore.Load<ReplaceRule>("no_such.json", baseDir: dir.Path));
    }

    [Fact]
    public void Load_CorruptFile_ThrowsInvalidDataException()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "rules.json"), "{broken json");

        Assert.Throws<InvalidDataException>(() =>
            JsonFileStore.Load<ReplaceRule>("rules.json", baseDir: dir.Path));
    }

    [Fact]
    public void SaveThenLoad_RoundTrips_NoTempLeftBehind()
    {
        using var dir = new TempDir();
        JsonFileStore.Save("rules.json", new List<ReplaceRule>
        {
            new() { Find = "「", Replace = "「" },
            new() { Find = "A", Replace = "B" },
        }, baseDir: dir.Path);

        var loaded = JsonFileStore.Load<ReplaceRule>("rules.json", baseDir: dir.Path);
        Assert.Equal(2, loaded.Count);
        Assert.Equal("「", loaded[0].Find);
        // 原子写入不应残留临时文件
        Assert.Empty(Directory.GetFiles(dir.Path, "*.tmp"));
    }
}
