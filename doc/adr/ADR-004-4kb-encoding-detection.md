# ADR-004: 编码检测 4KB 头部扫描策略

## 状态
已接受（2026-07）

## 背景
文本文件可能是 GBK、UTF-8（带/不带 BOM）或 UTF-16。全文件扫描虽然准确但大文件性能差。错误的检测会导致输出乱码。

## 决策
`EncodingDetector` 只读取文件头部 4KB（4096 字节）进行编码检测：
1. 检查 BOM（UTF-8 / UTF-16 LE / UTF-16 BE）
2. 无 BOM 时尝试 UTF-8 解码验证，修剪边界处可能的不完整多字节序列
3. UTF-8 验证失败则回退到 GBK

## 4KB 边界的特殊处理
`FileStream` 读取 4KB 可能截断一个多字节 UTF-8 字符（如 3 字节的汉字）。`TrimTrailingIncompleteUtf8()` 从末尾反向扫描，检测到不完整的 UTF-8 序列时截断到上一个完整字符，避免 UTF-8 解码误判导致回退到 GBK 产生乱码。

## 后果
- **优点**：大文件（100MB+）秒级检测，不读取整个文件
- **优点**：4KB 边界截断问题已修复（v1.5.1）
- **缺点**：文件头部 4KB 内如果是纯 ASCII 而后面出现 GBK 编码的中文，会误判为 UTF-8
- **缺点**：纯 ASCII 文件始终判定为 UTF-8（功能上正确，因为 ASCII 是合法 UTF-8）

## 关联
[[ADR-002-single-pass-pipeline]]（ProcessingPipeline 使用此检测结果）
