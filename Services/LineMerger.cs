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
    /// <returns>输出文件路径</returns>
    public static string Merge(string inputPath, MergeOptions options, Encoding encoding)
    {
        string dir = Path.GetDirectoryName(inputPath) ?? ".";
        string name = Path.GetFileNameWithoutExtension(inputPath);
        string ext = Path.GetExtension(inputPath);
        string outputPath = Path.Combine(dir, $"{name}_Processed{ext}");

        string[] lines = File.ReadAllLines(inputPath, encoding);
        var result = new List<string>();

        string currentLine = "";

        foreach (string line in lines)
        {
            if (currentLine == "")
            {
                currentLine = line;
            }
            else
            {
                currentLine += line;
            }

            // 持续追加直到达到阈值，然后输出并重置
            if (MeasureWidth(currentLine, options.Mode, encoding) >= options.Threshold)
            {
                result.Add(currentLine);
                currentLine = "";
            }
        }

        // 处理尾部未达阈值的残余行
        if (currentLine != "")
            result.Add(currentLine);

        // 输出为 UTF-8 with BOM
        File.WriteAllLines(outputPath, result, new UTF8Encoding(true));

        return outputPath;
    }

    private static int MeasureWidth(string text, MergeMode mode, Encoding encoding)
    {
        if (mode == MergeMode.ByteCount)
            return encoding.GetByteCount(text);
        else
            return text.Length;
    }
}
