using System.Reflection;
using System.Text;
using System.Text.Json;

namespace TextTool.Services;

internal static class EmbeddedResource
{
    /// <summary>读资源的原始文本。调用方要缓存资源文本（而非反序列化结果）时用它。</summary>
    internal static string LoadText(string resourceName) => ReadResourceText(resourceName);

    public static List<T> LoadJsonList<T>(string resourceName)
    {
        // 空/解析失败的非空校验留在本方法：LoadText 允许返回空串，两条调用点的语义不同
        var list = JsonSerializer.Deserialize<List<T>>(ReadResourceText(resourceName));
        if (list is null || list.Count == 0)
            throw new InvalidOperationException($"嵌入式资源 {resourceName} 为空或无法解析");
        return list;
    }

    /// <summary>取资源流并以 UTF-8 读成原始文本集。两个 Load 共用的同一段读取。</summary>
    private static string ReadResourceText(string resourceName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"缺少嵌入式资源 {resourceName}，请检查 csproj 的 EmbeddedResource 配置");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
