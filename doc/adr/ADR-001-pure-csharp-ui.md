# ADR-001: 纯 C# 代码构建 UI（无 Designer 文件）

## 状态
已接受（2026-07）

## 背景
TextTool 是 WinForms 应用。传统 WinForms 开发依赖 Visual Studio 的 Designer 生成 `*.Designer.cs` 文件，但 Designer 生成的代码可读性差、diff 难以审查、且容易引发合并冲突。

## 决策
全部 UI 控件使用纯 C# 代码在 `InitializeComponent()` 方法中手动创建和布局，不依赖 Designer 文件，也未使用任何 WinForms 设计器。

## 后果
- **优点**：代码完全可控，diff 清晰可审查，无自动生成的垃圾代码
- **优点**：布局逻辑就在控件旁边，阅读代码即可理解界面结构
- **优点**：无需安装 Visual Studio，任何编辑器均可修改
- **缺点**：手写布局代码比拖拽 Designer 耗时，需要手动计算 TableLayoutPanel 的行列位置
- **缺点**：调整布局时需要重新编译才能看到效果

## 关联
所有 Controls（`Controls/MergeTabControl.cs`、`Controls/JoinTabControl.cs`、`Controls/ReplaceTabControl.cs`、`Controls/AboutTabControl.cs`）均遵循此模式。
