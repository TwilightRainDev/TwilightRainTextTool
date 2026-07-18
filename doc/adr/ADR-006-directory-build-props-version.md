# ADR-006: Directory.Build.props 集中管理版本号

## 状态
已接受（2026-07-18）

## 背景
版本号散落在 `TextTool.csproj`（`<Version>`）、`AboutTabControl.cs` 的硬字符串、以及注释中。每次升级版本要改 3-5 个文件，容易遗漏导致版本号不一致。

## 决策
使用 `Directory.Build.props` 作为 MSBuild 属性的单一来源：
```xml
<Project>
  <PropertyGroup>
    <Version>1.6.1</Version>
  </PropertyGroup>
</Project>
```
- `TextTool.csproj` 不再设置 `<Version>`，自动继承
- `AboutTabControl` 通过 `Assembly.GetName().Version` 运行时读取，不再有硬编码
- `AssemblyVersion` = `1.6.1.0`（.NET 自动追加 Revision=0）

## 后果
- **优点**：改版本号只需改一个文件
- **优点**：源码零硬编码版本字符串
- **优点**：遵循 .NET 生态标准做法
- **注意**：`Assembly.GetName().Version` 只返回 `Major.Minor.Build`（Revision 被忽略），如果未来需要语义版本号（如 1.6.1-beta.1），需改用 `AssemblyInformationalVersion`

## 关联
[[ADR-005-controls-extraction]]（AboutTabControl 负责显示版本号）
