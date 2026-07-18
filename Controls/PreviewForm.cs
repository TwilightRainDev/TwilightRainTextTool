using System.Text;
using TextTool.Localization;
using TextTool.Services;

namespace TextTool.Controls;

/// <summary>
/// 预览窗口：只读展示处理后的文本内容。
/// </summary>
public sealed class PreviewForm : Form
{
    private readonly TextBox _txtPreview;
    private readonly Button _btnSave;
    private readonly Button _btnClose;
    private readonly string _outputPath;

    public PreviewForm(List<string> lines, string outputPath)
    {
        _outputPath = outputPath;

        Text = Loc.T("PreviewTitle");
        Size = new Size(800, 600);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Consolas", 10f);
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

        _txtPreview = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Text = string.Join(Environment.NewLine, lines),
            BackColor = Color.WhiteSmoke
        };

        var btnPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 44,
            Padding = new Padding(8),
            BackColor = SystemColors.Control
        };

        _btnClose = new Button
        {
            Text = Loc.T("BtnClose"),
            AutoSize = true,
            DialogResult = DialogResult.Cancel
        };

        _btnSave = new Button
        {
            Text = Loc.T("BtnSavePreview"),
            AutoSize = true,
            BackColor = Color.SteelBlue,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(0, 0, 8, 0)
        };
        _btnSave.FlatAppearance.BorderSize = 0;
        _btnSave.Click += OnSave;

        btnPanel.Controls.Add(_btnClose);
        btnPanel.Controls.Add(_btnSave);

        var topHint = new Label
        {
            Text = Loc.T("PreviewHint"),
            Dock = DockStyle.Top,
            Height = 24,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 0, 0),
            BackColor = Color.LightYellow,
            Font = new Font("Microsoft YaHei UI", 9f)
        };

        Controls.Add(_txtPreview);
        Controls.Add(topHint);
        Controls.Add(btnPanel);

        CancelButton = _btnClose;
    }

    private void OnSave(object? sender, EventArgs e)
    {
        try
        {
            File.WriteAllLines(_outputPath, _txtPreview.Text.Split(
                new[] { Environment.NewLine }, StringSplitOptions.None),
                new UTF8Encoding(true));
            MessageBox.Show(this, Loc.T("MsgSavedTo", _outputPath),
                Loc.T("MsgSaveTitle"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, Loc.T("MsgSaveFailed", ex.Message),
                Loc.T("MsgErrorTitle"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
