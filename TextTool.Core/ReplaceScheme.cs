using System.Text.Json;

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

    /// <summary>资源文本只读一次；反序列化仍每次重做，见 GetDefaultSchemes。</summary>
    private static readonly Lazy<string> DefaultSchemesJson =
        new(() => EmbeddedResource.LoadText(ResourceName));

    /// <summary>
    /// 获取内置默认方案。资源必须可解析；缺失即抛，对齐 PinnedRoots。
    /// 每次调用返回新实例，保证调用方修改不影响内部定义——因此缓存的是资源文本，不是反序列化结果。
    /// </summary>
    public static List<ReplaceScheme> GetDefaultSchemes() => Deserialize(DefaultSchemesJson.Value);

    /// <summary>解析失败或结果为空即抛，与 <see cref="EmbeddedResource.LoadJsonList{T}"/> 同口径。</summary>
    private static List<ReplaceScheme> Deserialize(string json)
        => JsonSerializer.Deserialize<List<ReplaceScheme>>(json) is { Count: > 0 } schemes
            ? schemes
            : throw new InvalidOperationException($"嵌入式资源 {ResourceName} 为空或无法解析");
}
