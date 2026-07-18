using System.Drawing;
using System.Windows.Forms;

namespace TextTool.Services;

/// <summary>
/// 自定义 FlatStyle.Flat 按钮，修复 WinForms 在 Enabled=false 时
/// 忽略 ForeColor 的 bug。禁用状态下仍使用 ForeColor 渲染文本。
/// </summary>
public class ThemedFlatButton : Button
{
    private SolidBrush? _bgBrush;

    public ThemedFlatButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
    }

    protected override void OnBackColorChanged(EventArgs e)
    {
        base.OnBackColorChanged(e);
        _bgBrush?.Dispose();
        _bgBrush = new SolidBrush(BackColor);
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        if (Enabled)
        {
            // 启用状态：让基类正常绘制（WinForms 的 FlatStyle.Flat 逻辑）
            base.OnPaint(pevent);
            return;
        }

        // ===== 禁用状态：手动绘制，确保使用 ForeColor =====

        var g = pevent.Graphics;
        var rect = ClientRectangle;

        // 1. 绘制背景（使用缓存的画刷，避免每次分配）
        g.FillRectangle(_bgBrush ?? (_bgBrush = new SolidBrush(BackColor)), rect);

        // 2. 绘制边框（如果有）
        if (FlatAppearance.BorderSize > 0 && FlatAppearance.BorderColor != Color.Empty)
        {
            using var borderPen = new Pen(FlatAppearance.BorderColor, FlatAppearance.BorderSize);
            var borderRect = rect;
            borderRect.Width--;
            borderRect.Height--;
            g.DrawRectangle(borderPen, borderRect);
        }

        // 3. 绘制文本 — 强制使用 ForeColor！
        if (!string.IsNullOrEmpty(Text))
        {
            TextRenderer.DrawText(g, Text, Font, rect, ForeColor, Color.Transparent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _bgBrush?.Dispose();
        base.Dispose(disposing);
    }
}
