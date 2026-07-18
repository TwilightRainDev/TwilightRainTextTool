# ADR-007: xUnit 单元测试覆盖核心 Services

## 状态
已接受（2026-07-18）

## 背景
v1.6.0 之前完全没有测试。所有功能依赖手动拖拽文件验证，回归测试耗时长且不可靠。

## 决策
使用 xUnit 为所有 `Services/` 类编写单元测试：
- 测试项目 `TextTool.Tests` 使用 `net7.0-windows` 目标框架，引用主项目
- 每个 Service 一个测试文件，测试方法遵循 `[Method]_[Scenario]_[Expected]` 命名
- 使用 `TempFile` / `TempDir` 夹具管理临时文件（`IDisposable` 自动清理）
- 文件 I/O 测试使用临时路径，不依赖外部文件

## 覆盖范围
| Service | 测试数 | 关键场景 |
|---------|--------|---------|
| LineMerger | 7 | 字节/字符模式、阈值、noMerge 阻止合并 |
| CjkParagraphMerger | 7 | CJK 检测、跨行合并、noMerge 优先、空白行、Extension B |
| PunctTruncationMerger | 6 | 标点触发合并、noMerge 优先、多标点 |
| PunctuationReplacer | 7 | 替换、空规则跳过、null Find |
| TextUtils | 10 | StringBuilder/string 重载、空白跳过、空/Null set |
| EncodingDetector | 8 | BOM、UTF-8 验证、GBK 回退、4KB 边界截断 |
| ProcessingPipeline | 4 | 完整流水线集成、TrimLeadingComma |
| FileJoiner | 5 | 多文件合并、编码混合、CRLF 处理 |

## 后果
- **优点**：修改代码后可以快速验证没有破坏既有功能
- **优点**：边界情况（空文件、边界截断、null 参数）有明确测试
- **优点**：测试即文档，新开发者通过测试理解各 Service 的预期行为
- **缺点**：文件 I/O 测试比纯逻辑测试慢（但仍 < 300ms 总运行时间）
- **缺点**：ProcessingPipeline 测试验证集成但需要临时文件

## 关联
[[ADR-002-single-pass-pipeline]]（Pipeline 的可测试性是其设计目标之一）
