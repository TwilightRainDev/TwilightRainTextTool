namespace TextTool.Tests.Services;

/// <summary>
/// 小说档的生理、氛围与强势腔判据。
///
/// 每条钉两件事：AI 稿里的形状要报出来；能写成同样字面的真人叙述不许报。
/// 护栏那半边是它们的立据——瞳孔会缩、呼吸会沉、氤氲与气场真人也写，
/// 不把「正常叙述长什么样」写进用例，规则就只能在真人稿上刷屏。
///
/// 严重度不同源：生理与强势腔按社区口碑收的是固定搭配，
/// 氛围道具按段内密度判，单处只是白描。
/// </summary>
public class FictionBodyRuleTests
{
    // 规则 Id 只在这里各出现一次，用例正文一律按现象名引用。
    private const string 生理反应 = "fiction-physio-tics";
    private const string 氛围道具 = "fiction-atmosphere-props";
    private const string 强势腔 = "fiction-dominant-register";

    private static List<LintHit> Hits(string text, string? genre, string id) =>
        LintRunner.Run(new[] { ("样本.txt", text) }, null, null, genre)
            .Reports[0].Hits.Where(h => h.Id == id).ToList();

    [Theory]
    [InlineData("他瞳孔骤然收缩。")]
    [InlineData("她瞳孔骤缩，瞬间放大。")]
    [InlineData("他眼神空洞，晦暗不明。")]
    [InlineData("他眼底泛起一丝暗红。")]
    [InlineData("他的呼吸瞬间停滞。")]
    [InlineData("她呼吸一滞。")]
    [InlineData("他呼吸沉重。")]
    [InlineData("他瞬间僵住。")]
    [InlineData("他浑身僵住。")]
    [InlineData("他的理智崩塌了。")]
    [InlineData("瞳孔微缩。")]
    [InlineData("我心中涌起一股荒诞感。")]
    [InlineData("她挤出一滴生理性的眼泪。")]
    public void 生理反应套话报出(string text)
    {
        Assert.NotEmpty(Hits(text, "fiction", 生理反应));
    }

    [Theory]
    [InlineData("他的瞳孔在背光里缩了一下，随即又放开。")]
    [InlineData("他的呼吸很沉，像是走了很远的路。")]
    [InlineData("她打了个寒战，把外套裹紧。")]
    [InlineData("他眼眶有点发酸，低头揉了揉。")]
    [InlineData("心里泛起一点酸味，他把它按下去。")]
    public void 正常的体征叙述不报(string text)
    {
        Assert.Empty(Hits(text, "fiction", 生理反应));
    }

    [Fact]
    public void 氛围道具按段内密度判()
    {
        // 单处氤氲是白描，真人照写；同一段凑够两处才是布景板
        Assert.Empty(Hits("屋里氤氲着热气，她把窗推开一条缝。", "fiction", 氛围道具));
        Assert.NotEmpty(Hits("空气仿佛凝固，屋里氤氲着热气。", "fiction", 氛围道具));
    }

    [Fact]
    public void 氛围道具整段凑够才报()
    {
        var text = "空气仿佛凝固了，暮色蒸腾成一片灰。他推门进来，额上有细密的汗珠，屋里氤氲着一种说不清的压迫感。";
        Assert.True(Hits(text, "fiction", 氛围道具).Count >= 2);
    }

    [Theory]
    [InlineData("不容置疑的语气。")]
    [InlineData("极具侵略性的目光。")]
    [InlineData("他的目光充满惩罚性与占有欲。")]
    [InlineData("他死盯着她。")]
    [InlineData("低沉的嗓音。")]
    [InlineData("像淬毒的刀子。")]
    [InlineData("想把她揉入骨血。")]
    [InlineData("宛若神明。")]
    [InlineData("一场献祭。")]
    public void 强势方腔调报出(string text)
    {
        Assert.NotEmpty(Hits(text, "fiction", 强势腔));
    }

    [Theory]
    [InlineData("他身上的气场很强，眼底像深渊。")]
    [InlineData("声音细若蚊呐。")]
    [InlineData("她像受惊的小兽，缩在墙角。")]
    [InlineData("雨水顺着甬道流下去。")]
    [InlineData("她抬眼看他，眼底有水光。")]
    public void 单词级与罕见隐喻不报(string text)
    {
        // 气场、深渊、小兽、甬道这些词真人照写；只收搭配是这条规则的存在前提
        Assert.Empty(Hits(text, "fiction", 强势腔));
    }

    [Fact]
    public void 动作与语气套话报出()
    {
        // 推眼镜本身就算动作模板，不管有没有「不存在的」那层变体
        Assert.NotEmpty(Hits("他抬手推了推不存在的眼镜。", "fiction", "fiction-body-tics"));
        Assert.NotEmpty(Hits("他抬手推了推眼镜，把合同翻到最后一页。", "fiction", "fiction-body-tics"));
        Assert.NotEmpty(Hits("他语气毫无波澜地补了一句。", "fiction", "fiction-body-tics"));
        Assert.NotEmpty(Hits("他绷着紧绷的声带开口。", "fiction", "fiction-body-tics"));
    }

    [Fact]
    public void 动作与语气套话的日常写法不报()
    {
        Assert.Empty(Hits("他把眼镜摘下来擦了擦。", "fiction", "fiction-body-tics"));
        Assert.Empty(Hits("弦绷紧了，再拧就要断。", "fiction", "fiction-body-tics"));
    }

    [Fact]
    public void 表情与体态模板报出()
    {
        // 脸红像苹果、脸色惨白、眉头拧成川字、细若蚊蝇、媚眼如丝、吐气如兰、勾起下巴
        Assert.NotEmpty(Hits("主角脸红得像苹果。", "fiction", "fiction-body-tics"));
        Assert.NotEmpty(Hits("脸色刷地一下惨白。", "fiction", "fiction-body-tics"));
        Assert.NotEmpty(Hits("眉头拧成川字。", "fiction", "fiction-body-tics"));
        Assert.NotEmpty(Hits("发出蚊子般的声音细若蚊蝇。", "fiction", "fiction-body-tics"));
        Assert.NotEmpty(Hits("一边媚眼如丝吐气如兰。", "fiction", "fiction-body-tics"));
        Assert.NotEmpty(Hits("用一根手指勾起你的下巴。", "fiction", "fiction-body-tics"));
    }

    [Fact]
    public void 表情与体态的日常写法不报()
    {
        Assert.Empty(Hits("她脸红了，低头喝了口水。", "fiction", "fiction-body-tics"));
        Assert.Empty(Hits("他的脸色不太好，可能是没睡好。", "fiction", "fiction-body-tics"));
        Assert.Empty(Hits("眉头皱了一下，没说话。", "fiction", "fiction-body-tics"));
        Assert.Empty(Hits("下巴上有一道陈年的疤。", "fiction", "fiction-body-tics"));
        Assert.Empty(Hits("她说话声音很细，尾音往下垂。", "fiction", "fiction-body-tics"));
    }

    [Fact]
    public void 石子族要比喻标记才算套话()
    {
        // 「她把一颗石子丢进水里」是写实动作，AI 的「石子丢入湖面」在字面上与它同形，
        // 所以放宽只放到比喻标记与心理水体，不放动词白名单
        Assert.NotEmpty(Hits("这句话像一颗投入心湖的石子。", "fiction", "fiction-community-taste"));
        Assert.NotEmpty(Hits("一句话掀起滔天巨浪。", "fiction", "fiction-community-taste"));
        Assert.Empty(Hits("她把一颗石子丢进水里，看着那圈纹路散开。", "fiction", "fiction-community-taste"));
    }
}
