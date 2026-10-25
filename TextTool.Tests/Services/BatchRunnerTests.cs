namespace TextTool.Tests.Services;

public class BatchRunnerTests
{
    /// <summary>
    /// 同步收集器：BatchRunner 在 Task.Run 里同步调用 progress.Report，
    /// 用 Progress&lt;T&gt; 会把回调封送到同步上下文导致测试拿不到结果，所以自己实现。
    /// </summary>
    private sealed class RecordingProgress : IProgress<(int Done, string Error)>
    {
        private readonly List<(int Done, string Error)> _reports = new();

        public List<(int Done, string Error)> Reports
        {
            get { lock (_reports) return new List<(int Done, string Error)>(_reports); }
        }

        public void Report((int Done, string Error) value)
        {
            lock (_reports) _reports.Add(value);
        }
    }

    [Fact]
    public async Task RunAsync_全部成功_返回成功数且逐条报进度()
    {
        var files = new[] { "a.txt", "b.txt", "c.txt" };
        var calls = new List<string>();
        var progress = new RecordingProgress();

        var result = await BatchRunner.RunAsync(
            files,
            (file, _) => { lock (calls) calls.Add(file); },
            progress,
            CancellationToken.None);

        Assert.Equal(3, result.Success);
        Assert.False(result.Cancelled);

        Assert.Equal(files, calls.ToArray());

        var reports = progress.Reports;
        Assert.Equal(3, reports.Count);
        Assert.Equal(new[] { 1, 2, 3 }, reports.Select(r => r.Done));
        Assert.All(reports, r => Assert.Equal("", r.Error));
    }

    [Fact]
    public async Task RunAsync_中间文件失败_计数不含它且后续继续处理()
    {
        var files = new[] { "a.txt", "b.txt", "c.txt" };
        var calls = new List<string>();
        var progress = new RecordingProgress();

        var result = await BatchRunner.RunAsync(
            files,
            (file, _) =>
            {
                lock (calls) calls.Add(file);
                if (file == "b.txt") throw new InvalidOperationException("坏掉了");
            },
            progress,
            CancellationToken.None);

        Assert.Equal(2, result.Success);
        Assert.False(result.Cancelled);

        // 失败的那个之后的文件仍然被处理
        Assert.Equal(files, calls.ToArray());

        var reports = progress.Reports;
        Assert.Equal(3, reports.Count);

        // 序号是 1 基：第 2 个文件失败，错误挂在序号 2 上
        Assert.Equal(2, reports[1].Done);
        Assert.Equal("坏掉了", reports[1].Error);

        // 成功的那两个错误为空
        Assert.Equal("", reports[0].Error);
        Assert.Equal("", reports[2].Error);

        // 最后一轮结束时错误为空字符串
        Assert.Equal("", reports[^1].Error);
    }

    [Fact]
    public async Task RunAsync_处理函数抛取消异常_立即停下且后续不再调用()
    {
        var files = new[] { "a.txt", "b.txt", "c.txt" };
        var calls = new List<string>();
        var progress = new RecordingProgress();

        var result = await BatchRunner.RunAsync(
            files,
            (file, _) =>
            {
                lock (calls) calls.Add(file);
                if (file == "b.txt") throw new OperationCanceledException();
            },
            progress,
            CancellationToken.None);

        Assert.Equal(1, result.Success);
        Assert.True(result.Cancelled);

        // 只有 a、b 被调用，c 没有被调用
        Assert.Equal(new[] { "a.txt", "b.txt" }, calls);
    }

    // 这条曾经是红的：BatchRunner 把 token 也传给了 Task.Run，令牌在委托开始前取消时
    // 任务直接进取消态、委托不执行，await 抛 TaskCanceledException，走不到循环里的
    // IsCancellationRequested 判断。调用方把异常当"处理失败"报给用户，主动取消会变成
    // 一条错误提示。现在 Task.Run 不再接 token，本条锁定"取消返回 (0,true) 而不是抛"。
    [Fact]
    public async Task RunAsync_外部令牌已取消_不处理任何文件并报取消()
    {
        var files = new[] { "a.txt", "b.txt", "c.txt" };
        var calls = new List<string>();
        var progress = new RecordingProgress();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await BatchRunner.RunAsync(
            files,
            (file, _) => { lock (calls) calls.Add(file); },
            progress,
            cts.Token);

        Assert.Equal(0, result.Success);
        Assert.True(result.Cancelled);
        Assert.Empty(calls);
    }

    [Fact]
    public async Task RunAsync_空文件列表_返回零且不抛异常()
    {
        var calls = new List<string>();
        var progress = new RecordingProgress();

        var result = await BatchRunner.RunAsync(
            Array.Empty<string>(),
            (file, _) => { lock (calls) calls.Add(file); },
            progress,
            CancellationToken.None);

        Assert.Equal(0, result.Success);
        Assert.False(result.Cancelled);
        Assert.Empty(calls);
        Assert.Empty(progress.Reports);
    }
}
