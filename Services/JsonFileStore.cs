using System.Text;
using System.Text.Json;

namespace TextTool.Services;

/// <summary>
/// 泛型 JSON 文件持久化 —— 消除 ReplaceRuleStore / ReplaceSchemeStore 中完全一致的
/// Load/Save 模板代码。新增 JSON 文件类型只需一行委托。
/// </summary>
public static class JsonFileStore
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>
    /// 从 AppContext.BaseDirectory 下的 JSON 文件加载列表。
    /// 文件不存在或格式损坏时返回 fallback（或空列表）。
    /// </summary>
    public static List<T> Load<T>(string fileName, Func<List<T>>? fallback = null)
    {
        string path = Path.Combine(AppContext.BaseDirectory, fileName);
        try
        {
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                var items = JsonSerializer.Deserialize<List<T>>(json);
                if (items != null) return items;
            }
        }
        catch { /* 文件损坏则走 fallback */ }

        return fallback?.Invoke() ?? new List<T>();
    }

    /// <summary>
    /// 保存列表到 AppContext.BaseDirectory 下的 JSON 文件（UTF-8 no BOM, 缩进）。
    /// </summary>
    public static void Save<T>(string fileName, List<T> items)
    {
        string path = Path.Combine(AppContext.BaseDirectory, fileName);
        string json = JsonSerializer.Serialize(items, Indented);
        File.WriteAllText(path, json, new UTF8Encoding(false));
    }
}
