using System.Text;

namespace TextTool.Services;

/// <summary>
/// 文件拼接：将目录中所有匹配文件合并为一个文件。
/// 每个文件按自身编码读取，输出为 UTF-8 with BOM。
/// </summary>
public static class FileJoiner
{
    /// <returns>输出文件路径</returns>
    public static string Join(string directory, string pattern, string outputName)
    {
        string outputPath = Path.Combine(directory, outputName);
        var files = Directory.GetFiles(directory, pattern)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        using var writer = new StreamWriter(outputPath, false, new UTF8Encoding(true));

        foreach (string file in files)
        {
            var detection = EncodingDetector.Detect(file);
            string content = File.ReadAllText(file, detection.Encoding);
            writer.Write(content);

            // 确保文件间有换行分隔
            if (!content.EndsWith("\n") && !content.EndsWith("\r\n"))
                writer.WriteLine();
        }

        return outputPath;
    }
}
