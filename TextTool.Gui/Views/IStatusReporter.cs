namespace TextTool.Gui.Views;

/// <summary>
/// 会往状态栏说话、也会报错的页签。MainWindow 按这个接口统一连线，
/// 不必知道对面是哪一个页签。
/// </summary>
public interface IStatusReporter
{
    /// <summary>状态栏文字。</summary>
    event Action<string>? StatusChanged;

    /// <summary>需要让用户知道出了错。</summary>
    event Action<string>? ErrorOccurred;
}
