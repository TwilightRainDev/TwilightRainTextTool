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
    public void GetDefaultRules_全部规则与顺序固定()
    {
        var ids = LintRuleStore.GetDefaultRules().Select(r => r.Id).ToArray();

        // 顺序即分组：通用规则在前，小说与智能体的专属规则垫后。档位是过滤器，规则全表只有这一份。
        // 条数不写进名字也不写进注释：加一条规则就要改一处数字，而中文数字又搜不到。
        Assert.Equal(
            new[] { "repeated-punctuation", "ellipsis-style", "mixed-punctuation-width", "dash-and-tilde", "dash-run", "dash-pause-in-reply", "tilde-run", "invisible-characters", "emoji-and-symbols", "foreign-script-mix", "char-face",
                    "cliche-words", "deck-speak", "ai-frequent-words", "vague-attribution", "training-cutoff-disclaimer", "chatbot-artifacts", "sycophancy", "filler-phrases",
                    "parallel-contrast", "triadic-parallel", "paragraph-ordinals", "elevated-ending", "stacked-modifiers", "fiction-modifier-chain", "copula-avoidance", "inline-title-list", "forced-reversal", "accounting-cliche",
                    "adverb-overuse", "violent-verbs", "hedging-ending", "disclaimer-stack", "hedge-denial-pile", "anthropomorphic-subject", "doc-process-residue",
                    "no-debt-declaration", "completion-log", "supersession", "tombstone-of-deletion", "re-report-suppression",
                    "assistant-persona", "meta-commentary-marker",
                    "fiction-body-tics", "fiction-physio-tics", "fiction-permission-consoling", "fiction-choice-diagnosis", "fiction-beat-action", "fiction-sensation-tics", "fiction-synonym-chain", "fiction-atmosphere-props", "fiction-dominant-register",
                    "fiction-filler-modifiers", "fiction-dead-metaphor", "fiction-emotion-telling", "fiction-vague-nominalization", "fiction-vague-simile", "fiction-community-taste", "fiction-proportion-eyes",
                    "fiction-text-self-reference", "fiction-dialogue-reference", "fiction-narrator-formula", "fiction-metaphor-lexicon", "latin-word-in-cjk",
                    "fiction-bold-template", "fiction-machine-numbers", "synopsis-formula",
                    "trailer-ending", "outline-residue", "synopsis-register", "connective-scene-shift",
                    "agent-commit-hash", "agent-code-list", "agent-path-list", "agent-cli-option",
                    "agent-smoke-jargon", "agent-test-jargon", "agent-process-jargon", "agent-inline-code",
                    "agent-truncated-path", "numbered-reference", "agent-conclusion-first", "agent-permission-ask",
                    "agent-one-char-commitment", "agent-agreement-opener", "agent-deliberation-leak", "agent-reassurance-pile", "agent-self-intro", "agent-error-paste", "agent-flattery", "agent-conclusion-preamble" },
            ids);
    }

    [Fact]
    public void Validate_Patterns与字面字段都为空即抛()
    {
        // 一条规则两种模式都不给就是空判据：它永远不报，却占着规则表的位置，加载期就该拦下
        var rule = Rule("cliche-words");
        rule.Patterns = new List<string>();
        rule.LiteralPatterns = new List<string>();

        var ex = Assert.Throws<ArgumentException>(() => LintRuleStore.Validate(new List<LintRule> { rule }));

        Assert.Contains("至少给一条词面或正则", ex.Message);
    }

    [Fact]
    public void Merge_字面字段随外部规则一起覆盖()
    {
        // 按 Id 覆盖是整条替换：漏克隆一个字段，用户改一条规则时会把它的词表悄悄清空
        var builtIn = new List<LintRule> { Rule("cliche-words") };
        builtIn[0].LiteralPatterns = new List<string> { "内置词面" };
        var external = new List<LintRule> { Rule("cliche-words", "覆盖") };
        external[0].LiteralPatterns = new List<string> { "外部词面" };

        var merged = LintRuleStore.Merge(builtIn, external);

        Assert.Equal(new[] { "外部词面" }, merged[0].LiteralPatterns!);
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
        // 序数词只在文档与智能体回复里查：小说里「再次」「最后」是日常用词
        Assert.Equal(new[] { "doc", "agent" }, rules["paragraph-ordinals"].Genres);
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
        Assert.Equal(1, rules["triadic-parallel"].MinDensityPerKChar);
        Assert.Empty(rules["adverb-overuse"].Genres);
        // 暴力说法是为「把改代码写成砍切钉咬」立的；小说里的刀口与撕咬是字面义，进小说档只会把战斗场面全报出来
        Assert.Equal(new[] { "doc", "agent" }, rules["violent-verbs"].Genres);
        Assert.Equal(new[] { "fiction" }, rules["fiction-body-tics"].Genres);
        Assert.Equal(new[] { "fiction" }, rules["fiction-physio-tics"].Genres);
        Assert.Equal(new[] { "fiction" }, rules["fiction-atmosphere-props"].Genres);
        Assert.Equal(new[] { "fiction" }, rules["fiction-dominant-register"].Genres);
        Assert.Equal(new[] { "fiction" }, rules["latin-word-in-cjk"].Genres);
        // 这条在真人小说库上每千字 0.108 处，一本动画改编小说 2582 处，全是专名与拼音替换，
        // 默认开着会把报告淹掉：默认不勾选，用户点名才跑
        Assert.True(rules["latin-word-in-cjk"].DefaultOff);
        Assert.Equal(new[] { "fiction" }, rules["fiction-bold-template"].Genres);
        Assert.Equal(new[] { "fiction" }, rules["fiction-machine-numbers"].Genres);
        // 加粗模板是提醒级以上的硬信号（出版正文不排标记），数值只到提醒
        Assert.Equal("warn", rules["fiction-bold-template"].Severity);
        Assert.Equal("info", rules["fiction-machine-numbers"].Severity);
        // 外文字符判据走字符集不跑词表，三档都要报，所以档位留空
        Assert.Empty(rules["foreign-script-mix"].Genres);
        Assert.Equal(new[] { "fiction" }, rules["fiction-emotion-telling"].Genres);
        Assert.Equal(new[] { "fiction" }, rules["fiction-vague-nominalization"].Genres);
        Assert.Equal(new[] { "doc" }, rules["anthropomorphic-subject"].Genres);
        // 两条靠每千字密度判：单处是正常用词，篇幅一长正常用词也会堆起来，绝对次数定不了门限
        Assert.Equal(new[] { "fiction" }, rules["fiction-text-self-reference"].Genres);
        Assert.Equal(2, rules["fiction-text-self-reference"].MinDensityPerKChar);
        Assert.Equal(new[] { "fiction" }, rules["fiction-dialogue-reference"].Genres);
        Assert.Equal(3, rules["fiction-dialogue-reference"].MinDensityPerKChar);
        Assert.Equal(new[] { "fiction" }, rules["fiction-narrator-formula"].Genres);
        Assert.Equal(3, rules["fiction-narrator-formula"].MinDensityPerKChar);
        Assert.Equal(new[] { "fiction" }, rules["fiction-metaphor-lexicon"].Genres);
        Assert.Equal(5, rules["fiction-metaphor-lexicon"].MinDensityPerKChar);
        Assert.Equal(new[] { "agent" }, rules["agent-one-char-commitment"].Genres);
        Assert.Equal(2, rules["agent-one-char-commitment"].MinCount);
        Assert.Equal(3, rules["agent-one-char-commitment"].MinDensityPerKChar);
        Assert.Equal(new[] { "agent" }, rules["agent-agreement-opener"].Genres);
        Assert.Equal(new[] { "agent" }, rules["agent-deliberation-leak"].Genres);
        Assert.Equal(new[] { "agent" }, rules["agent-reassurance-pile"].Genres);
        Assert.Equal(2, rules["agent-reassurance-pile"].MinCount);
        // 报错话术在博客与智能体回复里都出现，其余三条只对回复生效
        Assert.Equal(new[] { "doc", "agent" }, rules["agent-error-paste"].Genres);
        Assert.Equal(2, rules["agent-error-paste"].MinCount);
        Assert.Equal(new[] { "agent" }, rules["agent-self-intro"].Genres);
        Assert.Equal(new[] { "agent" }, rules["agent-flattery"].Genres);
        Assert.Equal(2, rules["agent-conclusion-preamble"].MinCount);

        // 能不能打断：裸代号、截断路径、行话族、结论先行、正文求许可都标了；装饰符号按默认只提醒
        Assert.True(rules["agent-code-list"].BlocksReply);
        Assert.True(rules["agent-truncated-path"].BlocksReply);
        Assert.True(rules["agent-smoke-jargon"].BlocksReply);
        Assert.True(rules["agent-conclusion-first"].BlocksReply);
        Assert.False(rules["emoji-and-symbols"].BlocksReply);
        Assert.True(rules["agent-permission-ask"].BlocksReply);

        // 行话族的回复门限与 lint 门限分开：lint 单处就报，回复里一段两处才打断
        Assert.Equal(1, rules["agent-process-jargon"].MinCount);
        Assert.Equal(2, rules["agent-process-jargon"].ReplyMinCount);
        Assert.Null(rules["agent-code-list"].ReplyMinCount);

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
        // 氛围道具按段内密度判：单处氤氲是白描，凑够两处才是布景板
        Assert.Equal("paragraph", rules["fiction-atmosphere-props"].Scope);
        Assert.Equal(2, rules["fiction-atmosphere-props"].MinCount);
        // 新加的小说判据都不带打断权：实时闸只跑智能体档与通用判据
        Assert.All(
            new[] { "fiction-physio-tics", "fiction-atmosphere-props", "fiction-dominant-register", "latin-word-in-cjk" },
            id => Assert.False(rules[id].BlocksReply));
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
    public void Validate_触发密度小于一即抛()
    {
        var rule = Rule("cliche-words");
        rule.MinDensityPerKChar = 0;

        var ex = Assert.Throws<ArgumentException>(() => LintRuleStore.Validate(new List<LintRule> { rule }));
        Assert.Contains("触发密度", ex.Message);
    }

    [Fact]
    public void Validate_段落级配触发密度即抛()
    {
        // 段落级拿不到全文长度，密度算不出每千字；放过去就是配了一个永不生效的门限
        var rule = Rule("cliche-words");
        rule.Scope = "paragraph";
        rule.MinDensityPerKChar = 2;

        var ex = Assert.Throws<ArgumentException>(() => LintRuleStore.Validate(new List<LintRule> { rule }));
        Assert.Contains("仅在 Scope=document 时有效", ex.Message);
    }

    [Fact]
    public void Validate_回复门限小于一即抛()
    {
        var rule = Rule("agent-process-jargon");
        rule.ReplyMinCount = 0;

        var ex = Assert.Throws<ArgumentException>(() => LintRuleStore.Validate(new List<LintRule> { rule }));
        Assert.Contains("回复门限", ex.Message);
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

    /// <summary>
    /// 界面上的词表编辑页把改动写进程序目录的 lint_rules.json，这里钉住三件事：
    /// 写进去能读回来、坏规则不落盘、清空即回到内置。
    /// </summary>
    [Fact]
    public void SaveOverrides_写入后能原样读回()
    {
        using var dir = new TempDir();
        var rule = Rule("cliche-words", "改过的词");
        rule.Genres = new List<string> { "fiction" };

        LintRuleStore.SaveOverrides(new List<LintRule> { rule }, dir.Path);

        var loaded = LintRuleStore.LoadOverrides(dir.Path);
        var only = Assert.Single(loaded);
        Assert.Equal("cliche-words", only.Id);
        Assert.Equal(new[] { "改过的词" }, only.Patterns);
        Assert.Equal(new[] { "fiction" }, only.Genres);
    }

    [Fact]
    public void SaveOverrides_非法正则不落盘()
    {
        using var dir = new TempDir();
        var rule = Rule("cliche-words");
        rule.Kind = "regex";
        rule.Patterns = new List<string> { "(未闭合" };

        Assert.Throws<ArgumentException>(
            () => LintRuleStore.SaveOverrides(new List<LintRule> { rule }, dir.Path));

        // 校验在写盘之前：磁盘上不能留下一条会让下次启动直接报错的规则
        Assert.False(File.Exists(Path.Combine(dir.Path, LintRuleStore.OverrideFileName)));
    }

    [Fact]
    public void SaveOverrides_空列表写到磁盘再读回是空表()
    {
        using var dir = new TempDir();
        LintRuleStore.SaveOverrides(new List<LintRule> { Rule("cliche-words") }, dir.Path);
        LintRuleStore.SaveOverrides(new List<LintRule>(), dir.Path);

        Assert.Empty(LintRuleStore.LoadOverrides(dir.Path));
    }

    [Fact]
    public void SaveOverrides_文件不存在时读回空表()
    {
        using var dir = new TempDir();

        Assert.Empty(LintRuleStore.LoadOverrides(dir.Path));
    }
}
