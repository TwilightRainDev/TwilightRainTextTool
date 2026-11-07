namespace TextTool.Tests.Services;

/// <summary>
/// 繁体输入走简体判据：命中的位置与片段必须还是原文的字。
/// </summary>
public class TraditionalChineseTests
{
    private static AiToneLintService Service(params LintRule[] rules) => new(rules);

    /// <summary>装真实规则表的引擎：算法规则也在表里，繁体稿照样要过它们。</summary>
    private static AiToneLintService Engine() => new(LintRuleStore.Load());

    private static LintRule Rule(string id, string kind, params string[] patterns) => new()
    {
        Id = id, Group = "lexis", Title = id, Kind = kind,
        Patterns = patterns.ToList(), Severity = "warn", Detail = "d",
    };

    [Fact]
    public void ToSimplified_简体稿转完还是原样()
    {
        // 简体稿走完一趟必得逐字不变：归一只能动繁体字面，否则判据自己写下的词会被换走。
        var text = "随着人工智能技术的发展，这意味着每个人都要用起来。";

        Assert.Equal(text, TraditionalChinese.ToSimplified(text));
    }

    [Fact]
    public void ToSimplified_繁转简且不改长度()
    {
        var traditional = "隨著人工智能技術的發展，這意味著每個人都要用起來。";

        var converted = TraditionalChinese.ToSimplified(traditional);

        Assert.Equal(traditional.Length, converted.Length);
        Assert.Equal("随着人工智能技术的发展，这意味着每个人都要用起来。", converted);
    }

    [Fact]
    public void ToSimplified_按词分辨著作与穿著()
    {
        // 逐字替换只能读对一半：把「著作」读成「着作」，或把「穿著」留成繁体。
        // 归一是给规则比对用的，这两种错都会让判据在繁体稿上哑火。
        var traditional = "代表著作有少年維特之煩惱；他穿著一件大衣，沿著河走";

        var converted = TraditionalChinese.ToSimplified(traditional);

        Assert.Equal("代表著作有少年维特之烦恼；他穿着一件大衣，沿着河走", converted);
        Assert.Equal(traditional.Length, converted.Length);
    }

    [Fact]
    public void ToSimplified_简体稿里的常用字不被翻成异体()
    {
        // 归一是用词库做的，词库里也收着台湾异体：整篇照搬那套表，「什么」会变成「什幺」、
        // 「抬起下巴」变成「擡起下巴」，含这些字的判据在简体稿上集体失声。
        var simplified = "他抬起下巴，什么也没说，一口吃完了那碗面条，众人都笑了。";

        var converted = TraditionalChinese.ToSimplified(simplified);

        Assert.Equal(simplified, converted);
    }

    [Fact]
    public void ToSimplified_繁体长篇的任意切片转完等长()
    {
        // 命中位置要在归一串与原文之间通用，长度是这条契约的底线。长稿上随便切一段都得等长，
        // 切片起点落在词中间也一样。
        var traditional = TraditionalFixture;
        var random = new Random(20260101);

        for (int i = 0; i < 30; i++)
        {
            int start = random.Next(0, traditional.Length - 20);
            int length = random.Next(20, Math.Min(200, traditional.Length - start));
            var piece = traditional.Substring(start, length);

            Assert.Equal(piece.Length, TraditionalChinese.ToSimplified(piece).Length);
        }
    }

    [Fact]
    public void ToSimplified_含一换二的字的行退回逐字也等长()
    {
        // 「鳽」换出来是两个字符，整行逐字换下来必须回退保留原字，否则这一行之后的位置全错。
        // 同稿其他行不受牵连：词级结果等长的行照旧按词换，不会连带被逐字读错。
        var text = "「也许。听过麻鳽叫吗？」\n代表著作有少年維特之煩惱，他穿著一件大衣。\n";

        var converted = TraditionalChinese.ToSimplified(text);

        Assert.Equal(text.Length, converted.Length);
        Assert.Contains("麻鳽", converted, StringComparison.Ordinal);
        Assert.Contains("代表著作", converted, StringComparison.Ordinal);
        Assert.Contains("穿着一件大衣", converted, StringComparison.Ordinal);
    }

    [Fact]
    public void Scan_繁体稿撞上简体字面的词条()
    {
        var report = Service(Rule("cliche-words", "literal", "这意味着"))
            .Scan("隨著技術的發展，這意味著每個人都要用起來。", "t.txt");

        Assert.Single(report.Hits);
    }

    [Fact]
    public void Scan_繁体稿与简体稿的命中位置一致()
    {
        var rule = Rule("cliche-words", "regex", "随着.{0,10}的发展", "这意味着");
        var traditional = Service(rule).Scan("隨著人工智能技術的發展，這意味著每個人都要用起來。", "t.txt");
        var simplified = Service(rule).Scan("随着人工智能技术的发展，这意味着每个人都要用起来。", "t.txt");

        Assert.Equal(simplified.Hits.Count, traditional.Hits.Count);
        Assert.Equal(
            simplified.Hits.Select(h => (h.Id, h.Line, h.Col, h.Length)),
            traditional.Hits.Select(h => (h.Id, h.Line, h.Col, h.Length)));
    }

    [Fact]
    public void Scan_命中行列与片段都是原文的字()
    {
        var text = "开头一行\n隨著時間的推移，這意味著什麼";
        var report = Service(Rule("cliche-words", "literal", "这意味着")).Scan(text, "t.txt");

        var hit = Assert.Single(report.Hits);
        Assert.Equal(2, hit.Line);
        Assert.Equal(9, hit.Col);
        Assert.Equal(4, hit.Length);
        Assert.Equal("這意味著", hit.Match);
        // 片段是原文切片：报给读者的是他们自己的字，不是归一后的字
        Assert.Contains("這意味著", hit.Snippet, StringComparison.Ordinal);
        Assert.DoesNotContain("这意味着", hit.Snippet, StringComparison.Ordinal);
    }

    [Fact]
    public void Scan_代码里的词表同样认得繁体写法()
    {
        // ordinal-skeleton 的词表写在引擎里（首先|其次|再次|最后），规则表里只有它的元数据，走的是同一条归一
        var report = Engine().Scan("首先準備材料，其次動手，最後收尾。", "t.txt", "doc");

        Assert.Contains(report.Notes, n => n.Id == "ordinal-skeleton");
    }

    [Fact]
    public void Scan_复读串报出原文写法()
    {
        var report = Engine().Scan("他極其極其極其極其安靜。", "t.txt");

        var hit = Assert.Single(report.Hits.Where(h => h.Id == "repeated-token-run"));
        Assert.Equal("極其", hit.Match);
        Assert.Contains("極其", hit.Detail, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------
    //  开头四千字符的繁体探测：简体稿不加载词库、不留副本
    // ---------------------------------------------------------------

    [Fact]
    public void NeedsConversion_简体稿不转()
    {
        Assert.False(TraditionalChinese.NeedsConversion("随着人工智能技术的发展，这意味着每个人都要用起来。"));
    }

    [Fact]
    public void NeedsConversion_归一会原样留着的著词不触发转换()
    {
        // 「著」简体也这么写（著作、著名），字表里没有它；归一会原样留着的几个词
        // 也不该把简体稿推去跑一趟归一。
        const string text = "这部代表著作的作者很著名，著者署名于昭著、巨著、名著、合著之间，着实不易。";

        Assert.False(TraditionalChinese.NeedsConversion(text));
    }

    [Fact]
    public void NeedsConversion_归一读得动的著词照样要转()
    {
        // 「著书」「显著」「论著」这些写法今天会被归一读成「着书」「显着」「论着」，
        // 例外表里故意不收：含它们的稿子照旧跑一趟归一，与以前一字不差。
        // 「代表著」这种把繁体写法带进简体稿的，同样照旧要转——不转就丢了「代表着」那条判据。
        Assert.True(TraditionalChinese.NeedsConversion("他著书立说，这是一个显著的变化，一部论著。"));
        Assert.True(TraditionalChinese.NeedsConversion("他代表著我们这一代人发问。"));
    }

    [Fact]
    public void 简体稿上归一唯一动得了的是著字()
    {
        // 归一对简体稿唯一改得动的是「著」：词库的短语表收的是繁体字面（著書），
        // 简体写法「著书」它认不出，读成「着书」；「著作」「著名」「着装」原样留着，
        // 「土著」被读成「土着」。规则表里没有以「着书」「土着」为字面的判据，
        // 报告上看不出这批差别，简体稿跳过归一因此不改对外结果。这条把边界钉在这里：
        // 归一要是还改得动别的字，跳过归一就成了行为差异。
        const string text = "代表著作有维特之烦恼，著名学者著书立说，着装整洁，土著居民。";

        Assert.Equal(
            "代表著作有维特之烦恼，著名学者着书立说，着装整洁，土着居民。",
            TraditionalChinese.ToSimplified(text));
    }

    [Fact]
    public void NeedsConversion_繁体稿要转()
    {
        Assert.True(TraditionalChinese.NeedsConversion("隨著人工智能技術的發展，這意味著每個人都要用起來。"));
        Assert.True(TraditionalChinese.NeedsConversion(TraditionalFixture));
    }

    [Fact]
    public void NeedsConversion_只看开头四千字符()
    {
        // 边界钉在这里：第 4000 个字符还算，第 4001 个不算。
        string 简体 = new('简', TraditionalChinese.HeadProbeChars - 1);

        Assert.True(TraditionalChinese.NeedsConversion(简体 + "這"));
        Assert.False(TraditionalChinese.NeedsConversion(简体 + "简這"));
    }

    [Fact]
    public void NeedsConversion_归一认得的字一个不漏()
    {
        // 探测表漏一个字，含那个字的稿子就会被当成简体稿不转，判据在它上面整体哑火。
        // 口径：归一真会换掉的每一个字，单独拿出来问，答的必须是「要转」。
        var 漏掉 = new List<string>();
        for (int code = 0; code <= char.MaxValue; code++)
        {
            char c = (char)code;
            if (char.IsSurrogate(c)) continue;
            char 简体 = TraditionalChinese.Simplify(c);
            if (简体 == c) continue;
            if (!TraditionalChinese.NeedsConversion(c.ToString())) 漏掉.Add($"「{c}」→「{简体}」");
        }

        Assert.Empty(漏掉);
    }

    [Fact]
    public void Scan_前四千字符是简体时后面的繁体字不归一()
    {
        // 已知取舍：开头四 KB 里认不出繁体就整份不转，后面那些繁体字面不会被换掉，
        // 按简体写死的词条因此拿它们没辙。这条钉住的是取舍本身，不是缺陷；
        // 真遇上这种排法，把开头一段换成繁体写法就能转。
        string 简体开头 = new('简', TraditionalChinese.HeadProbeChars);
        const string 繁体尾巴 = "隨著技術的發展，這意味著每個人都要用起來。";
        var rule = Rule("cliche-words", "literal", "这意味着");

        Assert.Empty(Service(rule).Scan(简体开头 + 繁体尾巴, "t.txt").Hits);

        // 同一句挪到开头四 KB 以内，就认得出：繁体字本身没有变，变的只是它在哪。
        Assert.Single(Service(rule).Scan(繁体尾巴, "t.txt").Hits);
    }

    /// <summary>
    /// 判据字面里的字都不该是繁体写法：归一之后，判据自己的词若被换掉，那条判据就在繁体稿与简体稿上同时失灵。
    /// 规则表新增用字时，这条先红，就说明那个字在归一里会被换掉。
    /// </summary>
    [Fact]
    public void 规则表的每个字都不是繁体写法()
    {
        var traditional = new List<string>();
        foreach (var rule in LintRuleStore.Load())
            foreach (var pattern in rule.Patterns)
                foreach (var c in pattern.Where(c => TraditionalChinese.Simplify(c) != c))
                    traditional.Add($"{rule.Id}: 「{c}」→「{TraditionalChinese.Simplify(c)}」");

        Assert.Empty(traditional);
    }

    /// <summary>
    /// 繁体段落，取自《儒林外史》的一节。要的是普通长篇的用字密度：換字、詞內外都有，
    /// 长到能随机切片，又不至于把测试拖成人工语料。
    /// </summary>
    private const string TraditionalFixture =
        "擺出酒席，四位模酒論文。直喫到日暮，牛布衣告別，兩公子問明寓處，送了出去。次早，遣家人去邀請魯編修，" +
        "直到日中才來，頭戴紗帽，身穿蟒衣，進了廳事就要進去拜老師神主。兩公子再三辭過，然後寬衣坐下，獻茶。" +
        "茶罷，蘧公孫出來拜見。三公子道，「這是舍表侄，南昌太守家姑丈之孫。」魯編修道，「久慕久慕。」彼此謙讓" +
        "坐下，寒暄已畢，擺上兩席酒來。魯編修道，「老世兄，這個就不是了。你我世交，知已間何必做這些客套。" +
        "依弟愚見，這廳事也太闊落，意欲借尊齋，只須一席酒，我四人促膝談心，方才暢快。」兩公子見這般說，" +
        "竟不違命，當下讓到書房裏。魯編修見瓶，花，爐，幾，位置得宜，不覺怡悅。奉席坐了，公子吩咐一聲叫" +
        "「焚香」，只見一個頭發齊眉的童子，在几上捧了一個古銅香爐出去，隨即兩個管家進來放下暖簾，就出去了。";
}
