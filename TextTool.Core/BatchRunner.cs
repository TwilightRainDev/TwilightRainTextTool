namespace TextTool.Services;

/// <summary>
/// 批量跑处理函数：逐文件执行，失败不中断，进度与错误通过 progress 回报。
///
/// 放在 Core 而不是界面层，因为两个界面都要用它，而且它的行为（失败继续、
/// 取消即停、取消不算失败）可以脱离界面测。
/// </summary>
public static class BatchRunner
{
    public static async Task<(int Success, bool Cancelled)> RunAsync(
        IReadOnlyList<string> files,
        Action<string, CancellationToken> processFile,
        IProgress<(int Done, string Error)>? progress,
        CancellationToken token)
    {
        return await Task.Run(() =>
        {
            int success = 0;
            for (int i = 0; i < files.Count; i++)
            {
                if (token.IsCancellationRequested)
                    return (success, true);

                try
                {
                    processFile(files[i], token);
                    success++;
                }
                catch (OperationCanceledException)
                {
                    return (success, true);
                }
                catch (Exception ex)
                {
                    // 失败的文件不再报成功进度，只把错误送出去。
                    progress?.Report((i + 1, ex.Message));
                    continue;
                }

                progress?.Report((i + 1, ""));
            }

            return (success, false);
        });
        // 不要把 token 传给 Task.Run：令牌在委托开始前就已取消时，Task.Run 会让任务直接进
        // 取消态并抛 TaskCanceledException，循环开头那句"已取消就返回"根本执行不到。
        // 调用方把异常当成"处理失败"报给用户，于是主动取消会变成一条错误提示。
    }
}
