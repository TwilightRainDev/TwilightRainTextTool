namespace TextTool.Tests.Scripts;

public class ReleaseScriptTests
{
    private static string ScriptPath =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "scripts", "release.sh"));

    private static string PropsPath =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Directory.Build.props"));

    [Fact]
    public void Script_Exists()
    {
        Assert.True(File.Exists(ScriptPath), ScriptPath);
    }

    [Fact]
    public void InvalidVersion_Exits2()
    {
        var (code, stderr, _) = Invoke("-Version", "abc");
        Assert.Equal(2, code);
        Assert.Contains("x.y.z", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void DryRun_Exits0_AndPrintsTag()
    {
        var (code, _, stdout) = Invoke("-DryRun");
        Assert.Equal(0, code);
        Assert.Contains("tag=v", stdout, StringComparison.Ordinal);
        var props = File.ReadAllText(PropsPath);
        var match = System.Text.RegularExpressions.Regex.Match(
            props, @"<Version>([0-9]+\.[0-9]+\.[0-9]+)</Version>");
        Assert.True(match.Success);
        Assert.Contains("version=" + match.Groups[1].Value, stdout, StringComparison.Ordinal);
    }

    [Fact]
    public void ScriptText_HasSafetyAndFlowMarkers()
    {
        var text = File.ReadAllText(ScriptPath);
        Assert.Contains("SkipTag", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DryRun", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Directory.Build.props", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ghp_", text, StringComparison.Ordinal);
        Assert.DoesNotContain("BEGIN", text, StringComparison.Ordinal);
    }

    private static (int Code, string Stderr, string Stdout) Invoke(params string[] args)
    {
        var shell = BashLocator.Resolve();
        var psi = new System.Diagnostics.ProcessStartInfo(shell)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        // 脚本路径用正斜杠：bash 把反斜杠当转义字符，C:/... 在 Git Bash 与 Linux 上都认。
        psi.ArgumentList.Add(ScriptPath.Replace('\\', '/'));
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var proc = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException("无法启动 bash");
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(30_000);
        return (proc.ExitCode, stderr, stdout);
    }
}
