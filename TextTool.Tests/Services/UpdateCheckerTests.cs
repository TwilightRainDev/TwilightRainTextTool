namespace TextTool.Tests.Services;

public class UpdateCheckerTests
{
    [Theory]
    [InlineData("2.3.0", "2.3.1", true)]
    [InlineData("2.3.0", "2.4.0", true)]
    [InlineData("2.3.0", "3.0.0", true)]
    [InlineData("2.3.0", "2.3.0", false)]
    [InlineData("2.3.0", "2.2.9", false)]
    [InlineData("v2.3.0", "v2.3.1", true)] // 带 v 前缀
    [InlineData("2.3", "2.3.1", true)]     // 缺少 patch 视为 0
    public void IsNewer_ComparesVersions(string current, string latest, bool expected)
    {
        Assert.Equal(expected, UpdateChecker.IsNewer(current, latest));
    }

    [Theory]
    [InlineData("abc", "2.3.0", false)]  // 非法当前版本
    [InlineData("2.3.0", "xyz", false)]  // 非法最新版本
    [InlineData("", "2.3.0", false)]     // 空字符串
    public void IsNewer_InvalidVersions_ReturnsFalse(string current, string latest, bool expected)
    {
        Assert.Equal(expected, UpdateChecker.IsNewer(current, latest));
    }
}
