namespace TextTool.Services;

/// <summary>
/// 基线差分：同一套规则把写前那份也扫一遍，按「规则 Id 加命中词面」计数相减，
/// 只留正文里比基线多出来的位置。
///
/// 读者拿这份报告决定「这次改写要不要动手」。整篇重扫给出的是「稿子里有多少处」，
/// 那个数在写之前就已经那么大，改完看一眼毫无意义；只有多出来的那几处才是这次写进去的。
///
/// 为什么按词面配对而不是按位置配对：写前与写后的行号没有对应关系，一句话挪了位置
/// 不该算成新增又删掉；同一个词多出现一次才是真新增。代价是「把一个词换成同义的另一个」
/// 会被算成删一处、加一处，那是宁可多报的方向。
/// </summary>
public static class LintBaseline
{
    /// <summary>
    /// 基线那份的命中计数：规则 Id 到「命中词面到处数」。同一份稿子扫两遍得到同一张表，
    /// 差分因此只看计数，不看两份报告的相对顺序。
    /// </summary>
    public sealed class Counts
    {
        private readonly Dictionary<string, Dictionary<string, int>> _byRule;

        private Counts(Dictionary<string, Dictionary<string, int>> byRule, int totalHits)
        {
            _byRule = byRule;
            TotalHits = totalHits;
        }

        /// <summary>基线那份的命中总处数，人读报告的第一行要报这个数。</summary>
        public int TotalHits { get; }

        /// <summary>基线里这条规则命中过这个词几次。没命中过是 0。</summary>
        internal int CountOf(string ruleId, string match) =>
            _byRule.TryGetValue(ruleId, out var byMatch) && byMatch.TryGetValue(match, out int count)
                ? count
                : 0;

        /// <summary>基线里出现过的规则 Id。正文一处都不剩时，这些规则仍要给一行新增数。</summary>
        internal IEnumerable<string> RuleIds => _byRule.Keys;

        internal static Counts From(LintReport report)
        {
            var byRule = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
            foreach (var hit in report.Hits)
            {
                if (!byRule.TryGetValue(hit.Id, out var byMatch))
                {
                    byMatch = new Dictionary<string, int>(StringComparer.Ordinal);
                    byRule[hit.Id] = byMatch;
                }
                byMatch[hit.Match] = byMatch.TryGetValue(hit.Match, out int seen) ? seen + 1 : 1;
            }
            return new Counts(byRule, report.Hits.Count);
        }
    }

    /// <summary>数一份报告里的命中，供正文扫描之前先把基线那份过一遍。</summary>
    public static Counts Count(LintReport baselineReport) => Counts.From(baselineReport);

    /// <summary>
    /// 把正文报告裁成「相对基线新增的位置」，并按规则写一行新增数。
    ///
    /// 同一个词在基线里出现过几次，正文里同名词面的前几处就当作存量丢掉，从第几处开始算新增：
    /// 位置没有对应关系，只能按出现次序认账。报告里的 Hits 因此只含新增位置，
    /// 行号仍是正文那份的行号，读者照着改的就是眼前这份稿子。
    /// </summary>
    /// <param name="body">正文报告，就地裁剪。</param>
    /// <param name="baseline">基线计数，由 <see cref="Count"/> 从基线报告得到。</param>
    /// <param name="rules">本次生效的规则表：delta 基准要按规则取门限。</param>
    /// <param name="text">报告文案出处，缺省中文。</param>
    public static void Apply(LintReport body, Counts baseline, IReadOnlyList<LintRule> rules, ILintText? text = null)
    {
        ILintText t = text ?? ChineseLintText.Instance;

        // 差分之前先记下参与比较的规则：删掉一段之后一条都不剩的规则，
        // 报告里仍要出现「新增 0 处」——「这次没变多」与「这条没查」是两件事。
        var involved = new HashSet<string>(
            baseline.RuleIds.Concat(body.Hits.Select(h => h.Id)), StringComparer.OrdinalIgnoreCase);

        var seen = new Dictionary<(string RuleId, string Match), int>();
        var added = new List<LintHit>(body.Hits.Count);
        foreach (var hit in body.Hits)
        {
            var key = (hit.Id, hit.Match);
            int already = seen.TryGetValue(key, out int count) ? count : 0;
            seen[key] = already + 1;
            // 前几处抵掉基线里的存量，剩下的才是这次写进去的
            if (already < baseline.CountOf(hit.Id, hit.Match)) continue;
            added.Add(hit);
        }
        body.Hits = added;

        ApplyDeltaDensity(body, rules);

        body.BaselineHits = baseline.TotalHits;
        foreach (var id in involved.OrderBy(id => id, StringComparer.Ordinal))
        {
            int count = body.Hits.Count(hit => string.Equals(hit.Id, id, StringComparison.OrdinalIgnoreCase));
            body.Notes.Add(new LintNote
            {
                Id = id,
                // 护栏按「第一个整数是该规则的新增处数」读这一行，措辞可以改，数字必须在最前
                Text = t.Get("LintNote_baseline", "相对基线新增 {0} 处", count),
            });
        }
    }

    /// <summary>
    /// delta 基准的门限：净增处数除以写后文本的千字数，不到下限就把这条规则的命中全撤掉。
    /// 分母取写后那份而不是基线那份：要回答的是「现在这份稿子有多密」，
    /// 已经删掉的那部分不该再参与分母。
    /// </summary>
    private static void ApplyDeltaDensity(LintReport body, IReadOnlyList<LintRule> rules)
    {
        foreach (var rule in rules)
        {
            if (rule.EffectiveDensity is not { Basis: LintDensity.BasisDelta } density) continue;
            int added = body.Hits.Count(hit => string.Equals(hit.Id, rule.Id, StringComparison.OrdinalIgnoreCase));
            if (added * 1000.0 / Math.Max(body.Chars, 1) >= density.Min) continue;
            body.Hits.RemoveAll(hit => string.Equals(hit.Id, rule.Id, StringComparison.OrdinalIgnoreCase));
        }
    }
}
