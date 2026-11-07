using System.Diagnostics;

namespace TextTool.Tests.Services;

/// <summary>
/// 批量命令（merge / replace / vn）的收尾口径：
/// - 两份都处理成功时退 0，每份报一行 [成功]；
/// - 有的一份失败时退 1，那一行写成「[失败] 文件名：原因」，原因不能省。
///
/// 退出码按进程退出码看；错误文本只能在同一处看——Main 把 Console.Error 接到进程标准错误上，
/// 进程内重定向取不到。整组走真实进程，也就不必改全局 Console.Out，不会与并发跑的用例互相踩。
///
/// [成功] 行里文件名后面还有输出路径，所以断的是「这一行包含文件名」，不逐字相等。
/// </summary>
public class CliBatchTests
{
    /// <summary>
    /// 三个批量命令，各自带一份保证跑得通的参数。replace 走内置方案：换台机器、那份替换规则表没配规则时，
    /// 不带方案的 replace 一条规则都没有，直接报错退出。
    /// </summary>
    public static IEnumerable<object[]> 三个批量命令 => new[]
    {
        new object[] { new[] { "merge" } },
        new object[] { new[] { "replace", "--scheme", "标点符号转换" } },
        new object[] { new[] { "vn" } },
    };

    private static void 写(string path, string text) =>
        File.WriteAllText(path, text, new UTF8Encoding(false));

    [Theory]
    [MemberData(nameof(三个批量命令))]
    public void 整批成功时退零且每份报一行成功(string[] command)
    {
        using var dir = new TempDir();
        string 甲 = Path.Combine(dir.Path, "甲.txt");
        string 乙 = Path.Combine(dir.Path, "乙.txt");
        写(甲, "第一行。\n第二行。\n");
        写(乙, "第三行。\n第四行。\n");

        var run = RunCli(command.Concat(new[] { 甲, 乙 }).ToArray());

        var 成功 = 成功行(run.Stdout);
        Assert.True(run.Code == 0,
            $"两份都处理成功时退出码该是 0，实际 {run.Code}。标准输出：{run.Stdout}；标准错误：{run.Stderr}");
        Assert.True(成功.Count == 2,
            $"两份都成功该有两行 [成功]，实际 {成功.Count} 行。标准输出：{run.Stdout}");
        Assert.Contains(成功, line => line.Contains("甲.txt", StringComparison.Ordinal));
        Assert.Contains(成功, line => line.Contains("乙.txt", StringComparison.Ordinal));
        Assert.DoesNotContain("[失败]", run.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void 有文件失败时退一且那行带文件名与原因()
    {
        using var dir = new TempDir();
        string 坏 = Path.Combine(dir.Path, "写不进去.txt");
        string 好 = Path.Combine(dir.Path, "正常.txt");
        写(坏, "第一行。\n");
        写(好, "第二行。\n");
        // 输出落点被同名目录占住：这一份写不进去，另一份照常处理
        Directory.CreateDirectory(Path.Combine(dir.Path, "写不进去_Processed.txt"));

        var run = RunCli("merge", 坏, 好);

        Assert.True(run.Code == 1, $"有一份失败时退出码该是 1，实际 {run.Code}。标准错误：{run.Stderr}");
        string 失败 = Assert.Single(失败行(run.Stderr));

        // 形状是「[失败] 文件名：原因」。只有文件名、没有原因的那行，读者拿不到要改什么。
        // 带路径时印的是完整路径，所以断「以文件名结尾」，不逐字相等。
        int 冒号 = 失败.IndexOf('：');
        Assert.True(冒号 > 0, $"失败行该写成「[失败] 文件名：原因」，实际：{失败}");
        Assert.True(失败["[失败] ".Length..冒号].EndsWith("写不进去.txt", StringComparison.Ordinal),
            $"失败行得点名是哪一份，实际：{失败}");

        string 原因 = 失败[(冒号 + 1)..].Trim();
        Assert.True(原因.Length > 0, $"失败行只有文件名，没写原因：{失败}");
        Assert.True(原因 != "写不进去.txt", $"失败行拿文件名当了原因：{失败}");
    }

    /// <summary>[成功] 行，去掉行尾 CR；只认行首的标记，其它输出不掺进来。</summary>
    private static List<string> 成功行(string stdout) =>
        stdout.Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.StartsWith("[成功] ", StringComparison.Ordinal))
            .ToList();

    private static List<string> 失败行(string stderr) =>
        stderr.Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.StartsWith("[失败] ", StringComparison.Ordinal))
            .ToList();

    /// <summary>
    /// 命令行的 dll，与测试同一配置、同一框架。测试项目引用命令行项目，构建测试就顺带产出了它。
    /// </summary>
    private static string CliDll
    {
        get
        {
            // .../TextTool.Tests/bin/<配置>/net8.0/，上四级是仓库根，配置名取自 bin 的下一层
            var here = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
            string config = here.Parent!.Name;
            string root = here.Parent!.Parent!.Parent!.Parent!.FullName;
            return Path.Combine(root, "TextTool.Cli", "bin", config, "net8.0", "texttool.dll");
        }
    }

    private static (int Code, string Stdout, string Stderr) RunCli(params string[] args)
    {
        string dll = CliDll;
        Assert.True(File.Exists(dll), $"先 build 命令行项目：{dll}");

        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
            UseShellExecute = false,
        };
        start.ArgumentList.Add(dll);
        foreach (string arg in args)
            start.ArgumentList.Add(arg);

        using var process = Process.Start(start)!;
        // 两条流都异步读：只同步读一条，另一条写满管道缓冲就会把子进程卡住
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return (process.ExitCode, stdout.Result, stderr.Result);
    }
}
