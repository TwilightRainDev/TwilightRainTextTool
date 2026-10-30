namespace TextTool.Tests.Scripts;

/// <summary>
/// Linux 启动器 scripts/linux-launcher.sh 的行为回归。发行包里的 texttool 与 TextTool.Gui
/// 就是它（发布时按产品名复制、打包时给可执行位），所以要钉住三件事：
/// PATH 上的 dotnet 能用、装到 ~/.dotnet 的 dotnet 也能用、两处都没有时给出装法而不是静默失败。
///
/// dll 名由启动器自己的文件名推出，用例因此把脚本复制成 texttool 再跑，
/// 顺带钉住「按文件名找 dll」这条约定。
/// </summary>
public class LinuxLauncherTests
{
    private static string ScriptPath => Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "scripts", "linux-launcher.sh"));

    [Fact]
    public void Script_Exists()
    {
        Assert.True(File.Exists(ScriptPath), ScriptPath);
    }

    [UnixFact]
    public void PATH上有dotnet_交给它并带上dll路径()
    {
        using var pkg = new Package();
        var stubDir = Path.Combine(pkg.Root, "bin");
        StubDotnet(stubDir);                           // PATH 上那个
        var home = EmptyHome(pkg.Root);                // 家里没有 .dotnet，逼它走 PATH

        var (code, _, _) = pkg.Run("PATH=" + stubDir + ":/usr/bin:/bin", "HOME=" + home, "TT_OUT=" + pkg.Record(stubDir));

        Assert.Equal(0, code);
        var argv = pkg.ReadRecord(stubDir);
        Assert.Equal(Path.Combine(pkg.Root, "texttool.dll"), argv[0]);
        Assert.Equal("--probe", argv[1]);
    }

    [UnixFact]
    public void 仅HOME下_dotnet_也能启动()
    {
        using var pkg = new Package();
        var home = Path.Combine(pkg.Root, "home");
        StubDotnet(Path.Combine(home, ".dotnet"));

        // PATH 里没有 dotnet，只能靠 $HOME/.dotnet 这一档。
        var (code, _, _) = pkg.Run("PATH=/usr/bin:/bin", "HOME=" + home, "TT_OUT=" + pkg.Record(home));

        Assert.Equal(0, code);
        Assert.Equal(Path.Combine(pkg.Root, "texttool.dll"), pkg.ReadRecord(home)[0]);
    }

    [UnixFact]
    public void 没有运行时_退出127并给出装法()
    {
        // 系统里真装了 .NET 时这条判不了：启动器会找到它并继续往下跑。跳过而不是判失败。
        string[] systemRoots =
        {
            "/usr/share/dotnet/dotnet", "/usr/lib/dotnet/dotnet",
            "/snap/dotnet-sdk/current/dotnet", "/opt/dotnet/dotnet",
        };
        if (systemRoots.Any(File.Exists))
            return;

        using var pkg = new Package();
        var (code, stderr, _) = pkg.Run("PATH=/usr/bin:/bin", "HOME=" + EmptyHome(pkg.Root));

        Assert.Equal(127, code);
        Assert.Contains("找不到 .NET 运行时", stderr, StringComparison.Ordinal);
        Assert.Contains("dotnet-install.sh", stderr, StringComparison.Ordinal);
        // 提示里要能让用户照着抄：既给装法，也给「已经装在别处」的出路。
        Assert.Contains("DOTNET_ROOT=", stderr, StringComparison.Ordinal);
    }

    [UnixFact]
    public void 缺dll_退出2()
    {
        using var pkg = new Package(withDll: false);
        var (code, stderr, _) = pkg.Run("PATH=/usr/bin:/bin", "HOME=" + EmptyHome(pkg.Root));

        Assert.Equal(2, code);
        Assert.Contains("安装包不完整", stderr, StringComparison.Ordinal);
    }

    /// <summary>
    /// 造一个假的假 dotnet：把收到的参数写进 $TT_OUT 后退出 0。
    /// 启动器只看它可执行，不看它是不是真的运行时，所以这够用来验证「交棒」这件事。
    /// </summary>
    private static string StubDotnet(string dir)
    {
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "dotnet");
        File.WriteAllText(path, "#!/usr/bin/env bash\nprintf '%s\\n' \"$@\" > \"$TT_OUT\"\n");
        // 只在 Linux 上改权限：调用方都是 UnixFact，别的平台根本不会走到这里。
        if (OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
        return path;
    }

    private static string EmptyHome(string root)
    {
        string home = Path.Combine(root, "empty-home");
        Directory.CreateDirectory(home);
        return home;
    }

    /// <summary>一个临时目录，布局与发行包一致：texttool（启动器）与 texttool.dll。</summary>
    private sealed class Package : IDisposable
    {
        public string Root { get; }

        public Package(bool withDll = true)
        {
            Root = Path.Combine(Path.GetTempPath(), "tt-launcher-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            File.Copy(ScriptPath, Path.Combine(Root, "texttool"));
            if (withDll)
                File.WriteAllBytes(Path.Combine(Root, "texttool.dll"), Array.Empty<byte>());
        }

        /// <summary>装配出「启动器会被调用的样子」，返回退出码与两路输出。</summary>
        public (int Code, string Stderr, string Stdout) Run(params string[] env)
        {
            var psi = new System.Diagnostics.ProcessStartInfo(BashLocator.Resolve())
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add(Path.Combine(Root, "texttool").Replace('\\', '/'));
            psi.ArgumentList.Add("--probe");

            foreach (var item in env)
            {
                int eq = item.IndexOf('=');
                psi.Environment[item[..eq]] = item[(eq + 1)..];
            }
            psi.Environment.Remove("DOTNET_ROOT");  // 别让开发机上的设置决定结论

            using var proc = System.Diagnostics.Process.Start(psi)
                ?? throw new InvalidOperationException("无法启动 bash");
            var stdout = proc.StandardOutput.ReadToEnd();
            var stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit(30_000);
            return (proc.ExitCode, stderr, stdout);
        }

        /// <summary>假 dotnet 把收到的参数写到这里；同一个目录既当 PATH 条目也当落点。</summary>
        public string Record(string dir) => Path.Combine(dir, "argv.txt");

        public string[] ReadRecord(string dir) => File.ReadAllLines(Record(dir));

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
                // 临时目录删不掉不影响断言；留给系统清。
            }
        }
    }
}
