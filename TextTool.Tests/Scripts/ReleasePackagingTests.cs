namespace TextTool.Tests.Scripts;

/// <summary>
/// 发布产物命名横跨四个地方：Core 的 SelfUpdater、CI 的打包步骤、release.ps1 的等待清单、
/// publish.ps1 的签名清单。任何一处落后于其他三处，就会出现"客户端找一个没人产出的包"
/// 或者"产出了却没人签名"的静默故障，而且只有真发版时才暴露。
/// 这里只钉「四处都认得 linux-x64 这份命令行包」，不测脚本内部逻辑。
/// </summary>
public class ReleasePackagingTests
{
    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    private static string ReadRepoFile(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot }.Concat(parts).ToArray()));

    [Fact]
    public void SelfUpdater_两个平台的命令行包命名()
    {
        // 客户端按自己的平台拼名字；名字和 CI 产出的文件对不上就永远更新不了。
        foreach (string rid in new[] { "win-x64", "linux-x64" })
        {
            string name = TextTool.Services.SelfUpdater.GetCliAssetName("2.0.0", rid);
            Assert.Equal($"TextTool-CLI-2.0.0-{rid}.zip", name);
        }
    }

    [Fact]
    public void CI_为命令行版打linux包()
    {
        var text = ReadRepoFile(".github", "workflows", "build-test.yml");

        Assert.Contains("-r linux-x64", text, StringComparison.Ordinal);
        Assert.Contains("TextTool-CLI-$version-linux-x64.zip", text, StringComparison.Ordinal);
    }

    [Fact]
    public void 发布脚本名单_含linux命令行包()
    {
        var text = ReadRepoFile("scripts", "github-release.ps1");

        Assert.Contains("TextTool-CLI-$Raw-linux-x64.zip", text, StringComparison.Ordinal);
    }

    [Fact]
    public void 签名与等待清单_都走同一份名单()
    {
        // 名单只有一个来源，两个脚本都不许自己再抄一份。
        Assert.Contains("Get-TextToolReleaseAssetNames", ReadRepoFile("scripts", "publish.ps1"), StringComparison.Ordinal);
        Assert.Contains("Get-TextToolReleaseAssetNames", ReadRepoFile("scripts", "release.ps1"), StringComparison.Ordinal);
    }
}
