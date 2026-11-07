using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace TextTool.Services;

/// <summary>
/// 一份报告的出口：逐份 <see cref="Add"/>，写完 <see cref="Finish"/>。
/// 报告不再攒成一整段字符串，输出多少就占多少内存；命令行逐份处理时靠它把内存压在一份报告的量级上。
/// </summary>
public interface ILintReportSink : IDisposable
{
    /// <summary>写一份报告。</summary>
    void Add(LintReport report);

    /// <summary>收尾：补上契约要求的闭合符号。一份都没写过时只产出一个空集合。</summary>
    void Finish();
}

/// <summary>
/// lint 结果的统一输出出口。CLI 的两条分支（人读 / JSON）都经由此处，
/// 行尾与分隔契约只维护一份，注入 <see cref="Console"/> 之外的纯函数，测试可直接断言。
/// </summary>
public static class LintOutput
{
    /// <summary>
    /// 把报告集合渲染成可直接写到 stdout 的完整文本。输出契约：
    /// <list type="bullet">
    /// <item>行尾一律为 LF（<c>\n</c>），不夹带 CR；</item>
    /// <item>人读分支在多份报告之间空一行，每份前面印一行文件名（只有一份时不印，单文件输出与从前一字不差），JSON 分支不插入额外分隔；</item>
    /// <item>整段文本以单个换行结尾（无报告时为空串）。</item>
    /// </list>
    /// 入参与返回值都不依赖 Console，是纯函数。
    ///
    /// 一次要出很多份、又不打算把整段留在内存里时改走 <see cref="BeginSink"/>，契约同一套。
    /// </summary>
    /// <param name="reportSet">要输出的报告集合。</param>
    /// <param name="asJson">true 走 JSON 契约，false 走人读契约。</param>
    /// <returns>可直接写入 stdout 的文本。</returns>
    public static string Render(LintReportSet reportSet, bool asJson, ILintText? text = null)
    {
        if (asJson)
            // ToJson 的正文已是 LF；末尾补一个 stdout 需要的换行，不再经 Console.WriteLine
            // 以免宿主把 \n 转成 CRLF
            return reportSet.ToJson() + "\n";

        // 排版（报告之间空一行、多份时每份前印文件名、行尾归一为 LF）只在 LintTextFormatter 里维护一份，
        // 命令行的 stdout 与界面的报告框因此不可能排出两种样子
        return LintTextFormatter.FormatSet(reportSet, text);
    }

    /// <summary>
    /// 按契约逐份写的出口：报告写一份丢一份，内存不随份数涨。
    /// 两个契约的排版都由各自的出口类维护，与 <see cref="Render"/> 逐字节相同。
    /// </summary>
    /// <param name="sink">写到哪里。命令行给标准输出，测试给 StringWriter。</param>
    /// <param name="asJson">true 走 JSON 契约，false 走人读契约。</param>
    /// <param name="version">JSON 契约版本；人读分支不用它。</param>
    public static ILintReportSink BeginSink(TextWriter sink, bool asJson, ILintText? text = null, int version = 4) =>
        asJson ? new LintJsonWriter(sink, version) : new LintHumanWriter(sink, text);
}

/// <summary>
/// --json 契约的写入器：报告直接写进输出流，不再整段序列化、再整段复制两遍。
///
/// 键序、缩进与转义与整段序列化逐字节相同，靠三件事保证：
/// 属性按声明顺序逐个写出（<see cref="LintReport"/>、<see cref="LintHit"/>、<see cref="LintNote"/>
/// 的属性次序就是 JSON 里的次序）；缩进与转义交给同一个 <see cref="Utf8JsonWriter"/> 与同一套选项；
/// 隐形字符与行尾在往外写的那一刻收口，见 <see cref="Drain"/>。
/// </summary>
public sealed class LintJsonWriter : ILintReportSink
{
    /// <summary>攒到这个字节数就往输出流写一次。切点都在一个完整的命中对象之后。</summary>
    private const int FlushThreshold = 64 * 1024;
    private static readonly JsonWriterOptions Options = new()
    {
        Indented = true,
        // 中文照原样输出，不转 \uXXXX。仍转义的只有这一组：< > & ' " 与 +（UTF-7 前缀），
        // 其余字符都放开。UnsafeRelaxedJsonEscaping 会把这一组也放开，那是另一个方向的过头。
        // 隐形字符不在这一步处理，见 Drain。
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    private readonly TextWriter _sink;
    private readonly Utf8JsonWriter _json;
    private readonly ArrayBufferWriter<byte> _buffer = new(FlushThreshold);
    /// <summary>解码用的一份复用缓冲：一片字节要么直接写出去，要么就地改写后写出，不落成字符串。</summary>
    private char[] _chars = new char[FlushThreshold];
    private readonly bool _trailingNewLine;
    private bool _finished;

    /// <param name="sink">写到哪里。</param>
    /// <param name="version">契约版本，写进第一个字段。</param>
    /// <param name="trailingNewLine">
    /// 结尾补不补一个换行。命令行输出以换行收尾（重定向成文件时也是完整的一行）；
    /// 整段 JSON（<see cref="LintReportSet.ToJson"/>）以 } 收尾，由调用方决定要不要补。
    /// </param>
    public LintJsonWriter(TextWriter sink, int version = 4, bool trailingNewLine = true)
    {
        _sink = sink;
        _trailingNewLine = trailingNewLine;
        _json = new Utf8JsonWriter(_buffer, Options);
        _json.WriteStartObject();
        _json.WriteNumber("Version", version);
        _json.WriteStartArray("Reports");
    }

    public void Add(LintReport report)
    {
        _json.WriteStartObject();
        _json.WriteString("File", report.File);
        _json.WriteNumber("Chars", report.Chars);
        _json.WriteString("ParagraphMode", report.ParagraphMode);

        _json.WriteStartArray("Hits");
        foreach (var hit in report.Hits)
        {
            WriteHit(hit);
            Drain(force: false);
        }
        _json.WriteEndArray();

        _json.WriteStartArray("Notes");
        foreach (var note in report.Notes)
        {
            _json.WriteStartObject();
            _json.WriteString("Id", note.Id);
            _json.WriteString("Text", note.Text);
            _json.WriteEndObject();
        }
        _json.WriteEndArray();

        // 没给基线时整个字段不出现：不带 --baseline 的调用输出与升版前一致
        if (report.BaselineHits is int baselineHits)
            _json.WriteNumber("BaselineHits", baselineHits);

        _json.WriteEndObject();
        Drain(force: true);
    }

    public void Finish()
    {
        if (_finished) return;
        _finished = true;
        // 一份都没写过时这里是 []，与序列化一个空集合的结果一致
        _json.WriteEndArray();
        _json.WriteEndObject();
        Drain(force: true);
        if (_trailingNewLine)
            _sink.Write('\n');
    }

    public void Dispose()
    {
        // 没调 Finish 就丢下的那一次，输出本来就不完整，这里不补闭合符号
        _json.Dispose();
    }

    private void WriteHit(LintHit hit)
    {
        _json.WriteStartObject();
        _json.WriteString("Id", hit.Id);
        _json.WriteString("Group", hit.Group);
        _json.WriteString("Title", hit.Title);
        _json.WriteString("Severity", hit.Severity);
        _json.WriteNumber("Line", hit.Line);
        _json.WriteNumber("Col", hit.Col);
        _json.WriteNumber("Length", hit.Length);
        _json.WriteString("Match", hit.Match);
        _json.WriteString("Snippet", hit.Snippet);
        _json.WriteString("Detail", hit.Detail);
        _json.WriteString("Hint", hit.Hint);                     // null 写成 null，与整段序列化一致
        _json.WriteString("SuggestScheme", hit.SuggestScheme);
        _json.WriteEndObject();
    }

    /// <summary>
    /// 把缓冲里的字节写出去，顺手收口两处：隐形字符转义为 \uXXXX 字面量（任何解析方都解回原字符，
    /// 语义不变）、行尾归一为 LF（输出常被重定向成文件，本工作区文件一律 LF）。
    ///
    /// 未提交的字节也要算进门槛：写入器把字节攒在自己的缓冲里，等 Flush 才交给下游，
    /// 只看下游缓冲的已写字节数会让整份输出一次攒到几十万处命中之后（五十兆的稿子光这一份就是几百兆）。
    ///
    /// 逐段解码到复用的一份缓冲里直接写，不把每一片先变成字符串：一片 64 KB 的字节解码成字符串
    /// 是 128 KB，正好落进大对象堆，几十万处命中下来，光这一层就够把内存峰值抬起好几百兆。
    ///
    /// 切点都在一个完整对象之后，多字节字符与转义序列都不会跨两段，所以逐段收口与整段收口的结果相同。
    /// </summary>
    private void Drain(bool force)
    {
        if (!force && _json.BytesCommitted + _json.BytesPending < FlushThreshold) return;
        _json.Flush();
        int bytes = _buffer.WrittenCount;
        if (bytes == 0) return;
        // 合法 UTF-8 里一个字符至少一个字节，按字节数备缓冲一定装得下
        if (_chars.Length < bytes) _chars = new char[bytes];
        int chars = Encoding.UTF8.GetChars(_buffer.WrittenSpan, _chars);
        var text = _chars.AsSpan(0, chars);
        // 大多数段里既没有隐形字符也没有 CR：扫一遍就直接把原样的字交出去
        if (text.IndexOfAny(LintTextFormatter.NeedsRewrite) < 0)
            _sink.Write(text);
        else
            _sink.Write(LintTextFormatter.RewriteChunk(text));
        _buffer.Clear();
    }
}
