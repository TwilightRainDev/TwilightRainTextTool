namespace TextTool.Services;

/// <summary>
/// lint 编排：多段输入 + 过滤 → 报告集。不读盘、不写盘。
///
/// 一次要查很多份时别走 <see cref="Run"/>：它把全部报告攒齐再交给调用方。
/// 用 <see cref="LintSession.Create"/> 建一次引擎，再逐份 <see cref="LintSession.Scan"/>，
/// 报告写一份丢一份。
/// </summary>
public static class LintRunner
{
    /// <param name="rules">
    /// 规则表。默认从磁盘加载；标定命令一次加载、逐文件复用，测试注入探针规则，
    /// 两条路径共用同一个引擎实例来源，不另写一套判定。
    /// </param>
    /// <param name="baseline">
    /// 写前那一份。给了它，报告只含比它多出来的命中，每条规则另给一行新增数；
    /// 不给就是整篇命中。基线只配单份输入：一次差分只能回答「这一份比那份多了什么」。
    /// </param>
    public static LintReportSet Run(
        IReadOnlyList<(string File, string Text)> inputs,
        IReadOnlyCollection<string>? onlyIds,
        string? minSeverity,
        string? genre = null,
        ILintText? lintText = null,
        IReadOnlyList<LintRule>? rules = null,
        (string File, string Text)? baseline = null)
    {
        if (inputs is null || inputs.Count == 0)
            throw new ArgumentException("lint 需要至少一个输入");

        var session = LintSession.Create(genre, onlyIds, minSeverity, lintText, rules,
            // 基线条数这条排在规则表校验之后，与它该报的位置一致
            baselineInputs: baseline is null ? null : inputs.Count);

        var reportSet = new LintReportSet();
        if (baseline is null)
        {
            foreach (var (file, text) in inputs)
                reportSet.Reports.Add(session.Scan(file, text));
            return reportSet;
        }

        reportSet.Reports.Add(session.ScanAgainstBaseline(
            inputs[0].File, inputs[0].Text, baseline.Value.File, baseline.Value.Text));
        return reportSet;
    }

    /// <summary>
    /// 按档位挑出参与扫描的规则。留空 Genres 的规则各档都跑；不给档位就是整张表（默认不勾选的除外）。
    ///
    /// 标了 DefaultOff 的判据只有被 --only 点名才跑：这类判据在真人语料上还没守住误报门限，
    /// 默认关着比删掉强——想用的人点得到，不想用的人永远看不到它。
    ///
    /// 被档位挡掉的规则与 --only 同时点名时必须报错，否则用户看到的是「零命中」，
    /// 而真相是「这条规则不在这个档」，两种沉默完全不同。
    /// 标定命令用同一份筛选建表，表里出现的规则就是那个档实际会跑的规则。
    /// </summary>
    public static List<LintRule> ActiveRules(
        IReadOnlyList<LintRule> rules, string? genre, IReadOnlyCollection<string>? namedByOnly = null)
    {
        ValidateGenre(genre);
        bool Named(LintRule r) => namedByOnly?.Contains(r.Id, StringComparer.OrdinalIgnoreCase) ?? false;
        if (genre is null)
            return rules.Where(r => !r.DefaultOff || Named(r)).ToList();

        if (namedByOnly is not null)
        {
            var excluded = rules
                .Where(r => r.Genres.Count > 0
                    && !r.Genres.Contains(genre, StringComparer.OrdinalIgnoreCase)
                    && namedByOnly.Contains(r.Id, StringComparer.OrdinalIgnoreCase))
                .ToList();
            if (excluded.Count > 0)
                throw new ArgumentException(
                    $"规则 {string.Join(", ", excluded.Select(r => r.Id))} 不在 {genre} 档生效（适用档位：{string.Join("/", excluded[0].Genres)}）");
        }

        return rules
            .Where(r => r.Genres.Count == 0 || r.Genres.Contains(genre, StringComparer.OrdinalIgnoreCase))
            .Where(r => !r.DefaultOff || Named(r))
            .ToList();
    }

    internal static void ValidateGenre(string? genre)
    {
        if (genre is not null
            && !LintRuleStore.GenreIds.Contains(genre, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException(
                $"--genre 只接受 {string.Join("、", LintRuleStore.GenreIds)}");
    }
}

/// <summary>
/// 一次检查的引擎：规则表加载、规则编译、档位筛选只做一次，逐份扫描复用同一个实例。
///
/// 引擎本身没有随扫描变化的状态（每次扫描的报告、待扫文本、切好的段都是新建的），
/// 所以同一实例扫完一份再扫下一份，结果与每份各建一个引擎相同。
/// </summary>
public sealed class LintSession
{
    private readonly AiToneLintService _service;
    private readonly IReadOnlyList<LintRule> _active;
    private readonly ILintText? _lintText;
    private readonly IReadOnlyCollection<string>? _onlyIds;
    private readonly string? _genre;
    private readonly bool _wantWarnOnly;

    private LintSession(
        AiToneLintService service,
        IReadOnlyList<LintRule> active,
        string? genre,
        IReadOnlyCollection<string>? onlyIds,
        string? minSeverity,
        ILintText? lintText)
    {
        _service = service;
        _active = active;
        _genre = genre;
        _onlyIds = onlyIds;
        _lintText = lintText;
        _wantWarnOnly = string.Equals(minSeverity, "warn", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 建引擎。参数校验的顺序与 <see cref="LintRunner.Run"/> 一致：档位、规则 Id、规则表。
    /// </summary>
    /// <param name="baselineInputs">
    /// 给了基线时这次调用有几份输入；不给基线就是 null。非 null 时必须是 1：
    /// 一次差分只能回答「这一份比那份多了什么」。这条校验排在规则表校验之后，
    /// 与「未知规则 Id」「档位挡住了点名的规则」一起报时，先报规则表那一条。
    /// </param>
    public static LintSession Create(
        string? genre,
        IReadOnlyCollection<string>? onlyIds,
        string? minSeverity,
        ILintText? lintText = null,
        IReadOnlyList<LintRule>? rules = null,
        int? baselineInputs = null)
    {
        if (minSeverity is not null
            && !minSeverity.Equals("info", StringComparison.OrdinalIgnoreCase)
            && !minSeverity.Equals("warn", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("--min-severity 只接受 info 或 warn");
        // 档位校验放在读规则表之前：拼错的档位不该先撞上规则文件损坏这类无关报错
        LintRunner.ValidateGenre(genre);
        if (onlyIds is not null && onlyIds.Count == 0)
            throw new ArgumentException("--only 需要至少一个规则 Id");
        var table = rules ?? LintRuleStore.Load();
        if (onlyIds is not null)
        {
            // 未如规则 Id 要拿全表校：拿档位过滤后的表校，会把「这条规则只在小说档跑」误报成「未知 Id」
            var known = AiToneLintService.AllRuleIds(table);
            var unknown = onlyIds.Where(id => !known.Contains(id, StringComparer.OrdinalIgnoreCase)).ToList();
            if (unknown.Count > 0)
                throw new ArgumentException($"未知规则 Id：{string.Join(", ", unknown)}");
        }

        if (baselineInputs is not null && baselineInputs != 1)
            throw new ArgumentException("--baseline 只配一个输入文件：一次差分只回答「这一份比那份多了什么」");

        var active = LintRunner.ActiveRules(table, genre, onlyIds);
        return new LintSession(new AiToneLintService(active, lintText), active, genre, onlyIds, minSeverity, lintText);
    }

    /// <summary>
    /// 扫一份，按 --only 与 --min-severity 裁好交出去。
    ///
    /// 没有过滤条件时把引擎刚产出的那份原样交回：那份报告只有这一个持有者，
    /// 逐条复制一遍命中再丢掉原件是白花的功夫。要裁的时候才走 <see cref="LintReport.Filter"/>，
    /// 它负责让结果与这份报告不共享任何可变状态。
    /// </summary>
    public LintReport Scan(string file, string text)
    {
        var report = _service.Scan(text, file, _genre, LintScanRole.Standalone, respectBlogDefaults: _onlyIds is null);
        return Filtered(report);
    }

    /// <summary>
    /// 带基线扫一份：同一份规则表扫两遍，基线那份只用来数词面，报告本身不给读者看。
    /// 两遍共用一个引擎实例，规则编译、繁简归一这些只做一次。
    /// </summary>
    public LintReport ScanAgainstBaseline(string file, string text, string baselineFile, string baselineText)
    {
        var counts = LintBaseline.Count(_service.Scan(
            baselineText, baselineFile, _genre, LintScanRole.Baseline, _onlyIds is null));
        var body = _service.Scan(text, file, _genre, LintScanRole.Compared, _onlyIds is null);
        LintBaseline.Apply(body, counts, _active, _lintText);
        return Filtered(body);
    }

    private LintReport Filtered(LintReport report) =>
        _onlyIds is null && !_wantWarnOnly ? report : report.Filter(_onlyIds, _wantWarnOnly ? "warn" : null);
}
