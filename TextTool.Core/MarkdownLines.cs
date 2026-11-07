namespace TextTool.Services;

/// <summary>
/// markdown 逐行的形状判定：这一行在结构上算什么。围栏、列表行、编号行、引用行、
/// 头部字段、指令行这六类只在这里认，别处不再另写正则或前缀判断。
///
/// 这里只回答「这一行是什么」，以及围栏开着没有（<see cref="MarkdownFenceTracker"/>）。
/// 哪类算正文、哪类整行遮掉、哪类要单独成段，都是调用方自己的事，不在这里定。
/// </summary>
public static class MarkdownLines
{
    /// <summary>
    /// 围栏的开门形状：去空白后的行首是成串的三连以上反引号或波浪号。
    /// <paramref name="fenceLength"/> 是那一串的长度，关门时要用它比。
    /// </summary>
    public static bool OpensFence(string trimmed, out char fenceChar, out int fenceLength)
    {
        fenceChar = trimmed.Length > 0 ? trimmed[0] : '\0';
        fenceLength = 0;
        if (fenceChar is not ('`' or '~')) return false;
        while (fenceLength < trimmed.Length && trimmed[fenceLength] == fenceChar) fenceLength++;
        return fenceLength >= 3;
    }

    /// <summary>
    /// 这一行关不关掉开着的围栏：整行全由开门那个字符组成，长度不短于开门那次。
    /// 围栏行后面跟语言名（<c>```js</c>）不算关门，那是在块里又写了一行。
    /// </summary>
    public static bool ClosesFence(string trimmed, char fenceChar, int fenceLength)
    {
        if (trimmed.Length < fenceLength) return false;
        for (int i = 0; i < trimmed.Length; i++)
            if (trimmed[i] != fenceChar) return false;
        return true;
    }

    /// <summary>列表项：行首是短横、星号或加号，后面跟一个空格。裸的短横（分隔线、破折号开头）不算。</summary>
    public static bool IsListItem(string raw)
    {
        string t = raw.TrimStart();
        return t.StartsWith("- ", StringComparison.Ordinal)
            || t.StartsWith("* ", StringComparison.Ordinal)
            || t.StartsWith("+ ", StringComparison.Ordinal);
    }

    /// <summary>引用行：行首是尖括号。</summary>
    public static bool IsQuote(string raw) => raw.TrimStart().StartsWith(">", StringComparison.Ordinal);

    /// <summary>
    /// 编号行：行首是阿拉伯数字，紧跟句点或顿号，后面还有字。
    /// 数字后面没有东西的行（单独一个 <c>1.</c> 结尾）不算编号，那是正文里的序号收尾。
    /// </summary>
    public static bool IsNumberedItem(string raw)
    {
        string t = raw.TrimStart();
        int digits = 0;
        while (digits < t.Length && char.IsAsciiDigit(t[digits])) digits++;
        return digits > 0 && digits + 1 < t.Length && (t[digits] == '.' || t[digits] == '、');
    }

    /// <summary>头部字段的开门行：去空白后是 <c>---</c>。字节序标记不算字。</summary>
    public static bool IsFrontMatterOpening(string trimmed) =>
        trimmed.TrimStart('\uFEFF').Trim() == "---";

    /// <summary>头部字段的收尾行：<c>---</c> 或 <c>...</c>。开头找键值行时也用它认「到这里还没键值」。</summary>
    public static bool IsFrontMatterClosing(string trimmed) => trimmed is "---" or "...";

    /// <summary>这一行是不是给定的头部字段键，<c>title: 标题</c> 与 <c>title : 标题</c> 都算。</summary>
    public static bool IsFrontMatterKey(string trimmed, string key)
    {
        if (!trimmed.StartsWith(key, StringComparison.OrdinalIgnoreCase)) return false;
        string rest = trimmed[key.Length..];
        return rest.StartsWith(':') || rest.TrimStart().StartsWith(':');
    }

    /// <summary>
    /// 收尾分隔符之前有没有 YAML 键值行（<c>title: 标题</c> 这种）。
    /// 有才当头部字段：纯文本拿 <c>---</c> 当分隔线的时候，后面那一段多半是正文，遮了就等于没检查。
    /// <paramref name="afterOpeningLine"/> 是开门那行的末字下标。
    /// </summary>
    public static bool LooksLikeFrontMatter(string text, int afterOpeningLine)
    {
        int pos = Math.Min(afterOpeningLine + 1, text.Length);
        for (int lines = 0; lines < 200 && pos < text.Length; lines++)
        {
            int newline = text.IndexOf('\n', pos);
            int lineEnd = newline < 0 ? text.Length : newline;
            string trimmed = text[pos..lineEnd].Trim();
            if (IsFrontMatterClosing(trimmed)) return false;
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

    /// <summary>指令行：行首是双冒号。这是本站主题的写法，博文靠它认出来。</summary>
    public static bool IsDirective(string trimmed) => trimmed.StartsWith("::", StringComparison.Ordinal);
}

/// <summary>
/// 围栏的开关状态：一行一行喂进去，回答两件事——这一行是不是围栏行，喂完之后围栏开着没有。
///
/// 认法按 CommonMark：行首三连以上反引号或波浪号开门，关门要同一个字符、长度不短于开门那次。
/// 四个反引号的块里出现三个反引号不算关门，仍在块内。三个调用方（博文遮罩、段落切分、回复检查）
/// 都用这一套，不再各自定宽严。
/// </summary>
public sealed class MarkdownFenceTracker
{
    /// <summary>喂完上一行之后围栏是否还开着。</summary>
    public bool InFence { get; private set; }

    private char _fenceChar;
    private int _fenceLength;

    /// <summary>
    /// 喂一行（换行符不用带）。返回这一行是不是围栏行（开门或关门的那行）。不是围栏行时状态不变。
    /// </summary>
    public bool Advance(string rawLine)
    {
        // 按 trim 后的行首认：全角空格缩进的围栏行照样算
        string trimmed = rawLine.TrimEnd('\r').Trim();

        if (InFence)
        {
            if (!MarkdownLines.ClosesFence(trimmed, _fenceChar, _fenceLength)) return false;
            InFence = false;
            return true;
        }

        if (!MarkdownLines.OpensFence(trimmed, out char fenceChar, out int fenceLength)) return false;
        InFence = true;
        _fenceChar = fenceChar;
        _fenceLength = fenceLength;
        return true;
    }
}
