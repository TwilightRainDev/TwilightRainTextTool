namespace TextTool.Tests.Services;

/// <summary>
/// 算法规则与规则表的接缝：一条规则只在规则表里写清楚，引擎就该跑到它，元数据也全部取自表里。
///
/// 钉住四件事：
/// 1. 表里挂一条算法规则就能跑，不需要改代码里的名单；
/// 2. 命中与附注的分组、严重程度、标题、说明取自规则表，不是代码里另写的一份；
/// 3. 适用档位同样取自规则表：空列表各档通用，写了档位就只在那些档跑，不给档位一律跑；
/// 4. 算法名写错、没写、或给算法规则写了模式，加载期就报，报错点名是哪条规则。
/// </summary>
public class AlgorithmRuleTableTests
{
    private static LintRule 内置规则(string id) =>
        LintRuleStore.GetDefaultRules().Single(rule => rule.Id == id).Clone();

    private static LintReport 扫(string text, string? genre = null) =>
        new AiToneLintService(LintRuleStore.GetDefaultRules()).Scan(text, "样本.txt", genre);

    private const string 引号混用 = "他说「好」，又说\"行\"。";

    private const string 叠喻句 =
        "说话像一颗石子投入湖中泛起一丝涟漪就像一根针扎破气球令我瞬间泄气放弃了思考"
        + "使我的手紧紧握住指节因为用力而泛白犹如一柄器械精准地划开我所有伪装"
        + "又将皮球踢给了我让我像受惊的小兽一般发出悲鸣和低沉的嘶吼"
        + "我的声音像砂纸打磨玻璃如同石头一般重重压得我喘不过气";

    private const string 术语稿 =
        "文明的褶皱里藏着熵增的递归，镜像是拓扑的量子纠缠；琥珀把星轨封进晶格，"
        + "超新星在坍缩里写十四行诗；镜像再一次递归，褶皱再一次熵增。";

    private const string 序数稿 = "首先看输入，其次看输出，最后看结果。";

    private const string 硬连稿 =
        "前句我没想清：星、叉、依赖，行；但拿去做魔改、二开，不行；这套你留，不开——"
        + "前句我收，这一刀，我认——议里那先挂着——都这么久了。";

    /// <summary>
    /// 只把这一条规则交给引擎：表里有它就跑，表里没有它就不跑。
    /// 「只在规则表里写清楚」这件事因此是能被验证的，不再依赖代码里另有一行登记。
    /// </summary>
    [Fact]
    public void 只在规则表里写一条算法规则就能跑到()
    {
        var rule = 内置规则("mixed-quote-style");

        var 只带这一条 = new AiToneLintService(new[] { rule }).Scan(引号混用, "样本.txt");
        var hit = Assert.Single(只带这一条.Hits);
        Assert.Equal("mixed-quote-style", hit.Id);

        // 规则表里没有它时，同一条文本不该报出这个 Id：跑不跑由表决定，不由代码决定
        var 空表 = new AiToneLintService(Array.Empty<LintRule>()).Scan(引号混用, "样本.txt");
        Assert.DoesNotContain(空表.Hits, h => h.Id == "mixed-quote-style");
    }

    /// <summary>分组、严重程度、标题都取自规则表：把表里那条改掉，命中跟着变。</summary>
    [Fact]
    public void 命中的分组严重程度与标题取自规则表()
    {
        var rule = 内置规则("mixed-quote-style");
        rule.Group = "自定分组";
        rule.Severity = "warn";
        rule.Title = "改过的标题";

        var hit = Assert.Single(new AiToneLintService(new[] { rule }).Scan(引号混用, "样本.txt").Hits);

        Assert.Equal("自定分组", hit.Group);
        Assert.Equal("warn", hit.Severity);
        Assert.Equal("改过的标题", hit.Title);
    }

    /// <summary>说明是规则表里的模板，算法只往占位符里填数。</summary>
    [Fact]
    public void 命中的说明取自规则表里的模板()
    {
        var rule = 内置规则("repeated-token-run");
        rule.Detail = "自定义模板：「{0}」写了 {1} 遍";

        var hit = Assert.Single(new AiToneLintService(new[] { rule })
            .Scan("他极其极其极其极其安静。", "样本.txt").Hits);

        Assert.Equal("自定义模板：「极其」写了 4 遍", hit.Detail);
    }

    /// <summary>统计附注的 Id 与文案同样跟着规则表走：附注没有分组与严重程度，只有 Id 与说明。</summary>
    [Fact]
    public void 统计附注的说明取自规则表里的模板()
    {
        var rule = 内置规则("ordinal-skeleton");
        rule.Detail = "自定义附注：序数词 {0} 处";

        var note = Assert.Single(new AiToneLintService(new[] { rule }).Scan(序数稿, "样本.txt").Notes);

        Assert.Equal("ordinal-skeleton", note.Id);
        Assert.Equal("自定义附注：序数词 3 处", note.Text);
    }

    /// <summary>
    /// 四种跑法下这四条受限规则的适用关系。不给档位时引擎不按档位过滤，
    /// 所以「写小说」与「除文档与智能体回复以外都跑」在四种跑法上等价——这一条在这里实测，不靠推理。
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("doc")]
    [InlineData("fiction")]
    [InlineData("agent")]
    public void 受限规则只在规则表写的那些档位跑(string? genre)
    {
        bool fiction = genre is null or "fiction";           // 小说档与不给档位
        bool docOrAgent = genre is null or "doc" or "agent"; // 文档档、智能体档与不给档位

        var report = 扫(叠喻句, genre);
        Assert.Equal(fiction, report.Hits.Any(h => h.Id == "fiction-simile-pile"));

        report = 扫(术语稿, genre);
        Assert.Equal(fiction, report.Hits.Any(h => h.Id == "terminology-pile"));

        report = 扫(序数稿, genre);
        Assert.Equal(docOrAgent, report.Notes.Any(n => n.Id == "ordinal-skeleton"));

        report = 扫(硬连稿, genre);
        Assert.Equal(docOrAgent, report.Hits.Any(h => h.Id == "choppy-clauses"));
    }

    /// <summary>其余算法规则在规则表里留空档位：那是「各档通用」，不是「哪一档都不跑」。</summary>
    [Fact]
    public void 其余算法规则在规则表里各档通用()
    {
        string[] 受限 = { "fiction-simile-pile", "terminology-pile", "ordinal-skeleton", "choppy-clauses" };

        var 通用 = LintRuleStore.GetDefaultRules()
            .Where(rule => rule.Kind == LintRule.KindAlgorithm)
            .Where(rule => !受限.Contains(rule.Id))
            .ToList();

        Assert.NotEmpty(通用);
        Assert.All(通用, rule => Assert.Empty(rule.Genres));
    }

    /// <summary>算法名写错必须在加载期报，且要能看出是哪条规则：运行期沉默等于「这条永远不报」。</summary>
    [Fact]
    public void 算法名写错加载即报()
    {
        var rule = 内置规则("mixed-quote-style");
        rule.AlgorithmName = "no-such-algorithm";

        var ex = Assert.Throws<ArgumentException>(() => LintRuleStore.Validate(new List<LintRule> { rule }));

        Assert.Contains("mixed-quote-style", ex.Message);
        Assert.Contains("no-such-algorithm", ex.Message);
    }

    [Fact]
    public void 算法规则没写算法名即报()
    {
        var rule = 内置规则("mixed-quote-style");
        rule.AlgorithmName = null;

        var ex = Assert.Throws<ArgumentException>(() => LintRuleStore.Validate(new List<LintRule> { rule }));

        Assert.Contains("mixed-quote-style", ex.Message);
        Assert.Contains("AlgorithmName", ex.Message);
    }

    [Fact]
    public void 算法规则写了模式即报()
    {
        var rule = 内置规则("mixed-quote-style");
        rule.Patterns = new List<string> { "引号" };

        var ex = Assert.Throws<ArgumentException>(() => LintRuleStore.Validate(new List<LintRule> { rule }));

        Assert.Contains("mixed-quote-style", ex.Message);
        Assert.Contains("Patterns", ex.Message);
    }

    [Fact]
    public void 算法规则写了建议条数即报()
    {
        var rule = 内置规则("mixed-quote-style");
        rule.Hints = new List<string> { "把引号统一" };

        var ex = Assert.Throws<ArgumentException>(() => LintRuleStore.Validate(new List<LintRule> { rule }));

        Assert.Contains("mixed-quote-style", ex.Message);
        Assert.Contains("Hints", ex.Message);
    }

    [Fact]
    public void 普通规则写了算法名即报()
    {
        var rule = new LintRule
        {
            Id = "user-test", Group = "lexis", Title = "标题", Kind = LintRule.KindLiteral,
            Patterns = new List<string> { "测试" }, AlgorithmName = "mixed-quote-style",
        };

        var ex = Assert.Throws<ArgumentException>(() => LintRuleStore.Validate(new List<LintRule> { rule }));

        Assert.Contains("user-test", ex.Message);
        Assert.Contains("算法名", ex.Message);
    }
}
