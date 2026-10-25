using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit.Abstractions;

namespace TextTool.Tests.Services;

/// <summary>
/// 本地化「调用点」侧的静态检查：Loc.T("键名", 实参...) 的实参个数必须等于该键在
/// json 里的占位符个数。
///
/// 为什么需要这条：Loc.T(key, args) 直接走 string.Format，而 string.Format 对
/// 「实参多于占位符」是不报错的 —— 多出来的实参被静默丢掉。项目里真出过：
/// Loc.T("MsgProcessFailed", 文件名, 错误信息) 而该键只有 {0}，界面上错误原因直接消失。
/// 三份 json 之间的占位符一致性测试（LocalizationDataTests）也拦不住这种错，
/// 因为问题在代码这一侧，所以这里扫源码。
///
/// 只比 zh_CN 一份：三份 json 的占位符集合已有 LocalizationDataTests 保证一致。
/// </summary>
public class LocalizationCallSiteTests
{
    private const string Lang = "zh_CN";

    private readonly ITestOutputHelper _output;

    public LocalizationCallSiteTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// 占位符：{0}、{1}、{0:N0} 之类。
    /// (?&lt;!\{) 是为了不把转义的字面大括号 {{0}} 当成占位符。
    /// 与 LocalizationDataTests 里的正则保持一致。
    /// </summary>
    private static readonly Regex PlaceholderRegex = new(@"(?<!\{)\{(\d+)[^{}]*\}", RegexOptions.Compiled);

    /// <summary>
    /// 测试输出目录是 TextTool.Tests/bin/&lt;Configuration&gt;/net8.0-windows，
    /// 往上四级刚好是仓库根目录（与 LocalizationDataTests 定位 Localization/ 的写法一致）。
    /// </summary>
    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    private static string JsonPath(string lang) => Path.Combine(RepoRoot, "Localization", lang + ".json");

    private static Dictionary<string, string?> Load(string lang)
    {
        string path = JsonPath(lang);
        Assert.True(File.Exists(path), $"本地化文件不存在：{path}");

        var data = JsonSerializer.Deserialize<Dictionary<string, string?>>(
            File.ReadAllText(path), new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip });

        Assert.NotNull(data);
        Assert.True(data!.Count > 0, $"{path} 解析出 0 个键，文件内容像是空的");
        return data;
    }

    /// <summary>取出某个串里的占位符编号（去重、升序）。</summary>
    private static List<int> Placeholders(string? value) =>
        value is null
            ? new List<int>()
            : PlaceholderRegex.Matches(value)
                .Select(m => int.Parse(m.Groups[1].Value))
                .Distinct()
                .OrderBy(n => n)
                .ToList();

    private static string ShowSet(IEnumerable<int> numbers)
    {
        var list = numbers.ToList();
        return list.Count == 0 ? "(无占位符)" : string.Join(" ", list.Select(n => "{" + n + "}"));
    }

    /// <summary>
    /// 拦：调用点传的实参个数跟键的占位符个数对不上。
    /// 实参多了 —— string.Format 静默丢弃多的，界面上的信息莫名消失（本类的起因）；
    /// 实参少了 —— string.Format 抛 FormatException，Loc.T 把它吞成未格式化的原文，
    /// 界面上就是一大段带 {0} {1} 的模板。
    /// 失败信息给：文件、行号、键名、占位符几个、实参几个、原文那一行。
    /// </summary>
    [Fact]
    public void 调用点的实参个数与占位符个数一致()
    {
        var strings = Load(Lang);
        var scan = LocCallScanner.ScanRepository(RepoRoot);

        _output.WriteLine($"扫描 {scan.Files.Count} 个文件，字面量键名的调用 {scan.Calls.Count} 处，"
            + $"非字面量键名跳过 {scan.Skipped.Count} 处，注释里出现 Loc.T 调用 {scan.CommentMentions} 处。");

        // 扫描器一旦失灵（路径变了、正则式样退化），下面的逐条比较会变成「空集合里挑不出错」，
        // 恒绿。留个下限兜住它。
        Assert.True(scan.Files.Count >= 10,
            $"只扫到 {scan.Files.Count} 个源文件，扫描范围可能已经不对了（Controls/*.cs、MainForm.cs、TextTool.Gui/**/*.cs）");
        Assert.True(scan.Calls.Count >= 150,
            $"只认出 {scan.Calls.Count} 处字面量键名的 Loc.T 调用，扫描器可能已失效，本测试的断言会失去意义");
        Assert.True(strings.Count > 0, $"{Lang}.json 是空表");

        // 跳过的调用不静默：列出来，方便人肉确认这些位置确实没法静态查。
        if (scan.Skipped.Count > 0)
        {
            _output.WriteLine($"以下调用首参不是字面量键名，静态查不出占位符个数，已跳过（共 {scan.Skipped.Count} 处）：");
            foreach (var skipped in scan.Skipped.OrderBy(s => s.File, StringComparer.Ordinal).ThenBy(s => s.Line))
                _output.WriteLine($"  {skipped.Where}  {skipped.Reason}  {skipped.Snippet}");
        }
        else
        {
            _output.WriteLine("没有需要跳过的调用：所有 Loc.T 的首参都是字面量键名。");
        }

        var problems = new List<string>();

        foreach (var call in scan.Calls)
        {
            if (!strings.TryGetValue(call.Key, out string? value))
            {
                problems.Add($"  {call.Where}  键 {call.Key} 在 {Lang}.json 里不存在"
                    + $"（Loc.T 会原样返回 [{call.Key}]）；{call.Snippet}");
                continue;
            }

            var numbers = Placeholders(value);
            // json 侧要求的最少实参个数 = 最大占位符编号 + 1（编号从 0 连续由另一条测试保证）。
            int required = numbers.Count == 0 ? 0 : numbers.Max() + 1;

            if (call.ArgCount != required)
            {
                string effect = call.ArgCount > required
                    ? "实参多出来的会被 string.Format 静默丢弃"
                    : "实参不够 string.Format 会抛 FormatException，被 Loc.T 吞成未格式化原文";

                problems.Add($"  {call.Where}  键 {call.Key}：json 里占位符 {required} 个 {ShowSet(numbers)}，"
                    + $"调用点传了 {call.ArgCount} 个（{effect}）；{call.Snippet}");
            }
        }

        Assert.True(problems.Count == 0,
            $"以下调用点的实参个数与占位符个数不一致（共 {problems.Count} 处）："
            + Environment.NewLine + string.Join(Environment.NewLine, problems)
            + Environment.NewLine + $"（键值取自 Localization/{Lang}.json）");
    }

    /// <summary>
    /// 自检：实参个数必须能跨过嵌套括号、字符串里的逗号、注释、跨行调用数对。
    /// 这条测试防的是「扫描器数错了，于是主测试恒绿」。
    /// </summary>
    [Fact]
    public void 扫描器自检_复杂实参也要数对()
    {
        const string code = """
            var a = Loc.T("K1", $"{Path.GetFileName(files[i - 1])}：{err}");
            var b = Loc.T("K2", files.Count, list.Sum(x => x.N), "a,b");
            var c = Loc.T("K3");
            var d = Loc.T("K4", /* 注释,里面有逗号 */ value);
            var e = Loc.T(
                "K5",
                a,
                b);
            var f = Loc.T("K6", $"{Foo("x,y")}");
            var g = Loc.T("K7", new[] { 1, 2 });
            var h = Loc.T("K8", Loc.T("K9", one, two));
            """;

        var scan = LocCallScanner.ScanText(code, "self-check.cs");

        Assert.Equal(
            "K1=1; K2=3; K3=0; K4=1; K5=2; K6=1; K7=1; K8=1; K9=2",
            string.Join("; ", scan.Calls.Select(c => $"{c.Key}={c.ArgCount}")));

        Assert.Empty(scan.Skipped);

        // 行号要跟着全文件算，不是按片段算；跨行调用的行号取调用起始行。
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 9, 10, 11, 11 }, scan.Calls.Select(c => c.Line).ToArray());
    }

    /// <summary>
    /// 自检：行注释、块注释、字符串里的 Loc.T 不算调用；插值孔里的是真代码，必须算。
    /// </summary>
    [Fact]
    public void 扫描器自检_注释不算_插值孔要算()
    {
        const string code = """
            // Loc.T("InLineComment", a, b)
            /* Loc.T("InBlockComment", a) */
            var s = $"{Loc.T("InHole")}";
            var t = @"Loc.T(""InVerbatim"")";
            MyLoc.T("NotOurs");
            var u = Loc.T("Normal", x);
            """;

        var scan = LocCallScanner.ScanText(code, "self-check.cs");

        Assert.Equal("InHole=0; Normal=1", string.Join("; ", scan.Calls.Select(c => $"{c.Key}={c.ArgCount}")));
        Assert.Equal(2, scan.CommentMentions);
    }

    /// <summary>
    /// 自检：读不出字面量键名的调用必须落进 Skipped 且带得上原因，不许静默消失。
    /// </summary>
    [Fact]
    public void 扫描器自检_非字面量键名记为跳过()
    {
        const string code = """
            var a = Loc.T(_lastDetection.LocKey);
            var b = Loc.T(KeyPrefix + "ExportDone", selected.Count, dlg.FileName);
            var c = Loc.T($"Key{index}");
            var d = Loc.T(statusKey, ruleCount);
            var e = Loc.T("Export" + suffix, selected.Count);
            """;

        var scan = LocCallScanner.ScanText(code, "self-check.cs");

        Assert.Empty(scan.Calls);
        Assert.Equal(5, scan.Skipped.Count);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, scan.Skipped.Select(s => s.Line).ToArray());
        Assert.All(scan.Skipped, s => Assert.False(string.IsNullOrWhiteSpace(s.Reason)));
        Assert.Contains("变量或表达式", scan.Skipped[0].Reason);
        Assert.Contains("变量或表达式", scan.Skipped[1].Reason);
        Assert.Contains("插值", scan.Skipped[2].Reason);
        Assert.Contains("变量或表达式", scan.Skipped[3].Reason);
        Assert.Contains("拼接", scan.Skipped[4].Reason);
    }
}
