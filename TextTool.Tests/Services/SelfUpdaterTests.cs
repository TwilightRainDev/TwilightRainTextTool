using System.Text.Json;

namespace TextTool.Tests.Services;

public class SelfUpdaterTests
{
    [Theory]
    [InlineData("2.3.0", "TextTool-CLI-2.3.0-win-x64.zip")]
    [InlineData("v2.3.0", "TextTool-CLI-2.3.0-win-x64.zip")]
    public void GetCliAssetName_StripsV(string version, string expected)
    {
        Assert.Equal(expected, SelfUpdater.GetCliAssetName(version));
    }

    [Theory]
    [InlineData("abc", "abc  file.txt")]                      // 长度不足
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz", "x")] // 非法 hex
    public void TryParseSha256_Invalid_ReturnsFalse(string hash, string rest)
    {
        Assert.False(SelfUpdater.TryParseSha256($"{hash}  {rest}", out _));
    }

    [Fact]
    public void TryParseSha256_Valid_UppercaseNormalized()
    {
        const string sha = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";
        Assert.True(SelfUpdater.TryParseSha256($"{sha}  TextTool-CLI-2.4.0-win-x64.zip", out string hash));
        Assert.Equal(sha.ToLowerInvariant(), hash);
    }

    [Fact]
    public void FindCliAssetUrl_FindsMatchingAsset()
    {
        string json = $$"""
            {"assets": [
              {"name": "TextTool-GUI-2.4.0-win-x64.zip", "browser_download_url": "https://x/gui.zip"},
              {"name": "TextTool-CLI-2.4.0-win-x64.zip", "browser_download_url": "https://x/cli.zip"}
            ]}
            """;
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("https://x/cli.zip", SelfUpdater.FindCliAssetUrl(doc, "2.4.0"));
    }

    [Fact]
    public void FindCliAssetUrl_NoMatch_ReturnsNull()
    {
        string json = """
            {"assets": [{"name": "TextTool-CLI-2.3.0-win-x64.zip", "browser_download_url": "https://x/cli.zip"}]}
            """;
        using var doc = JsonDocument.Parse(json);
        Assert.Null(SelfUpdater.FindCliAssetUrl(doc, "2.4.0"));
    }

    [Fact]
    public void BuildUpdateScript_ContainsReplaceAndLaunch()
    {
        string script = SelfUpdater.BuildUpdateScript(
            installDir: @"C:\Tools\texttool",
            stagingDir: @"C:\Temp\texttool-update\2.4.0\staging",
            exePath: @"C:\Tools\texttool\texttool.exe",
            parentPid: 1234);

        Assert.Contains("tasklist", script);          // 等待父进程退出
        Assert.Contains("1234", script);              // 父 PID
        Assert.Contains("robocopy", script);          // 覆盖安装目录
        Assert.Contains(@"C:\Tools\texttool", script);
        Assert.Contains(@"texttool.exe", script);
    }

    [Fact]
    public void BuildUpdateScript_QuotesPathsWithSpaces()
    {
        // 安装目录含空格时，robocopy/start 的目标必须正确引用，否则脚本无法覆盖安装目录
        string script = SelfUpdater.BuildUpdateScript(
            installDir: @"C:\Tools My App\texttool",
            stagingDir: @"C:\Temp\texttool-update\2.4.0\staging",
            exePath: @"C:\Tools My App\texttool\texttool.exe",
            parentPid: 4321);

        Assert.Contains("robocopy \"C:\\Temp\\texttool-update\\2.4.0\\staging\" \"C:\\Tools My App\\texttool\"", script);
        Assert.Contains("start \"\" /d \"C:\\Tools My App\\texttool\" \"C:\\Tools My App\\texttool\\texttool.exe\"", script);
    }

    [Fact]
    public void BuildUpdateScript_EscapesPercentInPaths()
    {
        // cmd 中 % 在引号内仍会做环境变量展开（唯一可注入字符），必须转义为 %%
        string script = SelfUpdater.BuildUpdateScript(
            installDir: @"C:\Tools\50%Off\texttool",
            stagingDir: @"C:\Temp\texttool-update\2.4.0\staging",
            exePath: @"C:\Tools\50%Off\texttool\texttool.exe",
            parentPid: 1234);

        Assert.Contains(@"C:\Tools\50%%Off\texttool", script);
        Assert.DoesNotContain(@"C:\Tools\50%Off", script);
    }

    [Fact]
    public void BuildUpdateScript_HandlesRobocopyFailure()
    {
        string script = SelfUpdater.BuildUpdateScript(
            installDir: @"C:\Tools\texttool",
            stagingDir: @"C:\Temp\texttool-update\2.4.0\staging",
            exePath: @"C:\Tools\texttool\texttool.exe",
            parentPid: 1234);

        Assert.Contains("/r:1 /w:1", script);     // 限制重试，避免 8 小时挂起
        Assert.Contains("GEQ 8", script);         // 复制失败分支
        Assert.Contains("已保留暂存目录", script); // 失败时不清空 staging
    }
}
