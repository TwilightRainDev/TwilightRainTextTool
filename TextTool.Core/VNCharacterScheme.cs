namespace TextTool.Services;

/// <summary>
/// 视觉小说角色预设方案 — 一组命名的角色集 + 路线名集合。
/// </summary>
public class VNCharacterScheme
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsBuiltIn { get; set; }
    public List<string> Characters { get; set; } = new();
    public List<string> RouteNames { get; set; } = new();

    /// <summary>
    /// 场景标记正则（可选）。空/未设置时使用引擎通用模式 `── 场景 ──`。
    /// 例：Steins;Gate 的 `SGFD_[A-Z]+[...]`。
    /// </summary>
    public string? ScenePattern { get; set; }
}

/// <summary>
/// 角色方案持久化存储 + 默认方案定义。
/// 文件: vn_schemes.json（AppContext.BaseDirectory）
/// </summary>
public static class VNCharacterSchemeStore
{
    /// <summary>
    /// 嵌入式资源名。前缀取 csproj 的 RootNamespace（TextTool），不是程序集名（TextTool.Core）——
    /// 资源名由 RootNamespace + 文件名生成，写成程序集名会查不到。缺失即抛，对齐 PinnedRoots。
    /// </summary>
    internal const string ResourceName = "TextTool.default_vn_schemes.json";

    public static List<VNCharacterScheme> Load()
    {
        var schemes = JsonFileStore.Load<VNCharacterScheme>("vn_schemes.json");
        return schemes.Count > 0 ? schemes : GetDefaultSchemes();
    }

    public static void Save(List<VNCharacterScheme> schemes) =>
        JsonFileStore.Save("vn_schemes.json", schemes);

    /// <summary>
    /// 获取内置默认方案。资源必须可解析；缺失即抛，对齐 PinnedRoots。
    /// 每次调用返回新实例。
    /// </summary>
    public static List<VNCharacterScheme> GetDefaultSchemes()
    {
        return EmbeddedResource.LoadJsonList<VNCharacterScheme>(ResourceName);
    }
}
