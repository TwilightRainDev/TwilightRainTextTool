namespace TextTool.Tests.Services;

public class DefaultSchemeTests
{
    [Fact]
    public void ReplaceSchemeStore_GetDefaultSchemes_LoadsEmbeddedResource()
    {
        var schemes = ReplaceSchemeStore.GetDefaultSchemes();

        Assert.NotEmpty(schemes);
        Assert.All(schemes, s => Assert.True(s.IsBuiltIn));
        // 内置方案应含完整规则（若资源丢失会回退硬编码，此处验证资源路径正确）
        Assert.Contains(schemes, s => s.Name.Contains("标点符号转换") && s.Rules.Count > 0);
    }

    [Fact]
    public void VNCharacterSchemeStore_GetDefaultSchemes_LoadsEmbeddedResource()
    {
        var schemes = VNCharacterSchemeStore.GetDefaultSchemes();

        Assert.NotEmpty(schemes);
        Assert.All(schemes, s => Assert.True(s.IsBuiltIn));
        Assert.Contains(schemes, s => s.Name.Contains("Steins;Gate"));
    }
}
