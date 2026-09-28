# TextTool 技术债 D-1–D-5 清除设计

> 日期：2026-09-26 ｜ 状态：已批准开工 ｜ 类型：设计文档
> 来源：用户指令「开始内驱驱动清除技术债务在 D-1 到 D-5」
> 权威清单：`doc/TECH-DEBT.md` §1（D-1–D-5）。本轮只消这五条，不顺手改 lint 契约。
> 前置事实：`main` 在 `2.6.0`（GUI lint 已合入）。五条彼此几乎无耦合，可按编号逐条落地。

## 1. 背景与目标

`TECH-DEBT.md` 上还挂着五条已核实的债：编码标签不随语言走、预览窗口多一次 join/split、发布仍靠手敲、主题遍历器没测试、内置方案有一份会静默吞错的内联副本。

**目标**：五条全部按建议方向落地。版本 `2.6.1`（债清除，不是新产品能力）。

**成功标准**：中文界面下编码标签走 `Loc`；`PreviewForm(List)` 不再经 string 往返；`scripts/publish.ps1` 能把检查清单跑成一条命令（测试只覆盖校验，不打真网）；`ApplyTheme` 有类型分派测试；两份 `GetHardcodedSchemes` 删除，资源缺失抛 `InvalidOperationException`。

## 2. 范围

### In scope

| 编号 | 项 | 归属 |
|---|---|---|
| D-1 | `DetectionResult` 改带枚举；UI 经 `Loc.T` 显示；`DetectStrict` 改判枚举 | Core + Tests + Loc + 三页签 |
| D-2 | `PreviewForm` 两个公开重载共用一份构造，List 路径不再 join+split | GUI |
| D-3 | `scripts/publish.ps1`：`-Version` / `-SkipUpload` / `-Force`，对齐 `doc/UpdateSecurity.md` | 仓库工具 + 文档 |
| D-4 | `ControlsHelper.ApplyTheme` 类型分派测试；WinForms 项目开 `InternalsVisibleTo` | Tests + csproj |
| D-5 | 删除两份内联兜底；抽出可测的嵌入式 JSON 读取；资源缺/空即抛 | Core + Tests |

### Out of scope（明确不做）

- **lint 引擎 / 规则 JSON / `--json` / `--fix`**：§5 已知遗留原样保留
- **2.4.5 规格里的 A1/A2/D7**：下载超时分级、wait-loop、私钥加密。D-3 只做 B1 发布脚本
- **打 git tag / 发 GitHub Release / 真机签名上传**：脚本落地即可，本轮不跑发布
- **给页签加功能测试**：D-4 只测 `ApplyTheme` 遍历器，不测三个页签的交互
- **`GetDefaultSchemes` 加缓存**：§5 已登记为可留
- **翻译编码名以外的技术专名**（如规则 Title）

## 3. 方案选型

**选定：五条各做最小闭环，最后统一改版本与债表。**

备选（不采用）：

- **只做 D-5**：用户已点名 D-1 到 D-5
- **DetectionResult 继续产英文串、UI 查表翻译**：`DetectStrict` 仍靠魔串，债没消干净
- **publish.ps1 在测试里打真实 `gh`**：会碰网络和凭据，禁止

## 4. 各项设计

### D-1 编码显示国际化

Core 不引用 `Loc`（`TextTool.Core` 是 `net8.0`，无 WinForms）。

```csharp
public enum DetectedEncoding
{
    Utf8Bom,
    Utf8,
    Utf16Le,
    Utf16Be,
    Gbk
}

public record DetectionResult(Encoding Encoding, DetectedEncoding Kind)
{
    public string LocKey => Kind switch
    {
        DetectedEncoding.Utf8Bom => "EncodingUtf8Bom",
        DetectedEncoding.Utf8 => "EncodingUtf8",
        DetectedEncoding.Utf16Le => "EncodingUtf16Le",
        DetectedEncoding.Utf16Be => "EncodingUtf16Be",
        DetectedEncoding.Gbk => "EncodingGbk",
        _ => throw new ArgumentOutOfRangeException(nameof(Kind), Kind, null)
    };
}
```

`Detect` / `DetectStrict` / `DetectFromBytes` 一律构造 `Kind`，**删除 `DisplayName`**。`DetectStrict` 信任快速路径的条件改为：

```csharp
if (headerResult.Kind is DetectedEncoding.Utf8Bom
    or DetectedEncoding.Utf16Le
    or DetectedEncoding.Utf16Be)
    return headerResult;
```

回退 GBK 时用 `DetectedEncoding.Gbk`，不再写 `"GBK (ANSI)"`。

三语键（插在现有 `EncodingNotSelected` 旁）：

| 键 | zh_CN | zh_TW | en_US |
|---|---|---|---|
| `EncodingUtf8Bom` | UTF-8（BOM） | UTF-8（BOM） | UTF-8 (BOM) |
| `EncodingUtf8` | UTF-8 | UTF-8 | UTF-8 |
| `EncodingUtf16Le` | UTF-16 小端 | UTF-16 小端 | UTF-16 LE |
| `EncodingUtf16Be` | UTF-16 大端 | UTF-16 大端 | UTF-16 BE |
| `EncodingGbk` | GBK（ANSI） | GBK（ANSI） | GBK (ANSI) |

UI：`Loc.T(result.LocKey)`。写入点：

- `ReplaceTabControl.cs` 选文件后的编码标签；补 `_lastDetection`，`ApplyLocalization` 在已检测时重译
- `MergeTabControl.cs` / `VNTabControl.cs` 已有 `_lastDetection`：检测时写 `Loc.T(...)`；`ApplyLocalization` 在非 null 时同样重译（今天语言一切换，已选文件的编码标签会留在旧语言）

测试：`EncodingDetectorTests` 全部 `DisplayName` 断言改为 `Kind`。另加 `LocKey` 五值映射的小测试（可放同一文件）。

### D-2 预览构造去往返

`List<string>` 重载不再委托 `string.Join`。`string` 重载先拆行再委托 `List` 重载：

```csharp
public PreviewForm(List<string> lines, string outputPath)
{
    _lines = lines;
    _outputPath = outputPath;
    // 原 string 重载里的 UI 初始化整段搬到这里
}

public PreviewForm(string text, string outputPath)
    : this(text.Split('\n').Select(l => l.TrimEnd('\r')).ToList(), outputPath)
{
}
```

保存路径、主题、文案不变。本仓不给 WinForms 窗体写测试；现有套件不回归即过。

### D-3 `scripts/publish.ps1`

对齐 `doc/specs/2026-08-03-texttool-2.4.5-design.md` B1，并写进 `doc/UpdateSecurity.md` 发布流程。

```
.\scripts\publish.ps1 -Version 2.6.1 [-SkipUpload] [-Force]
                      [-KeyPath <priv.pem>] [-Repo owner/name]
```

行为：

| 条件 | 行为 |
|---|---|
| 未传 `-Version`，或值不匹配 `^v?\d+\.\d+\.\d+$` | 写错误信息到 stderr，exit 2（不要把 `Version` 标成 `Mandatory`，否则缺参会交互等待） |
| 合法 | 去掉可选 `v` 前缀得到 `raw`（如 `2.6.1`），tag 为 `v$raw` |
| 下载 | `gh release download $tag --repo $Repo --pattern "TextTool-*-$raw-win-x64.zip*" --dir $work`（zip + sha256） |
| zip 或对应 `.sha256` 缺任一 | exit 1 |
| 签名 | `dotnet build tools/ReleaseSigner -c Release`（惰性）；对每个 zip 调 `sign`，私钥默认 `E:\work_zone\ApiKey\TextTool-signing.priv.pem`，可用 `-KeyPath` 覆盖；passphrase 只读环境变量 `TEXTTOOL_SIGN_PASSPHRASE`，**脚本内不写死任何密钥** |
| 自检 | 每个 zip 先 `verify`（公钥与私钥同目录的 `TextTool-signing.pub.pem`，或 `-KeyPath` 换扩展名 `.pub.pem`），失败 exit 1 |
| `.sig` 已在 release 且未 `-Force` | 不上传，exit 1，提示用 `-Force` |
| `-SkipUpload` | 走到 verify 后打印清单并 exit 0，不调用 `gh release upload` |
| 默认 | `gh release upload $tag *.sig` |

`.gitattributes` 补 `*.ps1 text eol=lf`。

测试（不打网）：`TextTool.Tests/Scripts/PublishScriptTests.cs` 用 `pwsh`（没有则 `powershell`）起脚本：

- `-Version "abc"` → exit 2，stderr 含版本格式提示
- 缺 `-Version` → 非 0（Mandatory 参数）
- 读脚本正文：含 `SkipUpload`、`Force`、`ReleaseSigner`、`verify`；**不含** `GithubApiToken`、`ghp_`、私钥 PEM 正文

不测真实 `gh download` / 签名 / 上传。

### D-4 主题遍历器测试

`TextTool.csproj` 增加：

```xml
<ItemGroup>
  <InternalsVisibleTo Include="TextTool.Tests" />
</ItemGroup>
```

`TextTool.Tests/Services/ControlsHelperThemeTests.cs` 三例（浅色或当前 `ThemeManager` 值均可，断言跟实时色板走，不调用 `Toggle()`，避免写 `app_config.json`）：

1. `Label`：`ForeColor == ThemeManager.Fg`；`Button`：`BackColor == ControlsHelper.ButtonBg` 且 `ForeColor == ControlsHelper.ButtonFg`
2. `TextBox` 与 `ComboBox`：`BackColor == ThemeManager.ControlBg` 且 `ForeColor == ThemeManager.Fg`
3. `CheckBox` 与 `RadioButton`：`ForeColor == ThemeManager.Fg`

根控件用一个 `Panel` 挂上这些子控件再 `ApplyTheme(root)`。不测 `LinkLabel` / `SplitContainer` / `Panel` 自身（原计划点名的六类）。

### D-5 删除内联兜底

新建 `TextTool.Core/EmbeddedResource.cs`：

```csharp
internal static class EmbeddedResource
{
    public static List<T> LoadJsonList<T>(string resourceName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"缺少嵌入式资源 {resourceName}，请检查 csproj 的 EmbeddedResource 配置");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var list = JsonSerializer.Deserialize<List<T>>(reader.ReadToEnd());
        if (list is null || list.Count == 0)
            throw new InvalidOperationException($"嵌入式资源 {resourceName} 为空或无法解析");
        return list;
    }
}
```

`ReplaceSchemeStore.GetDefaultSchemes` / `VNCharacterSchemeStore.GetDefaultSchemes` 改为 `return EmbeddedResource.LoadJsonList<...>(ResourceName);`。删除两个 `GetHardcodedSchemes` 及对它们的 `catch { }`。

`ResourceName` 注释里「静默回退」的说法改成「缺失即抛」。

测试：保留现有「资源在」四例；新增 `LoadJsonList` 对不存在资源名抛 `InvalidOperationException` 且消息含该名。不测空 JSON（没有第二份嵌入资源可卸）。

## 5. 实施顺序

D-1 → D-2 → D-3 → D-4 → D-5 → 文档与 `2.6.1`。

无跨任务接口依赖。版本号只在最后一任务改。

## 6. 风险

| 项 | 风险 | 缓解 |
|---|---|---|
| D-1 删 `DisplayName` | 仓外若有人读该属性会编译失败 | 本仓是唯一消费者；测试全改 `Kind` |
| D-3 脚本 | 误跑真上传 | 测试只走非法版本；文档写明先 `-SkipUpload` |
| D-5 抛错 | 资源名再写错会启动失败而不再静默 | 现有 ResourceName 常量测试钉死正确名 |
| D-4 WinForms 测试 | 无消息循环时个别属性异常 | 只设颜色，不 `Show()` |

行尾约定：本仓库文本文件为 LF。文档不得含 CRLF。禁用 emoji。
