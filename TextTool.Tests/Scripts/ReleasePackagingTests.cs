using System.Diagnostics;

namespace TextTool.Tests.Scripts;

/// <summary>
/// 发布产物命名横跨四处：Core 的 SelfUpdater、scripts/release-assets.sh 的名单、
/// CI 的打包步骤、两个发布脚本的签名与等待清单。名单只有一个来源
/// （texttool_release_asset_names），这里钉住其余三处都从它取、
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

    /// <summary>
    /// 在独立 bash 进程里 source 一份脚本再跑一段命令，返回去首尾空白的标准输出。
    /// 脚本路径用正斜杠：bash 把反斜杠当转义字符，C:/... 这种写法在 Git Bash 与 Linux 上都认。
    /// </summary>
    private static string RunBashScript(string scriptName, string command)
    {
        var shell = BashLocator.Resolve();
        var psi = new ProcessStartInfo(shell)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add($". '{ScriptPath(scriptName).Replace('\\', '/')}'; {command}");

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("无法启动 bash");
        string stdout = proc.StandardOutput.ReadToEnd();
        string stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(60_000);
        Assert.True(proc.ExitCode == 0, $"bash 执行失败（exit {proc.ExitCode}）：{stderr}");
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
        var names = RunBashScript(
                "release-assets.sh",
                "texttool_release_asset_names 2.0.0 | tr '\\n' '|'")
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
        const string guiWin = "TextTool-GUI-2.0.0-win-x64.zip";
        const string guiLinux = "TextTool-GUI-2.0.0-linux-x64.zip";
        const string cliWin = "TextTool-CLI-2.0.0-win-x64.zip";
        const string cliLinux = "TextTool-CLI-2.0.0-linux-x64.zip";

        // 什么都没产出：四份全点名，而不是报一句未找到 zip 了事。
        var nothing = RunBashScript(
                "release-assets.sh",
                "texttool_missing_release_assets 2.0.0 | tr '\\n' '|'")
            .Split('|', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(4, nothing.Length);
        Assert.Contains(guiWin, nothing);
        Assert.Contains(guiLinux, nothing);
        Assert.Contains(cliWin, nothing);
        Assert.Contains(cliLinux, nothing);

        // 只缺一套（linux 的 CLI）：必须单独点名，否则就只有那一份没人签、也没人发。
        var oneRid = RunBashScript(
            "release-assets.sh",
            $"texttool_missing_release_assets 2.0.0" +
            $" {guiWin} {guiWin}.sha256 {guiLinux} {guiLinux}.sha256 {cliWin} {cliWin}.sha256 | tr '\\n' '|'")
            // tr 每行都补一个分隔符，末行也带着，单点名比较时去掉。
            .TrimEnd('|');

        Assert.Equal(cliLinux, oneRid);

        // .sha256 缺了也算缺，名字带后缀点名。
        var oneSha = RunBashScript(
            "release-assets.sh",
            $"texttool_missing_release_assets 2.0.0" +
            $" {guiWin} {guiLinux} {guiLinux}.sha256 {cliWin} {cliWin}.sha256 {cliLinux} {cliLinux}.sha256" +
            " | tr '\\n' '|'")
            .TrimEnd('|');

        Assert.Equal($"{guiWin}.sha256", oneSha);

        // 四套齐全时一个字都不该报，否则真发版会被误拦。
        var noneMissing = RunBashScript(
            "release-assets.sh",
            "texttool_missing_release_assets 2.0.0" +
            " $(texttool_release_asset_names 2.0.0)" +
            " $(texttool_release_asset_names 2.0.0 | sed 's/$/.sha256/')" +
            " | tr '\\n' '|'");

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

        Assert.Contains("texttool_release_asset_names", text, StringComparison.Ordinal);
        Assert.Contains("texttool_missing_release_assets", text, StringComparison.Ordinal);

        // 名字一旦在 YAML 里写死，就等于又抄了一份名单，迟早和脚本名单对不上。
        // 认带 .zip 后缀的完整名字；YAML 里按前缀分流 GUI 与 CLI 不算抄名单。
        Assert.DoesNotMatch(@"TextTool-(GUI|CLI)-\S*\.zip", text);
    }

    [Fact]
    public void 发布脚本名单_含两个RID的四份产物()
    {
        var text = ReadRepoFile("scripts", "release-assets.sh");

        Assert.Contains("TextTool-GUI-$raw-win-x64.zip", text, StringComparison.Ordinal);
        Assert.Contains("TextTool-GUI-$raw-linux-x64.zip", text, StringComparison.Ordinal);
        Assert.Contains("TextTool-CLI-$raw-win-x64.zip", text, StringComparison.Ordinal);
        Assert.Contains("TextTool-CLI-$raw-linux-x64.zip", text, StringComparison.Ordinal);
    }

    [Fact]
    public void 签名与等待清单_都走同一份名单()
    {
        // 名单只有一个来源（texttool_release_asset_names），两个脚本都只调
        // texttool_missing_release_assets 这一处判据，不自己再抄一份。
        foreach (string script in new[] { "publish.sh", "release.sh" })
        {
            var text = ReadRepoFile("scripts", script);
            Assert.Contains("texttool_missing_release_assets", text, StringComparison.Ordinal);
            Assert.DoesNotContain("TextTool-GUI-$raw", text, StringComparison.Ordinal);
            Assert.DoesNotContain("TextTool-CLI-$raw", text, StringComparison.Ordinal);
        }

        Assert.Contains("texttool_release_asset_names", ReadRepoFile("scripts", "publish.sh"), StringComparison.Ordinal);
    }
}
