namespace TextTool.Services;

/// <summary>
/// lint 的输入整理：把命令行给的文件与目录摊平成待检文件清单。
///
/// 目录递归收 .txt 与 .md，同一个目录内部按相对路径排序，分隔符统一写成 /，
/// Linux 与 Windows 上得到同一个顺序；软链接跟到真实目标，同一份稿只留一次，
/// 指回上级的链接不会把一棵树重复展开。子目录读不了时报出路径接着展开其余部分。
/// 一个目录里一个都收不到就报用法错误，并且说清是哪个目录，
/// 「路径写错」和「目录里没有稿子」要能分开。
/// 传 - 表示 stdin，原样留给调用方去读。
/// </summary>
public static class LintInputs
{
    /// <summary>目录展开认的后缀，大小写不敏感，.MD 与 .Md 一样收。</summary>
    private static readonly string[] Extensions = { ".txt", ".md" };

    /// <summary>同一份稿的判定口径：Windows 上路径不分大小写，其它平台分。</summary>
    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>
    /// 展开输入：目录换成它下面所有 .txt 与 .md，文件与 - 直接放进清单。
    /// 整体顺序按传入顺序，同一个目录内部的顺序按相对路径排序。
    /// onWarning 收到读不了的子目录（中文说明带路径），调用方打到 stderr 即可；
    /// 不给就当作静默跳过。
    /// </summary>
    public static List<string> Collect(IReadOnlyList<string> inputs, Action<string>? onWarning = null)
    {
        var files = new List<string>();
        var seen = new HashSet<string>(PathComparer);
        foreach (string input in inputs)
        {
            // stdin 原样透传：连给两次就是读两次，这里不去重
            if (input == "-")
            {
                files.Add(input);
                continue;
            }

            // 不是目录的东西都当文件处理：真不存在时由读取那一步报「文件不存在」
            if (!Directory.Exists(input))
            {
                if (seen.Add(RealPath(input)))
                    files.Add(input);
                continue;
            }

            var expanded = Expand(input, onWarning);
            if (expanded.Count == 0)
                throw new ArgumentException($"目录下没有 .txt 或 .md 文件：{input}");

            // 去重按真实路径：同一个目录传两次、软链接与真身各给一次，同一份稿都只检一遍
            foreach (var entry in expanded)
                if (seen.Add(entry.Real))
                    files.Add(entry.File);
        }
        return files;
    }

    /// <summary>展开出来的一个文件：原路径、真实路径、排序键。</summary>
    private sealed record Entry(string File, string Real, string SortKey);

    /// <summary>
    /// 展开一个目录，按相对路径排序后返回。
    /// 走过的真实目录记下来，链接绕回上级时不会无限展开。
    /// </summary>
    private static List<Entry> Expand(string root, Action<string>? onWarning)
    {
        var found = new List<Entry>();
        var walked = new HashSet<string>(PathComparer);
        Walk(root, root, found, walked, onWarning);
        return found.OrderBy(entry => entry.SortKey, StringComparer.Ordinal).ToList();
    }

    private static void Walk(
        string root, string dir, List<Entry> found, HashSet<string> walked, Action<string>? onWarning)
    {
        // 走过的真实目录不再走：软链接指回上级时，一棵树会被展开成无限层
        if (!walked.Add(RealPath(dir)))
            return;

        try
        {
            foreach (string entry in Directory.EnumerateFileSystemEntries(dir))
            {
                if (Directory.Exists(entry))
                {
                    Walk(root, entry, found, walked, onWarning);
                    continue;
                }

                if (!Extensions.Contains(Path.GetExtension(entry), StringComparer.OrdinalIgnoreCase))
                    continue;

                found.Add(new Entry(entry, RealPath(entry), SortKey(root, entry)));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 这一段读不了就跳过，别的输入还得继续查：把路径和原因交出去，由调用方决定怎么显示
            onWarning?.Invoke($"读不了目录，跳过：{dir}（{Reason(ex)}）");
        }
    }

    /// <summary>
    /// 读不了的原因：权限不足、目录不存在这两种用中文说，命令行其余输出都是中文，
    /// 这里不自成一段外文。
    /// </summary>
    private static string Reason(Exception ex) => ex switch
    {
        UnauthorizedAccessException => "没有访问权限",
        DirectoryNotFoundException => "目录不存在",
        _ => ex.Message,
    };

    /// <summary>
    /// 排序键：相对 root 的路径，分隔符统一写成 /。
    /// 直接按原路径排会分平台走两套顺序——'/' 排在字母前面，'\' 排在字母后面，
    /// 同一个目录里既有子目录又有大写开头的文件时两边顺序相反。
    /// </summary>
    private static string SortKey(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/');

    /// <summary>
    /// 解析到真实目标：路径中间哪一段是软链接都跟过去，链上串了几层也跟到底。
    /// 解析不了（链接断掉、没权限）就按原路径返回，由后续去重与读取判定处理。
    /// </summary>
    private static string RealPath(string path)
    {
        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }

        string root = Path.GetPathRoot(full) ?? "";
        string current = root;
        string[] parts = full[root.Length..].Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);
        foreach (string part in parts)
        {
            current = Path.Combine(current, part);
            try
            {
                FileSystemInfo info = Directory.Exists(current)
                    ? new DirectoryInfo(current)
                    : new FileInfo(current);
                if (info.LinkTarget is not null
                    && info.ResolveLinkTarget(returnFinalTarget: true) is { } target)
                    current = target.FullName;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 取不到链接目标就按这一段原样拼：宁可多检一遍，也不能把稿漏掉
            }
        }
        return current;
    }
}
