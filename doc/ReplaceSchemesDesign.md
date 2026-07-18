# 预设替换方案勾选功能设计

## 概述

为"标点替换"页签增加"预设替换方案"功能，将 `replace_rules.json` 中的众多替换规则按类别抽取为 9 个预设方案，用户可通过新窗口勾选方案批量追加规则，并支持自定义方案。

## 需求要点

- 方案按规则类别分组（标点符号转换、引号括号统一、特殊符号清除等）
- 在提示标签下方新增「📋 预设替换方案勾选」按钮
- 点击后弹出新窗口，展示所有方案（可勾选/取消）
- 确定后，所选方案的规则 **追加** 到当前规则列表末尾
- 预设方案和自定义方案均可编辑（增删改规则）
- 支持新建自定义方案

## 数据模型

### 新增文件 `Services/ReplaceScheme.cs`

```csharp
public class ReplaceScheme
{
    public string Name { get; set; }       // 方案名称
    public string Description { get; set; } // 方案说明
    public bool IsBuiltIn { get; set; }     // 是否为内置预设
    public List<ReplaceRule> Rules { get; set; }
}
```

### 新增文件 `replace_schemes.json`

与 `replace_rules.json` 同级存放，格式：
```json
[
  {
    "name": "标点符号转换",
    "description": "英文标点符号转中文标点",
    "isBuiltIn": true,
    "rules": [ ... ]
  }
]
```

### 9 个预设方案

| # | 方案名 | 规则数 | 说明 |
|---|--------|--------|------|
| 1 | 标点符号转换 | 6 | `.` `,` `!` `?` `;` `:` → 中文标点 |
| 2 | 引号括号统一 | 17 | 各种引号/括号统一为「」，`（）` 转半角 |
| 3 | 特殊符号清除 | 23 | 删除 `■□●★☆♪♯` 等装饰/音乐符号及 `《》<>` |
| 4 | 横线省略号→逗号 | 13 | `--` `...` `—` `–` 等各种横线、省略号 → `，` |
| 5 | 重复矛盾标点整理 | 15 | `、、→、` `，，→，` `。。→。` 等合并整理 |
| 6 | 全角字母数字→半角 | 62 | 全角英文字母和数字 → 半角 |
| 7 | 阿拉伯数字→中文 | 10 | `0→零` ... `9→九` |
| 8 | 语气词清理 | 24 | `嗯，→，` `哦。→。` 等口语语气词清理 |
| 9 | 词语替换 | 5 | `chapter→章节` `※→注` 等 |

## UI 设计

### ReplaceTabControl 改动

- 在右侧面板提示标签下方新增第 4 行，放置 `ThemedFlatButton`（`_btnPresetSchemes`）
- 按钮文本：「📋 预设替换方案勾选」

### 方案选择窗口 `SchemeSelectionForm`（新窗口）

- 尺寸 620×520，居中父窗体
- 左侧/主体：`TreeView`（CheckBoxes=true），每个方案为一个根节点，展开显示其规则
- 勾选父节点 → 级联勾选所有子节点
- 底部操作栏：「新建方案」「编辑方案」「删除方案」
- 底部状态栏：「已勾选 N 个方案，共 M 条规则」
- 「确定」→ 收集勾选方案的规则列表返回给调用方

### 方案编辑对话框 `EditSchemeDialog`

- 尺寸 520×420
- 方案名称 TextBox + 方案说明 TextBox
- 规则列表 ListBox
- 查找/替换为 TextBox + 添加/更新 + 删除所选 按钮
- 支持选中规则回填编辑（类似 ReplaceTabControl 的规则编辑器）

## 数据流

```
用户点击「预设替换方案勾选」按钮
  → ReplaceSchemeStore.Load()  加载 replace_schemes.json（或返回默认方案）
  → 打开 SchemeSelectionForm（用户勾选方案、可选编辑方案）
  → 用户点击确定
  → 收集勾选方案的规则列表
  → _rules.AddRange(selected) 追加到当前规则
  → ReplaceRuleStore.Save()   保存 replace_rules.json
  → ReplaceSchemeStore.Save() 保存 replace_schemes.json（方案可能被编辑）
  → 刷新规则列表
```

## 修改文件清单

| 文件 | 操作 | 说明 |
|------|------|------|
| `Services/ReplaceScheme.cs` | 新增 | 数据模型 + 持久化存储 + 默认方案定义 |
| `Controls/SchemeSelectionForm.cs` | 新增 | 方案选择窗口 + 方案编辑对话框 |
| `Controls/ReplaceTabControl.cs` | 修改 | 添加按钮字段、初始化、事件处理、本地化、主题 |
| `Localization/zh_CN.json` | 修改 | 添加 19 条新翻译键 |
| `Localization/en_US.json` | 修改 | 添加 19 条新翻译键 |
| `Localization/zh_TW.json` | 修改 | 添加 19 条新翻译键 |
