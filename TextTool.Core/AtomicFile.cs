using System.Text;

namespace TextTool.Services;

/// <summary>
/// 原子文件写入：先写同目录临时文件，再整体替换（Move）。
/// 进程中途崩溃不会留下半写文件；替换目录项也不跟随硬链接/重解析点
/// 改写共享目录中的其他链接。
/// </summary>
public static class AtomicFile
{
    public static void WriteAllLines(string path, IEnumerable<string> lines, Encoding encoding) =>
        Write(path, tmp => File.WriteAllLines(tmp, lines, encoding));

    public static void WriteAllText(string path, string content, Encoding encoding) =>
        Write(path, tmp => File.WriteAllText(tmp, content, encoding));

    private static void Write(string path, Action<string> write)
    {
        string tmp = path + ".tmp";
        write(tmp);
        File.Move(tmp, path, overwrite: true);
    }
}
