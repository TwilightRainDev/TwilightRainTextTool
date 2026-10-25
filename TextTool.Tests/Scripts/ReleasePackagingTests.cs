using System.Diagnostics;

namespace TextTool.Tests.Scripts;

/// <summary>
/// 发布产物命名横跨四处：Core 的 SelfUpdater、scripts/github-release.ps1 的名单、
/// CI 的打包步骤、两个发布脚本的签名与等待清单。名单只有一个来源
/// （Get-TextToolReleaseAssetNames），这里钉住其余三处都从它取、
/// 且命名与 SelfUpdater 在 win-x64 与 linux-x64 上都对得上。
/// 任何一处只改一边都会让「客户端找一个没人产出的包」或「产出了却没人签名」
/// 这类故障悄悄溜过去，而且只有真发版时才暴露。
/// </summary>
public class ReleasePackagingTests
{
    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    private static string ReadRepoFile(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot }.Concat(parts).ToArray()));

    private static string ScriptPath(string name) => Path.Combine(RepoRoot, "scripts", name);

    /// <summary>在独立 PowerShell 进程里跑一段前缀脚本的命令，返回去首尾空白的标准输出。</summary>
    private static string RunPowerShellScript(string scriptName, string command)
    {
        var shell = PowerShellLocator.Resolve();
        var psi = new ProcessStartInfo(shell)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add($". '{ScriptPath(scriptName)}'; {command}");

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("无法启动 PowerShell");
        string stdout = proc.StandardOutput.ReadToEnd();
        string stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(60_000);
        Assert.True(proc.ExitCode == 0, $"PowerShell 执行失败（exit {proc.ExitCode}）：{stderr}");
        return stdout.Trim();
    }

    [Fact]
    public void SelfUpdater_命令行包命名_两个RID()
    {
        // 客户端按自己的平台拼名字；名字和 CI 产出的文件对不上就永远更新不了。
        foreach (string rid in new[] { "win-x64", "linux-x64" })
        {
            Assert.Equal($"TextTool-CLI-2.0.0-{rid}.zip",
                TextTool.Services.SelfUpdater.GetCliAssetName("2.0.0", rid));
        }
    }

    [Theory]
    [InlineData("3.0.0", "win-x64", "TextTool-GUI-3.0.0-win-x64.zip")]
    [InlineData("v3.0.0", "win-x64", "TextTool-GUI-3.0.0-win-x64.zip")]
    [InlineData("3.0.0", "linux-x64", "TextTool-GUI-3.0.0-linux-x64.zip")]
    [InlineData("v3.0.0", "linux-x64", "TextTool-GUI-3.0.0-linux-x64.zip")]
    public void SelfUpdater_图形界面包命名_按RID(string version, string rid, string expected)
    {
        Assert.Equal(expected, TextTool.Services.SelfUpdater.GetGuiAssetName(version, rid));
    }

    [Fact]
    public void 名单与SelfUpdater命名_四份逐一对应()
    {
        // 双 RID 齐活：GUI 与 CLI 各有 win-x64 与 linux-x64，一共四份。
        var names = RunPowerShellScript(
                "github-release.ps1",
                "(Get-TextToolReleaseAssetNames -Raw '2.0.0') -join '|'")
            .Split('|', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(4, names.Length);
        foreach (string rid in new[] { "win-x64", "linux-x64" })
        {
            Assert.Contains(TextTool.Services.SelfUpdater.GetGuiAssetName("2.0.0", rid), names);
            Assert.Contains(TextTool.Services.SelfUpdater.GetCliAssetName("2.0.0", rid), names);
        }
    }

    [Fact]
    public void 缺一套产物_被逐项点名()
    {
        // 什么都没产出：四份全点名，而不是报一句未找到 zip 了事。
        var nothing = RunPowerShellScript(
                "github-release.ps1",
                "(Get-TextToolMissingReleaseAssets -Raw '2.0.0' -Present @()) -join '|'")
            .Split('|', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(4, nothing.Length);
        Assert.Contains("TextTool-GUI-2.0.0-win-x64.zip", nothing);
        Assert.Contains("TextTool-GUI-2.0.0-linux-x64.zip", nothing);
        Assert.Contains("TextTool-CLI-2.0.0-win-x64.zip", nothing);
        Assert.Contains("TextTool-CLI-2.0.0-linux-x64.zip", nothing);

        // 只缺一套（linux 的 CLI）：必须单独点名，否则就只有那一份没人签、也没人发。
        var oneRid = RunPowerShellScript(
                "github-release.ps1",
                "$present = @('TextTool-GUI-2.0.0-win-x64.zip', 'TextTool-GUI-2.0.0-win-x64.zip.sha256'," +
                " 'TextTool-GUI-2.0.0-linux-x64.zip', 'TextTool-GUI-2.0.0-linux-x64.zip.sha256'," +
                " 'TextTool-CLI-2.0.0-win-x64.zip', 'TextTool-CLI-2.0.0-win-x64.zip.sha256');" +
                " (Get-TextToolMissingReleaseAssets -Raw '2.0.0' -Present $present) -join '|'");

        Assert.Equal("TextTool-CLI-2.0.0-linux-x64.zip", oneRid);

        // .sha256 缺了也算缺，名字带后缀点名。
        var oneSha = RunPowerShellScript(
                "github-release.ps1",
                "$present = @('TextTool-GUI-2.0.0-win-x64.zip'," +
                " 'TextTool-GUI-2.0.0-linux-x64.zip', 'TextTool-GUI-2.0.0-linux-x64.zip.sha256'," +
                " 'TextTool-CLI-2.0.0-win-x64.zip', 'TextTool-CLI-2.0.0-win-x64.zip.sha256'," +
                " 'TextTool-CLI-2.0.0-linux-x64.zip', 'TextTool-CLI-2.0.0-linux-x64.zip.sha256');" +
                " (Get-TextToolMissingReleaseAssets -Raw '2.0.0' -Present $present) -join '|'");

        Assert.Equal("TextTool-GUI-2.0.0-win-x64.zip.sha256", oneSha);

        // 四套齐全时一个字都不该报，否则真发版会被误拦。
        var noneMissing = RunPowerShellScript(
            "github-release.ps1",
            "$present = @(Get-TextToolReleaseAssetNames -Raw '2.0.0' | ForEach-Object { $_, \"$_.sha256\" });" +
            " (Get-TextToolMissingReleaseAssets -Raw '2.0.0' -Present $present) -join '|'");

        Assert.Equal("", noneMissing);
    }

    [Fact]
    public void CI_双RID矩阵_图形界面走框架依赖发布()
    {
        var text = ReadRepoFile(".github", "workflows", "build-test.yml");

        // 矩阵写死两个 RID，少了任何一个都发不出对应平台的包。
        Assert.Contains("rid: [win-x64, linux-x64]", text, StringComparison.Ordinal);

        // 新界面项目接替旧的 WinForms 项目；框架依赖发布是 10MB 下载包上限的前提。
        Assert.Contains("TextTool.Gui/TextTool.Gui.csproj", text, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet publish TextTool.csproj", text, StringComparison.Ordinal);
        Assert.Contains("--self-contained false", text, StringComparison.Ordinal);

        // 命令行版同样双 RID。
        Assert.Contains("TextTool.Cli/TextTool.Cli.csproj", text, StringComparison.Ordinal);
    }

    [Fact]
    public void CI_不另写名单_从脚本函数取名()
    {
        var text = ReadRepoFile(".github", "workflows", "build-test.yml");

        Assert.Contains("Get-TextToolReleaseAssetNames", text, StringComparison.Ordinal);
        Assert.Contains("Get-TextToolMissingReleaseAssets", text, StringComparison.Ordinal);

        // 名字一旦在 YAML 里写死，就等于又抄了一份名单，迟早和脚本名单对不上。
        Assert.DoesNotContain("TextTool-GUI-$version", text, StringComparison.Ordinal);
        Assert.DoesNotContain("TextTool-CLI-$version", text, StringComparison.Ordinal);
    }

    [Fact]
    public void 发布脚本名单_含两个RID的四份产物()
    {
        var text = ReadRepoFile("scripts", "github-release.ps1");

        Assert.Contains("TextTool-GUI-$Raw-win-x64.zip", text, StringComparison.Ordinal);
        Assert.Contains("TextTool-GUI-$Raw-linux-x64.zip", text, StringComparison.Ordinal);
        Assert.Contains("TextTool-CLI-$Raw-win-x64.zip", text, StringComparison.Ordinal);
        Assert.Contains("TextTool-CLI-$Raw-linux-x64.zip", text, StringComparison.Ordinal);
    }

    [Fact]
    public void 签名与等待清单_都走同一份名单()
    {
        // 名单只有一个来源（Get-TextToolReleaseAssetNames），两个脚本都只调
        // Get-TextToolMissingReleaseAssets 这一处判据，不自己再抄一份。
        foreach (string script in new[] { "publish.ps1", "release.ps1" })
        {
            var text = ReadRepoFile("scripts", script);
            Assert.Contains("Get-TextToolMissingReleaseAssets", text, StringComparison.Ordinal);
            Assert.DoesNotContain("TextTool-GUI-$Raw", text, StringComparison.Ordinal);
            Assert.DoesNotContain("TextTool-CLI-$Raw", text, StringComparison.Ordinal);
        }

        Assert.Contains("Get-TextToolReleaseAssetNames", ReadRepoFile("scripts", "publish.ps1"), StringComparison.Ordinal);
    }
}
