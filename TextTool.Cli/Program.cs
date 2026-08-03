using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using TextTool.Services;

namespace TextTool.Cli;

/// <summary>
/// TextTool 命令行入口 — 供脚本化批处理/CI 集成使用。
///
/// 用法：
///   texttool merge &lt;file...&gt; [--threshold N] [--chars|--bytes] [--overwrite] [--no-cjk] [--no-punct] [--punct-chars &quot;，&quot;]
///   texttool replace &lt;file...&gt; [--scheme &lt;name&gt;]
///   texttool vn &lt;file...&gt; [--scheme &lt;name...&gt;] [--max-para N] [--reformat-only|--punct-only]
///   texttool join &lt;directory&gt; [--pattern &quot;*.txt&quot;] [--output &lt;name&gt;]
///   texttool update [--check]
/// </summary>
public static class Program
{
    private static readonly string Version =
        typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public static int Main(string[] args)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "merge" => RunMerge(args[1..]),
                "replace" => RunReplace(args[1..]),
                "vn" => RunVn(args[1..]),
                "join" => RunJoin(args[1..]),
                "update" => RunUpdate(args[1..]),
                "help" or "--help" or "-h" => PrintUsage(),
                _ => UnknownCommand(args[0]),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"错误：{ex.Message}");
            return 1;
        }
    }

    private static int RunMerge(string[] args)
    {
        var options = new MergeOptions { Mode = MergeMode.CharCount };
        bool fixCjk = true, fixPunct = false, trimComma = true, overwrite = false;
        string punctChars = "，";
        var files = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--threshold": options.Threshold = ParseInt(args, ref i, "--threshold"); break;
                case "--chars": options.Mode = MergeMode.CharCount; break;
                case "--bytes": options.Mode = MergeMode.ByteCount; break;
                case "--overwrite": overwrite = true; break;
                case "--no-cjk": fixCjk = false; break;
                case "--fix-punct": fixPunct = true; break;
                case "--punct-chars": punctChars = ParseValue(args, ref i, "--punct-chars"); break;
                case "--no-trim-comma": trimComma = false; break;
                case "--help" or "-h": PrintMergeHelp(); return 0;
                default: files.Add(args[i]); break;
            }
        }

        RequireFiles(files, "merge");
        var rules = ReplaceRuleStore.Load();
        var post = new PostProcessOptions(
            FixCjk: fixCjk, FixPunct: fixPunct, PunctChars: punctChars,
            NoMerge: false, NoMergeChars: "",
            ApplyReplace: false, TrimLeadingComma: trimComma,
            Rules: rules);

        int success = 0;
        foreach (var file in files)
        {
            var encoding = EncodingDetector.DetectStrict(file);
            var result = ProcessingPipeline.Run(file, encoding.Encoding, options, post, overwrite);
            Console.WriteLine($"[成功] {Path.GetFileName(file)} → {result.OutputPath}");
            success++;
        }
        return success == files.Count ? 0 : 1;
    }

    private static int RunReplace(string[] args)
    {
        var files = new List<string>();
        string? schemeName = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--scheme": schemeName = ParseValue(args, ref i, "--scheme"); break;
                case "--help" or "-h": PrintReplaceHelp(); return 0;
                default: files.Add(args[i]); break;
            }
        }

        RequireFiles(files, "replace");

        List<ReplaceRule> rules = ReplaceRuleStore.Load();
        if (schemeName != null)
        {
            var scheme = ReplaceSchemeStore.Load().FirstOrDefault(s => s.Name == schemeName)
                ?? throw new ArgumentException($"未找到方案：{schemeName}");
            rules = scheme.Rules;
        }
        if (rules.Count == 0)
            throw new ArgumentException("没有可用的替换规则（请先在 GUI 配置规则，或指定 --scheme）");

        int success = 0;
        foreach (var file in files)
        {
            var encoding = EncodingDetector.DetectStrict(file);
            string content = File.ReadAllText(file, encoding.Encoding);
            string replaced = PunctuationReplacer.Apply(content, rules);
            string outputPath = PathHelper.GetProcessedPath(file);
            File.WriteAllText(outputPath, replaced, new UTF8Encoding(true));
            Console.WriteLine($"[成功] {Path.GetFileName(file)} → {Path.GetFileName(outputPath)}");
            success++;
        }
        return success == files.Count ? 0 : 1;
    }

    private static int RunVn(string[] args)
    {
        var files = new List<string>();
        var schemeNames = new List<string>();
        int maxPara = 450;
        bool doReformat = true, doFixPunct = true;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--scheme": schemeNames.Add(ParseValue(args, ref i, "--scheme")); break;
                case "--max-para": maxPara = ParseInt(args, ref i, "--max-para"); break;
                case "--reformat-only": doFixPunct = false; break;
                case "--punct-only": doReformat = false; break;
                case "--help" or "-h": PrintVnHelp(); return 0;
                default: files.Add(args[i]); break;
            }
        }

        RequireFiles(files, "vn");

        HashSet<string> characters = new();
        List<string> routeNames = new();
        List<string> scenePatterns = new();
        if (schemeNames.Count > 0)
        {
            var store = VNCharacterSchemeStore.Load();
            foreach (var name in schemeNames)
            {
                var scheme = store.FirstOrDefault(s => s.Name == name)
                    ?? throw new ArgumentException($"未找到方案：{name}");
                characters.UnionWith(scheme.Characters);
                routeNames.AddRange(scheme.RouteNames);
                if (!string.IsNullOrWhiteSpace(scheme.ScenePattern))
                    scenePatterns.Add(scheme.ScenePattern!);
            }
        }

        int success = 0;
        foreach (var file in files)
        {
            var encoding = EncodingDetector.DetectStrict(file);
            string content = File.ReadAllText(file, encoding.Encoding);

            if (doReformat)
            {
                Regex? scenePattern = scenePatterns.Count > 0
                    ? new Regex(string.Join("|", scenePatterns))
                    : null;
                var reformatter = new VNReformatterService(
                    characters: characters, routeNames: routeNames,
                    maxParaLength: maxPara, scenePattern: scenePattern);
                content = reformatter.Reformat(content);
            }
            if (doFixPunct)
                content = new PunctFixerService().Fix(content);

            string outputPath = PathHelper.GetProcessedPath(file);
            File.WriteAllText(outputPath, content, new UTF8Encoding(true));
            Console.WriteLine($"[成功] {Path.GetFileName(file)} → {Path.GetFileName(outputPath)}");
            success++;
        }
        return success == files.Count ? 0 : 1;
    }

    private static int RunUpdate(string[] args)
    {
        bool checkOnly = false;
        foreach (string arg in args)
        {
            switch (arg)
            {
                case "--check":
                    checkOnly = true;
                    break;
                case "--help" or "-h":
                    PrintUpdateHelp();
                    return 0;
                default:
                    throw new ArgumentException($"未知参数：{arg}");
            }
        }

        using var client = UpdateClient.Create(TimeSpan.FromSeconds(30));
        UpdateClient.ResetMitmDetection();
        var result = checkOnly
            ? SelfUpdater.CheckAsync(client, Version).GetAwaiter().GetResult()
            : SelfUpdater.UpdateAsync(client, Version).GetAwaiter().GetResult();

        if (UpdateClient.MitmDetected)
            Console.WriteLine("警告：检测到本机 GitHub 流量经中间代理（TLS 根证书非公共 CA，如 S302 类加速器/企业代理）。" +
                "安装包签名校验仍保证安全；如不信任该代理，可设置环境变量 TEXTTOOL_UPDATE_STRICT_TLS=1 强制拒绝后更新。");

        if (result.Error is not null)
        {
            Console.Error.WriteLine($"错误：{result.Error}");
            return 1;
        }

        if (result.HasUpdate)
        {
            Console.WriteLine($"发现新版本：{result.LatestVersion}（当前 {Version}）");
            Console.WriteLine("NEW_VERSION_AVAILABLE=true");
            Console.WriteLine($"NEW_VERSION={result.LatestVersion}");
        }
        else
        {
            Console.WriteLine($"已是最新版本（{result.LatestVersion ?? Version}）。");
            Console.WriteLine("NEW_VERSION_AVAILABLE=false");
        }
        return 0;
    }

    private static int RunJoin(string[] args)
    {
        string? directory = null;
        string pattern = "*.txt";
        string output = "joined.txt";

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--pattern": pattern = ParseValue(args, ref i, "--pattern"); break;
                case "--output": output = ParseValue(args, ref i, "--output"); break;
                case "--help" or "-h": PrintJoinHelp(); return 0;
                default: directory = args[i]; break;
            }
        }

        if (string.IsNullOrEmpty(directory))
            throw new ArgumentException("join 需要指定目录");
        if (!Directory.Exists(directory))
            throw new ArgumentException($"目录不存在：{directory}");

        var (outputPath, count) = FileJoiner.Join(directory, pattern, output);
        Console.WriteLine($"[成功] 合并 {count} 个文件 → {outputPath}");
        return 0;
    }

    // ================================================================
    //  Helpers
    // ================================================================

    private static void RequireFiles(List<string> files, string cmd)
    {
        if (files.Count == 0)
            throw new ArgumentException($"{cmd} 需要至少一个输入文件");
        foreach (var f in files)
            if (!File.Exists(f))
                throw new ArgumentException($"文件不存在：{f}");
    }

    private static string ParseValue(string[] args, ref int i, string flag)
    {
        if (i + 1 >= args.Length)
            throw new ArgumentException($"{flag} 需要参数值");
        i++;
        return args[i];
    }

    private static int ParseInt(string[] args, ref int i, string flag)
    {
        string v = ParseValue(args, ref i, flag);
        return int.TryParse(v, out int n) ? n : throw new ArgumentException($"{flag} 需要整数");
    }

    private static int UnknownCommand(string cmd)
    {
        Console.Error.WriteLine($"未知命令：{cmd}");
        PrintUsage();
        return 1;
    }

    private static int PrintUsage()
    {
        Console.WriteLine($"""
            TextTool 命令行工具 v{Version}

            用法：
              texttool merge <文件...> [选项]      行合并 + 后处理
              texttool replace <文件...> [选项]    标点符号替换
              texttool vn <文件...> [选项]        视觉小说排版
              texttool join <目录> [选项]         文件拼接
              texttool update [--check]           自更新（--check 仅检查）

            运行 texttool <命令> --help 查看各命令选项。
            """);
        return 0;
    }

    private static void PrintUpdateHelp() =>
        Console.WriteLine("""
            update 选项：
              --check       仅检查是否有新版本，不执行更新
            """);

    private static void PrintMergeHelp() =>
        Console.WriteLine("""
            merge 选项：
              --threshold <N>      行合并阈值（默认 20）
              --chars / --bytes    按字符数 / 字节数（默认字符数）
              --overwrite          直接覆盖原文件（危险，写前自动备份）
              --no-cjk             禁用中文截断修复
              --fix-punct          启用标点截断修复
              --punct-chars <s>    标点截断字符集（默认 "，"）
              --no-trim-comma      禁用行首逗号清理
            """);

    private static void PrintReplaceHelp() =>
        Console.WriteLine("""
            replace 选项：
              --scheme <名>        使用指定预设方案（默认用配置的替换规则）
            """);

    private static void PrintVnHelp() =>
        Console.WriteLine("""
            vn 选项：
              --scheme <名>        使用指定角色方案（可多次指定）
              --max-para <N>       段落最大字数（默认 450）
              --reformat-only      仅排版
              --punct-only         仅补标点
            """);

    private static void PrintJoinHelp() =>
        Console.WriteLine("""
            join 选项：
              --pattern <glob>     文件匹配模式（默认 *.txt）
              --output <名>        输出文件名（默认 joined.txt）
            """);
}
