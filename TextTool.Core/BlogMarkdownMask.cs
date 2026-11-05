namespace TextTool.Services;

/// <summary>
/// 博文稿的结构遮罩：标出 markdown 里不属于正文的那些字符，检查与标点替换都不碰它们。
///
/// 认哪些区域，按本博客的写法定：
/// <list type="bullet">
/// <item>开头的头部字段：第一行是 <c>---</c>，到下一个 <c>---</c> 或 <c>...</c> 收尾。题目、摘要、标签在这里。</item>
/// <item>三冒号指令行：<c>:::admon[...]</c>、<c>:::fold[...]</c>、<c>::card{...}</c>、收尾的 <c>:::</c>。
/// 只遮这一行本身，指令包着的正文照样算正文。</item>
/// <item>围栏代码块（``` 与 ~~~）整段，含两头的围栏行；行内代码连反引号一起。</item>
/// <item>公式：<c>$...$</c>、<c>$$...$$</c>，以及整行的 <c>$$</c> 块。</item>
/// <item>链接与图片的地址部分：<c>]()</c> 里那一段，方括号里的文字留着；自动链接整条。</item>
/// <item>引用式链接定义整行、HTML 注释整段。</item>
/// </list>
///
/// 判断一份稿子是不是博文：出现头部字段或三冒号指令行才算，两样都没有就不是博文，
/// 调用方拿到的遮罩为 null，后续行为与不认博文时完全一致。所以普通 txt 稿子一个字都不会变。
///
/// 遮罩与原文等长、逐位对齐，换行不动：命中的位置、行列、Match 仍按原文取，
/// 段落切分也照原文的换行进行，被遮住的字符只当空格看。
/// </summary>
public sealed class BlogMarkdownMask
{
    private readonly bool[] _masked;

    private BlogMarkdownMask(bool[] masked)
    {
        _masked = masked;
        int count = 0;
        foreach (bool bit in masked)
            if (bit) count++;
        MaskedChars = count;
    }

    /// <summary>被遮住的字符数，换行也算在内。<see cref="AiToneLintService"/> 用它从字数里扣掉。</summary>
    public int MaskedChars { get; }

    /// <summary>
    /// 读一份稿子，认出博文结构并建遮罩。不像博文时返回 null。
    /// </summary>
    public static BlogMarkdownMask? Create(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;

        var masked = new bool[text.Length];
        bool sawStructure = false;
        bool inFrontMatter = false;
        bool inFence = false;
        char fenceChar = '\0';
        int fenceLength = 0;
        bool inComment = false;
        bool inMathBlock = false;
        int pos = 0;
        int lineNo = 0;

        while (true)
        {
            int newline = text.IndexOf('\n', pos);
            int lineEnd = newline < 0 ? text.Length : newline;
            string line = text[pos..lineEnd].TrimEnd('\r');
            string trimmed = line.Trim();
            bool wholeLine = false;

            if (inFrontMatter)
            {
                wholeLine = true;
                if (trimmed is "---" or "...") inFrontMatter = false;
            }
            else if (inFence)
            {
                wholeLine = true;
                if (ClosesFence(trimmed, fenceChar, fenceLength)) inFence = false;
            }
            else if (inComment)
            {
                wholeLine = true;
                if (trimmed.Contains("-->", StringComparison.Ordinal)) inComment = false;
            }
            else if (inMathBlock)
            {
                wholeLine = true;
                if (trimmed.Contains("$$", StringComparison.Ordinal)) inMathBlock = false;
            }
            else if (lineNo == 0 && IsFrontMatterDelimiter(trimmed))
            {
                wholeLine = true;
                inFrontMatter = true;
                // 开头一个 --- 不算证据：纯文本也拿 --- 当分隔线，所以还要看这段里有没有键值行
                sawStructure |= LooksLikeFrontMatter(text, lineEnd);
            }
            else if (OpensFence(trimmed, out fenceChar, out fenceLength))
            {
                wholeLine = true;
                inFence = true;
            }
            else if (trimmed.StartsWith("$$", StringComparison.Ordinal)
                     && !trimmed[2..].Contains("$$", StringComparison.Ordinal))
            {
                wholeLine = true;
                inMathBlock = true;
            }
            else if (trimmed.StartsWith("::", StringComparison.Ordinal))
            {
                wholeLine = true;
                sawStructure = true;
            }
            else if (IsReferenceDefinition(trimmed))
            {
                wholeLine = true;
            }

            // 整行被遮时连它的换行一起遮：这一行不参与，换行也不该算进正文字数
            if (wholeLine)
                Mask(masked, pos, newline < 0 ? lineEnd : newline + 1);
            else
                MaskInline(text, masked, pos, lineEnd, ref inComment);

            if (newline < 0) break;
            pos = newline + 1;
            lineNo++;
            if (pos >= text.Length) break;
        }

        return sawStructure ? new BlogMarkdownMask(masked) : null;
    }

    /// <summary>
    /// 把遮住的字符换成空格，换行原样留着。给规则与统计用：空格匹配不上任何词，
    /// 整行被遮的段落又会被当成空行，于是这些区域既不报命中，也不进篇幅统计。
    /// </summary>
    public string Apply(string text)
    {
        var buffer = new char[text.Length];
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            buffer[i] = _masked[i] && c is not ('\n' or '\r') ? ' ' : c;
        }
        return new string(buffer);
    }

    /// <summary>这个位置是不是被遮住的区域。给需要自己看原文的调用方用。</summary>
    public bool IsMasked(int index) => index >= 0 && index < _masked.Length && _masked[index];

    private static void Mask(bool[] masked, int from, int to)
    {
        for (int i = Math.Max(0, from); i < Math.Min(to, masked.Length); i++)
            masked[i] = true;
    }

    /// <summary>首行是 --- 才算头部字段。字节序标记不算字。</summary>
    private static bool IsFrontMatterDelimiter(string trimmed) =>
        trimmed.TrimStart('\uFEFF').Trim() == "---";

    /// <summary>
    /// 收尾分隔符之前有没有 YAML 键值行（<c>title: 标题</c> 这种）。
    /// 有才当头部字段：纯文本拿 --- 当分隔线的时候，后面那一段多半是正文，遮了就等于没检查。
    /// </summary>
    private static bool LooksLikeFrontMatter(string text, int afterOpeningLine)
    {
        int pos = Math.Min(afterOpeningLine + 1, text.Length);
        for (int lines = 0; lines < 200 && pos < text.Length; lines++)
        {
            int newline = text.IndexOf('\n', pos);
            int lineEnd = newline < 0 ? text.Length : newline;
            string trimmed = text[pos..lineEnd].Trim();
            if (trimmed is "---" or "...") return false;
            if (HasYamlKey(trimmed)) return true;
            if (newline < 0) return false;
            pos = newline + 1;
        }
        return false;
    }

    /// <summary>键值行的形状：行首是英文键名，紧跟一个冒号。汉字键名不算，那是正文里的「注意：」。</summary>
    private static bool HasYamlKey(string trimmed)
    {
        int i = 0;
        if (i >= trimmed.Length || !char.IsAsciiLetter(trimmed[i])) return false;
        while (i < trimmed.Length && (char.IsAsciiLetterOrDigit(trimmed[i]) || trimmed[i] is '_' or '-'))
            i++;
        return i < trimmed.Length && trimmed[i] == ':';
    }

    private static bool OpensFence(string trimmed, out char fenceChar, out int fenceLength)
    {
        fenceChar = trimmed.Length > 0 ? trimmed[0] : '\0';
        fenceLength = 0;
        if (fenceChar is not ('`' or '~')) return false;
        while (fenceLength < trimmed.Length && trimmed[fenceLength] == fenceChar) fenceLength++;
        return fenceLength >= 3;
    }

    private static bool ClosesFence(string trimmed, char fenceChar, int fenceLength)
    {
        if (trimmed.Length < fenceLength) return false;
        for (int i = 0; i < trimmed.Length; i++)
            if (trimmed[i] != fenceChar) return false;
        return true;
    }

    /// <summary>引用式链接定义：<c>[名字]: 地址</c>。整行没有正文，全遮。</summary>
    private static bool IsReferenceDefinition(string trimmed)
    {
        if (!trimmed.StartsWith('[')) return false;
        int close = trimmed.IndexOf("]:", StringComparison.Ordinal);
        return close > 1;
    }

    /// <summary>行内那几样：行内代码、公式、链接地址、自动链接、HTML 注释。</summary>
    private static void MaskInline(string text, bool[] masked, int start, int end, ref bool inComment)
    {
        int i = start;
        while (i < end)
        {
            char c = text[i];

            if (c == '`')
            {
                int open = RunLength(text, i, end, '`');
                int close = FindBacktickRun(text, i + open, end, open);
                if (close < 0)
                {
                    i += open;
                    continue;
                }
                Mask(masked, i, close + open);
                i = close + open;
                continue;
            }

            if (c == '<')
            {
                if (StartsWith(text, i, end, "<!--"))
                {
                    int close = text.IndexOf("-->", i + 4, end - i - 4, StringComparison.Ordinal);
                    if (close < 0)
                    {
                        // 注释跨行：这一行的尾巴也遮掉，后面的行由 inComment 接着遮
                        Mask(masked, i, end);
                        inComment = true;
                        return;
                    }
                    Mask(masked, i, close + 3);
                    i = close + 3;
                    continue;
                }
                if (LooksLikeAutolink(text, i, end, out int tagEnd))
                {
                    Mask(masked, i, tagEnd);
                    i = tagEnd;
                    continue;
                }
            }

            if (c == '$' && TryMaskFormula(text, masked, i, end, out int afterFormula))
            {
                i = afterFormula;
                continue;
            }

            if (c == ']' && i + 1 < end && text[i + 1] == '(')
            {
                // 链接与图片只遮地址：方括号里那截是给人看的字，照常检查
                int close = text.IndexOf(')', i + 2, end - i - 2);
                if (close >= 0)
                {
                    Mask(masked, i + 1, close + 1);
                    i = close + 1;
                    continue;
                }
            }

            i++;
        }
    }

    private static int RunLength(string text, int start, int end, char c)
    {
        int i = start;
        while (i < end && text[i] == c) i++;
        return i - start;
    }

    private static int FindBacktickRun(string text, int from, int end, int length)
    {
        for (int i = from; i < end;)
        {
            if (text[i] != '`')
            {
                i++;
                continue;
            }
            int run = RunLength(text, i, end, '`');
            if (run == length) return i;
            i += run;
        }
        return -1;
    }

    private static bool StartsWith(string text, int start, int end, string value)
    {
        if (start + value.Length > end) return false;
        return string.CompareOrdinal(text, start, value, 0, value.Length) == 0;
    }

    private static bool LooksLikeAutolink(string text, int start, int end, out int tagEnd)
    {
        tagEnd = start;
        if (!StartsWith(text, start, end, "<http://")
            && !StartsWith(text, start, end, "<https://")
            && !StartsWith(text, start, end, "<mailto:"))
            return false;
        int close = text.IndexOf('>', start, end - start);
        if (close < 0) return false;
        tagEnd = close + 1;
        return true;
    }

    /// <summary>
    /// 行内公式：<c>$...$</c> 与 <c>$$...$$</c>。开记号后面紧跟空白的不算，收记号前面是空白的也不算，
    /// 单 $ 还要求两边不是数字，免得把「一件 $5 一件 $10」当成公式遮掉。
    /// </summary>
    private static bool TryMaskFormula(string text, bool[] masked, int start, int end, out int after)
    {
        after = start;
        int marker = start + 1 < end && text[start + 1] == '$' ? 2 : 1;
        int open = start + marker;
        if (open >= end) return false;

        char first = text[open];
        if (char.IsWhiteSpace(first)) return false;
        if (marker == 1 && char.IsAsciiDigit(first)) return false;

        string fence = marker == 2 ? "$$" : "$";
        int close = text.IndexOf(fence, open, end - open, StringComparison.Ordinal);
        if (close < 0) return false;
        if (char.IsWhiteSpace(text[close - 1])) return false;
        if (marker == 1 && close + 1 < end && char.IsAsciiDigit(text[close + 1])) return false;

        Mask(masked, start, close + marker);
        after = close + marker;
        return true;
    }
}
