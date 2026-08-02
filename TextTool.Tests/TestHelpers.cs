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
        // 清理 overwrite 模式产生的备份链（对应 BackupHelper 的 MaxHistory=3 + .bak）
        for (int i = 0; i <= 3; i++)
        {
            string bak = Path + (i == 0 ? ".bak" : $".bak.{i}");
            if (File.Exists(bak)) File.Delete(bak);
        }
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
