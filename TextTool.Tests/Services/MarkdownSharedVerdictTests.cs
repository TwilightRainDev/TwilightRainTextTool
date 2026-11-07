namespace TextTool.Tests.Services;

/// <summary>
/// 三种结构形状在三个调用方之间的口径一致：同一份输入，围栏、列表行、头部字段的判定
/// 都由 <see cref="MarkdownLines"/> 与 <see cref="MarkdownFenceTracker"/> 给出，三处叫出来的结论对得上。
///
/// 三处各自的用法不同，所以断言盯的是「对不上的地方」：
///   博文遮罩    这些字要不要遮；
///   段落切分    这一段是不是单独成段、进不进正文；
///   回复检查    围栏里整块跳过，围栏外逐行扫。
/// </summary>
public class MarkdownSharedVerdictTests
{
    private const string 裸代号 = "agent-code-list";

    private static ReplyShapeScanner Scanner() => new(LintRuleStore.GetDefaultRules());

    private static List<LintHit> 命中(string text) =>
        LintRunner.Run(new[] { ("样本.md", text) }, new[] { 裸代号 }, null).Reports[0].Hits.ToList();

    /// <summary>逐行喂给共享件，拿到「这一行是围栏行吗」与「喂完这行围栏开着吗」。</summary>
    private static (bool 围栏行, bool 围栏开着)[] 围栏判定(string text)
    {
        var fence = new MarkdownFenceTracker();
        return text.Split('\n').Select(raw =>
        {
            bool isFenceLine = fence.Advance(raw);
            return (isFenceLine, fence.InFence);
        }).ToArray();
    }

    private static int 行首下标(string text, int line)
    {
        int offset = 0;
        for (int i = 0; i < line; i++)
            offset = text.IndexOf('\n', offset) + 1;
        return offset;
    }

    /// <summary>这一行整行是否都被遮住（行内至少有一个字被遮即算）。</summary>
    private static bool 整行被遮(BlogMarkdownMask mask, string text, int line)
    {
        int start = 行首下标(text, line);
        int end = text.IndexOf('\n', start) is int newline && newline >= 0 ? newline : text.Length;
        for (int i = start; i < end; i++)
            if (!char.IsWhiteSpace(text[i]) && mask.IsMasked(i)) return true;
        return false;
    }

    private static List<string> 段落文本(BlogMarkdownMask mask, string text) =>
        AiToneLintService.Paragraphs(mask.Apply(text), "markdown").Select(p => p.Text).ToList();

    [Fact]
    public void 围栏形状_三处结论对得上()
    {
        const string text =
            "---\ntitle: 标题\ndate: 2026-03-04\n---\n\n正文开头。\n\n```\nS1 在围栏里\n```\n\n正文结尾，S2 在这里。";
        const int 围栏开门 = 7, 围栏里 = 8, 围栏关门 = 9;

        var fence = 围栏判定(text);
        Assert.True(fence[围栏开门].围栏行);
        Assert.True(fence[围栏里].围栏开着);
        Assert.False(fence[围栏里].围栏行);
        Assert.True(fence[围栏关门].围栏行);
        Assert.False(fence[围栏关门].围栏开着);

        var mask = BlogMarkdownMask.Create(text);
        Assert.True(mask.IsBlogPost);
        Assert.True(整行被遮(mask, text, 围栏开门));
        Assert.True(整行被遮(mask, text, 围栏里));
        Assert.True(整行被遮(mask, text, 围栏关门));

        var paragraphs = 段落文本(mask, text);
        Assert.Contains("正文开头。", paragraphs);
        Assert.Contains("正文结尾，S2 在这里。", paragraphs);
        Assert.DoesNotContain(paragraphs, p => p.Contains("S1", StringComparison.Ordinal));

        var hits = Scanner().Scan(text);
        Assert.Equal(new[] { "S2" }, hits.Select(h => h.Token));
        Assert.Equal(12, hits[0].Line);
    }

    [Fact]
    public void 列表行形状_三处结论对得上()
    {
        const string text = "---\ntitle: 标题\ndate: 2026-03-04\n---\n\n- 第一项 S1\n- 第二项\n\n正文收尾。";
        const int 第一项 = 5, 第二项 = 6;

        Assert.True(MarkdownLines.IsListItem(text.Split('\n')[第一项]));
        Assert.True(MarkdownLines.IsListItem(text.Split('\n')[第二项]));
        Assert.False(围栏判定(text)[第一项].围栏行);

        var mask = BlogMarkdownMask.Create(text);
        Assert.False(整行被遮(mask, text, 第一项));
        Assert.False(整行被遮(mask, text, 第二项));

        // 列表项各自成段：并进散文段会让「段内序数词」跨条目误报
        var paragraphs = 段落文本(mask, text);
        Assert.Contains("- 第一项 S1", paragraphs);
        Assert.Contains("- 第二项", paragraphs);
        Assert.Contains("正文收尾。", paragraphs);

        var hits = Scanner().Scan(text);
        Assert.Equal(new[] { "S1" }, hits.Select(h => h.Token));
        Assert.Equal(6, hits[0].Line);
    }

    [Fact]
    public void 头部字段形状_三处结论对得上()
    {
        const string text = "---\ntitle: S1 标题\ndate: 2026-03-04\n---\n\n正文里 S2 在这里。";
        var lines = text.Split('\n');

        Assert.True(MarkdownLines.IsFrontMatterOpening(lines[0].Trim()));
        Assert.True(MarkdownLines.IsFrontMatterKey(lines[1].Trim(), "title"));
        Assert.True(MarkdownLines.IsFrontMatterKey(lines[2].Trim(), "date"));
        Assert.True(MarkdownLines.IsFrontMatterClosing(lines[3].Trim()));
        Assert.True(MarkdownLines.LooksLikeFrontMatter(text, 2));
        // 头部字段不是围栏：回复检查不该把这一段当代码块吞掉
        Assert.All(围栏判定(text), f => Assert.False(f.围栏行));

        var mask = BlogMarkdownMask.Create(text);
        Assert.True(mask.IsBlogPost);
        for (int line = 0; line <= 3; line++)
            Assert.True(整行被遮(mask, text, line), $"第 {line + 1} 行属于头部字段，应当被遮");

        var paragraphs = 段落文本(mask, text);
        Assert.Equal(new[] { "正文里 S2 在这里。" }, paragraphs);

        // 回复里没有头部字段这回事：那几行按正文扫，命中照报
        var hits = Scanner().Scan(text);
        Assert.Equal(new[] { "S1", "S2" }, hits.Select(h => h.Token));
        Assert.Equal(new[] { 2, 6 }, hits.Select(h => h.Line));
    }

    [Fact]
    public void 四反引号块里的三反引号_三处都算块内()
    {
        // 四个反引号开的块，三个反引号关不掉：块里的内容一直算代码，三处结论一致。
        // 这一条以前钉的是「两处口径不同」，并轨之后改成钉一致。
        const string text = "````\n```\nS1 在四反引号块里\n```\n````\n\n正文里 S2。";
        const string 博文 = "---\ntitle: 标题\ndate: 2026-03-04\n---\n\n" + text;

        var fence = 围栏判定(text);
        Assert.True(fence[2].围栏开着);
        Assert.False(fence[4].围栏开着);

        var mask = BlogMarkdownMask.Create(博文);
        Assert.True(整行被遮(mask, 博文, 7));
        Assert.False(整行被遮(mask, 博文, 11));

        // 块内那行三反引号不关门，S1 整块跳过；块外的 S2 照扫
        var hits = Scanner().Scan(text);
        Assert.DoesNotContain(hits, h => h.Token == "S1");
        Assert.Contains(hits, h => h.Token == "S2" && h.Line == 7);
    }
}
