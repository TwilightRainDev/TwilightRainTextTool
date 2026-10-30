namespace TextTool.Tests;

/// <summary>
/// 只在 Linux 上跑的事实用例。
///
/// 结论依赖 POSIX 的这两件事：PATH 里按可执行位找命令、给文件设可执行位。
/// Windows 上由 MSYS 模拟，script 不带扩展名时按不可执行处理，用它判失败，
/// 测的是模拟层的脾气而不是启动器的行为。跳过而不是判失败。
/// </summary>
public sealed class UnixFactAttribute : FactAttribute
{
    public UnixFactAttribute()
    {
        if (!OperatingSystem.IsLinux())
            Skip = "启动器是 Linux 产物，且结论依赖 POSIX 的可执行位判定；非 Linux 环境跳过";
    }
}
