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

    [Fact]
    public void ReplaceSchemeStore_EmbeddedResourceName_IsResolvable()
    {
        // 直接查资源，绕过 store 的兜底逻辑——兜底数据与 JSON 相同，
        // 只断言「有方案」无法区分两条路径，因此必须直查资源名
        var stream = typeof(ReplaceSchemeStore).Assembly
            .GetManifestResourceStream("TextTool.default_schemes.json");

        Assert.NotNull(stream);
    }

    [Fact]
    public void VNCharacterSchemeStore_EmbeddedResourceName_IsResolvable()
    {
        var stream = typeof(VNCharacterSchemeStore).Assembly
            .GetManifestResourceStream("TextTool.default_vn_schemes.json");

        Assert.NotNull(stream);
    }
}
