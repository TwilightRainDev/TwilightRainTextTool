using System.Text.RegularExpressions;

namespace TextTool.Services;

/// <summary>
/// 中文 AI 味检查引擎：只报位，不改写。
/// 两类规则都来自规则表：数据规则给出词面或正则，算法规则给出算法名。
/// 本类只放算法本体；分组、严重程度、标题、说明、适用档位全部取自规则表，
/// 其中标题、说明、建议方案在未自定义时换成当前语言的译文。
/// </summary>
public sealed class AiToneLintService
{
    private readonly List<CompiledRule> _rules;

    /// <summary>报告文案出处。默认中文原样返回，界面可注入按当前语言取词的实现。</summary>
    private readonly ILintText _text;

    public AiToneLintService(IEnumerable<LintRule> rules, ILintText? text = null)
    {
        _rules = rules.Select(CompiledRule.Create).ToList();
        _text = text ?? ChineseLintText.Instance;
    }

    /// <summary>
    /// 规则表里的文字与译文之间取哪一份：自定义过的字段用规则表里的原文，
    /// 其余去取译文，译文缺了才退回原文。args 给说明里的占位符。
    ///
    /// 自定义条目是用户在这台机器上改的，译文文件里那份说的是内置写法：
    /// 拿它盖掉用户改的字，界面报告正文就与规则清单对不上。命令行没有译文，一贯拿原文，
    /// 按这条取词，两边拿到的是同一份文字。
    /// </summary>
    internal static string RuleText(ILintText text, LintRule rule, string field, string fallback,
        params object[] args) =>
        rule.UsesTableText(field)
            ? Format(fallback, args)
            : text.Get($"LintRule_{rule.Id}_{field}", fallback, args);

    /// <summary>自定义文字里的占位符照常代入；花括号写坏了就原样给出，不让一条配置把整份报告打崩。</summary>
    private static string Format(string text, object[] args)
    {
        if (args.Length == 0)
            return text;

        try
        {
            return string.Format(text, args);
        }
        catch (FormatException)
        {
            return text;
        }
    }

    /// <summary>
    /// 扫描一份稿子。
    /// </summary>
    /// <param name="role">
    /// 这份稿子在比较里站什么位置。只有 delta 密度基准读它：净增要写前写后两稿才算得出来，
    /// 单扫一份时那条门限无从判定，报用法错而不是退回全文口径。
    /// </param>
    /// <param name="respectBlogDefaults">
    /// 博文稿上那几条标了 SkipInBlog 的判据跑不跑。点名指定规则时为 false：
    /// 用户把规则名写出来就是要看它，博文的默认口径不该把他的点名吃掉。
    /// </param>
    public LintReport Scan(string text, string fileName, string? genre = null,
        LintScanRole role = LintScanRole.Standalone, bool respectBlogDefaults = true)
    {
        // 判据按简体字面写，繁体稿直接比对不响：先备一份繁转简的副本专门拿去匹配。
        // 归一串保证与原文等长，两份串的下标、长度、行列完全通用，报告里的位置与片段仍取原文。
        // markdown 结构（头部字段、指令行、代码块、公式、链接地址）在归一份里遮成空格，
        // 规则与统计都碰不到它们，位置仍按原文算。
        var source = new ScanText(text);
        string mode = DetectParagraphMode(text);
        // 博文稿只有显式指定了档位才吃那几条标了 SkipInBlog 的判据：
        // 不指定档位等于让三张尺子一起上，那几条在博文上会把报告淹掉。
        bool blogDefault = source.IsBlogPost && genre is null && respectBlogDefaults;
        var report = new LintReport { File = fileName, Chars = source.Chars, ParagraphMode = mode, SourceText = text };
        // 折叠阈值随报告带出：渲染层不查规则表，界面与命令行两处看到的折叠口径因此一致
        foreach (var rule in _rules)
            if (rule.Rule.CollapseAtDensity is int density)
                report.CollapseAtDensity[rule.Rule.Id] = density;
        // 行首下标表建一次就够：逐命中从头重扫是 O(长度×命中数)
        var lineStarts = new LineStarts(text);
        // 按口径切好的段与切好的句子全篇只切一次：段落作用域规则有几十条，每条都重切一遍
        // 等于把全文复制几十份，长稿上这批副本就是内存峰值的大头
        var segments = new Segments(source.Matching, mode);

        // 算法规则共用一份上下文：切段只切一次，「节拍已报过」也要在算法之间传下去。
        // 真跑到一条算法规则时才建：没有算法规则的表（测试注入的探针规则）不必付这份开销。
        AlgorithmRun? algorithmRun = null;

        foreach (var rule in _rules)
        {
            if (rule.Rule.SkipInMarkdown && mode == "markdown")
                continue;
            if (rule.Rule.SkipInBlog && blogDefault)
                continue;
            // 算法规则也按规则表跑：档位不是它自己判的事，而是表里的 Genres。
            // 这里再筛一遍：LintRunner 已经按 ActiveRules 筛过，引擎也可能被直接调用（测试与报告重放），
            // 那时筛档只发生在本处，两条路径因此得到同一张规则表。
            if (rule.Rule.Kind == LintRule.KindAlgorithm)
            {
                if (!AppliesToGenre(rule.Rule, genre))
                    continue;
                algorithmRun ??= new AlgorithmRun
                {
                    Source = source, Mode = mode, Report = report, LineStarts = lineStarts, Text = _text,
                    Segments = segments,
                };
                algorithmRun.Rule = rule.Rule;
                RunAlgorithm(algorithmRun);
                continue;
            }
            if (rule.Rule.Scope == "paragraph")
                ScanParagraphScope(rule, source, report, segments, lineStarts);
            else
                ScanDocumentScope(rule, source, report, lineStarts, role);
        }
        // 命中按 (行, 列) 排一次：同一规则的多条 Pattern 各扫一遍会让行号来回跳。
        // 同一位置的多条命中保序（OrderBy 稳定），repeated-punctuation/ellipsis-style 这类同跨度双报的次序不动。
        // 人读渲染只做组内排序（LintTextFormatter.Format），整表排序在这里——
        // 组间顺序因此是「各组首命中的位置顺序」，不再是规则表顺序。
        report.Hits = report.Hits.OrderBy(h => h.Line).ThenBy(h => h.Col).ToList();
        return report;
    }

    /// <summary>判定段落口径的取样长度：4096 个字符，与编码探测的 4KB（ADR-004，4096 字节）同一量级但口径不同。</summary>
    internal const int ParagraphProbeChars = 4096;

    /// <summary>
    /// Markdown 判据：首 4096 字符的空行占行数比例。实测真实 .md 样本落在 7.7%–53%，
    /// 行式文本（无空行）为 0%——取 10% 偏向保守：漏判退化成行式口径，不误判行式文本。
    /// </summary>
    internal const double MarkdownBlankLineRatio = 0.10;

    /// <summary>
    /// 段落口径探测：行式文本（每行一段，本引擎的行式口径）或 Markdown（空行分段，软换行并入同段）。
    /// </summary>
    internal static string DetectParagraphMode(string text)
    {
        var probe = text.Length <= ParagraphProbeChars ? text : text[..ParagraphProbeChars];
        if (probe.EndsWith('\n')) probe = probe[..^1];

        int lines = 0, blanks = 0;
        foreach (var raw in probe.Split('\n'))
        {
            lines++;
            if (raw.Trim().Length == 0) blanks++;
        }
        // blanks < lines 兜住空串与纯空白文件："" 拆出 1 行 1 空行，否则会被判成 markdown
        return lines > 0 && blanks < lines && (double)blanks / lines >= MarkdownBlankLineRatio
            ? "markdown"
            : "line";
    }

    /// <summary>
    /// 算法名 → 实现。规则表的 AlgorithmName 指到这里：一张表既是分派表，也是加载时校验算法名的
    /// 取值全集（见 LintRuleStore.Validate）。加一条算法只在这里加一行，规则表里写错名字加载即报。
    ///
    /// 算法本体拿到的只有 <see cref="AlgorithmRun"/>：自己那条规则、待扫文本、报位入口与文案出处。
    /// 分组、严重程度、标题、说明、建议方案一律从规则表取，代码里不留第二份，也不再自己判档位。
    /// </summary>
    private static readonly Dictionary<string, Action<AlgorithmRun>> Algorithms =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["sentence-tempo"] = RunSentenceTempo,
            ["fiction-simile-pile"] = RunSimilePile,
            ["terminology-pile"] = RunTerminologyPile,
            ["mixed-quote-style"] = RunQuoteStyleMix,
            ["mixed-bracket-width"] = RunBracketWidthMix,
            ["paragraph-length-uniform"] = RunParagraphLengthUniform,
            ["sentence-length-uniform"] = RunSentenceLengthUniform,
            ["short-sentence-run"] = RunShortSentenceRun,
            ["repeated-paragraph-opening"] = RunRepeatedParagraphOpening,
            ["exclamation-density"] = RunExclamationDensity,
            ["ordinal-skeleton"] = RunOrdinalSkeleton,
            ["explicit-numbering"] = RunExplicitNumbering,
            ["repeated-verbatim"] = RunRepeatedVerbatim,
            ["repeated-token-run"] = RunRepeatedTokenRun,
            ["choppy-clauses"] = RunChoppyClauses,
        };

    /// <summary>引擎认识的全部算法名。规则表里的 AlgorithmName 必须落在这里，拼错加载即报。</summary>
    public static IReadOnlyCollection<string> AlgorithmNames => Algorithms.Keys;

    /// <summary>
    /// 算法规则的 Id 全集，从内置规则表算出来：不再是手写的第二份名单。
    /// --only 的取值校验、外部覆盖的边界检查、界面的规则清单三处共用这一份；
    /// 规则表里增删一条算法规则，这里跟着变，不需要另改代码。
    /// </summary>
    public static IReadOnlyList<string> AlgorithmRuleIds { get; } = LintRuleStore.GetDefaultRules()
        .Where(rule => rule.Kind == LintRule.KindAlgorithm)
        .Select(rule => rule.Id)
        .ToList();

    /// <summary>
    /// 算法规则与给定规则表的 Id 并集（--only 的校验集）。
    /// 算法规则就在表里，所以并集通常等于表本身；留着算法那一侧是为了让只传了部分规则的调用方
    /// （测试注入）仍然能点名到算法规则，报错口径不变。
    /// </summary>
    public static IReadOnlyCollection<string> AllRuleIds(IEnumerable<LintRule> rules)
    {
        var ids = new List<string>(AlgorithmRuleIds);
        foreach (var rule in rules)
            if (!ids.Contains(rule.Id)) ids.Add(rule.Id);
        return ids;
    }

    /// <summary>算法规则与内置数据规则的 Id 并集，即引擎实际可能产出的 Id 全集。</summary>
    public static IReadOnlyCollection<string> AllRuleIds() => AllRuleIds(LintRuleStore.Load());

    private void ScanDocumentScope(CompiledRule rule, ScanText source, LintReport report,
        LineStarts lineStarts, LintScanRole role)
    {
        var candidates = new List<(int Index, int Length, string? Hint)>();
        string matching = source.Matching;
        foreach (var (regex, hint) in rule.Patterns)
        {
            // 先收再摸：同一段文本只扫一遍。取出的是位置，不建 Match 对象（一个 Match 几十字节，
            // 五十兆的稿子单条词表就能有几十万处）。词表没凑够次数就把它那几条从末尾撤掉，
            // 顺序仍是词表的先后，与逐条收到齐再判一致。
            int from = candidates.Count;
            foreach (var m in regex.EnumerateMatches(matching))
                candidates.Add((m.Index, m.Length, hint));
            if (candidates.Count - from < rule.Rule.MinCount)
                candidates.RemoveRange(from, candidates.Count - from);
        }

        var kept = DropOverlaps(candidates);
        if (!MeetsDensity(rule, kept.Count, source.Chars, role)) return;

        foreach (var (index, length, hint) in kept)
            report.Hits.Add(Hit(rule.Rule, lineStarts, source, index, length, hint));
    }

    /// <summary>
    /// 密度门限：词表规则按千字处数判，不按绝对次数。
    /// 算的是去重后的处数——同一处被两条模式各咬一口不是两处证据。
    ///
    /// 分母按规则写的基准取：document 用全文长度；delta 的净增处数要差分之后才知道，
    /// 这里先全放行，由 LintBaseline.Apply 按「净增处数除以写后千字数」再筛一次。
    /// </summary>
    private static bool MeetsDensity(CompiledRule rule, int hits, int chars, LintScanRole role)
    {
        if (rule.Density is not { } density) return true;
        if (density.Basis == LintDensity.BasisDocument)
            return hits * 1000.0 / Math.Max(chars, 1) >= density.Min;
        if (density.Basis == LintDensity.BasisDelta)
        {
            if (role == LintScanRole.Standalone)
                throw new ArgumentException(
                    $"规则 {rule.Rule.Id} 的触发密度按净增算（Basis=delta），这次扫描没有基线：" +
                    "同一份稿子算不出净增，带 --baseline 再跑");
            return true;
        }
        // 加载时已校验过取值，能走到这里的只剩尚未实现的那两种基准
        throw new NotImplementedException(
            $"规则 {rule.Rule.Id} 的密度基准 {density.Basis} 尚未实现：先用 document 或 delta");
    }

    private void ScanParagraphScope(CompiledRule rule, ScanText source, LintReport report,
        Segments segments, LineStarts lineStarts)
    {
        var paragraphs = segments.Paragraphs;
        // MinCount 按段判定：段内序数词必须是同一段里凑够次数才算骨架，
        // 分散在多段的单次出现合计到达阈值不算（规则名与用例的「段内」口径）。
        foreach (var p in paragraphs)
        {
            // 窗口只与段有关，与 Pattern 无关：提到 Pattern 循环外
            int windowStart = rule.Rule.TailChars is int tail && p.Text.Length > tail
                ? p.Text.Length - tail
                : 0;
            var candidates = new List<(int Index, int Length, string? Hint)>();
            foreach (var (regex, hint) in rule.Patterns)
            {
                var matches = regex.Matches(p.Text, windowStart);
                if (matches.Count < rule.Rule.MinCount) continue;
                foreach (Match m in matches)
                    candidates.Add((m.Index, m.Length, hint));
            }

            foreach (var (index, length, hint) in DropOverlaps(candidates))
                report.Hits.Add(Hit(rule.Rule, lineStarts, source, p.Offset + index, length, hint));
        }
    }

    /// <summary>
    /// 一个偏移落没落在若干区间里的某一段上。区间要按起点升序且互不重叠——从文本里扫出来
    /// 或按偏移切出来的区间天然就是这个形状：二分找最后一个起点不晚于本偏移的段，再看它盖不盖住。
    /// 逐段扫一遍是 O(次数 × 段数)，在「每句 / 每个命中都问一次」的地方两项都随篇幅涨。
    /// </summary>
    private static bool InsideRange(List<(int Start, int End)> ranges, int offset)
    {
        int lo = 0, hi = ranges.Count - 1, found = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (ranges[mid].Start <= offset)
            {
                found = mid;
                lo = mid + 1;
            }
            else hi = mid - 1;
        }
        return found >= 0 && offset < ranges[found].End;
    }

    /// <summary>
    /// 同一条规则里，同一处只报一次：两张词表可能盖住同一个词（「砍一刀」与「这一刀」），
    /// 一个词也可能被两条词表从不同位置咬住（「验收口径」里既有「口径」又有「收口」）。
    /// 这两种都数两遍同一条证据，读者看到的命中数就虚高。重叠时留长的：读者认的是整个词。
    ///
    /// 取舍按优先级逐条过：长的先看，同长按起点。每条先问「与已留的某一段相交吗」，不相交才留。
    /// 逐条扫一遍已留段是 O(候选数 × 已留数)：候选数随篇幅涨，已留数也跟着涨，长稿上这一层就是
    /// 平方量级（五十兆的稿子单条规则几万个候选）。这里把「相交」换个写法：两段相交等于
    /// 「起点早于本段终点」的那批已留段里，有终点越过本段起点。于是每条只需要一次前缀最大值查询。
    /// 候选先按起点排成位置序，已留段的终点记进一棵前缀最大值树（二分索引），每条 O(log 候选数)，
    /// 比较次数不再乘上已留数。留与不留的判定与逐个扫完全一致，返回顺序也照旧按起点升序。
    ///
    /// 候选少时建索引比逐个比较还贵（段落作用域规则每次只给段内那几个候选），
    /// 所以不足 DropOverlapsScanLimit 条的仍逐个比较，两条路的取舍语义是同一套。
    /// </summary>
    internal static List<(int Index, int Length, string? Hint)> DropOverlaps(
        List<(int Index, int Length, string? Hint)> candidates)
    {
        if (candidates.Count <= 1) return candidates;
        if (candidates.Count <= DropOverlapsScanLimit) return DropOverlapsByScan(candidates);

        int count = candidates.Count;
        // 位置序：候选按起点升序排。相交判定里的「起点早于本段终点」在这条序上正好是一段前缀
        var byStart = new int[count];
        for (int i = 0; i < count; i++) byStart[i] = i;
        Array.Sort(byStart, (a, b) => candidates[a].Index != candidates[b].Index
            ? candidates[a].Index.CompareTo(candidates[b].Index)
            : a.CompareTo(b));
        var starts = new int[count];
        var rank = new int[count];   // 候选下标 → 位置序上的下标
        for (int i = 0; i < count; i++)
        {
            starts[i] = candidates[byStart[i]].Index;
            rank[byStart[i]] = i;
        }

        // 前缀最大值树：tree[i] 存位置序末尾是 i 的那一段（长度 lowbit(i)）里已留段终点的最大值。
        // 没留过是 -1；终点不会小于 0，所以 -1 天然输给任何一次比较
        var tree = new int[count + 1];
        Array.Fill(tree, -1);
        int MaxEndBefore(int positions)
        {
            int max = -1;
            for (int i = positions; i > 0; i -= i & -i)
                if (tree[i] > max) max = tree[i];
            return max;
        }
        void Keep(int position, int end)
        {
            for (int i = position + 1; i <= count; i += i & -i)
                if (end > tree[i]) tree[i] = end;
        }

        // 优先级序：长的先看，同长按起点；同长同起点的按候选原来的先后
        var byPriority = new int[count];
        for (int i = 0; i < count; i++) byPriority[i] = i;
        Array.Sort(byPriority, (a, b) => candidates[a].Length != candidates[b].Length
            ? candidates[b].Length.CompareTo(candidates[a].Length)
            : candidates[a].Index != candidates[b].Index
                ? candidates[a].Index.CompareTo(candidates[b].Index)
                : a.CompareTo(b));

        var kept = new List<(int Index, int Length, string? Hint)>();
        foreach (int i in byPriority)
        {
            var c = candidates[i];
            int end = c.Index + c.Length;
            // 起点早于本段终点的位置有几个：位置序已按起点升序，二分找第一个起点不早于本段终点的
            int lo = 0, hi = count;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (starts[mid] < end) lo = mid + 1;
                else hi = mid;
            }
            if (MaxEndBefore(lo) > c.Index) continue;   // 与已留段的某一段相交，让给已留的那段
            kept.Add(c);
            Keep(rank[i], end);
        }
        return kept.OrderBy(k => k.Index).ToList();
    }

    /// <summary>候选到这个数以下就直接两两比：索引一次要建四个数组加两次排序，小清单上不划算。</summary>
    private const int DropOverlapsScanLimit = 32;

    /// <summary>候选很少时的取舍：每条与已留的逐个比一遍，与旧写法一致。</summary>
    private static List<(int Index, int Length, string? Hint)> DropOverlapsByScan(
        List<(int Index, int Length, string? Hint)> candidates)
    {
        var kept = new List<(int Index, int Length, string? Hint)>();
        foreach (var c in candidates.OrderByDescending(c => c.Length).ThenBy(c => c.Index))
        {
            bool overlaps = kept.Any(k => c.Index < k.Index + k.Length && k.Index < c.Index + c.Length);
            if (!overlaps) kept.Add(c);
        }
        return kept.OrderBy(k => k.Index).ToList();
    }

    // 同一套引号里的双引号与单引号不是两种风格：中文规范用法就是「双引号里嵌单引号」，
    // 按单个字符算会把规范的嵌套引用判成混用。Family 只用来判混用，展示仍按单个名称报数量。
    private static readonly (string Family, string Key, string Name, Regex Pattern)[] QuoteStyles =
    {
        ("curly", "curly-double", "弯引号", RegexGuard.Create("[“”]")),
        ("curly", "curly-single", "弯单引号", RegexGuard.Create("[‘’]")),
        ("ascii", "ascii-double", "ASCII 直引号", RegexGuard.Create("\"")),
        ("ascii", "ascii-single", "ASCII 直单引号", RegexGuard.Create("'")),
        ("corner", "corner", "直角引号", RegexGuard.Create("[「」]")),
        ("corner", "double-corner", "双直角引号", RegexGuard.Create("[『』]")),
    };

    private static readonly Regex OrdinalSkeleton = RegexGuard.Create("首先|其次|再次|最后|其一|其二|其三");

    // RegexGuard.Create 每次调用都新建实例，mixed-bracket-width/sentence-length-uniform 的正则原先是每次扫描各造一遍
    private static readonly Regex HalfWidthBracket = RegexGuard.Create("[()]");
    private static readonly Regex FullWidthBracket = RegexGuard.Create("[（）]");
    private static readonly Regex AnyBracket = RegexGuard.Create("[()（）]");
    /// <summary>句末标点：切句只在句末标点之后，不另划一条正则（见 <see cref="SentenceLengths"/>）。</summary>
    private static bool IsSentenceEnd(char c) => c is '。' or '！' or '？' or '…';

    /// <summary>短句阈值：连着这么多句、每句不超过这么多字，就算节拍化碎片。</summary>
    internal const int ShortSentenceMaxChars = 10;
    internal const int ShortSentenceMinRun = 4;

    /// <summary>节拍窗口上限：800 字。比它短的稿子整篇当一窗，短于下限就不判——短窗的均句长由个别句子左右，判不出节拍。</summary>
    internal const int TempoWindowChars = 800;
    /// <summary>窗口下限与步长：400 字。相邻窗口重叠一半，节拍撞在窗口边界上也不会被切成两段而双双落标。</summary>
    internal const int TempoMinTextChars = 400;
    internal const int TempoWindowStep = 400;
    /// <summary>窗口内至少几句才判：句数太少，平均句长由个别句子决定，判不出节拍。</summary>
    internal const int TempoMinSentences = 10;
    /// <summary>参与均句长的最短句：与句长均齐度同口径，短句单独成句不计。</summary>
    internal const int TempoMinSentenceChars = 6;
    /// <summary>
    /// 平均句长门限（字）。实测十本人类长篇：800 字窗口最低 14.0 字、第 1 百分位 18.0；
    /// 全是十来字短句的稿子在 11.6 字。
    /// </summary>
    internal const double TempoMaxMeanChars = 15;
    /// <summary>
    /// 最长句门限（字）：整窗没有一句超过 28 字。人类长篇 500 字窗口里最长句最低 31 字、
    /// 800 字窗口最低 32 字——真人的段落总要有一句把话说长，节拍稿一句都没有。
    /// 取 28 而不是 31：一段用短台词填满的场面（人物一句一句往外丢话）最长句常落在 27 字上下，
    /// 卡 25 会整篇漏掉。人类十本的 500、600、800 字窗口在 28 上零误报，
    /// 400 字窗口在 6223 个里误报 4 个（0.06%），只在四百到五百字的短稿上才会碰到。
    /// </summary>
    internal const int TempoMaxSentenceChars = 28;

    /// <summary>逐字重复的下限：短于这个长度的段落与句子不比对，否则「好。」「他点了点头。」会把整篇报成重复。</summary>
    internal const int MinRepeatParagraphChars = 20;
    internal const int MinRepeatSentenceChars = 12;

    /// <summary>显式序号标记：行首的 1. / 1、/ 1) / （1）/ 一、 之类硬编号。</summary>
    private static readonly Regex ExplicitOrdinalMarker = RegexGuard.Create(
        @"(?m)^[ \t]*(?:[0-9]{1,2}[.、)]|[（(][0-9]{1,2}[)）]|[一二三四五六七八九十]+[.、])");

    /// <summary>这条规则在当前档位下跑不跑。留空 Genres 表示各档通用；不给档位就是不过滤。</summary>
    private static bool AppliesToGenre(LintRule rule, string? genre) =>
        genre is null
        || rule.Genres.Count == 0
        || rule.Genres.Contains(genre, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 按算法名分派到算法本体。名字已在加载时校验过，走到这里还不认识，只能是分派表与规则表不同步。
    /// </summary>
    private static void RunAlgorithm(AlgorithmRun run)
    {
        if (!Algorithms.TryGetValue(run.Rule.AlgorithmName ?? string.Empty, out Action<AlgorithmRun>? algorithm))
            throw new InvalidOperationException($"规则 {run.Rule.Id} 的算法没有实现：{run.Rule.AlgorithmName}");
        algorithm(run);
    }

    /// <summary>
    /// mixed-quote-style 引号风格混用：按「套」判，不按单个字符判——双引号里嵌单引号是规范写法，不是混用。
    /// 命中定位到全文最早出现的引号字符，各套的用量写进说明。
    ///
    /// 这一条的说明是按用量现算的（见 LintQuoteUsage），规则表里的 Detail 只描述这条判据；
    /// 其余算法的说明都直接取自规则表。
    /// </summary>
    private static void RunQuoteStyleMix(AlgorithmRun run)
    {
        string scan = run.Matching;
        var used = QuoteStyles
            .Select(s => (
                s.Family,
                Name: run.Text.Get($"LintQuote_{s.Key}", s.Name),
                Count: s.Pattern.Count(scan),
                Index: s.Pattern.Match(scan).Index))
            .Where(s => s.Count > 0)
            .ToList();
        if (used.Select(s => s.Family).Distinct().Count() <= 1)
            return;

        int first = used.Min(s => s.Index);
        var (line, col) = run.Locate(first);
        string detail = string.Join(" / ",
            used.Select(s => run.Text.Get("LintQuoteUsage", "{0} {1} 个", s.Name, s.Count)));
        run.Report.Hits.Add(run.Hit(line, col, 1, first, 1, first, 1, detail));
    }

    /// <summary>mixed-bracket-width 括号全半角混用：两侧都出现过才报，定位到全文最先出现的那个括号。</summary>
    private static void RunBracketWidthMix(AlgorithmRun run)
    {
        string scan = run.Matching;
        int half = HalfWidthBracket.Count(scan);
        int full = FullWidthBracket.Count(scan);
        if (half == 0 || full == 0)
            return;

        var m = AnyBracket.Match(scan);
        var (line, col) = run.Locate(m.Index);
        run.Report.Hits.Add(run.Hit(line, col, 1, m.Index, 1, m.Index, 1,
            run.RuleText("Detail", run.Rule.Detail, half, full)));
    }

    /// <summary>
    /// paragraph-length-uniform 段落长度过于均一：五段以上、变异系数低于 0.25 才报。
    /// 只出统计附注：段落长短参差是风格问题，不是逐条照着改的错。
    /// </summary>
    private static void RunParagraphLengthUniform(AlgorithmRun run)
    {
        var paragraphs = run.Paragraphs;
        if (paragraphs.Count < 5)
            return;

        var lens = paragraphs.Select(p => (double)p.Text.Length).ToList();
        double mean = lens.Average(), cv = StdDev(lens, mean) / mean;
        if (cv >= 0.25)
            return;

        run.Report.Notes.Add(new LintNote
        {
            Id = run.Rule.Id,
            Text = run.NoteText(cv.ToString("0.00"), paragraphs.Count),
        });
    }

    /// <summary>sentence-length-uniform 句长分布过于整齐：十句以上、变异系数低于 0.35 才报，只出统计附注。</summary>
    private static void RunSentenceLengthUniform(AlgorithmRun run)
    {
        var sentences = run.Segments.SentenceLengths
            .Where(length => length >= 6).ToList();
        if (sentences.Count < 10)
            return;

        var lens = sentences.Select(length => (double)length).ToList();
        double mean = lens.Average(), cv = StdDev(lens, mean) / mean;
        if (cv >= 0.35)
            return;

        run.Report.Notes.Add(new LintNote
        {
            Id = run.Rule.Id,
            Text = run.NoteText(cv.ToString("0.00"), sentences.Count),
        });
    }

    /// <summary>
    /// short-sentence-run 连续短句：一句十来字正常，连着四句以上就是节拍化——
    /// 每句都在表演「克制」，读起来像按模板在填。与 sentence-length-uniform 查的不是同一件事：
    /// 那条看句长是否过于整齐（方差小），这条看是否连着短。节拍已报过时不再重复报同一个现象。
    /// </summary>
    private static void RunShortSentenceRun(AlgorithmRun run)
    {
        if (run.TempoReported)
            return;

        var rawSentences = run.Segments.SentenceLengths.Where(length => length > 0).ToList();
        int streak = 0, maxRun = 0;
        foreach (int length in rawSentences)
        {
            streak = length <= ShortSentenceMaxChars ? streak + 1 : 0;
            if (streak > maxRun) maxRun = streak;
        }
        if (maxRun < ShortSentenceMinRun)
            return;

        run.Report.Notes.Add(new LintNote
        {
            Id = run.Rule.Id,
            Text = run.NoteText(maxRun, ShortSentenceMaxChars),
        });
    }

    /// <summary>repeated-paragraph-opening 段首两字高度重复：四段以上、同一开头出现三次才报。</summary>
    private static void RunRepeatedParagraphOpening(AlgorithmRun run)
    {
        var paragraphs = run.Paragraphs;
        if (paragraphs.Count < 4)
            return;

        var dup = paragraphs.Select(p => p.Text[..Math.Min(2, p.Text.Length)])
            .GroupBy(h => h).Where(g => g.Count() >= 3).ToList();
        if (dup.Count == 0)
            return;

        run.Report.Notes.Add(new LintNote
        {
            Id = run.Rule.Id,
            Text = run.NoteText(string.Join("、", dup.Select(g => $"「{g.Key}」×{g.Count()}"))),
        });
    }

    /// <summary>exclamation-density 叹号密度偏高：四百字以上、每千字超过三个叹号。</summary>
    private static void RunExclamationDensity(AlgorithmRun run)
    {
        string scan = run.Matching;
        int exclam = scan.Count(c => c == '！');
        if (scan.Length < 400 || exclam * 1000.0 / scan.Length <= 3)
            return;

        run.Report.Notes.Add(new LintNote
        {
            Id = run.Rule.Id,
            Text = run.NoteText(exclam, scan.Length),
        });
    }

    /// <summary>
    /// ordinal-skeleton 序数词骨架密度：段内凑够两处「首先/其次/再次/最后」。
    /// 小说档不查（规则表里这条只写文档与智能体回复）：模型写小说本来就不会排「首先……其次……」，
    /// 而「再次」「最后」在叙事里是日常用词，一段里凑够两次就会误报。
    /// </summary>
    private static void RunOrdinalSkeleton(AlgorithmRun run)
    {
        int ordinals = OrdinalSkeleton.Count(run.Matching);
        if (ordinals < 2)
            return;

        run.Report.Notes.Add(new LintNote
        {
            Id = run.Rule.Id,
            Text = run.NoteText(ordinals),
        });
    }

    /// <summary>explicit-numbering 显式序号标记：手写文本很少通篇硬编号，三处以上是排版痕迹。</summary>
    private static void RunExplicitNumbering(AlgorithmRun run)
    {
        int markers = ExplicitOrdinalMarker.Count(run.Matching);
        if (markers < 3)
            return;

        run.Report.Notes.Add(new LintNote
        {
            Id = run.Rule.Id,
            Text = run.NoteText(markers),
        });
    }

    /// <summary>短句硬连的门限：一段里凑够这么多分句、平均这么短，就算把话切碎了硬接。</summary>
    internal const int ChoppyClauseMinCount = 8;
    internal const int ChoppyClauseMaxMeanChars = 5;

    /// <summary>切分句的标点：句末、逗号、顿号、分号、冒号与破折号都算一处停顿。</summary>
    private static readonly char[] ClauseBreaks = { '。', '！', '？', '；', '，', '、', '：', '\n', '—', '\r' };

    /// <summary>
    /// choppy-clauses 短句硬连：该断的地方用分号、破折号硬接，一长句里七八个短促分句，
    /// 主语宾语全给省掉，读者得自己把话补全。
    ///
    /// 判的是句子形状，不是用词：分句数与平均句长两个都要够门槛。单看分号或单看顿号都会误伤，
    /// 正常的中文并列与列举满篇都是顿号；号列表（首先、其次、一、二、三）也不算，那是结构，不是硬接。
    /// 小说档不跑（规则表里这条只写文档与智能体回复）：短句在叙事里是正常手法。
    /// </summary>
    private static void RunChoppyClauses(AlgorithmRun run)
    {
        foreach (var (offset, body) in run.Paragraphs)
        {
            if (!body.Contains('；') && !body.Contains("——")) continue;
            if (OrdinalSkeleton.IsMatch(body)) continue;
            var clauses = body.Split(ClauseBreaks, StringSplitOptions.RemoveEmptyEntries)
                .Select(c => c.Trim()).Where(c => c.Length > 0).ToList();
            if (clauses.Count < ChoppyClauseMinCount) continue;
            double mean = clauses.Average(c => (double)c.Length);
            if (mean > ChoppyClauseMaxMeanChars) continue;

            var (line, col) = run.Locate(offset);
            string head = body.Trim();
            if (head.Length > 30) head = head[..30];
            run.Report.Hits.Add(run.Hit(line, col, head.Length, offset, head.Length, offset, head.Length,
                run.RuleText("Detail", run.Rule.Detail, clauses.Count, Math.Round(mean, 1))));
        }
    }

    /// <summary>复读串阈值：同一单元同段连写这么多遍算复读。</summary>
    internal const int RepeatedTokenMinRun = 4;

    /// <summary>单元长度的上下限：单字连写不算（那多为语气或强调，且重复标点另有规则），最长四个字。</summary>
    internal const int RepeatedTokenMinUnit = 2;
    internal const int RepeatedTokenMaxUnit = 4;

    /// <summary>语气字与拟声字：连着写是说话的样子（「哈哈哈哈」），不是没词了。</summary>
    private const string InterjectionChars = "哈呵嘿嘻呜哇呀吧啦嗯哦喔欸咦唔噗额呃嗗嘎啊嘤咪嘘唶";

    /// <summary>
    /// repeated-token-run 复读串：同一个单元在同一段里连写四遍以上。
    /// 与 repeated-verbatim 分工：那条比完全相同的句子与段落，这条只看串里的重复，不要求整句相同。
    /// 报整串一处而不是逐次报——「极其极其极其…」四十几条命中说的是一件事，
    /// 逐次报只把报告淹掉，读者还得自己数。串里的单点命中因此让给本条。
    /// </summary>
    private static void RunRepeatedTokenRun(AlgorithmRun run)
    {
        var runs = new List<(int Start, int End)>();
        foreach (var (offset, body) in run.Paragraphs)
        {
            int i = 0;
            while (i < body.Length)
            {
                var (unit, count) = FindTokenRun(body, i);
                if (unit is null)
                {
                    i++;
                    continue;
                }

                int start = offset + i;
                int length = unit.Length * count;
                var (line, col) = run.Locate(start);
                // 归一只为匹配：复读串报给读者的是原文那几个字
                string shown = run.Source.Slice(start, unit.Length);
                run.Report.Hits.Add(run.Hit(line, col, length, start, unit.Length, start, Math.Min(length, 40),
                    run.RuleText("Detail", run.Rule.Detail, shown, count)));
                runs.Add((start, start + length));
                i += length;
            }
        }

        if (runs.Count == 0) return;

        // 整串已经报过一处，串里的其他命中不再各报一遍：副词同段堆叠在复读串里是同一个现象。
        // 比的是本条规则自己的 Id，不是写死的字符串：算法与规则表里那条 Id 一一对应。
        var spans = runs.Select(r =>
        {
            var (l1, c1) = run.Locate(r.Start);
            var (l2, c2) = run.Locate(r.End - 1);
            return (L1: l1, C1: c1, L2: l2, C2: c2);
        }).ToList();
        run.Report.Hits.RemoveAll(h =>
            !string.Equals(h.Id, run.Rule.Id, StringComparison.OrdinalIgnoreCase)
            && spans.Any(s => InsideSpan(s, h.Line, h.Col)));
    }

    private static bool InsideSpan((int L1, int C1, int L2, int C2) span, int line, int col)
    {
        if (line < span.L1 || line > span.L2) return false;
        if (line == span.L1 && line == span.L2) return col >= span.C1 && col <= span.C2;
        if (line == span.L1) return col >= span.C1;
        if (line == span.L2) return col <= span.C2;
        return true;
    }

    /// <summary>
    /// 从 from 起的复读串：单元长度从短到长试，最短的那个凑够 RepeatedTokenMinRun 遍就是这串的周期。
    /// 从短往长试而不是反过来：先撞上的那个才是「同一个词」（「极其极其极其」是「极其」在复读，
    /// 不是「极其极其」在复读），报出来的 Match 也才是读者认得的那个词。
    /// 带空白的、不含任何字母汉字的（标点、emoji）、全由语气字构成的单元都不算。
    /// </summary>
    private static (string? Unit, int Count) FindTokenRun(string body, int from)
    {
        // 每个字符位置都要问一次「这里起头的是不是复读串」，所以这里一个都不许分配：
        // 切子串、LINQ 枚举、Distinct 的散列表乘上百万个位置就是几个 G 的垃圾。
        // 候选单元按 span 比，只有真要报出来的时候才把它变成字符串。
        var text = body.AsSpan();
        for (int len = RepeatedTokenMinUnit; len <= RepeatedTokenMaxUnit; len++)
        {
            if (from + len * RepeatedTokenMinRun > text.Length) continue;
            var unit = text.Slice(from, len);
            if (ContainsWhiteSpace(unit)) continue;
            if (!ContainsLetter(unit)) continue;
            // 同一个字连写（「字字字字」）是强调或占位，不是词在复读。
            if (AllSameChar(unit)) continue;
            if (AllInterjection(unit)) continue;

            int count = 1;
            while (from + len * (count + 1) <= text.Length
                   && text.Slice(from + len * count, len).SequenceEqual(unit))
                count++;
            if (count >= RepeatedTokenMinRun) return (unit.ToString(), count);
        }
        return (null, 0);
    }

    private static bool ContainsWhiteSpace(ReadOnlySpan<char> unit)
    {
        foreach (char c in unit)
            if (char.IsWhiteSpace(c))
                return true;
        return false;
    }

    private static bool ContainsLetter(ReadOnlySpan<char> unit)
    {
        foreach (char c in unit)
            if (char.IsLetter(c))
                return true;
        return false;
    }

    private static bool AllSameChar(ReadOnlySpan<char> unit)
    {
        foreach (char c in unit)
            if (c != unit[0])
                return false;
        return true;
    }

    private static bool AllInterjection(ReadOnlySpan<char> unit)
    {
        foreach (char c in unit)
            if (!InterjectionChars.Contains(c))
                return false;
        return true;
    }

    /// <summary>
    /// repeated-verbatim 逐字重复：完全相同的段落或句子再次出现时报出来。
    /// 只比逐字相同（去首尾空白后完全相等），近似复述不在此列——那要相似度阈值，
    /// 误伤对象是排比、副歌式的刻意反复。段重复时不再逐句报；句子级的多处重复
    /// 若都指向同一处原文，并成一条：一次贴回一大段，逐句各报一处只会把报告淹掉。
    /// </summary>
    private static void RunRepeatedVerbatim(AlgorithmRun run)
    {
        var paragraphs = run.Paragraphs;
        var firstSeen = new Dictionary<string, (int Line, int Col)>(StringComparer.Ordinal);
        var repeatedRanges = new List<(int Start, int End)>();

        void Report(int offset, string body, string? detail = null)
        {
            var (line, col) = run.Locate(offset);
            var (firstLine, firstCol) = firstSeen[body];
            run.Report.Hits.Add(run.Hit(line, col, body.Length, offset, body.Length, offset, body.Length,
                detail ?? run.RuleText("Detail", run.Rule.Detail, firstLine, firstCol)));
        }

        foreach (var (offset, body) in paragraphs)
        {
            if (body.Length < MinRepeatParagraphChars) continue;
            if (firstSeen.TryAdd(body, run.Locate(offset)))
                continue;
            Report(offset, body);
            repeatedRanges.Add((offset, offset + body.Length));
        }

        // 句子级的首现表单独一份：段与句的长度不同，同一段的两份不会互相冒充。
        // 首现位置要连偏移一起记：判「连着的几句是不是同一段的两份」，靠的是两边的间隔对得上。
        // 键是原文里的一段而不是切下来的字符串：五十兆的稿子几十万句，每句存一份是几十兆。
        var firstAt = new Dictionary<ReadOnlyMemory<char>, int>(SpanTextComparer.Instance);
        var repeats = new List<(int Offset, int Length, int Source)>();
        string matching = run.Matching;
        foreach (var (offset, length) in SentenceRanges(matching))
        {
            if (length < MinRepeatSentenceChars) continue;
            // 段里已经报过的句子不再逐句报。逐句扫一遍已报的重复段是 O(句数 × 重复段数)，
            // 两份都随篇幅涨；段区间按偏移升序且互不重叠，二分一次就够（见 InsideRange）。
            if (InsideRange(repeatedRanges, offset)) continue;
            if (firstAt.TryAdd(matching.AsMemory(offset, length), offset)) continue;
            repeats.Add((offset, length, firstAt[matching.AsMemory(offset, length)]));
        }

        // 把连着的重复并成一段：贴回一长段时，逐句各报一处只会把报告淹掉
        int k = 0;
        while (k < repeats.Count)
        {
            int start = repeats[k].Offset, sourceStart = repeats[k].Source;
            int end = start + repeats[k].Length, sourceEnd = sourceStart + repeats[k].Length;
            int count = 1;
            while (k + 1 < repeats.Count)
            {
                var next = repeats[k + 1];
                int gap = next.Offset - end;
                int sourceGap = next.Source - sourceEnd;
                if (gap > RepeatJoinGap || sourceGap != gap) break;
                end = next.Offset + next.Length;
                sourceEnd = next.Source + next.Length;
                count++;
                k++;
            }

            var (line, col) = run.Locate(start);
            var (sourceLine, sourceCol) = run.Locate(sourceStart);
            // 「并成一段」的说明是同一件事的第二种写法，规则表里只有一个说明字段，
            // 所以它的兜底原文留在这里，键仍是 LintRule_repeated-verbatim_GroupDetail。
            run.Report.Hits.Add(run.Hit(line, col, end - start, start, end - start, start, Math.Min(end - start, 40),
                count > 1
                    ? run.Text.Get("LintRule_repeated-verbatim_GroupDetail",
                        "从这一句起共 {0} 句与第 {1} 行第 {2} 列起逐字相同", count, sourceLine, sourceCol)
                    : run.RuleText("Detail", run.Rule.Detail, sourceLine, sourceCol)));
            k++;
        }
    }

    /// <summary>并段时容许的间隔：句与句之间只该有标点或换行，隔得再远就不算同一段被贴回。</summary>
    private const int RepeatJoinGap = 2;

    /// <summary>按句末标点切句，给出每句去首尾空白后的起止：只记位置，不为每句切一份字符串。</summary>
    private static IEnumerable<(int Offset, int Length)> SentenceRanges(string text)
    {
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (!IsSentenceEnd(text[i])) continue;
            foreach (var block in Trimmed(text, start, i + 1)) yield return block;
            start = i + 1;
        }
        foreach (var block in Trimmed(text, start, text.Length)) yield return block;
    }

    private static IEnumerable<(int Offset, int Length)> Trimmed(string text, int from, int to)
    {
        if (to <= from) yield break;
        int start = from, end = to;
        while (start < end && char.IsWhiteSpace(text[start])) start++;
        while (end > start && char.IsWhiteSpace(text[end - 1])) end--;
        if (end > start) yield return (start, end - start);
    }

    /// <summary>
    /// 按原文片段比内容：键是原文里的一段，取哈希与比较都直接看那几个字，不另存一份字符串。
    /// 逐字相同才叫同句，近似复述不在此列。
    /// </summary>
    private sealed class SpanTextComparer : IEqualityComparer<ReadOnlyMemory<char>>
    {
        public static readonly SpanTextComparer Instance = new();

        public bool Equals(ReadOnlyMemory<char> x, ReadOnlyMemory<char> y) =>
            x.Span.SequenceEqual(y.Span);

        public int GetHashCode(ReadOnlyMemory<char> value) => string.GetHashCode(value.Span);
    }

    private static double StdDev(List<double> values, double mean)
    {
        double sum = values.Sum(v => (v - mean) * (v - mean));
        return Math.Sqrt(sum / values.Count);
    }

    /// <summary>
    /// 一次扫描里按口径切好的段与句子。切法只取决于文本与段落口径，与规则无关，
    /// 所以整次扫描共用一份：几十条段落作用域规则各切一遍的话，长稿上光是这批副本就几十兆。
    ///
    /// 用到才切：只跑文档作用域规则的扫描一份都不切。
    /// </summary>
    private sealed class Segments
    {
        private readonly string _text;
        private readonly string _mode;
        private List<(int Offset, string Text)>? _paragraphs;

        public Segments(string text, string mode)
        {
            _text = text;
            _mode = mode;
        }

        /// <summary>按段落口径切好的段，与 <see cref="Paragraphs"/> 同一份结果。</summary>
        public List<(int Offset, string Text)> Paragraphs => _paragraphs ??= Paragraphs(_text, _mode);

        /// <summary>
        /// 按句末标点切好的每一句去首尾空白后的长度，末句空着时也占一个 0。
        /// 用到句子的两条算法只看长度（句长均齐度、连续短句），这里就只算长度：
        /// 五十兆的稿子切出几十万句，每句存一份字符串是几十兆的常驻，存一个整数是两兆。
        /// 切点就是每个句末标点之后，句末标点留在它那句的末尾。
        /// </summary>
        public List<int> SentenceLengths => _sentenceLengths ??= SentenceLengths(_text);

        private List<int>? _sentenceLengths;
    }

    /// <summary>一句一段地切，只留去空白后的长度。切点在每个句末标点之后，末尾那截空着时也算一句。</summary>
    private static List<int> SentenceLengths(string text)
    {
        var result = new List<int>();
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (!IsSentenceEnd(text[i])) continue;
            result.Add(TrimmedLength(text, start, i + 1));
            start = i + 1;
        }
        result.Add(TrimmedLength(text, start, text.Length));
        return result;
    }

    /// <summary>
    /// 按口径切段。Offset 为去空白后段首在全文中的绝对下标；
    /// Text 是原文的连续切片，保证 <c>Offset + Match.Index</c> 的定位换算成立。
    /// </summary>
    internal static List<(int Offset, string Text)> Paragraphs(string text, string mode)
        => mode == "markdown" ? MarkdownParagraphs(text) : LineParagraphs(text);

    private static List<(int Offset, string Text)> LineParagraphs(string text)
    {
        var result = new List<(int, string)>();
        int offset = 0;
        int lineStart = 0;
        // 行首行尾逐个往下走，只为定下段落的起止：每一行都先切成字符串再丢掉，
        // 五十兆的稿子光这一层就是几十万个副本
        for (int i = 0; i <= text.Length; i++)
        {
            if (i < text.Length && text[i] != '\n') continue;
            int end = LineEnd(text, lineStart, i);
            int from = lineStart, to = end;
            while (from < to && char.IsWhiteSpace(text[from])) from++;
            while (to > from && char.IsWhiteSpace(text[to - 1])) to--;
            if (to > from)
                result.Add((offset + (from - lineStart), text[from..to]));
            offset += i - lineStart + 1;
            lineStart = i + 1;
        }
        return result;
    }

    private static List<(int Offset, string Text)> MarkdownParagraphs(string text)
    {
        // 行的起止只存下标（行尾不含 \r）：不为每一行留一份字符串，段落才是要留下的东西
        var lineStart = new List<int> { 0 };
        var lineEnd = new List<int>();
        for (int pos = 0; pos < text.Length; pos++)
        {
            if (text[pos] != '\n') continue;
            lineEnd.Add(LineEnd(text, lineStart[^1], pos));
            lineStart.Add(pos + 1);
        }
        lineEnd.Add(LineEnd(text, lineStart[^1], text.Length));

        var result = new List<(int, string)>();
        int i = 0;
        while (i < lineStart.Count)
        {
            if (IsBlankLine(text, lineStart[i], lineEnd[i])) { i++; continue; }
            if (IsBlockLine(text[lineStart[i]..lineEnd[i]]))
            {
                AddBlock(result, text, lineStart[i], lineEnd[i]);
                i++;
                continue;
            }
            int end = i;
            while (end + 1 < lineStart.Count
                   && !IsBlankLine(text, lineStart[end + 1], lineEnd[end + 1])
                   && !IsBlockLine(text[lineStart[end + 1]..lineEnd[end + 1]]))
                end++;
            AddBlock(result, text, lineStart[i], lineEnd[end]);
            i = end + 1;
        }
        return result;
    }

    /// <summary>行尾：不含行尾的换行，也不含行尾的回车。</summary>
    private static int LineEnd(string text, int start, int end)
    {
        while (end > start && text[end - 1] == '\r') end--;
        return end;
    }

    /// <summary>空行：去掉首尾空白后什么都不剩。</summary>
    private static bool IsBlankLine(string text, int start, int end)
    {
        while (start < end && char.IsWhiteSpace(text[start])) start++;
        while (end > start && char.IsWhiteSpace(text[end - 1])) end--;
        return start == end;
    }

    /// <summary>列表项、引用行与编号行各自成段：并进散文段会让 paragraph-ordinals 的「段内序数词」跨条目误报。</summary>
    private static bool IsBlockLine(string raw) =>
        MarkdownLines.IsListItem(raw) || MarkdownLines.IsQuote(raw) || MarkdownLines.IsNumberedItem(raw);

    private static void AddBlock(List<(int Offset, string Text)> result, string text, int start, int end)
    {
        // 先定下真正要留的那截，只切一次：切完再 Trim 就是一份多余的副本
        while (start < end && char.IsWhiteSpace(text[start])) start++;
        while (end > start && char.IsWhiteSpace(text[end - 1])) end--;
        if (end > start)
            result.Add((start, text[start..end]));
    }

    private LintHit Hit(LintRule rule, LineStarts lineStarts, ScanText source, int index,
        int length, string? hint)
    {
        var (line, col) = lineStarts.Locate(index);
        var hit = new LintHit
        {
            Id = rule.Id, Group = rule.Group, Severity = rule.Severity,
            Title = RuleText(_text, rule, LintRule.TextFieldTitle, rule.Title),
            Line = line, Col = col, Length = length,
            Detail = RuleText(_text, rule, LintRule.TextFieldDetail, rule.Detail), Hint = hint,
            SuggestScheme = rule.SuggestScheme is null
                ? null
                : RuleText(_text, rule, LintRule.TextFieldSuggest, rule.SuggestScheme),
        };
        // 位置与长度对归一串和原文通用：取值时切出来的就是读者自己交来的那几个字
        hit.BindTo(source.Original, index, length, index, length);
        return hit;
    }

    /// <summary>
    /// sentence-tempo 节拍化短句：按窗口算平均句长。
    ///
    /// 与 short-sentence-run 分工：后者只看「连着几句短」，一条引言、一串动作描写也能凑出来；
    /// 本规则看整个窗口的平均句长，判的是「全段没有一句长句」这种整体形状。
    /// 与 choppy-clauses 也不同：后者要有分号或破折号硬接，本规则用句号断开的短句一样报。
    /// 只出统计附注，不进命中：真人写的对话体与意识流小说（时空轮回、喧哗与骚动等九本）
    /// 在真人库四千七百万字里出现 65 个这样的窗口，最密的一本 34 处——短句密度分不开人与模型，
    /// 它只是「这一段全在短句」的观察，不该算作判据。
    /// </summary>
    /// <returns>报出的窗口数。</returns>
    private static void RunSentenceTempo(AlgorithmRun run)
    {
        string text = run.Matching;
        if (text.Length < TempoMinTextChars) return;

        // 句末标点之后就是下一句的起点。切点全篇算一次，每个窗口只在这张表上取值：
        // 逐窗重切是同一段文字被相邻两窗各切一遍（窗口八百字、每挪四百字），长稿上等于
        // 把全文多扫一遍，每句还要切出子串再 Trim。句子的长短、条数从切点直接算出来，不落字符串。
        var cuts = new List<int>();
        for (int i = 0; i < text.Length; i++)
            if (text[i] is '。' or '！' or '？' or '…') cuts.Add(i + 1);
        int cutCursor = 0;   // 窗口按步长往后挪，切点也只会往后

        int reported = 0;
        int windowChars = Math.Min(TempoWindowChars, text.Length);
        // 上一窗也是节拍就不重复报：相邻窗重叠一半，同一段短句会在几窗里连续成立，跟着步长报就是刷屏
        bool previousWasTempo = false;
        for (int start = 0; start + windowChars <= text.Length; start += TempoWindowStep)
        {
            int end = start + windowChars;
            while (cutCursor < cuts.Count && cuts[cutCursor] <= start) cutCursor++;
            // 窗内的句子：窗口起点到窗里第一个切点、切点到下一个切点……最后一句的末尾就是窗口末尾
            int sentenceCount = 0, sentenceChars = 0, longestSentence = 0;
            int from = start;
            for (int k = cutCursor; ; k++)
            {
                int to = k < cuts.Count && cuts[k] < end ? cuts[k] : end;
                int length = TrimmedLength(text, from, to);
                if (length >= TempoMinSentenceChars)
                {
                    sentenceCount++;
                    sentenceChars += length;
                    if (length > longestSentence) longestSentence = length;
                }
                if (to >= end) break;
                from = to;
            }
            double mean = sentenceCount > 0 ? (double)sentenceChars / sentenceCount : 0;
            bool shortWindow = sentenceCount >= TempoMinSentences
                && mean < TempoMaxMeanChars
                && longestSentence <= TempoMaxSentenceChars;
            if (!shortWindow)
            {
                previousWasTempo = false;
                continue;
            }

            if (!previousWasTempo)
            {
                run.Report.Notes.Add(new LintNote
                {
                    Id = run.Rule.Id,
                    Text = run.NoteText(
                        run.Locate(start).Line + 1, sentenceCount,
                        Math.Round(mean, 1),
                        longestSentence),
                });
                reported++;
            }

            previousWasTempo = true;
        }

        // 报过的窗口要传到后面：连续短句那条靠它避免把同一个现象再报一遍
        run.TempoReported = reported > 0;
    }

    /// <summary>掐掉首尾空白之后的长度。节拍窗口里每句都要问一次，只为拿长度不必切出子串。</summary>
    private static int TrimmedLength(string text, int from, int to)
    {
        while (from < to && char.IsWhiteSpace(text[from])) from++;
        while (to > from && char.IsWhiteSpace(text[to - 1])) to--;
        return to - from;
    }

    /// <summary>一句里叠几个比喻才算堆：三个。</summary>
    internal const int SimilePileMinSimiles = 3;
    /// <summary>只判长句：不足六十字的短句里连用比喻，是修辞不是堆砌。</summary>
    internal const int SimilePileMinSentenceChars = 60;
    /// <summary>
    /// 全篇密度门限（每千字处数）。实测真人库 86 本、4696 万字，最密的一本（到灯塔去）每千字 0.08 处；
    /// 一句两百字连缀八个比喻的稿子是 4.69 处。取 1.0：高出人类上限十二倍，仍留四倍余量给这篇稿子。
    /// </summary>
    internal const double SimilePileMinDensity = 1.0;

    private static readonly Regex SentenceSpan = RegexGuard.Create(@"[^。！？…\n]+");
    private static readonly Regex SimileMarker = RegexGuard.Create("好像|像|如同|仿佛|犹如|好似|宛若|宛如|好比");

    /// <summary>
    /// fiction-simile-pile 一句里叠着比喻：一句话六十字以上、串了三个以比况词引出的喻体。
    ///
    /// 与 fiction-dead-metaphor 分工：那条认的是哪个喻体被用旧了（石子、手术刀），本规则认的是形状——
    /// 一句里堆几个，用的是新喻体也照报。与 fiction-metaphor-lexicon 也不同：那条数全篇的器物词，
    /// 本规则只看一句之内，且不限词表。
    ///
    /// 只在小说档判（规则表里这条写的就是小说）：编程文档与智能体回复里，比喻堆叠不是这套工具要管的事。
    /// 密度不到门限就整条不报——一句长句带几个比喻，真人写作里也有，凑够密度才是堆砌。
    /// </summary>
    private static void RunSimilePile(AlgorithmRun run)
    {
        string scan = run.Matching;
        var candidates = new List<(int Index, int Length, int Similes)>();
        foreach (var m in SentenceSpan.EnumerateMatches(scan))
        {
            if (TrimmedLength(scan, m.Index, m.Index + m.Length) < SimilePileMinSentenceChars) continue;
            int similes = SimileMarker.Count(scan.AsSpan(m.Index, m.Length));
            if (similes < SimilePileMinSimiles) continue;
            candidates.Add((m.Index, m.Length, similes));
        }
        if (candidates.Count == 0) return;
        if (candidates.Count * 1000.0 / Math.Max(scan.Length, 1) < SimilePileMinDensity) return;

        foreach (var (index, length, similes) in candidates)
        {
            var (line, col) = run.Locate(index);
            // 句首可能带空白，切过空白之后的下标才是这个词在原文里的起点
            int headIndex = index;
            while (headIndex < index + length && char.IsWhiteSpace(scan[headIndex])) headIndex++;
            int tail = index + length;
            while (tail > headIndex && char.IsWhiteSpace(scan[tail - 1])) tail--;
            int headLength = Math.Min(tail - headIndex, 30);
            run.Report.Hits.Add(run.Hit(line, col, headLength, headIndex, headLength, index, headLength,
                run.RuleText("Detail", run.Rule.Detail, tail - headIndex, similes)));
        }
    }

    /// <summary>
    /// 术语与意象堆砌（terminology-pile）：褶皱、熵增、递归、拓扑、量子纠缠、镜像、琥珀这一批词。
    /// 它们在散文里替掉了具体的观察：每句都在把传统物件翻译成一个科技名词。
    /// </summary>
    private static readonly Regex TerminologyTerm = RegexGuard.Create(
        "褶皱|熵增|递归|拓扑|量子纠缠|晶格|超新星|镜像|琥珀|星轨|坍缩|希格斯|麦克斯韦妖|生物硬盘|暗涌|坐标定位|解码的密钥|文明的辉光|十四行诗|降维|密码学|二级编码|建构|架构|数字孪生|全息|区块链|物联网|无人机|二维码|算法|大数据");

    /// <summary>引号内的文字。人在念这批词给人看，不是在用：匹配前先把这些段落挖掉。</summary>
    private static readonly Regex QuotedSpan = RegexGuard.Create("[「“][^」”]*[」”]");

    /// <summary>
    /// 门限（每千字处数）。实测真人库 86 本、4696 万字，最密的一本每千字 0.054 处；
    /// 全篇堆这批词的稿子是 16 到 43 处。取 5：高出真人上限九十倍，
    /// 又高过「把词念一遍」那类人写稿的 3.74 处。
    /// </summary>
    internal const double TerminologyPileMinDensity = 5;

    /// <summary>
    /// terminology-pile 术语与意象堆砌：挖掉引号内的文字之后，数这批词还剩几处。
    ///
    /// 必须先挖引号，因为人写的批评稿会把被批评的词原样念一遍（降维碾压、密码学、二级编码），
    /// 按字面数，这类人写稿的密度会高过真正的病灶。挖引号管住了这种；另一种是没加引号的贯口，
    /// 交给门限——实测它落在每千字 3.74 处，门限是 5。
    ///
    /// 编程文档与智能体回复两档不判（规则表里这条写的就是小说）：在那两档里「算法」「架构」是正常用词。
    /// </summary>
    private static void RunTerminologyPile(AlgorithmRun run)
    {
        string scan = run.Matching;
        var quoted = new List<(int Start, int End)>();
        foreach (var q in QuotedSpan.EnumerateMatches(scan))
            quoted.Add((q.Index, q.Index + q.Length));
        bool InQuotes(int index) => InsideRange(quoted, index);

        var hits = new List<(int Index, int Length)>();
        foreach (var m in TerminologyTerm.EnumerateMatches(scan))
        {
            if (InQuotes(m.Index)) continue;
            hits.Add((m.Index, m.Length));
        }
        if (hits.Count == 0) return;
        double density = hits.Count * 1000.0 / Math.Max(scan.Length, 1);
        if (density < TerminologyPileMinDensity) return;

        double shown = Math.Round(density, 1);
        foreach (var (index, length) in hits)
        {
            var (line, col) = run.Locate(index);
            run.Report.Hits.Add(run.Hit(line, col, length, index, length, index, length,
                run.RuleText("Detail", run.Rule.Detail, shown)));
        }
    }

    /// <summary>
    /// 一份待扫文本的两面：原文交回读者，归一字面交给规则。
    /// 归一按词换字，但保证与原文等长、逐位对齐，命中位置因此不必换算：
    /// 匹配得到的 (index, length) 直接在原文里切，拿到的是用户自己交来的那几个字。
    ///
    /// 博文结构在归一份里先遮成空格：匹配不到词，整行被遮的地方当空行，篇幅统计因此只算正文。
    /// 原文原样留着，Match 与 Snippet 取到的仍是用户写的那些字。
    ///
    /// 简体稿的归一面就直接是原文：判断见 <see cref="TraditionalChinese.NeedsConversion"/>。
    /// </summary>
    private readonly struct ScanText
    {
        public ScanText(string text)
        {
            Original = text;
            var mask = BlogMarkdownMask.Create(text);
            string matching = mask.Apply(text);
            // 开头没有繁体写法就不转：词库不加载，也不再留一份与原文等长的副本。
            // 不是博文时 Apply 原样交回入参，此时 Matching 与 Original 是同一份串。
            Matching = TraditionalChinese.NeedsConversion(text) ? ToSimplified(matching) : matching;
            Chars = text.Length - mask.MaskedChars;
            IsBlogPost = mask.IsBlogPost;
        }

        /// <summary>一片多长。与转换器内部分段的大小同一个量级，一片落在一个线程手上，缓冲都待在小对象堆里。</summary>
        private const int SimplifyPieceChars = 32_000;

        /// <summary>
        /// 繁转简按行边界切片、并行转换。整篇一次交出去时转换器按 32000 字一段把活分给各线程，
        /// 五十兆的稿子每个线程就分到百万把字，那几个缓冲落在大对象堆上，内存峰值被抬高一截。
        /// 转换是逐行做的，片与片之间没有上下文，按行边界切开再拼回去与整篇转换一字不差。
        /// </summary>
        private static string ToSimplified(string text)
        {
            if (text.Length <= SimplifyPieceChars) return TraditionalChinese.ToSimplified(text);

            var bounds = new List<int> { 0 };
            while (bounds[^1] < text.Length)
            {
                int end = Math.Min(text.Length, bounds[^1] + SimplifyPieceChars);
                if (end < text.Length)
                {
                    int newline = text.IndexOf('\n', end);
                    end = newline < 0 ? text.Length : newline + 1;
                }
                bounds.Add(end);
            }

            var pieces = new string[bounds.Count - 1];
            Parallel.For(0, pieces.Length, i =>
                pieces[i] = TraditionalChinese.ToSimplified(text[bounds[i]..bounds[i + 1]]));
            return string.Concat(pieces);
        }

        /// <summary>用户原文。行列、Match、Snippet 都从它取。</summary>
        public string Original { get; }

        /// <summary>
        /// 繁简归一后的字面，只给匹配与统计看。博文结构在这里是空格。
        /// 简体稿不必归一，这里就是原文本身（或博文遮罩后的那一份）。
        /// </summary>
        public string Matching { get; }

        /// <summary>正文字数：原文长度扣掉博文结构那部分。密度门限与报告里的字数都按它算。</summary>
        public int Chars { get; }

        /// <summary>这份稿子按博文处理：头部字段里有 title 与 date，或者有 <c>::</c> 指令行。</summary>
        public bool IsBlogPost { get; }

        /// <summary>原文的一段连续切片：Match 与说明文字里要引用用户字面的地方用它。</summary>
        public string Slice(int index, int length) => Original.Substring(index, length);

        /// <summary>原文里命中前后各 8 字的上下文，取窗口的规则见 static Snippet。</summary>
        public string Snippet(int index, int length, int pad = 8) =>
            AiToneLintService.Snippet(Original, index, length, pad);
    }

    /// <summary>
    /// 行首下标表。Position 每次命中都从头重扫是 O(长度×命中数)；
    /// Scan 建一次、所有命中复用。
    /// </summary>
    internal sealed class LineStarts
    {
        private readonly List<int> _starts = new() { 0 };

        public LineStarts(string text)
        {
            for (int i = 0; i < text.Length; i++)
                if (text[i] == '\n') _starts.Add(i + 1);
        }

        public (int Line, int Col) Locate(int index)
        {
            int lo = 0, hi = _starts.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (_starts[mid] <= index) lo = mid;
                else hi = mid - 1;
            }
            return (lo + 1, index - _starts[lo] + 1);
        }
    }

    /// <summary>
    /// 前后各 8 字的上下文，保留原文含换行：Snippet 是原文的连续切片，跨行命中时也含 Match 原文。
    /// 窗口边界不劈开代理对——emoji-and-symbols 会命中 emoji，命中点附近的窗口若不外扩，切片会产出落单代理项。
    /// </summary>
    internal static string Snippet(string text, int index, int length, int pad = 8)
    {
        SnippetWindow(text, index, length, pad, out int lo, out int hi);
        var s = text[lo..hi];
        if (lo > 0) s = "…" + s;
        if (hi < text.Length) s += "…";
        return s;
    }

    /// <summary>
    /// 片段窗口的两端：前后各 pad 字，边界落在低代理上就往回退一格，落在高代理前就往前进一格。
    /// 切字符串与直接写出两条路都从这里取窗口，两边算出的片段因此不会差一个字。
    /// </summary>
    internal static void SnippetWindow(string text, int index, int length, int pad, out int lo, out int hi)
    {
        lo = Math.Max(0, index - pad);
        hi = Math.Min(text.Length, index + length + pad);
        if (lo > 0 && char.IsLowSurrogate(text[lo])) lo--;                  // 左边界落在低代理上：回退一格
        if (hi < text.Length && char.IsHighSurrogate(text[hi - 1])) hi++;   // 右边界停在高代理后：前进一格
    }

    private sealed class CompiledRule
    {
        public required LintRule Rule { get; init; }
        public required List<(Regex Regex, string? Hint)> Patterns { get; init; }

        /// <summary>折算过的密度门限：旧写法的规则在这里变成 document 基准，判定处只认一个口。</summary>
        public LintDensity? Density { get; init; }

        public static CompiledRule Create(LintRule rule)
        {
            var density = rule.EffectiveDensity;
            // 尚未实现的基准在编译时就报：等扫到命中才报，会被读成「这份稿子没问题」，
            // 而真相是这条门限根本没算过
            if (density is not null
                && density.Basis is LintDensity.BasisParagraph or LintDensity.BasisWindow)
                throw new NotImplementedException(
                    $"规则 {rule.Id} 的密度基准 {density.Basis} 尚未实现：先用 document 或 delta");

            // 词面在前、正则在后的顺序就是 Hints 的下标顺序：
            // 改词面的人写在前面的几条建议，与规则文件里两个列表的书写次序一致。
            var sources = (rule.LiteralPatterns ?? new List<string>())
                .Select(literal => (Pattern: literal, Escaped: true))
                .Concat(rule.Patterns.Select(pattern => (Pattern: pattern, Escaped: rule.Kind == LintRule.KindLiteral)));
            return new()
            {
                Rule = rule,
                Density = density,
                Patterns = sources.Select((source, i) =>
                {
                    // 字面串与 literal 规则都经 Regex.Escape 走同一条匹配路径；
                    // 只有 regex 规则的正则原文才交给引擎解释
                    var regex = source.Escaped
                        ? RegexGuard.Create(Regex.Escape(source.Pattern))
                        : RegexGuard.Create(source.Pattern);
                    string? hint = rule.Hints is not null && i < rule.Hints.Count ? rule.Hints[i] : null;
                    return (regex, hint);
                }).ToList(),
            };
        }
    }

    /// <summary>
    /// 一条算法规则的执行上下文：算法本体只从这里拿自己那条规则、待扫文本、报位入口与文案出处。
    ///
    /// 同一次扫描里的算法共用一份：按口径切好的段只切一次，「节拍已报过」也要在算法之间传下去。
    /// 分组、严重程度、标题、说明、建议方案一律从规则表取，算法里不写第二份。
    /// </summary>
    private sealed class AlgorithmRun
    {
        /// <summary>
        /// 当前正在跑的那条规则：分派前先赋值，再调用算法。
        /// 上下文一次扫描只建一份（切好的段与「节拍已报过」要在算法之间传下去），
        /// 所以这个字段随分派换，建上下文的那一刻还没有规则可给。
        /// </summary>
        public LintRule Rule { get; set; } = null!;

        public required ScanText Source { get; init; }

        /// <summary>段落口径：line 或 markdown。</summary>
        public required string Mode { get; init; }

        public required LintReport Report { get; init; }

        public required LineStarts LineStarts { get; init; }

        /// <summary>报告文案出处。界面换了语言，算法报出的说明跟着换。</summary>
        public required ILintText Text { get; init; }

        /// <summary>归一字面：匹配与统计看它；报给读者的 Match 与 Snippet 从原文切。</summary>
        public string Matching => Source.Matching;

        /// <summary>节拍是否已经报过：short-sentence-run 靠它避免把同一个现象报两遍。</summary>
        public bool TempoReported { get; set; }

        /// <summary>同一次扫描切好的段与句子：文档作用域规则与算法共用一份，全篇只切一次。</summary>
        public required Segments Segments { get; init; }

        /// <summary>按口径切好的段。</summary>
        public List<(int Offset, string Text)> Paragraphs => Segments.Paragraphs;

        public (int Line, int Col) Locate(int index) => LineStarts.Locate(index);

        /// <summary>
        /// 规则表里这条规则的标题、说明、建议方案：自定义过的用表里的原文，
        /// 其余取译文，缺译时退回表里的原文。
        /// </summary>
        public string RuleText(string field, string fallback, params object[] args) =>
            AiToneLintService.RuleText(Text, Rule, field, fallback, args);

        /// <summary>
        /// 统计附注的文案：键是 LintNote_&lt;id&gt;，缺译时退回规则表里的说明。
        /// 说明被改过时直接用表里的文字：统计行说的也是这条判据，两处不该各说各的。
        /// </summary>
        public string NoteText(params object[] args) =>
            Rule.UsesTableText(LintRule.TextFieldDetail)
                ? Format(Rule.Detail, args)
                : Text.Get($"LintNote_{Rule.Id}", Rule.Detail, args);

        /// <summary>
        /// 构造一条命中。位置、长度、说明由算法给，两段文字的取法也要由算法给：
        /// matchIndex/matchLength 是命中原文在原文里的那一段（与报出去的长度 Length 不必相同，
        /// 复读串报的是整串，命中原文只是串首那个单元），
        /// snippetIndex/snippetLength 是展示片段要取的那一段。
        /// 命中只记区间，Match 与 Snippet 到取值时才从原文切。
        /// 分组、严重程度直接取自规则表；标题、说明、建议方案按 <see cref="AiToneLintService.RuleText"/> 的次序取，
        /// 自定义过的用表里的原文，其余取译文。
        /// </summary>
        public LintHit Hit(int line, int col, int length,
            int matchIndex, int matchLength, int snippetIndex, int snippetLength, string detail)
        {
            var hit = new LintHit
            {
                Id = Rule.Id,
                Group = Rule.Group,
                Severity = Rule.Severity,
                Title = RuleText("Title", Rule.Title),
                Line = line,
                Col = col,
                Length = length,
                Detail = detail,
                SuggestScheme = Rule.SuggestScheme is null ? null : RuleText("Suggest", Rule.SuggestScheme),
            };
            hit.BindTo(Source.Original, matchIndex, matchLength, snippetIndex, snippetLength);
            return hit;
        }
    }
}

/// <summary>
/// 一份稿子在基线比较里站的位置。净增是两稿之间的事，单扫一份无从判定；
/// 「没基线」与「这是基线那一篇」对读者是两件事，所以分开说，不拿同一个空值抵掉。
/// </summary>
public enum LintScanRole
{
    /// <summary>单独一份稿子，手上没有基线。</summary>
    Standalone,

    /// <summary>基线那一篇：它自己没有净增可比，delta 门限不判。</summary>
    Baseline,

    /// <summary>要对基线的正文那一篇：delta 门限留到差分之后按净增判。</summary>
    Compared,
}
