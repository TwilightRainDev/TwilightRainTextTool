using System.Net.Http;
using System.Runtime.ExceptionServices;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using TextTool.Services;

namespace TextTool.Cli;

/// <summary>
/// TextTool 命令行入口 — 供脚本化批处理/CI 集成使用。
///
/// 用法：
///   texttool merge &lt;file...&gt; [--threshold N] [--chars|--bytes] [--overwrite] [--backup] [--no-cjk] [--no-punct] [--punct-chars &quot;，&quot;]
///   texttool replace &lt;file...&gt; [--scheme &lt;name&gt;]
///   texttool vn &lt;file...&gt; [--scheme &lt;name...&gt;] [--max-para N] [--reformat-only|--punct-only]
///   texttool join &lt;directory&gt; [--pattern &quot;*.txt&quot;] [--output &lt;name&gt;]
///   texttool lint &lt;file|dir...&gt; [--only &lt;id...&gt;] [--min-severity warn] [--genre doc|fiction|agent] [--baseline &lt;file&gt;] [--json]
///   texttool calibrate --human &lt;directory&gt; --ai &lt;directory&gt; [--genre doc|fiction|agent] [--json]
///   texttool reply-check [--json] [&lt;file&gt;|-]
///   texttool update [--check]
/// </summary>
public static class Program
{
    private static readonly string Version =
        typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public static int Main(string[] args)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        // stderr 行尾统一 LF（本工作区文本约定）：重定向到文件时不带进 CR。
        // 包一层保留 AutoFlush，内容与既有 stderr 一致，只改换行符
        Console.SetError(new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false))
        {
            AutoFlush = true,
            NewLine = "\n",
        });

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
                "lint" => RunLint(args[1..]),
                "calibrate" => RunCalibrate(args[1..]),
                "reply-check" => RunReplyCheck(args[1..]),
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
        bool fixCjk = true, fixPunct = false, trimComma = true, overwrite = false, backup = false;
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
                case "--backup": backup = true; break; // 隐含 --overwrite
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

        var writeMode = !overwrite && !backup ? WriteMode.NewFile
            : backup ? WriteMode.OverwriteWithBackup : WriteMode.Overwrite;
        return RunPerFile(files, (file, encoding) =>
            ProcessingPipeline.Run(file, encoding, options, post, writeMode).OutputPath);
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

        return RunPerFile(files, (file, encoding) =>
        {
            string content = File.ReadAllText(file, encoding);
            string replaced = PunctuationReplacer.Apply(content, rules);
            string outputPath = PathHelper.GetProcessedPath(file);
            AtomicFile.WriteAllText(outputPath, replaced, new UTF8Encoding(true));
            return Path.GetFileName(outputPath);
        });
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

        // maxPara=0 会在 SplitLongParagraph 中永不缩短文本，死循环占用 CPU
        if (maxPara < 1)
            throw new ArgumentException("--max-para 需要大于 0 的整数");

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

        // 场景标记正则：经 RegexGuard 带超时构造，防止配置来源模式触发 ReDoS
        Regex? scenePattern = null;
        if (scenePatterns.Count > 0)
        {
            try
            {
                scenePattern = RegexGuard.Create(string.Join("|", scenePatterns));
            }
            catch (ArgumentException ex)
            {
                throw new ArgumentException($"场景标记规则非法（方案包损坏或被篡改）：{ex.Message}");
            }
        }

        return RunPerFile(files, (file, encoding) =>
        {
            string content = File.ReadAllText(file, encoding);

            if (doReformat)
            {
                try
                {
                    content = new VNReformatterService(
                        characters: characters, routeNames: routeNames,
                        maxParaLength: maxPara, scenePattern: scenePattern).Reformat(content);
                }
                catch (RegexMatchTimeoutException)
                {
                    throw new InvalidOperationException(
                        "场景标记正则执行超时——所选方案的规则过于复杂，请编辑方案简化后重试");
                }
            }
            if (doFixPunct)
                content = new PunctFixerService().Fix(content);

            string outputPath = PathHelper.GetProcessedPath(file);
            AtomicFile.WriteAllText(outputPath, content, new UTF8Encoding(true));
            return Path.GetFileName(outputPath);
        });
    }

    /// <summary>
    /// 逐个文件跑一次处理：严格探测编码、处理、写盘，成功报一行 [成功]。
    ///
    /// 单个文件失败不中断，原因带文件名打到 stderr，接着跑下一份——
    /// 与界面的批量执行同一语义，一份坏文件不该让整批停在那里。
    /// 一份以上输入时印完整路径：同一批里不同目录下的同名文件，只印文件名分不清。
    /// 退出码：全成功 0，有失败 1。
    ///
    /// process 收到源文件与它的编码，返回写到哪儿（供 [成功] 那行显示）。
    /// </summary>
    private static int RunPerFile(
        IReadOnlyList<string> files, Func<string, Encoding, string> process)
    {
        bool failed = false;
        bool many = files.Count > 1;
        foreach (string file in files)
        {
            string label = many ? file : Path.GetFileName(file);
            try
            {
                var encoding = EncodingDetector.DetectStrict(file);
                string outputPath = process(file, encoding.Encoding);
                Console.WriteLine($"[成功] {label} → {outputPath}");
            }
            catch (Exception ex)
            {
                failed = true;
                Console.Error.WriteLine($"[失败] {label}：{ex.Message}");
            }
        }
        return failed ? 1 : 0;
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

        // 自动替换靠一个 cmd 脚本在进程退出后跑（见 SelfUpdater.BuildUpdateScript），
        // Linux 上没有对应实现。这里直接拒绝，不给出“下载成功但换不了”的假成功；
        // --check 只是查询，两个平台都放行。
        if (!checkOnly && !OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine(
                "错误：自动替换只在 Windows 上提供。Linux 请手动下载 TextTool-CLI-<版本>-linux-x64.zip 解压覆盖，" +
                "可以用 update --check 看看有没有新版。");
            return 1;
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

    /// <summary>
    /// lint：只报不改的 AI 味检查。
    /// 退出码与其它子命令不同：0 = 无命中，1 = 有命中，2 = 用法/读取错误。
    /// 单份稿读不了不打断整批：原因连同路径打到 stderr，其余照常出报告，跑完再按读取错误退 2。
    /// </summary>
    private static int RunLint(string[] args)
    {
        try
        {
            // Windows 控制台默认 GBK 码页：报隐形字符/中文时可能编码失败，强制 UTF-8 输出。
            // 宿主拒绝设置（无控制台、编码不受支持、权限受限）一律吞掉：此时按既有编码输出，
            // 遇到编不出的字符仍由外层 catch 响亮退 2，不静默。
            // Console.OutputEncoding 的 setter 只抛这三类：宿主无控制台/重定向失败为 IOException，
            // 权限受限为 SecurityException，编码不受支持为 ArgumentException；
            // 其它异常不属于「宿主拒绝设置」，不在这里吞。
            try { Console.OutputEncoding = new UTF8Encoding(false); }
            catch (Exception ex) when (ex is IOException or SecurityException or ArgumentException) { }

            var files = new List<string>();
            bool json = false;
            string? only = null, minSeverity = null, genre = null, baselineFile = null, logFile = null;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--json": json = true; break;
                    case "--only": only = ParseValue(args, ref i, "--only"); break;
                    case "--min-severity": minSeverity = ParseValue(args, ref i, "--min-severity"); break;
                    case "--genre": genre = ParseValue(args, ref i, "--genre"); break;
                    case "--baseline": baselineFile = ParseValue(args, ref i, "--baseline"); break;
                    case "--log": logFile = ParseValue(args, ref i, "--log"); break;
                    case "--help" or "-h": PrintLintHelp(); return 0;
                    default: files.Add(args[i]); break;
                }
            }

            if (files.Count == 0)
                throw new ArgumentException("lint 需要至少一个输入文件或目录（- 表示 stdin）");

            var onlyIds = only is null
                ? null
                : only.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            // 展开时读不了的子目录报出来接着查其余的，不整次停下
            var expanded = LintInputs.Collect(files, message => Console.Error.WriteLine(message));

            // 一次差分只回答「这一份比那份多了什么」：先把目录摊平再数，
            // 不要等整棵树读完才说这句
            if (baselineFile is not null && expanded.Count != 1)
                throw new ArgumentException(
                    "--baseline 只配一个输入文件：一次差分只回答「这一份比那份多了什么」");

            // 规则表加载、规则编译、档位筛选只做一次，逐份扫描复用同一个引擎。
            // 校验没过的那一次先把错记下来，等读不上的那几份报完再抛：
            // 一次调用里既有读不上的稿、又有拼错的规则时，读者先看到的仍是「哪几份读不上」。
            LintSession? session = null;
            Exception? setupError = null;
            try
            {
                session = LintSession.Create(genre, onlyIds, minSeverity);
            }
            catch (Exception ex)
                when (ex is ArgumentException or IOException or InvalidDataException
                    or System.Text.Json.JsonException or FormatException or NotSupportedException)
            {
                setupError = ex;
            }

            // 日志流开在第一次真要写的时候：一份报告都没出的那一次（规则拼错、基线读不上）
            // 不该留下一个空的日志文件。开不了的那一次也等报告出完再报错，与从前一样。
            StreamWriter? log = null;
            Exception? logError = null;
            ILintReportSink? sink = null;
            string? baselineText = null;
            int unreadable = 0;
            int readable = 0;
            bool anyHit = false;
            try
            {
                // 一份报告落地的三件事：输出（人读或 JSON）、命中日志、命中标记。
                // 逐份与并行两条路都走这里，两条路的输出形状因此只有一个来源。
                void Emit(string label, LintReport report)
                {
                    // 人读要标出每条命中属于哪份稿：多份时每份前面一行文件名。
                    // JSON 契约仍由 LintOutput 一处维护，两条分支的字段不受人读排版影响
                    sink ??= LintOutput.BeginSink(Console.Out, json);
                    sink.Add(report);

                    // 命中日志另存一份：留档与脚本读的是全部位置，不受人读报告的折叠与分组影响
                    if (logFile is not null && log is null && logError is null)
                    {
                        try
                        {
                            log = HitLog.OpenAppender(logFile);
                        }
                        catch (Exception ex)
                            when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                or NotSupportedException or SecurityException)
                        {
                            logError = ex;
                        }
                    }
                    log?.Write(HitLog.Lines(report));
                    anyHit |= report.Hits.Count > 0;
                }

                // 一份的收尾：读不了就点名路径与原因，读得到就计数并出报告。
                // 读之外的错（引擎自己坏了）不属于「这一份不行」，原样抛出去。
                static bool ReadError(Exception ex) => ex is IOException or UnauthorizedAccessException
                    or InvalidDataException or ArgumentException or NotSupportedException;

                void Handle(string label, LintReport? report, Exception? error)
                {
                    if (error is not null)
                    {
                        // 一份读不了不拖住整批：原因带完整路径打到 stderr，跑完再按读取错误退 2
                        if (!ReadError(error)) ExceptionDispatchInfo.Capture(error).Throw();
                        unreadable++;
                        Console.Error.WriteLine($"[失败] {label}：{error.Message}");
                        return;
                    }

                    readable++;
                    // 校验没过：只把读不上的那几份报出来，报告一份都不出
                    if (report is not null) Emit(label, report);
                }

                // 输入里带 - 时只能逐份：stdin 只有一条流，不能和别的稿同时读。
                // 引擎没建起来时同样逐份：那一次只报哪几份读不上，报告一份都不出。
                if (session is not null && expanded.Count > 1 && !expanded.Contains("-"))
                {
                    // 多份同时开工，结果按文件顺序落地，与逐份跑的输出顺序一致
                    LintBatch.Run(expanded, session, ReadFileForLint,
                        item => Handle(item.File, item.Report, item.Error));
                }
                else
                {
                    foreach (string file in expanded)
                    {
                        string label = file == "-" ? "(stdin)" : file;
                        string text;
                        try
                        {
                            text = file == "-" ? ReadStdin() : ReadFileForLint(file);
                        }
                        catch (Exception ex)
                        {
                            Handle(label, null, ex);
                            continue;
                        }

                        if (session is null)
                        {
                            Handle(label, null, null);
                            continue;
                        }

                        Handle(label, baselineFile is null
                            ? session.Scan(label, text)
                            : session.ScanAgainstBaseline(
                                label, text, baselineFile, baselineText ??= ReadBaseline(baselineFile)), null);
                    }
                }

                sink?.Finish();
            }
            finally
            {
                sink?.Dispose();
                log?.Dispose();
            }

            // 一份都读不到时不拿空报告装作检查过
            if (readable == 0)
            {
                Console.Error.WriteLine($"错误：{unreadable} 份稿都读不了，没有可以出报告的输入");
                return 2;
            }

            // 基线那份要在报规则错之前读：基线读不到时先说基线
            if (baselineFile is not null && baselineText is null)
                ReadBaseline(baselineFile);

            // 规则拼错的那一次一份报告都没出，错就在这里报
            if (setupError is not null)
                throw setupError;
            if (logError is not null)
                throw logError;
            if (log is not null)
                Console.Error.WriteLine($"[日志] 命中追加到 {logFile}");

            // 读不了的汇总到最后：报告照常给，退出码按读取错误退 2
            if (unreadable > 0)
            {
                Console.Error.WriteLine($"错误：{unreadable} 份稿读不了，上面的报告只覆盖读得到的那几份");
                return 2;
            }

            return anyHit ? 1 : 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"错误：{ex.Message}");
            return 2;
        }
    }

    private static string ReadFileForLint(string file)
    {
        if (!File.Exists(file))
            throw new ArgumentException($"文件不存在：{file}");
        var encoding = EncodingDetector.DetectStrict(file);
        return File.ReadAllText(file, encoding.Encoding);
    }

    /// <summary>
    /// 基线那份。读不到时报错要说清是基线，不能只说「文件不存在」：
    /// 一次带 --baseline 的调用涉及两个路径，读者改错哪一个都能停住，但得知这是哪一个。
    /// </summary>
    internal static string ReadBaseline(string file)
    {
        if (!File.Exists(file))
            throw new ArgumentException($"基线文件读不到：{file}");
        try
        {
            var encoding = EncodingDetector.DetectStrict(file);
            return File.ReadAllText(file, encoding.Encoding);
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            throw new ArgumentException($"基线文件读不到：{file}（{ex.Message}）", ex);
        }
    }

    /// <summary>stdin 按 UTF-8 读原始字节，不受控制台码页影响。</summary>
    private static string ReadStdin()
    {
        using var stream = Console.OpenStandardInput();
        using var reader = new StreamReader(stream, new UTF8Encoding(false));
        return reader.ReadToEnd();
    }

    /// <summary>
    /// calibrate：人类文本与模型文本两侧各跑一遍规则，按规则汇总命中与每千字密度。
    /// 退出码只有 0 与 2：这张表本身不是判定，跑完就算成功；缺参数、目录不存在、
    /// 档位拼错都是用法问题，用 2，与 lint 对用法错误的口径一致。
    /// </summary>
    private static int RunCalibrate(string[] args)
    {
        try
        {
            // 表里有中文标题，Windows 控制台默认 GBK 码页可能编不出其中的字
            try { Console.OutputEncoding = new UTF8Encoding(false); }
            catch (Exception ex) when (ex is IOException or SecurityException or ArgumentException) { }

            string? human = null, ai = null, genre = null;
            bool json = false;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--human": human = ParseValue(args, ref i, "--human"); break;
                    case "--ai": ai = ParseValue(args, ref i, "--ai"); break;
                    case "--genre": genre = ParseValue(args, ref i, "--genre"); break;
                    case "--json": json = true; break;
                    case "--help" or "-h": PrintCalibrateHelp(); return 0;
                    default:
                        throw new ArgumentException(
                            $"未知参数：{args[i]}（calibrate 只收 --human、--ai、--genre、--json）");
                }
            }

            // 缺的参数一次说全：两个都没给时逐条提示，会让人多跑一遍。
            var missing = new List<string>();
            if (human is null) missing.Add("--human <目录>");
            if (ai is null) missing.Add("--ai <目录>");
            if (missing.Count > 0)
            {
                Console.Error.WriteLine($"错误：calibrate 缺少 {string.Join(" 与 ", missing)}");
                return 2;
            }

            var result = LintCalibration.RunDirectories(human!, ai!, genre);
            Console.Write(json ? result.ToJson() + "\n" : result.Render());
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"错误：{ex.Message}");
            return 2;
        }
    }

    /// <summary>
    /// reply-check：回复里「离开文件就看不懂」的写法，一行一处。
    /// 回答闸把它当工具调用，输出因此是稳定契约：人读一行一处，JSON 带版本号。
    /// 退出码与 lint 一致：0 = 无命中，1 = 有命中，2 = 用法/读取错误。
    /// </summary>
    private static int RunReplyCheck(string[] args)
    {
        try
        {
            try { Console.OutputEncoding = new UTF8Encoding(false); }
            catch (Exception ex) when (ex is IOException or SecurityException or ArgumentException) { }

            bool json = false;
            string? input = null;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--json": json = true; break;
                    case "--help" or "-h": PrintReplyCheckHelp(); return 0;
                    case "-":
                        if (input is not null) throw new ArgumentException("reply-check 只读一个输入");
                        input = "-";
                        break;
                    default:
                        if (input is not null) throw new ArgumentException("reply-check 只读一个输入");
                        input = args[i];
                        break;
                }
            }

            string text = input is null or "-" ? ReadStdin() : ReadFileForLint(input);
            var hits = ReplyShapeScanner.FromStore().Scan(text);
            Console.Write(ReplyShapeOutput.Render(hits, json));
            return hits.Count > 0 ? 1 : 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"错误：{ex.Message}");
            return 2;
        }
    }

    private static void PrintReplyCheckHelp() =>
        Console.WriteLine("""
            reply-check 选项：
              --json                以 JSON 输出（Version 2：Version 与 Hits 两项，每条带 Id、Token、Line、Blocks）
              --help                显示本说明

            输入：一个文件路径，或传 - 从 stdin 读；不给也走 stdin。
            围栏代码块里的内容整块跳过：那是引用的原文，不是回复的措辞。

            退出码与 lint 一致：0 = 没有命中，1 = 有命中，2 = 用法或读取错误。
            """);

    private static void PrintCalibrateHelp() =>
        Console.WriteLine("""
            calibrate 选项：
              --human <目录>        人类写的文本目录（递归收 .txt）
              --ai <目录>           模型写的文本目录（递归收 .txt）
              --genre <doc|fiction|agent>  只跑该档规则；不给则整张规则表都跑
              --json                以 JSON 输出（Version 1：Genre、Human、Ai、SeparableCount、Rules）

            判定：人类侧零命中、模型侧有命中记「可分」；两侧都有命中且模型侧每千字命中
            达到人类侧三倍记「弱」；其余记「不可分」。密度用未取整的值算，表里只取两位小数。

            退出码：0 = 跑完，2 = 用法或读取错误（缺 --human/--ai、目录不存在、没有 .txt、档位拼错）。
            """);

    private static void PrintLintHelp() =>
        Console.WriteLine("""
            lint 选项：
              --json                以 JSON 输出（顶层 Version 与 Reports；命中带 Id、Line、Col、Snippet）
              --only <Id,...>       只跑指定规则，如 --only parallel-contrast,cliche-words
              --min-severity <info|warn>  只输出 warn 及以上（统计项不受影响）
              --genre <doc|fiction|agent> 按文本类型选规则：doc = 编程文档，fiction = 小说，
                                          agent = 智能体回复（读者要能一句句读懂，不受团队行话与代号）；
                                          不给则跑全部规则（与加档位之前一致）
              --baseline <文件>     只报相对这份稿子多出来的命中，每条规则给一行新增处数
              --log <文件>          把命中追加写成一行一条的 JSON 日志：留档、跑全库统计时用；
                                    日志里只有 ASCII 与汉字，别的字符转成 \uXXXX

            输入：文件或目录，可多个。给目录时递归收 .txt 与 .md，按路径排序；
                  软链接跟到真实目标，一份稿只检一次，指回上级的链接不会重复展开；
                  子目录读不了会点名路径报出来，其余文件照常检查。
                  目录里一个都收不到按用法错误退 2，并点名是哪个目录。
                  传 - 表示从 stdin 读。带 --baseline 时只能给一个输入（目录按展开后的份数算）。

            --baseline 的用法：写前那一份当基线、写后那一份当输入，行号按写后那份算。
            存量命中不再列出，退出码只看新增：改写没有让任何判据变多就是 0。

            退出码（与其它子命令不同）：0 = 无命中，1 = 有命中，2 = 用法/读取错误。
            单份稿读不了不打断整批：原因连同路径打到 stderr，报告只覆盖读得到的部分，最后退 2。
            命中判定基于过滤后的集合，因此 --min-severity warn 即「只在有 warn 时退 1」。
            """);

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
              texttool lint <文件|目录...> [选项]  AI 味检查（只报不改）
              texttool calibrate [选项]            规则标定：人类文本与模型文本两侧对比
              texttool reply-check [选项] [文件]    回复里读者解不开的写法（只报不改）
              texttool update [--check]           自更新（--check 仅检查）

            运行 texttool <命令> --help 查看各命令选项。
            """);
        return 0;
    }

    private static void PrintUpdateHelp() =>
        Console.WriteLine("""
            update 选项：
              --check       仅检查是否有新版本，不执行更新

            Windows 上不带 --check 会下载并自动替换；Linux 上只能查询，替换请手动做。
            """);

    private static void PrintMergeHelp() =>
        Console.WriteLine("""
            merge 选项：
              --threshold <N>      行合并阈值（默认 20）
              --chars / --bytes    按字符数 / 字节数（默认字符数）
              --overwrite          直接覆盖原文件，不留备份（危险）
              --backup             覆盖原文件，覆盖前备份为 .bak（隐含 --overwrite）
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
