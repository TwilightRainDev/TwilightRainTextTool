using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace TextTool.Services;

/// <summary>
/// 控件工厂方法和共享辅助函数，消除跨页签重复代码。
///
/// 按钮反转配色定义在此处（而非 ThemeManager），因为它是渲染层决策：
/// 白天模式 → 深色按钮 + 白色文字；深色模式 → 白色按钮 + 深色文字。
/// ThemeManager 只提供基础语义色板，不耦合控件类型。
/// </summary>
internal static class ControlsHelper
{
    /// <summary>按钮背景色（反转：白天深色背景，深色浅色背景）</summary>
    public static Color ButtonBg => ThemeManager.IsDarkMode ? Color.White : Color.FromArgb(30, 30, 30);
    /// <summary>按钮前景色（反转：白天浅色文字，深色深色文字）</summary>
    public static Color ButtonFg => ThemeManager.IsDarkMode ? Color.Black : Color.White;

    /// <summary>创建主操作按钮（Process / Join / Execute Replace）</summary>
    public static ThemedFlatButton MakePrimaryButton(string text)
    {
        var btn = new ThemedFlatButton
        {
            Text = text,
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold),
            BackColor = ButtonBg,
            ForeColor = ButtonFg,
            Padding = new Padding(24, 6, 24, 6)
        };
        btn.FlatAppearance.MouseOverBackColor = ButtonBg;
        return btn;
    }

    /// <summary>创建右对齐标签</summary>
    public static Label MakeLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        TextAlign = ContentAlignment.MiddleRight,
        Anchor = AnchorStyles.Right
    };

    /// <summary>在资源管理器中选中指定文件</summary>
    public static void RevealInExplorer(string filePath)
    {
        Process.Start("explorer.exe", $"/select,\"{filePath}\"");
    }

    /// <summary>在资源管理器中打开指定文件夹</summary>
    public static void RevealFolder(string folder)
    {
        Process.Start("explorer.exe", folder);
    }

    /// <summary>创建文本文件打开对话框</summary>
    public static OpenFileDialog CreateTextFileDialog(string title) => new()
    {
        Title = title,
        Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
        Multiselect = true,
        RestoreDirectory = true
    };

    // ================================================================
    //  统一拖放事件处理（H2-1）
    // ================================================================

    /// <summary>拖放进入：判断是否为文件拖放，高亮控件</summary>
    public static void SetupFileDragEnter(DragEventArgs e, Control target)
    {
        e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true
            ? DragDropEffects.Copy : DragDropEffects.None;
        if (e.Effect == DragDropEffects.Copy)
            target.BackColor = Color.LemonChiffon;
    }

    /// <summary>拖放悬浮：保持复制光标</summary>
    public static void SetupFileDragOver(DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
            e.Effect = DragDropEffects.Copy;
    }

    /// <summary>拖放离开：恢复控件背景色（主题感知）</summary>
    public static void ResetFileDragLeave(Control target)
    {
        target.BackColor = ThemeManager.ControlBg;
    }

    // ================================================================
    //  统一主题遍历器（H1-1）
    // ================================================================

    /// <summary>
    /// 统一对控件树应用当前主题配色。
    /// 遍历所有子控件，按类型设置对应配色规则。
    /// 特殊覆盖（如 _chkOverwrite 橙色、_lblEncoding 灰色）由调用方在之后覆盖。
    /// </summary>
    public static void ApplyTheme(Control root)
    {
        ApplyThemeToControlTree(root);
    }

    private static void ApplyThemeToControlTree(Control ctl)
    {
        switch (ctl)
        {
            // 注意：必须子类在前、基类在后
            case LinkLabel ll:
                ll.LinkColor = ThemeManager.IsDarkMode ? Color.LightBlue : Color.SteelBlue;
                ll.ActiveLinkColor = ThemeManager.IsDarkMode ? Color.DeepSkyBlue : Color.DarkBlue;
                break;
            case Label lbl:
                lbl.ForeColor = ThemeManager.Fg;
                break;
            case Button btn:
                btn.BackColor = ButtonBg;
                btn.ForeColor = ButtonFg;
                if (btn.FlatAppearance != null)
                    btn.FlatAppearance.MouseOverBackColor = ButtonBg;
                break;
            case TextBox txt:
            case ListBox lb:
            case ComboBox cmb:
            case NumericUpDown nud:
                ctl.BackColor = ThemeManager.ControlBg;
                ctl.ForeColor = ThemeManager.Fg;
                break;
            case CheckBox chk:
            case RadioButton rb:
                ctl.ForeColor = ThemeManager.Fg;
                break;
            case SplitContainer sc:
                sc.BackColor = ThemeManager.IsDarkMode ? ThemeManager.DarkControlBg : SystemColors.Control;
                ApplyThemeToControlTree(sc.Panel1);
                ApplyThemeToControlTree(sc.Panel2);
                return;
            case TableLayoutPanel tlp:
            case FlowLayoutPanel flp:
                // 容器递归子控件，不改变自身背景
                break;
            case Panel p:
                p.BackColor = ThemeManager.Bg;
                break;
        }

        if (ctl.HasChildren)
            foreach (Control child in ctl.Controls)
                ApplyThemeToControlTree(child);
    }
}
