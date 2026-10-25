namespace TextTool.Tools.GuiSnapshot;

/// <summary>
/// 控制台输出的小工具：分区标题、表格、失败行。
///
/// 表格要按显示宽度对齐，而不是按字符个数：中文是全角占两格，用 string.Length 补空格
/// 会让含中文的列整个歪掉。这里只做够用的近似（CJK 与全角标点算两格）。
///
/// 输出只用 ASCII 符号加中文，不用破折号、中点、乘号、弯引号，终端不会乱码。
/// </summary>
internal static class Report
{
    public static void Section(string title) => Console.WriteLine($"\n== {title} ==");

    public static void Info(string message) => Console.WriteLine($"[信息] {message}");

    /// <summary>
    /// 失败行走 stderr，好让脚本能单独抓；但先 flush stdout，否则管道里表格会被
    /// stderr 插到中间，读起来像表格坏了。
    /// </summary>
    public static void Fail(string message)
    {
        Console.Out.Flush();
        Console.Error.WriteLine($"[失败] {message}");
    }

    public static void Pass(string message) => Console.WriteLine($"[通过] {message}");

    /// <summary>打印表格。summary 不为空时，多打一行跨列汇总，固定在第一列。</summary>
    public static void PrintTable(string[] headers, IReadOnlyList<string[]> rows, string? summary = null)
    {
        // 统一过一道 Clean：表格里会出现界面上的原文，原文可能带破折号这类字符，
        // 终端输出只留 ASCII 和中文。判定用的是原始值，这里只影响显示。
        headers = headers.Select(Clean).ToArray();
        rows = rows.Select(row => row.Select(Clean).ToArray()).ToArray();
        summary = summary is null ? null : Clean(summary);

        var widths = new int[headers.Length];
        for (int i = 0; i < headers.Length; i++)
            widths[i] = Width(headers[i]);

        foreach (string[] row in rows)
            for (int i = 0; i < headers.Length && i < row.Length; i++)
                widths[i] = Math.Max(widths[i], Width(row[i]));

        PrintRow(headers, widths);
        PrintSeparator(widths);

        foreach (string[] row in rows)
            PrintRow(row, widths);

        if (summary is not null)
        {
            PrintSeparator(widths);
            Console.WriteLine($"{Pad("汇总", widths[0])} | {summary}");
        }
    }

    private static void PrintRow(string[] cells, int[] widths)
    {
        var parts = new string[widths.Length];
        for (int i = 0; i < widths.Length; i++)
            parts[i] = Pad(i < cells.Length ? cells[i] : string.Empty, widths[i]);

        Console.WriteLine(string.Join(" | ", parts).TrimEnd());
    }

    private static void PrintSeparator(int[] widths) =>
        Console.WriteLine(string.Join("-+-", widths.Select(w => new string('-', w))));

    private static string Pad(string text, int width) =>
        text + new string(' ', Math.Max(0, width - Width(text)));

    /// <summary>把非 ASCII 的标点换成人能读的 ASCII 等价物，只为满足终端输出约束。</summary>
    private static string Clean(string text)
    {
        var builder = new System.Text.StringBuilder(text.Length);
        foreach (char c in text)
        {
            builder.Append(c switch
            {
                '\u2014' or '\u2013' or '\u2212' => '-',
                '\u00b7' or '\u2022' => '.',
                '\u00d7' => 'x',
                '\u2018' or '\u2019' => '\'',
                '\u201c' or '\u201d' => '"',
                '\u2026' => '.',
                _ => c,
            });
        }

        return builder.ToString();
    }

    private static int Width(string text)
    {
        int width = 0;
        foreach (char c in text)
            width += IsWide(c) ? 2 : 1;

        return width;
    }

    /// <summary>CJK 汉字、假名、韩文、全角标点：这些在等宽终端里占两格。</summary>
    private static bool IsWide(char c) =>
        c == 0x2329 || c == 0x232A ||
        (c >= 0x1100 && c <= 0x115F) ||
        (c >= 0x2E80 && c <= 0xA4CF) ||
        (c >= 0xAC00 && c <= 0xD7A3) ||
        (c >= 0xF900 && c <= 0xFAFF) ||
        (c >= 0xFE30 && c <= 0xFE6F) ||
        (c >= 0xFF00 && c <= 0xFF60) ||
        (c >= 0xFFE0 && c <= 0xFFE6);
}
