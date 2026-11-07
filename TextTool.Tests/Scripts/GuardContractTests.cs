using System.Reflection;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using TextTool.Cli;

namespace TextTool.Tests.Scripts;

/// <summary>
/// 回答闸与工具侧的契约：闸按 projectId 认工具返回的规则 Id，两边对不上时闸会静悄悄少一条判据。
/// 两边各写各的名单，对不上时闸会静悄悄少一条判据；这里把它们对起来，顺带核对闸写死的工具落点。
///
/// 只钉两个仓之间真的会打架的两位：可执行文件名与目标框架目录名。
/// 闸从工作区根拼过去时中间那几段（目录前缀、bin、配置名）是闸自己的落点写法，
/// 它换写法时工具仓一行没错也会红，所以这里不钉：只在它明写 bin 时核一句配置。
///
/// 向上找得到护栏文件时才跑，找不到带原因跳过。要让「找不到」算失败——例如两个仓都拉了的流水线——
/// 设 TEXTTOOL_GUARD_REQUIRED=1：跳过在这类流水线上恒成立，护栏那边改错一个 Id 也一路全绿。
/// </summary>
public class GuardContractTests
{
    /// <summary>护栏的规则身份表，相对工作区根，用 / 分隔。</summary>
    public const string RulesFile = "Config/Extensions/guard/reply/rules.ts";

    /// <summary>护栏里写死工具落点的文件，与规则表同根。</summary>
    private const string HostFile = "Config/Extensions/guard/host.ts";

    /// <summary>
    /// 护栏 rules.ts 里现有 10 个 projectId：8 条规则，其中 jargon 那条带三个
    /// （agent-process-jargon、agent-smoke-jargon、agent-test-jargon）。
    /// 读到比这少只有两种可能：正则只啃到一部分（读到几个也算读到），或那边的名单比这短。
    /// 两种都会让这条校验在少认几条规则的情况下变绿，所以数量本身也要核。
    /// </summary>
    private const int 已知ProjectId条数 = 10;

    [GuardFact]
    public void ProjectIds_AreCoveredByScannerRuleIds()
    {
        var 规则表 = RequireWorkspaceFile(RulesFile);
        var projectIds = ReadProjectIds(File.ReadAllText(规则表.Path));
        Assert.True(projectIds.Count >= 已知ProjectId条数,
            $"{规则表.Path} 里读到 {projectIds.Count} 个 projectIds，护栏现有 {已知ProjectId条数} 个：" +
            "读到更少说明护栏换了写法、这条正则只啃到一部分，或那边的名单比这短");

        // 扫描器按 OrdinalIgnoreCase 查表，只有大小写不同的写法在运行期照样认得，
        // 因此这里也按大小写不敏感比：报出来的必须是真的没被覆盖。
        var covered = ReplyShapeScanner.RuleIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = projectIds.Where(id => !covered.Contains(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Assert.True(missing.Count == 0,
            $"护栏按这些 projectId 认规则，ReplyShapeScanner.RuleIds 里没有：" + string.Join(", ", missing));
    }

    [GuardFact]
    public void ToolBuildPath_MatchesCliAssembly()
    {
        var 规则表 = RequireWorkspaceFile(RulesFile);
        var 护栏脚本 = RequireWorkspaceFile(HostFile);

        // 两份文件得在同一个工作区根下找到：一份在父仓、一份在下层仓里各找各的，
        // 拼工作区根时就会指到别处。
        Assert.Equal(规则表.Root, 护栏脚本.Root);

        var text = File.ReadAllText(护栏脚本.Path);
        var array = Regex.Match(text, @"TOOL_BUILD\s*=\s*\[([^\]]*)\]");
        Assert.True(array.Success, $"{HostFile} 里没找到 TOOL_BUILD 的字面数组：护栏换了写法，这条校验要跟着改");

        var segments = Regex.Matches(array.Groups[1].Value, "\"([^\"]+)\"")
            .Select(match => match.Groups[1].Value).ToList();
        Assert.True(segments.Count >= 2, $"TOOL_BUILD 只读到 {segments.Count} 段，拼不出一条路径");

        var cli = typeof(Program).Assembly;

        // 闸按最后一段找可执行文件：程序集改名而这里没跟着改，闸就永远起不来。
        Assert.Equal(cli.GetName().Name + ".dll", segments[^1]);

        // 倒数第二段是目标框架目录，随 TargetFramework 走；工具仓升了框架而闸没跟上，
        // 闸拼出来的是一个不存在的目录。
        Assert.Equal(TargetFrameworkFolder(cli), segments[^2]);

        // 配置段只在闸明写 bin 时核：写错的配置段让闸去找一份没人产出的文件。
        // 闸允许 Debug 构建、或干脆换了落点时，两个仓并没有对不上，不该在这里红。
        if (segments.Count >= 4 && segments[^4] == "bin")
            Assert.Equal("Release", segments[^3]);
    }

    /// <summary>规则表里的全部 projectIds。只认 <c>projectIds: [ ... ]</c> 这种字面数组，类型声明那行不匹配。</summary>
    private static List<string> ReadProjectIds(string text)
    {
        var ids = new List<string>();
        foreach (Match array in Regex.Matches(text, @"projectIds\s*:\s*\[([^\]]*)\]"))
            ids.AddRange(Regex.Matches(array.Groups[1].Value, "\"([^\"]+)\"").Select(match => match.Groups[1].Value));
        return ids;
    }

    /// <summary>".NETCoreApp,Version=v8.0" 归一成输出目录名 "net8.0"。</summary>
    private static string TargetFrameworkFolder(Assembly assembly)
    {
        string name = assembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName ?? "";
        var match = Regex.Match(name, @"Version=v([0-9]+\.[0-9]+)");
        Assert.True(match.Success, $"{assembly.GetName().Name} 上读不到目标框架，元数据里是：{name}");
        return "net" + match.Groups[1].Value;
    }

    /// <summary>
    /// 从测试程序集目录向上逐级找护栏文件，找到时把「当作工作区根的那一级目录」一并带出来。
    ///
    /// 工作区根只有这一个来源：相对路径就是拼在这一级上找的。
    /// 按固定层数（rules.ts 往上四级）另算一遍根，目录布局一变就报到别处去。
    /// </summary>
    private static (string Path, string Root)? FindWorkspaceFile(string relativePath)
    {
        string tail = relativePath.Replace('/', Path.DirectorySeparatorChar);
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, tail);
            if (File.Exists(candidate)) return (candidate, dir.FullName);
        }
        return null;
    }

    /// <summary>
    /// 取护栏文件，找不到就失败。什么情况下允许跳过由 <see cref="GuardFactAttribute"/> 定：
    /// 设了 TEXTTOOL_GUARD_REQUIRED 的检出没有跳过这一说，「说好护栏在场、实际没找到」要报出来。
    /// </summary>
    private static (string Path, string Root) RequireWorkspaceFile(string relativePath)
    {
        if (FindWorkspaceFile(relativePath) is { } found) return found;

        Assert.Fail(GuardFactAttribute.Required
            ? $"{GuardFactAttribute.RequiredEnv}=1 说好护栏在场，向上却没有找到 {relativePath}：" +
              "护栏改名、挪了位置，或这份检出只拉了工具仓"
            : $"找到了 {RulesFile} 却没有 {relativePath}：护栏的文件布局变了");
        return default; // Assert.Fail 不返回，这里只为让编译器满意
    }

    /// <summary>向上找规则表，找不到返回 null（跳过与否由 <see cref="GuardFactAttribute"/> 定）。</summary>
    public static (string Path, string Root)? FindRulesFile() => FindWorkspaceFile(RulesFile);
}

/// <summary>
/// 需要护栏在场的用例。向上找不到规则表时默认带原因跳过：只有工具仓库的检出里没有可比的对方，
/// 判失败会把「没带护栏」说成「契约坏了」，判通过又等于什么都没查。
///
/// 但跳过在只拉本仓的流水线上恒成立。知道护栏一定在场的检出（两个仓都拉了的流水线）
/// 设 TEXTTOOL_GUARD_REQUIRED=1，这时不跳过，缺文件由用例照实报失败。
/// </summary>
public sealed class GuardFactAttribute : FactAttribute
{
    /// <summary>设成 1：护栏必须在场，找不到规则表算失败，不再跳过。</summary>
    public const string RequiredEnv = "TEXTTOOL_GUARD_REQUIRED";

    /// <summary>本次运行是否要求护栏必须在场。</summary>
    public static bool Required => Environment.GetEnvironmentVariable(RequiredEnv) == "1";

    public GuardFactAttribute()
    {
        if (!Required && GuardContractTests.FindRulesFile() is null)
            Skip = $"向上没有找到 {GuardContractTests.RulesFile}：这份检出里没有护栏，跨仓契约无从比对" +
                   $"（要它必须在场就设 {RequiredEnv}=1）";
    }
}
