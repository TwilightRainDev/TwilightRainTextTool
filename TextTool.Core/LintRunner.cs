namespace TextTool.Services;

/// <summary>
/// lint 编排：多段输入 + 过滤 → 报告集。不读盘、不写盘。
/// </summary>
public static class LintRunner
{
    /// <param name="rules">
    /// 规则表。默认从磁盘加载；标定命令一次加载、逐文件复用，测试注入探针规则，
    /// 两条路径共用同一个引擎实例来源，不另写一套判定。
    /// </param>
    public static LintReportSet Run(
        IReadOnlyList<(string File, string Text)> inputs,
        IReadOnlyCollection<string>? onlyIds,
        string? minSeverity,
        string? genre = null,
        ILintText? lintText = null,
        IReadOnlyList<LintRule>? rules = null)
    {
        if (inputs is null || inputs.Count == 0)
            throw new ArgumentException("lint 需要至少一个输入");
        if (minSeverity is not null
            && !minSeverity.Equals("info", StringComparison.OrdinalIgnoreCase)
            && !minSeverity.Equals("warn", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("--min-severity 只接受 info 或 warn");
        // 档位校验放在读规则表之前：拼错的档位不该先撞上规则文件损坏这类无关报错
        ValidateGenre(genre);
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

        var service = new AiToneLintService(ActiveRules(table, genre, onlyIds), lintText);
        var reportSet = new LintReportSet();
        foreach (var (file, text) in inputs)
            reportSet.Reports.Add(service.Scan(text, file, genre).Filter(onlyIds, minSeverity));
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

    private static void ValidateGenre(string? genre)
    {
        if (genre is not null
            && !LintRuleStore.GenreIds.Contains(genre, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException(
                $"--genre 只接受 {string.Join("、", LintRuleStore.GenreIds)}");
    }
}
