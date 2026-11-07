using System.Text;

namespace TextTool.Services;

/// <summary>
/// 原子文件写入：先写同目录临时文件，再整体替换（Move）。
/// 进程中途崩溃不会留下半写文件；替换目录项也不跟随硬链接/重解析点
/// 改写共享目录中的其他链接。
/// 失败时临时文件立即删掉，目录里只留原文件。
/// </summary>
public static class AtomicFile
{
    public static void WriteAllLines(string path, IEnumerable<string> lines, Encoding encoding) =>
        Write(path, tmp => File.WriteAllLines(tmp, lines, encoding));

    public static void WriteAllText(string path, string content, Encoding encoding) =>
        Write(path, tmp => File.WriteAllText(tmp, content, encoding));

    /// <summary>
    /// 先写临时文件再整体替换。写与替换任一步失败都把临时文件删掉再抛：
    /// 批量跑不会因为一份失败就停，留下 .tmp 就是让用户在目录里看见一个自己没建过的文件。
    /// </summary>
    private static void Write(string path, Action<string> write)
    {
        string tmp = path + ".tmp";
        try
        {
            write(tmp);
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            try
            {
                File.Delete(tmp);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 删不掉也不能盖住真正的失败原因：当时抛的那个更有用
            }
            throw;
        }
    }
}
