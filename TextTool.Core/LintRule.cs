using System.Reflection;
using System.Text;
using System.Text.Json;

namespace TextTool.Services;

/// <summary>
/// 一条 AI 味检查规则。两类规则共用本模型，区别只在 <see cref="Kind"/>：
/// 数据规则给出词面或正则，算法规则给出算法名——它由 <see cref="AlgorithmName"/> 指向引擎里的一段代码。
/// 一条规则的全部元数据（分组、标题、严重程度、适用档位、是否默认关、说明）只写在规则表里，
/// 代码不再写第二份。
/// </summary>
public class LintRule
{
    /// <summary>Kind 的取值：literal 词面、regex 正则、algorithm 由代码算。</summary>
    public const string KindLiteral = "literal";
    public const string KindRegex = "regex";
    public const string KindAlgorithm = "algorithm";

    /// <summary>
    /// 取文案时认的三处文字，与译文键的后缀同名：LintRule_&lt;id&gt;_Title、_Detail、_Suggest。
    /// 自定义标记按这三个名字记，两处写岔了不会报错，只会静默退回译文。
    /// </summary>
    public const string TextFieldTitle = "Title";
    public const string TextFieldDetail = "Detail";
    public const string TextFieldSuggest = "Suggest";

    public string Id { get; set; } = "";
    public string Group { get; set; } = "";
    public string Title { get; set; } = "";
    /// <summary>literal、regex 或 algorithm。</summary>
    public string Kind { get; set; } = KindRegex;
    /// <summary>
    /// 算法规则由哪段代码算：取值见 <see cref="AiToneLintService.AlgorithmNames"/>。
    /// 数据规则留空。名字写错加载即报，不会静默变成一条永远跑不到的规则。
    /// </summary>
    public string? AlgorithmName { get; set; }
    /// <summary>多条模式——repeated-punctuation 有 2 条正则、cliche-words 有 26 条词，单串装不下。</summary>
    public List<string> Patterns { get; set; } = new();
    /// <summary>
    /// 纯字面子串：写什么就找什么，`已.*完成` 也按这六个字面字符找，不会被当成正则。
    /// 一条判据常常既要几个固定说法又要一类结构，而 Kind 只能取一个值，
    /// 所以固定说法写这里、结构写 Patterns。两个列表都非空时按「词面在前、正则在后」编译，
    /// 这个顺序也是 Hints 的下标顺序。
    /// </summary>
    public List<string>? LiteralPatterns { get; set; }
    /// <summary>
    /// 段落在 markdown 语境下跳过本规则。行内标题列表这类判据只对纯文本文档成立，
    /// markdown 的「- **要点**：」是标准写法，不是 AI 痕迹。
    /// </summary>
    public bool SkipInMarkdown { get; set; }
    /// <summary>
    /// 博文稿不跑本判据。只在没指定档位的博文稿上生效：指定了 --genre 就等于告诉工具按谁的尺子量，照跑。
    /// 给两类判据用：一是历史叙事那五条，博文正文讲的就是过程，全篇都是这类词（护栏在博文路径上也不拿它提醒）；
    /// 二是越界字符，博文用箭头画流程、用颜文字收尾，那是正文内容，不是排版事故。
    /// 博文按 <see cref="BlogMarkdownMask.IsBlogPost"/> 认，不是只要带头部字段就算。
    /// </summary>
    public bool SkipInBlog { get; set; }
    /// <summary>document 或 paragraph。</summary>
    public string Scope { get; set; } = "document";
    /// <summary>
    /// 适用档位：doc（编程文档）、fiction（小说）、agent（智能体回复）。留空 = 各档通用。
    /// 留空不是「不适用」：写成 ["doc"] 的规则在小说档与智能体档里都会静默消失。
    /// </summary>
    public List<string> Genres { get; set; } = new();
    /// <summary>仅 Scope=paragraph 有效：只检查段末 N 字。</summary>
    public int? TailChars { get; set; }
    /// <summary>每条 Pattern 各自的出现次数达到本值才报。</summary>
    public int MinCount { get; set; } = 1;
    /// <summary>info 或 warn。</summary>
    public string Severity { get; set; } = "info";
    /// <summary>
    /// 命中时是否值得打断对话当场重写。默认 false：新判据默认不获得打断权。
    /// 与 Severity 不是一回事：Severity 说的是这条值不值得改，本字段说的是值不值得停下来说。
    /// 只有实时闸读它，lint 不看；留空的通用判据也可标，档位专属的必须是 agent 档。
    /// </summary>
    public bool BlocksReply { get; set; }
    /// <summary>
    /// 回复里要凑够几处才值得按上一字段处置。留空按 1 算；与 MinCount 分开，因为
    /// lint 的门限是给文档定的，回复里的密度是另一件事。
    /// </summary>
    public int? ReplyMinCount { get; set; }
    /// <summary>
    /// 人读报告折叠阈值（每千字处数）。同一规则在同一份报告里处数够多、密度又够高时，
    /// 人读视图只列三条例样加一行统计，位置一条不删；全量位置仍在 --json 里。
    /// 只对「同一模板反复」那几条开放：标点、隐形字符这类位置是给人照着改的，不折叠。
    /// </summary>
    public int? CollapseAtDensity { get; set; }
    /// <summary>
    /// 触发密度门限：哪种分母下的每千字命中处数，不到本值一条都不报。只对 Scope=document 有效。
    ///
    /// 词表类规则要用它而不是 MinCount：一个「石头」在万字稿里是正常用词，同一套比喻
    /// 千字里出现五次才是病灶。按绝对次数定门限，长文的正常用词会随篇幅一起堆过门限，
    /// 短文里的真问题又永远凑不够次数。
    /// </summary>
    public LintDensity? Density { get; set; }
    /// <summary>
    /// 旧写法：只表达「全文每千字多少处」，等价于 Density = { Basis: "document", Min: 本值 }。
    /// 保留它是为了让已写好的规则文件与外部配置照旧生效；两种写法只能出现一种，
    /// 同时写分不清哪个算数，加载即报错。新写规则用 Density。
    /// </summary>
    public int? MinDensityPerKChar { get; set; }

    /// <summary>
    /// 实际生效的密度门限。旧写法在这里折算成 document 基准，调用方只认这一个入口，
    /// 两种写法因此走同一条判定路径、给出同一个结果。
    /// 不进 JSON：它由另外两个字段算出来，写回规则文件等于凭空多出一份可能自相矛盾的配置。
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public LintDensity? EffectiveDensity => Density
        ?? (MinDensityPerKChar is int legacy
            ? new LintDensity { Basis = LintDensity.BasisDocument, Min = legacy }
            : null);
    /// <summary>
    /// 默认不勾选：不点名就不跑，点名才跑。给「只在特定文体里才是毛病」的判据用。
    ///
    /// 命令行不给 --only 时这类判据整批跳过；界面里它们初始处于未勾状态，
    /// 用户勾上就进 --only 名单照跑。判据本身的误报门限没过真人语料时，
    /// 与其删掉，不如默认关着让人自己开。
    /// </summary>
    public bool DefaultOff { get; set; }
    public string Detail { get; set; } = "";
    /// <summary>
    /// 与「词面 + 正则」合并后的下标对齐的建议改法（可空）。
    /// 两种模式混着写时，条数按合计算，少一条加载就会失败。
    /// </summary>
    public List<string>? Hints { get; set; }
    public string? SuggestScheme { get; set; }

    /// <summary>三处文字都没有自定义过：内置规则、或者外部条目与内置一字不差时的常态。</summary>
    private static readonly IReadOnlySet<string> NoCustomText = new HashSet<string>(StringComparer.Ordinal);

    private IReadOnlySet<string> _customTextFields = NoCustomText;

    /// <summary>
    /// 本条的哪些文字（<see cref="TextFieldTitle"/> 等三处）出自程序目录里的 lint_rules.json，
    /// 且与内置写法不同。取词按它决定：列在这里的用规则表里的文字，其余去取译文，
    /// 译文缺了才退回规则表。
    ///
    /// 标记由 <see cref="LintRuleStore.Merge"/> 在现场逐处比对算出来，不进配置文件：
    /// 它是两份表比出来的结论，写回磁盘就变成第三份，用户改回内置写法之后它还会说谎。
    /// 界面保存一条规则写的是整条副本，只改了说明时标题那一栏也在文件里、值却与内置一字不差，
    /// 所以只能按字段比，不能按「文件里有没有这一项」判。
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlySet<string> CustomTextFields => _customTextFields;

    /// <summary>这一处文字用规则表里的写法（自定义过），还是去取译文。</summary>
    public bool UsesTableText(string field) => _customTextFields.Contains(field);

    /// <summary>合并现场标记这几处文字出自自定义条目。只该由 <see cref="LintRuleStore.Merge"/> 调。</summary>
    internal void MarkCustomText(params string[] fields) =>
        _customTextFields = fields.Length == 0
            ? NoCustomText
            : new HashSet<string>(fields, StringComparer.Ordinal);

    /// <summary>
    /// 逐字段复制，两个模式列表各自开新表：规则表按 Id 覆盖时要整条替换，
    /// 共用一份列表等于改一条规则时把别处的词表一起改了。
    /// 新增字段不补进来，覆盖或界面保存一次就会把它悄悄清空。
    /// 自定义标记也跟着走：它决定这条规则当下用哪份文字，副本丢了标记就会退回译文。
    /// </summary>
    public LintRule Clone() => new()
    {
        Id = Id, Group = Group, Title = Title, Kind = Kind, AlgorithmName = AlgorithmName,
        Patterns = new List<string>(Patterns),
        LiteralPatterns = LiteralPatterns is null ? null : new List<string>(LiteralPatterns),
        Scope = Scope,
        Genres = new List<string>(Genres), BlocksReply = BlocksReply,
        ReplyMinCount = ReplyMinCount,
        TailChars = TailChars, MinCount = MinCount, Severity = Severity,
        CollapseAtDensity = CollapseAtDensity,
        Density = Density is null
            ? null
            : new LintDensity { Basis = Density.Basis, Min = Density.Min, Window = Density.Window },
        MinDensityPerKChar = MinDensityPerKChar,
        DefaultOff = DefaultOff,
        SkipInMarkdown = SkipInMarkdown,
        SkipInBlog = SkipInBlog,
        Detail = Detail, Hints = Hints is null ? null : new List<string>(Hints),
        SuggestScheme = SuggestScheme,
        // 标记跟着副本走：副本丢了标记，它在报告里就会退回译文
        _customTextFields = _customTextFields,
    };
}

/// <summary>
/// 触发密度门限的分母与下限。
///
/// 决定分母是有代价的：全文每千字几处，判的是「这份稿子整体上有多爱用这套词」；
/// 净增每千字几处，判的是「这次改写有没有让它变多」。前者要整篇才成立，后者要两稿才成立。
/// </summary>
public sealed class LintDensity
{
    public const string BasisDocument = "document";
    public const string BasisParagraph = "paragraph";
    public const string BasisWindow = "window";
    public const string BasisDelta = "delta";

    /// <summary>
    /// 分母取哪段文本。取值集合见 <see cref="LintRuleStore.DensityBases"/>。
    /// 目前只实现 document 与 delta：另外两个先认取值、留报错，不静默按 document 算。
    /// </summary>
    public string Basis { get; set; } = BasisDocument;

    /// <summary>该基准下每千字命中的下限。小于等于 0 是配置错：门限为 0 等于没配。</summary>
    public double Min { get; set; }

    /// <summary>Basis=window 时的窗口长度（字）。其它基准写它没有意义，加载即报错。</summary>
    public int? Window { get; set; }
}

/// <summary>
/// 规则持久化：内置嵌入式资源 + 外部 lint_rules.json 按 Id 合并。
/// </summary>
public static class LintRuleStore
{
    /// <summary>资源名前缀取 RootNamespace（TextTool），不是程序集名（TextTool.Core）。</summary>
    internal const string EmbeddedResourceName = "TextTool.default_lint_rules.json";

    /// <summary>外部规则的文件名。界面写它，命令行读它，路径固定为程序目录。</summary>
    public const string OverrideFileName = "lint_rules.json";

    /// <summary>
    /// 档位取值全集。三处要用：规则文件的档位校验、命令行 --genre 的取值校验、界面的下拉项。
    /// 拼错一个字母的档位（novel、agents）不会报错，规则会在所有档位下静默不跑，所以只列这一次。
    /// </summary>
    public static readonly IReadOnlyList<string> GenreIds = new[] { "doc", "fiction", "agent" };

    /// <summary>
    /// 密度基准取值全集。校验认这四个，实现只做了 document 与 delta：
    /// paragraph 与 window 写进配置会加载通过、扫到这条规则时响亮报「尚未实现」，
    /// 而不是悄悄按全文算出一个看着合理的数。
    /// </summary>
    public static readonly IReadOnlyList<string> DensityBases = new[]
    {
        LintDensity.BasisDocument, LintDensity.BasisParagraph, LintDensity.BasisWindow, LintDensity.BasisDelta,
    };

    public static List<LintRule> Load()
    {
        var builtIn = GetDefaultRules();
        var external = LoadOverrides();
        // 两侧各自先校验，再合并，再校验合并结果。四个边界各有理由：
        // 算法撞码——external 的 Id 撞上代码产出的算法 Id 时先挡下来；
        // 内置侧——Merge 会 Clone，Patterns 为 null 时 Clone 先 NRE；
        // 外部侧——重复 Id 必须在这里得到清晰报错，否则 Merge 按「后者覆盖前者」
        //   静默取最后一条，用户无从知道自己的配置里写了两个同 Id 规则。
        ValidateOverrides(builtIn, external);
        Validate(builtIn);
        Validate(external);
        var merged = Merge(builtIn, external);
        Validate(merged);
        return merged;
    }

    /// <summary>
    /// 程序目录里那份外部规则。它只存被改过的条目，按 Id 覆盖内置；空列表就是全部走内置。
    /// 界面上的词表编辑页与命令行读的是同一份文件，改完两边立刻一致。
    /// </summary>
    /// <param name="baseDir">仅测试注入用，生产路径固定为程序目录。</param>
    public static List<LintRule> LoadOverrides(string? baseDir = null) =>
        JsonFileStore.Load<LintRule>(OverrideFileName, baseDir: baseDir);

    /// <summary>
    /// 写回外部规则。先按内置实际合并一遍再校验，坏数据不落盘：
    /// 写一个非法正则进文件，下次启动 Load 会抛，程序起不来。
    /// </summary>
    /// <param name="baseDir">仅测试注入用，生产路径固定为程序目录。</param>
    public static void SaveOverrides(List<LintRule> rules, string? baseDir = null)
    {
        var builtIn = GetDefaultRules();
        ValidateOverrides(builtIn, rules);
        Validate(rules);
        Validate(Merge(builtIn, rules));
        JsonFileStore.Save(OverrideFileName, rules, baseDir);
    }

    public static List<LintRule> GetDefaultRules()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(EmbeddedResourceName)
            ?? throw new InvalidOperationException($"缺少嵌入式资源 {EmbeddedResourceName}，请检查 csproj 的 EmbeddedResource 配置");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return JsonSerializer.Deserialize<List<LintRule>>(reader.ReadToEnd()) ?? new List<LintRule>();
    }

    /// <summary>外部规则按 Id 覆盖内置（覆盖后仍排原位置），新 Id 追加在末尾。Id 大小写不敏感。</summary>
    internal static List<LintRule> Merge(List<LintRule> builtIn, List<LintRule> external)
    {
        var result = builtIn.Select(rule => rule.Clone()).ToList();
        // 字典直接存下标：覆盖写回原位置，位置不变；
        // 若同 Id 的外部规则第二次进来仍能命中同一位置，不再依赖 IndexOf（那会因对象已被换掉而返回 -1）。
        // 比较口径与 Validate / ValidateOverrides / 下游 --only 一致，均为大小写不敏感。
        var index = result
            .Select((rule, i) => (rule.Id, Position: i))
            .ToDictionary(x => x.Id, x => x.Position, StringComparer.OrdinalIgnoreCase);
        // 比分的是内置写法，不是文件里那一条：外部条目是整条副本，与它自己比永远比不出改了什么
        var builtInById = builtIn.ToDictionary(rule => rule.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var rule in external)
        {
            if (index.TryGetValue(rule.Id, out int at))
            {
                result[at] = MarkChangedText(rule.Clone(), builtInById[rule.Id]);
            }
            else
            {
                // 外部侧同样 Clone：合并结果与调用方的对象脱钩，两侧对称
                var added = rule.Clone();
                // 新条目三处文字全部出自这份文件：内置表里没有它，译文里也不会有
                added.MarkCustomText(LintRule.TextFieldTitle, LintRule.TextFieldDetail, LintRule.TextFieldSuggest);
                result.Add(added);
                index[rule.Id] = result.Count - 1;
            }
        }
        return result;
    }

    /// <summary>
    /// 外部条目与内置逐处比文字，把不一样的那几处标成自定义。
    ///
    /// 比的是值不是有没有写：界面保存一条规则写的是整条副本，只改了说明时标题、
    /// 建议方案那几杠也照样躺在文件里，值却与内置一字不差——那几处该继续用译文。
    /// </summary>
    private static LintRule MarkChangedText(LintRule rule, LintRule builtIn)
    {
        var changed = new List<string>(3);
        if (!string.Equals(rule.Title, builtIn.Title, StringComparison.Ordinal))
            changed.Add(LintRule.TextFieldTitle);
        if (!string.Equals(rule.Detail, builtIn.Detail, StringComparison.Ordinal))
            changed.Add(LintRule.TextFieldDetail);
        if (!string.Equals(rule.SuggestScheme, builtIn.SuggestScheme, StringComparison.Ordinal))
            changed.Add(LintRule.TextFieldSuggest);
        rule.MarkCustomText(changed.ToArray());
        return rule;
    }

    /// <summary>
    /// 外部规则动算法规则的边界：按 Id 覆盖时只允许改标题、说明、严重程度、适用档位与是否默认关。
    /// 算法名与模式都归代码：改了算法名等于换一段代码算，加模式等于写了一份没人读的词表，
    /// 两种都会让「我改了配置」与「跑出来的结果」对不上，加载即报。
    /// 新 Id 的算法规则同样不受理——算法只存在于引擎里，规则文件能造出来的只有永远跑不到的空壳。
    /// </summary>
    internal static void ValidateOverrides(List<LintRule> builtIn, List<LintRule> external)
    {
        var algorithms = builtIn
            .Where(rule => rule is not null && rule.Kind == LintRule.KindAlgorithm)
            .ToDictionary(rule => rule.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var rule in external)
        {
            // null 与重复 Id 由 Validate 报，这里只管算法规则的边界
            if (rule is null)
                continue;
            if (!algorithms.TryGetValue(rule.Id, out LintRule? builtInRule))
            {
                if (rule.Kind == LintRule.KindAlgorithm)
                    throw new ArgumentException(
                        $"外部规则不能新增算法规则：{rule.Id}（算法随程序发布，只能按 Id 覆盖内置的那几条）");
                continue;
            }

            if (rule.Kind != LintRule.KindAlgorithm)
                throw new ArgumentException(
                    $"外部规则把算法规则 {rule.Id} 的 Kind 改成了 {rule.Kind}：算法规则只能覆盖标题、说明、严重程度、适用档位与是否默认关");
            if (!string.Equals(rule.AlgorithmName, builtInRule.AlgorithmName, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException(
                    $"外部规则改了算法规则 {rule.Id} 的算法名：{rule.AlgorithmName}（内置为 {builtInRule.AlgorithmName}）");
            if (rule.Patterns is { Count: > 0 })
                throw new ArgumentException(
                    $"外部规则给算法规则 {rule.Id} 写了 Patterns：这条规则命不命中由算法算，词表写了也不生效");
            if (rule.LiteralPatterns is { Count: > 0 })
                throw new ArgumentException(
                    $"外部规则给算法规则 {rule.Id} 写了 LiteralPatterns：这条规则命不命中由算法算，词面写了也不生效");
            if (rule.Hints is not null)
                throw new ArgumentException(
                    $"外部规则给算法规则 {rule.Id} 写了 Hints：建议改法由算法给，写进来只会对不上");
        }
    }

    internal static void Validate(List<LintRule> rules)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in rules)
        {
            if (rule is null)
                throw new ArgumentException("规则列表含 null 元素");
            if (string.IsNullOrWhiteSpace(rule.Id))
                throw new ArgumentException("规则 Id 不能为空");
            if (!seen.Add(rule.Id))
                throw new ArgumentException($"规则 Id 重复：{rule.Id}");
            if (rule.Kind is not (LintRule.KindLiteral or LintRule.KindRegex or LintRule.KindAlgorithm))
                throw new ArgumentException(
                    $"规则 {rule.Id} 的 Kind 非法：{rule.Kind}（可选 {LintRule.KindLiteral}、{LintRule.KindRegex}、{LintRule.KindAlgorithm}）");
            // 算法规则的「实现」只能由算法名指认，且必须是引擎认识的那几个：
            // 名字拼错或干脆没写，规则会在报告里永远沉默，读者分不出「没问题」与「没跑」
            if (rule.Kind == LintRule.KindAlgorithm)
            {
                if (string.IsNullOrWhiteSpace(rule.AlgorithmName))
                    throw new ArgumentException(
                        $"算法规则 {rule.Id} 没写 AlgorithmName：它指到引擎里的实现，缺了这条永远跑不到");
                if (!AiToneLintService.AlgorithmNames.Contains(rule.AlgorithmName, StringComparer.OrdinalIgnoreCase))
                    throw new ArgumentException(
                        $"规则 {rule.Id} 的算法名未知：{rule.AlgorithmName}（可选 {string.Join("、", AiToneLintService.AlgorithmNames)}）");
            }
            else if (!string.IsNullOrWhiteSpace(rule.AlgorithmName))
            {
                throw new ArgumentException(
                    $"规则 {rule.Id} 是 {rule.Kind} 规则，不能写算法名：{rule.AlgorithmName}");
            }
            if (rule.Scope is not ("document" or "paragraph"))
                throw new ArgumentException($"规则 {rule.Id} 的 Scope 非法：{rule.Scope}");
            if (rule.Genres is null)
                throw new ArgumentException($"规则 {rule.Id} 的 Genres 为 null");
            // 拼错的档位（novel、Doc）会让规则在任何 --genre 下都不跑，与 Id 撞码同类：加载即报
            foreach (var genre in rule.Genres)
                if (!GenreIds.Contains(genre))
                    throw new ArgumentException(
                        $"规则 {rule.Id} 的档位非法：{genre}（可选 {string.Join("、", GenreIds)}）");
            // 文档或小说专属判据标可打断是配置错：实时闸只跑智能体档（含通用判据），这个值在那里读不到
            if (rule.ReplyMinCount is < 1)
                throw new ArgumentException($"规则 {rule.Id} 的回复门限小于 1");

            if (rule.BlocksReply
                && rule.Genres.Count > 0
                && !rule.Genres.Contains("agent", StringComparer.OrdinalIgnoreCase))
                throw new ArgumentException($"规则 {rule.Id} 标了可打断，但档位不含 agent");
            if (rule.Severity is not ("info" or "warn"))
                throw new ArgumentException($"规则 {rule.Id} 的 Severity 非法：{rule.Severity}");
            // 显式 null 会被 System.Text.Json 写回属性，模型上的 = new() 默认值挡不住，
            // 不在这里拦就会变成 NullReferenceException 而非清晰的校验报错
            if (rule.Patterns is null)
                throw new ArgumentException($"规则 {rule.Id} 的 Patterns 为 null");
            if (rule.Patterns.Any(string.IsNullOrEmpty))
                throw new ArgumentException($"规则 {rule.Id} 的 Patterns 含空串");
            // 空串字面会命中任意位置，写进来是配置错，不是「暂时留空」
            if (rule.LiteralPatterns is not null && rule.LiteralPatterns.Any(string.IsNullOrEmpty))
                throw new ArgumentException($"规则 {rule.Id} 的 LiteralPatterns 含空串");
            if (rule.Kind != LintRule.KindAlgorithm
                && rule.Patterns.Count == 0
                && (rule.LiteralPatterns is null || rule.LiteralPatterns.Count == 0))
                throw new ArgumentException($"规则 {rule.Id} 至少给一条词面或正则");
            // 算法规则的模式与建议都出自代码，表里再写一份只会让人以为改它有用
            if (rule.Kind == LintRule.KindAlgorithm && rule.Patterns.Count > 0)
                throw new ArgumentException(
                    $"算法规则 {rule.Id} 不能写 Patterns：命不命中由算法算，词表写了也不生效");
            if (rule.Kind == LintRule.KindAlgorithm && rule.LiteralPatterns is { Count: > 0 })
                throw new ArgumentException(
                    $"算法规则 {rule.Id} 不能写 LiteralPatterns：命不命中由算法算，词面写了也不生效");
            if (rule.Kind == LintRule.KindAlgorithm && rule.Hints is not null)
                throw new ArgumentException(
                    $"算法规则 {rule.Id} 不能写 Hints：建议改法由算法给，写进来只会对不上");
            if (rule.CollapseAtDensity is <= 0)
                throw new ArgumentException($"规则 {rule.Id} 的折叠密度必须为正整数");
            if (rule.MinDensityPerKChar is <= 0)
                throw new ArgumentException($"规则 {rule.Id} 的触发密度必须为正整数");
            // 两种写法同时出现时哪个算数说不清，读者会以为自己改的那份生效了
            if (rule.Density is not null && rule.MinDensityPerKChar is not null)
                throw new ArgumentException(
                    $"规则 {rule.Id} 的触发密度写了两种：Density 与 MinDensityPerKChar 只能留一种");
            if (rule.Density is not null)
            {
                var density = rule.Density;
                if (!DensityBases.Contains(density.Basis))
                    throw new ArgumentException(
                        $"规则 {rule.Id} 的密度基准非法：{density.Basis}（可选 {string.Join("、", DensityBases)}）");
                if (density.Min <= 0)
                    throw new ArgumentException($"规则 {rule.Id} 的触发密度必须为正数");
                if (density.Window is <= 0)
                    throw new ArgumentException($"规则 {rule.Id} 的窗口长度必须为正整数");
                // 写了窗口基准却没给窗口长度，算出来的是一整篇的密度，而不是窗口里的
                if (density.Basis == LintDensity.BasisWindow && density.Window is null)
                    throw new ArgumentException($"规则 {rule.Id} 的密度基准为 window，但没有给窗口长度");
                // 写了窗口却按别的基准算，读者会以为窗口生效了
                if (density.Window is not null && density.Basis != LintDensity.BasisWindow)
                    throw new ArgumentException(
                        $"规则 {rule.Id} 的 Window 仅在密度基准为 window 时有效");
            }
            // 段落级拿不到全文长度，密度算不出每千字：写在这里等于配了一个永远不生效的门限。
            // paragraph 基准的分母是段内字数，那条约束等它实现时再一起放开。
            if (rule.EffectiveDensity is { Basis: not LintDensity.BasisParagraph } && rule.Scope != "document")
                throw new ArgumentException($"规则 {rule.Id} 的触发密度仅在 Scope=document 时有效");
            if (rule.MinCount < 1)
                throw new ArgumentException($"规则 {rule.Id} 的 MinCount 必须 >= 1");
            if (rule.TailChars is <= 0)
                throw new ArgumentException($"规则 {rule.Id} 的 TailChars 必须为正整数");
            if (rule.TailChars is not null && rule.Scope != "paragraph")
                throw new ArgumentException($"规则 {rule.Id} 的 TailChars 仅在 Scope=paragraph 时有效");
            // 词面与正则混排时，改法按合并后的顺序排，条数也只能按合计对：
            // 少一条的后果是后面的建议整体错位，读者照错的建议改稿。
            int totalPatterns = rule.Patterns.Count + (rule.LiteralPatterns?.Count ?? 0);
            if (rule.Hints is not null && rule.Hints.Count != totalPatterns)
                throw new ArgumentException(
                    $"规则 {rule.Id} 的 Hints 长度与词面加正则合计不一致：{rule.Hints.Count} 条建议对 {totalPatterns} 条模式");
            if (rule.Kind == LintRule.KindRegex)
                foreach (var pattern in rule.Patterns)
                    try { RegexGuard.Create(pattern); }
                    catch (ArgumentException ex)
                    {
                        throw new ArgumentException($"规则 {rule.Id} 的正则非法：{pattern}（{ex.Message}）", ex);
                    }
        }
    }
}
