namespace TextTool.Services;

/// <summary>
/// lint 编排：多段输入 + 过滤 → 报告集。不读盘、不写盘。
/// </summary>
public static class LintRunner
{
    public static LintReportSet Run(
        IReadOnlyList<(string File, string Text)> inputs,
        IReadOnlyCollection<string>? onlyIds,
        string? minSeverity)
    {
        if (inputs is null || inputs.Count == 0)
            throw new ArgumentException("lint 需要至少一个输入");
        if (minSeverity is not (null or "info" or "warn"))
            throw new ArgumentException("--min-severity 只接受 info 或 warn");
        if (onlyIds is not null && onlyIds.Count == 0)
            throw new ArgumentException("--only 需要至少一个规则 Id");
        if (onlyIds is not null)
        {
            var known = AiToneLintService.AllRuleIds();
            var unknown = onlyIds.Where(id => !known.Contains(id, StringComparer.OrdinalIgnoreCase)).ToList();
            if (unknown.Count > 0)
                throw new ArgumentException($"未知规则 Id：{string.Join(", ", unknown)}");
        }

        var service = new AiToneLintService(LintRuleStore.Load());
        var reportSet = new LintReportSet();
        foreach (var (file, text) in inputs)
            reportSet.Reports.Add(service.Scan(text, file).Filter(onlyIds, minSeverity));
        return reportSet;
    }
}
