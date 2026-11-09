using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace TextTool.Services;

/// <summary>
/// 规则标定：同一套规则分别跑人类文本与模型文本，按规则汇总两侧的命中数与每千字密度。
///
/// 判定沿用 lint 的口径：每个文件都过一次 <see cref="LintRunner"/>，规则表、档位、
/// 密度门限、重叠去重全按同一套走。本类只做汇总，且在扫出的命中上走一遍，
/// 不回头重扫原文，四千七百万字要跑得完就靠这一条。
/// </summary>
public static class LintCalibration
{
    /// <summary>
    /// 「弱」的倍数：模型侧每千字命中达到人类侧三倍。
    /// 规则要在模型文本上密到能当签名，人类文本里偶尔踩到不算失守。
    /// </summary>
    public const double WeakRatio = 3.0;

    /// <summary>
    /// 跑两个目录并把结果汇总成一张表。
    /// </summary>
    /// <param name="humanDir">人类写的文本目录，递归收 .txt。</param>
    /// <param name="aiDir">模型写的文本目录，递归收 .txt。</param>
    /// <param name="genre">只跑该档规则；null 表示整张表都跑，与 lint 不给 --genre 时一致。</param>
    /// <param name="rules">规则表，默认从磁盘加载；测试注入探针规则用。</param>
    public static CalibrationResult RunDirectories(
        string humanDir, string aiDir, string? genre = null, IReadOnlyList<LintRule>? rules = null)
    {
        var humanFiles = TxtFiles(humanDir, "--human");
        var aiFiles = TxtFiles(aiDir, "--ai");

        var table = rules ?? LintRuleStore.Load();
        var active = LintRunner.ActiveRules(table, genre);

        // 先按生效规则建满表：两侧都零命中的规则也要占一行，那种「活着但量不到东西」的判据
        // 正是标定要暴露的对象，只列有命中的规则就把它们藏起来了。
        var tallies = new Dictionary<string, Tally>(StringComparer.OrdinalIgnoreCase);
        var titles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in active)
        {
            tallies[rule.Id] = new Tally();
            titles[rule.Id] = rule.Title;
        }

        var result = new CalibrationResult { Genre = genre };
        result.Human = Scan(humanFiles, genre, table, tallies, titles, aiSide: false);
        result.Ai = Scan(aiFiles, genre, table, tallies, titles, aiSide: true);

        result.Rules = tallies
            .Select(pair => new CalibrationRow
            {
                Id = pair.Key,
                Title = titles[pair.Key],
                HumanHits = pair.Value._human,
                AiHits = pair.Value._ai,
            })
            .ToList();
        foreach (var row in result.Rules)
        {
            row.HumanPerKChar = LintTextFormatter.Density(row.HumanHits, result.Human.Chars);
            row.AiPerKChar = LintTextFormatter.Density(row.AiHits, result.Ai.Chars);
            row.Verdict = Verdict(row.HumanHits, row.HumanPerKChar, row.AiHits, row.AiPerKChar);
        }

        // 排序用未取整的密度，显示才取两位小数：读到 3.00 就以为刚好三倍，会误判门限
        result.Rules = result.Rules
            .OrderByDescending(r => r.AiPerKChar)
            .ThenByDescending(r => r.AiHits)
            .ThenByDescending(r => r.HumanHits)
            .ThenBy(r => r.Id, StringComparer.Ordinal)
            .ToList();
        result.SeparableCount = result.Rules.Count(r => r.Verdict == CalibrationVerdict.Separable);
        return result;
    }

    /// <summary>
    /// 一个集合的扫描：逐文件读、逐文件扫、命中立刻入账再丢报告，
    /// 这样峰值内存是「一个文件」而不是「整个集合的报告」。
    /// </summary>
    private static CalibrationCollection Scan(
        IReadOnlyList<string> files, string? genre, IReadOnlyList<LintRule> table,
        Dictionary<string, Tally> tallies, Dictionary<string, string> titles, bool aiSide)
    {
        var stats = new CalibrationCollection { Files = files.Count };
        // 规则编译与档位筛选只做一次，逐份复用：文件再多也只有这一次开销。
        var session = LintSession.Create(genre, null, null, null, table);

        // 命中按文件顺序入账：计数是整数，先后不影响结果；读不了的那一份也在原位置抛出来。
        LintBatch.Run(files, session, ReadForCalibration, item =>
        {
            // 标定不把「读不了」当成结果：一个文件的编码认不出来，两侧的密度就不可比，
            // 必须整次停下。位置与原因原样抛给调用方。
            if (item.Error is not null) ExceptionDispatchInfo.Capture(item.Error).Throw();

            var report = item.Report!;
            stats.Chars += report.Chars;
            stats.Hits += report.Hits.Count;
            foreach (var hit in report.Hits)
            {
                if (!tallies.TryGetValue(hit.Id, out var tally))
                {
                    // 兜底：生效规则里没有这条 Id，标题就从命中里带出来（会跑的规则上面已按表预建）
                    tally = new Tally();
                    tallies[hit.Id] = tally;
                    titles[hit.Id] = hit.Title;
                }
                if (aiSide) tally._ai++;
                else tally._human++;
            }
        });

        stats.PerKChar = LintTextFormatter.Density(stats.Hits, stats.Chars);
        return stats;
    }

    /// <summary>读一份要标定的稿：严格模式认编码，与 lint 读同一份文件的读法一致。</summary>
    private static string ReadForCalibration(string file)
    {
        var encoding = EncodingDetector.DetectStrict(file);
        return File.ReadAllText(file, encoding.Encoding);
    }

    /// <summary>
    /// 递归收 .txt，按路径排序：同一批文件跑两次，表里的数字要完全一样。
    /// 空目录与不存在分别报错，两种沉默不一样：一个是路径写错，一个是目录选错。
    /// </summary>
    public static List<string> TxtFiles(string directory, string flag)
    {
        if (!Directory.Exists(directory))
            throw new ArgumentException($"{flag} 目录不存在：{directory}");
        var files = Directory
            .EnumerateFiles(directory, "*.txt", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
        if (files.Count == 0)
            throw new ArgumentException($"{flag} 目录下没有 .txt 文件：{directory}");
        return files;
    }

    private static string Verdict(int humanHits, double humanDensity, int aiHits, double aiDensity)
    {
        if (humanHits == 0 && aiHits > 0)
            return CalibrationVerdict.Separable;
        if (humanHits > 0 && aiHits > 0 && aiDensity >= humanDensity * WeakRatio)
            return CalibrationVerdict.Weak;
        return CalibrationVerdict.Indistinguishable;
    }

    private sealed class Tally
    {
        public int _human;
        public int _ai;
    }
}

/// <summary>
/// 规则在两个集合上的表现分档。判据只有这两条：人类侧有没有命中，模型侧密度够不够高。
/// </summary>
public static class CalibrationVerdict
{
    /// <summary>人类侧零命中、模型侧有命中：这条规则量到的写法人类文本里一次都没出现。</summary>
    public const string Separable = "separable";

    /// <summary>两侧都有命中，但模型侧每千字命中达到人类侧三倍：有噪声，量级仍在。</summary>
    public const string Weak = "weak";

    /// <summary>其余情况，含两侧都零与人类有、模型没有：这条规则分不开两类文本。</summary>
    public const string Indistinguishable = "indistinguishable";

    /// <summary>JSON 用稳定英文字段值，人读表用中文标签：脚本与眼睛各取所需。</summary>
    public static string Label(string verdict) => verdict switch
    {
        Separable => "可分",
        Weak => "弱",
        _ => "不可分",
    };
}

/// <summary>一侧文本的规模：文件数、总字数、总命中数。</summary>
public sealed class CalibrationCollection
{
    public int Files { get; set; }
    public int Chars { get; set; }
    public int Hits { get; set; }
    /// <summary>总命中数折成每千字，与规则密度同一口径（chars 为 0 时记 0）。</summary>
    public double PerKChar { get; set; }
}

/// <summary>一条规则在两侧的读数。</summary>
public sealed class CalibrationRow
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public int HumanHits { get; set; }
    public double HumanPerKChar { get; set; }
    public int AiHits { get; set; }
    public double AiPerKChar { get; set; }
    /// <summary>separable / weak / indistinguishable，见 <see cref="CalibrationVerdict"/>。</summary>
    public string Verdict { get; set; } = CalibrationVerdict.Indistinguishable;
}

/// <summary>
/// 标定结果。人读表与 JSON 都出自这一份对象：两种输出不会各算一遍而给出不同数字。
/// </summary>
public sealed class CalibrationResult
{
    /// <summary>契约版本号，当前为 1；字段结构见 <see cref="ToJson"/>。</summary>
    public int Version { get; set; } = 1;
    /// <summary>本次只跑了哪一档；null 表示整张规则表都跑了。</summary>
    public string? Genre { get; set; }
    public CalibrationCollection Human { get; set; } = new();
    public CalibrationCollection Ai { get; set; } = new();
    /// <summary>判定为「可分」的规则条数，与 <see cref="Rules"/> 里的 Verdict 同一份判定。</summary>
    public int SeparableCount { get; set; }
    /// <summary>按模型侧每千字密度从高到低排好的规则读数。</summary>
    public List<CalibrationRow> Rules { get; set; } = new();

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        // 中文照原样输出，与 lint --json 同一口径
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    /// <summary>行尾归一为 LF：输出常被重定向成文件，本工作区文件一律 LF。</summary>
    public string ToJson() => JsonSerializer.Serialize(this, Options).Replace("\r\n", "\n");

    /// <summary>
    /// 人读表格：列按「模型侧密度高的先看」排，末行是两侧规模与可分条数。
    /// 数字列右对齐、末列不补空格：这张表经常被原样贴进文档，行尾不留空白。
    /// </summary>
    public string Render()
    {
        string[] header = { "规则", "标题", "人类命中", "人类/千字", "模型命中", "模型/千字", "判定" };
        var rows = new List<string[]> { header };
        foreach (var rule in Rules)
        {
            rows.Add(new[]
            {
                rule.Id,
                rule.Title,
                rule.HumanHits.ToString(CultureInfo.InvariantCulture),
                rule.HumanPerKChar.ToString("0.00", CultureInfo.InvariantCulture),
                rule.AiHits.ToString(CultureInfo.InvariantCulture),
                rule.AiPerKChar.ToString("0.00", CultureInfo.InvariantCulture),
                CalibrationVerdict.Label(rule.Verdict),
            });
        }

        var widths = new int[header.Length];
        foreach (var row in rows)
            for (int i = 0; i < row.Length; i++)
                widths[i] = Math.Max(widths[i], DisplayWidth(row[i]));

        var sb = new StringBuilder();
        foreach (var row in rows)
        {
            for (int i = 0; i < row.Length; i++)
            {
                if (i > 0) sb.Append("  ");
                // 判定列是最后一列，不补齐，行尾因此没有空白
                if (i == row.Length - 1)
                {
                    sb.Append(row[i]);
                    break;
                }
                int pad = widths[i] - DisplayWidth(row[i]);
                bool rightAlign = i is >= 2 and <= 5;
                if (rightAlign) sb.Append(' ', pad).Append(row[i]);
                else sb.Append(row[i]).Append(' ', pad);
            }
            sb.Append('\n');
        }

        sb.Append("人类 ").Append(Human.Files.ToString(CultureInfo.InvariantCulture)).Append(" 个文件 ")
            .Append(Human.Chars.ToString(CultureInfo.InvariantCulture)).Append(" 字，命中 ")
            .Append(Human.Hits.ToString(CultureInfo.InvariantCulture)).Append(" 处（每千字 ")
            .Append(Human.PerKChar.ToString("0.00", CultureInfo.InvariantCulture)).Append("）；模型 ")
            .Append(Ai.Files.ToString(CultureInfo.InvariantCulture)).Append(" 个文件 ")
            .Append(Ai.Chars.ToString(CultureInfo.InvariantCulture)).Append(" 字，命中 ")
            .Append(Ai.Hits.ToString(CultureInfo.InvariantCulture)).Append(" 处（每千字 ")
            .Append(Ai.PerKChar.ToString("0.00", CultureInfo.InvariantCulture)).Append("）；可分规则 ")
            .Append(SeparableCount.ToString(CultureInfo.InvariantCulture)).Append(" 条。\n");
        return sb.ToString();
    }

    /// <summary>
    /// 终端显示宽度：汉字、假名、全角标点占两格，其余占一格。
    /// 表格按这个宽度补齐，中文标题才不会把后面的数字挤歪。
    /// </summary>
    internal static int DisplayWidth(string text)
    {
        int width = 0;
        foreach (char c in text)
            width += IsWide(c) ? 2 : 1;
        return width;
    }

    private static bool IsWide(char c) =>
        c is (>= '\u1100' and <= '\u115F')   // 谚文字母
            or (>= '\u2E80' and <= '\uA4CF') // CJK 部首、假名、注音、汉字
            or (>= '\uAC00' and <= '\uD7A3') // 谚文音节
            or (>= '\uF900' and <= '\uFAFF') // CJK 兼容汉字
            or (>= '\uFE30' and <= '\uFE6F') // CJK 兼容标点
            or (>= '\uFF00' and <= '\uFF60') // 全角 ASCII 与半角 CJK 标点
            or (>= '\uFFE0' and <= '\uFFE6'); // 全角货币符号
}
