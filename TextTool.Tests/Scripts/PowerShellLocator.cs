namespace TextTool.Tests.Scripts;

/// <summary>
/// 解析测试要调用的 PowerShell：优先 PowerShell 7（pwsh），找不到再回落 Windows PowerShell。
/// 只按 PATH 逐段探测，不写死安装目录，保证换机器 / 换盘符仍可用。
/// </summary>
internal static class PowerShellLocator
{
    private static readonly string[] PreferredNames = { "pwsh", "pwsh.exe", "powershell", "powershell.exe" };

    public static string Resolve()
    {
        foreach (var name in PreferredNames)
        {
            var found = FindOnPath(name);
            if (found is not null)
                return found;
        }

        // 一个都没探到：交给进程启动按 PATH / 系统默认解析。
        return "powershell.exe";
    }

    private static string? FindOnPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
            return null;

        foreach (var segment in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(segment))
                continue;

            var candidate = Path.Combine(segment, fileName);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }
}
