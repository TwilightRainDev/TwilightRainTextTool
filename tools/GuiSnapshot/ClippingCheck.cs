using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace TextTool.Tools.GuiSnapshot;

/// <summary>
/// 文字裁切体检：遍历整棵可视树，找出"文字想要的空间比它实际分到的框大"的控件。
///
/// 为什么在布局层面查而不是看图：文字被切掉一部分只差几个像素，截图上要人盯着比对才能
/// 发现，页签一多就没人看了；而两个尺寸一比就是像素级确定的结论，三种语言、两种主题、
/// 每个页签都能自动跑一遍。
///
/// 判据：控件的想要尺寸比 Bounds.Size 大出半像素以上。半像素容差是留给布局取整的，
/// 不然每一轮都会报一堆 0.3 像素的假阳性。
///
/// 关于"想要尺寸"怎么取：不能直接读排版那一刻的 DesiredSize。布局传给控件的约束是
/// "最多这么宽"，Avalonia 会把 DesiredSize 压回约束上，于是"20 宽的格子里放 336 宽的
/// 文字"这种最典型的情形，DesiredSize 报的就是 20，和 Bounds 一般大，永远看不出被裁
/// （负向验证里实测到的就是这个值）。所以这里用无界约束把控件重测一次，取那个
/// DesiredSize 作为"它真正需要多大"。重测会改掉当轮布局，Check 返回前整棵树会重排回去。
/// </summary>
internal static class ClippingCheck
{
    /// <summary>布局取整带来的小误差，超过这个才算真裁。</summary>
    private const double Tolerance = 0.5;

    /// <summary>表格里文本列只留前 20 个字，够认出是哪个控件，又不至于把表格撑爆。</summary>
    private const int TextPreviewLength = 20;

    /// <summary>一个窗口的体检结果：裁切处数，以及汇总表里那一列要写的字。</summary>
    public readonly record struct Result(int Count, string Summary);

    /// <param name="label">窗口的标识，用在失败表和失败行里，一般是 语言-主题。</param>
    public static Result Check(string label, Control root)
    {
        var findings = new List<Finding>();

        // root 本身是 Window，不在观察名单里，所以只看后代。
        var watched = root.GetVisualDescendants()
            .OfType<Control>()
            .Where(IsWatched)
            .ToList();

        foreach (Control control in watched)
            Inspect(control, findings);

        // 上面为了拿到"想要多大"用无界约束重测过，这里把树还原成真的布局，
        // 免得后续代码（或者下一个检查项）量到的是重测后的尺寸。
        if (watched.Count > 0)
        {
            root.InvalidateMeasure();
            Dispatcher.UIThread.RunJobs();
        }

        if (findings.Count > 0)
            Dump(label, findings);

        // 一个观察对象都没扫到，说明窗口是空的、或者树遍历坏了。这时候的"通过"是假绿，
        // 必须当成失败报出来，否则页签还没写完的阶段这个检查会一路绿灯。
        if (watched.Count == 0)
        {
            Report.Fail($"{label}: 一个可检查的控件都没扫到，窗口可能是空的");
            return new Result(1, "扫到 0 个");
        }

        return new Result(
            findings.Count,
            findings.Count == 0 ? "通过" : $"{findings.Count} 处");
    }

    /// <summary>
    /// 观察名单：会画文字、且尺寸由文字撑起来的控件。容器类（Panel、Border）不查，
    /// 它们的 DesiredSize 是子控件撑的，查了只会和子控件重复报同一处问题。
    /// CheckBox 和 RadioButton 都是 Button 的子类，被 Button 这条覆盖。
    /// </summary>
    private static bool IsWatched(Control control) => control switch
    {
        TextBlock => true,
        TextPresenter => true,
        Button => true,
        ComboBox => true,
        NumericUpDown => true,
        Label => true,
        _ => false,
    };

    private static void Inspect(Control control, List<Finding> findings)
    {
        // 自家不可见：不参与排版也不渲染，没东西可裁。只看控件自己的 Visibility，
        // 祖先隐藏时子树里的控件自己仍是 Visible，但那种情况用户根本看不到。
        if (!control.IsVisible)
            return;

        // 宽或高为 0：还没真正排上版面（或本来就压成一条线），DesiredSize 和 Bounds
        // 比出来的差值不代表文字被裁，比了只会是假阳性。
        if (control.Bounds.Width <= 0 || control.Bounds.Height <= 0)
            return;

        // 祖先里有 ScrollViewer：内容超出可以滚出来，用户照样能看到全部文字。
        // 滚动区域里的文字"比框大"是设计意图，不是缺陷。
        if (control.GetVisualAncestors().Any(ancestor => ancestor is ScrollViewer))
            return;

        // 想要多大：不能直接读排版时的 DesiredSize。布局给的约束是"最多这么宽"，Avalonia
        // 会把 DesiredSize 压到约束上，于是"20 宽的格子里放 336 宽的文字"这种情形，
        // DesiredSize 报的就是 20，和 Bounds 一般大，永远看不出被裁。
        // 所以用无界约束重测一次，拿到的才是控件真正需要的尺寸。
        // 重测会改掉这一轮布局的状态，拍照已经完成，Check 结束前会整棵树重排回去。
        // 数值框要拨到极值再量：默认值往往最短（20），装得下；拨到 1000 才看得出
        // 固定宽度够不够。量完立刻还原，不影响同一次里的其它控件。
        decimal? restoreValue = null;
        if (control is NumericUpDown numeric && numeric.Maximum is { } maximum && numeric.Value != maximum)
        {
            restoreValue = numeric.Value;
            numeric.Value = maximum;
            Dispatcher.UIThread.RunJobs();
        }

        control.Measure(Size.Infinity);

        // DesiredSize 含外边距，Bounds 不含。不减掉的话，凡是设了 Margin 的控件
        // 都会"超出"一个 Margin 大小，报成假裁切。行合并页之所以没被报，是因为它
        // 套在 ScrollViewer 里，上面那条早退了。
        Thickness margin = control.Margin;
        Size wanted = new(
            control.DesiredSize.Width - margin.Left - margin.Right,
            control.DesiredSize.Height - margin.Top - margin.Bottom);
        Size actual = control.Bounds.Size;

        if (restoreValue is { } value)
        {
            ((NumericUpDown)control).Value = value;
            Dispatcher.UIThread.RunJobs();
        }

        bool widthClipped = wanted.Width > actual.Width + Tolerance;
        bool heightClipped = wanted.Height > actual.Height + Tolerance;

        // 设了 TextTrimming 又窄：多出来的字本来就要变省略号，是设计意图，不算裁。
        // 只免掉宽度方向，高度方向省不省号都救不了，仍然要报。
        if (TrimmingOf(control) is { } trimming && trimming != TextTrimming.None)
            widthClipped = false;

        // 换行的控件：无界重测得的是单行宽度，比出来的宽度必然偏大，宽度方向没有意义。
        // 该问的是「按它实际拿到的宽度折行，需要多高」，所以按实际宽度再量一次，
        // 只保留高度这一项判断。父级把高度写死时，折出来的行数就会超出。
        if (Wraps(control))
        {
            control.Measure(new Size(actual.Width, double.PositiveInfinity));
            double wrappedHeight = control.DesiredSize.Height - margin.Top - margin.Bottom;
            widthClipped = false;
            heightClipped = wrappedHeight > actual.Height + Tolerance;
            wanted = new Size(actual.Width, wrappedHeight);
        }

        if (!widthClipped && !heightClipped)
            return;

        findings.Add(new Finding(
            control.GetType().Name,
            DescribeText(control),
            wanted,
            actual,
            widthClipped,
            heightClipped));
    }

    /// <summary>
    /// 只有 TextBlock 有 TextTrimming。TextPresenter（TextBox 内部的文字绘制者）没有这个属性，
    /// 它靠父控件裁，那种情况会由外观上的 Bounds 反映出来。
    /// </summary>
    private static TextTrimming? TrimmingOf(Control control) => control switch
    {
        TextBlock block => block.TextTrimming,
        _ => null,
    };

    private static bool Wraps(Control control) => control switch
    {
        TextBlock block => block.TextWrapping != TextWrapping.NoWrap,
        TextPresenter presenter => presenter.TextWrapping != TextWrapping.NoWrap,
        _ => false,
    };

    /// <summary>把控件上显示的文字取出来，只为了在表格里能认出是哪一处。</summary>
    private static string DescribeText(Control control)
    {
        string? text = control switch
        {
            TextBlock block => block.Text,
            TextPresenter presenter => presenter.Text,
            ComboBox combo => combo.SelectedItem?.ToString() ?? combo.PlaceholderText,
            NumericUpDown numeric => numeric.Value?.ToString(),
            ContentControl content => content.Content?.ToString(),
            _ => null,
        };

        return Shorten(text);
    }

    private static string Shorten(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "(无文本)";

        // 文本框里的换行会把表格撑成两行，先压成空格再看长度。
        var flat = new string(text.Select(c => char.IsControl(c) ? ' ' : c).ToArray()).Trim();
        return flat.Length <= TextPreviewLength
            ? flat
            : string.Concat(flat.AsSpan(0, TextPreviewLength), "...");
    }

    private static void Dump(string label, List<Finding> findings)
    {
        Report.Section($"文字裁切: {label}");

        foreach (Finding finding in findings)
            Report.Fail($"{label}: {finding.Describe()}");

        var rows = findings
            .Select(finding => new[]
            {
                finding.Type,
                finding.Text,
                $"{finding.Wanted.Width:F1}x{finding.Wanted.Height:F1}",
                $"{finding.Actual.Width:F1}x{finding.Actual.Height:F1}",
                // 差只算被裁的那个方向，没超的那边记 0：否则会出现 -13.0 这种
                // 看着像"框比文字高还好"的负数，掩盖真正的超宽值。
                $"{Math.Max(0, finding.Wanted.Width - finding.Actual.Width):F1}x{Math.Max(0, finding.Wanted.Height - finding.Actual.Height):F1}",
            })
            .ToList();

        Report.PrintTable(
            new[] { "控件类型", "文本前20字", "想要 宽x高", "实际 宽x高", "超出 宽x高" },
            rows,
            $"{label} 这个窗口共 {findings.Count} 处文字被裁");
    }

    private sealed record Finding(
        string Type,
        string Text,
        Size Wanted,
        Size Actual,
        bool WidthClipped,
        bool HeightClipped)
    {
        /// <summary>失败行里写清是哪个方向被裁，方便一眼定位。</summary>
        public string Describe()
        {
            string direction = (WidthClipped, HeightClipped) switch
            {
                (true, true) => "宽和高都",
                (true, false) => "宽",
                _ => "高",
            };

            return $"{Type} \"{Text}\" {direction}被裁: 想要 {Wanted.Width:F1}x{Wanted.Height:F1}, "
                + $"实际 {Actual.Width:F1}x{Actual.Height:F1}";
        }
    }
}
