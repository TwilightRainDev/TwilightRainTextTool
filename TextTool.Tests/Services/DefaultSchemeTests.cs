namespace TextTool.Tests.Services;

public class DefaultSchemeTests
{
    [Fact]
    public void ReplaceSchemeStore_GetDefaultSchemes_LoadsEmbeddedResource()
    {
        var schemes = ReplaceSchemeStore.GetDefaultSchemes();

        Assert.NotEmpty(schemes);
        Assert.All(schemes, s => Assert.True(s.IsBuiltIn));
        // 内置方案应含完整规则（资源必须可解析）
        Assert.Contains(schemes, s => s.Name.Contains("标点符号转换") && s.Rules.Count > 0);
    }

    [Fact]
    public void VNCharacterSchemeStore_GetDefaultSchemes_LoadsEmbeddedResource()
    {
        var schemes = VNCharacterSchemeStore.GetDefaultSchemes();

        Assert.NotEmpty(schemes);
        Assert.All(schemes, s => Assert.True(s.IsBuiltIn));
        // 资源必须可解析
        Assert.Contains(schemes, s => s.Name.Contains("Steins;Gate"));
    }

    [Fact]
    public void EmbeddedResource_MissingName_Throws()
    {
        const string missing = "TextTool.does_not_exist.json";
        var ex = Assert.Throws<InvalidOperationException>(
            () => EmbeddedResource.LoadJsonList<object>(missing));
        Assert.Contains(missing, ex.Message);
        Assert.Contains("缺少嵌入式资源", ex.Message);
    }

    [Fact]
    public void ReplaceSchemeStore_ResourceName_ResolvesToEmbeddedResource()
    {
        // 引用 store 自己的常量而非字面串；资源必须可解析
        // 用常量才能让查询串写错时测试真正失败
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
