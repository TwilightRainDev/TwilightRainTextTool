using System.Text;
using System.Text.Json;
using TextTool.Localization;

namespace TextTool.Services;

/// <summary>
/// 全局主题管理器 — 管理深色/浅色模式状态与持久化。
///
/// 语义化色板：
///   Bg/Fg         — 主背景/前景
///   ControlBg     — 输入控件背景（文本框、下拉框、列表等）
///   MutedFg       — 次要文字（提示、状态说明等）
///
/// 按钮的反转配色（白天黑底白字，深色白底黑字）由 ControlsHelper 提供，
/// 属于渲染层决策，ThemeManager 只提供基础色板。
/// </summary>
public static class ThemeManager
{
    public static bool IsDarkMode { get; private set; }

    // ===== 原始色板 =====
    public static readonly Color DarkBg = Color.FromArgb(30, 30, 30);
    public static readonly Color DarkFg = Color.FromArgb(220, 220, 220);
    public static readonly Color DarkControlBg = Color.FromArgb(45, 45, 45);
    public static readonly Color LightBg = Color.White;
    public static readonly Color LightFg = Color.Black;
    public static readonly Color LightControlBg = Color.White;

    // ===== 语义化属性 =====

    /// <summary>主背景色</summary>
    public static Color Bg => IsDarkMode ? DarkBg : LightBg;
    /// <summary>主前景色</summary>
    public static Color Fg => IsDarkMode ? DarkFg : LightFg;
    /// <summary>输入控件背景色（文本框、下拉框、列表等）</summary>
    public static Color ControlBg => IsDarkMode ? DarkControlBg : LightControlBg;
    /// <summary>次要文字色（提示、状态说明等）</summary>
    public static Color MutedFg => IsDarkMode ? Color.FromArgb(180, 180, 180) : Color.Gray;

    /// <summary>主题切换时触发</summary>
    public static event Action<bool>? ThemeChanged;

    /// <summary>Init() 从配置文件中读到的语言偏好，供 Loc.Init() 使用以避免重复读文件</summary>
    internal static string? InitialLanguage { get; private set; }

    private static string ConfigPath =>
        Path.Combine(AppContext.BaseDirectory, "app_config.json");

    /// <summary>
    /// 从配置文件加载深色模式偏好和语言偏好。
    /// 必须在 Loc.Init() 之前调用。
    /// </summary>
    public static void Init()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                string json = File.ReadAllText(ConfigPath, Encoding.UTF8);
                var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("darkMode", out var dm))
                    IsDarkMode = dm.GetBoolean();
                if (doc.RootElement.TryGetProperty("language", out var lang))
                    InitialLanguage = lang.GetString();
            }
        }
        catch { }
    }

    /// <summary>切换深色/浅色模式，保存偏好并触发 ThemeChanged 事件。</summary>
    public static void Toggle()
    {
        IsDarkMode = !IsDarkMode;
        SaveAll();
        ThemeChanged?.Invoke(IsDarkMode);
    }

    /// <summary>将语言 + 深色模式持久化到 app_config.json</summary>
    public static void SaveAll()
    {
        try
        {
            var config = new Dictionary<string, object>
            {
                ["language"] = Loc.CurrentLang,
                ["darkMode"] = IsDarkMode
            };
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config), Encoding.UTF8);
        }
        catch { }
    }
}
