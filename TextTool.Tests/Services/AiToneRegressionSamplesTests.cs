namespace TextTool.Tests.Services;

/// <summary>
/// 去 AI 味规则的标注样本回归。
///
/// 样本来自一次全工作区实测（261 个中文 md、约 100 万字），由作者逐条标注「AI 出身」还是「人写」，
/// 因此这里的期望值不是「读起来像不像」推出来的，是作者记忆里的真值。出处见每条样本的行尾注释。
///
/// 口径（作者拍定）：**人写的命中也在期望之内**——「人类写也不惯着」。
/// 所以本类只钉两件事：
/// 1. 每一处「反而」都要报出来（漏报一处 AI 味，就是留一个病灶——实测那一批漏报 8 处，全是 AI 原话）；
/// 2. 「算…账」的术语护栏必须挡住（`结算账户`/`核算账目`/`账单日` 这类真术语不是套话）。
///
/// 改判据先跑这些用例：「反而」那条（`S7`）取裸词而非词表，正是因为词表档位在这批样本上漏掉了 8 处 AI 原话。
/// </summary>
public class AiToneRegressionSamplesTests
{
    private static AiToneLintService Service() => new(LintRuleStore.GetDefaultRules());

    private static List<LintHit> HitsOf(string id, string text) =>
        Service().Scan(text, "样本.txt").Hits.Where(h => h.Id == id).ToList();

    /// <summary>
    /// 实测语料里「反而」的全部 19 处。第一列是作者标注的出身：
    /// `ai` 16 处、`human` 3 处——两列都期望报出，出身只作为改写时的优先级参考。
    /// </summary>
    public static IEnumerable<object[]> 反而样本() => new[]
    {
        // 人写 3 处（口径上照报）
        new object[] { "human", "一大段文字会分散用户注意力，决策点被埋在里面，人机沟通效率反而下降。" },                 // Config/Archive/…/prefer-ask-user-question.md
        new object[] { "human", "厂商可能出于产品意图设计驻留机制，内容质量高反而降低警惕。" },                            // Config/Skills/SecurityAudit/…/install-review.md
        new object[] { "human", "我就想看两三个视频，信息量却先灌一脸，反而更累了。" },                                    // Blog/bilicompact-source.md

        // AI 出身 16 处
        new object[] { "ai", "nvidia-smi 报的是驻留量，于是读数反而变小，看起来还有余量。" },                             // Config/Archive/…/wddm-vram-readings-lie.md
        new object[] { "ai", "而占用读数反而下降，这就是分页的指纹。" },                                                  // 同上
        new object[] { "ai", "最便宜的模型在多步任务上常规性地花 2-3 倍回合，总成本反而更高。" },                          // Config/Skills/DevProgressSkill/…/model-selection.md
        new object[] { "ai", "跑完它的文件 P3 命中数反而上升。" },                                                       // Config/Skills/WriteText/…/toolchain-boundaries.md
        new object[] { "ai", "参数单调变大，占用读数反而下降。" },                                                       // Blog/five-wrong-readings.md
        new object[] { "ai", "开思考反而让输出下滑。" },                                                                 // 同上
        new object[] { "ai", "一份什么都敢下结论的评审，可信度反而更低。" },                                              // Blog/two-per-task.md
        new object[] { "ai", "跑引号括号统一会把全角括号转半角、P3 命中反而上升，实测 2→6。" },                            // TextTool/doc/archive/…/final-review.md
        new object[] { "ai", "想让漂移现形、验证读数是否处在两态之间摆动时，轮询一下反而看得清。" },                       // Config/Archive/…/nvidia-smi-polling-pins-gpu.md
        new object[] { "ai", "整个文件都是改动，真正插入的那 3 行反而被淹没。" },                                         // Config/Skills/GitHubOps/…/ci-line-endings.md
        new object[] { "ai", "每个元素都要挣得自己的位置；有时增加上下文反而简化。" },                                    // Config/Skills/HtmlDesign/…/design-language.md
        new object[] { "ai", "命中时按需读 references，反而比全文内嵌更快。" },                                          // Config/Skills/ManageSkills/…/full-workflow.md
        new object[] { "ai", "想验证读数是不是正在两态之间摇摆的时候，轮询一下反而看得最清楚。" },                         // Blog/five-wrong-readings.md
        new object[] { "ai", "剩下的事情反而简单。" },                                                                   // Blog/with-her-eyes.md
        new object[] { "ai", "3 行重复；抽公共常量反而把两个不相干的 store 绑在一起。" },                                 // TextTool/doc/archive/…/final-review.md
        new object[] { "ai", "机械层方案把全角括号转半角，反而制造新的 P3 命中。" },                                       // TextTool/doc/archive/…/progress.md
    };

    [Theory]
    [MemberData(nameof(反而样本))]
    public void 反而每一处都报出且命中原文为反而(string origin, string text)
    {
        var hits = HitsOf("S7", text);

        var hit = Assert.Single(hits);
        Assert.Equal("反而", hit.Match);
        Assert.NotEmpty(origin);   // origin 进参数只为在测试名里保留出身，不参与断言
    }

    [Theory]
    [InlineData("科学家算了一笔账：每天省十分钟，一年就是六十小时。")]
    [InlineData("我们来算算这笔账。")]
    [InlineData("算总账的时候到了。")]
    [InlineData("这笔账迟早要算清楚。")]
    [InlineData("这笔账算下来也值。整个项目实际消耗约 4 亿 token，折人民币 23 块。")]   // 实测语料里的真命中，作者确认是模型蹦出的词
    public void 算账式陈词滥调报出(string text)
    {
        Assert.NotEmpty(HitsOf("S8", text));
    }

    [Theory]
    [InlineData("打开云计算的结算账户页面，核对账单分期。")]
    [InlineData("核算账目的同事今天不在。")]
    [InlineData("结算周期以账单日为准。")]
    [InlineData("账户余额不足，账单已出。")]
    public void 算账术语护栏挡住真术语(string text)
    {
        Assert.Empty(HitsOf("S8", text));
    }
}
