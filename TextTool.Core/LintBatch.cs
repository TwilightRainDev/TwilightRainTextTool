namespace TextTool.Services;

/// <summary>
/// 并行扫一批稿：几份同时读、同时扫，结果仍按传进来的文件顺序交回。
///
/// 顺序是给读者的：人读报告按文件名排，命中日志一行一条，失败提示也各有各的位置。
/// 并行只压缩等待，不改变这些顺序，逐份跑与并行跑拿到的是同一份输出。
///
/// 同时开工的份数有上限，内存峰值是一窗的稿子加一窗的报告，不是一个目录的全部。
/// 引擎（<see cref="LintSession"/>）扫描时不改自身状态，同一个实例并发扫多份与逐份扫结果相同。
/// </summary>
public static class LintBatch
{
    /// <summary>
    /// 一批里同时开工的最多份数。默认按核数取，上限 8：
    /// 再往上加在真实目录上换不来时间——读盘与扫描各自都已接近饱和——只把内存峰值抬高。
    /// </summary>
    public const int MaxWorkers = 8;

    /// <summary>
    /// 一份的结果：扫出报告时 <see cref="Report"/> 有值，读不了时 <see cref="Error"/> 有值，两者必有一个。
    /// 读不了不抛给调用方：那一份在原位置报出来，其余照常往下跑。
    /// </summary>
    public readonly record struct Item(string File, LintReport? Report, Exception? Error);

    /// <summary>
    /// 跑完一批。<paramref name="read"/> 读一份稿，抛异常表示这份读不了；
    /// 扫由本方法调引擎完成。<paramref name="onItem"/> 在调用本方法的线程上按文件顺序逐份调用，
    /// 所以它可以放心地写输出流、记录计数器，不必自己加锁。
    /// </summary>
    /// <param name="workers">同时开工的份数；不给按核数取。给 1 就是逐份跑，结果与按核数并行取到的一样。</param>
    public static void Run(
        IReadOnlyList<string> files,
        LintSession session,
        Func<string, string> read,
        Action<Item> onItem,
        int? workers = null)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(onItem);

        int width = workers is int given
            ? Math.Clamp(given, 1, MaxWorkers)
            : Math.Clamp(Environment.ProcessorCount, 1, MaxWorkers);

        if (width == 1 || files.Count <= 1)
        {
            foreach (string file in files)
                onItem(One(file, session, read));
            return;
        }

        // 滑动窗口：窗口里的几份同时开工，按顺序交回，交回时只等队首。
        // 队首要等多久，窗口里其余几份的时间都并进这段时间里；在跑的份数不超过一窗。
        var running = new Queue<Task<Item>>(width);
        int next = 0;
        while (next < files.Count || running.Count > 0)
        {
            while (next < files.Count && running.Count < width)
            {
                string file = files[next++];
                running.Enqueue(Task.Run(() => One(file, session, read)));
            }
            onItem(running.Dequeue().GetAwaiter().GetResult());
        }
    }

    /// <summary>
    /// 读一份并扫一份。读不了的那一份带着原因交回，同批其余照常出结果：
    /// 原因与位置都落在它自己的顺序上，调用方按顺序读即可。
    /// </summary>
    private static Item One(string file, LintSession session, Func<string, string> read)
    {
        try
        {
            return new Item(file, session.Scan(file, read(file)), null);
        }
        catch (Exception ex)
        {
            return new Item(file, null, ex);
        }
    }
}
