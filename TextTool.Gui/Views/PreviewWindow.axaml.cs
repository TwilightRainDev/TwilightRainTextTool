using Avalonia.Controls;
using TextTool.Gui.Localization;

namespace TextTool.Gui.Views;

/// <summary>
/// 预览窗：只读显示处理后的文本。Step 7 会补上文件信息、行数与主题细节，
/// 这里先保证预览这条链路通，内容与写盘前的那份文本完全一致。
/// </summary>
public partial class PreviewWindow : Window
{
    public PreviewWindow()
    {
        InitializeComponent();
    }

    public PreviewWindow(string text, string outputPath) : this()
    {
        Title = $"{Loc.T("BtnPreview")} - {System.IO.Path.GetFileName(outputPath)}";
        PathLabel.Text = outputPath;
        ContentBox.Text = text;
    }
}
