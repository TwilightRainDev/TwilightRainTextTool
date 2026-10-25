using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using TextTool.Gui.Localization;
using TextTool.Gui.Views;
using TextTool.Services;

namespace TextTool.Tools.GuiSnapshot;

/// <summary>
/// 交互体检：不看像素，看按钮按下去以后磁盘上到底变了什么。
///
/// 截图只能证明画得对，证明不了「点保存真的保存了」「点删除真的删掉了」。
/// 这一项驱动真实的标点替换页：按按钮、读回 json、跑一次替换、比对输出文件内容。
/// 它跑在进程自己的目录里，跑前备份、跑完还原，不污染构建产物。
/// </summary>
internal static class ReplaceTabDriveCheck
{
    private const string RulesFile = "replace_rules.json";
    private const string SchemesFile = "replace_schemes.json";

    public static int Run()
    {
        Report.Section("标点替换页交互体检");

        if (Application.Current is null)
        {
            Report.Fail("Application.Current 为空");
            return 1;
        }

        Loc.SetLanguage("zh_CN");

        string? rulesBackup = Backup(RulesFile, out bool rulesExisted);
        string? schemesBackup = Backup(SchemesFile, out bool schemesExisted);

        int failures = 0;
        string workDir = Path.Combine(Path.GetTempPath(), "texttool-drive-" + Guid.NewGuid().ToString("N")[..8]);

        try
        {
            Directory.CreateDirectory(workDir);

            var window = new MainWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();

            if (window.FindControl<TabControl>("Tabs") is { } tabs)
            {
                tabs.SelectedIndex = 2;
                Dispatcher.UIThread.RunJobs();
            }

            if (window.FindControl<ReplaceTab>("ReplaceTabView") is not { } tab)
            {
                Report.Fail("找不到标点替换页");
                return 1;
            }

            // 从空规则开始，免得本机遗留的配置影响判断。
            File.Delete(Path.Combine(AppContext.BaseDirectory, RulesFile));
            tab.SetRules(new List<ReplaceRule>());
            failures += Drive(tab, workDir);
        }
        catch (Exception error)
        {
            Report.Fail($"未处理异常: {error.GetType().Name}: {error.Message}");
            failures++;
        }
        finally
        {
            Restore(RulesFile, rulesBackup, rulesExisted);
            Restore(SchemesFile, schemesBackup, schemesExisted);
            TryDelete(workDir);
        }

        return failures;
    }

    private static int Drive(ReplaceTab tab, string workDir)
    {
        int failures = 0;
        int step = 0;

        void Expect(bool ok, string what)
        {
            step++;
            if (ok)
                Report.Info($"第 {step} 步通过：{what}");
            else
            {
                Report.Fail($"第 {step} 步失败：{what}");
                failures++;
            }
        }

        var add = Find<Button>(tab, "AddUpdateButton");
        var delete = Find<Button>(tab, "DeleteButton");
        var moveUp = Find<Button>(tab, "MoveUpButton");
        var find = Find<TextBox>(tab, "FindBox");
        var replace = Find<TextBox>(tab, "ReplaceBox");
        var list = Find<ListBox>(tab, "RuleList");
        var run = Find<Button>(tab, "ReplaceButton");
        var warning = Find<TextBlock>(tab, "InlineWarning");

        // 1. 空列表时删除与上移下移都是禁用的，不给用户点了没反应的空动作。
        Expect(!delete.IsEnabled && !moveUp.IsEnabled, "空列表时删除与上下移禁用");

        // 2. 添加两条规则，磁盘上应立刻出现两条。
        AddRule(add, find, replace, "，", ",");
        AddRule(add, find, replace, "。", ".");
        var saved = ReadRules();
        Expect(list.ItemCount == 2 && saved.Count == 2, "添加两条规则后列表 2 条且落盘 2 条");
        Expect(saved[0].Find == "，" && saved[1].Find == "。", "落盘顺序与添加顺序一致");

        // 3. 上移：选中第二条，上移后顺序互换。
        list.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        Click(moveUp);
        saved = ReadRules();
        Expect(saved[0].Find == "。" && saved[1].Find == "，", "上移后落盘顺序互换");
        Expect(list.SelectedIndex == 0, "上移后选中跟着走到第一行");

        // 4. 删除后编辑框清空，此时点添加什么都不该发生（旧版在这里会多出一条重复规则）。
        Click(delete);
        Dispatcher.UIThread.RunJobs();
        Expect(list.ItemCount == 1, "删除后列表剩 1 条");
        Expect(string.IsNullOrEmpty(find.Text), "删除后查找框已清空");

        Click(add);
        Dispatcher.UIThread.RunJobs();
        Expect(list.ItemCount == 1, "查找框为空时点添加不会新增规则");
        Expect(warning.IsVisible, "查找框为空时给出行内提示");

        // 5. 真实跑一次替换：读、替换、写成 _Processed 文件。
        // 上一步删掉了句号那一条，这里补回来，两条规则一起验。
        AddRule(add, find, replace, "。", ".");
        string input = Path.Combine(workDir, "sample.txt");
        File.WriteAllText(input, "你好，世界。", new System.Text.UTF8Encoding(false));

        tab.UseFilesForCheck(new[] { input });
        Expect(run.IsEnabled, "选中文件后执行按钮可用");

        Click(run);
        WaitForResult(tab);

        string output = PathHelper.GetProcessedPath(input);
        string content = File.Exists(output) ? File.ReadAllText(output) : "";
        Expect(File.Exists(output), "生成了 _Processed 输出文件");
        Expect(content == "你好,世界.", "替换结果正确（，→ ,   。→ .）");

        // 6. 规则为空时执行要挡住，并给出行内提示。
        tab.SetRules(new List<ReplaceRule>());
        Click(run);
        Dispatcher.UIThread.RunJobs();
        var banner = Find<Border>(tab, "ConfigErrorBar");
        Expect(banner.IsVisible, "没有规则时执行被挡住并给出提示");

        return failures;
    }

    private static void AddRule(Button add, TextBox find, TextBox replace, string findText, string replaceText)
    {
        find.Text = findText;
        replace.Text = replaceText;
        Click(add);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Click(Button button)
    {
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>批处理在后台线程跑，结果回来要把消息泵转起来，最多等五秒。</summary>
    private static void WaitForResult(ReplaceTab tab)
    {
        var result = Find<TextBlock>(tab, "ResultLabel");
        for (int i = 0; i < 100 && string.IsNullOrEmpty(result.Text); i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(50);
        }
    }

    private static T Find<T>(Control root, string name) where T : Control =>
        root.FindControl<T>(name) ?? throw new InvalidOperationException($"找不到控件 {name}");

    private static List<ReplaceRule> ReadRules() =>
        JsonFileStore.Load<ReplaceRule>(RulesFile);

    private static string? Backup(string fileName, out bool existed)
    {
        string path = Path.Combine(AppContext.BaseDirectory, fileName);
        existed = File.Exists(path);
        return existed ? File.ReadAllText(path) : null;
    }

    private static void Restore(string fileName, string? content, bool existed)
    {
        string path = Path.Combine(AppContext.BaseDirectory, fileName);
        if (existed && content is not null)
            File.WriteAllText(path, content);
        else
            TryDeleteFile(path);
    }

    private static void TryDeleteFile(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
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
