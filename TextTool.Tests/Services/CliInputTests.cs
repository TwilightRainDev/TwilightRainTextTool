using System.Diagnostics;
using System.Text;
using TextTool.Services;

namespace TextTool.Tests.Services;

/// <summary>
/// 命令行的输入与批量语义：
/// - lint 的输入既收文件也收目录，目录递归收 .txt 与 .md、按相对路径排序；
/// - 同一份稿只检一次：同一个目录传两次、软链接绕回上级都不会重复展开；
/// - 目录里一个可检文件都没有按用法错误退 2，并点名那个目录；子目录读不了时点名路径接着跑；
/// - lint 一份稿读不了不打断整批，跑完按读取错误退 2；
/// - merge / replace / vn 逐个文件跑，单个失败不中断，失败原因带文件名进 stderr，跑完有失败退 1；
/// - 写盘失败后目录里不留 .tmp。
///
/// 退出码与 stderr 只能在真实进程里看：Console.Error 由 Main 接管到进程标准错误上。
/// </summary>
public class CliInputTests
{
    private static void 写入(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, new UTF8Encoding(false));
    }

    /// <summary>
    /// 把路径的权限收到一点不剩，用来造「读不了」的场景。
    /// 提权进程能读到任何东西，Windows 上没有对应的位，两种情况下返回 false，调用方跳过这条。
    /// </summary>
    private static bool 收紧权限(string path)
    {
        if (OperatingSystem.IsWindows() || Environment.IsPrivilegedProcess)
            return false;
        try
        {
            File.SetUnixFileMode(path, UnixFileMode.None);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    /// <summary>把权限放回来，临时目录才删得掉。</summary>
    private static void 恢复权限(string path)
    {
        if (OperatingSystem.IsWindows())
            return;
        try
        {
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        catch (Exception)
        {
            // 恢复不了就让临时目录自己报，别在清理阶段掩盖真正的失败
        }
    }

    // ================================================================
    //  目录展开
    // ================================================================

    [Fact]
    public void 目录递归收txt与md且按路径排序()
    {
        using var dir = new TempDir();
        using var other = new TempDir();
        string 单独给的 = Path.Combine(other.Path, "单独.md");
        写入(单独给的, "正文。\n");
        string 甲 = Path.Combine(dir.Path, "a.txt");
        string 乙 = Path.Combine(dir.Path, "b.md");
        string 丙 = Path.Combine(dir.Path, "book", "c.md");
        写入(甲, "正文。\n");
        写入(乙, "正文。\n");
        写入(丙, "正文。\n");
        写入(Path.Combine(dir.Path, "notes.json"), "{}");

        var collected = LintInputs.Collect(new[] { 单独给的, dir.Path });

        // 先给的文件排在最前，目录展开的部分按路径排序，json 不收
        Assert.Equal(new[] { 单独给的, 甲, 乙, 丙 }, collected);
    }

    [Fact]
    public void 目录展开收大写后缀()
    {
        using var dir = new TempDir();
        string 甲 = Path.Combine(dir.Path, "a.MD");
        string 乙 = Path.Combine(dir.Path, "b.TXT");
        写入(甲, "正文。\n");
        写入(乙, "正文。\n");

        var collected = LintInputs.Collect(new[] { dir.Path });

        Assert.Equal(new[] { 甲, 乙 }, collected);
    }

    [Fact]
    public void 传给Collect的stdin原样透传()
    {
        var collected = LintInputs.Collect(new[] { "-" });

        Assert.Equal(new[] { "-" }, collected);
    }

    [Fact]
    public void 同一个目录传两次时每份稿只收一次()
    {
        using var dir = new TempDir();
        string 甲 = Path.Combine(dir.Path, "a.md");
        string 乙 = Path.Combine(dir.Path, "sub", "b.md");
        写入(甲, "正文。\n");
        写入(乙, "正文。\n");

        // 第二个目录是第一个目录的子目录：它的那份稿已经在第一次展开里了
        var collected = LintInputs.Collect(new[] { dir.Path, dir.Path, Path.Combine(dir.Path, "sub") });

        Assert.Equal(new[] { 甲, 乙 }, collected);
    }

    [Fact]
    public void 同一个目录传两次时命令行只出一份报告()
    {
        using var dir = new TempDir();
        写入(Path.Combine(dir.Path, "a.md"), "不是这样，而是那样。\n");
        写入(Path.Combine(dir.Path, "sub", "b.md"), "不是这样，而是那样。\n");

        var run = RunCli("lint", dir.Path, dir.Path, Path.Combine(dir.Path, "sub"),
            "--only", "parallel-contrast", "--genre", "doc", "--json");

        Assert.Equal(1, run.Code);
        // 报告数按 JSON 里的 File 字段数：3 次目录参数、2 份稿，报告只能是 2 份
        Assert.Equal(2, run.Stdout.Split("\"File\":").Length - 1);
    }

    [Fact]
    public void 软链接绕回上级时不重复也不死循环()
    {
        using var dir = new TempDir();
        string 甲 = Path.Combine(dir.Path, "a.md");
        string 乙 = Path.Combine(dir.Path, "sub", "b.md");
        写入(甲, "正文。\n");
        写入(乙, "正文。\n");
        try
        {
            // 子目录里放一条指回上级的链接：跟下去就是同一个目录，再跟就没有尽头
            Directory.CreateSymbolicLink(Path.Combine(dir.Path, "sub", "loop"), dir.Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return; // 建不了软链接的平台上验不了这一条
        }

        var collected = LintInputs.Collect(new[] { dir.Path });

        Assert.Equal(new[] { 甲, 乙 }, collected);
    }

    [Fact]
    public void 目录里收不到可检文件时报错并点名目录()
    {
        using var dir = new TempDir();
        写入(Path.Combine(dir.Path, "notes.json"), "{}");

        var ex = Assert.Throws<ArgumentException>(() => LintInputs.Collect(new[] { dir.Path }));

        Assert.Contains(dir.Path, ex.Message, StringComparison.Ordinal);
        Assert.Contains(".txt", ex.Message, StringComparison.Ordinal);
    }

    // ================================================================
    //  lint 收目录
    // ================================================================

    [Fact]
    public void Lint_收目录时按命中退一或退零()
    {
        using var dir = new TempDir();
        string 稿 = Path.Combine(dir.Path, "sub", "a.md");
        写入(稿, "不是这样，而是那样。\n");
        string[] common = { "--only", "parallel-contrast", "--genre", "doc" };

        var 有命中 = RunCli(new[] { "lint", dir.Path }.Concat(common).ToArray());
        Assert.Equal(1, 有命中.Code);
        Assert.Contains("parallel-contrast", 有命中.Stdout, StringComparison.Ordinal);

        // 把命中那句换掉，同一个目录退 0
        写入(稿, "这一句没有对比句。\n");
        var 无命中 = RunCli(new[] { "lint", dir.Path }.Concat(common).ToArray());
        Assert.Equal(0, 无命中.Code);
        Assert.Equal("", 无命中.Stderr);
    }

    [Fact]
    public void Lint_目录里没有可检文件时退二并点名目录()
    {
        using var dir = new TempDir();

        var run = RunCli("lint", dir.Path);

        Assert.Equal(2, run.Code);
        Assert.Contains(dir.Path, run.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void 子目录读不了时报中文路径并接着查其余文件()
    {
        using var dir = new TempDir();
        string 好 = Path.Combine(dir.Path, "a.md");
        string 内 = Path.Combine(dir.Path, "inner");
        写入(好, "不是这样，而是那样。\n");
        写入(Path.Combine(内, "b.md"), "不是这样，而是那样。\n");
        if (!收紧权限(内))
            return; // 提权进程与 Windows 上造不出读不了的目录

        try
        {
            var run = RunCli("lint", dir.Path, "--only", "parallel-contrast", "--genre", "doc");

            Assert.Equal(1, run.Code);                                            // 读得到的那份照常出命中
            Assert.Contains("parallel-contrast", run.Stdout, StringComparison.Ordinal);
            Assert.Contains("读不了目录", run.Stderr, StringComparison.Ordinal);   // 说明是中文，不是系统原话
            Assert.Contains(内, run.Stderr, StringComparison.Ordinal);             // 点名是哪一个
            Assert.DoesNotContain("Access to the path", run.Stderr, StringComparison.Ordinal);
        }
        finally
        {
            恢复权限(内);
        }
    }

    // ================================================================
    //  多份稿的人读报告
    // ================================================================

    [Fact]
    public void 多份稿的人读报告每份前面印文件名()
    {
        using var dir = new TempDir();
        string 甲 = Path.Combine(dir.Path, "a.md");
        string 乙 = Path.Combine(dir.Path, "sub", "b.md");
        写入(甲, "不是这样，而是那样。\n");
        写入(乙, "不是这样，而是那样。\n");
        string[] common = { "--only", "parallel-contrast", "--genre", "doc" };

        var 多份 = RunCli(new[] { "lint", dir.Path }.Concat(common).ToArray());
        var 多份行 = 多份.Stdout.Split('\n');
        Assert.Equal(1, 多份.Code);
        Assert.Contains(甲, 多份行);   // 每份前面单独一行文件名，这一行整行就是路径
        Assert.Contains(乙, 多份行);

        // 只有一份时不印：脚本按原来的样子读输出
        var 单份 = RunCli(new[] { "lint", 甲 }.Concat(common).ToArray());
        Assert.DoesNotContain(甲, 单份.Stdout.Split('\n'));
    }

    [Fact]
    public void 人读片段里的换行印成空格而不是转义序列()
    {
        var report = new LintReport { File = "a.md", Chars = 20 };
        report.Hits.Add(new LintHit
        {
            Id = "parallel-contrast", Title = "对举句式", Line = 1, Col = 1, Snippet = "这不是A，\n而是B。",
        });

        string text = LintTextFormatter.Format(report);

        Assert.DoesNotContain("\\u000a", text, StringComparison.Ordinal);
        Assert.Contains("这不是A， 而是B。", text, StringComparison.Ordinal);
        // 隐形字符照样转义：那是为了老控制台编不出来时不抛异常，与换行不是一回事
        Assert.Equal("前\\u200b后", LintTextFormatter.Visible("前\u200b后"));
    }

    // ================================================================
    //  lint 的读取失败
    // ================================================================

    [Fact]
    public void Lint_一份读不了时其余照常出报告且跑完退二()
    {
        using var dir = new TempDir();
        string 好 = Path.Combine(dir.Path, "a.md");
        string 缺 = Path.Combine(dir.Path, "nope.md");
        写入(好, "不是这样，而是那样。\n");

        var run = RunCli("lint", 缺, 好, "--only", "parallel-contrast", "--genre", "doc");

        Assert.Equal(2, run.Code);                                            // 有读不了的按读取错误口径
        Assert.Contains("parallel-contrast", run.Stdout, StringComparison.Ordinal);  // 好稿照常检
        Assert.Contains(缺, run.Stderr, StringComparison.Ordinal);             // 失败带完整路径
        Assert.Contains("文件不存在", run.Stderr, StringComparison.Ordinal);    // 与原因
    }

    [Fact]
    public void Lint_全部读不了时不拿空报告装作检查过()
    {
        using var dir = new TempDir();
        string 缺一 = Path.Combine(dir.Path, "nope1.md");
        string 缺二 = Path.Combine(dir.Path, "nope2.md");

        var run = RunCli("lint", 缺一, 缺二);

        Assert.Equal(2, run.Code);
        Assert.Equal("", run.Stdout);
        Assert.Contains(缺一, run.Stderr, StringComparison.Ordinal);
        Assert.Contains(缺二, run.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void 带baseline的目录输入在展开后就退二()
    {
        using var dir = new TempDir();
        using var other = new TempDir();
        string 甲 = Path.Combine(dir.Path, "a.md");
        string 乙 = Path.Combine(dir.Path, "b.md");
        写入(甲, "第一句。\n");
        写入(乙, "第二句。\n");
        string 基线 = Path.Combine(other.Path, "base.md");
        写入(基线, "第一句。\n");
        // 展开后就有两份稿，此时该报「只配一个输入」，不该先去读它们
        if (!收紧权限(乙))
            return;

        try
        {
            var run = RunCli("lint", dir.Path, "--baseline", 基线);

            Assert.Equal(2, run.Code);
            Assert.Contains("只配一个输入文件", run.Stderr, StringComparison.Ordinal);
            Assert.DoesNotContain("[失败]", run.Stderr, StringComparison.Ordinal);
        }
        finally
        {
            恢复权限(乙);
        }
    }

    [Fact]
    public void 目录加log时日志里写的是展开后的路径()
    {
        using var dir = new TempDir();
        string 稿 = Path.Combine(dir.Path, "sub", "a.md");
        写入(稿, "不是这样，而是那样。\n");
        string 日志 = Path.Combine(dir.Path, "hits.jsonl");

        var run = RunCli("lint", dir.Path, "--only", "parallel-contrast", "--genre", "doc", "--log", 日志);

        Assert.Equal(1, run.Code);
        string 记录 = File.ReadAllText(日志);
        // 日志里的 File 是展开后的那份稿的路径，不是目录名也不是文件名
        Assert.Contains(稿.Replace("\\", "\\\\"), 记录, StringComparison.Ordinal);
    }

    // ================================================================
    //  写盘失败不留临时文件
    // ================================================================

    [Fact]
    public void 写盘失败后目录里不留tmp()
    {
        using var dir = new TempDir();
        string 目标 = Path.Combine(dir.Path, "out.txt");
        Directory.CreateDirectory(目标); // 目标被目录占住：临时文件写得进去，整体替换必然失败

        Assert.ThrowsAny<Exception>(() =>
            AtomicFile.WriteAllLines(目标, new[] { "第一行。" }, new UTF8Encoding(false)));
        Assert.Empty(Directory.GetFiles(dir.Path, "*.tmp", SearchOption.AllDirectories));

        Assert.ThrowsAny<Exception>(() =>
            AtomicFile.WriteAllText(目标, "第一行。", new UTF8Encoding(false)));
        Assert.Empty(Directory.GetFiles(dir.Path, "*.tmp", SearchOption.AllDirectories));
    }

    // ================================================================
    //  逐文件失败不中断
    // ================================================================

    [Fact]
    public void Merge_单个文件失败不中断跑完全部再退一()
    {
        using var dir = new TempDir();
        string bad = Path.Combine(dir.Path, "bad.txt");
        string good = Path.Combine(dir.Path, "good.txt");
        写入(bad, "第一行。\n");
        写入(good, "第二行。\n");
        // 输出位置被目录占住：这一份写不进去，另一份照常处理
        Directory.CreateDirectory(Path.Combine(dir.Path, "bad_Processed.txt"));

        var run = RunCli("merge", bad, good);

        Assert.Equal(1, run.Code);
        Assert.Contains(good, run.Stdout, StringComparison.Ordinal);   // 一份以上输入时带完整路径
        Assert.DoesNotContain(bad, run.Stdout, StringComparison.Ordinal);
        Assert.Contains(bad, run.Stderr, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(dir.Path, "good_Processed.txt")));
    }

    [Fact]
    public void Replace_与vn同样逐文件失败不中断()
    {
        string[][] commands =
        {
            new[] { "replace", "--scheme", "标点符号转换" },
            new[] { "vn" },
        };

        foreach (string[] command in commands)
        {
            using var dir = new TempDir();
            string bad = Path.Combine(dir.Path, "bad.txt");
            string good = Path.Combine(dir.Path, "good.txt");
            写入(bad, "第一行。\n");
            写入(good, "第二行。\n");
            Directory.CreateDirectory(Path.Combine(dir.Path, "bad_Processed.txt"));

            var run = RunCli(command.Concat(new[] { bad, good }).ToArray());

            Assert.Equal(1, run.Code);
            Assert.Contains(good, run.Stdout, StringComparison.Ordinal);
            Assert.DoesNotContain(bad, run.Stdout, StringComparison.Ordinal);
            Assert.Contains(bad, run.Stderr, StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(dir.Path, "good_Processed.txt")));
        }
    }

    // ================================================================
    //  真实进程
    // ================================================================

    /// <summary>
    /// 命令行的 dll，与测试同一配置、同一框架。
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

    [Fact]
    public void Lint_规则拼错又碰上读不上的稿时先报读不上的那几份()
    {
        using var dir = new TempDir();
        string 好 = Path.Combine(dir.Path, "a.md");
        string 缺 = Path.Combine(dir.Path, "nope.md");
        写入(好, "不是这样，而是那样。\n");

        // 一份读得上、一份读不上，规则 Id 又拼错：读不上的那条要排在规则错之前
        var run = RunCli("lint", 缺, 好, "--only", "NOPE");

        Assert.Equal(2, run.Code);
        Assert.Equal("", run.Stdout);                                        // 规则没过，一份报告都不出
        Assert.Contains("[失败]", run.Stderr, StringComparison.Ordinal);
        // 两条都在，且读不上的那条在前：读盘与校验的次序不能倒过来
        int 读失败 = run.Stderr.IndexOf("[失败]", StringComparison.Ordinal);
        int 规则错 = run.Stderr.IndexOf("未知规则 Id", StringComparison.Ordinal);
        Assert.True(读失败 >= 0 && 规则错 >= 0 && 读失败 < 规则错, $"次序不对：{run.Stderr}");
    }

    [Fact]
    public void Lint_一份报告都没出时不留空日志文件()
    {
        using var dir = new TempDir();
        string 稿 = Path.Combine(dir.Path, "a.md");
        写入(稿, "不是这样，而是那样。\n");
        string 日志 = Path.Combine(dir.Path, "hits.jsonl");

        var run = RunCli("lint", 稿, "--only", "NOPE", "--log", 日志);

        Assert.Equal(2, run.Code);
        Assert.False(File.Exists(日志), "规则没过时一条命中都没写，不该先建出一个空日志文件");
    }

    [Fact]
    public void Lint_日志落点写不进去时报告照常出完再报日志错()
    {
        using var dir = new TempDir();
        string 稿 = Path.Combine(dir.Path, "a.md");
        写入(稿, "不是这样，而是那样。\n");

        var run = RunCli("lint", 稿, "--only", "parallel-contrast", "--genre", "doc",
            "--log", Path.Combine(dir.Path, "缺失", "hits.jsonl"));

        Assert.Equal(2, run.Code);
        Assert.Contains("parallel-contrast", run.Stdout, StringComparison.Ordinal);   // 报告在报错之前出完
        Assert.Contains("日志目录不存在", run.Stderr, StringComparison.Ordinal);
    }
}
