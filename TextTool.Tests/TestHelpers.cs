namespace TextTool.Tests;

/// <summary>
/// 临时文件夹具，自动清理自身及 _Processed 变体。
/// </summary>
public sealed class TempFile : IDisposable
{
    public string Path { get; } = System.IO.Path.GetTempFileName() + ".txt";

    public void Dispose()
    {
        if (File.Exists(Path)) File.Delete(Path);
        var processed = System.IO.Path.ChangeExtension(Path, null) + "_Processed.txt";
        if (File.Exists(processed)) File.Delete(processed);
    }
}

/// <summary>
/// 临时目录夹具，自动递归清理。
/// </summary>
public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TT_" + Guid.NewGuid());
    public TempDir() => Directory.CreateDirectory(Path);
    public void Dispose()
    {
        if (Directory.Exists(Path)) Directory.Delete(Path, true);
    }
}
