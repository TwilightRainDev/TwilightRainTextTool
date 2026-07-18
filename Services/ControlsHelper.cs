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
}
