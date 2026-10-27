namespace TextTool.Tests;

/// <summary>
/// 只在 Windows 上跑的事实用例。
///
/// 自更新是 Windows 专有链路：脚本经 cmd.exe 执行，资产名按 win-x64 一类 RID 匹配。
/// 在 WSL/Linux 上跑，cmd.exe 收到 <c>\\wsl.localhost\…</c> 这种 UNC 路径会直接拒绝，
/// RID 也对不上——测的既不是产品行为，也不是回归。跳过而不是判失败，否则这两个环境
/// 里必有一条命令永远过不去（本仓库的 pre-commit 钩子会跑全量测试）。
/// </summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
            Skip = "自更新只在 Windows 上可用（cmd.exe + win RID 资产名），非 Windows 环境跳过";
    }
}
