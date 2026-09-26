namespace TextTool.Services;

/// <summary>
/// 替换方案 — 一组命名的替换规则集合
/// </summary>
public class ReplaceScheme
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsBuiltIn { get; set; }
    public List<ReplaceRule> Rules { get; set; } = new();
}

/// <summary>
/// 替换方案持久化存储 + 默认方案定义
/// </summary>
public static class ReplaceSchemeStore
{
    /// <summary>
    /// 嵌入式资源名。前缀取 csproj 的 RootNamespace（TextTool），不是程序集名（TextTool.Core）——
    /// 资源名由 RootNamespace + 文件名生成，写成程序集名会查不到。缺失即抛，对齐 PinnedRoots。
    /// </summary>
    internal const string ResourceName = "TextTool.default_schemes.json";

    public static List<ReplaceScheme> Load()
    {
        var schemes = JsonFileStore.Load<ReplaceScheme>("replace_schemes.json");
        return schemes.Count > 0 ? schemes : GetDefaultSchemes();
    }

    public static void Save(List<ReplaceScheme> schemes) => JsonFileStore.Save("replace_schemes.json", schemes);

    /// <summary>
    /// 获取内置默认方案。资源必须可解析；缺失即抛，对齐 PinnedRoots。
    /// 每次调用返回新实例，保证调用方修改不影响内部定义。
    /// </summary>
    public static List<ReplaceScheme> GetDefaultSchemes()
    {
        return EmbeddedResource.LoadJsonList<ReplaceScheme>(ResourceName);
    }
}
