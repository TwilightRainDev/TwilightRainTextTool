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
    public void ReplaceSchemeStore_ResourceName_ResolvesToEmbeddedResource()
    {
        // 引用 store 自己的常量而非字面串：兜底数据与 JSON 相同，只断言「有方案」
        // 无法区分两条路径，故直查资源；用常量才能让查询串写错时测试真正失败
        var stream = typeof(ReplaceSchemeStore).Assembly
            .GetManifestResourceStream(ReplaceSchemeStore.ResourceName);

        Assert.NotNull(stream);
    }

    [Fact]
    public void VNCharacterSchemeStore_ResourceName_ResolvesToEmbeddedResource()
    {
        var stream = typeof(VNCharacterSchemeStore).Assembly
            .GetManifestResourceStream(VNCharacterSchemeStore.ResourceName);

        Assert.NotNull(stream);
    }
}
