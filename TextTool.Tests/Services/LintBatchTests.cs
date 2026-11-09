namespace TextTool.Tests.Services;

/// <summary>
/// 并行扫一批的对外行为：顺序、失败位置、与逐份一致、同时开工的份数。
///
/// 读者按报告顺序对文件，命中日志一行一条——顺序是契约，并行只压缩等待。
/// 这里用真规则表（引擎的建法跟命令行一致），命中取的是规则 Id 与行号，
/// 不比对整份报告：报告里带片段，片段随文案改动，不该让这条用例跟着改。
/// </summary>
public class LintBatchTests
{
    private const string 有命中的稿 = "此外，此外，此外。\n";
    private const string 干净的稿 = "窗口在下午三点开。\n";

    private static LintSession 引擎() => LintSession.Create(null, null, null);

    /// <summary>按路径取稿子的读法；路径不在表里就当读不了，抛的错与真读盘时同一类。</summary>
    private static Func<string, string> 读法(IReadOnlyDictionary<string, string> 稿子) => file =>
       稿子.TryGetValue(file, out string? text) ? text : throw new IOException($"读不了：{file}");

    /// <summary>一次批量跑的结果摘要：(文件, 命中的规则 Id 与行号)，或 (文件, 失败原因开头)。</summary>
    private static List<string> 摘要(IReadOnlyList<string> files, IReadOnlyDictionary<string, string> 稿子, int? workers)
    {
        var lines = new List<string>();
        LintBatch.Run(files, 引擎(), 读法(稿子), item =>
        {
            if (item.Error is not null)
            {
                lines.Add($"{item.File}|失败|{item.Error.Message}");
                return;
            }

            var hits = item.Report!.Hits.Select(h => $"{h.Id}@{h.Line}").OrderBy(s => s, StringComparer.Ordinal);
            lines.Add($"{item.File}|{string.Join(",", hits)}");
        }, workers);
        return lines;
    }

    [Fact]
    public void 结果按输入顺序交回()
    {
        var files = new[] { "a.md", "b.md", "c.md", "d.md", "e.md" };
        var 稿子 = files.ToDictionary(f => f, f => f == "b.md" ? 有命中的稿 : 干净的稿);

        var seen = new List<string>();
        LintBatch.Run(files, 引擎(), 读法(稿子), item => seen.Add(item.File), workers: 4);

        Assert.Equal(files, seen);
    }

    [Fact]
    public void 读不了的那一份带着原因回到原位且其余照常()
    {
        var files = new[] { "a.md", "missing.md", "c.md" };
        var 稿子 = new Dictionary<string, string> { ["a.md"] = 干净的稿, ["c.md"] = 有命中的稿 };

        var items = new List<LintBatch.Item>();
        LintBatch.Run(files, 引擎(), 读法(稿子), items.Add, workers: 4);

        Assert.Equal(files, items.Select(i => i.File).ToArray());
        Assert.Null(items[0].Error);
        Assert.IsType<IOException>(items[1].Error);
        Assert.Null(items[1].Report);
        Assert.Null(items[2].Error);
        Assert.Contains(items[2].Report!.Hits, h => h.Id == "ai-frequent-words");
    }

    [Fact]
    public void 并行与逐份给出同一份结果()
    {
        var files = new[] { "a.md", "b.md", "c.md", "missing.md", "d.md", "e.md" };
        var 稿子 = new Dictionary<string, string>
        {
            ["a.md"] = 有命中的稿,
            ["b.md"] = 干净的稿,
            ["c.md"] = 有命中的稿 + 干净的稿,
            ["d.md"] = 干净的稿,
            ["e.md"] = 有命中的稿,
        };

        Assert.Equal(摘要(files, 稿子, workers: 1), 摘要(files, 稿子, workers: 4));
    }

    /// <summary>
    /// 同时开工的份数：读法里数着「此刻有几份在读」的峰值。
    /// 逐份跑时峰值只能是 1；并行时它要大于 1，且不超过给的窗口。
    /// 靠计数而不是靠掐表：机器快慢不影响这条用例的判定。
    /// </summary>
    [Fact]
    public void 同时开工的份数不超过窗口且多份并行()
    {
        var files = Enumerable.Range(0, 12).Select(i => $"f{i:D2}.md").ToArray();
        var 稿子 = files.ToDictionary(f => f, _ => 干净的稿);
        int 在读 = 0;
        int 峰值 = 0;

        LintBatch.Run(files, 引擎(), file =>
        {
            int now = Interlocked.Increment(ref 在读);
            int seen;
            while ((seen = Volatile.Read(ref 峰值)) < now
                && Interlocked.CompareExchange(ref 峰值, now, seen) != seen) { }
            // 停一会儿，让同时开工的份数真的叠起来；不留停顿的话，
            // 线程池来得及把一份做完再开下一份，这条用例就测不到并行。
            Thread.Sleep(30);
            Interlocked.Decrement(ref 在读);
            return 稿子[file];
        }, _ => { }, workers: 4);

        Assert.True(峰值 > 1, $"峰值只有 {峰值}：这一批是逐份跑的，没有并行。");
        Assert.True(峰值 <= 4, $"峰值 {峰值} 超过窗口 4：在跑的份数没有上限，内存会跟着份数涨。");
    }
}
