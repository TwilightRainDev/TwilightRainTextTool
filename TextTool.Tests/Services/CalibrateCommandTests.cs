using System.Diagnostics;
using System.Text.Json;

namespace TextTool.Tests.Services;

/// <summary>
/// calibrate 的聚合、分档与输出契约。
///
/// 口径：
/// - 每个文件都过 LintRunner，命中与 lint 命令行完全一致；标定只负责按规则结算；
/// - 分档界线只有两处：人类侧零命中、模型侧每千字达到人类侧三倍。判定用未取整的密度，
///   表里取两位小数只是显示；
/// - 两侧都零命中的规则照样占一行，那种「活着但量不到东西」的判据正是标定要暴露的对象；
/// - 算法规则（引号混用之类）不在规则表里，命中之后也要进表，标题从命中带出来。
/// </summary>
public class CalibrateCommandTests
{
    /// <summary>
    /// 无标点、无换行的垫文，凑够指定字数。标点与换行会带进别的规则，
    /// 密度算不准；这段重复串也凑不出复读串（周期单元同字不算）。
    /// </summary>
    private static string 垫文(int chars) =>
        string.Concat(Enumerable.Repeat("天地玄黄宇宙洪荒", chars / 8 + 1))[..chars];

    /// <summary>占位规则：literal 单模式，Genres 留空即各档通用。</summary>
    private static LintRule 规则(string id, string pattern, params string[] genres) => new()
    {
        Id = id,
        Group = "test",
        Title = id,
        Kind = "literal",
        Scope = "document",
        Patterns = new List<string> { pattern },
        Genres = genres.ToList(),
        Severity = "info",
    };

    private static void 写入(string dir, string name, string text) =>
        File.WriteAllText(Path.Combine(dir, name), text, new UTF8Encoding(false));

    [Fact]
    public void 两侧命中与每千字密度按规则汇总()
    {
        using var human = new TempDir();
        using var ai = new TempDir();
        写入(human.Path, "h.txt", "探" + 垫文(2999));   // 3000 字，1 处
        写入(ai.Path, "a.txt", "探探探" + 垫文(997));    // 1000 字，3 处

        var result = LintCalibration.RunDirectories(
            human.Path, ai.Path, null, new[] { 规则("probe", "探") });

        var row = Assert.Single(result.Rules);
        Assert.Equal("probe", row.Id);
        Assert.Equal(1, row.HumanHits);
        Assert.Equal(3, row.AiHits);
        Assert.Equal(3000, result.Human.Chars);
        Assert.Equal(1000, result.Ai.Chars);
        Assert.Equal(1, result.Human.Files);
        Assert.Equal(1, result.Human.Hits);
        Assert.Equal(3, result.Ai.Hits);
        // 1 处 / 3000 字 = 0.333/千字，3 处 / 1000 字 = 3/千字，模型侧正好是人类侧九倍
        Assert.Equal(1.0 / 3, row.HumanPerKChar, 6);
        Assert.Equal(3.0, row.AiPerKChar, 6);
        Assert.Equal(CalibrationVerdict.Weak, row.Verdict);
        Assert.Equal(3.0, result.Ai.PerKChar, 6);
        Assert.Equal(0, result.SeparableCount);
    }

    [Theory]
    [InlineData(2, CalibrationVerdict.Indistinguishable)]   // 2/千字 对 1/千字，差两倍
    [InlineData(3, CalibrationVerdict.Weak)]                // 3/千字 对 1/千字，正好三倍
    public void 弱档界线在人类侧三倍上(int aiHits, string expected)
    {
        using var human = new TempDir();
        using var ai = new TempDir();
        写入(human.Path, "h.txt", "探探探" + 垫文(2997));            // 3000 字，3 处，1/千字
        写入(ai.Path, "a.txt", new string('探', aiHits) + 垫文(1000 - aiHits)); // 1000 字

        var row = Assert.Single(LintCalibration.RunDirectories(
            human.Path, ai.Path, null, new[] { 规则("probe", "探") }).Rules);

        Assert.Equal(1.0, row.HumanPerKChar, 6);
        Assert.Equal(aiHits, row.AiPerKChar, 6);
        Assert.Equal(expected, row.Verdict);
    }

    [Fact]
    public void 人类侧零命中模型侧有命中记可分()
    {
        using var human = new TempDir();
        using var ai = new TempDir();
        写入(human.Path, "h.txt", 垫文(100));
        写入(ai.Path, "a.txt", "探" + 垫文(99));

        var result = LintCalibration.RunDirectories(
            human.Path, ai.Path, null, new[] { 规则("probe", "探") });

        var row = Assert.Single(result.Rules);
        Assert.Equal(0, row.HumanHits);
        Assert.Equal(1, row.AiHits);
        Assert.Equal(CalibrationVerdict.Separable, row.Verdict);
        Assert.Equal(1, result.SeparableCount);
    }

    [Fact]
    public void 两侧都零命中也占一行记不可分()
    {
        using var human = new TempDir();
        using var ai = new TempDir();
        写入(human.Path, "h.txt", 垫文(100));
        写入(ai.Path, "a.txt", 垫文(100));

        var result = LintCalibration.RunDirectories(
            human.Path, ai.Path, null, new[] { 规则("probe", "探") });

        var row = Assert.Single(result.Rules);
        Assert.Equal(0, row.HumanHits);
        Assert.Equal(0, row.AiHits);
        Assert.Equal(0.0, row.AiPerKChar);
        Assert.Equal(CalibrationVerdict.Indistinguishable, row.Verdict);
        Assert.Equal(0, result.SeparableCount);
    }

    [Fact]
    public void 人类有命中模型没有记不可分()
    {
        using var human = new TempDir();
        using var ai = new TempDir();
        写入(human.Path, "h.txt", "探" + 垫文(99));
        写入(ai.Path, "a.txt", 垫文(100));

        var row = Assert.Single(LintCalibration.RunDirectories(
            human.Path, ai.Path, null, new[] { 规则("probe", "探") }).Rules);

        Assert.Equal(1, row.HumanHits);
        Assert.Equal(0, row.AiHits);
        Assert.Equal(CalibrationVerdict.Indistinguishable, row.Verdict);
    }

    [Fact]
    public void 递归收子目录的txt且不收其它后缀()
    {
        using var human = new TempDir();
        using var ai = new TempDir();
        写入(human.Path, "h.txt", 垫文(50));
        Directory.CreateDirectory(Path.Combine(ai.Path, "book", "part"));
        写入(Path.Combine(ai.Path, "book", "part"), "a.txt", 垫文(50));
        写入(ai.Path, "b.txt", 垫文(50));
        写入(ai.Path, "notes.md", 垫文(50));

        var result = LintCalibration.RunDirectories(
            human.Path, ai.Path, null, Array.Empty<LintRule>());

        Assert.Equal(1, result.Human.Files);
        Assert.Equal(2, result.Ai.Files);
        Assert.Equal(100, result.Ai.Chars);
    }

    [Fact]
    public void 目录不存在时报出是哪个参数()
    {
        using var human = new TempDir();
        using var ai = new TempDir();
        写入(human.Path, "h.txt", 垫文(20));
        string missing = Path.Combine(ai.Path, "没有这个目录");

        var ex = Assert.Throws<ArgumentException>(() =>
            LintCalibration.RunDirectories(human.Path, missing, null, Array.Empty<LintRule>()));

        Assert.Contains("--ai", ex.Message);
        Assert.Contains(missing, ex.Message);
    }

    [Fact]
    public void 目录里没有txt即抛()
    {
        using var human = new TempDir();
        using var ai = new TempDir();

        var ex = Assert.Throws<ArgumentException>(() =>
            LintCalibration.RunDirectories(human.Path, ai.Path, null, Array.Empty<LintRule>()));

        Assert.Contains("--human", ex.Message);
        Assert.Contains("没有 .txt", ex.Message);
    }

    [Fact]
    public void 档位只把该档的规则建进表()
    {
        using var human = new TempDir();
        using var ai = new TempDir();
        写入(human.Path, "h.txt", 垫文(50));
        写入(ai.Path, "a.txt", 垫文(50));

        var rules = new[] { 规则("doc-only", "探", "doc"), 规则("common", "探") };
        var row = Assert.Single(
            LintCalibration.RunDirectories(human.Path, ai.Path, "fiction", rules).Rules);
        Assert.Equal("common", row.Id);

        var ex = Assert.Throws<ArgumentException>(() =>
            LintCalibration.RunDirectories(human.Path, ai.Path, "novel", rules));
        Assert.Contains("--genre", ex.Message);
    }

    [Fact]
    public void 算法规则命中后也进表且标题来自命中()
    {
        using var human = new TempDir();
        using var ai = new TempDir();
        写入(human.Path, "h.txt", 垫文(50));
        写入(ai.Path, "a.txt", "「他说」和\"直引号\"");

        var row = Assert.Single(LintCalibration.RunDirectories(
            human.Path, ai.Path, null, Array.Empty<LintRule>()).Rules);

        Assert.Equal("mixed-quote-style", row.Id);
        Assert.Equal("引号风格混用", row.Title);
        Assert.Equal(0, row.HumanHits);
        Assert.Equal(1, row.AiHits);
        Assert.Equal(CalibrationVerdict.Separable, row.Verdict);
    }

    [Fact]
    public void 人读表按模型侧密度降序且末行汇总()
    {
        using var human = new TempDir();
        using var ai = new TempDir();
        写入(human.Path, "h.txt", 垫文(100));
        写入(ai.Path, "a.txt", "丙丙丙丙丙乙" + 垫文(94));   // 100 字：丙 5 处、乙 1 处

        var rules = new[] { 规则("low", "乙"), 规则("high", "丙") };
        var rendered = LintCalibration.RunDirectories(human.Path, ai.Path, null, rules).Render();
        var lines = rendered.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.StartsWith("规则", lines[0]);
        Assert.Contains("high", lines[1]);
        Assert.Contains("low", lines[2]);
        Assert.Contains("人类 1 个文件 100 字", lines[3]);
        Assert.Contains("模型 1 个文件 100 字", lines[3]);
        Assert.Contains("可分规则 2 条", lines[3]);
        // 输出常被原样贴进文档，行尾不留空白
        Assert.All(lines, line => Assert.Equal(line.TrimEnd(), line));
    }

    [Fact]
    public void Json契约版本与字段()
    {
        using var human = new TempDir();
        using var ai = new TempDir();
        写入(human.Path, "h.txt", 垫文(100));
        写入(ai.Path, "a.txt", "探" + 垫文(99));

        var json = LintCalibration.RunDirectories(
            human.Path, ai.Path, "fiction", new[] { 规则("probe", "探") }).ToJson();

        Assert.DoesNotContain("\r", json, StringComparison.Ordinal);
        // 中文照原样，不转 \uXXXX
        Assert.Contains("probe", json, StringComparison.Ordinal);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal(1, root.GetProperty("Version").GetInt32());
        Assert.Equal("fiction", root.GetProperty("Genre").GetString());
        Assert.Equal(1, root.GetProperty("Human").GetProperty("Files").GetInt32());
        Assert.Equal(100, root.GetProperty("Ai").GetProperty("Chars").GetInt32());
        Assert.Equal(1, root.GetProperty("SeparableCount").GetInt32());
        var rule = Assert.Single(root.GetProperty("Rules").EnumerateArray());
        Assert.Equal("probe", rule.GetProperty("Id").GetString());
        Assert.Equal(0, rule.GetProperty("HumanHits").GetInt32());
        Assert.Equal(1, rule.GetProperty("AiHits").GetInt32());
        Assert.Equal(CalibrationVerdict.Separable, rule.GetProperty("Verdict").GetString());
    }

    // ================================================================
    //  命令行：走真实进程，钉住退出码与 stderr 那一行
    // ================================================================

    [Fact]
    public void 缺参数退出2并指名缺哪个()
    {
        var (code, _, stderr) = RunCli("calibrate");
        Assert.Equal(2, code);
        Assert.Contains("--human", stderr, StringComparison.Ordinal);
        Assert.Contains("--ai", stderr, StringComparison.Ordinal);

        using var human = new TempDir();
        var (onlyHuman, _, onlyHumanErr) = RunCli("calibrate", "--human", human.Path);
        Assert.Equal(2, onlyHuman);
        Assert.Contains("缺少 --ai", onlyHumanErr, StringComparison.Ordinal);
        Assert.DoesNotContain("--human <目录>", onlyHumanErr, StringComparison.Ordinal);
    }

    [Fact]
    public void 跑完退出0并输出表或JSON()
    {
        using var human = new TempDir();
        using var ai = new TempDir();
        写入(human.Path, "h.txt", 垫文(20));
        写入(ai.Path, "a.txt", "此外，这不是数据不够，而是口径不同。");

        var (code, stdout, stderr) = RunCli(
            "calibrate", "--human", human.Path, "--ai", ai.Path, "--genre", "fiction");
        Assert.Equal(0, code);
        Assert.Equal("", stderr);
        Assert.Contains("可分规则", stdout, StringComparison.Ordinal);

        var (jsonCode, jsonOut, _) = RunCli(
            "calibrate", "--human", human.Path, "--ai", ai.Path, "--json");
        Assert.Equal(0, jsonCode);
        using var doc = JsonDocument.Parse(jsonOut);
        Assert.Equal(1, doc.RootElement.GetProperty("Version").GetInt32());
    }

    /// <summary>
    /// 命令行的 dll，与测试同一配置、同一框架。
    /// 退出码与 stderr 只能在真实进程里看：Console.Error 由 Main 接管到进程标准错误上，
    /// 进程内重定向因此取不到错误文本。
    /// </summary>
    private static string CliDll
    {
        get
        {
            // .../TextTool.Tests/bin/<配置>/net8.0/，上四级是仓库根，配置名取自 bin 的下一层
            var here = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
            string config = here.Parent!.Name;
            string root = here.Parent!.Parent!.Parent!.Parent!.FullName;
            return Path.Combine(root, "TextTool.Cli", "bin", config, "net8.0", "texttool.dll");
        }
    }

    private static (int Code, string Stdout, string Stderr) RunCli(params string[] args)
    {
        string dll = CliDll;
        Assert.True(File.Exists(dll), $"先 build 命令行项目：{dll}");

        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
            UseShellExecute = false,
        };
        start.ArgumentList.Add(dll);
        foreach (string arg in args)
            start.ArgumentList.Add(arg);

        using var process = Process.Start(start)!;
        // 两条流都异步读：只同步读一条，另一条写满管道缓冲就会把子进程卡住
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return (process.ExitCode, stdout.Result, stderr.Result);
    }
}
