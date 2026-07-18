using System.Text;

namespace TextTool.Services;

/// <summary>
/// 行合并后处理选项。
/// 将分散的 bool/string 参数打包为一条 record，避免方法签名膨胀。
/// </summary>
public record PostProcessOptions(
    bool FixCjk,
    bool FixPunct,
    string PunctChars,
    bool NoMerge,
    string NoMergeChars,
    bool ApplyReplace,
    bool TrimLeadingComma,
    List<ReplaceRule> Rules
);

/// <summary>
/// 处理流水线：将行合并 → CJK 修复 → 标点截断修复 → 标点替换串联为一次性内存操作，
/// 仅在最后一步写入文件，消除旧版多步各写一次/读回一次的 I/O 开销。
///
/// noMerge 规则在此统一预编译为 HashSet，所有 Merger 共享同一份 set，
/// 后续新增 Merger 只需接受这个 set 参数即可获得 noMerge 支持。
/// </summary>
public static class ProcessingPipeline
{
    /// <summary>
    /// 执行完整处理流水线。
    /// </summary>
    /// <param name="inputPath">输入文件路径</param>
    /// <param name="encoding">输入文件编码</param>
    /// <param name="mergeOpts">行合并配置</param>
    /// <param name="postProcess">后处理选项</param>
    /// <returns>处理结果记录</returns>
    public static ProcessingResult Run(
        string inputPath, Encoding encoding,
        MergeOptions mergeOpts,
        PostProcessOptions postProcess)
    {
        // 预编译 set（一次性构造，所有 Merger 共享，需在 Step 1 前准备好供 LineMerger 使用）
        HashSet<char>? noMergeSet = postProcess.NoMerge && !string.IsNullOrEmpty(postProcess.NoMergeChars)
            ? new HashSet<char>(postProcess.NoMergeChars) : null;

        // Step 1: 行合并（内存操作，不写盘）
        var (lines, outputPath) = LineMerger.Merge(inputPath, mergeOpts, encoding, noMergeSet);

        // Step 2: 中文截断修复（内存操作）
        bool cjkApplied = false;
        if (postProcess.FixCjk)
        {
            var fixedLines = CjkParagraphMerger.Fix(lines, noMergeSet);
            cjkApplied = fixedLines.Count != lines.Count;
            lines = fixedLines;
        }

        // Step 3: 标点截断修复（内存操作）
        bool punctApplied = false;
        if (postProcess.FixPunct && !string.IsNullOrEmpty(postProcess.PunctChars))
        {
            var mergeSet = new HashSet<char>(postProcess.PunctChars);
            var fixedLines = PunctTruncationMerger.Fix(lines, mergeSet, noMergeSet);
            punctApplied = fixedLines.Count != lines.Count;
            lines = fixedLines;
        }

        // Step 4: 去除行首逗号（内存操作）
        bool trimApplied = false;
        if (postProcess.TrimLeadingComma)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].Length > 0 && lines[i][0] == '，')
                {
                    lines[i] = lines[i].Substring(1);
                    trimApplied = true;
                }
            }
        }

        // Step 5: 标点替换（内存操作）
        bool replaceApplied = false;
        if (postProcess.ApplyReplace && postProcess.Rules.Count > 0)
        {
            for (int i = 0; i < lines.Count; i++)
                lines[i] = PunctuationReplacer.Apply(lines[i], postProcess.Rules);
            replaceApplied = true;
        }

        // 一次性写入 UTF-8 with BOM
        File.WriteAllLines(outputPath, lines, new UTF8Encoding(true));

        return new ProcessingResult(lines, outputPath, cjkApplied, punctApplied, trimApplied, replaceApplied);
    }
}

/// <summary>
/// 处理流水线执行结果
/// </summary>
public record ProcessingResult(
    List<string> Lines,
    string OutputPath,
    bool CjkFixed,
    bool PunctFixed,
    bool Trimmed,
    bool Replaced
);
