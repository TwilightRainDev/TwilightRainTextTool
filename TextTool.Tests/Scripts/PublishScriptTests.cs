namespace TextTool.Tests.Scripts;

public class PublishScriptTests
{
    private static string ScriptPath =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "scripts", "publish.ps1"));

    [Fact]
    public void Script_Exists()
    {
        Assert.True(File.Exists(ScriptPath), ScriptPath);
    }

    [Fact]
    public void InvalidVersion_Exits2()
    {
        var (code, stderr) = Invoke("-Version", "abc");
        Assert.Equal(2, code);
        Assert.Contains("版本", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingVersion_ExitsNonZero()
    {
        var (code, _) = Invoke();
        Assert.NotEqual(0, code);
    }

    [Fact]
    public void ScriptText_HasSafetyAndFlowMarkers()
    {
        var text = File.ReadAllText(ScriptPath);
        Assert.Contains("SkipUpload", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Force", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ReleaseSigner", text, StringComparison.Ordinal);
        Assert.Contains("verify", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GithubApiToken", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ghp_", text, StringComparison.Ordinal);
        Assert.DoesNotContain("BEGIN", text, StringComparison.Ordinal);
    }

    private static (int Code, string Stderr) Invoke(params string[] args)
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
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(30_000);
        return (proc.ExitCode, stderr);
    }
}
