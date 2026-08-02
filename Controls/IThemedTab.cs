namespace TextTool.Controls;

/// <summary>
/// 页签控件接口 — MainForm 据此强类型调用 ApplyTheme / ApplyLocalization，
/// 替代原反射 GetMethod(...).Invoke 方式。漏实现方法名变成编译错误而非静默跳过。
/// </summary>
public interface IThemedTab
{
    void ApplyTheme();
    void ApplyLocalization();
}
