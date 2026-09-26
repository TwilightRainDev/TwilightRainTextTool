using System.Windows.Forms;

namespace TextTool.Tests.Services;

public class ControlsHelperThemeTests
{
    [Fact]
    public void ApplyTheme_Label和Button_颜色等于当前色板()
    {
        using var root = new Panel();
        Label label = new();
        Button button = new();
        root.Controls.Add(label);
        root.Controls.Add(button);

        ControlsHelper.ApplyTheme(root);

        Assert.Equal(ThemeManager.Fg, label.ForeColor);
        Assert.Equal(ControlsHelper.ButtonBg, button.BackColor);
        Assert.Equal(ControlsHelper.ButtonFg, button.ForeColor);
    }

    [Fact]
    public void ApplyTheme_TextBox和ComboBox_颜色等于当前色板()
    {
        using var root = new Panel();
        TextBox textBox = new();
        ComboBox comboBox = new();
        root.Controls.Add(textBox);
        root.Controls.Add(comboBox);

        ControlsHelper.ApplyTheme(root);

        Assert.Equal(ThemeManager.ControlBg, textBox.BackColor);
        Assert.Equal(ThemeManager.Fg, textBox.ForeColor);
        Assert.Equal(ThemeManager.ControlBg, comboBox.BackColor);
        Assert.Equal(ThemeManager.Fg, comboBox.ForeColor);
    }

    [Fact]
    public void ApplyTheme_CheckBox和RadioButton_颜色等于当前色板()
    {
        using var root = new Panel();
        CheckBox checkBox = new();
        RadioButton radioButton = new();
        root.Controls.Add(checkBox);
        root.Controls.Add(radioButton);

        ControlsHelper.ApplyTheme(root);

        Assert.Equal(ThemeManager.Fg, checkBox.ForeColor);
        Assert.Equal(ThemeManager.Fg, radioButton.ForeColor);
    }
}
