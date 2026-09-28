namespace TextTool.Tests.Scripts;

public class ReleaseScriptTests
{
    private static string ScriptPath =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "scripts", "release.ps1"));

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
        var shell = File.Exists(@"C:\Program Files\PowerShell\7\pwsh.exe")
            ? @"C:\Program Files\PowerShell\7\pwsh.exe"
            : "powershell.exe";
        var arg_line = $"-NoProfile -ExecutionPolicy Bypass -File \"{ScriptPath}\"";
        if (args.Length > 0)
            arg_line += " " + string.Join(" ", args);
        var psi = new System.Diagnostics.ProcessStartInfo(shell, arg_line)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        using var proc = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException("无法启动 PowerShell");
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(30_000);
        return (proc.ExitCode, stderr, stdout);
    }
}
