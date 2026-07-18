# ADR-005: MainForm 拆分为独立 Controls 目录

## 状态
已接受（2026-07-18）

## 背景
原 `MainForm.cs` 约 1060 行，将四个 Tab 的所有 UI 创建、事件处理和业务逻辑堆在一个类中。任何修改都必须理解整个文件，合并冲突频繁，难以分工。

## 决策
将每个 Tab 抽取为独立的 `UserControl`，放在 `Controls/` 目录下：
- `MergeTabControl.cs` — 行合并页签（414 行）
- `JoinTabControl.cs` — 文件拼接页签（151 行）
- `ReplaceTabControl.cs` — 标点替换页签（427 行）
- `AboutTabControl.cs` — 关于页签（包括版本号和语言选择器）

`MainForm` 仅负责组装四个 TabPage 和状态栏，以及广播 `ApplyLocalization()`。

## 通信模式
各 Controls 通过事件向父级报告状态和错误：
```csharp
public event Action<string>? StatusChanged;
public event Action<string>? ErrorOccurred;
```
共享数据（替换规则列表）通过构造注入传递。

## 后果
- **优点**：每个页签独立文件，降低认知负担，修改一个 Tab 只需看一个文件
- **优点**：Git diff 更清晰，冲突概率降低
- **优点**：各 Tab 可以独立测试
- **缺点**：跨 Tab 通信需要冒泡到 MainForm 再转发（当前场景只有一个跨 Tab 数据流——替换规则列表）

## 关联
[[ADR-001-pure-csharp-ui]]（所有 Controls 手写 UI，无 Designer）
