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
        Assert.Contains("x.y.z", stderr, StringComparison.Ordinal);
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
        Assert.Contains("api.github.com", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("gh release", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GithubApiToken", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ghp_", text, StringComparison.Ordinal);
        Assert.DoesNotContain("BEGIN", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ScriptText_发布说明并入升级提示()
    {
        var text = File.ReadAllText(ScriptPath);

        Assert.Contains("ReleaseNotes.md", text, StringComparison.Ordinal);
        Assert.Contains("Set-GithubReleaseBody", text, StringComparison.Ordinal);

        // 只钉「两个词都出现过」发现不了调用点被挪到 -SkipUpload 之前（那会让
        // -SkipUpload 反而去改 release 正文）。这里按文本位置钉住顺序：
        // Set-GithubReleaseBody 必须排在提前返回分支的 exit 0 之后。
        int skipBranch = text.IndexOf("if ($SkipUpload)", StringComparison.Ordinal);
        Assert.True(skipBranch >= 0, "未找到 -SkipUpload 提前返回分支");

        int skipExit = text.IndexOf("exit 0", skipBranch, StringComparison.Ordinal);
        Assert.True(skipExit >= 0, "未找到 -SkipUpload 分支的提前返回");

        int setBody = text.IndexOf("Set-GithubReleaseBody", StringComparison.Ordinal);
        Assert.True(setBody > skipExit, "Set-GithubReleaseBody 必须位于 -SkipUpload 提前返回之后");
    }

    [Fact]
    public void ReleaseNotes_存在且写明受阻版本()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "doc", "ReleaseNotes.md"));

        Assert.True(File.Exists(path), path);
        Assert.Contains("2.6.1", File.ReadAllText(path), StringComparison.Ordinal);
    }

    private static (int Code, string Stderr) Invoke(params string[] args)
    {
        var shell = PowerShellLocator.Resolve();
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
