using System.Text;
using System.Text.Json;

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
    public static List<VNCharacterScheme> Load()
    {
        var schemes = JsonFileStore.Load<VNCharacterScheme>("vn_schemes.json");
        return schemes.Count > 0 ? schemes : GetDefaultSchemes();
    }

    public static void Save(List<VNCharacterScheme> schemes) =>
        JsonFileStore.Save("vn_schemes.json", schemes);

    /// <summary>获取内置默认方案（每次返回新实例）</summary>
    public static List<VNCharacterScheme> GetDefaultSchemes()
    {
        return new List<VNCharacterScheme>
        {
            new()
            {
                Name = "Steins;Gate 默认角色",
                Description = "命运石之门全角色集（47 个角色 + 7 条路线）",
                IsBuiltIn = true,
                ScenePattern = @"SGFD_[A-Z]+[〇零一二三四五六七八九十百千万\d]+",
                Characters = new List<string>
                {
                    "伦太郎", "真由理", "红莉栖", "琉华", "萌郁", "铃羽",
                    "菲利丝", "至", "达鲁", "天王寺", "桐生萌郁", "漆原琉华",
                    "桥田至", "牧濑红莉栖", "阿万音铃羽", "菲利斯", "比屋定真帆",
                    "陌生人", "中年男子", "打工战士", "四摄氏度", "猫耳", "优雅",
                    "眼镜", "麻花辫", "护士", "岡部", "女性", "男性", "少女",
                    "少年", "店主", "大叔", "阿姨", "男子", "由季", "绹",
                    "漆原父", "编辑", "编辑二", "女子A", "女子B", "随从A", "随从B",
                    "男人", "管家",
                },
                RouteNames = new List<string>
                {
                    "序章共通线", "牧濑红莉栖线", "椎名真由理线", "漆原琉华线",
                    "桐生萌郁线", "菲利斯线", "阿万音铃羽线",
                }
            },
            new()
            {
                Name = "进击的巨人",
                Description = "进击的巨人主要角色（5 个角色）",
                IsBuiltIn = true,
                Characters = new List<string>
                {
                    "艾伦", "三笠", "阿尔敏", "利威尔", "爱尔敏",
                }
            },
            new()
            {
                Name = "空之境界",
                Description = "空之境界主要角色（8 个角色）",
                IsBuiltIn = true,
                Characters = new List<string>
                {
                    "两仪式", "黑桐干也", "苍崎橙子", "荒耶宗莲",
                    "藤乃", "鲜花", "玄雾皋月", "白纯里绪",
                }
            },
            new()
            {
                Name = "樱之诗 默认角色",
                Description = "樱之诗全角色集（约 30 个角色 + 9 个章节）",
                IsBuiltIn = true,
                Characters = new List<string>
                {
                    "直哉", "圭", "蓝", "禀", "雫", "真琴", "健一郎",
                    "义贞", "吹", "宁", "铃菜", "樱子", "香奈",
                    "琴子", "水菜", "片贝", "葛", "雾乃", "丽华",
                    "优美", "小牧", "伯奇", "宇纲", "坂本", "若田",
                    "教師", "校长", "校長", "护士", "店主", "广播", "神父",
                    "众人", "孩子A", "孩子B", "女子A", "女子B", "男子A", "男子B",
                },
                RouteNames = new List<string>
                {
                    "序章", "第Ⅰ章 Frühlingsbeginn", "第Ⅱ章 Abend", "第Ⅲ章",
                    "第Ⅳ章 What is mind?", "第Ⅴ章 The Happy Prince", "第Ⅵ章",
                    "Cordyceps Sinensis", "The Happy Princess",
                }
            },
        };
    }
}
