using System.Numerics;

namespace TextTool.Services;

/// <summary>
/// 博文稿的结构遮罩：认出这份稿子是不是博文，并标出其中不属于正文的那些字符。
///
/// 先认博文：头部字段里同时有 <c>title</c> 与 <c>date</c> 两行键值，或者出现过以 <c>::</c> 开头的行。
/// 这两个条件是 Hexo 与本站主题各自的招牌，普通 markdown 文档不会同时占。
/// 光凭「有头部字段」不行：VuePress、Docusaurus 那类文档站也有头部字段，而它们该按文档检查。
///
/// 认成博文之后才遮，遮这五类：开头的头部字段整段、以 <c>::</c> 开头的整行（指令行与收尾行）、
/// 围栏代码块与行内代码、<c>$...$</c> 与 <c>$$...$$</c> 公式、链接与图片的地址（连同引用式链接定义整行）。
/// 不是博文的稿子一个字都不遮，检查与替换按整篇来，行内代码里的文件名照旧能被判据看到。
///
/// 遮罩与原文等长、逐位对齐，换行不动：命中的位置、行列、Match 仍按原文取，
/// 段落切分也照原文的换行进行，被遮住的字符只当空格看。
///
/// 遮罩一个字符占一比特（五十兆的稿子占六兆），被遮的字数在构造时顺手数完，
/// <see cref="Apply"/> 只分配目标串一次。
///
/// 头部字段、指令行、围栏行这几类形状的判定取 <see cref="MarkdownLines"/> 与
/// <see cref="MarkdownFenceTracker"/>，围栏按严格口径；行内那几样（行内代码、公式、链接地址、
/// 自动链接、HTML 注释）在本文件里认。
/// </summary>
public sealed class BlogMarkdownMask
{
    /// <summary>遮罩本体；不是博文时为 null，一个字都不遮。</summary>
    private readonly BitMask? _mask;

    /// <summary>遮罩对得上的字符数，不是博文时为 0。</summary>
    private readonly int _length;

    private BlogMarkdownMask(BitMask? mask, int length, bool isBlogPost)
    {
        _mask = mask;
        _length = length;
        IsBlogPost = isBlogPost;
    }

    /// <summary>被遮住的字符数，换行也算在内。字数统计用它从原文长度里扣。</summary>
    public int MaskedChars => _mask?.Count ?? 0;

    /// <summary>
    /// 这份稿子按博文处理：头部字段里有 title 与 date，或者出现过 <c>::</c> 指令行。
    /// 不是博文时 <see cref="MaskedChars"/> 为 0，调用方按整篇处理。
    /// </summary>
    public bool IsBlogPost { get; }

    /// <summary>
    /// 读一份稿子，建遮罩。不是博文时返回空遮罩（<see cref="MaskedChars"/> 为 0），调用方不必判空。
    /// </summary>
    public static BlogMarkdownMask Create(string text)
    {
        if (string.IsNullOrEmpty(text)) return new BlogMarkdownMask(null, 0, false);

        var masked = new BitMask(text.Length);
        bool inFrontMatter = false;
        bool hasTitle = false;
        bool hasDate = false;
        bool sawDirective = false;
        var fence = new MarkdownFenceTracker();
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
                hasTitle |= MarkdownLines.IsFrontMatterKey(trimmed, "title");
                hasDate |= MarkdownLines.IsFrontMatterKey(trimmed, "date");
                if (MarkdownLines.IsFrontMatterClosing(trimmed)) inFrontMatter = false;
            }
            else if (fence.InFence)
            {
                wholeLine = true;
                fence.Advance(line);
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
            else if (lineNo == 0 && MarkdownLines.IsFrontMatterOpening(trimmed)
                     && MarkdownLines.LooksLikeFrontMatter(text, lineEnd))
            {
                wholeLine = true;
                inFrontMatter = true;
            }
            else if (fence.Advance(line))
            {
                wholeLine = true;
            }
            else if (trimmed.StartsWith("$$", StringComparison.Ordinal)
                     && !trimmed[2..].Contains("$$", StringComparison.Ordinal))
            {
                wholeLine = true;
                inMathBlock = true;
            }
            else if (MarkdownLines.IsDirective(trimmed))
            {
                wholeLine = true;
                sawDirective = true;
            }
            else if (IsReferenceDefinition(trimmed))
            {
                wholeLine = true;
            }

            // 整行被遮时连它的换行一起遮：这一行不参与，换行也不该算进正文字数
            if (wholeLine)
                masked.Set(pos, newline < 0 ? lineEnd : newline + 1);
            else
                MaskInline(text, masked, pos, lineEnd, ref inComment);

            if (newline < 0) break;
            pos = newline + 1;
            lineNo++;
            if (pos >= text.Length) break;
        }

        bool isBlog = (hasTitle && hasDate) || sawDirective;
        // 不是博文就一点不遮：行内代码、围栏代码块里的字对文档与回复是有用的证据
        return isBlog
            ? new BlogMarkdownMask(masked, text.Length, true)
            : new BlogMarkdownMask(null, 0, false);
    }

    /// <summary>
    /// 把遮住的字符换成空格，换行原样留着。给规则与统计用：空格匹配不上任何词，
    /// 整行被遮的段落又会被当成空行，于是这些区域既不报命中，也不进篇幅统计。
    /// </summary>
    public string Apply(string text)
    {
        // 不是博文时没有遮罩，长度对不上：原样交回，调用方不必先问 IsBlogPost
        if (_mask is not { } mask || _length != text.Length) return text;

        return string.Create(text.Length, (Text: text, Mask: mask), static (span, state) =>
        {
            state.Text.AsSpan().CopyTo(span);
            for (int i = 0; i < span.Length; i++)
                if (state.Mask.Get(i) && span[i] is not ('\n' or '\r'))
                    span[i] = ' ';
        });
    }

    /// <summary>这个位置是不是被遮住的区域。</summary>
    public bool IsMasked(int index) =>
        _mask is { } mask && index >= 0 && index < _length && mask.Get(index);

    /// <summary>
    /// 逐位存的遮罩：第 i 位对应第 i 个字符。标一段是按字对齐到整字一次填满，
    /// 标上的位数当场累加，不必构造完再扫一遍全量。
    /// </summary>
    private sealed class BitMask
    {
        private readonly ulong[] _words;
        private readonly int _length;

        public BitMask(int length)
        {
            _length = length;
            _words = new ulong[(length + 63) / 64];
        }

        /// <summary>标上的位数，就是被遮住的字符数。</summary>
        public int Count { get; private set; }

        public bool Get(int index) => (_words[index >> 6] & (1UL << (index & 63))) != 0;

        /// <summary>标上从 from（含）到 to（不含）这一段；越界的部分直接丢掉。</summary>
        public void Set(int from, int to)
        {
            int start = Math.Max(0, from);
            int stop = Math.Min(to, _length);
            if (start >= stop) return;

            int first = start >> 6;
            int last = (stop - 1) >> 6;
            ulong head = ulong.MaxValue << (start & 63);
            ulong tail = ulong.MaxValue >> (63 - ((stop - 1) & 63));

            if (first == last)
            {
                Fill(first, head & tail);
                return;
            }

            Fill(first, head);
            for (int word = first + 1; word < last; word++)
                Fill(word, ulong.MaxValue);
            Fill(last, tail);
        }

        private void Fill(int word, ulong bits)
        {
            Count += BitOperations.PopCount(bits & ~_words[word]);
            _words[word] |= bits;
        }
    }

    /// <summary>引用式链接定义：<c>[名字]: 地址</c>。整行没有正文，全遮。</summary>
    private static bool IsReferenceDefinition(string trimmed)
    {
        if (!trimmed.StartsWith('[')) return false;
        int close = trimmed.IndexOf("]:", StringComparison.Ordinal);
        return close > 1;
    }

    /// <summary>行内那几样：行内代码、公式、链接地址、自动链接、HTML 注释。</summary>
    private static void MaskInline(string text, BitMask masked, int start, int end, ref bool inComment)
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
                masked.Set(i, close + open);
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
                        masked.Set(i, end);
                        inComment = true;
                        return;
                    }
                    masked.Set(i, close + 3);
                    i = close + 3;
                    continue;
                }
                if (LooksLikeAutolink(text, i, end, out int tagEnd))
                {
                    masked.Set(i, tagEnd);
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
                    masked.Set(i + 1, close + 1);
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
    private static bool TryMaskFormula(string text, BitMask masked, int start, int end, out int after)
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

        masked.Set(start, close + marker);
        after = close + marker;
        return true;
    }
}
