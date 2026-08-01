namespace TextTool.Services;

/// <summary>
/// 文件路径辅助方法 —— 消除 LineMerger / ReplaceTabControl 中 _Processed 路径生成的重复。
/// </summary>
public static class PathHelper
{
    /// <summary>
    /// 根据输入文件路径生成 "*_Processed.*" 输出路径。
    /// </summary>
    public static string GetProcessedPath(string inputPath)
    {
        string dir = Path.GetDirectoryName(inputPath) ?? ".";
        string name = Path.GetFileNameWithoutExtension(inputPath);
        string ext = Path.GetExtension(inputPath);
        return Path.Combine(dir, $"{name}_Processed{ext}");
    }
}
