using System.Text;

namespace TextTool.Services;

/// <summary>
/// 合并模式：按字符数 或 按字节数
/// </summary>
public enum MergeMode
{
    CharCount,
    ByteCount
}

/// <summary>
/// 行合并配置
/// </summary>
public class MergeOptions
{
    public int Threshold { get; set; } = 20;
    public MergeMode Mode { get; set; } = MergeMode.ByteCount;
}

/// <summary>
/// 核心行合并引擎。
/// 将短于阈值的行逐行拼接，直到达到阈值后输出。
/// 永不修改原文件，输出到 "*_Processed.*"
/// </summary>
public static class LineMerger
{
    /// <summary>
    /// 合并短行并输出。
    /// 返回 (内存行列表, 输出文件路径)，调用方可直接操作 Lines 避免第二次文件 I/O。
    /// </summary>
    public static (List<string> Lines, string OutputPath) Merge(string inputPath, MergeOptions options, Encoding encoding)
    {
        string dir = Path.GetDirectoryName(inputPath) ?? ".";
        string name = Path.GetFileNameWithoutExtension(inputPath);
        string ext = Path.GetExtension(inputPath);
        string outputPath = Path.Combine(dir, $"{name}_Processed{ext}");

        string[] lines = File.ReadAllLines(inputPath, encoding);
        var result = new List<string>(lines.Length);
        var sb = new StringBuilder();
        int cumulativeWidth = 0;

        foreach (string line in lines)
        {
            sb.Append(line);
            cumulativeWidth += MeasureWidth(line, options.Mode, encoding);

            if (cumulativeWidth >= options.Threshold)
            {
                result.Add(sb.ToString());
                sb.Clear();
                cumulativeWidth = 0;
            }
        }

        if (sb.Length > 0)
            result.Add(sb.ToString());

        return (result, outputPath);
    }

    private static int MeasureWidth(string text, MergeMode mode, Encoding encoding)
    {
        if (mode == MergeMode.ByteCount)
            return encoding.GetByteCount(text);
        else
            return text.Length;
    }
}
