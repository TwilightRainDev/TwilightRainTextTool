using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;

namespace TextTool.Tests.Services;

public class SelfUpdaterTests
{
    [Theory]
    [InlineData("2.3.0", "TextTool-CLI-2.3.0-win-x64.zip")]
    [InlineData("v2.3.0", "TextTool-CLI-2.3.0-win-x64.zip")]
    public void GetCliAssetName_StripsV(string version, string expected)
    {
        Assert.Equal(expected, SelfUpdater.GetCliAssetName(version));
    }

    [Theory]
    [InlineData("abc", "abc  file.txt")]                      // 长度不足
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz", "x")] // 非法 hex
    public void TryParseSha256_Invalid_ReturnsFalse(string hash, string rest)
    {
        Assert.False(SelfUpdater.TryParseSha256($"{hash}  {rest}", out _));
    }

    [Fact]
    public void TryParseSha256_Valid_UppercaseNormalized()
    {
        const string sha = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";
        Assert.True(SelfUpdater.TryParseSha256($"{sha}  TextTool-CLI-2.4.0-win-x64.zip", out string hash));
        Assert.Equal(sha.ToLowerInvariant(), hash);
    }

    [Fact]
    public void FindCliAssetUrl_FindsMatchingAsset()
    {
        string json = $$"""
            {"assets": [
              {"name": "TextTool-GUI-2.4.0-win-x64.zip", "browser_download_url": "https://x/gui.zip"},
              {"name": "TextTool-CLI-2.4.0-win-x64.zip", "browser_download_url": "https://x/cli.zip"}
            ]}
            """;
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("https://x/cli.zip", SelfUpdater.FindCliAssetUrl(doc, "2.4.0"));
    }

    [Fact]
    public void FindCliAssetUrl_NoMatch_ReturnsNull()
    {
        string json = """
            {"assets": [{"name": "TextTool-CLI-2.3.0-win-x64.zip", "browser_download_url": "https://x/cli.zip"}]}
            """;
        using var doc = JsonDocument.Parse(json);
        Assert.Null(SelfUpdater.FindCliAssetUrl(doc, "2.4.0"));
    }

    [Fact]
    public void BuildUpdateScript_ContainsReplaceAndLaunch()
    {
        string script = SelfUpdater.BuildUpdateScript(
            installDir: @"C:\Tools\texttool",
            stagingDir: @"C:\Temp\texttool-update\2.4.0\staging",
            exePath: @"C:\Tools\texttool\texttool.exe",
            parentPid: 1234);

        Assert.Contains("DisableDelayedExpansion", script);  // 显式关闭延迟展开，防未来编辑引入注入面
        Assert.Contains("tasklist", script);          // 等待父进程退出
        Assert.Contains("1234", script);              // 父 PID
        Assert.Contains("robocopy", script);          // 覆盖安装目录
        Assert.Contains(@"C:\Tools\texttool", script);
        Assert.Contains(@"texttool.exe", script);
    }

    [Fact]
    public void BuildUpdateScript_QuotesPathsWithSpaces()
    {
        // 安装目录含空格时，robocopy/start 的目标必须正确引用，否则脚本无法覆盖安装目录
        string script = SelfUpdater.BuildUpdateScript(
            installDir: @"C:\Tools My App\texttool",
            stagingDir: @"C:\Temp\texttool-update\2.4.0\staging",
            exePath: @"C:\Tools My App\texttool\texttool.exe",
            parentPid: 4321);

        Assert.Contains("robocopy \"C:\\Temp\\texttool-update\\2.4.0\\staging\" \"C:\\Tools My App\\texttool\"", script);
        Assert.Contains("start \"\" /d \"C:\\Tools My App\\texttool\" \"C:\\Tools My App\\texttool\\texttool.exe\"", script);
    }

    [Fact]
    public void BuildUpdateScript_EscapesPercentInPaths()
    {
        // cmd 中 % 在引号内仍会做环境变量展开（唯一可注入字符），必须转义为 %%
        string script = SelfUpdater.BuildUpdateScript(
            installDir: @"C:\Tools\50%Off\texttool",
            stagingDir: @"C:\Temp\texttool-update\2.4.0\staging",
            exePath: @"C:\Tools\50%Off\texttool\texttool.exe",
            parentPid: 1234);

        Assert.Contains(@"C:\Tools\50%%Off\texttool", script);
        Assert.DoesNotContain(@"C:\Tools\50%Off", script);
    }

    [Fact]
    public void BuildUpdateScript_HandlesRobocopyFailure()
    {
        string script = SelfUpdater.BuildUpdateScript(
            installDir: @"C:\Tools\texttool",
            stagingDir: @"C:\Temp\texttool-update\2.4.0\staging",
            exePath: @"C:\Tools\texttool\texttool.exe",
            parentPid: 1234);

        Assert.Contains("/r:1 /w:1", script);     // 限制重试，避免 8 小时挂起
        Assert.Contains("GEQ 8", script);         // 复制失败分支
        Assert.Contains("已保留暂存目录", script); // 失败时不清空 staging
    }

    // ================================================================
    //  降级重放检测（审计发现 1 的残余：签名不覆盖版本号，MITM 可回放官方旧版）
    // ================================================================

    [Theory]
    [InlineData("2.4.0", "2.5.0", true)]   // 最新低于记录 → 疑似降级
    [InlineData("2.6.0", "2.5.0", false)]  // 正常更新
    [InlineData("2.5.0", "2.5.0", false)]  // 持平
    [InlineData("2.4.0", null, false)]     // 无记录（首次使用）
    [InlineData("2.4.0", "abc", false)]    // 记录损坏视为无记录
    public void IsDowngradeAttempt_DetectsVersionReplay(string latest, string? recorded, bool expected)
    {
        Assert.Equal(expected, SelfUpdater.IsDowngradeAttempt(latest, recorded));
    }

    [Fact]
    public void LastKnownVersion_Missing_ReturnsNull()
    {
        using var dir = new TempDir();
        Assert.Null(LastKnownVersion.Read(dir.Path));
    }

    [Fact]
    public void LastKnownVersion_RoundTrips()
    {
        using var dir = new TempDir();
        LastKnownVersion.Write("2.5.0", dir.Path);
        Assert.Equal("2.5.0", LastKnownVersion.Read(dir.Path));
    }

    // ================================================================
    //  解压安全上限（审计发现 9：zip 声明长度可伪造，须边解压边计数）
    // ================================================================

    [Fact]
    public void ExtractWithLimits_ExtractsNestedEntry()
    {
        using var dir = new TempDir();
        string zipPath = Path.Combine(dir.Path, "pkg.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("bin/app.exe");
            using var w = new StreamWriter(entry.Open());
            w.Write("hello");
        }

        string staging = Path.Combine(dir.Path, "staging");
        SelfUpdater.ExtractWithLimits(zipPath, staging, maxBytes: 1_000_000, maxEntries: 100);

        Assert.Equal("hello", File.ReadAllText(Path.Combine(staging, "bin", "app.exe")));
    }

    [Fact]
    public void ExtractWithLimits_ActualBytesOverCap_Throws()
    {
        // 2MB 零字节压缩后极小：声明长度与实际展开量不一致，必须以实读字节数为准
        using var dir = new TempDir();
        string zipPath = Path.Combine(dir.Path, "bomb.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("big.bin");
            using var w = new BinaryWriter(entry.Open());
            w.Write(new byte[2 * 1024 * 1024]);
        }

        Assert.Throws<InvalidOperationException>(() =>
            SelfUpdater.ExtractWithLimits(zipPath, Path.Combine(dir.Path, "staging"),
                maxBytes: 1024 * 1024, maxEntries: 100));
    }

    [Fact]
    public void ExtractWithLimits_ZipSlipEntry_Throws()
    {
        using var dir = new TempDir();
        string zipPath = Path.Combine(dir.Path, "slip.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("../evil.txt");
            using var w = new StreamWriter(entry.Open());
            w.Write("x");
        }

        Assert.Throws<InvalidOperationException>(() =>
            SelfUpdater.ExtractWithLimits(zipPath, Path.Combine(dir.Path, "staging"),
                maxBytes: 1_000_000, maxEntries: 100));
    }

    // ================================================================
    //  替换脚本 wait-loop 健壮化（A2）
    //  这两条刻意不用文本断言：A2 的故障全在 cmd 的运行时语义里
    //  （PATH 解析、%var% 展开时机），"脚本里有没有某一行"抓不住它们。
    // ================================================================

    private sealed record ScriptRun(bool Exited, string Output, string Error);

    /// <summary>
    /// 运行批处理并等待结束，超过 boundSeconds 未退出即强杀。返回是否自行退出——
    /// "不退出"本身就是被测行为，不能靠断言脚本文本来代替。
    /// 输出必须一并返回：光看退出与否会把"cmd 直接报错退出"误判成"脚本跑完"。
    /// </summary>
    private static ScriptRun RunScript(string scriptPath, int boundSeconds, string? pathPrefix = null)
    {
        var psi = new ProcessStartInfo("cmd.exe", $"/c \"{scriptPath}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(scriptPath)!,
        };
        if (pathPrefix is not null)
            psi.Environment["PATH"] = pathPrefix + ";" + psi.Environment["PATH"];

        using var proc = Process.Start(psi)!;
        // 必须并发把输出读走：管道写满会让子进程阻塞在写，等待方永远等不到退出
        Task<string> outDrained = proc.StandardOutput.ReadToEndAsync();
        Task<string> errDrained = proc.StandardError.ReadToEndAsync();
        bool exited = proc.WaitForExit(boundSeconds * 1000);
        if (!exited)
            proc.Kill(entireProcessTree: true);
        proc.WaitForExit();
        Task.WaitAll(outDrained, errDrained);
        return new ScriptRun(exited, outDrained.Result, errDrained.Result);
    }

    /// <summary>建一个 staging 目录，内含一个占位文件——替换是否真的发生，看它有没有落到安装目录。</summary>
    private static (string Install, string Staging) ArrangeDirs(TempDir dir)
    {
        string install = Path.Combine(dir.Path, "install");
        string staging = Path.Combine(dir.Path, "staging");
        Directory.CreateDirectory(install);
        Directory.CreateDirectory(staging);
        File.WriteAllText(Path.Combine(staging, "texttool.exe"), "new-build");
        return (install, staging);
    }

    [Fact]
    public void BuildUpdateScript_ParentPidStillAlive_StillAppliesUpdate()
    {
        // Windows 复用 PID：父进程退出后该 PID 若被别的进程占用，无次数上限的
        // wait-loop 会永久空转、替换永不发生。用存活的自身 PID 制造该场景。
        // 断言的是替换的最终结果（staging 落到安装目录），而不是"脚本退出了"——
        // 后者会被 cmd 直接报错退出伪造成通过。
        using var dir = new TempDir();
        var (install, staging) = ArrangeDirs(dir);
        string script = Path.Combine(dir.Path, "apply-update.cmd");
        File.WriteAllText(script, SelfUpdater.BuildUpdateScript(
            installDir: install,
            stagingDir: staging,
            exePath: @"C:\Windows\System32\where.exe",   // 无参即打印用法退出，不留常驻窗口
            parentPid: Environment.ProcessId,
            maxWaitTries: 2));

        ScriptRun run = RunScript(script, boundSeconds: 30);

        Assert.True(File.Exists(Path.Combine(install, "texttool.exe")),
            $"父 PID 存活时替换从未发生——wait-loop 缺少次数上限。stdout=[{run.Output}] stderr=[{run.Error}]");
    }

    [Fact]
    public void BuildUpdateScript_ShadowedTimeoutOnPath_IsNotInvoked()
    {
        // 裸 `timeout` 会被 PATH 靠前的同名命令遮蔽（本机 Git 的 GNU timeout 即在列）：
        // 参数被当成非法时长、命令立刻返回，循环退化成忙等（空耗 CPU），
        // 只有绝对路径才能保证等到真正的 Windows timeout。
        using var dir = new TempDir();
        var (install, staging) = ArrangeDirs(dir);
        string shadowDir = Path.Combine(dir.Path, "shadow");
        Directory.CreateDirectory(shadowDir);
        string marker = Path.Combine(dir.Path, "shadow-invoked.txt");
        File.WriteAllText(Path.Combine(shadowDir, "timeout.cmd"), $"@echo shadowed>\"{marker}\"\r\n");

        string script = Path.Combine(dir.Path, "apply-update.cmd");
        File.WriteAllText(script, SelfUpdater.BuildUpdateScript(
            installDir: install,
            stagingDir: staging,
            exePath: @"C:\Windows\System32\where.exe",
            parentPid: Environment.ProcessId,
            maxWaitTries: 2));

        ScriptRun run = RunScript(script, boundSeconds: 30, pathPrefix: shadowDir);

        // 先确认脚本确实跑到了替换：否则"没调用遮蔽 timeout"会因为脚本根本没跑而假绿
        Assert.True(File.Exists(Path.Combine(install, "texttool.exe")),
            $"脚本未完成替换，遮蔽断言无意义。stdout=[{run.Output}] stderr=[{run.Error}]");
        Assert.False(File.Exists(marker),
            "脚本调用了 PATH 上的裸 `timeout`，被同名命令遮蔽后循环退化为忙等");
    }

    [Fact]
    public void BuildUpdateScript_UsesCrlfLineEndings()
    {
        // 批处理必须以 CRLF 落盘：cmd 按字节偏移解析，LF 行尾叠加多字节字符会错位、
        // 把行从中间劈开（实测 setlocal 那行被拆成 'xpansion'），加固语句会被跳过。
        // 这条单列出来，是为了让"顺手统一成 LF"这种改动得到一个指名道姓的失败。
        string script = SelfUpdater.BuildUpdateScript(
            installDir: @"C:\Tools\texttool",
            stagingDir: @"C:\Temp\staging",
            exePath: @"C:\Tools\texttool\texttool.exe",
            parentPid: 1234);

        Assert.DoesNotContain("\n", script.Replace("\r\n", ""));
    }

    // ================================================================
    //  下载超时分级（A1）
    // ================================================================

    /// <summary>
    /// 应答 release 元数据与资产下载；资产应答可延迟，用来模拟慢下载。
    /// 只产出假响应，不碰网络。
    /// </summary>
    private sealed class FakeReleaseHandler : HttpMessageHandler
    {
        public const string AssetUrl =
            "https://github.com/TwilightRainDev/TwilightRainTextTool/releases/download/v9.9.9/TextTool-CLI-9.9.9-win-x64.zip";

        private readonly int _assetDelayMs;

        public FakeReleaseHandler(int assetDelayMs = 0) => _assetDelayMs = assetDelayMs;

        /// <summary>已应答的资产请求数——zip + .sha256 + .sig 三次都到齐，才算下载阶段跑完。</summary>
        public int AssetRequestCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body;
            if (request.RequestUri!.AbsolutePath.EndsWith("/releases/latest", StringComparison.Ordinal))
            {
                body = $$"""
                    {"tag_name":"v9.9.9","assets":[{"name":"TextTool-CLI-9.9.9-win-x64.zip",
                     "browser_download_url":"{{AssetUrl}}"}]}
                    """;
            }
            else
            {
                AssetRequestCount++;
                await Task.Delay(_assetDelayMs, cancellationToken);
                body = "payload";
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body)),
            };
        }
    }

    [Fact]
    public async Task UpdateAsync_AssetSlowerThanCallerTimeout_StillDownloads()
    {
        // 调用方 client 是"查询用"的短超时（无网时要秒级失败），而资产下载耗时超过它。
        // 下载若复用该 client 就会被掐断——这正是 S302 类代理下 zip 下载接近 30s
        // 撞上 30s 查询超时的形态。
        using var caller = new HttpClient(new FakeReleaseHandler())
        {
            Timeout = TimeSpan.FromMilliseconds(150),
        };
        var downloadClientHandler = new FakeReleaseHandler(assetDelayMs: 600);
        var downloadTimeouts = new List<TimeSpan>();

        SelfUpdater.UpdateResult result = await SelfUpdater.UpdateAsync(caller, "1.0.0", timeout =>
        {
            downloadTimeouts.Add(timeout);
            return new HttpClient(downloadClientHandler);
        });

        Assert.NotEmpty(downloadTimeouts);                                   // 下载没有搭调用方 client
        Assert.All(downloadTimeouts, t => Assert.True(t > caller.Timeout));  // 且超时确实更宽松
        Assert.Equal(3, downloadClientHandler.AssetRequestCount);            // zip/.sha256/.sig 都下完
        Assert.Equal("安装包校验失败（SHA256 不匹配），已中止更新", result.Error);
    }
}
