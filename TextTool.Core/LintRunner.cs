namespace TextTool.Services;

/// <summary>
/// lint 编排：多段输入 + 过滤 → 报告集。不读盘、不写盘。
/// </summary>
public static class LintRunner
{
    public static LintReportSet Run(
        IReadOnlyList<(string File, string Text)> inputs,
        IReadOnlyCollection<string>? onlyIds,
        string? minSeverity,
        string? genre = null,
        ILintText? lintText = null)
    {
        if (inputs is null || inputs.Count == 0)
            throw new ArgumentException("lint 需要至少一个输入");
        if (minSeverity is not null
            && !minSeverity.Equals("info", StringComparison.OrdinalIgnoreCase)
            && !minSeverity.Equals("warn", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("--min-severity 只接受 info 或 warn");
        if (genre is not null
            && !LintRuleStore.GenreIds.Contains(genre, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException(
                $"--genre 只接受 {string.Join("、", LintRuleStore.GenreIds)}");
        if (onlyIds is not null && onlyIds.Count == 0)
            throw new ArgumentException("--only 需要至少一个规则 Id");
        var rules = LintRuleStore.Load();
        if (onlyIds is not null)
        {
            // 未如规则 Id 要拿全表校：拿档位过滤后的表校，会把「这条规则只在小说档跑」误报成「未知 Id」
            var known = AiToneLintService.AllRuleIds(rules);
            var unknown = onlyIds.Where(id => !known.Contains(id, StringComparer.OrdinalIgnoreCase)).ToList();
            if (unknown.Count > 0)
                throw new ArgumentException($"未知规则 Id：{string.Join(", ", unknown)}");
        }
        if (genre is not null)
        {
            // 留空的规则各档都跑；被档位挡掉的规则与 --only 同时点名时必须报错，
            // 否则用户看到的是「零命中」，而真相是「这条规则不在这个档」——两种沉默完全不同
            var excluded = rules
                .Where(r => r.Genres.Count > 0
                    && !r.Genres.Contains(genre, StringComparer.OrdinalIgnoreCase)
                    && onlyIds is not null
                    && onlyIds.Contains(r.Id, StringComparer.OrdinalIgnoreCase))
                .Select(r => r.Id)
                .ToList();
            if (excluded.Count > 0)
                throw new ArgumentException(
                    $"规则 {string.Join(", ", excluded)} 不在 {genre} 档生效（适用档位：{string.Join("/", rules.First(r => r.Id == excluded[0]).Genres)}）");
            rules = rules
                .Where(r => r.Genres.Count == 0 || r.Genres.Contains(genre, StringComparer.OrdinalIgnoreCase))
                .ToList();
        }

        var service = new AiToneLintService(rules, lintText);
        var reportSet = new LintReportSet();
        foreach (var (file, text) in inputs)
            reportSet.Reports.Add(service.Scan(text, file).Filter(onlyIds, minSeverity));
        return reportSet;
    }
}
