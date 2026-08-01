using System.Reflection;
using System.Text;
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
    public static List<ReplaceScheme> Load()
    {
        var schemes = JsonFileStore.Load<ReplaceScheme>("replace_schemes.json");
        return schemes.Count > 0 ? schemes : GetDefaultSchemes();
    }

    public static void Save(List<ReplaceScheme> schemes) => JsonFileStore.Save("replace_schemes.json", schemes);

    /// <summary>
    /// 获取内置默认方案。优先从嵌入式资源加载；资源缺失时回退到内联数据。
    /// 每次调用返回新实例，保证调用方修改不影响内部定义。
    /// </summary>
    public static List<ReplaceScheme> GetDefaultSchemes()
    {
        // 尝试从嵌入式资源加载
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream("TextTool.Core.default_schemes.json");
            if (stream != null)
            {
                using var reader = new StreamReader(stream, Encoding.UTF8);
                string json = reader.ReadToEnd();
                var schemes = JsonSerializer.Deserialize<List<ReplaceScheme>>(json);
                if (schemes != null && schemes.Count > 0)
                    return schemes;
            }
        }
        catch { /* 资源加载失败则回退到内联数据 */ }

        return GetHardcodedSchemes();
    }

    /// <summary>
    /// 内联兜底方案（嵌入式资源不存在时使用）
    /// </summary>
    private static List<ReplaceScheme> GetHardcodedSchemes()
    {
        return new List<ReplaceScheme>
        {
            new()
            {
                Name = "标点符号转换",
                Description = "英文标点符号转中文标点（. → 。  , → ，  ! → ！  ? → ？  ; → ；  : → ：）",
                IsBuiltIn = true,
                Rules = new List<ReplaceRule>
                {
                    new() { Find = ".", Replace = "。" },
                    new() { Find = ",", Replace = "，" },
                    new() { Find = "!", Replace = "！" },
                    new() { Find = "?", Replace = "？" },
                    new() { Find = ";", Replace = "；" },
                    new() { Find = ":", Replace = "：" },
                }
            },
            new()
            {
                Name = "引号括号统一",
                Description = "各种引号/方括号/花括号统一为「」，中文括号（）转半角",
                IsBuiltIn = true,
                Rules = new List<ReplaceRule>
                {
                    new() { Find = "“", Replace = "「" },
                    new() { Find = "”", Replace = "」" },
                    new() { Find = "‘", Replace = "「" },
                    new() { Find = "’", Replace = "」" },
                    new() { Find = "『", Replace = "「" },
                    new() { Find = "』", Replace = "」" },
                    new() { Find = "〖", Replace = "「" },
                    new() { Find = "〗", Replace = "」" },
                    new() { Find = "【", Replace = "「" },
                    new() { Find = "】", Replace = "」" },
                    new() { Find = "〔", Replace = "「" },
                    new() { Find = "〕", Replace = "」" },
                    new() { Find = "[", Replace = "「" },
                    new() { Find = "]", Replace = "」" },
                    new() { Find = "{", Replace = "「" },
                    new() { Find = "}", Replace = "」" },
                    new() { Find = "（", Replace = "(" },
                    new() { Find = "）", Replace = ")" },
                }
            },
            new()
            {
                Name = "特殊符号清除",
                Description = "删除各类装饰符号、图形符号、音乐符号等特殊字符",
                IsBuiltIn = true,
                Rules = new List<ReplaceRule>
                {
                    new() { Find = "■", Replace = "" }, new() { Find = "□", Replace = "" },
                    new() { Find = "○", Replace = "" }, new() { Find = "●", Replace = "" },
                    new() { Find = "◎", Replace = "" }, new() { Find = "◯", Replace = "" },
                    new() { Find = "△", Replace = "" }, new() { Find = "▲", Replace = "" },
                    new() { Find = "▽", Replace = "" }, new() { Find = "▼", Replace = "" },
                    new() { Find = "◇", Replace = "" }, new() { Find = "◆", Replace = "" },
                    new() { Find = "★", Replace = "" }, new() { Find = "☆", Replace = "" },
                    new() { Find = "·", Replace = "" }, new() { Find = "*", Replace = "" },
                    new() { Find = "《", Replace = "" }, new() { Find = "》", Replace = "" },
                    new() { Find = "<", Replace = "" }, new() { Find = ">", Replace = "" },
                    new() { Find = "♪", Replace = "。" }, new() { Find = "♭", Replace = "。" },
                    new() { Find = "♯", Replace = "。" },
                }
            },
            new()
            {
                Name = "横线省略号→逗号",
                Description = "各种破折号、连接线、省略号等统一转为中文逗号",
                IsBuiltIn = true,
                Rules = new List<ReplaceRule>
                {
                    new() { Find = "--", Replace = "，" }, new() { Find = "……", Replace = "，" },
                    new() { Find = "…", Replace = "，" }, new() { Find = "...", Replace = "，" },
                    new() { Find = "⋮", Replace = "，" }, new() { Find = "⋯", Replace = "，" },
                    new() { Find = "⋰", Replace = "，" }, new() { Find = "⋱", Replace = "，" },
                    new() { Find = "——", Replace = "，" }, new() { Find = "—", Replace = "，" },
                    new() { Find = "–", Replace = "，" }, new() { Find = "-", Replace = "，" },
                    new() { Find = "―", Replace = "，" },
                }
            },
            new()
            {
                Name = "重复矛盾标点整理",
                Description = "合并重复标点（、、→、  ，，→，  。。→。  ？？→？），整理逗号句号混乱等",
                IsBuiltIn = true,
                Rules = new List<ReplaceRule>
                {
                    new() { Find = "、、", Replace = "、" }, new() { Find = "、", Replace = "，" },
                    new() { Find = "，，", Replace = "，" }, new() { Find = "。。", Replace = "。" },
                    new() { Find = "？？", Replace = "？" }, new() { Find = "。，", Replace = "。" },
                    new() { Find = "，。", Replace = "。" }, new() { Find = "？，", Replace = "？" },
                    new() { Find = "，？", Replace = "？" }, new() { Find = "。？", Replace = "？" },
                    new() { Find = "？。", Replace = "？" }, new() { Find = "「，", Replace = "「" },
                    new() { Find = "「。", Replace = "「" }, new() { Find = "，」", Replace = "。」" },
                    new() { Find = "  ", Replace = " " },
                }
            },
            new()
            {
                Name = "全角字母数字→半角",
                Description = "将全角英文字母和数字转为半角（Ａ→A  ａ→a  ０→0）",
                IsBuiltIn = true,
                Rules = new List<ReplaceRule>
                {
                    new() { Find = "Ａ", Replace = "A" }, new() { Find = "Ｂ", Replace = "B" },
                    new() { Find = "Ｃ", Replace = "C" }, new() { Find = "Ｄ", Replace = "D" },
                    new() { Find = "Ｅ", Replace = "E" }, new() { Find = "Ｆ", Replace = "F" },
                    new() { Find = "Ｇ", Replace = "G" }, new() { Find = "Ｈ", Replace = "H" },
                    new() { Find = "Ｉ", Replace = "I" }, new() { Find = "Ｊ", Replace = "J" },
                    new() { Find = "Ｋ", Replace = "K" }, new() { Find = "Ｌ", Replace = "L" },
                    new() { Find = "Ｍ", Replace = "M" }, new() { Find = "Ｎ", Replace = "N" },
                    new() { Find = "Ｏ", Replace = "O" }, new() { Find = "Ｐ", Replace = "P" },
                    new() { Find = "Ｑ", Replace = "Q" }, new() { Find = "Ｒ", Replace = "R" },
                    new() { Find = "Ｓ", Replace = "S" }, new() { Find = "Ｔ", Replace = "T" },
                    new() { Find = "Ｕ", Replace = "U" }, new() { Find = "Ｖ", Replace = "V" },
                    new() { Find = "Ｗ", Replace = "W" }, new() { Find = "Ｘ", Replace = "X" },
                    new() { Find = "Ｙ", Replace = "Y" }, new() { Find = "Ｚ", Replace = "Z" },
                    new() { Find = "ａ", Replace = "a" }, new() { Find = "ｂ", Replace = "b" },
                    new() { Find = "ｃ", Replace = "c" }, new() { Find = "ｄ", Replace = "d" },
                    new() { Find = "ｅ", Replace = "e" }, new() { Find = "ｆ", Replace = "f" },
                    new() { Find = "ｇ", Replace = "g" }, new() { Find = "ｈ", Replace = "h" },
                    new() { Find = "ｉ", Replace = "i" }, new() { Find = "ｊ", Replace = "j" },
                    new() { Find = "ｋ", Replace = "k" }, new() { Find = "ｌ", Replace = "l" },
                    new() { Find = "ｍ", Replace = "m" }, new() { Find = "ｎ", Replace = "n" },
                    new() { Find = "ｏ", Replace = "o" }, new() { Find = "ｐ", Replace = "p" },
                    new() { Find = "ｑ", Replace = "q" }, new() { Find = "ｒ", Replace = "r" },
                    new() { Find = "ｓ", Replace = "s" }, new() { Find = "ｔ", Replace = "t" },
                    new() { Find = "ｕ", Replace = "u" }, new() { Find = "ｖ", Replace = "v" },
                    new() { Find = "ｗ", Replace = "w" }, new() { Find = "ｘ", Replace = "x" },
                    new() { Find = "ｙ", Replace = "y" }, new() { Find = "ｚ", Replace = "z" },
                    new() { Find = "０", Replace = "0" }, new() { Find = "１", Replace = "1" },
                    new() { Find = "２", Replace = "2" }, new() { Find = "３", Replace = "3" },
                    new() { Find = "４", Replace = "4" }, new() { Find = "５", Replace = "5" },
                    new() { Find = "６", Replace = "6" }, new() { Find = "７", Replace = "7" },
                    new() { Find = "８", Replace = "8" }, new() { Find = "９", Replace = "9" },
                }
            },
            new()
            {
                Name = "阿拉伯数字→中文",
                Description = "将阿拉伯数字转为中文数字（0→零  1→一  ...  9→九）",
                IsBuiltIn = true,
                Rules = new List<ReplaceRule>
                {
                    new() { Find = "0", Replace = "零" }, new() { Find = "1", Replace = "一" },
                    new() { Find = "2", Replace = "二" }, new() { Find = "3", Replace = "三" },
                    new() { Find = "4", Replace = "四" }, new() { Find = "5", Replace = "五" },
                    new() { Find = "6", Replace = "六" }, new() { Find = "7", Replace = "七" },
                    new() { Find = "8", Replace = "八" }, new() { Find = "9", Replace = "九" },
                }
            },
            new()
            {
                Name = "语气词清理",
                Description = "清理口语中的语气词及常见无意义填充词",
                IsBuiltIn = true,
                Rules = new List<ReplaceRule>
                {
                    new() { Find = "哈哈", Replace = "" }, new() { Find = "嗯，", Replace = "，" },
                    new() { Find = "喔。", Replace = "。" }, new() { Find = "咦，", Replace = "，" },
                    new() { Find = "啊，", Replace = "，" }, new() { Find = "吧。", Replace = "。" },
                    new() { Find = "呀，", Replace = "，" }, new() { Find = "唉，", Replace = "，" },
                    new() { Find = "啦，", Replace = "，" }, new() { Find = "喂，", Replace = "，" },
                    new() { Find = "啊。", Replace = "。" }, new() { Find = "哪，", Replace = "，" },
                    new() { Find = "啦。", Replace = "。" }, new() { Find = "呀。", Replace = "。" },
                    new() { Find = "啊啊", Replace = "啊" }, new() { Find = "唉。", Replace = "" },
                    new() { Find = "嗳，", Replace = "" }, new() { Find = "哎唷", Replace = "" },
                    new() { Find = "唷", Replace = "" }, new() { Find = "我的天。", Replace = "" },
                    new() { Find = "不，不。", Replace = "不，" }, new() { Find = "不，不，", Replace = "不，" },
                    new() { Find = "哩。", Replace = "。" }, new() { Find = "噢，", Replace = "，" },
                }
            },
            new()
            {
                Name = "词语替换",
                Description = "常见中英文词语替换（chapter→章节  ※→注）",
                IsBuiltIn = true,
                Rules = new List<ReplaceRule>
                {
                    new() { Find = "chapter", Replace = "章节" }, new() { Find = "Chapter", Replace = "章节" },
                    new() { Find = "这一点", Replace = "这点" }, new() { Find = "一个接一个", Replace = "挨个" },
                    new() { Find = "※", Replace = "注" },
                }
            },
        };
    }
}
