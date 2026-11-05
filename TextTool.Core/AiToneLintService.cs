using System.Text.RegularExpressions;

namespace TextTool.Services;

/// <summary>
/// 中文 AI 味检查引擎：只报位，不改写。
/// 数据规则来自 LintRuleStore；统计类算法规则在本类内。
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

    /// <summary>按规则 Id 取译文，缺键时退回规则表里带的原文。</summary>
    private string RuleText(string id, string field, string fallback) =>
        _text.Get($"LintRule_{id}_{field}", fallback);

    /// <summary>
    /// 扫描一份稿子。
    /// </summary>
    /// <param name="role">
    /// 这份稿子在比较里站什么位置。只有 delta 密度基准读它：净增要写前写后两稿才算得出来，
    /// 单扫一份时那条门限无从判定，报用法错而不是退回全文口径。
    /// </param>
    public LintReport Scan(string text, string fileName, string? genre = null,
        LintScanRole role = LintScanRole.Standalone)
    {
        // 判据按简体字面写，繁体稿直接比对不响：先备一份繁转简的副本专门拿去匹配。
        // 归一串保证与原文等长，两份串的下标、长度、行列完全通用，报告里的位置与片段仍取原文。
        // 博文结构（头部字段、三冒号指令行、代码块、公式、链接地址）在归一份里遮成空格，
        // 规则与统计都碰不到它们，位置仍按原文算。
        var source = new ScanText(text);
        string mode = DetectParagraphMode(text);
        var report = new LintReport { File = fileName, Chars = source.Chars, ParagraphMode = mode };
        // 折叠阈值随报告带出：渲染层不查规则表，界面与命令行两处看到的折叠口径因此一致
        foreach (var rule in _rules)
            if (rule.Rule.CollapseAtDensity is int density)
                report.CollapseAtDensity[rule.Rule.Id] = density;
        // 行首下标表建一次就够：逐命中从头重扫是 O(长度×命中数)
        var lineStarts = new LineStarts(text);

        foreach (var rule in _rules)
        {
            if (rule.Rule.SkipInMarkdown && mode == "markdown")
                continue;
            if (rule.Rule.Scope == "paragraph")
                ScanParagraphScope(rule, source, report, mode, lineStarts);
            else
                ScanDocumentScope(rule, source, report, lineStarts, role);
        }

        ScanAlgorithmRules(source, mode, report, lineStarts, genre);
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

    /// <summary>代码内算法规则的 Id 全集（数据规则之外的 mixed-quote-style/mixed-bracket-width 与 paragraph-length-uniform-explicit-numbering）。</summary>
    public static readonly IReadOnlyList<string> AlgorithmRuleIds =
        new[] { "mixed-quote-style", "mixed-bracket-width", "paragraph-length-uniform", "sentence-length-uniform", "repeated-paragraph-opening", "exclamation-density", "ordinal-skeleton", "explicit-numbering", "short-sentence-run", "repeated-token-run", "choppy-clauses",
            // 两个窗口/句子级的判据也走代码，登记在这里三条地方才会认它们：--only 的取值校验、外部规则文件撞码检查、界面的规则清单
            "sentence-tempo", "fiction-simile-pile", "terminology-pile" };

    /// <summary>算法规则与给定数据规则的 Id 并集（--only 的校验集）。</summary>
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
        foreach (var (regex, hint) in rule.Patterns)
        {
            var matches = regex.Matches(source.Matching);
            if (matches.Count < rule.Rule.MinCount) continue;
            foreach (Match m in matches)
                candidates.Add((m.Index, m.Length, hint));
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
        string mode, LineStarts lineStarts)
    {
        var paragraphs = Paragraphs(source.Matching, mode);
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
    /// 同一条规则里，同一处只报一次：两张词表可能盖住同一个词（「砍一刀」与「这一刀」），
    /// 一个词也可能被两条词表从不同位置咬住（「验收口径」里既有「口径」又有「收口」）。
    /// 这两种都数两遍同一条证据，读者看到的命中数就虚高。重叠时留长的：读者认的是整个词。
    /// </summary>
    private static List<(int Index, int Length, string? Hint)> DropOverlaps(
        List<(int Index, int Length, string? Hint)> candidates)
    {
        if (candidates.Count <= 1) return candidates;

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
    private static readonly Regex SentenceEnd = RegexGuard.Create("(?<=[。！？…])");

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

    private void ScanAlgorithmRules(ScanText source, string mode, LintReport report,
        LineStarts lineStarts, string? genre)
    {
        // 判定看归一字面；要交回读者看的 Match 与 Snippet 从原文取
        string scan = source.Matching;
        // 节拍先算：它一旦报出，「连续短句」附注就不重复报同一个现象
        bool tempoReported = ScanSentenceTempo(scan, report, lineStarts) > 0;
        ScanSimilePile(source, report, lineStarts, genre);
        ScanTerminologyPile(source, report, lineStarts, genre);

        // mixed-quote-style 引号风格混用：命中定位到全文最早出现的引号字符，规模写进 Detail
        var used = QuoteStyles
            .Select(s => (
                s.Family,
                Name: _text.Get($"LintQuote_{s.Key}", s.Name),
                Count: s.Pattern.Matches(scan).Count,
                Index: s.Pattern.Match(scan).Index))
            .Where(s => s.Count > 0)
            .ToList();
        // 按套判，不按单个字符判：双引号里嵌单引号是规范写法，不是混用
        if (used.Select(s => s.Family).Distinct().Count() > 1)
        {
            int first = used.Min(s => s.Index);
            var (line, col) = lineStarts.Locate(first);
            string detail = string.Join(
                " / ",
                used.Select(s => _text.Get("LintQuoteUsage", "{0} {1} 个", s.Name, s.Count)));
            report.Hits.Add(new LintHit
            {
                Id = "mixed-quote-style", Group = "punctuation", Severity = "info",
                Title = _text.Get("LintRule_mixed-quote-style_Title", "引号风格混用"),
                Line = line, Col = col, Length = 1,
                Match = source.Slice(first, 1), Snippet = source.Snippet(first, 1),
                Detail = detail,
                SuggestScheme = _text.Get("LintRule_mixed-quote-style_Suggest", "引号括号统一"),
            });
        }

        // mixed-bracket-width 括号全半角混用
        int half = HalfWidthBracket.Matches(scan).Count;
        int full = FullWidthBracket.Matches(scan).Count;
        if (half > 0 && full > 0)
        {
            var m = AnyBracket.Match(scan);
            var (line, col) = lineStarts.Locate(m.Index);
            report.Hits.Add(new LintHit
            {
                Id = "mixed-bracket-width", Group = "punctuation", Severity = "info",
                Title = _text.Get("LintRule_mixed-bracket-width_Title", "括号全半角混用"),
                Line = line, Col = col, Length = 1,
                Match = source.Slice(m.Index, 1), Snippet = source.Snippet(m.Index, 1),
                Detail = _text.Get(
                    "LintRule_mixed-bracket-width_Detail", "半角 {0} 个 / 全角 {1} 个", half, full),
                SuggestScheme = _text.Get("LintRule_mixed-bracket-width_Suggest", "引号括号统一"),
            });
        }

        var paragraphs = Paragraphs(scan, mode);

        // paragraph-length-uniform 段落长度过于均一
        if (paragraphs.Count >= 5)
        {
            var lens = paragraphs.Select(p => (double)p.Text.Length).ToList();
            double mean = lens.Average(), cv = StdDev(lens, mean) / mean;
            if (cv < 0.25)
                report.Notes.Add(new LintNote
                {
                    Id = "paragraph-length-uniform",
                    Text = _text.Get(
                        "LintNote_paragraph-length-uniform",
                        "段落长度过于均一（变异系数 {0}，{1} 段）——人写文本段落长短通常更参差",
                        cv.ToString("0.00"), paragraphs.Count),
                });
        }

        // sentence-length-uniform 句长分布过于整齐
        var sentences = SentenceEnd.Split(scan)
            .Select(s => s.Trim()).Where(s => s.Length >= 6).ToList();
        if (sentences.Count >= 10)
        {
            var lens = sentences.Select(s => (double)s.Length).ToList();
            double mean = lens.Average(), cv = StdDev(lens, mean) / mean;
            if (cv < 0.35)
                report.Notes.Add(new LintNote
                {
                    Id = "sentence-length-uniform",
                    Text = _text.Get(
                        "LintNote_sentence-length-uniform",
                        "句长分布过于整齐（变异系数 {0}，{1} 句）",
                        cv.ToString("0.00"), sentences.Count),
                });
        }

        // short-sentence-run 连续短句：一句十来字正常，连着四句以上就是节拍化——
        // 每句都在表演「克制」，读起来像按模板在填。与 sentence-length-uniform 查的不是同一件事：
        // 那条看句长是否过于整齐（方差小），这条看是否连着短。
        var rawSentences = SentenceEnd.Split(scan).Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        int run = 0, maxRun = 0;
        foreach (var sentence in rawSentences)
        {
            run = sentence.Length <= ShortSentenceMaxChars ? run + 1 : 0;
            if (run > maxRun) maxRun = run;
        }
        if (maxRun >= ShortSentenceMinRun && !tempoReported)
            report.Notes.Add(new LintNote
            {
                Id = "short-sentence-run",
                Text = _text.Get(
                    "LintNote_short-sentence-run",
                    "连续 {0} 句都在 {1} 字以内——节拍化的碎片句，像在逐条填写动作与情绪",
                    maxRun, ShortSentenceMaxChars),
            });

        // repeated-paragraph-opening 段首两字高度重复
        if (paragraphs.Count >= 4)
        {
            var dup = paragraphs.Select(p => p.Text[..Math.Min(2, p.Text.Length)])
                .GroupBy(h => h).Where(g => g.Count() >= 3).ToList();
            if (dup.Count > 0)
                report.Notes.Add(new LintNote
                {
                    Id = "repeated-paragraph-opening",
                    Text = _text.Get(
                        "LintNote_repeated-paragraph-opening",
                        "段首两字高度重复：{0}",
                        string.Join("、", dup.Select(g => $"「{g.Key}」×{g.Count()}"))),
                });
        }

        // exclamation-density 叹号密度偏高
        int exclam = scan.Count(c => c == '！');
        if (scan.Length >= 400 && exclam * 1000.0 / scan.Length > 3)
            report.Notes.Add(new LintNote
            {
                Id = "exclamation-density",
                Text = _text.Get(
                    "LintNote_exclamation-density", "叹号密度偏高：{0} 个 / {1} 字", exclam, scan.Length),
            });

        // ordinal-skeleton 序数词骨架密度。
        // 小说档不查：模型写小说本来就不会排「首先……其次……」，而「再次」「最后」在叙事里是日常用词，
        // 一段里凑够两次就会误报（实测一篇投石头的散文里两处「再次」就够门限）。
        int ordinals = OrdinalSkeleton.Matches(scan).Count;
        if (ordinals >= 2 && !string.Equals(genre, "fiction", StringComparison.OrdinalIgnoreCase))
            report.Notes.Add(new LintNote
            {
                Id = "ordinal-skeleton",
                Text = _text.Get(
                    "LintNote_ordinal-skeleton",
                    "序数词骨架「首先/其次/最后」出现 {0} 次——结构化排版的典型痕迹",
                    ordinals),
            });

        // explicit-numbering 显式序号标记：手写文本很少通篇硬编号，密度高是排版痕迹
        int markers = ExplicitOrdinalMarker.Matches(scan).Count;
        if (markers >= 3)
            report.Notes.Add(new LintNote
            {
                Id = "explicit-numbering",
                Text = _text.Get(
                    "LintNote_explicit-numbering",
                    "显式序号标记 {0} 处——结构化排版的典型痕迹",
                    markers),
            });

        ScanRepeatedVerbatim(source, paragraphs, report, lineStarts);
        ScanRepeatedTokenRun(source, paragraphs, report, lineStarts);
        ScanChoppyClauses(source, paragraphs, report, lineStarts, genre);
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
    /// 小说不跑：短句在叙事里是正常手法。
    /// </summary>
    private void ScanChoppyClauses(ScanText source, List<(int Offset, string Text)> paragraphs,
        LintReport report, LineStarts lineStarts, string? genre)
    {
        if (string.Equals(genre, "fiction", StringComparison.OrdinalIgnoreCase)) return;

        foreach (var (offset, body) in paragraphs)
        {
            if (!body.Contains('；') && !body.Contains("——")) continue;
            if (OrdinalSkeleton.IsMatch(body)) continue;
            var clauses = body.Split(ClauseBreaks, StringSplitOptions.RemoveEmptyEntries)
                .Select(c => c.Trim()).Where(c => c.Length > 0).ToList();
            if (clauses.Count < ChoppyClauseMinCount) continue;
            double mean = clauses.Average(c => (double)c.Length);
            if (mean > ChoppyClauseMaxMeanChars) continue;

            var (line, col) = lineStarts.Locate(offset);
            string head = body.Trim();
            if (head.Length > 30) head = head[..30];
            report.Hits.Add(new LintHit
            {
                Id = "choppy-clauses", Group = "structure", Severity = "info",
                Title = _text.Get("LintRule_choppy-clauses_Title", "短句硬连"),
                Line = line, Col = col, Length = head.Length,
                Match = source.Slice(offset, head.Length),
                Snippet = source.Snippet(offset, head.Length),
                Detail = _text.Get("LintRule_choppy-clauses_Detail",
                    "这一段切成 {0} 个短促分句，平均 {1} 字，靠分号与破折号硬接——成分全省了，读者得自己补主语和宾语",
                    clauses.Count, Math.Round(mean, 1)),
            });
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
    private void ScanRepeatedTokenRun(ScanText source, List<(int Offset, string Text)> paragraphs,
        LintReport report, LineStarts lineStarts)
    {
        var runs = new List<(int Start, int End)>();
        foreach (var (offset, body) in paragraphs)
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
                var (line, col) = lineStarts.Locate(start);
                // 归一只为匹配：复读串报给读者的是原文那几个字
                string shown = source.Slice(start, unit.Length);
                report.Hits.Add(new LintHit
                {
                    Id = "repeated-token-run", Group = "structure", Severity = "warn",
                    Title = _text.Get("LintRule_repeated-token-run_Title", "复读串"),
                    Line = line, Col = col, Length = length,
                    Match = shown, Snippet = source.Snippet(start, Math.Min(length, 40)),
                    Detail = _text.Get(
                        "LintRule_repeated-token-run_Detail", "「{0}」连写 {1} 次", shown, count),
                });
                runs.Add((start, start + length));
                i += length;
            }
        }

        if (runs.Count == 0) return;

        // 整串已经报过一处，串里的其他命中不再各报一遍：副词同段堆叠在复读串里是同一个现象。
        var spans = runs.Select(r =>
        {
            var (l1, c1) = lineStarts.Locate(r.Start);
            var (l2, c2) = lineStarts.Locate(r.End - 1);
            return (L1: l1, C1: c1, L2: l2, C2: c2);
        }).ToList();
        report.Hits.RemoveAll(h =>
            h.Id != "repeated-token-run" && spans.Any(s => InsideSpan(s, h.Line, h.Col)));
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
        for (int len = RepeatedTokenMinUnit; len <= RepeatedTokenMaxUnit; len++)
        {
            if (from + len * RepeatedTokenMinRun > body.Length) continue;
            string unit = body.Substring(from, len);
            if (unit.Any(char.IsWhiteSpace)) continue;
            if (!unit.Any(char.IsLetter)) continue;
            // 同一个字连写（「字字字字」）是强调或占位，不是词在复读。
            if (unit.Distinct().Count() == 1) continue;
            if (unit.All(c => InterjectionChars.Contains(c))) continue;

            int count = 1;
            while (from + len * (count + 1) <= body.Length &&
                   string.CompareOrdinal(body, from + len * count, unit, 0, len) == 0)
                count++;
            if (count >= RepeatedTokenMinRun) return (unit, count);
        }
        return (null, 0);
    }

    /// <summary>
    /// repeated-verbatim 逐字重复：完全相同的段落或句子再次出现时报出来。
    /// 只比逐字相同（去首尾空白后完全相等），近似复述不在此列——那要相似度阈值，
    /// 误伤对象是排比、副歌式的刻意反复。段重复时不再逐句报；句子级的多处重复
    /// 若都指向同一处原文，并成一条：一次贴回一大段，逐句各报一处只会把报告淹掉。
    /// </summary>
    private void ScanRepeatedVerbatim(ScanText source, List<(int Offset, string Text)> paragraphs,
        LintReport report, LineStarts lineStarts)
    {
        var firstSeen = new Dictionary<string, (int Line, int Col)>(StringComparer.Ordinal);
        var repeatedRanges = new List<(int Start, int End)>();

        void Report(int offset, string body, string? detail = null)
        {
            var (line, col) = lineStarts.Locate(offset);
            var (firstLine, firstCol) = firstSeen[body];
            report.Hits.Add(new LintHit
            {
                Id = "repeated-verbatim", Group = "structure", Severity = "warn",
                Title = _text.Get("LintRule_repeated-verbatim_Title", "逐字重复"),
                Line = line, Col = col, Length = body.Length,
                Match = source.Slice(offset, body.Length),
                Snippet = source.Snippet(offset, body.Length),
                Detail = detail ?? _text.Get("LintRule_repeated-verbatim_Detail",
                    "与第 {0} 行第 {1} 列逐字相同", firstLine, firstCol),
            });
        }

        foreach (var (offset, body) in paragraphs)
        {
            if (body.Length < MinRepeatParagraphChars) continue;
            if (firstSeen.TryAdd(body, lineStarts.Locate(offset)))
                continue;
            Report(offset, body);
            repeatedRanges.Add((offset, offset + body.Length));
        }

        // 句子级的首现表单独一份：段与句的长度不同，同一段的两份不会互相冒充。
        // 首现位置要连偏移一起记：判「连着的几句是不是同一段的两份」，靠的是两边的间隔对得上。
        var firstAt = new Dictionary<string, int>(StringComparer.Ordinal);
        var repeats = new List<(int Offset, int Length, int Source)>();
        foreach (var (offset, body) in SentenceBlocks(source.Matching))
        {
            if (body.Length < MinRepeatSentenceChars) continue;
            if (repeatedRanges.Any(r => offset >= r.Start && offset < r.End)) continue;
            if (firstAt.TryAdd(body, offset)) continue;
            repeats.Add((offset, body.Length, firstAt[body]));
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

            var (line, col) = lineStarts.Locate(start);
            var (sourceLine, sourceCol) = lineStarts.Locate(sourceStart);
            report.Hits.Add(new LintHit
            {
                Id = "repeated-verbatim", Group = "structure", Severity = "warn",
                Title = _text.Get("LintRule_repeated-verbatim_Title", "逐字重复"),
                Line = line, Col = col, Length = end - start,
                Match = source.Slice(start, end - start),
                Snippet = source.Snippet(start, Math.Min(end - start, 40)),
                Detail = count > 1
                    ? _text.Get("LintRule_repeated-verbatim_GroupDetail",
                        "从这一句起共 {0} 句与第 {1} 行第 {2} 列起逐字相同", count, sourceLine, sourceCol)
                    : _text.Get("LintRule_repeated-verbatim_Detail",
                        "与第 {0} 行第 {1} 列逐字相同", sourceLine, sourceCol),
            });
            k++;
        }
    }

    /// <summary>并段时容许的间隔：句与句之间只该有标点或换行，隔得再远就不算同一段被贴回。</summary>
    private const int RepeatJoinGap = 2;

    /// <summary>按句末标点切句，带上原文偏移，供逐字比对定位用。</summary>
    private static IEnumerable<(int Offset, string Text)> SentenceBlocks(string text)
    {
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] is not ('。' or '！' or '？' or '…')) continue;
            foreach (var block in Trimmed(text, start, i + 1)) yield return block;
            start = i + 1;
        }
        foreach (var block in Trimmed(text, start, text.Length)) yield return block;
    }

    private static IEnumerable<(int Offset, string Text)> Trimmed(string text, int from, int to)
    {
        if (to <= from) yield break;
        int start = from, end = to;
        while (start < end && char.IsWhiteSpace(text[start])) start++;
        while (end > start && char.IsWhiteSpace(text[end - 1])) end--;
        if (end > start) yield return (start, text[start..end]);
    }

    private static double StdDev(List<double> values, double mean)
    {
        double sum = values.Sum(v => (v - mean) * (v - mean));
        return Math.Sqrt(sum / values.Count);
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
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var trimmed = line.Trim();
            if (trimmed.Length > 0)
                result.Add((offset + (line.Length - line.TrimStart().Length), trimmed));
            offset += raw.Length + 1;
        }
        return result;
    }

    private static List<(int Offset, string Text)> MarkdownParagraphs(string text)
    {
        var lines = new List<(int Offset, string Raw)>();
        int offset = 0;
        foreach (var raw in text.Split('\n'))
        {
            lines.Add((offset, raw.TrimEnd('\r')));
            offset += raw.Length + 1;
        }

        var result = new List<(int, string)>();
        int i = 0;
        while (i < lines.Count)
        {
            if (lines[i].Raw.Trim().Length == 0) { i++; continue; }
            if (IsBlockLine(lines[i].Raw))
            {
                AddBlock(result, text, lines[i].Offset, lines[i].Offset + lines[i].Raw.Length);
                i++;
                continue;
            }
            int end = i;
            while (end + 1 < lines.Count
                   && lines[end + 1].Raw.Trim().Length > 0
                   && !IsBlockLine(lines[end + 1].Raw))
                end++;
            AddBlock(result, text, lines[i].Offset, lines[end].Offset + lines[end].Raw.Length);
            i = end + 1;
        }
        return result;
    }

    /// <summary>列表项、引用行与编号行各自成段：并进散文段会让 paragraph-ordinals 的「段内序数词」跨条目误报。</summary>
    private static bool IsBlockLine(string raw)
    {
        var t = raw.TrimStart();
        if (t.StartsWith("- ", StringComparison.Ordinal)
            || t.StartsWith("* ", StringComparison.Ordinal)
            || t.StartsWith("+ ", StringComparison.Ordinal)
            || t.StartsWith(">", StringComparison.Ordinal))
            return true;

        int digits = 0;
        while (digits < t.Length && char.IsAsciiDigit(t[digits])) digits++;
        return digits > 0 && digits + 1 < t.Length && (t[digits] == '.' || t[digits] == '、');
    }

    private static void AddBlock(List<(int Offset, string Text)> result, string text, int start, int end)
    {
        var slice = text[start..end];
        var trimmed = slice.Trim();
        if (trimmed.Length > 0)
            result.Add((start + (slice.Length - slice.TrimStart().Length), trimmed));
    }

    private LintHit Hit(LintRule rule, LineStarts lineStarts, ScanText source, int index,
        int length, string? hint)
    {
        var (line, col) = lineStarts.Locate(index);
        return new LintHit
        {
            Id = rule.Id, Group = rule.Group, Severity = rule.Severity,
            Title = RuleText(rule.Id, "Title", rule.Title),
            Line = line, Col = col, Length = length,
            // 位置与长度对归一串和原文通用：这里取到的就是读者自己交来的那几个字
            Match = source.Slice(index, length),
            Snippet = source.Snippet(index, length),
            Detail = RuleText(rule.Id, "Detail", rule.Detail), Hint = hint,
            SuggestScheme = rule.SuggestScheme is null
                ? null
                : RuleText(rule.Id, "Suggest", rule.SuggestScheme),
        };
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
    private int ScanSentenceTempo(string text, LintReport report, LineStarts lineStarts)
    {
        if (text.Length < TempoMinTextChars) return 0;

        int reported = 0;
        int windowChars = Math.Min(TempoWindowChars, text.Length);
        // 上一窗也是节拍就不重复报：相邻窗重叠一半，同一段短句会在几窗里连续成立，跟着步长报就是刷屏
        bool previousWasTempo = false;
        for (int start = 0; start + windowChars <= text.Length; start += TempoWindowStep)
        {
            string window = text.Substring(start, windowChars);
            var sentences = SentenceEnd.Split(window)
                .Select(s => s.Trim())
                .Where(s => s.Length >= TempoMinSentenceChars)
                .ToList();
            bool shortWindow = sentences.Count >= TempoMinSentences
                && sentences.Average(s => (double)s.Length) < TempoMaxMeanChars
                && sentences.Max(s => s.Length) <= TempoMaxSentenceChars;
            if (!shortWindow)
            {
                previousWasTempo = false;
                continue;
            }

            if (!previousWasTempo)
            {
                report.Notes.Add(new LintNote
                {
                    Id = "sentence-tempo",
                    Text = _text.Get(
                        "LintNote_sentence-tempo",
                        "第 {0} 行起这一窗 {1} 句，平均 {2} 字，最长 {3} 字——一句都没说长，整段按节拍往下砸",
                        lineStarts.Locate(start).Line + 1, sentences.Count,
                        Math.Round(sentences.Average(s => (double)s.Length), 1),
                        sentences.Max(s => s.Length)),
                });
                reported++;
            }

            previousWasTempo = true;
        }

        return reported;
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
    /// 只在小说与「全部规则」两种情形下判：编程文档与智能体回复里，比喻堆叠不是这套工具要管的事。
    /// 密度不到门限就整条不报——一句长句带几个比喻，真人写作里也有，凑够密度才是堆砌。
    /// </summary>
    private void ScanSimilePile(ScanText source, LintReport report, LineStarts lineStarts,
        string? genre)
    {
        if (genre is "doc" or "agent") return;

        string scan = source.Matching;
        var candidates = new List<(int Index, int Length, int Similes)>();
        foreach (Match m in SentenceSpan.Matches(scan))
        {
            if (m.Value.Trim().Length < SimilePileMinSentenceChars) continue;
            int similes = SimileMarker.Matches(m.Value).Count;
            if (similes < SimilePileMinSimiles) continue;
            candidates.Add((m.Index, m.Length, similes));
        }
        if (candidates.Count == 0) return;
        if (candidates.Count * 1000.0 / Math.Max(scan.Length, 1) < SimilePileMinDensity) return;

        foreach (var (index, length, similes) in candidates)
        {
            var (line, col) = lineStarts.Locate(index);
            string raw = scan.Substring(index, length);
            string whole = raw.Trim();
            string head = whole.Length > 30 ? whole[..30] : whole;
            // 句首可能带空白，切过空白之后的下标才是这个词在原文里的起点
            int headIndex = index + (raw.Length - raw.TrimStart().Length);
            report.Hits.Add(new LintHit
            {
                Id = "fiction-simile-pile", Group = "syntax", Severity = "info",
                Title = _text.Get("LintRule_fiction-simile-pile_Title", "一句里叠着比喻"),
                Line = line, Col = col, Length = head.Length,
                Match = source.Slice(headIndex, head.Length),
                Snippet = source.Snippet(index, head.Length),
                Detail = _text.Get(
                    "LintRule_fiction-simile-pile_Detail",
                    "这一句 {0} 字里串了 {1} 个比喻——读者拿到的是一串喻体，不是一个画面；真人长篇里这种句子每千字不超过 0.08 句",
                    whole.Length, similes),
            });
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
    /// 编程文档与智能体回复两档不判：在那两档里「算法」「架构」是正常用词。
    /// </summary>
    private void ScanTerminologyPile(ScanText source, LintReport report, LineStarts lineStarts,
        string? genre)
    {
        if (genre is "doc" or "agent") return;

        string scan = source.Matching;
        var quoted = new List<(int Start, int End)>();
        foreach (Match q in QuotedSpan.Matches(scan))
            quoted.Add((q.Index, q.Index + q.Length));
        bool InQuotes(int index) => quoted.Any(q => index >= q.Start && index < q.End);

        var hits = new List<(int Index, int Length, string Term)>();
        foreach (Match m in TerminologyTerm.Matches(scan))
        {
            if (InQuotes(m.Index)) continue;
            hits.Add((m.Index, m.Length, source.Slice(m.Index, m.Length)));
        }
        if (hits.Count == 0) return;
        double density = hits.Count * 1000.0 / Math.Max(scan.Length, 1);
        if (density < TerminologyPileMinDensity) return;

        double shown = Math.Round(density, 1);
        foreach (var (index, length, term) in hits)
        {
            var (line, col) = lineStarts.Locate(index);
            report.Hits.Add(new LintHit
            {
                Id = "terminology-pile", Group = "lexis", Severity = "info",
                Title = _text.Get("LintRule_terminology-pile_Title", "术语与意象堆砌"),
                Line = line, Col = col, Length = length,
                Match = term, Snippet = source.Snippet(index, length),
                Detail = _text.Get(
                    "LintRule_terminology-pile_Detail",
                    "全篇这类词每千字 {0} 处，引号外的都算——术语替掉了具体的观察，读者拿到的是名词，不是看见的东西；真人长篇每千字最高 0.05 处",
                    shown),
            });
        }
    }

    /// <summary>
    /// 一份待扫文本的两面：原文交回读者，归一字面交给规则。
    /// 归一按词换字，但保证与原文等长、逐位对齐，命中位置因此不必换算：
    /// 匹配得到的 (index, length) 直接在原文里切，拿到的是用户自己交来的那几个字。
    ///
    /// 博文结构在归一份里先遮成空格：匹配不到词，整行被遮的地方当空行，篇幅统计因此只算正文。
    /// 原文原样留着，Match 与 Snippet 取到的仍是用户写的那些字。
    /// </summary>
    private readonly struct ScanText
    {
        public ScanText(string text)
        {
            Original = text;
            var mask = BlogMarkdownMask.Create(text);
            Matching = TraditionalChinese.ToSimplified(mask is null ? text : mask.Apply(text));
            Chars = text.Length - (mask?.MaskedChars ?? 0);
        }

        /// <summary>用户原文。行列、Match、Snippet 都从它取。</summary>
        public string Original { get; }

        /// <summary>繁转简后的副本，只给匹配与统计看。博文结构在这里是空格。</summary>
        public string Matching { get; }

        /// <summary>正文字数：原文长度扣掉博文结构那部分。密度门限与报告里的字数都按它算。</summary>
        public int Chars { get; }

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
        int lo = Math.Max(0, index - pad);
        int hi = Math.Min(text.Length, index + length + pad);
        if (lo > 0 && char.IsLowSurrogate(text[lo])) lo--;                  // 左边界落在低代理上：回退一格
        if (hi < text.Length && char.IsHighSurrogate(text[hi - 1])) hi++;   // 右边界停在高代理后：前进一格
        var s = text[lo..hi];
        if (lo > 0) s = "…" + s;
        if (hi < text.Length) s += "…";
        return s;
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
                .Concat(rule.Patterns.Select(pattern => (Pattern: pattern, Escaped: rule.Kind == "literal")));
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
