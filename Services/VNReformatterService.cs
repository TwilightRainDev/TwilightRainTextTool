using System.Text;
using System.Text.RegularExpressions;

namespace TextTool.Services;

/// <summary>
/// 视觉小说文本排版引擎 — 移植自 vn_tools.reformat.VNReformatter (Python)。
/// 将视觉小说脚本从固定宽度硬换行 → 自然段落格式，
/// 自动识别对话、叙事、场景标记和路线标题。
/// </summary>
public sealed class VNReformatterService
{
    // ================================================================
    //  兜底默认值（通用，非特定作品）
    // ================================================================

    // 空角色集：未注入角色时不做角色名识别（对话识别仍依赖「角色」「台词」模式）
    private static readonly HashSet<string> EmptyCharacters = new();

    private static readonly List<string> EmptyRouteNames = new();

    private static readonly Regex DefaultScenePattern = new(
        @"(?<=── )\S+(?= ──)",
        RegexOptions.Compiled);

    // ================================================================
    //  实例字段
    // ================================================================

    private readonly HashSet<string> _characters;
    private readonly List<string> _routeNames;
    private readonly Regex _scenePattern;
    private readonly Regex _charPattern;
    private readonly int _maxParaLen;
    private readonly int _minParaLen;

    /// <summary>句末标点集合（用于补全判断）</summary>
    private static readonly HashSet<char> SentenceEnders = new()
    {
        '。', '！', '？', '；', '：', '」', '）', '】', '』', '》',
        '、', '…', '—', '～', '·', '.', '!', '?', ';', ':',
    };

    private static readonly Regex DialogueLineRegex = new(
        @"「[^」]*」「", RegexOptions.Compiled);

    private static readonly Regex RepeatedEnder = new(
        @"([。！？])\1{2,}", RegexOptions.Compiled);

    // ================================================================
    //  构造
    // ================================================================

    public VNReformatterService(
        IEnumerable<string>? characters = null,
        IEnumerable<string>? routeNames = null,
        int maxParaLength = 450,
        int minParaLength = 80,
        Regex? scenePattern = null)
    {
        _characters = new HashSet<string>(characters ?? EmptyCharacters);
        _routeNames = new List<string>(routeNames ?? EmptyRouteNames);
        _maxParaLen = maxParaLength;
        _minParaLen = minParaLength;
        _scenePattern = scenePattern ?? DefaultScenePattern;

        // 角色名正则：最长优先匹配；无角色时构建永不匹配的模式，避免空匹配误判
        if (_characters.Count > 0)
        {
            var sorted = _characters.OrderByDescending(c => c.Length).Select(Regex.Escape);
            _charPattern = new Regex("(?:" + string.Join("|", sorted) + ")", RegexOptions.Compiled);
        }
        else
        {
            _charPattern = new Regex(@"\b(?!\b)", RegexOptions.Compiled); // 永不匹配
        }
    }

    // ================================================================
    //  标签类型（线条分类用）
    // ================================================================

    private readonly record struct LineTag(string Type, string Text);

    private readonly record struct ParagraphPart(string Type, string Text);

    // ================================================================
    //  公开 API
    // ================================================================

    /// <summary>处理输入文本，返回排版后的结果。</summary>
    public string Reformat(string text)
    {
        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        if (lines.Length == 0) return "";

        // 移除 BOM
        if (lines[0].Length > 0 && lines[0][0] == '﻿')
            lines[0] = lines[0][1..];

        var tags = ClassifyLines(lines);
        var parts = BuildParagraphs(lines, tags);
        return FormatOutput(parts);
    }

    /// <summary>处理文件，直接写出结果。</summary>
    public void ReformatFile(string inputPath, string outputPath, Encoding encoding)
    {
        string text = File.ReadAllText(inputPath, encoding);
        string result = Reformat(text);
        File.WriteAllText(outputPath, result, new UTF8Encoding(true));
    }

    // ================================================================
    //  线条分类
    // ================================================================

    private LineTag[] ClassifyLines(string[] lines)
    {
        var tags = new LineTag[lines.Length];
        for (int i = 0; i < lines.Length; i++)
        {
            string s = lines[i].Trim();
            if (s.Length == 0)
            {
                tags[i] = new LineTag("empty", "");
            }
            else if (IsSeparator(s))
            {
                // ★★★★ / ==== 装饰分隔线，跳过并 flush 缓冲区
                tags[i] = new LineTag("empty", "");
            }
            else if (IsRouteHeading(s))
            {
                tags[i] = new LineTag("heading", s);
            }
            else if (IsSceneMarker(s))
            {
                tags[i] = new LineTag("scene", s);
            }
            else if (IsDialogueLine(s))
            {
                tags[i] = new LineTag("dialogue", s);
            }
            else if (s.StartsWith('「') && s[1..].Contains('「'))
            {
                // 「克莉斯蒂娜？」「你才是...」— 对话变体
                tags[i] = new LineTag("dialogue", s);
            }
            else if (s.StartsWith('「') && !s[1..].Contains('「'))
            {
                // 「纯引文」— 内心独白或引用，视为对话
                tags[i] = new LineTag("dialogue", s);
            }
            else if (HasDialogue(s))
            {
                tags[i] = new LineTag("mixed", s);
            }
            else
            {
                tags[i] = new LineTag("narrative", s);
            }
        }
        return tags;
    }

    private bool IsDialogueLine(string s) =>
        s.StartsWith('「') && DialogueLineRegex.IsMatch(s);

    private bool IsSceneMarker(string s) =>
        _scenePattern.IsMatch(s);

    private bool IsRouteHeading(string s)
    {
        if (s.StartsWith('「') || IsSceneMarker(s)) return false;
        return _routeNames.Any(rn => s.Contains(rn)) && s.Length <= 30;
    }

    /// <summary>全★或全=的装饰分隔线，跳过不产生输出。</summary>
    private static bool IsSeparator(string s) =>
        s.Length >= 4 && s.All(c => c is '★' or '＝' or '=');

    private bool HasDialogue(string s)
    {
        if (DialogueLineRegex.IsMatch(s)) return true;
        if (!s.Contains('「')) return false;

        foreach (Match m in _charPattern.Matches(s))
        {
            int idx = m.Index + m.Length;
            // 角色名「台词」
            if (idx < s.Length && s[idx] == '「')
                return true;
            // 角色名，「台词」（樱之诗等 VN 格式）
            if (idx + 1 < s.Length && s[idx] == '，' && s[idx + 1] == '「')
                return true;
        }
        return false;
    }

    /// <summary>从混合行中分离叙事 and 对话，返回 (narr, dial)。</summary>
    private (string Narr, string Dial) ExtractDialogue(string s)
    {
        // 「角色」「台词」模式
        var m = DialogueLineRegex.Match(s);
        if (m.Success)
            return (s[..m.Index], s[m.Index..]);

        // 角色名「台词」或 角色名，「台词」模式
        var cm = _charPattern.Match(s);
        if (cm.Success)
        {
            int idx = cm.Index + cm.Length;
            if (idx < s.Length && s[idx] == '「')
                return (s[..cm.Index], s[cm.Index..]);
            if (idx + 1 < s.Length && s[idx] == '，' && s[idx + 1] == '「')
                return (s[..cm.Index], s[cm.Index..]);
        }

        return (s, "");
    }

    // ================================================================
    //  段落构建
    // ================================================================

    private List<ParagraphPart> BuildParagraphs(string[] lines, LineTag[] tags)
    {
        var parts = new List<ParagraphPart>();
        var buf = new List<string>();

        void Flush()
        {
            if (buf.Count == 0) return;
            string para = string.Concat(buf).Trim();
            if (para.Length > 0)
            {
                para = FixPunctuation(para);
                if (!SentenceEnders.Contains(para[^1]))
                    para += "。";
                foreach (var sub in SplitLongParagraph(para))
                    parts.Add(new ParagraphPart("narrative", sub));
            }
            buf.Clear();
        }

        for (int i = 0; i < tags.Length; i++)
        {
            var (type, text) = tags[i];
            switch (type)
            {
                case "empty":
                    if (buf.Count > 0) Flush();
                    break;

                case "heading":
                    Flush();
                    string clean = text.Trim('─').Trim();
                    foreach (var rn in _routeNames)
                        if (clean.Contains(rn)) { clean = rn; break; }
                    parts.Add(new ParagraphPart("heading", clean));
                    break;

                case "scene":
                    parts.AddRange(ProcessSceneLine(text, buf));
                    break;

                case "dialogue":
                    Flush();
                    parts.Add(new ParagraphPart("dialogue", FixPunctuation(text)));
                    break;

                case "mixed":
                    var (narr, dial) = ExtractDialogue(text);
                    if (narr.Trim().Length > 0) buf.Add(narr);
                    if (dial.Trim().Length > 0)
                    {
                        Flush();
                        parts.Add(new ParagraphPart("dialogue", FixPunctuation(dial)));
                    }
                    break;

                case "narrative":
                    buf.Add(text);
                    break;
            }
        }

        Flush();
        return parts;
    }

    /// <summary>处理场景标记行：提取前导叙事、路线名、SGFD 代码。</summary>
    private List<ParagraphPart> ProcessSceneLine(string text, List<string> buf)
    {
        var result = new List<ParagraphPart>();
        string textBare = text;
        string routeName = "";

        foreach (var rn in _routeNames)
        {
            if (textBare.Contains(rn))
            {
                routeName = rn;
                textBare = textBare.Replace(rn, "");
                break;
            }
        }

        var m = _scenePattern.Match(textBare);
        string tagText = m.Success ? m.Value : textBare.Trim('─').Trim();

        if (m.Success)
        {
            string pre = textBare[..m.Index].Trim().Trim("─「」".ToCharArray()).Trim();
            if (pre.Length > 0) buf.Add(pre);
        }

        // 先 flush 缓冲区
        if (buf.Count > 0)
        {
            string para = string.Concat(buf).Trim();
            // 此处不用像 Flush() 那样补标点，因为只是暂存叙事
            if (para.Length > 0)
            {
                para = FixPunctuation(para);
                if (!SentenceEnders.Contains(para[^1]))
                    para += "。";
                foreach (var sub in SplitLongParagraph(para))
                    result.Add(new ParagraphPart("narrative", sub));
            }
            buf.Clear();
        }

        if (routeName.Length > 0)
            result.Add(new ParagraphPart("heading", routeName));
        result.Add(new ParagraphPart("scene", tagText));

        return result;
    }

    // ================================================================
    //  输出排版
    // ================================================================

    private static string FormatOutput(List<ParagraphPart> parts)
    {
        var outLines = new List<string>();
        string? prevType = null;

        for (int i = 0; i < parts.Count; i++)
        {
            var (type, text) = parts[i];
            switch (type)
            {
                case "heading":
                    if (i > 0) outLines.Add("");
                    outLines.Add($"# {text}");
                    break;
                case "scene":
                    if (prevType is "heading" or "narrative" or "dialogue")
                        outLines.Add("");
                    outLines.Add($"─── {text} ───");
                    break;
                case "dialogue":
                    if (prevType is "narrative" or "heading" or "scene")
                        outLines.Add("");
                    outLines.Add(text);
                    break;
                case "narrative":
                    if (prevType is not null)
                        outLines.Add("");
                    outLines.Add(text);
                    break;
            }
            prevType = type;
        }

        string result = string.Join("\n", outLines);
        result = Regex.Replace(result, @"\n{3,}", "\n\n");
        return result;
    }

    // ================================================================
    //  工具方法
    // ================================================================

    /// <summary>修复常见标点重复问题。</summary>
    private static string FixPunctuation(string s)
    {
        s = s.Replace("。。", "。");
        s = s.Replace("，，", "，");
        s = s.Replace("？？", "？");
        s = s.Replace("！！", "！");
        s = s.Replace("，。", "。");
        s = s.Replace("。？", "？");
        s = s.Replace("，？", "？");
        s = s.Replace("。，", "，");
        s = RepeatedEnder.Replace(s, "$1$1");
        s = Regex.Replace(s, @"\.{3,}", "…");
        s = Regex.Replace(s, @"…{2,}", "……");
        return s;
    }

    /// <summary>超长叙事段落按句号拆分，避免文字墙。</summary>
    private IEnumerable<string> SplitLongParagraph(string text)
    {
        if (text.Length <= _maxParaLen)
        {
            yield return text;
            yield break;
        }

        while (text.Length > _maxParaLen)
        {
            int cut = FindCut(text, _maxParaLen);
            if (cut == -1) cut = _maxParaLen;
            string seg = text[..cut].Trim();
            if (seg.Length > 0) yield return seg;
            text = text[cut..].Trim();
        }
        if (text.Length > 0) yield return text;
    }

    /// <summary>
    /// 在 limit 范围内查找最后一个句末标点位置作为切割点。
    /// 注意！号、」嵌套深度，确保不在引号内切割。
    /// </summary>
    private int FindCut(string text, int limit)
    {
        int depth = 0;
        int start = Math.Max(_minParaLen - 1, 0);
        int end = Math.Min(limit, text.Length) - 1;

        for (int i = end; i >= start; i--)
        {
            if (text[i] == '」') depth++;
            else if (text[i] == '「') depth--;
            if (depth == 0 && i > 0 && "。！？".Contains(text[i - 1]))
                return i;
        }
        return -1;
    }
}
