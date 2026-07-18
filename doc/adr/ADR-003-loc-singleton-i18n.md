# ADR-003: Loc 单例实现国际化（i18n）

## 状态
已接受（2026-07）

## 背景
工具用户横跨简中、繁中、英文三个语言群体。需要一个轻量级的 i18n 方案，不引入外部库。

## 决策
使用自定义 `Loc` 单例模式，搭配 JSON 语言包：
- `Loc.Init()` 在 `MainForm` 构造时调用，自动检测系统 UI 文化
- 每个语言一个 JSON 文件（`zh_CN.json`、`zh_TW.json`、`en_US.json`）
- `Loc.T("Key")` 获取翻译文本，支持 `string.Format` 风格的参数
- 语言偏好持久化到 `app_config.json`
- 语言切换时触发 `Loc.LanguageChanged` 事件，各页签通过 `ApplyLocalization()` 刷新文本

## 后果
- **优点**：零外部依赖，纯 `System.Text.Json` 实现
- **优点**：JSON 语言包比 Resource (.resx) 文件更易编辑和审查 diff
- **优点**：切换语言无需重启，即时生效
- **缺点**：缺少 ICU 消息格式、复数规则等高级功能（当前场景不需要）
- **缺点**：新增语言需要手动创建 JSON 文件并在 `Strings.cs` 中注册

## 关联
[[ADR-001-pure-csharp-ui]]（`ApplyLocalization()` 遍历各页签刷新 UI）
