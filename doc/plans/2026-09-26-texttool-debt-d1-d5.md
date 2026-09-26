# TextTool 技术债 D-1–D-5 实施计划

> **对于执行者：** 必需子技能：使用 SubagentDrivenDev（推荐）或 ExecutingPlans 逐任务实施此计划。步骤使用复选框（`- [ ]`）语法进行跟踪。
> 规格来源：`doc/specs/2026-09-26-texttool-debt-d1-d5-design.md`（已批准）。债条原文以 `doc/TECH-DEBT.md` §1 为准。本计划与规格冲突时以规格为准，并把冲突回报给用户。

**目标：** 清除 D-1 到 D-5；版本升至 `2.6.1`。

**架构：** 五条独立闭环。Core 枚举替换魔串；GUI 经 `Loc` 显示；发布脚本只做仓库工具；主题测试走 `InternalsVisibleTo`；内置方案只认嵌入式 JSON。

**技术栈：** .NET 8、C#、WinForms、xUnit、PowerShell 5.1+。

## 问题陈述

五条已核实的债还在：编码标签不随语言走、预览多一次 join/split、发布靠手敲、主题遍历器无测试、内置方案有一份会吞错的内联副本。

## 解决方案

按规格逐条落地。不改 lint 契约，不发 Release，不加密私钥。

## 用户故事

1. 作为切到简体/繁体的用户，我想在合并/替换/VN 页签看到已翻译的编码名。
2. 作为打开预览的用户，我不想多付一次无意义的字符串往返。
3. 作为发版的人，我想用一条脚本走完下载、签名、校验、上传。
4. 作为改主题的人，我想类型分派被测试钉住。
5. 作为改默认方案的人，我只想改 JSON；资源坏了应该响亮失败。

## 全局约束

- 目标框架：Core / CLI / Tests 为 `net8.0`（Tests 为 `net8.0-windows`）；GUI 为 `net8.0-windows`。
- 行尾 LF、无 BOM（`.gitattributes` 强制）；禁用 emoji（用 `[OK]` / `[FAIL]` / `[NOTE]`）。
- 命名与风格以现有代码为准：file-scoped namespace、私有字段 `_camelCase`、`PascalCase.cs`、XML 文档注释写中文。
- **局部变量与参数一律 camel_case**（`.editorconfig` 的 `locals_should_be_camelcase`）。`dotnet format --verify-no-changes` 会报 `IDE1006`。
- **量门禁退出码时不要接管道**：`cmd > log 2>&1; echo $?`。PowerShell 5.1 不要用 `&&`，用 `;`。
- 不改 `AiToneLintService`、`LintReport`、`default_lint_rules.json`、自更新客户端行为。
- 不调用真实 `gh release upload`，不把 `ApiKey` 或 token 写进仓库。
- 质量门禁（每个任务提交前跑）：`dotnet build TextTool.sln -c Release -warnaserror`、`dotnet test TextTool.Tests/TextTool.Tests.csproj -c Release`、`dotnet format TextTool.sln --verify-no-changes`。
- 提交信息中文，前缀 `feat:` / `fix:` / `refactor:` / `test:` / `docs:`。正文另起一行写 `Co-Authored-By: Cursor <noreply@cursor.com>`。署名沿用本仓惯例（`git log -1 --format=%an <%ae>` 现为 `TwilightRainDev <122437146+TwilightRainDev@users.noreply.github.com>`），不要改 `git config`、不要用本机其它身份。
- 在分支 `feat/tech-debt-d1-d5` 上实施，不要提交到 `main`。
- 版本号只在任务 6 改：`2.6.0` → `2.6.1`。

## 任务

### Task 1：D-1 EncodingDetector 国际化

**文件：**

- 修改：`TextTool.Core/EncodingDetector.cs`
- 修改：`TextTool.Tests/Services/EncodingDetectorTests.cs`
- 修改：`Localization/zh_CN.json`、`zh_TW.json`、`en_US.json`
- 修改：`Controls/ReplaceTabControl.cs`、`Controls/MergeTabControl.cs`、`Controls/VNTabControl.cs`

**接口：**

- 删除 `DetectionResult.DisplayName`
- 新增 `public enum DetectedEncoding { Utf8Bom, Utf8, Utf16Le, Utf16Be, Gbk }`
- `public record DetectionResult(Encoding Encoding, DetectedEncoding Kind)`，带 `public string LocKey`（规格 §4 D-1 的 switch）
- 消费：三个页签 `Loc.T(result.LocKey)`

- [ ] **步骤 1：编写失败的测试**

在 `EncodingDetectorTests` 里：所有 `result.DisplayName` / `.DisplayName` 断言改为对应 `Kind`：

| 原 DisplayName | Kind |
|---|---|
| `"UTF-8 (BOM)"` | `DetectedEncoding.Utf8Bom` |
| `"UTF-16 LE"` | `DetectedEncoding.Utf16Le` |
| `"UTF-16 BE"` | `DetectedEncoding.Utf16Be` |
| `"UTF-8"` | `DetectedEncoding.Utf8` |
| `"GBK (ANSI)"` | `DetectedEncoding.Gbk` |

同一文件追加：

```csharp
[Theory]
[InlineData(DetectedEncoding.Utf8Bom, "EncodingUtf8Bom")]
[InlineData(DetectedEncoding.Utf8, "EncodingUtf8")]
[InlineData(DetectedEncoding.Utf16Le, "EncodingUtf16Le")]
[InlineData(DetectedEncoding.Utf16Be, "EncodingUtf16Be")]
[InlineData(DetectedEncoding.Gbk, "EncodingGbk")]
public void DetectionResult_LocKey_MatchesKind(DetectedEncoding kind, string key)
{
    var encoding = kind is DetectedEncoding.Gbk
        ? Encoding.GetEncoding(936)
        : Encoding.UTF8;
    var result = new DetectionResult(encoding, kind);
    Assert.Equal(key, result.LocKey);
}
```

先改测试、不改产品代码，跑 `dotnet test TextTool.Tests/TextTool.Tests.csproj -c Release --filter EncodingDetector`。预期：编译失败（无 `Kind` / 仍有 `DisplayName`）或断言失败。把 RED 输出写入报告。

- [ ] **步骤 2：改 DetectionResult 与检测路径**

按规格实现 `DetectedEncoding`、`DetectionResult`、`LocKey`。`DetectFromBytes` / `DetectStrict` 全部改用 `Kind`。删掉所有 `"UTF-8 (BOM)"` 等显示串字面量（测试文件除外，测试已改成枚举）。

- [ ] **步骤 3：三语键与 UI**

三份 JSON 在 `"EncodingNotSelected"` 后插入规格表中的五个键（JSON 合法、键名完全一致）。

`ReplaceTabControl`：增加 `private DetectionResult? _lastDetection;`。选中单文件成功检测后赋值并 `_lblReplaceEncoding.Text = Loc.T(detection.LocKey);`；失败或清空时 `_lastDetection = null`。`ApplyLocalization`：无检测用 `EncodingNotSelected`，有则 `Loc.T(_lastDetection.LocKey)`。多文件选中保持现有 `StatusFilesSelected`，不要把 `_lastDetection` 当成单文件编码显示。

`MergeTabControl` / `VNTabControl`：检测成功处改为 `Loc.T(_lastDetection.LocKey)`。`ApplyLocalization` 在 `_lastDetection != null` 时同样 `Loc.T(_lastDetection.LocKey)`，不要只在 null 时改标签。

- [ ] **步骤 4：门禁并提交**

跑全局约束里的三道门禁。

```
git add TextTool.Core/EncodingDetector.cs TextTool.Tests/Services/EncodingDetectorTests.cs \
        Localization/zh_CN.json Localization/zh_TW.json Localization/en_US.json \
        Controls/ReplaceTabControl.cs Controls/MergeTabControl.cs Controls/VNTabControl.cs
git commit -m "fix: 编码检测结果改枚举并由 Loc 显示" -m "Co-Authored-By: Cursor <noreply@cursor.com>"
```

### Task 2：D-2 PreviewForm 去掉 join/split 往返

**文件：**

- 修改：`Controls/PreviewForm.cs`

**接口：**

- `public PreviewForm(List<string> lines, string outputPath)` 直接持有 `lines` 并做 UI 初始化
- `public PreviewForm(string text, string outputPath)` 拆行后 `: this(list, outputPath)`
- 禁止 `List` 重载再调用 `string.Join` 再进 `string` 重载

- [ ] **步骤 1：确认 RED 不适用于本任务**

本任务无新测试（规格：不给 WinForms 窗体加测）。在报告里写明：验收是 List 重载源码不再出现 `string.Join(Environment.NewLine, lines)` 作为委托给另一重载的手段。

- [ ] **步骤 2：改构造**

把现有 `string` 重载体搬进 `List` 重载。`string` 重载改为：

```csharp
public PreviewForm(string text, string outputPath)
    : this(text.Split('\n').Select(l => l.TrimEnd('\r')).ToList(), outputPath)
{
}
```

`List` 重载：`_lines = lines;`（不要 `ToList()` 拷贝，调用方已交出列表），然后是原来的控件创建。`OnSave` 仍写 `_lines`。

- [ ] **步骤 3：门禁并提交**

```
git add Controls/PreviewForm.cs
git commit -m "refactor: PreviewForm 不再经 string 往返构造" -m "Co-Authored-By: Cursor <noreply@cursor.com>"
```

### Task 3：D-3 publish.ps1

**文件：**

- 创建：`scripts/publish.ps1`
- 创建：`TextTool.Tests/Scripts/PublishScriptTests.cs`
- 修改：`.gitattributes`（加 `*.ps1 text eol=lf`）
- 修改：`doc/UpdateSecurity.md`（发布流程改为先写脚本用法，手工步骤降为说明）

**接口：**

- 参数：`Version`（可选，缺省或非法都 exit 2，**不要**标 `Mandatory`）、`SkipUpload`、`Force`、`KeyPath`（可选）、`Repo`（可选，默认 `TwilightRainDev/TwilightRainTextTool`）
- 退出码：版本缺/非法 2；其它失败 1；成功 0

- [ ] **步骤 1：编写失败的测试**

`TextTool.Tests/Scripts/PublishScriptTests.cs`：

```csharp
namespace TextTool.Tests.Scripts;

public class PublishScriptTests
{
    private static string ScriptPath =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "scripts", "publish.ps1"));

    [Fact]
    public void Script_Exists()
    {
        Assert.True(File.Exists(ScriptPath), ScriptPath);
    }

    [Fact]
    public void InvalidVersion_Exits2()
    {
        var (code, stderr) = Invoke("-Version", "abc");
        Assert.Equal(2, code);
        Assert.Contains("版本", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingVersion_ExitsNonZero()
    {
        var (code, _) = Invoke();
        Assert.NotEqual(0, code);
    }

    [Fact]
    public void ScriptText_HasSafetyAndFlowMarkers()
    {
        var text = File.ReadAllText(ScriptPath);
        Assert.Contains("SkipUpload", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Force", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ReleaseSigner", text, StringComparison.Ordinal);
        Assert.Contains("verify", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GithubApiToken", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ghp_", text, StringComparison.Ordinal);
        Assert.DoesNotContain("BEGIN", text, StringComparison.Ordinal);
    }

    private static (int Code, string Stderr) Invoke(params string[] args)
    {
        var shell = File.Exists(@"C:\Program Files\PowerShell\7\pwsh.exe")
            ? @"C:\Program Files\PowerShell\7\pwsh.exe"
            : "powershell.exe";
        var arg_line = $"-NoProfile -ExecutionPolicy Bypass -File \"{ScriptPath}\"";
        if (args.Length > 0)
            arg_line += " " + string.Join(" ", args);
        var psi = new System.Diagnostics.ProcessStartInfo(shell, arg_line)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        using var proc = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException("无法启动 PowerShell");
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(30_000);
        return (proc.ExitCode, stderr);
    }
}
```

注意：`Invoke` 的局部变量必须是 `arg_line`（camel_case）。先提交测试再写脚本则 `Script_Exists` RED。

- [ ] **步骤 2：实现脚本**

`scripts/publish.ps1` 要点（PowerShell 5.1 可跑）：

```powershell
[CmdletBinding()]
param(
    [string]$Version,
    [switch]$SkipUpload,
    [switch]$Force,
    [string]$KeyPath = "E:\work_zone\ApiKey\TextTool-signing.priv.pem",
    [string]$Repo = "TwilightRainDev/TwilightRainTextTool"
)

$ErrorActionPreference = "Stop"
if ($Version -notmatch '^v?\d+\.\d+\.\d+$') {
    [Console]::Error.WriteLine("版本号格式应为 x.y.z 或 vx.y.z")
    exit 2
}
```

其后按规格：规范化 tag / raw、`gh release download`、检查 zip+sha256、build ReleaseSigner、sign、verify、按 `-SkipUpload`/`-Force` 决定是否 upload、打印检查清单。公钥路径：`(Split-Path $KeyPath) + "\TextTool-signing.pub.pem"`（若 KeyPath 文件名是 priv，同目录 pub）。

不要在脚本里读 `ApiKey\GithubApiToken.txt`。依赖已登录的 `gh`。

- [ ] **步骤 3：gitattributes 与 UpdateSecurity**

`.gitattributes` 在 `*.md` 那一组附近加 `*.ps1 text eol=lf`。

`doc/UpdateSecurity.md`「发布流程」开头改为：推荐 `.\scripts\publish.ps1 -Version <x.y.z> [-SkipUpload] [-Force]`，并保留原手工命令作对照。不要改密钥路径的事实陈述。

- [ ] **步骤 4：门禁并提交**

新文件用 LF。若 git 报 CRLF，先规范化再提交。

```
git add scripts/publish.ps1 TextTool.Tests/Scripts/PublishScriptTests.cs .gitattributes doc/UpdateSecurity.md
git commit -m "feat: 增加 publish.ps1 发布脚本" -m "Co-Authored-By: Cursor <noreply@cursor.com>"
```

### Task 4：D-4 ApplyTheme 类型分派测试

**文件：**

- 修改：`TextTool.csproj`（`InternalsVisibleTo`）
- 创建：`TextTool.Tests/Services/ControlsHelperThemeTests.cs`

**接口：**

- 不改 `ApplyTheme` 行为
- 禁止测试里调用 `ThemeManager.Toggle()`

- [ ] **步骤 1：编写失败的测试**

`TextTool.csproj` 先加（否则测不到 `internal`）：

```xml
  <ItemGroup>
    <InternalsVisibleTo Include="TextTool.Tests" />
  </ItemGroup>
```

放在 `ProjectReference` 那组附近即可。

然后写三个 `[Fact]`，方法名中文，按规格 §4 D-4：一个 `Panel` 上挂 Label+Button；一个挂 TextBox+ComboBox；一个挂 CheckBox+RadioButton。`ControlsHelper.ApplyTheme(root)` 后断言颜色等于 `ThemeManager` / `ControlsHelper` 当前值。

先写测试、在未加 `InternalsVisibleTo` 或未调用 `ApplyTheme` 时 RED（若你先加了 InternalsVisibleTo，RED 表现为断言失败——那就先写测试再确认实现已能绿；若实现已存在，本任务的 RED 是「测试文件尚无、套件尚未覆盖 ApplyTheme」：先添加测试并跑通，GREEN 即测试通过。不要为了制造 RED 去改坏 `ApplyTheme`。）

本任务允许「测试补齐已有行为」：GREEN = 新测试通过 + 全套不回归。在报告里写清：产品代码无行为 diff（除 csproj 的 InternalsVisibleTo）。

- [ ] **步骤 2：门禁并提交**

```
git add TextTool.csproj TextTool.Tests/Services/ControlsHelperThemeTests.cs
git commit -m "test: 覆盖 ApplyTheme 控件类型分派" -m "Co-Authored-By: Cursor <noreply@cursor.com>"
```

### Task 5：D-5 删除内联硬编码兜底

**文件：**

- 创建：`TextTool.Core/EmbeddedResource.cs`
- 修改：`TextTool.Core/ReplaceScheme.cs`、`TextTool.Core/VNCharacterScheme.cs`
- 修改：`TextTool.Tests/Services/DefaultSchemeTests.cs`

**接口：**

- `internal static class EmbeddedResource`，方法 `public static List<T> LoadJsonList<T>(string resourceName)`（规格原文）
- 删除 `GetHardcodedSchemes`（两处）
- `GetDefaultSchemes` 不再 `catch { }`

- [ ] **步骤 1：编写失败的测试**

`DefaultSchemeTests` 追加：

```csharp
    [Fact]
    public void EmbeddedResource_MissingName_Throws()
    {
        const string missing = "TextTool.does_not_exist.json";
        var ex = Assert.Throws<InvalidOperationException>(
            () => EmbeddedResource.LoadJsonList<object>(missing));
        Assert.Contains(missing, ex.Message);
        Assert.Contains("缺少嵌入式资源", ex.Message);
    }
```

更新现有两例 `GetDefaultSchemes` 里「若资源丢失会回退硬编码」的注释，改为「资源必须可解析」。跑测试：`EmbeddedResource` 不存在 → RED。

- [ ] **步骤 2：实现并删除兜底**

按规格加 `EmbeddedResource`。两个 store 的 `GetDefaultSchemes` 各一行 `return EmbeddedResource.LoadJsonList<...>(ResourceName);`。删掉整个 `GetHardcodedSchemes` 方法体。`ResourceName` 的 XML 注释去掉「静默回退到内联兜底」的表述，改成缺失即抛、对齐 `PinnedRoots`。

不要改 `default_schemes.json` / `default_vn_schemes.json` 内容。不要给 `GetDefaultSchemes` 加缓存。

- [ ] **步骤 3：门禁并提交**

```
git add TextTool.Core/EmbeddedResource.cs TextTool.Core/ReplaceScheme.cs \
        TextTool.Core/VNCharacterScheme.cs TextTool.Tests/Services/DefaultSchemeTests.cs
git commit -m "refactor: 删除内置方案的内联兜底" -m "Co-Authored-By: Cursor <noreply@cursor.com>"
```

### Task 6：文档、债表与版本 2.6.1

**文件：**

- 修改：`Directory.Build.props`
- 修改：`README.md`
- 修改：`doc/ArchitectureGuide.md`（若结构树提到版本或 `scripts/`，补 `scripts/publish.ps1` 与 `EmbeddedResource.cs`）
- 修改：`doc/TECH-DEBT.md`

**接口：** 无代码接口。

- [ ] **步骤 1：版本**

`Directory.Build.props`：`<Version>2.6.0</Version>` → `<Version>2.6.1</Version>`。注释里的 `2.6.0.0` 改为 `2.6.1.0`。

- [ ] **步骤 2：README**

中英结构树里的 `v2.6.0` / `(2.6.0)` 改为 `2.6.1`。英文树补 `scripts/publish.ps1`；中文树同样。不要改「最新 tag v2.4.4」这类尚未打新 tag 的句子。不要打 tag。

- [ ] **步骤 3：TECH-DEBT**

§1 的 D-1 到 D-5 整段移出「未落地」，在 §3「已核实落地」表各加一行，证据写本轮落地的类型/键/路径（短）。§4 锚点：

- 版本期望改为 `2.6.1`
- 测试数、`MainForm` 行数改成任务提交前实测值（跑规格里那组命令，不要沿用 188 / 214）
- **删除**「全仓无 PowerShell 脚本」那条锚点，改为 `test -f scripts/publish.ps1`（或等价）期望存在
- `Services/` 文件数若未变则保持

§5 lint 遗留不动。

- [ ] **步骤 4：ArchitectureGuide**

结构树 `TextTool.Core/` 补 `EmbeddedResource.cs`；仓库根补 `scripts/publish.ps1`。不要改页签数量。

- [ ] **步骤 5：门禁并提交**

```
git add Directory.Build.props README.md doc/ArchitectureGuide.md doc/TECH-DEBT.md
git commit -m "docs: 债 D-1–D-5 收口与版本升至 2.6.1" -m "Co-Authored-By: Cursor <noreply@cursor.com>"
```

## 决策文档

- Core 只带 `Kind`/`LocKey`，不引用 `Loc`：避免 Core 依赖 WinForms 本地化。
- 删 `DisplayName` 而不是并存：避免 `DetectStrict` 继续吃魔串。
- D-4 不断言绝对 RGB：跟 `ThemeManager` 当前值比，避免测试写配置。
- D-3 测试只打版本校验与脚本正文：真网发布留给人。
- D-5 抽出 `EmbeddedResource`：才能在不拆掉真资源的情况下测「资源缺」。
- 版本用补丁号 `2.6.1`：没有新产品能力。

## 测试决策

- D-1：改现有编码测试 + `LocKey` Theory。不测三个页签的点击。
- D-2：无新测试。
- D-3：进程起 PowerShell，非法版本 exit 2。
- D-4：三例类型分派。
- D-5：缺资源名抛错；保留原有「资源在」四例。
- 先例：`EncodingDetectorTests`、`DefaultSchemeTests`（xUnit、中文方法名、`GlobalUsings`）。

## 范围之外

- lint 契约与 §5 遗留、A1/A2/D7、打 tag、发 Release、私钥加密、`GetDefaultSchemes` 缓存。

## 进一步说明

- 实施在 `feat/tech-debt-d1-d5`。
- 工作区导航 `Docs\未来计划.md` / `Docs\上手-工作区全景.md` 由控制器改，不进本仓提交。
