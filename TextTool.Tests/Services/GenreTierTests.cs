namespace TextTool.Tests.Services;

/// <summary>
/// 档位（doc / fiction / agent）的隔离，以及小说档与通用档新增规则的行为。
///
/// 口径：
/// - 档位只决定「哪些规则参与扫描」，报告结构不变，所以 --json 的契约版本不动；
/// - 不给 --genre 时跑全部规则，与加档位之前的行为一致；
/// - 小说档的判据（一丝、瞬间、他知道）在编程文档里是正常用法，文档档必须一条不报——
///   这就是分档的全部理由，混在一档里只能二选一：要么小说里漏，要么文档里吵。
/// </summary>
public class GenreTierTests
{
    // 规则 Id 只在这里各出现一次，用例正文按现象名引用；改 Id 时只改这几行。
    private const string 套话词 = "cliche-words";
    private const string 序数词 = "paragraph-ordinals";
    private const string 网文脸 = "fiction-body-tics";
    private const string 生理反应 = "fiction-physio-tics";
    private const string 氛围道具 = "fiction-atmosphere-props";
    private const string 强势腔 = "fiction-dominant-register";
    private const string 外文混排 = "foreign-script-mix";
    private const string 中文夹英文 = "latin-word-in-cjk";
    private const string 程度词堆叠 = "fiction-filler-modifiers";
    private const string 陈词比喻 = "fiction-dead-metaphor";
    private const string 心理告知 = "fiction-emotion-telling";
    private const string 副词重复 = "adverb-overuse";
    private const string 抽象名词化 = "fiction-vague-nominalization";
    private const string 万能比喻 = "fiction-vague-simile";
    private const string 暴力动词 = "violent-verbs";
    private const string 碎片短句 = "short-sentence-run";
    private const string 段末兜底 = "hedging-ending";
    private const string 求许可 = "agent-permission-ask";
    private const string 非活物主语 = "anthropomorphic-subject";
    private const string 过程残留 = "doc-process-residue";
    private const string 助手号腔 = "assistant-persona";
    private const string 元评价路标 = "meta-commentary-marker";
    private const string 谄媚语气 = "sycophancy";
    private const string 对举句式 = "parallel-contrast";
    private const string 英文AI词 = "ai-frequent-words";
    private const string 定语堆叠 = "stacked-modifiers";
    private const string 波浪号 = "tilde-run";
    private const string 简介模板 = "synopsis-formula";
    private const string 全半角混排 = "mixed-punctuation-width";

    private static List<LintHit> Hits(string text, string? genre, string id) =>
        LintRunner.Run(new[] { ("样本.txt", text) }, null, null, genre)
            .Reports[0].Hits.Where(h => h.Id == id).ToList();

    [Theory]
    [InlineData(网文脸, "他眼中闪过一丝不耐，嘴角勾起一抹笑。")]
    [InlineData(网文脸, "温热的气息让你水光潋滟。")]
    [InlineData(网文脸, "她深吸一口气，心头一震，垂下眼睑。")]
    [InlineData(网文脸, "他邪魅一笑，退开半步。")]
    [InlineData(网文脸, "她贝齿轻咬下唇，没接话。")]
    [InlineData(网文脸, "银牙咬碎，一个字也没说。")]
    [InlineData(网文脸, "眼睑垂下，美得惊心动魄。")]
    [InlineData(程度词堆叠, "她微微侧头，语气里带着些许冷，还藏着几分不甘，只剩一抹苦笑。")]
    [InlineData(陈词比喻, "他的话像潮水般涌来，仿佛整个世界都被淹了一般。")]
    [InlineData(陈词比喻, "她哭得梨花带雨，笑得如沐春风。")]
    [InlineData(陈词比喻, "那番话说下来像是一把手术刀。")]
    [InlineData(陈词比喻, "那双眼睛如同两颗宝石。")]
    [InlineData(陈词比喻, "这句话像雷霆般击碎了她的困惑。")]
    [InlineData(陈词比喻, "微微眯起那双深邃宝石般的眼眸。")]
    [InlineData(心理告知, "他这才明白，原来不是他想的那样。他不知道的是，门已经开了。")]
    [InlineData(心理告知, "此刻，她终于意识到自己错了。")]
    [InlineData(副词重复, "他猛地站起，椅子刮过地面。她猛地回头。")]
    [InlineData(副词重复, "她不由得后退半步，不由自主地捏紧包带，情不自禁地冷笑，自然而然地把话接过去。")]
    [InlineData(副词重复, "极黑的眼圈下，极长的袍子已经很久没洗，极糟的味道挥之不去。")]
    [InlineData(抽象名词化, "他目光里有一种读不懂的东西。")]
    [InlineData(万能比喻, "他语气很轻，像是在说今天天气不错。")]
    [InlineData(生理反应, "他瞳孔骤然收缩。")]
    [InlineData(生理反应, "她呼吸一滞，瞬间僵住。")]
    [InlineData(生理反应, "他的理智崩塌了，眼底泛起一丝暗红。")]
    [InlineData(生理反应, "他倒吸一口凉气，把话咽了回去。")]
    [InlineData(生理反应, "全场瞳孔地震。")]
    [InlineData(强势腔, "全身缓缓散发出王者之气。")]
    public void 小说档规则命中样本(string id, string text)
    {
        Assert.NotEmpty(Hits(text, "fiction", id));
    }

    [Theory]
    [InlineData(网文脸, "他眼中闪过一丝不耐，嘴角勾起一抹笑。")]
    [InlineData(网文脸, "温热的气息让你水光潋滟。")]
    [InlineData(程度词堆叠, "她微微侧头，语气里带着些许冷，还藏着几分不甘，只剩一抹苦笑。")]
    [InlineData(陈词比喻, "他的话像潮水般涌来，仿佛整个世界都被淹了一般。")]
    [InlineData(心理告知, "此刻，她终于意识到自己错了。")]
    [InlineData(抽象名词化, "他目光里有一种读不懂的东西。")]
    [InlineData(万能比喻, "他语气很轻，像是在说今天天气不错。")]
    [InlineData(生理反应, "他瞳孔骤然收缩。")]
    [InlineData(氛围道具, "空气仿佛凝固，屋里氤氲着热气。")]
    [InlineData(强势腔, "他死盯着她，目光充满占有欲。")]
    [InlineData(强势腔, "那只带着侵略性的手按在他肩上。")]
    [InlineData(强势腔, "宠溺的眼神落在她身上。")]
    [InlineData(强势腔, "如同信徒窥见神明的真容。")]
    [InlineData(强势腔, "仿佛一尊献祭给恶神的木偶。")]
    [InlineData(强势腔, "全身缓缓散发出王者之气。")]
    [InlineData(强势腔, "眼神里满是野兽般的占有欲。")]
    [InlineData(中文夹英文, "他 subtly 退开半步。")]
    [InlineData(网文脸, "他邪魅一笑，退开半步。")]
    [InlineData(生理反应, "他倒吸一口凉气。")]
    public void 文档档不报小说规则(string id, string text)
    {
        Assert.Empty(Hits(text, "doc", id));
    }

    [Theory]
    [InlineData("他低声说了一句почти。")]
    [InlineData("他低声说了一句có gǎng。")]
    public void 外文混排三档都报(string text)
    {
        // 字符集判据不跑词表：模型把语气词写成别国文字，与档位无关
        foreach (var genre in new[] { "doc", "fiction", "agent" })
            Assert.NotEmpty(Hits(text, genre, 外文混排));
    }

    [Fact]
    public void 中文夹英文词只在小说档报()
    {
        // 同人译名与品牌名也会命中，所以只到提醒级，且不进文档档与智能体档
        Assert.NotEmpty(Hits("他 subtly 退开半步。", "fiction", 中文夹英文));
        Assert.Empty(Hits("他 subtly 退开半步。", "agent", 中文夹英文));
    }

    [Theory]
    [InlineData("砍一刀就能把这段改完。")]
    [InlineData("这一刀最狠，直接补一刀就行。")]
    [InlineData("这个报错咬了我一下。")]
    [InlineData("这是最阴的一类，把接口钉死了。")]
    public void 暴力动词在文档档也报(string text)
    {
        Assert.NotEmpty(Hits(text, "doc", 暴力动词));
    }

    [Theory]
    [InlineData("一刀切的策略不可取。")]
    [InlineData("他一口咬定是环境问题。")]
    [InlineData("齿轮咬合不良会影响寿命。")]
    [InlineData("这间屋子最阴凉，适合放设备。")]
    public void 暴力动词的术语护标挡住正常用法(string text)
    {
        Assert.Empty(Hits(text, "doc", 暴力动词));
    }

    [Theory]
    [InlineData("若故障持续，还是要检查或更换设备。")]
    [InlineData("开会员买时间才可能划算，但总支出也未必更低。")]
    [InlineData("这些做法因人而异。")]
    [InlineData("希望这些信息对你有所帮助。")]
    public void 段末兜底报出(string text)
    {
        Assert.NotEmpty(Hits(text, null, 段末兜底));
    }

    [Theory]
    [InlineData("如果你愿意，我可以进一步帮你把这几步跑一遍。")]
    [InlineData("如果你需要，我可以先把清单列出来。")]
    [InlineData("接下来怎么排期，得你定。")]
    [InlineData("这一步你说了算。")]
    [InlineData("如果你想，我真的可以再拆一遍。")]
    [InlineData("如果您确认优先级后，我继续推进。")]
    [InlineData("这部分等您确认后我再动。")]
    [InlineData("您看怎么安排？")]
    [InlineData("如需，我可以生成验收结论摘要。")]
    [InlineData("我可以帮你整理一份字段差异清单。")]
    [InlineData("我可以列出一份待签字的清单。")]
    public void 正文里求许可报出(string text)
    {
        Assert.NotEmpty(Hits(text, "agent", 求许可));
    }

    [Theory]
    [InlineData("如果您确认无误，请签字。")]
    [InlineData("如果你确认了，就直接发给对方。")]
    [InlineData("我可以确认这一点，不用你操心。")]
    public void 条件句里的确认不算求许可(string text)
    {
        // 判的是等对方发话才动手，不是「假如你确认了」这种条件说明
        Assert.Empty(Hits(text, "agent", 求许可));
    }

    [Fact]
    public void 兜底词不在段末就不报()
    {
        // 截取的是段末三十字：句中一句「未必」不是免责收尾，是正常论述
        var text = "未必所有方案都适用，这一点开场就该说清楚。接下来按步骤讲三段：先备料，再调色，最后合成导出并检查首尾帧。";
        Assert.Empty(Hits(text, null, 段末兜底));
    }

    [Theory]
    [InlineData("微观经济学里的希克斯需求问的是怎样用尽量少的支出达到给定的项目水平。")]
    [InlineData("这份报告告诉我们，效率提升了三成。")]
    public void 非活物做人的动作在文档档报出(string text)
    {
        Assert.NotEmpty(Hits(text, "doc", 非活物主语));
    }

    [Fact]
    public void 非活物做人的动作不掺进小说档()
    {
        Assert.Empty(Hits("这份报告告诉我们，效率提升了三成。", "fiction", 非活物主语));
    }

    [Theory]
    [InlineData("阶段一加了登录模块。第二阶段补齐导出。")]
    [InlineData("本轮已完成，剩余能力留到未来再展开。")]
    [InlineData("该功能原先是同步实现，某个时间点改成了异步。")]
    public void 文档里的过程残留报出(string text)
    {
        Assert.NotEmpty(Hits(text, "doc", 过程残留));
    }

    [Theory]
    [InlineData("本模块只负责导出，不管渲染。")]
    [InlineData("同步改成异步之后，接口保持不变。")]
    [InlineData("本阶段的目标是把导出链路打通。")]
    public void 只讲现状的句子不报(string text)
    {
        Assert.Empty(Hits(text, "doc", 过程残留));
    }

    [Fact]
    public void 过程残留不掺进小说档()
    {
        Assert.Empty(Hits("阶段一加了登录模块。", "fiction", 过程残留));
    }

    [Theory]
    [InlineData("每一步都摊开给你们看。")]
    [InlineData("先立三条规矩：日更打卡绝不摸鱼。")]
    [InlineData("评论区就是我的会议室。")]
    [InlineData("本鲸服务器一颤（深度思考中）。")]
    [InlineData("花的都是主人的钱，别白烧。")]
    public void 助手号腔报出(string text)
    {
        Assert.NotEmpty(Hits(text, null, 助手号腔));
    }

    [Theory]
    [InlineData("遇到服务器繁忙，重试三次即可。")]
    [InlineData("这个流程公开之后，审核快了不少。")]
    [InlineData("立规矩容易，守规矩难。")]
    [InlineData("评论区里有不少好问题。")]
    public void 同一批词的正常用法不报(string text)
    {
        // 单收单词会把「流程公开」「立规矩」「服务器繁忙」全判成 AI 腔，那些都是正常中文
        Assert.Empty(Hits(text, null, 助手号腔));
    }

    [Theory]
    [InlineData("这不叫偷懒，这叫编。")]
    [InlineData("他要的不是改得更卖力，是你得先分清同类。")]
    [InlineData("那不是句号的锅，是不会写。")]
    public void 纠偏句式的新骨架报出(string text)
    {
        Assert.NotEmpty(Hits(text, null, 对举句式));
    }

    [Theory]
    [InlineData("这条最硬，先说为什么不核销。")]
    [InlineData("挤牙膏那条，真疼。")]
    [InlineData("这条我不改，我给他补一刀。")]
    public void 元评价路标报出(string text)
    {
        Assert.NotEmpty(Hits(text, null, 元评价路标));
    }

    [Theory]
    [InlineData("我必须很认真地说一句：这条判据站不住。")]
    [InlineData("我必须很认真的说一句：这条判据站不住。")]
    [InlineData("一句话拆开，不啰嗦。")]
    public void 元评价路标的新形状也报出(string text)
    {
        Assert.NotEmpty(Hits(text, null, 元评价路标));
    }

    [Theory]
    [InlineData("我们不妨换个角度来看这件事。")]
    [InlineData("这背后反映的是一个更深层次的问题。")]
    public void 换角度与背后反映算元话语(string text)
    {
        Assert.NotEmpty(Hits(text, null, 元评价路标));
    }

    [Theory]
    [InlineData("这其实是一个非常好的问题。")]
    [InlineData("你提到了一个非常关键的点。")]
    [InlineData("我会稳稳地接住你。这反而更稳。")]
    public void 抬高对方与接话腔报出(string text)
    {
        Assert.NotEmpty(Hits(text, null, 谄媚语气));
    }

    [Theory]
    [InlineData("新调查找到了原因，修好问题，还把修改交了。")]
    [InlineData("我们换个人来看这段。")]
    public void 谄媚与元话语的护栏挡住正常搭配(string text)
    {
        // 「修好问题」是好与问题的偶然相邻，「换个人」不是换个角度
        Assert.Empty(Hits(text, null, 谄媚语气));
        Assert.Empty(Hits(text, null, 元评价路标));
    }

    [Theory]
    [InlineData("他不是老师，是医生。")]
    [InlineData("她不是不想去，是走不开。")]
    public void 人称代词开头的判断句不报(string text)
    {
        // 这两种在小说对白里到处都是，收窄的代价是漏掉「他不是不想，是不能」这类真纠偏
        Assert.DoesNotContain(Hits(text, null, 对举句式), h => h.Match.Contains("，是"));
    }

    [Theory]
    [InlineData("这条路最硬的时候是冬天，地面会冻裂。")]
    [InlineData("这两条建议跟别的一样，都要求先备份。")]
    public void 路标式的正常用法不报(string text)
    {
        Assert.Empty(Hits(text, null, 元评价路标));
    }

    [Fact]
    public void 小说里获得展示不报()
    {
        // 真实小说实测：这条规则 572 处命中里 85% 是「获得能力」「展示实力」
        Assert.Empty(Hits("他获得了一份新的能力，并展示给所有人看。", "fiction", 英文AI词));
        Assert.NotEmpty(Hits("他获得了能力，获得了信任，获得了名声。", "doc", 英文AI词));   // MinCount 按每条模式各自计数，得同一个词出现三次
    }

    [Fact]
    public void 小说里文学定语不报()
    {
        Assert.Empty(Hits("他翻着画家提香的作品集，想起那家十分有人气的咖啡厅。", "fiction", 定语堆叠));
        Assert.NotEmpty(Hits("这是一个基于多维度的深层次的长期的方案。", "doc", 定语堆叠));   // 三个「的」才成堆
    }

    [Fact]
    public void 小说里波浪号不报()
    {
        // 网文的语气延长符号；文档里才是排版噪声
        Assert.Empty(Hits("我回来了~~你等我一下~~", "fiction", 波浪号));
        Assert.NotEmpty(Hits("见第 3 节～", "doc", 波浪号));
    }

    [Fact]
    public void 直引号交给引号风格规则()
    {
        // 全半角混排只管标点，引号风格由 mixed-quote-style 单管，否则小说里满屏直引号全是噪声
        Assert.Empty(Hits("他说\"行\"，然后就走了。", null, 全半角混排));
        Assert.NotEmpty(Hits("他说\"行\",然后就走了。", null, 全半角混排));
    }

    [Theory]
    [InlineData("他犹如一把刀。", false)]
    [InlineData("他犹如一把刀一般站着。", true)]
    public void 犹如是正常词_带组合才算陈词(string text, bool hit)
    {
        Assert.Equal(hit, Hits(text, "fiction", 陈词比喻).Count > 0);
    }

    [Fact]
    public void 小说里段末的总之不报_文档里报()
    {
        // 真人译制小说里「总之」是自然用法（实测 394 处），AI 小说 0 处
        var text = "他把话咽了回去，摆摆手走了。总之，这件事就这么算了。";
        Assert.Empty(Hits(text, "fiction", "elevated-ending"));
        Assert.NotEmpty(Hits(text, "doc", "elevated-ending"));
    }

    [Theory]
    [InlineData("在这个拥挤的城市里，每个人都有着自己的故事。")]
    [InlineData("直到那天，他偶然发现了一种神秘的力量。")]
    [InlineData("主角叶轩，是一个身处都市中的普通年轻人。")]
    [InlineData("从此，他开始了他的冒险之旅。")]
    [InlineData("他结识了一群志同道合的朋友。")]
    [InlineData("他遇到了各种危险，也来到了一个完全陌生的世界。")]
    public void 简介模板骨架报出(string text)
    {
        Assert.NotEmpty(Hits(text, "fiction", 简介模板));
    }

    [Theory]
    [InlineData("她把饭盒放进包里，回头看了一眼走廊尽头。")]
    [InlineData("这个城市在下雨，路灯把水面拉成长条。")]
    public void 具体叙述不报简介模板(string text)
    {
        Assert.Empty(Hits(text, "fiction", 简介模板));
    }

    [Fact]
    public void 连续碎片短句作为统计项报出()
    {
        var notes = LintRunner.Run(
                new[] { ("样本.txt", "他没说话。她也没说。屋里很静。风停了。") }, null, null, "fiction")
            .Reports[0].Notes;

        Assert.Contains(notes, n => n.Id == 碎片短句);
    }

    [Fact]
    public void 长短句交错的段落不报碎片短句()
    {
        var notes = LintRunner.Run(
                new[] { ("样本.txt", "他停下来，把杯子放回桌上，看了她一会儿才开口。没事。然后他走了。") },
                null, null, "fiction")
            .Reports[0].Notes;

        Assert.DoesNotContain(notes, n => n.Id == 碎片短句);
    }

    [Theory]
    [InlineData("极地考察队回来了。")]
    [InlineData("这是极限，不是常态。")]
    [InlineData("极少数人会这么选。")]
    [InlineData("他态度积极，极为认真。")]
    [InlineData("极端天气要提前准备。")]
    [InlineData("极昼过后是极光。")]
    [InlineData("极化与极权是两回事。")]
    [InlineData("这已经做到极致了。")]
    [InlineData("享受到了极点。")]
    [InlineData("极大与极小之间还有很长的路。")]
    [InlineData("极高的温度下，极轻的粉末会飘起来。")]
    public void 极族的正常词不报(string text)
    {
        Assert.Empty(Hits(text, "fiction", 副词重复));
    }

    [Theory]
    [InlineData("他看了她一眼，没说话。她把杯子放下，起身走了。")]
    [InlineData("她微微点头，没接话。")]
    [InlineData("他猛地站起，椅子刮过地面。")]
    [InlineData("她不由得后退半步，没说别的。")]
    [InlineData("医生握着手术刀走了进来。")]
    [InlineData("宝石戒指掉在桌上，滚了半圈。")]
    [InlineData("母神教的信徒在门外吟唱。")]
    [InlineData("他咬紧牙关，没再说话。")]
    [InlineData("我倒吸了一口冷气，因为屋里没开暖气。")]
    [InlineData("这片土地很肥沃，适合种麦子。")]
    [InlineData("医生说是眼睑炎，开了药。")]
    public void 小说档的正常描写不报(string text)
    {
        foreach (var id in new[] { 网文脸, 陈词比喻 })
            Assert.Empty(Hits(text, "fiction", id));
        // 程度词与副词都是「同段凑够次数」才报：单次出现是正常写法
        Assert.Empty(Hits(text, "fiction", 程度词堆叠));
        Assert.Empty(Hits(text, "fiction", 副词重复));
    }

    [Fact]
    public void 序数词骨架小说档不报()
    {
        // 叙事里「再次」「最后」是日常用词，两类判据在小说档都只剩误报：
        // 段内序数词与序数词骨架统计项都不出声
        var text = "他再次抬手，涟漪再次升起，再次平息。\n最后一枚石子入水时，涟漪散得更远。";

        Assert.Empty(Hits(text, "fiction", "paragraph-ordinals"));
        var notes = LintRunner.Run(new[] { ("样本.txt", text) }, null, null, "fiction")
            .Reports[0].Notes.Select(n => n.Id).ToList();
        Assert.DoesNotContain("ordinal-skeleton", notes);
    }

    [Fact]
    public void 序数词骨架文档档照报()
    {
        var text = "首先看输入，其次看输出，最后看中间结果。";

        Assert.NotEmpty(Hits(text, "doc", 序数词));
        var notes = LintRunner.Run(new[] { ("t.md", text) }, null, null, "doc")
            .Reports[0].Notes.Select(n => n.Id).ToList();
        Assert.Contains("ordinal-skeleton", notes);
    }

    [Fact]
    public void 小说档仍跑通用规则()
    {
        // 档位是过滤不是替换：小说档照样要抓套话
        Assert.NotEmpty(Hits("不可否认，这套方案赋能了整个流程。", "fiction", 套话词));
    }

    [Theory]
    [InlineData("这件事至关重要，尤為关键。")]
    [InlineData("不容忽视的是，结果前所未有。")]
    public void 评价空话小说档与文档档都抓(string text)
    {
        // 套话词是通用规则：这四个词在两种文本里都是空话
        Assert.NotEmpty(Hits(text, "fiction", 套话词));
        Assert.NotEmpty(Hits(text, "doc", 套话词));
    }

    [Fact]
    public void 不给档位时跑全部规则()
    {
        Assert.NotEmpty(Hits("此刻，她终于意识到自己错了。", null, 心理告知));
        Assert.NotEmpty(Hits("不可否认，这套方案赋能了整个流程。", null, 套话词));
    }

    [Fact]
    public void 未知档位即抛()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            LintRunner.Run(new[] { ("a.txt", "干净的一段话。") }, null, null, "poetry"));

        Assert.Contains("--genre", ex.Message);
    }

    [Fact]
    public void 点名了不在该档的规则即抛()
    {
        // 「零命中」与「这条规则不在这个档」是两种完全不同的沉默，后者必须说出来
        var ex = Assert.Throws<ArgumentException>(() =>
            LintRunner.Run(new[] { ("a.txt", "干净的一段话。") }, new[] { 网文脸 }, null, "doc"));

        Assert.Contains(网文脸, ex.Message);
    }

    [Fact]
    public void 档位与只跑指定规则可叠加()
    {
        var hits = LintRunner.Run(
                new[] { ("样本.txt", "此刻，她终于意识到自己错了。不可否认，这套方案赋能了整个流程。") },
                new[] { 心理告知 }, null, "fiction")
            .Reports[0].Hits;

        Assert.All(hits, h => Assert.Equal(心理告知, h.Id));
        Assert.NotEmpty(hits);
    }
}
