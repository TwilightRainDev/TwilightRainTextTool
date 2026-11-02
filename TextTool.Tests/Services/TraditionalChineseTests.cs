namespace TextTool.Tests.Services;

/// <summary>
/// 繁体输入走简体判据：命中的位置与片段必须还是原文的字。
/// </summary>
public class TraditionalChineseTests
{
    private static AiToneLintService Service(params LintRule[] rules) => new(rules);

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
        // ordinal-skeleton 的词表写在引擎里（首先|其次|再次|最后），不在规则表，走的是同一条归一
        var report = Service().Scan("首先準備材料，其次動手，最後收尾。", "t.txt", "doc");

        Assert.Contains(report.Notes, n => n.Id == "ordinal-skeleton");
    }

    [Fact]
    public void Scan_复读串报出原文写法()
    {
        var report = Service().Scan("他極其極其極其極其安靜。", "t.txt");

        var hit = Assert.Single(report.Hits.Where(h => h.Id == "repeated-token-run"));
        Assert.Equal("極其", hit.Match);
        Assert.Contains("極其", hit.Detail, StringComparison.Ordinal);
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
