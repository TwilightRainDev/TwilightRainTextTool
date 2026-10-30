using System.Text.Json;

namespace TextTool.Tests.Services;

/// <summary>
/// 回复形状扫描：回答闸调用的那个工具的核心。
/// 钉三件事：形状认得对、围栏代码块整块不看、规则表缺条目或写法不符时立刻报错而不是静默零命中。
/// </summary>
public class ReplyShapeScannerTests
{
    private const string 裸代号 = "agent-code-list";
    private const string 截断路径 = "agent-truncated-path";
    private const string 装饰符号 = "emoji-and-symbols";
    private const string 暴力说法 = "violent-verbs";
    private const string 流程行话 = "agent-process-jargon";
    private const string 结论先行 = "agent-conclusion-first";
    private const string 求许可 = "agent-permission-ask";

    private static ReplyShapeScanner Scanner() => new(LintRuleStore.GetDefaultRules());

    private static List<ReplyShapeHit> Hits(string text) => Scanner().Scan(text).ToList();

    [Fact]
    public void 裸代号逐处报出行号()
    {
        var hits = Hits("先看 S1，再看 P3 与 D-2。");

        Assert.Equal(new[] { "S1", "P3", "D-2" }, hits.Select(h => h.Token));
        Assert.All(hits, h => Assert.Equal(1, h.Line));
        Assert.All(hits, h => Assert.Equal(裸代号, h.Id));
    }

    [Fact]
    public void 跨行时行号跟着走()
    {
        var hits = Hits("第一行干净。\n第二行有 L64。");

        Assert.Single(hits);
        Assert.Equal(2, hits[0].Line);
    }

    [Fact]
    public void 同一行的同一个片段只报一次()
    {
        // 读者的问题只有一个，位置报两遍只是噪声
        Assert.Single(Hits("S1 先说一遍，S1 再说一遍。"));
    }

    [Theory]
    [InlineData("WSL2 上核 SHA256，注意 UTF8 与 GPT4 的差别")]
    [InlineData("pi 1.1.0 与 v2 接口")]
    [InlineData("编码是 UTF-8")]
    public void 正常写法不报(string text)
    {
        Assert.Empty(Hits(text));
    }

    [Fact]
    public void 围栏代码块整块跳过()
    {
        // 围栏里是引用的原文：里面的代号是内容，不是回复的措辞
        const string text = "先看这段：\n```\n{\"rule\": \"S1\", \"line\": 64}\n```\n看完再说。";
        Assert.Empty(Hits(text));
    }

    [Fact]
    public void 波浪号围栏同样跳过()
    {
        Assert.Empty(Hits("例子：\n~~~\nS1 在这里\n~~~\n结束。"));
    }

    [Fact]
    public void 围栏外的照旧报()
    {
        var hits = Hits("看 S1：\n```\nS2\n```\n再看 P3。");
        Assert.Equal(new[] { "S1", "P3" }, hits.Select(h => h.Token));
    }

    [Fact]
    public void 半截路径报出且不带标点()
    {
        var hits = Hits("见 `TextTool/doc/archive/\u2026/final-review.md`，还有这条。");

        Assert.Single(hits);
        Assert.Equal("TextTool/doc/archive/\u2026/final-review.md", hits[0].Token);
        Assert.Equal(截断路径, hits[0].Id);
    }

    [Theory]
    [InlineData("完整路径是 Config/Extensions/README.md")]
    [InlineData("这件事\u2026\u2026后来没变")]
    [InlineData("「负面/程度词」（变笨/更累/下降\u2026）")]
    public void 完整路径与行文里的省略号不报(string text)
    {
        Assert.Empty(Hits(text));
    }

    [Fact]
    public void 规则表缺条目立刻抛()
    {
        var rules = LintRuleStore.GetDefaultRules().Where(r => r.Id != 截断路径).ToList();

        var ex = Assert.Throws<InvalidOperationException>(() => new ReplyShapeScanner(rules));
        Assert.Contains(截断路径, ex.Message);
    }

    [Fact]
    public void 判据没有模式立刻抛()
    {
        // 空模式等于静默零命中：宁可加载即报，也不让调用方以为查过了
        var rules = LintRuleStore.GetDefaultRules();
        var target = rules.First(r => r.Id == 裸代号);
        target.Patterns.Clear();

        var ex = Assert.Throws<InvalidOperationException>(() => new ReplyShapeScanner(rules));
        Assert.Contains(裸代号, ex.Message);
    }

    [Fact]
    public void 判据有多条模式时逐条都认()
    {
        // 装饰符号按码区分成五条：少认一条就是漏报一类字形
        var families = new[] { "\uFE0F", "\uD83D\uDE00", "\u2600", "\u2B50", "\uD83E\uDD14" };
        foreach (var text in families)
            Assert.Contains(Hits(text), h => h.Id == 装饰符号);
    }

    [Fact]
    public void 扫描器要的判据都在表里且写法固定()
    {
        // 前两条同时被 lint 的智能体档使用，第三条是通用判据：形状只在这张表里写一份
        var rules = LintRuleStore.GetDefaultRules().ToDictionary(r => r.Id);

        foreach (var id in ReplyShapeScanner.RuleIds)
        {
            Assert.True(rules.ContainsKey(id), id);
            Assert.Equal("regex", rules[id].Kind);
            Assert.NotEmpty(rules[id].Patterns);
        }
        Assert.Equal(new[] { "agent" }, rules[裸代号].Genres);
        Assert.Equal(new[] { "agent" }, rules[截断路径].Genres);
        Assert.Empty(rules[装饰符号].Genres);
    }

    [Fact]
    public void 能不能打断由规则表决定()
    {
        // 裸代号与截断路径当场拦；装饰符号只提醒
        Assert.True(Hits("看 S1。")[0].Blocks);
        Assert.True(Hits("见 TextTool/doc\u2026/final.md 那份文件。")[0].Blocks);
        Assert.False(Hits("修好了 \u2705").Single(h => h.Id == 装饰符号).Blocks);
    }

    [Fact]
    public void 行话要一段里凑够两处才返回()
    {
        // 单处行话既不值得打断也不值得提醒，整段不提
        Assert.DoesNotContain(Hits("这段只有一处收口。"), h => h.Id == 流程行话);

        var hits = Hits("这段收口了，也落盘了。\n\n另一段只有一处收口。").Where(h => h.Id == 流程行话).ToList();

        Assert.Equal(new[] { "收口", "落盘" }, hits.Select(h => h.Token));
        Assert.All(hits, h => Assert.True(h.Blocks));
    }

    [Fact]
    public void 门限按段算而不是按行算()
    {
        // 两处行话分在两段，各自都够不着门限
        Assert.DoesNotContain(Hits("这段收口。\n\n那段落盘。"), h => h.Id == 流程行话);
    }

    [Fact]
    public void 结论先行与正文求许可都打断()
    {
        var hits = Hits("先说结论，这条能跑。\n\n要不要我下一步接进闸？");

        Assert.True(Assert.Single(hits.Where(h => h.Id == 结论先行)).Blocks);
        // 求拍板该走问卷工具：正文里问一句就占掉一轮，命中即打回
        Assert.True(Assert.Single(hits.Where(h => h.Id == 求许可)).Blocks);
    }

    [Theory]
    [InlineData("如果你愿意，我可以把寻模型的启示再拆一遍。")]
    [InlineData("如果你需要，我可以先把中转站的口径收一遍。")]
    [InlineData("接下来怎么让过程更值钱，得你定。")]
    [InlineData("这一步你说了算。")]
    public void 把决定推回用户的形状也打断(string text)
    {
        Assert.True(Assert.Single(Hits(text).Where(h => h.Id == 求许可)).Blocks);
    }

    [Theory]
    [InlineData("这一刀最狠，把那条接口钉死了。")]
    [InlineData("这是最阴的一类问题，报错咬了我一口。")]
    public void 暴力说法按只提醒处理(string text)
    {
        var hits = Hits(text).Where(h => h.Id == 暴力说法).ToList();

        Assert.NotEmpty(hits);
        Assert.All(hits, h => Assert.False(h.Blocks));
    }

    [Theory]
    [InlineData("一刀切的策略不可取。")]
    [InlineData("他一口咬定是环境问题。")]
    [InlineData("齿轮咬合不良会影响寿命。")]
    [InlineData("这间屋子最阴凉。")]
    public void 暴力说法的护标挡住正常用法(string text)
    {
        Assert.DoesNotContain(Hits(text), h => h.Id == 暴力说法);
    }

    [Fact]
    public void 人读输出把打回与只提醒分开列()
    {
        var text = ReplyShapeOutput.Render(Hits("看 S1，修好了 \u2705。"), false);

        Assert.Contains("[命中] 裸代号清单（1 处）", text);
        Assert.Contains("[只提醒] 表情与装饰符号（1 处）", text);
    }

    [Fact]
    public void 人读输出一处一行_零命中明说()
    {
        Assert.Equal("没有读者解不开的写法。\n", ReplyShapeOutput.Render(Array.Empty<ReplyShapeHit>(), false));

        var text = ReplyShapeOutput.Render(Hits("先看 S1，再看 `\u2026/rule-map.md`。"), false);
        Assert.Contains("[命中] 裸代号清单（1 处）", text);
        Assert.Contains("[命中] 截断路径（1 处）", text);
        Assert.Contains("第 1 行  S1", text);
        Assert.DoesNotContain("\r", text);
    }

    [Fact]
    public void JSON输出带契约版本与三个字段()
    {
        var json = ReplyShapeOutput.Render(Hits("先看 S1。"), true);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(2, doc.RootElement.GetProperty("Version").GetInt32());
        var hit = doc.RootElement.GetProperty("Hits")[0];
        Assert.Equal(裸代号, hit.GetProperty("Id").GetString());
        Assert.Equal("S1", hit.GetProperty("Token").GetString());
        Assert.Equal(1, hit.GetProperty("Line").GetInt32());
        Assert.True(hit.GetProperty("Blocks").GetBoolean());
        // 只给调用方要的四个字段：标题这类人读文案不进 JSON
        Assert.Equal(4, hit.EnumerateObject().Count());
        Assert.DoesNotContain("\r", json);
    }
}
