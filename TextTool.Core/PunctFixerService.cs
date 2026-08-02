using System.Text;

namespace TextTool.Services;

/// <summary>
/// 对话标点补齐器 — 移植自 vn_tools.punct.PunctFixer (Python)。
/// 自动给「人物」「台词」格式的对话行补充句末标点。
/// </summary>
public sealed class PunctFixerService
{
    private readonly HashSet<char> _terminalChars;

    /// <param name="terminalChars">被视为句末标点的字符集合，匹配则跳过补点。</param>
    public PunctFixerService(string terminalChars = "。！？…")
    {
        _terminalChars = new HashSet<char>(terminalChars);
    }

    // ================================================================
    //  公开 API
    // ================================================================

    /// <summary>处理文本，给对话行补句号。</summary>
    public string Fix(string text)
    {
        var lines = text.Split('\n');
        var result = new string[lines.Length];

        for (int i = 0; i < lines.Length; i++)
            result[i] = ProcessLine(lines[i]);

        return string.Join("\n", result);
    }

    /// <summary>处理文件，写出结果。</summary>
    public void FixFile(string inputPath, string outputPath, Encoding encoding)
    {
        string content = File.ReadAllText(inputPath, encoding);
        string result = Fix(content);
        File.WriteAllText(outputPath, result, new UTF8Encoding(true));
    }

    // ================================================================
    //  核心逻辑
    // ================================================================

    private string ProcessLine(string line)
    {
        string s = line.TrimEnd('\r');
        if (!IsDialogueLine(s))
            return line;

        int lastClose = s.LastIndexOf('」');
        if (lastClose < 0)
            return line;

        // 引号之后已以句末标点结尾（如「…」？），不再补点
        if (lastClose < s.Length - 1)
        {
            string tail = s[(lastClose + 1)..].TrimEnd();
            if (tail.Length > 0 && _terminalChars.Contains(tail[^1]))
                return line;
        }

        // 越过末尾的引号字符（支持嵌套引号）
        int i = lastClose - 1;
        while (i >= 0 && (s[i] == '」' || s[i] == '』' || s[i] == '\'' || s[i] == '"'))
            i--;

        if (i < 0)
            return line;

        if (_terminalChars.Contains(s[i]))
            return line;

        // 在最后一个 」前插入 。
        return s[..lastClose] + "。" + s[lastClose..];
    }

    private static bool IsDialogueLine(string s) =>
        DialogueLine.IsDialogueLine(s);
}
