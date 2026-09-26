using System.Reflection;
using System.Text;
using System.Text.Json;

namespace TextTool.Services;

internal static class EmbeddedResource
{
    public static List<T> LoadJsonList<T>(string resourceName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"缺少嵌入式资源 {resourceName}，请检查 csproj 的 EmbeddedResource 配置");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var list = JsonSerializer.Deserialize<List<T>>(reader.ReadToEnd());
        if (list is null || list.Count == 0)
            throw new InvalidOperationException($"嵌入式资源 {resourceName} 为空或无法解析");
        return list;
    }
}
