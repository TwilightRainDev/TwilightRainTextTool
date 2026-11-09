using System.Diagnostics;

namespace TextTool.Tests.Services;

/// <summary>
/// 扫描速度的上限。
///
/// 护栏在每条回复、每次写文件时都调这个工具，给它两秒。工具慢到越线，那条判据会静默停用——
/// 读者不会看到「它没检查」，只看到一句提示。所以速度在这里是正确性的一部分，要有一条用例盯着。
///
/// 上限取两秒，与护栏给的上限同数；量引擎内扫描，不含进程启动。
/// 本机三十五万字的稿子约零点四秒。这条用例拦的是「慢了一个数量级」的退步，
/// 不追毫秒级波动：门限留在上限，机器慢一档也照过。
/// </summary>
public class LintSpeedTests
{
    /// <summary>护栏给的时限，单位毫秒。</summary>
    private const long BudgetMs = 2000;

    /// <summary>稿子字数。三十五万字约一兆字节的 UTF-8。</summary>
    private const int Chars = 350_000;

    [Fact]
    public void 扫描三十五万字的稿子在两秒之内()
    {
        string text = 稿件(Chars);
        var session = LintSession.Create("doc", null, null);

        // 先扫一遍：正则编译、词库加载、JIT 都在这一遍里做完，下面量的是稳定态。
        // 计时把这一遍算进去的话，测到的多半是首扫的固定开销，与稿子大小无关。
        session.Scan("warm.md", text);

        var watch = Stopwatch.StartNew();
        var report = session.Scan("big.md", text);
        watch.Stop();

        Assert.True(report.Chars > 0, "扫完一个字都没读进来，这条用例量到的不是扫描时间。");
        Assert.True(watch.ElapsedMilliseconds < BudgetMs,
            $"三十五万字的稿子扫了 {watch.ElapsedMilliseconds} 毫秒，上限 {BudgetMs} 毫秒。");
    }

    /// <summary>
    /// 固定字数的稿子：句子按固定次序轮换，不掺随机数——同一份输入每次都一样，两次计时才有可比性。
    /// 二百五十六种组合，不至于全篇重复到让算法类规则按同一个形状反复报。
    /// </summary>
    private static string 稿件(int chars)
    {
        string[] 开头 = { "窗口", "名单", "报告", "段落", "规则", "编码", "目录", "命令行" };
        string[] 中段 = { "在下午三点开", "按路径排序", "只列命中位置", "先核对一遍", "照原样输出", "不攒批", "分两路读", "逐份落地" };
        string[] 结尾 = { "，值班的人先核对名单。", "，读者照着做就能走完。", "，其余部分不碰。", "，这一次没有例外。" };

        var text = new StringBuilder(chars + 128);
        for (int i = 0; text.Length < chars; i++)
            text.Append(开头[i % 8]).Append(中段[(i / 8) % 8]).Append(结尾[(i / 64) % 4]).Append('\n');
        return text.ToString();
    }
}
