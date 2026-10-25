using System.Text;
using System.Text.Json;
using TextTool.Services;

namespace TextTool.Gui.Services;

/// <summary>
/// 界面偏好（深浅色、语言），存在程序目录的 app_config.json 里。
///
/// 键名与 WinForms 版完全一致："darkMode" 和 "language"，这样迁移期间两边能读同一份
/// 配置，用户切换过去的版本不用重新设置。读配置失败一律回落默认值：这是纯外观偏好，
/// 不值得为它在启动时弹错误框。
/// </summary>
public static class AppSettings
{
    private const string FileName = "app_config.json";

    public static bool DarkMode { get; private set; }
    public static string? Language { get; private set; }

    private static string ConfigPath => Path.Combine(AppContext.BaseDirectory, FileName);

    public static void Load()
    {
        try
        {
            if (!File.Exists(ConfigPath))
                return;

            using var doc = JsonDocument.Parse(File.ReadAllText(ConfigPath, Encoding.UTF8));
            if (doc.RootElement.TryGetProperty("darkMode", out var dark))
                DarkMode = dark.GetBoolean();
            if (doc.RootElement.TryGetProperty("language", out var lang))
                Language = lang.GetString();
        }
        catch
        {
            // 配置坏了就用默认值，不打断启动。
        }
    }

    public static void SetDarkMode(bool dark)
    {
        DarkMode = dark;
        Save();
    }

    public static void Save()
    {
        try
        {
            var config = new Dictionary<string, object>
            {
                ["language"] = Language ?? "zh_CN",
                ["darkMode"] = DarkMode
            };
            AtomicFile.WriteAllText(ConfigPath, JsonSerializer.Serialize(config), Encoding.UTF8);
        }
        catch
        {
            // 写不进去只影响下次启动是否记得这个偏好，不该让用户看到错误。
        }
    }
}
