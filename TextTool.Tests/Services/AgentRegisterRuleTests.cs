namespace TextTool.Tests.Services;

/// <summary>
/// 智能体档的八条判据：回复里那些只有机器与团队内部认得的形状。
///
/// 出处是一次数语料差分：智能体会话正文 15.3 万字，对伙伴自己写的技术博文 11.6 万字。
/// 八条在两侧的密度差是 5 到 100 倍，判决样本逐条摘自那批原句。
///
/// 这一档的目标与另外两档不同：智能体必定是 AI，不判「像不像 AI」，只判读者能不能一句句读懂。
/// 所以判据收的是「要解码才能接着读」的东西，而不是文风。每条钉两件事：
/// 智能体原话里要报出来；人写的同类句子不许报——少了后半边，判据会在正常人话上刷屏。
/// </summary>
public class AgentRegisterRuleTests
{
    // 规则 Id 只在这里各出现一次，用例正文一律按现象名引用。
    private const string 提交哈希 = "agent-commit-hash";
    private const string 裸代号 = "agent-code-list";
    private const string 路径清单 = "agent-path-list";
    private const string 命令行选项 = "agent-cli-option";
    private const string 冒烟行话 = "agent-smoke-jargon";
    private const string 测试行话 = "agent-test-jargon";
    private const string 流程行话 = "agent-process-jargon";
    private const string 行内代码 = "agent-inline-code";

    private const string 套话词 = "cliche-words";
    private const string 网文脸 = "fiction-body-tics";
    private const string 过程残留 = "doc-process-residue";

    private static List<LintHit> Hits(string text, string? genre, string id) =>
        LintRunner.Run(new[] { ("样本.txt", text) }, null, null, genre)
            .Reports[0].Hits.Where(h => h.Id == id).ToList();

    [Theory]
    [InlineData(提交哈希, "提交 9f7761a，把那条判据从文档档收回来了。")]
    [InlineData(提交哈希, "这一笔在 2773ef7 里，只动了注释。")]
    [InlineData(裸代号, "内置 19 条数据规则（P1 P2 P3 P6 P7 P8 / L1–L7 / S1–S6）。")]
    [InlineData(裸代号, "这次补的是 S7 强加反转与 S8 算账式引出数据。")]
    [InlineData(路径清单, "规则在 TextTool.Core/default_lint_rules.json，测试在 LintRuleStoreTests.cs。")]
    [InlineData(路径清单, "产物是 TextTool-GUI-3.0.1-win-x64.zip，旁边还有 TextTool-CLI-3.0.1-win-x64.zip。")]
    [InlineData(命令行选项, "跑 texttool lint --only parallel-contrast --json 看命中。")]
    [InlineData(命令行选项, "加 --min-severity warn 之后只剩两类。")]
    [InlineData(冒烟行话, "CLI 冒烟通过，两档隔离正确。")]
    [InlineData(冒烟行话, "这批样本先拿来做烟测。")]
    [InlineData(测试行话, "加四条回归用例把这次体检测出来的行为钉住。")]
    [InlineData(测试行话, "断言全部通过，单测也绿了。")]
    [InlineData(流程行话, "按你选的口径这轮不落盘，等收口之后再入仓。")]
    [InlineData(流程行话, "那条断链先从档位配置里摘掉。")]
    [InlineData(流程行话, "门禁全过之后才能合并。")]
    [InlineData(流程行话, "先灰度发布再放量，出异常就立刻回滚。")]
    [InlineData(流程行话, "建立唯一基线，拍快照，存证据链。")]
    [InlineData(流程行话, "先干跑一次，再上故障注入。")]
    [InlineData(流程行话, "探针埋好了，接着跑验收矩阵。")]
    [InlineData(流程行话, "还要 dry-run 一遍才算完。")]
    [InlineData(流程行话, "先做 rollback 预案，再谈 canary。")]
    [InlineData(流程行话, "RTO 与 RPO 都要压到十分钟以内。")]
    [InlineData(测试行话, "这两条 regression 各配一个 fixture，fixture 单独放。")]
    [InlineData(行内代码, "改 `LintRule.cs` 的 `Genres` 字段、`LintRunner` 的 `GenreIds` 判断、`LintTab.axaml` 的下拉项，还有三份 `Localization` 的键。")]
    public void 智能体回复里的机器形状报出(string id, string text)
    {
        Assert.NotEmpty(Hits(text, "agent", id));
    }

    [Theory]
    [InlineData(提交哈希, "订单号 2026082612345678 那天出的事。")]
    [InlineData(裸代号, "第 45 行那一处（L45）改过了。")]
    [InlineData(路径清单, "这次只动了 main.user.js 一个文件。")]
    [InlineData(命令行选项, "| 规则 | 命中 |\n|---|---|\n| 破折号 | 768 |")]
    [InlineData(冒烟行话, "我跑了一遍安装包，装完能打开。")]
    [InlineData(测试行话, "他断言明年会更好。")]
    [InlineData(流程行话, "他把文件从磁盘上删了，重启之后好了。")]
    [InlineData(流程行话, "这张照片先转成灰度图，再调对比度。")]
    [InlineData(行内代码, "改 `package.json` 里的一行就够了。")]
    public void 人写的同类句子不报(string id, string text)
    {
        Assert.Empty(Hits(text, "agent", id));
    }

    [Fact]
    public void 英文行话只在智能体回复里报()
    {
        // dry-run、rollback 这些写在技术文档里是正常说法，两条行话规则只属于智能体回复
        Assert.Empty(Hits("跑一次 dry-run，再补一个 fixture。", "doc", 流程行话));
        Assert.Empty(Hits("跑一次 dry-run，再补一个 fixture。", "doc", 测试行话));
        Assert.NotEmpty(Hits("跑一次 dry-run，再补一个 fixture。", "agent", 流程行话));
    }

    [Fact]
    public void 纯数字串不算哈希()
    {
        // 全部判别据里唯一要躲的是「长得像哈希的数字」：日期、订单号、时间戳都不是十六进制串
        Assert.Empty(Hits("20260826 那次改动之后，订单号 2026082612345678 就废了。", "agent", 提交哈希));
        Assert.NotEmpty(Hits("20260826a 那次改动之后就没再动过。", "agent", 提交哈希));
    }

    [Fact]
    public void 行内代码按密度判而不是按有无()
    {
        const string 一处 = "改 `LintRule.cs` 里那行就够了。";
        const string 五处 = "改 `LintRule.cs`、`LintRunner.cs`、`LintTab.axaml`、`zh_CN.json`、`GenreTierTests.cs` 五处。";

        Assert.Empty(Hits(一处, "agent", 行内代码));
        Assert.NotEmpty(Hits(五处, "agent", 行内代码));
    }

    [Fact]
    public void 代号按段内个数判而不是按有无()
    {
        // 单个代号是正常引用（「S1 这一条我改过了」），一段里攒够两个才成一串编号
        Assert.Empty(Hits("S1 这一条我改过了。", "agent", 裸代号));
        Assert.NotEmpty(Hits("S1 这条报的是纠偏句式，S5 那条报的是重复标点。", "agent", 裸代号));
    }

    [Theory]
    [InlineData(提交哈希, "提交 9f7761a，把那条判据从文档档收回来了。")]
    [InlineData(裸代号, "内置 19 条数据规则（P1 P2 P3 P6 P7 P8 / L1–L7 / S1–S6）。")]
    [InlineData(路径清单, "规则在 TextTool.Core/default_lint_rules.json，测试在 LintRuleStoreTests.cs。")]
    [InlineData(命令行选项, "跑 texttool lint --only parallel-contrast --json 看命中。")]
    [InlineData(冒烟行话, "CLI 冒烟通过，两档隔离正确。")]
    [InlineData(测试行话, "加四条回归用例把这次体检测出来的行为钉住。")]
    [InlineData(流程行话, "按你选的口径这轮不落盘，等收口之后再入仓。")]
    [InlineData(行内代码, "改 `LintRule.cs` 的 `Genres` 字段、`LintRunner` 的 `GenreIds` 判断、`LintTab.axaml` 的下拉项，还有三份 `Localization` 的键。")]
    public void 文档档与小说档一条都不报(string id, string text)
    {
        // 这些形状在编程文档里是本职（路径、命令、测试术语），在小说里根本不会出现
        Assert.Empty(Hits(text, "doc", id));
        Assert.Empty(Hits(text, "fiction", id));
    }

    [Fact]
    public void 智能体档仍跑通用规则()
    {
        // 档位是过滤不是替换：套话、对举句式这些不分语域
        Assert.NotEmpty(Hits("不可否认，这套方案赋能了整个流程。", "agent", 套话词));
    }

    [Fact]
    public void 智能体档不跑另外两档专属的规则()
    {
        Assert.Empty(Hits("他眼中闪过一丝不耐，嘴角勾起一抹笑。", "agent", 网文脸));
        Assert.Empty(Hits("阶段一加了登录模块。第二阶段补齐导出。", "agent", 过程残留));
    }

    [Fact]
    public void 不给档位时智能体规则照跑()
    {
        // 与加档位之前的行为一致：不给 --genre 就是不过滤
        Assert.NotEmpty(Hits("CLI 冒烟通过，两档隔离正确。", null, 冒烟行话));
    }
}
