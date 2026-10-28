namespace TextTool.Tests.Services;

public class LintRuleStoreTests
{
    private static LintRule Rule(string id, string pattern = "测试") => new()
    {
        Id = id, Group = "lexis", Title = "标题", Kind = "literal",
        Patterns = new List<string> { pattern }, Severity = "info",
    };

    [Fact]
    public void Merge_外部规则按Id覆盖内置_其余保留()
    {
        var builtIn = new List<LintRule> { Rule("cliche-words"), Rule("ai-frequent-words") };
        builtIn[1].Genres = new List<string> { "agent" };
        builtIn[1].BlocksReply = true;
        var external = new List<LintRule> { Rule("ai-frequent-words", "覆盖"), Rule("appended-external-rule") };
        external[0].Genres = new List<string> { "agent" };
        external[0].BlocksReply = false;

        var merged = LintRuleStore.Merge(builtIn, external);

        Assert.Equal(3, merged.Count);
        Assert.Equal(new[] { "cliche-words", "ai-frequent-words", "appended-external-rule" }, merged.Select(r => r.Id));
        Assert.Equal("覆盖", merged[1].Patterns[0]);   // 覆盖发生在原位置
        Assert.False(merged[1].BlocksReply);           // 开关随外部规则一起生效，克隆不漏字段
    }

    [Fact]
    public void Validate_Id为空即抛()
    {
        var rule = Rule("cliche-words");
        rule.Id = "";

        Assert.Throws<ArgumentException>(() => LintRuleStore.Validate(new List<LintRule> { rule }));
    }

    [Fact]
    public void Validate_Id重复即抛()
    {
        var rules = new List<LintRule> { Rule("cliche-words"), Rule("cliche-words") };

        Assert.Throws<ArgumentException>(() => LintRuleStore.Validate(rules));
    }

    [Fact]
    public void Validate_Id仅差大小写即抛()
    {
        // 口径与 ValidateNoAlgorithmIdCollision 及下游 --only / Filter 一致：Id 大小写不敏感，
        // 否则校验放过、下游把它们塌缩成同一个，重复 Id 一直到运行期才暴露
        var rules = new List<LintRule> { Rule("cliche-words"), Rule("CLICHE-WORDS") };

        Assert.Throws<ArgumentException>(() => LintRuleStore.Validate(rules));
    }

    [Fact]
    public void Validate_正则语法错即抛()
    {
        var rule = Rule("repeated-punctuation", "([a-z]");
        rule.Kind = "regex";

        var ex = Assert.Throws<ArgumentException>(() => LintRuleStore.Validate(new List<LintRule> { rule }));
        Assert.Contains("repeated-punctuation", ex.Message);
    }

    [Fact]
    public void Validate_Hints长度与Patterns不匹配即抛()
    {
        var rule = Rule("filler-phrases");
        rule.Patterns.Add("第二条");
        rule.Hints = new List<string> { "只有一条" };

        Assert.Throws<ArgumentException>(() => LintRuleStore.Validate(new List<LintRule> { rule }));
    }

    [Fact]
    public void Validate_Patterns为null即抛而非NRE()
    {
        var rule = Rule("cliche-words");
        rule.Patterns = null!;   // JSON 里的 "Patterns": null 会走到这里

        var ex = Assert.Throws<ArgumentException>(() => LintRuleStore.Validate(new List<LintRule> { rule }));
        Assert.Contains("Patterns", ex.Message);
    }

    [Fact]
    public void Validate_Patterns含空串即抛()
    {
        var rule = Rule("cliche-words", "");   // 空串模式会匹配任意位置，是配置错误

        Assert.Throws<ArgumentException>(() => LintRuleStore.Validate(new List<LintRule> { rule }));
    }

    [Fact]
    public void GetDefaultRules_数据规则49条且顺序完整()
    {
        var ids = LintRuleStore.GetDefaultRules().Select(r => r.Id).ToArray();

        // 顺序即分组：通用规则在前，小说档十条、智能体档九条垫后。档位是过滤器，规则全表只有这一份。
        Assert.Equal(
            new[] { "repeated-punctuation", "ellipsis-style", "mixed-punctuation-width", "dash-and-tilde", "tilde-run", "invisible-characters", "emoji-and-symbols",
                    "cliche-words", "ai-frequent-words", "vague-attribution", "training-cutoff-disclaimer", "chatbot-artifacts", "sycophancy", "filler-phrases",
                    "parallel-contrast", "paragraph-ordinals", "elevated-ending", "stacked-modifiers", "copula-avoidance", "inline-title-list", "forced-reversal", "accounting-cliche",
                    "adverb-overuse", "violent-verbs", "hedging-ending", "anthropomorphic-subject", "doc-process-residue", "assistant-persona", "meta-commentary-marker",
                    "fiction-body-tics", "fiction-filler-modifiers", "fiction-dead-metaphor", "fiction-emotion-telling", "fiction-vague-nominalization", "fiction-vague-simile", "synopsis-formula",
                    "trailer-ending", "outline-residue", "synopsis-register", "connective-scene-shift",
                    "agent-commit-hash", "agent-code-list", "agent-path-list", "agent-cli-option",
                    "agent-smoke-jargon", "agent-test-jargon", "agent-process-jargon", "agent-inline-code",
                    "agent-truncated-path" },
            ids);
    }

    [Fact]
    public void GetDefaultRules_非默认字段真的绑定()
    {
        // Validate 不拦未知键：JSON 里把 MinCount 拼成 Mincount，值会静默回落到模型默认
        // （MinCount=1、TailChars=null、Hints=null），规则行为随数据变而无人报错。
        // 这里把四个非默认字段的实际取值钉在数据上。
        var rules = LintRuleStore.GetDefaultRules().ToDictionary(r => r.Id);

        Assert.Equal(3, rules["ai-frequent-words"].MinCount);

        Assert.Equal("paragraph", rules["paragraph-ordinals"].Scope);
        Assert.Equal(2, rules["paragraph-ordinals"].MinCount);

        Assert.Equal("paragraph", rules["elevated-ending"].Scope);
        Assert.Equal(30, rules["elevated-ending"].TailChars);

        // 预告句只在段末 120 字里判：预警要的是收尾那几句，段中偶尔出现不算。
        Assert.Equal("paragraph", rules["trailer-ending"].Scope);
        Assert.Equal(120, rules["trailer-ending"].TailChars);
        // 摘要动词与连接词按次数判：单次「探索」是正常用词，凑够两次才是骨架。
        Assert.Equal(2, rules["synopsis-register"].MinCount);
        Assert.Equal(2, rules["connective-scene-shift"].MinCount);

        Assert.Equal(
            new[] { "为了实现这一点", "因为下雨", "现在", "如果您需要帮助", "系统可以处理", "数据显示" },
            rules["filler-phrases"].Hints!.ToArray());

        // 档位：通用规则留空，小说与智能体规则只属于自己的档。留空是「各档都跑」的意思，
        // 写成 ["doc"] 会让它在小说档与智能体档里静默消失。
        Assert.Empty(rules["cliche-words"].Genres);
        // 这两条在真实小说里 85% 以上命中是正常用词（获得、展示、文学定语），收回文档档
        Assert.Equal(new[] { "doc" }, rules["ai-frequent-words"].Genres);
        Assert.Equal(new[] { "doc" }, rules["stacked-modifiers"].Genres);
        // 真人小说每万字 2.1 处、AI 小说 0 处：段末升华在小说档只制造噪声
        Assert.Equal(new[] { "doc" }, rules["elevated-ending"].Genres);
        Assert.Empty(rules["adverb-overuse"].Genres);
        Assert.Empty(rules["violent-verbs"].Genres);
        Assert.Equal(new[] { "fiction" }, rules["fiction-body-tics"].Genres);
        Assert.Equal(new[] { "fiction" }, rules["fiction-emotion-telling"].Genres);
        Assert.Equal(new[] { "fiction" }, rules["fiction-vague-nominalization"].Genres);
        Assert.Equal(new[] { "doc" }, rules["anthropomorphic-subject"].Genres);

        // 能不能打断：只有裸代号与截断路径标了，其余按默认不打断
        Assert.True(rules["agent-code-list"].BlocksReply);
        Assert.True(rules["agent-truncated-path"].BlocksReply);
        Assert.False(rules["emoji-and-symbols"].BlocksReply);
        Assert.False(rules["agent-smoke-jargon"].BlocksReply);

        // 智能体档：四条按有无判，四条按段内密度判；档位只属于智能体档
        Assert.All(
            new[]
            {
                "agent-commit-hash", "agent-code-list", "agent-path-list", "agent-cli-option",
                "agent-smoke-jargon", "agent-test-jargon", "agent-process-jargon", "agent-inline-code",
            },
            id => Assert.Equal(new[] { "agent" }, rules[id].Genres));
        Assert.Equal("document", rules["agent-commit-hash"].Scope);
        Assert.Equal("paragraph", rules["agent-code-list"].Scope);
        Assert.Equal(2, rules["agent-code-list"].MinCount);
        Assert.Equal(2, rules["agent-path-list"].MinCount);
        Assert.Equal(2, rules["agent-test-jargon"].MinCount);
        Assert.Equal(5, rules["agent-inline-code"].MinCount);

        // 段末兜底与段末升华是同一条赛道的两端：同一 Scope、同一截取长度
        Assert.Equal("paragraph", rules["hedging-ending"].Scope);
        Assert.Equal(30, rules["hedging-ending"].TailChars);

        // 两条重复判定都靠「同段凑够次数」，阈值钉在数据上
        Assert.Equal("paragraph", rules["adverb-overuse"].Scope);
        Assert.Equal(2, rules["adverb-overuse"].MinCount);
        Assert.Equal("paragraph", rules["fiction-filler-modifiers"].Scope);
        Assert.Equal(3, rules["fiction-filler-modifiers"].MinCount);
    }

    [Fact]
    public void Validate_Genres取值非法即抛()
    {
        // 档位拼错（doc\uaced 之类）会让规则在所有档位下静默不跑，与 Id 撞码同类，必须在加载期报错
        var rule = Rule("fiction-body-tics");
        rule.Genres = new List<string> { "novel" };

        var ex = Assert.Throws<ArgumentException>(() => LintRuleStore.Validate(new List<LintRule> { rule }));
        Assert.Contains("novel", ex.Message);
    }

    [Fact]
    public void Validate_文档专属判据标可打断即抛()
    {
        // 实时闸只跑智能体档（含通用判据），文档专属判据上的这个值永远读不到，是配置错
        var rule = Rule("doc-process-residue");
        rule.Genres = new List<string> { "doc" };
        rule.BlocksReply = true;

        var ex = Assert.Throws<ArgumentException>(() => LintRuleStore.Validate(new List<LintRule> { rule }));
        Assert.Contains("agent", ex.Message);
    }

    [Fact]
    public void Validate_通用判据可标可打断()
    {
        // 留空是「各档都跑」，实时闸会读到它
        var rule = Rule("violent-verbs");
        rule.BlocksReply = true;

        LintRuleStore.Validate(new List<LintRule> { rule });
    }

    [Fact]
    public void Load_内置规则集可加载且通过校验()
    {
        var builtIn = LintRuleStore.GetDefaultRules();
        var rules = LintRuleStore.Load();

        // 计数不钉 21：Load 读的是程序目录的 lint_rules.json（装过方案的机器上可能有它），
        // 钉死计数就把这条用例变成环境耦合。内置 21 条的精确清单由
        // GetDefaultRules_数据规则21条且顺序完整 在资源层钉住，与目录内容无关。
        Assert.NotEmpty(builtIn);
        Assert.All(builtIn, b => Assert.Contains(rules, r => r.Id == b.Id));
        Assert.All(rules, r =>
        {
            Assert.False(string.IsNullOrWhiteSpace(r.Title));
            Assert.False(string.IsNullOrWhiteSpace(r.Detail));
        });
    }

    [Fact]
    public void GetDefaultRules_literal规则的Patterns不含反斜杠()
    {
        // literal 走 Regex.Escape：JSON 里写的 \uXXXX 解码后是六个普通字符，
        // 被 Escape 再转义一次后永远匹配不到真实字符——这类规则必须写成 regex
        var literal = LintRuleStore.GetDefaultRules().Where(r => r.Kind == "literal").ToList();

        // literal 规则集为空时下面的 Assert.Empty 必然空过，先把「有可查对象」钉住
        Assert.NotEmpty(literal);

        var offenders = literal
            .SelectMany(r => r.Patterns.Select(p => (r.Id, Pattern: p)))
            .Where(x => x.Pattern.Contains('\\'))
            .Select(x => x.Id)
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Merge_同Id外部规则出现两次不抛且以最后一次为准()
    {
        var builtIn = new List<LintRule> { Rule("cliche-words") };
        var external = new List<LintRule> { Rule("cliche-words", "第一次"), Rule("cliche-words", "第二次") };

        // Load() 会先被 Validate(external) 挡住，但 Merge 自身也必须把重复 Id 落到同一位置：
        // 索引存下标后第二次进来仍能命中，不再有 IndexOf 返回 -1 的坑
        var merged = LintRuleStore.Merge(builtIn, external);

        Assert.Single(merged);
        Assert.Equal("第二次", merged[0].Patterns[0]);
    }

    [Fact]
    public void Merge_大小写不同的同Id覆盖原位置()
    {
        var builtIn = new List<LintRule> { Rule("cliche-words"), Rule("ai-frequent-words") };
        var external = new List<LintRule> { Rule("CLICHE-WORDS", "覆盖") };

        var merged = LintRuleStore.Merge(builtIn, external);

        Assert.Equal(2, merged.Count);
        Assert.Equal(new[] { "CLICHE-WORDS", "ai-frequent-words" }, merged.Select(r => r.Id));
        Assert.Equal("覆盖", merged[0].Patterns[0]);
    }

    [Fact]
    public void Merge_结果不与任一侧入参共享引用()
    {
        var builtIn = new List<LintRule> { Rule("cliche-words") };
        var overwritten = Rule("cliche-words", "覆盖");
        var appended = Rule("appended-external-rule");
        var external = new List<LintRule> { overwritten, appended };

        var merged = LintRuleStore.Merge(builtIn, external);

        Assert.NotSame(builtIn[0], merged[0]);
        Assert.NotSame(overwritten, merged[0]);
        Assert.NotSame(appended, merged[1]);
    }

    [Fact]
    public void Validate_列表含null元素即抛而非NRE()
    {
        var rules = new List<LintRule> { Rule("cliche-words"), null! };

        var ex = Assert.Throws<ArgumentException>(() => LintRuleStore.Validate(rules));

        Assert.Contains("null", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateNoAlgorithmIdCollision_外部Id撞算法Id即抛()
    {
        var external = new List<LintRule> { Rule("mixed-quote-style") };

        // 直接测守卫本身，不往程序目录写 lint_rules.json：那个文件会被并行跑的其它测试类读到
        var ex = Assert.Throws<ArgumentException>(() => LintRuleStore.ValidateNoAlgorithmIdCollision(external));

        Assert.Contains("mixed-quote-style", ex.Message);
    }

    [Fact]
    public void ValidateNoAlgorithmIdCollision_数据规则Id放行()
    {
        LintRuleStore.ValidateNoAlgorithmIdCollision(new List<LintRule> { Rule("cliche-words"), Rule("X9") });
    }
}
