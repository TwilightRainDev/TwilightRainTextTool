using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TextTool.Localization;

/// <summary>
/// 语言信息
/// </summary>
public class LanguageInfo
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
}

/// <summary>
/// 国际化本地化管理器（单例）。
/// 支持 zh_CN / zh_TW / en_US，自动检测系统语言，用户可手动切换。
/// 语言偏好持久化至 app_config.json。
/// </summary>
public static class Loc
{
    private static Dictionary<string, string> _strings = new();
    private static string _currentLang = "zh_CN";

    /// <summary>当前语言代码</summary>
    public static string CurrentLang => _currentLang;

    /// <summary>语言切换时触发</summary>
    public static event Action? LanguageChanged;

    /// <summary>
    /// 初始化：加载保存的语言偏好或自动检测系统语言
    /// </summary>
    public static void Init()
    {
        string saved = LoadSavedLanguage();
        SetLanguage(string.IsNullOrEmpty(saved) ? DetectSystemLanguage() : saved);
    }

    /// <summary>
    /// 根据当前系统 UI 文化检测应使用的语言
    /// </summary>
    public static string DetectSystemLanguage()
    {
        var culture = CultureInfo.CurrentUICulture;
        var name = culture.Name.ToUpperInvariant();

        if (name.StartsWith("ZH-HANT") || name.StartsWith("ZH-TW") ||
            name.StartsWith("ZH-HK") || name.StartsWith("ZH-MO"))
            return "zh_TW";
        if (name.StartsWith("ZH"))
            return "zh_CN";
        if (name.StartsWith("EN"))
            return "en_US";

        return "zh_CN";
    }

    /// <summary>
    /// 获取支持的语言列表（供 UI 下拉框使用）
    /// </summary>
    public static List<LanguageInfo> GetSupportedLanguages() => new()
    {
        new LanguageInfo { Code = "zh_CN", Name = T("LangZhCN") },
        new LanguageInfo { Code = "zh_TW", Name = T("LangZhTW") },
        new LanguageInfo { Code = "en_US", Name = T("LangEnUS") },
    };

    /// <summary>
    /// 切换语言
    /// </summary>
    public static void SetLanguage(string lang)
    {
        string file = lang switch
        {
            "zh_TW" => "zh_TW.json",
            "en_US" => "en_US.json",
            _ => "zh_CN.json",
        };

        string path = Path.Combine(AppContext.BaseDirectory, "Localization", file);
        if (File.Exists(path))
        {
            string json = File.ReadAllText(path, Encoding.UTF8);
            _strings = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
        }
        else
        {
            _strings = new Dictionary<string, string>();
        }

        _currentLang = lang;
        SaveLanguage(lang);
        LanguageChanged?.Invoke();
    }

    /// <summary>
    /// 获取翻译字符串
    /// </summary>
    public static string T(string key)
    {
        return _strings.TryGetValue(key, out var value) ? value : $"[{key}]";
    }

    /// <summary>
    /// 获取翻译字符串并格式化
    /// </summary>
    public static string T(string key, params object[] args)
    {
        string format = T(key);
        return string.Format(format, args);
    }

    // ================================================================
    //  持久化
    // ================================================================

    private static string ConfigPath =>
        Path.Combine(AppContext.BaseDirectory, "app_config.json");

    private static void SaveLanguage(string lang)
    {
        try
        {
            var config = new Dictionary<string, string> { ["language"] = lang };
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config), Encoding.UTF8);
        }
        catch { /* 写入失败不阻断程序 */ }
    }

    private static string LoadSavedLanguage()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                string json = File.ReadAllText(ConfigPath, Encoding.UTF8);
                var config = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (config != null && config.TryGetValue("language", out var lang))
                    return lang;
            }
        }
        catch { /* 配置文件损坏则忽略 */ }
        return "";
    }
}
