namespace TextTool.Services;

/// <summary>
/// 本地记录的"最近一次成功安装的版本"——用于检测更新链降级重放
/// （中间人回放官方旧版安装包，把用户永久钉死在旧版本）。
/// 记录文件位于程序目录（单目录哲学，见 ADR-009）；
/// 程序目录不可写时降级为"无记录"，不阻断更新本身。
/// </summary>
public static class LastKnownVersion
{
    private const string FileName = "last_known_version.txt";

    /// <summary>读取记录；无记录或读失败返回 null。</summary>
    public static string? Read(string? baseDir = null)
    {
        string path = Path.Combine(baseDir ?? AppContext.BaseDirectory, FileName);
        try
        {
            if (!File.Exists(path)) return null;
            string v = File.ReadAllText(path).Trim();
            return v.Length == 0 ? null : v;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    /// <summary>写入记录（原子替换，防崩溃留半写文件）。</summary>
    public static void Write(string version, string? baseDir = null)
    {
        string path = Path.Combine(baseDir ?? AppContext.BaseDirectory, FileName);
        try
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, version);
            File.Move(tmp, path, overwrite: true);
        }
        catch (IOException) { /* 程序目录不可写时降级为无记录，不阻断更新 */ }
        catch (UnauthorizedAccessException) { }
    }
}
