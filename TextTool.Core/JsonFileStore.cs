using System.Text;
using System.Text.Json;

namespace TextTool.Services;

/// <summary>
/// 泛型 JSON 文件持久化 —— 消除 ReplaceRuleStore / ReplaceSchemeStore 中完全一致的
/// Load/Save 模板代码。新增 JSON 文件类型只需一行委托。
///
/// 配置文件与程序本体同目录（单目录哲学，见 ADR-009）。错误策略：
/// "文件不存在" 视为首次使用（返回 fallback/空列表）；
/// "文件存在但损坏" 视为数据异常，显式抛 InvalidDataException——
/// 绝不静默降级为无规则运行。保存走临时文件 + 原子替换，避免崩溃留下半写配置。
/// </summary>
public static class JsonFileStore
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>加载列表。baseDir 仅测试注入用，生产路径固定为程序目录。</summary>
    public static List<T> Load<T>(string fileName, Func<List<T>>? fallback = null, string? baseDir = null)
    {
        string path = Path.Combine(baseDir ?? AppContext.BaseDirectory, fileName);
        if (!File.Exists(path))
            return fallback?.Invoke() ?? new List<T>();

        try
        {
            string json = File.ReadAllText(path, Encoding.UTF8);
            return JsonSerializer.Deserialize<List<T>>(json) ?? new List<T>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new InvalidDataException(
                $"配置文件损坏：{fileName}（无法解析；请用「配置导入/导出」功能恢复，或删除该文件后重新配置）", ex);
        }
    }

    /// <summary>保存列表到程序目录（原子替换，UTF-8 no BOM, 缩进）。</summary>
    public static void Save<T>(string fileName, List<T> items, string? baseDir = null)
    {
        string path = Path.Combine(baseDir ?? AppContext.BaseDirectory, fileName);
        string json = JsonSerializer.Serialize(items, Indented);
        AtomicFile.WriteAllText(path, json, new UTF8Encoding(false));
    }
}
