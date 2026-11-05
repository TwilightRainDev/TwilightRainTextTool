namespace TextTool.Tests.Services;

/// <summary>
/// 博文结构的遮罩：头部字段、三冒号指令行、代码块、行内代码、公式、链接地址不参与检查与标点替换。
///
/// 两条判据分开钉：哪一段算博文结构（遮罩认得准不准），以及这些区域里的字报不报（调用方接得对不对）。
/// 普通 txt 稿子一个字都不该变，样本里也留了这类对照。
/// </summary>
public class BlogMarkdownTests
{
    private const string 重复标点 = "repeated-punctuation";
    private const string 破折号 = "dash-and-tilde";

    /// <summary>正文的骨架：头部字段加一段正文，各条用例只换中间那截。</summary>
    private static string 博文(string body) => $"---\ntitle: 标题\n---\n\n{body}";

    private static bool 整段被遮(string text, string fragment)
    {
        var mask = BlogMarkdownMask.Create(text);
        Assert.NotNull(mask);
        int start = text.IndexOf(fragment, StringComparison.Ordinal);
        Assert.True(start >= 0, $"样本里找不到「{fragment}」");
        for (int i = start; i < start + fragment.Length; i++)
            if (text[i] is not ('\n' or '\r') && !mask!.IsMasked(i)) return false;
        return true;
    }

    private static bool 有字被遮(string text, string fragment)
    {
        var mask = BlogMarkdownMask.Create(text);
        Assert.NotNull(mask);
        int start = text.IndexOf(fragment, StringComparison.Ordinal);
        Assert.True(start >= 0, $"样本里找不到「{fragment}」");
        for (int i = start; i < start + fragment.Length; i++)
            if (mask!.IsMasked(i)) return true;
        return false;
    }

    private static LintReport 扫(string text, string id) =>
        LintRunner.Run(new[] { ("样本.md", text) }, new[] { id }, null).Reports[0];

    // ---------------------------------------------------------------
    //  遮罩自带的口径
    // ---------------------------------------------------------------

    [Fact]
    public void 头部字段整段被遮_正文不受影响()
    {
        const string text = "---\ntitle: 标题\nexcerpt: 摘要\n---\n\n正文第一段。";

        Assert.True(整段被遮(text, "title: 标题\nexcerpt: 摘要"));
        Assert.False(有字被遮(text, "正文第一段。"));
    }

    [Fact]
    public void 只有分隔线没有键值行的纯文本不算博文()
    {
        // 纯文本也拿 --- 当分隔线，这种稿子遮一遮就等于整段不检查了
        const string text = "---\n正文第一段。\n---\n\n正文第二段。";

        Assert.Null(BlogMarkdownMask.Create(text));
    }

    [Fact]
    public void 三冒号指令行被遮_块里的正文照常算()
    {
        string text = 博文(":::admon[note 提示]\n块里的正文。\n:::\n");

        Assert.True(整段被遮(text, ":::admon[note 提示]"));
        Assert.True(整段被遮(text, ":::"));
        Assert.False(有字被遮(text, "块里的正文。"));
    }

    [Fact]
    public void 围栏代码块整段被遮_行内代码只遮那一截()
    {
        string text = 博文("```csharp\nvar x = 1;\n```\n\n正文用 `code` 举例。");

        Assert.True(整段被遮(text, "```csharp\nvar x = 1;\n```"));
        Assert.True(整段被遮(text, "`code`"));
        Assert.False(有字被遮(text, "正文用"));
    }

    [Fact]
    public void 链接地址与公式被遮_链接文字留着()
    {
        string text = 博文("见 [关于本站](https://twilightrain.com/about/) 与 $E=mc^2$。");

        Assert.True(整段被遮(text, "(https://twilightrain.com/about/)"));
        Assert.True(整段被遮(text, "$E=mc^2$"));
        Assert.False(有字被遮(text, "关于本站"));
    }

    [Fact]
    public void 金额不是公式()
    {
        string text = 博文("一件 5 块，一件 $5 一件 $10。");

        Assert.False(有字被遮(text, "$5 一件 $10"));
    }

    [Fact]
    public void 没有博文结构的稿子不建遮罩()
    {
        const string text = "普通的一段话。\n\n另一段。";

        Assert.Null(BlogMarkdownMask.Create(text));
    }

    // ---------------------------------------------------------------
    //  检查侧：博文结构里的字不报，正文照报
    // ---------------------------------------------------------------

    [Fact]
    public void 头部字段里的重复标点不报()
    {
        var report = 扫("---\ntitle: 标题。。\n---\n\n正文照常。。", 重复标点);

        var hit = Assert.Single(report.Hits);
        Assert.Contains("正文照常", hit.Snippet);
    }

    [Fact]
    public void 三冒号指令行不报_块内正文照报()
    {
        var report = 扫(博文(":::fold[details 折叠。。]\n正文照常。。\n:::\n"), 重复标点);

        var hit = Assert.Single(report.Hits);
        Assert.Contains("正文照常", hit.Snippet);
    }

    [Fact]
    public void 代码块里的字不报()
    {
        var report = 扫(博文("```\n代码里。。\n```\n\n正文照常。。"), 重复标点);

        var hit = Assert.Single(report.Hits);
        Assert.Contains("正文照常", hit.Snippet);
    }

    [Fact]
    public void 链接地址里的连字符不算破折号()
    {
        var report = 扫(博文("见 [关于本站](https://twilight-rain.example/about/) 与 [另一处](a--b)。"), 破折号);

        Assert.Empty(report.Hits);
    }

    [Fact]
    public void 字数只算正文()
    {
        // 头部字段与代码块长短都不动字数：两边各自只改被遮的那一段，正文字数应当相同
        var shortTitle = 扫("---\ntitle: 标题\n---\n\n正文。", 重复标点);
        var longTitle = 扫("---\ntitle: 一个长到离谱的标题，长到把字数撑出几十个字去\n---\n\n正文。", 重复标点);
        var shortCode = 扫("---\ntitle: 标题\n---\n\n```\n短\n```\n\n正文。", 重复标点);
        var longCode = 扫("---\ntitle: 标题\n---\n\n```\n很长很长很长的一段代码放在这里\n```\n\n正文。", 重复标点);

        Assert.Equal(shortTitle.Chars, longTitle.Chars);
        Assert.Equal(shortCode.Chars, longCode.Chars);
    }

    // ---------------------------------------------------------------
    //  替换侧：博文结构原样保留
    // ---------------------------------------------------------------

    [Fact]
    public void 标点替换不动头部字段与代码块()
    {
        const string text = "---\ntitle: 标题。。\n---\n\n```\n代码里。。\n```\n\n正文。。";
        var rules = new List<ReplaceRule> { new() { Find = "。。", Replace = "。" } };

        string replaced = PunctuationReplacer.Apply(text, rules);

        Assert.Contains("title: 标题。。", replaced);
        Assert.Contains("代码里。。", replaced);
        Assert.Contains("正文。", replaced);
    }

    [Fact]
    public void 普通稿子照旧整篇替换()
    {
        const string text = "一句。。\n\n另一句。。";
        var rules = new List<ReplaceRule> { new() { Find = "。。", Replace = "。" } };

        Assert.Equal("一句。\n\n另一句。", PunctuationReplacer.Apply(text, rules));
    }

    [Fact]
    public void 行合并流水线里的替换仍按行处理()
    {
        // 行合并看不到整份稿子的头尾，博文结构遮罩开不了：这条命令的行为一字不变
        using var tf = new TempFile();
        File.WriteAllLines(tf.Path, new[] { "标题。。", "正文" }, Encoding.UTF8);

        var encoding = EncodingDetector.Detect(tf.Path);
        var mergeOpts = new MergeOptions { Threshold = 1, Mode = MergeMode.CharCount };
        var post = new PostProcessOptions(
            FixCjk: false, FixPunct: false, PunctChars: "",
            NoMerge: false, NoMergeChars: "",
            ApplyReplace: true, TrimLeadingComma: false,
            Rules: new List<ReplaceRule> { new() { Find = "。。", Replace = "。" } });

        var result = ProcessingPipeline.Run(tf.Path, encoding.Encoding, mergeOpts, post);

        Assert.Contains("标题。", string.Join("\n", result.Lines));
    }
}
