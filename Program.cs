using System.Text;

namespace TextTool;

static class Program
{
    [STAThread]
    static void Main()
    {
        // 注册 GBK (codepage 936) 等额外编码支持
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.Run(new MainForm());
    }
}
