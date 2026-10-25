using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using TextTool.Gui.Services;

namespace TextTool.Gui.Localization;

/// <summary>下拉框里的一项语言。显示名用它自己的语言写，不随界面语言变。</summary>
public sealed record LanguageOption(string Code, string Display);

/// <summary>
/// 界面文案。文案存在 Localization 下的三份 json 里，运行时嵌进程序集，
/// 按当前语言装成一份资源字典挂到 Application.Resources 上。
///
/// 视图里写 {DynamicResource 键名}，换语言时整份字典换掉，DynamicResource 自己重取。
/// 键名与 WinForms 版完全一致，迁移期间两边读的是同一批 json 文件。
/// </summary>
public static class Loc
{
    private const string ResourcePrefix = "TextTool.Gui.Localization.";

    private static Dictionary<string, string> Strings = new();
    private static ResourceDictionary? MountedDictionary;

    public static string CurrentLang { get; private set; } = "zh_CN";

    /// <summary>语言切换后触发。只在需要刷新代码里的文字时用，绑了 DynamicResource 的控件不用管。</summary>
    public static event Action? LanguageChanged;

    public static IReadOnlyList<LanguageOption> SupportedLanguages { get; } = new[]
    {
        new LanguageOption("zh_CN", "简体中文"),
        new LanguageOption("zh_TW", "繁體中文"),
        new LanguageOption("en_US", "English"),
    };

    /// <summary>启动时调用：有保存过的偏好就用它，没有就按系统语言猜。</summary>
    public static void Init(string? saved)
    {
        SetLanguage(string.IsNullOrWhiteSpace(saved) ? DetectSystemLanguage() : saved, persist: false);
    }

    public static string DetectSystemLanguage()
    {
        string name = CultureInfo.CurrentUICulture.Name.ToUpperInvariant();

        if (name.StartsWith("ZH-HANT", StringComparison.Ordinal) ||
            name.StartsWith("ZH-TW", StringComparison.Ordinal) ||
            name.StartsWith("ZH-HK", StringComparison.Ordinal) ||
            name.StartsWith("ZH-MO", StringComparison.Ordinal))
            return "zh_TW";
        if (name.StartsWith("ZH", StringComparison.Ordinal))
            return "zh_CN";
        if (name.StartsWith("EN", StringComparison.Ordinal))
            return "en_US";

        return "zh_CN";
    }

    public static void SetLanguage(string lang) => SetLanguage(lang, persist: true);

    private static void SetLanguage(string lang, bool persist)
    {
        CurrentLang = SupportedLanguages.Any(l => l.Code == lang) ? lang : "zh_CN";
        Strings = LoadStrings(CurrentLang);
        Mount();

        if (persist)
            AppSettings.SetLanguage(CurrentLang);

        LanguageChanged?.Invoke();
    }

    /// <summary>取一条文案。缺键时返回 [键名]，一眼能看出漏翻了什么。</summary>
    public static string T(string key) =>
        Strings.TryGetValue(key, out string? value) ? value : $"[{key}]";

    /// <summary>取一条带占位符的文案。翻译串缺占位符时回退原文，不抛异常。</summary>
    public static string T(string key, params object[] args)
    {
        string format = T(key);
        try
        {
            return string.Format(format, args);
        }
        catch (FormatException)
        {
            return format;
        }
    }

    private static Dictionary<string, string> LoadStrings(string lang)
    {
        string name = $"{ResourcePrefix}{lang}.json";
        using Stream? stream = typeof(Loc).Assembly.GetManifestResourceStream(name);

        if (stream is null)
        {
            // 找不到就退回简体，不回退成空表：界面宁可显示中文，也不要满屏 [键名]。
            using Stream? fallback = typeof(Loc).Assembly.GetManifestResourceStream($"{ResourcePrefix}zh_CN.json");
            return fallback is null
                ? new Dictionary<string, string>()
                : Deserialize(fallback);
        }

        return Deserialize(stream);
    }

    private static Dictionary<string, string> Deserialize(Stream stream)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
                   ?? new Dictionary<string, string>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>();
        }
    }

    /// <summary>把当前语言的文案挂成资源字典。整份替换而不是逐条改，避免半新半旧。</summary>
    private static void Mount()
    {
        if (Application.Current is not { } app)
            return;

        var dictionary = new ResourceDictionary();
        foreach ((string key, string value) in Strings)
            dictionary[key] = value;

        if (MountedDictionary is not null)
            app.Resources.MergedDictionaries.Remove(MountedDictionary);

        app.Resources.MergedDictionaries.Add(dictionary);
        MountedDictionary = dictionary;
    }
}
