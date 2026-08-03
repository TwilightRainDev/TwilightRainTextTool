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

    [Theory]
    [InlineData("2.4.3", true)]
    [InlineData("v2.4.3", true)]
    [InlineData("2.4", true)]                      // 二段（patch 视为 0）
    [InlineData("9999999999.0.0", false)]           // 溢出——不能再瘫痪更新通道
    [InlineData("-1.0.0", false)]                   // 负数
    [InlineData("2.4.3.1", false)]                  // 四段
    [InlineData("2.4.0beta", false)]                // 后缀垃圾
    [InlineData("2.4.0&calc", false)]               // 命令字符
    [InlineData("2.4.0%APPDATA%", false)]           // 环境变量字符
    [InlineData("..\\..\\2.4.0", false)]            // 路径穿越
    [InlineData("10000.0.0", false)]                // 分量超上限
    [InlineData("", false)]
    [InlineData("2.4.0 ", false)]                   // 尾部空格
    public void IsStrictVersion_ValidatesFormat(string version, bool expected)
    {
        Assert.Equal(expected, UpdateChecker.IsStrictVersion(version));
    }
}
