namespace TextTool.Services;

/// <summary>
/// 状态事件源接口 —— 所有 Tab 控件统一实现此接口，
/// MainForm 订阅时可泛化处理，新增 Tab 只需 : UserControl, IStatusSource。
/// </summary>
public interface IStatusSource
{
    /// <summary>状态栏文字更新</summary>
    event Action<string>? StatusChanged;

    /// <summary>错误弹窗</summary>
    event Action<string>? ErrorOccurred;
}
