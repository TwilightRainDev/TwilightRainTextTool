namespace TextTool.Tests.Scripts;

/// <summary>
/// 解析测试要调用的 bash：先按 PATH 逐段探测，探不到再试几个固定位置。
/// 发布脚本是 bash，测试要在 Windows 与 Linux 上都跑得起来。CI 在 windows-latest 上，
/// bash 不保证进了 PATH（GitHub Actions 的 shell: bash 会临时把 Git Bash 塞进去，
/// 但测试是独立进程，看不到那层包装），而 Git for Windows 一定装在下面那些固定目录里，
/// 所以 PATH 落空后按固定位置补探，保证同一份测试在两种系统上都能起 bash。
/// </summary>
internal static class BashLocator
{
    private static readonly string[] PreferredNames = { "bash", "bash.exe" };

    private static readonly string[] FallbackPaths =
    {
        "/bin/bash",
        "/usr/bin/bash",
        @"C:\Program Files\Git\bin\bash.exe",
        @"C:\Program Files\Git\usr\bin\bash.exe",
    };

    public static string Resolve()
    {
        foreach (var name in PreferredNames)
        {
            var found = FindOnPath(name);
            if (found is not null)
                return found;
        }

        foreach (var path in FallbackPaths)
        {
            if (File.Exists(path))
                return path;
        }

        // 一个都没探到：交给进程启动按 PATH / 系统默认解析。
        return "bash";
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
