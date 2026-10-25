using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using TextTool.Gui.Localization;
using TextTool.Gui.Views;

namespace TextTool.Tools.GuiSnapshot;

/// <summary>
/// 预览窗体检：截图只能证明画得对，证明不了「点保存到底往磁盘写了什么」。
///
/// 这里按真实操作跑三件事：
///   1. 两种主题各构造一次预览窗，渲染一帧，并对窗口过一遍文字裁切体检。
///   2. 在临时目录上真存一次，回读字节：首三字节必须是 UTF-8 BOM，正文一字不差。
///   3. 存到不可写路径：结果行变橙色、磁盘上不留残件、窗口不崩。
///
/// 三件事都是「界面上说的和磁盘上发生的对不对得上」，所以不看像素只看结论。
/// </summary>
internal static class PreviewCheck
{
    /// <summary>正文样例：空行、全角缩进、超长行各来一条，保存后必须原样回来。</summary>
    private const string SampleText =
        "第一行：预览内容和写盘结果必须一字不差。\n" +
        "\n" +
        "　　「这行开头有全角空格与引号，折行会把它们吞掉。」\n" +
        "这一行很长，用来确认预览里横向滚得动、保存时也不被截断或重新折行。\n" +
        "最后一行。";

    private static readonly (string Name, ThemeVariant Variant)[] Variants =
    {
        ("light", ThemeVariant.Light),
        ("dark", ThemeVariant.Dark),
    };

    public static int Run()
    {
        Report.Section("预览窗体检");

        if (Application.Current is not { } app)
        {
            Report.Fail("Application.Current 为空，没法渲染预览窗");
            return 1;
        }

        Loc.SetLanguage("zh_CN");

        string workDir = Path.Combine(
            Path.GetTempPath(), "texttool-preview-" + Guid.NewGuid().ToString("N")[..8]);

        int failures = 0;
        var rows = new List<string[]>();

        try
        {
            Directory.CreateDirectory(workDir);

            foreach ((string themeName, ThemeVariant variant) in Variants)
            {
                app.RequestedThemeVariant = variant;
                failures += Render(themeName, workDir, rows);
            }

            // 保存两项各开一次新窗口：第一项存成功后正文与结果行都变了，混在一起量不准。
            app.RequestedThemeVariant = ThemeVariant.Light;
            failures += SaveWithBom(workDir, rows);
            failures += SaveUnwritable(workDir, rows);
        }
        catch (Exception error)
        {
            Report.Fail($"未处理异常: {error.GetType().Name}: {error.Message}");
            failures++;
        }
        finally
        {
            TryDelete(workDir);
        }

        Report.PrintTable(
            new[] { "检查项", "主题", "结果", "详情" },
            rows,
            failures == 0 ? $"{rows.Count} 项全部通过" : $"失败 {failures} 项");

        return failures;
    }

    /// <summary>第 1 件事：渲染一帧，顺带查文字裁切。</summary>
    private static int Render(string themeName, string workDir, List<string[]> rows)
    {
        var window = new PreviewWindow(SampleText, Path.Combine(workDir, "preview-render.txt"));
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // 提示行和路径行都会折行，是这一页最容易在窄窗口下被裁的两处。
        ClippingCheck.Result clipping = ClippingCheck.Check($"预览窗-{themeName}", window);
        var frame = window.CaptureRenderedFrame();
        string variant = window.ActualThemeVariant.ToString() ?? "null";
        window.Close();

        if (frame is null)
        {
            Report.Fail($"预览窗-{themeName}: 渲染结果为空");
            rows.Add(new[] { "渲染一帧", themeName, "失败", "渲染结果为空" });
            return 1 + clipping.Count;
        }

        string size = $"{frame.PixelSize.Width}x{frame.PixelSize.Height}";
        rows.Add(new[]
        {
            "渲染一帧",
            variant,
            clipping.Count == 0 ? "通过" : "失败",
            $"{size}，文字裁切 {clipping.Summary}",
        });

        return clipping.Count;
    }

    /// <summary>第 2 件事：真存一次，回读字节验证 BOM 与正文。</summary>
    private static int SaveWithBom(string workDir, List<string[]> rows)
    {
        string output = Path.Combine(workDir, "saved.txt");

        var window = new PreviewWindow(SampleText, output);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        string variant = window.ActualThemeVariant.ToString() ?? "null";

        if (!ClickSave(window))
        {
            window.Close();
            Report.Fail("预览窗里找不到保存按钮");
            rows.Add(new[] { "保存与编码", variant, "失败", "找不到保存按钮" });
            return 1;
        }

        var details = new List<string>();
        int failures = 0;

        if (!File.Exists(output))
        {
            details.Add("保存后文件不存在");
            failures++;
        }
        else
        {
            byte[] bytes = File.ReadAllBytes(output);
            bool bom = HasUtf8Bom(bytes);
            details.Add(bom ? "首三字节 EF BB BF" : "首三字节不是 EF BB BF");
            if (!bom)
                failures++;

            // 去掉 BOM 再解一遍：正文要逐字对上，包括空行与全角空格。
            string decoded = bom
                ? new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3)
                : new UTF8Encoding(false).GetString(bytes);

            bool same = decoded == SampleText;
            details.Add(same ? "正文一致" : "正文与预览内容不一致");
            if (!same)
            {
                Report.Fail($"回读的正文与预览内容不一致（长度 {decoded.Length} / {SampleText.Length}）");
                failures++;
            }
        }

        bool green = UsesBrush(window, "SuccessFg");
        details.Add(green ? "结果行绿色" : "结果行不是成功色");
        if (!green)
            failures++;

        window.Close();

        if (failures > 0)
            Report.Fail($"保存与编码 {variant}: {string.Join("，", details)}");

        rows.Add(new[] { "保存与编码", variant, failures == 0 ? "通过" : "失败", string.Join("，", details) });
        return failures;
    }

    /// <summary>第 3 件事：写不进去的时候，结果行报橙色，窗口不崩，磁盘上不留残件。</summary>
    private static int SaveUnwritable(string workDir, List<string[]> rows)
    {
        // 父目录不存在，写入必然失败；这条路径也不会在磁盘上留下任何东西。
        string output = Path.Combine(
            workDir, "no-such-dir-" + Guid.NewGuid().ToString("N")[..6], "saved.txt");

        var window = new PreviewWindow(SampleText, output);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        string variant = window.ActualThemeVariant.ToString() ?? "null";

        if (!ClickSave(window))
        {
            window.Close();
            Report.Fail("预览窗里找不到保存按钮");
            rows.Add(new[] { "写不进时的结果行", variant, "失败", "找不到保存按钮" });
            return 1;
        }

        var label = window.FindControl<TextBlock>("ResultLabel");
        bool reported = label is { IsVisible: true } && !string.IsNullOrEmpty(label.Text);
        bool orange = UsesBrush(window, "WarningFg");
        bool nothingWritten = !File.Exists(output) && !File.Exists(output + ".tmp");
        // 窗口还挂在可视树上、关得掉，说明异常被拦在保存里，没把窗口拆掉。
        bool alive = window.IsVisible;

        string text = label?.Text ?? string.Empty;
        window.Close();

        var details = new List<string>
        {
            reported ? "结果行报出失败" : "结果行没有报出失败",
            orange ? "橙色" : "不是橙色",
            nothingWritten ? "磁盘无残件" : "磁盘出现残件",
            alive ? "窗口存活" : "窗口被拆掉",
        };

        int failures = 0;
        if (!reported || !orange || !nothingWritten || !alive)
        {
            failures = 1;
            Report.Fail($"写不进时的结果行: {string.Join("，", details)}（原文：{text}）");
        }

        rows.Add(new[]
        {
            "写不进时的结果行",
            variant,
            failures == 0 ? "通过" : "失败",
            string.Join("，", details),
        });

        return failures;
    }

    /// <summary>按下保存按钮并把消息泵转完：保存是同步的，转完就已经写完盘。</summary>
    private static bool ClickSave(Window window)
    {
        if (window.FindControl<Button>("BtnSavePreview") is not { } save)
            return false;

        save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        return true;
    }

    private static bool HasUtf8Bom(byte[] bytes) =>
        bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;

    /// <summary>结果行用的是不是指定语义色。必须带主题变体查，否则查不到语义色。</summary>
    private static bool UsesBrush(Window window, string key)
    {
        if (window.FindControl<TextBlock>("ResultLabel") is not { } label)
            return false;

        return window.TryFindResource(key, window.ActualThemeVariant, out object? raw)
            && raw is IBrush brush
            && Equals(label.Foreground, brush);
    }

    private static void TryDelete(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch (IOException)
        {
            // 临时目录删不掉不影响结论，系统迟早会清。
        }
    }
}
