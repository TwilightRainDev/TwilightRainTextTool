using TextTool.Localization;
using TextTool.Services;

namespace TextTool.Controls;

/// <summary>
/// "文件拼接" 页签：选择目录 → 按匹配模式合并多个文件。
/// </summary>
public sealed class JoinTabControl : UserControl
{
    public event Action<string>? StatusChanged;
    public event Action<string>? ErrorOccurred;

    private Label _lblFolder = null!;
    private TextBox _txtFolder = null!;
    private Button _btnBrowseFolder = null!;
    private Label _lblPattern = null!;
    private TextBox _txtPattern = null!;
    private Label _lblOutputFile = null!;
    private TextBox _txtOutputName = null!;
    private Button _btnJoin = null!;

    public JoinTabControl()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 5,
            Padding = new Padding(16, 16, 16, 8)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        _lblFolder = MakeLabel("Folder:");
        layout.Controls.Add(_lblFolder, 0, 0);
        _txtFolder = new TextBox
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Text = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
        };
        layout.Controls.Add(_txtFolder, 1, 0);
        _btnBrowseFolder = new Button { Text = "Browse...", AutoSize = true };
        _btnBrowseFolder.Click += OnBrowseFolder;
        layout.Controls.Add(_btnBrowseFolder, 2, 0);

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        _lblPattern = MakeLabel("Pattern:");
        layout.Controls.Add(_lblPattern, 0, 1);
        _txtPattern = new TextBox { Text = "*.txt", Anchor = AnchorStyles.Left | AnchorStyles.Right };
        layout.Controls.Add(_txtPattern, 1, 1);

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        _lblOutputFile = MakeLabel("Output:");
        layout.Controls.Add(_lblOutputFile, 0, 2);
        _txtOutputName = new TextBox { Text = "combined.txt", Anchor = AnchorStyles.Left | AnchorStyles.Right };
        layout.Controls.Add(_txtOutputName, 1, 2);

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        _btnJoin = MakePrimaryButton("Join");
        _btnJoin.Click += OnJoin;
        layout.Controls.Add(_btnJoin, 1, 3);

        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(layout);
    }

    public void ApplyLocalization()
    {
        _lblFolder.Text = Loc.T("LabelFolder");
        _lblPattern.Text = Loc.T("LabelPattern");
        _lblOutputFile.Text = Loc.T("LabelOutputFile");
        _btnJoin.Text = Loc.T("BtnJoin");
    }

    private void OnBrowseFolder(object? sender, EventArgs e)
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = Loc.T("TabJoin"),
            UseDescriptionForTitle = true,
            InitialDirectory = _txtFolder.Text
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
            _txtFolder.Text = dlg.SelectedPath;
    }

    private void OnJoin(object? sender, EventArgs e)
    {
        string folder = _txtFolder.Text.Trim();
        string pattern = _txtPattern.Text.Trim();
        string outputName = _txtOutputName.Text.Trim();
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) { ErrorOccurred?.Invoke(Loc.T("MsgInvalidFolder")); return; }
        if (string.IsNullOrEmpty(pattern)) { ErrorOccurred?.Invoke(Loc.T("MsgEmptyPattern")); return; }
        if (string.IsNullOrEmpty(outputName)) { ErrorOccurred?.Invoke(Loc.T("MsgEmptyOutput")); return; }

        try
        {
            _btnJoin.Enabled = false; _btnJoin.Text = Loc.T("StatusJoining");
            StatusChanged?.Invoke(Loc.T("StatusJoining"));
            var (outputPath, fileCount) = FileJoiner.Join(folder, pattern, outputName);
            StatusChanged?.Invoke(Loc.T("StatusJoinComplete", fileCount, outputPath));

            if (MessageBox.Show(this,
                Loc.T("MsgJoinBody", outputPath),
                Loc.T("MsgJoinTitle"),
                MessageBoxButtons.OK, MessageBoxIcon.Information) == DialogResult.OK)
            {
                RevealInExplorer(outputPath);
            }
        }
        catch (Exception ex) { ErrorOccurred?.Invoke(Loc.T("MsgJoinFailed", ex.Message)); }
        finally { _btnJoin.Enabled = true; _btnJoin.Text = Loc.T("BtnJoin"); }
    }

    private static void RevealInExplorer(string filePath)
    {
        System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{filePath}\"");
    }

    private static Label MakeLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        TextAlign = ContentAlignment.MiddleRight,
        Anchor = AnchorStyles.Right
    };

    private static Button MakePrimaryButton(string text)
    {
        var btn = new Button
        {
            Text = text,
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold),
            BackColor = Color.SteelBlue,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Padding = new Padding(24, 6, 24, 6)
        };
        btn.FlatAppearance.BorderSize = 0;
        return btn;
    }
}
