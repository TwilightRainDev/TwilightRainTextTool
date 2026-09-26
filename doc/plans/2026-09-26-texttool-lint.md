# TextTool 中文 AI 味检查（lint）实施计划

> **对于执行者：** 必需子技能：使用 SubagentDrivenDev（推荐）或 ExecutingPlans 逐任务实施此计划。步骤使用复选框（`- [ ]`）语法进行跟踪。
> 规格来源：`doc/specs/2026-09-26-texttool-lint-design.md`（已批准）。本计划与规格冲突时以规格为准，并把冲突回报给用户。

**目标：** 给 `texttool` 加一个只报不改的中文 AI 味检查子命令，规则数据化（JSON），输出稳定 JSON 契约供 Claude 与 HumanizerZh 技能消费；技能侧 `tone_lint.py` 退休。

**架构：** 词表/句式类规则全部落 `default_lint_rules.json`（嵌入式资源，外部 `lint_rules.json` 按 `Id` 覆盖合并）；统计/段落类规则是 `AiToneLintService` 里的算法检出器。引擎产出 `LintReport`（`Hits` 计退出码，`Notes` 不计），CLI 负责参数、渲染与退出码。先做 R0——修掉两处写错的嵌入式资源名，因为本模块复用同一机制。

**技术栈：** .NET 8、C#、System.Text.Json、xUnit、`RegexGuard`（带超时的正则构造）、`JsonFileStore`（原子写 + 损坏即抛）。

## 问题陈述

工具现有能力全是改写：合并、标点修复、字面替换、VN 排版。想检查一段中文有没有 AI 味，只能去跑技能侧的 `tone_lint.py`——Python 脚本，无测试、无 CI、规则写死在代码里，改一个词要改 `.py`，且与工具是两套代码。

同时，去 AI 味的方法论主体（cc-switch 快照 `humanizer-zh` 的 24 条模式）停留在文档里，没有被任何可执行物消费；技能与工具各自维护一份重叠的标点方案（108 条里 74 条与内置重复）。

## 解决方案

`texttool lint` 读文件或 stdin，按数据化规则集报出命中位置与类别，`--json` 输出结构化契约，退出码可当 CI 关卡。改词只改 JSON；规则集随工具走，有测试有 CI。

## 全局约束

- 目标框架：`net8.0`（Core / CLI / Tests）。**本计划不碰 GUI 项目**（`TextTool.csproj`、`Controls/`、`Services/` 一律不动）。
- 行尾 LF、无 BOM（`.gitattributes` 强制）；**禁用 emoji**（工作区约定，用 `[OK]`/`[FAIL]`/`>` 等纯文本标记）。
- 命名与风格以现有 Core 文件为准：file-scoped namespace（`namespace TextTool.Services;`）、私有字段 `_camelCase`、`PascalCase.cs` 文件名、XML 文档注释写中文。
- **所有来自 JSON 的正则必须经 `RegexGuard.Create` 构造**（2s 超时，防 ReDoS），不得直接 `new Regex(...)`。
- 质量门禁（每个任务提交前跑）：`dotnet build TextTool.sln -c Release -warnaserror`、`dotnet test TextTool.Tests/TextTool.Tests.csproj -c Release`、`dotnet format TextTool.sln --verify-no-changes`。
- 提交信息中文，前缀 `feat:` / `fix:` / `docs:` / `test:`；**署名沿用本仓既有惯例**（`git log -1 --format=%an <%ae>` 当前为 `TwilightRainDev <122437146+TwilightRainDev@users.noreply.github.com>`），不要用本机其它身份顶替。
- 版本号只在任务 10 改：`Directory.Build.props` 的 `2.4.4` → `2.5.0`。

## 任务

### Task 1：修正嵌入式资源名，并让测试能真正失败

**文件：**

- 修改：`TextTool.Core/ReplaceScheme.cs:41`
- 修改：`TextTool.Core/VNCharacterScheme.cs:47`
- 修改：`TextTool.Core/TextTool.Core.csproj`（加 `InternalsVisibleTo`）
- 修改：`TextTool.Tests/Services/DefaultSchemeTests.cs`

**接口：**

- 消费：无
- 产出：两个 store 各暴露 `internal const string ResourceName`（值以 `TextTool.` 为前缀）；`DefaultSchemeTests` 的两个测试**引用该常量**并断言其可解析

背景：已构建程序集里的真实资源名是 `TextTool.default_schemes.json` / `TextTool.default_vn_schemes.json`（前缀取 `RootNamespace`，不是程序集名）。现有代码查 `TextTool.Core.*`，查不到就静默走内联兜底——两份数据逐条相同，所以无行为差异，但两个 JSON 是死文件。`TextTool.Core/PinnedRoots.cs:19` 是正确范式。

> TDD 顺序说明：测试必须**引用 store 上的常量**才能与生产代码耦合。测试里硬编码字面资源名**测不到本缺陷**——资源存在与否由 csproj 决定，与 store 的查询串解耦，那样的测试改前改后都通过。因此这里的 RED 靠"常量先填错值"取得。

- [ ] **步骤 1：加常量与可见性，常量先填旧错值，并写引用常量的测试**

`TextTool.Core/TextTool.Core.csproj` 的 `ItemGroup` 内加：

```xml
    <InternalsVisibleTo Include="TextTool.Tests" />
```

`TextTool.Core/ReplaceScheme.cs` 在类内加常量（**故意先填旧错值**，步骤 3 改回）：

```csharp
    /// <summary>
    /// 嵌入式资源名。前缀取 RootNamespace（TextTool），不是程序集名（TextTool.Core）
    /// ——这里写错过一次，导致查不到资源、静默回落到内联硬编码。
    /// </summary>
    internal const string ResourceName = "TextTool.Core.default_schemes.json";
```

`TextTool.Core/VNCharacterScheme.cs` 同法加 `internal const string ResourceName = "TextTool.Core.default_vn_schemes.json";`。

`TextTool.Tests/Services/DefaultSchemeTests.cs` 新增（测试引用常量，不写字面串）：

```csharp
    [Fact]
    public void ReplaceSchemeStore_ResourceName_ResolvesToEmbeddedResource()
    {
        var stream = typeof(ReplaceSchemeStore).Assembly
            .GetManifestResourceStream(ReplaceSchemeStore.ResourceName);

        Assert.NotNull(stream);
    }

    [Fact]
    public void VNCharacterSchemeStore_ResourceName_ResolvesToEmbeddedResource()
    {
        var stream = typeof(VNCharacterSchemeStore).Assembly
            .GetManifestResourceStream(VNCharacterSchemeStore.ResourceName);

        Assert.NotNull(stream);
    }
```

- [ ] **步骤 2：运行测试以确认失败**

运行：`dotnet test TextTool.Tests/TextTool.Tests.csproj -c Release --filter "ResourceName"`
预期：FAIL，两个测试均报 `Assert.NotNull() Failure`（常量是错值时解析不到资源）

- [ ] **步骤 3：把常量改回正确值，并让 store 用常量取资源**

两个常量改为 `TextTool.default_schemes.json` / `TextTool.default_vn_schemes.json`；两处 `GetManifestResourceStream(字面串)` 改为 `GetManifestResourceStream(ResourceName)`。

- [ ] **步骤 4：运行测试以确认通过**

运行：`dotnet test TextTool.Tests/TextTool.Tests.csproj -c Release --filter "ResourceName"`
预期：PASS（2 个）

- [ ] **步骤 5：跑全量门禁并提交**

```bash
dotnet build TextTool.sln -c Release -warnaserror
dotnet test TextTool.Tests/TextTool.Tests.csproj -c Release
dotnet format TextTool.sln --verify-no-changes
git add TextTool.Core/ReplaceScheme.cs TextTool.Core/VNCharacterScheme.cs \
        TextTool.Core/TextTool.Core.csproj TextTool.Tests/Services/DefaultSchemeTests.cs
git commit -m "fix: 修正嵌入式资源名前缀并提为 store 常量，测试改为引用常量"
```

> 执行记录：本任务实际分两次提交完成（先按初版 brief 修资源名，再按实测结论改造为常量方案）。本节文字已按最终落地形态改写——初版把"测试里直查字面资源名"当作 RED，实施实测证伪了该预期（那种测试与 store 查询串解耦，改前改后都通过）。

### Task 2：规则模型与存储

**文件：**

- 创建：`TextTool.Core/LintRule.cs`
- 测试：`TextTool.Tests/Services/LintRuleStoreTests.cs`

**接口：**

- 消费：`RegexGuard.Create(string, RegexOptions)`、`JsonFileStore.Load<T>(string)`
- 产出：
  - `public class LintRule`，属性：`string Id`、`string Group`、`string Title`、`string Kind`、`List<string> Patterns`、`string Scope`、`int? TailChars`、`int MinCount`、`string Severity`、`string Detail`、`List<string>? Hints`、`string? SuggestScheme`
  - `public static class LintRuleStore`：`internal const string EmbeddedResourceName`（与 `ReplaceSchemeStore.ResourceName` 的可见性一致）、`List<LintRule> Load()`（内置与外部各自先校验、再合并、再整体校验）、`List<LintRule> GetDefaultRules()`、`internal static List<LintRule> Merge(List<LintRule> builtIn, List<LintRule> external)`、`internal static void Validate(List<LintRule> rules)`

- [ ] **步骤 1：编写失败的测试**

```csharp
namespace TextTool.Tests.Services;

public class LintRuleStoreTests
{
    private static LintRule Rule(string id, string pattern = "测试") => new()
    {
        Id = id, Group = "L", Title = "标题", Kind = "literal",
        Patterns = new List<string> { pattern }, Severity = "info",
    };

    [Fact]
    public void Merge_外部规则按Id覆盖内置_其余保留()
    {
        var builtIn = new List<LintRule> { Rule("L1"), Rule("L2") };
        var external = new List<LintRule> { Rule("L2", "覆盖"), Rule("L9") };

        var merged = LintRuleStore.Merge(builtIn, external);

        Assert.Equal(3, merged.Count);
        Assert.Equal(new[] { "L1", "L2", "L9" }, merged.Select(r => r.Id));
        Assert.Equal("覆盖", merged[1].Patterns[0]);   // 覆盖发生在原位置
    }

    [Fact]
    public void Validate_Id为空即抛()
    {
        var rule = Rule("L1");
        rule.Id = "";

        Assert.Throws<ArgumentException>(() => LintRuleStore.Validate(new List<LintRule> { rule }));
    }

    [Fact]
    public void Validate_Id重复即抛()
    {
        var rules = new List<LintRule> { Rule("L1"), Rule("L1") };

        Assert.Throws<ArgumentException>(() => LintRuleStore.Validate(rules));
    }

    [Fact]
    public void Validate_正则语法错即抛()
    {
        var rule = Rule("P1", "([a-z]");
        rule.Kind = "regex";

        var ex = Assert.Throws<ArgumentException>(() => LintRuleStore.Validate(new List<LintRule> { rule }));
        Assert.Contains("P1", ex.Message);
    }

    [Fact]
    public void Validate_Hints长度与Patterns不匹配即抛()
    {
        var rule = Rule("L7");
        rule.Patterns.Add("第二条");
        rule.Hints = new List<string> { "只有一条" };

        Assert.Throws<ArgumentException>(() => LintRuleStore.Validate(new List<LintRule> { rule }));
    }

    [Fact]
    public void Validate_Patterns为null即抛而非NRE()
    {
        var rule = Rule("L1");
        rule.Patterns = null!;   // JSON 里的 "Patterns": null 会走到这里

        var ex = Assert.Throws<ArgumentException>(() => LintRuleStore.Validate(new List<LintRule> { rule }));
        Assert.Contains("Patterns", ex.Message);
    }

    [Fact]
    public void Validate_Patterns含空串即抛()
    {
        var rule = Rule("L1", "");   // 空串模式会匹配任意位置，是配置错误

        Assert.Throws<ArgumentException>(() => LintRuleStore.Validate(new List<LintRule> { rule }));
    }
}
```

- [ ] **步骤 2：运行测试以确认失败**

运行：`dotnet test TextTool.Tests/TextTool.Tests.csproj -c Release --filter "LintRuleStoreTests"`
预期：FAIL，提示 `LintRule` / `LintRuleStore` 不存在

- [ ] **步骤 3：编写最小实现**

`TextTool.Core/LintRule.cs`：

```csharp
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace TextTool.Services;

/// <summary>
/// AI 味检查规则（数据驱动部分）。算法规则不进本模型，见 AiToneLintService。
/// </summary>
public class LintRule
{
    public string Id { get; set; } = "";
    public string Group { get; set; } = "";
    public string Title { get; set; } = "";
    /// <summary>literal 或 regex。</summary>
    public string Kind { get; set; } = "regex";
    /// <summary>多条模式——P1 有 2 条正则、L1 有 26 条词，单串装不下。</summary>
    public List<string> Patterns { get; set; } = new();
    /// <summary>document 或 paragraph。</summary>
    public string Scope { get; set; } = "document";
    /// <summary>仅 Scope=paragraph 有效：只检查段末 N 字。</summary>
    public int? TailChars { get; set; }
    /// <summary>每条 Pattern 各自的出现次数达到本值才报。</summary>
    public int MinCount { get; set; } = 1;
    /// <summary>info 或 warn。</summary>
    public string Severity { get; set; } = "info";
    public string Detail { get; set; } = "";
    /// <summary>与 Patterns 下标对齐的建议改法（可空）。</summary>
    public List<string>? Hints { get; set; }
    public string? SuggestScheme { get; set; }
}

/// <summary>
/// 规则持久化：内置嵌入式资源 + 外部 lint_rules.json 按 Id 合并。
/// </summary>
public static class LintRuleStore
{
    /// <summary>资源名前缀取 RootNamespace（TextTool），不是程序集名（TextTool.Core）。</summary>
    internal const string EmbeddedResourceName = "TextTool.default_lint_rules.json";

    public static List<LintRule> Load()
    {
        var builtIn = GetDefaultRules();
        var external = JsonFileStore.Load<LintRule>("lint_rules.json");
        // 两侧各自先校验，再合并，再校验合并结果。三个边界各有理由：
        // 内置侧——Merge 会 Clone，Patterns 为 null 时 Clone 先 NRE；
        // 外部侧——重复 Id 必须在这里得到清晰报错，否则 Merge 内部索引指向已被替换的
        //   对象、IndexOf 返回 -1，先抛 ArgumentOutOfRangeException，Validate 轮不到。
        Validate(builtIn);
        Validate(external);
        var merged = Merge(builtIn, external);
        Validate(merged);
        return merged;
    }

    public static List<LintRule> GetDefaultRules()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(EmbeddedResourceName)
            ?? throw new InvalidOperationException($"缺少嵌入式资源 {EmbeddedResourceName}，请检查 csproj 的 EmbeddedResource 配置");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return JsonSerializer.Deserialize<List<LintRule>>(reader.ReadToEnd()) ?? new List<LintRule>();
    }

    /// <summary>外部规则按 Id 覆盖内置（覆盖后仍排原位置），新 Id 追加在末尾。</summary>
    internal static List<LintRule> Merge(List<LintRule> builtIn, List<LintRule> external)
    {
        var result = builtIn.Select(Clone).ToList();
        var index = result.ToDictionary(r => r.Id, StringComparer.Ordinal);
        foreach (var rule in external)
        {
            if (index.TryGetValue(rule.Id, out var existing))
                result[result.IndexOf(existing)] = rule;
            else
            {
                result.Add(rule);
                index[rule.Id] = rule;
            }
        }
        return result;
    }

    internal static void Validate(List<LintRule> rules)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Id))
                throw new ArgumentException("规则 Id 不能为空");
            if (!seen.Add(rule.Id))
                throw new ArgumentException($"规则 Id 重复：{rule.Id}");
            if (rule.Kind is not ("literal" or "regex"))
                throw new ArgumentException($"规则 {rule.Id} 的 Kind 非法：{rule.Kind}");
            if (rule.Scope is not ("document" or "paragraph"))
                throw new ArgumentException($"规则 {rule.Id} 的 Scope 非法：{rule.Scope}");
            if (rule.Severity is not ("info" or "warn"))
                throw new ArgumentException($"规则 {rule.Id} 的 Severity 非法：{rule.Severity}");
            // 显式 null 会被 System.Text.Json 写回属性，模型上的 = new() 默认值挡不住，
            // 不在这里拦就会变成 NullReferenceException 而非清晰的校验报错
            if (rule.Patterns is null || rule.Patterns.Count == 0)
                throw new ArgumentException($"规则 {rule.Id} 的 Patterns 为空");
            if (rule.Patterns.Any(string.IsNullOrEmpty))
                throw new ArgumentException($"规则 {rule.Id} 的 Patterns 含空串");
            if (rule.MinCount < 1)
                throw new ArgumentException($"规则 {rule.Id} 的 MinCount 必须 >= 1");
            if (rule.TailChars is <= 0)
                throw new ArgumentException($"规则 {rule.Id} 的 TailChars 必须为正整数");
            if (rule.TailChars is not null && rule.Scope != "paragraph")
                throw new ArgumentException($"规则 {rule.Id} 的 TailChars 仅在 Scope=paragraph 时有效");
            if (rule.Hints is not null && rule.Hints.Count != rule.Patterns.Count)
                throw new ArgumentException($"规则 {rule.Id} 的 Hints 长度与 Patterns 不一致");
            if (rule.Kind == "regex")
                foreach (var pattern in rule.Patterns)
                    try { RegexGuard.Create(pattern); }
                    catch (ArgumentException ex)
                    {
                        throw new ArgumentException($"规则 {rule.Id} 的正则非法：{pattern}（{ex.Message}）", ex);
                    }
        }
    }

    private static LintRule Clone(LintRule r) => new()
    {
        Id = r.Id, Group = r.Group, Title = r.Title, Kind = r.Kind,
        Patterns = new List<string>(r.Patterns), Scope = r.Scope,
        TailChars = r.TailChars, MinCount = r.MinCount, Severity = r.Severity,
        Detail = r.Detail, Hints = r.Hints is null ? null : new List<string>(r.Hints),
        SuggestScheme = r.SuggestScheme,
    };
}
```

- [ ] **步骤 4：运行测试以确认通过**

运行：`dotnet test TextTool.Tests/TextTool.Tests.csproj -c Release --filter "LintRuleStoreTests"`
预期：PASS（7 个）

> 注意：断言"内置规则集合法"的测试放在任务 3——本任务提交时 pre-commit 会跑**全量测试**，任何失败都会挡住提交，所以本任务不得留下未通过的测试。

- [ ] **步骤 5：提交**

```bash
git add TextTool.Core/LintRule.cs TextTool.Tests/Services/LintRuleStoreTests.cs
git commit -m "feat: 新增 AI 味检查规则模型与存储（内置 + 外部按 Id 合并）"
```

### Task 3：规则数据 `default_lint_rules.json`

**文件：**

- 创建：`TextTool.Core/default_lint_rules.json`
- 修改：`TextTool.Core/TextTool.Core.csproj`
- 测试：`TextTool.Tests/Services/LintRuleStoreTests.cs`（补一条数据规模断言）

**接口：**

- 消费：`LintRule` 的字段名（任务 2）
- 产出：19 条数据规则的完整定义

JSON 里写 `\\uXXXX`（JSON 转义后成为正则的 `\uXXXX`）；含 CJK 的字符类一律用 `\u3400-\u4DBF\u4E00-\u9FFF\uF900-\uFAFF\u3000-\u303F\uFF00-\uFFEF`，不要嵌真实汉字——避免文件编码歧义。字段名 PascalCase（`System.Text.Json` 默认区分大小写，写 camelCase 会静默绑不上）。

- [ ] **步骤 1：编写失败的测试**

```csharp
    [Fact]
    public void GetDefaultRules_数据规则19条且顺序完整()
    {
        var ids = LintRuleStore.GetDefaultRules().Select(r => r.Id).ToArray();

        Assert.Equal(
            new[] { "P1", "P2", "P3", "P6", "P7", "P8",
                    "L1", "L2", "L3", "L4", "L5", "L6", "L7",
                    "S1", "S2", "S3", "S4", "S5", "S6" },
            ids);
    }

    [Fact]
    public void Load_内置规则集可加载且通过校验()
    {
        var rules = LintRuleStore.Load();

        Assert.Equal(19, rules.Count);
        Assert.All(rules, r =>
        {
            Assert.False(string.IsNullOrWhiteSpace(r.Title));
            Assert.False(string.IsNullOrWhiteSpace(r.Detail));
        });
    }

    [Fact]
    public void GetDefaultRules_literal规则的Patterns不含反斜杠()
    {
        // literal 走 Regex.Escape：JSON 里写的 \uXXXX 解码后是六个普通字符，
        // 被 Escape 再转义一次后永远匹配不到真实字符——这类规则必须写成 regex
        var offenders = LintRuleStore.GetDefaultRules()
            .Where(r => r.Kind == "literal")
            .SelectMany(r => r.Patterns.Select(p => (r.Id, Pattern: p)))
            .Where(x => x.Pattern.Contains('\\'))
            .Select(x => x.Id)
            .ToList();

        Assert.Empty(offenders);
    }
```

- [ ] **步骤 2：运行测试以确认失败**

运行：`dotnet test TextTool.Tests/TextTool.Tests.csproj -c Release --filter "数据规则19条"`
预期：FAIL，提示缺少嵌入式资源或数量不符

- [ ] **步骤 3：写数据文件并登记资源**

`TextTool.Core/TextTool.Core.csproj` 的 `ItemGroup` 内追加一行（与既有三行并列）：

```xml
    <EmbeddedResource Include="default_lint_rules.json" />
```

`TextTool.Core/default_lint_rules.json`（19 条，内容照抄）：

```json
[
  { "Id": "P1", "Group": "P", "Title": "重复标点", "Kind": "regex",
    "Patterns": ["([。，、？！；：～])\\1+", "([!?])\\1+"],
    "Severity": "warn", "Detail": "连续重复的标点",
    "SuggestScheme": "重复矛盾标点整理" },

  { "Id": "P2", "Group": "P", "Title": "省略号风格", "Kind": "regex",
    "Patterns": ["\\.{3,}", "。{3,}", "(?<!…)…(?!…)"],
    "Severity": "warn", "Detail": "非标准省略号写法（标准为 ……）",
    "SuggestScheme": "去AI味标点归一" },

  { "Id": "P3", "Group": "P", "Title": "全半角混用", "Kind": "regex",
    "Patterns": [
      "[,;:?!()\"'][\\u3400-\\u4DBF\\u4E00-\\u9FFF\\uF900-\\uFAFF\\u3000-\\u303F\\uFF00-\\uFFEF]",
      "[\\u3400-\\u4DBF\\u4E00-\\u9FFF\\uF900-\\uFAFF\\u3000-\\u303F\\uFF00-\\uFFEF][,;:?!()\"']"
    ],
    "Severity": "warn", "Detail": "半角标点与中文相邻",
    "SuggestScheme": "去AI味标点归一" },

  { "Id": "P6", "Group": "P", "Title": "破折号与波浪号", "Kind": "regex",
    "Patterns": ["--+", "(?<!—)—(?!—)",
      "[\\u3400-\\u4DBF\\u4E00-\\u9FFF] ?- ?[\\u3400-\\u4DBF\\u4E00-\\u9FFF]", "~{2,}|[～]"],
    "Severity": "info", "Detail": "破折号/波浪号写法不规范",
    "SuggestScheme": "去AI味标点归一" },

  { "Id": "P7", "Group": "P", "Title": "隐形字符", "Kind": "regex",
    "Patterns": ["\\u200B", "\\u200C", "\\u200D", "\\u200E", "\\u200F", "\\u2060", "\\u00AD", "\\u00A0"],
    "Severity": "warn", "Detail": "隐形字符（零宽/软连字符/不换行空格）",
    "SuggestScheme": "去AI味标点归一" },

  { "Id": "P8", "Group": "P", "Title": "表情与装饰符号", "Kind": "regex",
    "Patterns": ["[\\uFE0F]", "\\uD83C[\\uDF00-\\uDFFF]", "\\uD83D[\\uDC00-\\uDEFF]",
      "\\uD83E[\\uDD00-\\uDDFF]", "[\\u2600-\\u27BF\\u2B00-\\u2BFF]"],
    "Severity": "info", "Detail": "表情符号或装饰性符号",
    "SuggestScheme": "特殊符号清除" },

  { "Id": "L1", "Group": "L", "Title": "套话词", "Kind": "regex",
    "Patterns": ["赋能", "抓手", "闭环", "生态位", "护城河", "降本增效", "提质增效",
      "不可否认", "毋庸置疑", "值得注意的是", "众所周知", "在当今社会",
      "随着.{0,10}的发展", "极大地", "深入地", "全方位", "多维度", "深层次",
      "带来.{0,6}的.{0,6}体验", "具有重要意义", "发挥着重要作用", "起到了关键作用",
      "在.{0,10}的背景下", "从某种意义上说", "换句话说", "简而言之"],
    "Severity": "info", "Detail": "套话/空词" },

  { "Id": "L2", "Group": "L", "Title": "AI 高频词", "Kind": "literal", "MinCount": 3,
    "Patterns": ["此外", "至关重要", "深入探讨", "持久的", "增强", "培养", "相互作用",
      "复杂性", "格局", "织锦", "宝贵的", "充满活力的", "获得", "展示", "与……保持一致"],
    "Severity": "info", "Detail": "AI 高频词密度偏高" },

  { "Id": "L3", "Group": "L", "Title": "模糊归因", "Kind": "regex",
    "Patterns": ["行业报告显示", "观察者指出", "专家认为", "一些批评者认为", "多个来源",
      "有观点认为", "业内人士指出"],
    "Severity": "warn", "Detail": "观点归因于模糊来源" },

  { "Id": "L4", "Group": "L", "Title": "知识截止免责", "Kind": "regex",
    "Patterns": ["截至\\s*\\d{4}\\s*年", "根据我最后的训练更新", "根据我的训练数据",
      "虽然具体细节(?:有限|稀缺)", "基于(?:我)?(?:现有|可用)的?信息", "我的知识截止"],
    "Severity": "warn", "Detail": "模型免责声明残留" },

  { "Id": "L5", "Group": "L", "Title": "协作交流痕迹", "Kind": "regex",
    "Patterns": ["希望这对您有(?:所)?帮助", "当然！", "一定！", "您说得(?:完全)?正确",
      "请(?:随时)?告诉我", "这是一个很好的?问题"],
    "Severity": "warn", "Detail": "聊天对话痕迹残留" },

  { "Id": "L6", "Group": "L", "Title": "谄媚语气", "Kind": "regex",
    "Patterns": ["好问题[！!]", "您说得完全正确", "这是一个(?:很好的|非常棒的)观点",
      "非常抱歉", "感谢您的耐心"],
    "Severity": "warn", "Detail": "过度讨好或客套" },

  { "Id": "L7", "Group": "L", "Title": "填充短语", "Kind": "literal",
    "Patterns": ["为了实现这一目标", "由于下雨的事实", "在这个时间点",
      "在您需要帮助的情况下", "系统具有处理的能力", "值得注意的是数据显示"],
    "Hints": ["为了实现这一点", "因为下雨", "现在", "如果您需要帮助",
      "系统可以处理", "数据显示"],
    "Severity": "info", "Detail": "填充短语，可直接替换" },

  { "Id": "S1", "Group": "S", "Title": "对举句式", "Kind": "regex",
    "Patterns": ["不是[^。！？\\n]{1,25}?而是", "并非[^。！？\\n]{1,25}?而是",
      "与其说[^。！？\\n]{1,25}?不如说", "不只是[^。！？\\n]{1,25}?更是",
      "不在于[^。！？\\n]{1,25}?而在于", "既[^。！？\\n]{1,15}?又[^。！？\\n]{1,15}?又"],
    "Severity": "warn", "Detail": "对举句式（AI 骨架）" },

  { "Id": "S2", "Group": "S", "Title": "段内序数词", "Kind": "regex",
    "Patterns": ["首先|其次|再次|最后|其一|其二|其三"],
    "Scope": "paragraph", "MinCount": 2,
    "Severity": "warn", "Detail": "段内序数词骨架" },

  { "Id": "S3", "Group": "S", "Title": "结尾升华", "Kind": "regex",
    "Patterns": ["不仅仅", "总而言之", "总之", "综上", "由此可见", "值得我们", "让我们",
      "这正是", "在这个.{0,12}的时代", "不仅.{0,10}更是", "未来可期", "值得期待",
      "方能", "唯有", "方得", "行稳致远"],
    "Scope": "paragraph", "TailChars": 30,
    "Severity": "warn", "Detail": "段末拔高收尾" },

  { "Id": "S4", "Group": "S", "Title": "定语堆叠", "Kind": "regex",
    "Patterns": ["的[^。，、\\n]{0,8}的[^。，、\\n]{0,8}的"],
    "Severity": "info", "Detail": "短距离内三个「的」" },

  { "Id": "S5", "Group": "S", "Title": "系动词回避", "Kind": "regex",
    "Patterns": ["作为.{0,10}的?(?:象征|标志|证明|体现|代表)", "代表着.{0,10}",
      "标志着.{0,10}", "充当着?.{0,10}"],
    "Severity": "info", "Detail": "用复杂结构替代「是/有」" },

  { "Id": "S6", "Group": "S", "Title": "内联标题列表", "Kind": "regex",
    "Patterns": ["(?m)^[-*]\\s+\\*\\*[^*\\n]{1,20}\\*\\*[：:]"],
    "Severity": "info", "Detail": "粗体标题 + 冒号的清单项" }
]
```

> **P7 为什么是 `regex` 而不是 `literal`**：`literal` 走 `Regex.Escape`，JSON 里写的 `\\u200B` 解码后是六个普通字符（反斜杠 u 2 0 0 B），再被 `Regex.Escape` 转义一次，于是去匹配文本里字面的 `​` 序列——真实文本里的 U+200B 永远匹配不到，规则静默失效。写成 `regex` 则由正则引擎把 `​` 解释为真字符，且文件里仍看得见转义（比在 JSON 里嵌真实不可见字符可靠得多）。`L2`/`L7` 是纯中文文本、不含反斜杠，用 `literal` 正确。下面那条不变量测试负责拦住这类写法。

- [ ] **步骤 4：运行测试以确认通过**

运行：`dotnet test TextTool.Tests/TextTool.Tests.csproj -c Release --filter "LintRuleStoreTests"`
预期：PASS（10 个：任务 2 的 7 个 + 本任务的 3 个）

- [ ] **步骤 5：提交**

```bash
git add TextTool.Core/default_lint_rules.json TextTool.Core/TextTool.Core.csproj TextTool.Tests/Services/LintRuleStoreTests.cs
git commit -m "feat: 新增 19 条 AI 味检查数据规则（P/L/S 三组）"
```

### Task 4：报告模型与 JSON 契约

**文件：**

- 创建：`TextTool.Core/LintReport.cs`
- 测试：`TextTool.Tests/Services/LintReportTests.cs`

**接口：**

- 消费：无
- 产出：
  - `public sealed class LintHit`：`string Id`、`string Group`、`string Title`、`string Severity`、`int Line`、`int Col`、`int Length`、`string Match`、`string Snippet`、`string Detail`、`string? Hint`、`string? SuggestScheme`
  - `public sealed class LintNote`：`string Id`、`string Text`
  - `public sealed class LintReport`：`string File`、`int Chars`、`List<LintHit> Hits`、`List<LintNote> Notes`、`LintReport Filter(IReadOnlyCollection<string>? onlyIds, string? minSeverity)`
  - `public sealed class LintReportSet`：`int Version`（固定 1）、`List<LintReport> Reports`、`string ToJson()`

- [ ] **步骤 1：编写失败的测试**

```csharp
namespace TextTool.Tests.Services;

public class LintReportTests
{
    private static LintReport Sample() => new()
    {
        File = "a.md", Chars = 10,
        Hits = new List<LintHit>
        {
            new() { Id = "S1", Severity = "warn", Line = 1, Col = 1, Match = "不是A而是B" },
            new() { Id = "L1", Severity = "info", Line = 2, Col = 3, Match = "赋能" },
        },
        Notes = new List<LintNote> { new() { Id = "C1", Text = "段落过于均一" } },
    };

    [Fact]
    public void Filter_按严重度保留warn及以上_统计项不受影响()
    {
        var filtered = Sample().Filter(null, "warn");

        Assert.Single(filtered.Hits);
        Assert.Equal("S1", filtered.Hits[0].Id);
        Assert.Single(filtered.Notes);
    }

    [Fact]
    public void Filter_按Id过滤_大小写不敏感()
    {
        var filtered = Sample().Filter(new[] { "l1" }, null);

        Assert.Single(filtered.Hits);
        Assert.Equal("L1", filtered.Hits[0].Id);
    }

    [Fact]
    public void ToJson_键为PascalCase且中文不转义()
    {
        var json = new LintReportSet { Reports = new List<LintReport> { Sample() } }.ToJson();

        Assert.Contains("\"Version\": 1", json);
        Assert.Contains("\"Reports\"", json);
        Assert.Contains("\"Match\": \"赋能\"", json);
        Assert.DoesNotContain("\\u8D4B", json);   // 中文未被转义成 \uXXXX
    }

    [Fact]
    public void ToJson_命中隐形字符不抛异常()
    {
        var report = new LintReport
        {
            File = "a.md", Chars = 3,
            Hits = new List<LintHit> { new() { Id = "P7", Severity = "warn", Match = "​" } },
        };

        var json = new LintReportSet { Reports = new List<LintReport> { report } }.ToJson();

        Assert.Contains("P7", json);
    }
}
```

- [ ] **步骤 2：运行测试以确认失败**

运行：`dotnet test TextTool.Tests/TextTool.Tests.csproj -c Release --filter "LintReportTests"`
预期：FAIL，提示 `LintReport` / `LintHit` 不存在

- [ ] **步骤 3：编写最小实现**

`TextTool.Core/LintReport.cs`：

```csharp
using System.Text.Encodings.Web;
using System.Text.Json;

namespace TextTool.Services;

/// <summary>一条命中。Match 是命中的原文，Snippet 是带上下文的展示片段。</summary>
public sealed class LintHit
{
    public string Id { get; set; } = "";
    public string Group { get; set; } = "";
    public string Title { get; set; } = "";
    public string Severity { get; set; } = "info";
    public int Line { get; set; }
    public int Col { get; set; }
    public int Length { get; set; }
    public string Match { get; set; } = "";
    public string Snippet { get; set; } = "";
    public string Detail { get; set; } = "";
    public string? Hint { get; set; }
    public string? SuggestScheme { get; set; }
}

/// <summary>统计观察，不计入退出码。</summary>
public sealed class LintNote
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
}

public sealed class LintReport
{
    public string File { get; set; } = "";
    public int Chars { get; set; }
    public List<LintHit> Hits { get; set; } = new();
    public List<LintNote> Notes { get; set; } = new();

    /// <summary>过滤命中（Notes 不受影响）。退出码基于过滤后的集合判定。</summary>
    public LintReport Filter(IReadOnlyCollection<string>? onlyIds, string? minSeverity)
    {
        var only = onlyIds is null
            ? null
            : new HashSet<string>(onlyIds, StringComparer.OrdinalIgnoreCase);
        bool WantWarnOnly = string.Equals(minSeverity, "warn", StringComparison.OrdinalIgnoreCase);

        return new LintReport
        {
            File = File,
            Chars = Chars,
            Notes = Notes,
            Hits = Hits
                .Where(h => only is null || only.Contains(h.Id))
                .Where(h => !WantWarnOnly || h.Severity == "warn")
                .ToList(),
        };
    }
}

/// <summary>多文件输出的统一包装，消费者不必按文件数分支。</summary>
public sealed class LintReportSet
{
    public int Version { get; set; } = 1;
    public List<LintReport> Reports { get; set; } = new();

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,   // 中文照原样输出，不转 \uXXXX
    };

    public string ToJson() => JsonSerializer.Serialize(this, Options);
}
```

- [ ] **步骤 4：运行测试以确认通过**

运行：`dotnet test TextTool.Tests/TextTool.Tests.csproj -c Release --filter "LintReportTests"`
预期：PASS（4 个）

- [ ] **步骤 5：提交**

```bash
git add TextTool.Core/LintReport.cs TextTool.Tests/Services/LintReportTests.cs
git commit -m "feat: 新增 AI 味检查报告模型与 JSON 契约"
```

### Task 5：引擎——数据规则扫描与定位

**文件：**

- 创建：`TextTool.Core/AiToneLintService.cs`
- 测试：`TextTool.Tests/Services/AiToneLintServiceTests.cs`

**接口：**

- 消费：`LintRule`（任务 2）、`LintReport`/`LintHit`/`LintNote`（任务 4）
- 产出：
  - `public sealed class AiToneLintService`：构造函数 `AiToneLintService(IEnumerable<LintRule> rules)`、`LintReport Scan(string text, string fileName)`、`public static IReadOnlyCollection<string> AllRuleIds()`
  - 本任务先实现 `Scope=document` 的数据规则；段落与算法规则在任务 6、7 补

- [ ] **步骤 1：编写失败的测试**

```csharp
namespace TextTool.Tests.Services;

public class AiToneLintServiceTests
{
    private static AiToneLintService Service(params LintRule[] rules) => new(rules);

    private static LintRule Rule(string id, string kind, params string[] patterns) => new()
    {
        Id = id, Group = "L", Title = id, Kind = kind,
        Patterns = patterns.ToList(), Severity = "warn", Detail = "d",
    };

    [Fact]
    public void Scan_字面规则命中并给出行列与匹配原文()
    {
        var text = "第一行\n第二行有 赋能 二字";
        var report = Service(Rule("L1", "literal", "赋能")).Scan(text, "t.txt");

        var hit = Assert.Single(report.Hits);
        Assert.Equal(2, hit.Line);
        Assert.Equal(6, hit.Col);
        Assert.Equal("赋能", hit.Match);
        Assert.Contains("赋能", hit.Snippet);
        Assert.Equal(14, report.Chars);
    }

    [Fact]
    public void Scan_正则规则命中()
    {
        var report = Service(Rule("S1", "regex", "不是[^。]*?而是"))
            .Scan("这不是数据问题而是口径问题。", "t.txt");

        Assert.Single(report.Hits);
        Assert.Equal("不是数据问题而是", Assert.Single(report.Hits).Match);
    }

    [Fact]
    public void Scan_重复段落中第二处定位正确()
    {
        var text = "同样的一段\n同样的一段";
        var report = Service(Rule("L1", "literal", "同样")).Scan(text, "t.txt");

        Assert.Equal(2, report.Hits.Count);
        Assert.Equal(1, report.Hits[0].Line);
        Assert.Equal(2, report.Hits[1].Line);
        Assert.Equal(1, report.Hits[1].Col);
    }

    [Fact]
    public void Scan_MinCount未达阈值不报()
    {
        var rule = Rule("L2", "literal", "此外");
        rule.MinCount = 3;

        Assert.Empty(Service(rule).Scan("此外只有一次。", "t.txt").Hits);
        Assert.Equal(3, Service(rule).Scan("此外，此外，此外。", "t.txt").Hits.Count);
    }

    [Fact]
    public void Scan_未命中返回空报告且Chars正确()
    {
        var report = Service(Rule("L1", "literal", "赋能")).Scan("干净的一段话。", "t.txt");

        Assert.Empty(report.Hits);
        Assert.Empty(report.Notes);
        Assert.Equal(7, report.Chars);
        Assert.Equal("t.txt", report.File);
    }
}
```

- [ ] **步骤 2：运行测试以确认失败**

运行：`dotnet test TextTool.Tests/TextTool.Tests.csproj -c Release --filter "AiToneLintServiceTests"`
预期：FAIL，提示 `AiToneLintService` 不存在

- [ ] **步骤 3：编写最小实现**

`TextTool.Core/AiToneLintService.cs`：

```csharp
using System.Text.RegularExpressions;

namespace TextTool.Services;

/// <summary>
/// 中文 AI 味检查引擎：只报位，不改写。
/// 数据规则来自 LintRuleStore；统计类算法规则在本类内。
/// </summary>
public sealed class AiToneLintService
{
    private readonly List<CompiledRule> _rules;

    public AiToneLintService(IEnumerable<LintRule> rules)
        => _rules = rules.Select(CompiledRule.Create).ToList();

    public LintReport Scan(string text, string fileName)
    {
        var report = new LintReport { File = fileName, Chars = text.Length };

        foreach (var rule in _rules)
        {
            if (rule.Rule.Scope == "paragraph")
                ScanParagraphScope(rule, text, report);
            else
                ScanDocumentScope(rule, text, report);
        }

        return report;
    }

    private static void ScanDocumentScope(CompiledRule rule, string text, LintReport report)
    {
        foreach (var (regex, hint) in rule.Patterns)
        {
            var matches = regex.Matches(text);
            if (matches.Count < rule.Rule.MinCount) continue;
            foreach (Match m in matches)
                report.Hits.Add(Hit(rule.Rule, text, m.Index, m.Length, m.Value, hint));
        }
    }

    private static void ScanParagraphScope(CompiledRule rule, string text, LintReport report)
        => throw new NotImplementedException("任务 6 实现");

    internal static LintHit Hit(LintRule rule, string text, int index, int length, string match, string? hint)
    {
        var (line, col) = Position(text, index);
        return new LintHit
        {
            Id = rule.Id, Group = rule.Group, Title = rule.Title, Severity = rule.Severity,
            Line = line, Col = col, Length = length, Match = match,
            Snippet = Snippet(text, index, length),
            Detail = rule.Detail, Hint = hint, SuggestScheme = rule.SuggestScheme,
        };
    }

    /// <summary>全文偏移量换算行列；逐字符累计而非查找首次出现（重复段落会定位错）。</summary>
    internal static (int Line, int Col) Position(string text, int index)
    {
        int line = 1, lineStart = 0;
        for (int i = 0; i < index && i < text.Length; i++)
            if (text[i] == '\n') { line++; lineStart = i + 1; }
        return (line, index - lineStart + 1);
    }

    /// <summary>前后各 8 字的上下文，换行去除。</summary>
    internal static string Snippet(string text, int index, int length, int pad = 8)
    {
        int lo = Math.Max(0, index - pad);
        int hi = Math.Min(text.Length, index + length + pad);
        var s = text[lo..hi].Replace("\r", "").Replace("\n", "");
        if (lo > 0) s = "…" + s;
        if (hi < text.Length) s += "…";
        return s;
    }

    private sealed class CompiledRule
    {
        public required LintRule Rule { get; init; }
        public required List<(Regex Regex, string? Hint)> Patterns { get; init; }

        public static CompiledRule Create(LintRule rule) => new()
        {
            Rule = rule,
            Patterns = rule.Patterns.Select((p, i) =>
            {
                // literal 经 Regex.Escape 走同一条匹配路径；regex 原文经 RegexGuard 构造
                var regex = rule.Kind == "literal"
                    ? RegexGuard.Create(Regex.Escape(p))
                    : RegexGuard.Create(p);
                string? hint = rule.Hints is not null && i < rule.Hints.Count ? rule.Hints[i] : null;
                return (regex, hint);
            }).ToList(),
        };
    }
}
```

- [ ] **步骤 4：运行测试以确认通过**

运行：`dotnet test TextTool.Tests/TextTool.Tests.csproj -c Release --filter "AiToneLintServiceTests"`
预期：PASS（5 个）

- [ ] **步骤 5：提交**

```bash
git add TextTool.Core/AiToneLintService.cs TextTool.Tests/Services/AiToneLintServiceTests.cs
git commit -m "feat: AI 味检查引擎支持全文作用域规则与行列定位"
```

### Task 6：引擎——段落作用域（TailChars / 段内 MinCount）

**文件：**

- 修改：`TextTool.Core/AiToneLintService.cs`（替换 `ScanParagraphScope` 的 `NotImplementedException`）
- 测试：`TextTool.Tests/Services/AiToneLintServiceTests.cs`

**接口：**

- 消费：`Scan`、`CompiledRule`（任务 5）
- 产出：`internal static List<(int Offset, string Text)> Paragraphs(string text)`——返回去空白后的段首绝对下标与段文本，供段落规则与任务 7 的算法规则共用

- [ ] **步骤 1：编写失败的测试**

```csharp
    [Fact]
    public void Scan_段内序数词需同段出现两次才报()
    {
        var rule = Rule("S2", "regex", "首先|其次|最后");
        rule.Scope = "paragraph";
        rule.MinCount = 2;

        Assert.Empty(Service(rule).Scan("首先看这件事。\n其次看那件事。", "t.txt").Hits);
        Assert.Equal(2, Service(rule).Scan("首先看这件事，其次看那件事。", "t.txt").Hits.Count);
    }

    [Fact]
    public void Scan_TailChars只在段末范围内命中()
    {
        var rule = Rule("S3", "regex", "未来可期");
        rule.Scope = "paragraph";
        rule.TailChars = 30;

        var 段中 = "未来可期" + new string('字', 40);
        var 段末 = new string('字', 40) + "未来可期";

        Assert.Empty(Service(rule).Scan(段中, "t.txt").Hits);
        Assert.Single(Service(rule).Scan(段末, "t.txt").Hits);
    }

    [Fact]
    public void Scan_段落命中位置为绝对行列()
    {
        var rule = Rule("S3", "regex", "未来可期");
        rule.Scope = "paragraph";
        var text = "第一段。\n第二段末未来可期";

        var hit = Assert.Single(Service(rule).Scan(text, "t.txt").Hits);

        Assert.Equal(2, hit.Line);
        Assert.Equal(5, hit.Col);
    }
```

- [ ] **步骤 2：运行测试以确认失败**

运行：`dotnet test TextTool.Tests/TextTool.Tests.csproj -c Release --filter "AiToneLintServiceTests"`
预期：FAIL，`NotImplementedException: 任务 6 实现`

- [ ] **步骤 3：编写最小实现**

替换 `ScanParagraphScope` 整个方法：

```csharp
    private static void ScanParagraphScope(CompiledRule rule, string text, LintReport report)
    {
        var paragraphs = Paragraphs(text);
        foreach (var (regex, hint) in rule.Patterns)
        {
            var matches = paragraphs
                .SelectMany(p =>
                {
                    int windowStart = rule.Rule.TailChars is int tail && p.Text.Length > tail
                        ? p.Text.Length - tail
                        : 0;
                    return regex.Matches(p.Text, windowStart)
                        .Select(m => (Index: p.Offset + m.Index, m.Length, m.Value));
                })
                .ToList();
            if (matches.Count < rule.Rule.MinCount) continue;
            foreach (var (index, length, value) in matches)
                report.Hits.Add(Hit(rule.Rule, text, index, length, value, hint));
        }
    }

    /// <summary>
    /// 非空行即一段（与技能侧 tone_lint 的段落口径一致，Markdown 软换行会被拆开——已知简化）。
    /// Offset 为去空白后段首在全文中的绝对下标。
    /// </summary>
    internal static List<(int Offset, string Text)> Paragraphs(string text)
    {
        var result = new List<(int, string)>();
        int offset = 0;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var trimmed = line.Trim();
            if (trimmed.Length > 0)
                result.Add((offset + (line.Length - line.TrimStart().Length), trimmed));
            offset += raw.Length + 1;
        }
        return result;
    }
```

- [ ] **步骤 4：运行测试以确认通过**

运行：`dotnet test TextTool.Tests/TextTool.Tests.csproj -c Release --filter "AiToneLintServiceTests"`
预期：PASS（8 个）

- [ ] **步骤 5：提交**

```bash
git add TextTool.Core/AiToneLintService.cs TextTool.Tests/Services/AiToneLintServiceTests.cs
git commit -m "feat: AI 味检查引擎支持段落作用域与段末窗口"
```

### Task 7：引擎——算法规则（P4/P5 与 C1–C5）

**文件：**

- 修改：`TextTool.Core/AiToneLintService.cs`
- 测试：`TextTool.Tests/Services/AiToneLintServiceTests.cs`

**接口：**

- 消费：`Paragraphs`（任务 6）、`Hit`/`Position`（任务 5）
- 产出：
  - `AiToneLintService.AllRuleIds()`——数据规则 Id 与算法规则 Id 的并集
  - `AiToneLintService.AlgorithmRuleIds`——`P4`、`P5`、`C1`–`C5`
  - `Scan` 在数据规则之后追加算法检出：`P4`/`P5` 产 `Hits`，`C1`–`C5` 产 `Notes`

- [ ] **步骤 1：编写失败的测试**

```csharp
    [Fact]
    public void Scan_引号风格混用产出P4且给出规模()
    {
        var text = "他说\"好\"，又说「行」。";

        var hit = Assert.Single(Service().Scan(text, "t.txt").Hits.Where(h => h.Id == "P4"));

        Assert.Contains("ASCII 直引号", hit.Detail);
        Assert.Contains("直角引号", hit.Detail);
    }

    [Fact]
    public void Scan_引号风格单一不报P4()
    {
        var report = Service().Scan("他说「好」，又说「行」。", "t.txt");

        Assert.DoesNotContain(report.Hits, h => h.Id == "P4");
    }

    [Fact]
    public void Scan_段落长度均一产出C1统计项()
    {
        var text = string.Join("\n", Enumerable.Repeat(new string('字', 20), 5));

        var note = Assert.Single(Service().Scan(text, "t.txt").Notes.Where(n => n.Id == "C1"));

        Assert.Contains("变异系数", note.Text);
    }

    [Fact]
    public void Scan_统计项不进Hits()
    {
        var text = string.Join("\n", Enumerable.Repeat(new string('字', 20), 5));

        Assert.DoesNotContain(Service().Scan(text, "t.txt").Hits, h => h.Group == "C");
    }

    [Fact]
    public void AllRuleIds_含数据规则与算法规则()
    {
        var ids = AiToneLintService.AllRuleIds();

        Assert.Contains("P1", ids);
        Assert.Contains("P4", ids);
        Assert.Contains("C5", ids);
    }
```

- [ ] **步骤 2：运行测试以确认失败**

运行：`dotnet test TextTool.Tests/TextTool.Tests.csproj -c Release --filter "AiToneLintServiceTests"`
预期：FAIL，`AllRuleIds` 不存在、P4/C1 无命中

- [ ] **步骤 3：编写最小实现**

在 `Scan` 的数据规则循环之后追加：

```csharp
        ScanAlgorithmRules(text, report);
        return report;
```

新增以下成员（`P4`/`P5` 与 `C1`–`C5`）：

```csharp
    public static readonly IReadOnlyList<string> AlgorithmRuleIds =
        new[] { "P4", "P5", "C1", "C2", "C3", "C4", "C5" };

    public static IReadOnlyCollection<string> AllRuleIds()
    {
        var ids = new List<string>(AlgorithmRuleIds);
        foreach (var rule in LintRuleStore.Load())
            if (!ids.Contains(rule.Id)) ids.Add(rule.Id);
        return ids;
    }

    private static readonly (string Name, Regex Pattern)[] QuoteStyles =
    {
        ("ASCII 直引号", RegexGuard.Create("\"")),
        ("弯引号", RegexGuard.Create("[“”]")),
        ("直角引号", RegexGuard.Create("[「」]")),
        ("双直角引号", RegexGuard.Create("[『』]")),
        ("ASCII 直单引号", RegexGuard.Create("'")),
        ("弯单引号", RegexGuard.Create("[‘’]")),
    };

    private static readonly Regex OrdinalSkeleton = RegexGuard.Create("首先|其次|再次|最后|其一|其二|其三");

    private static void ScanAlgorithmRules(string text, LintReport report)
    {
        // P4 引号风格混用：命中定位到全文最早出现的引号字符，规模写进 Detail
        var used = QuoteStyles
            .Select(s => (s.Name, Count: s.Pattern.Matches(text).Count, Index: s.Pattern.Match(text).Index))
            .Where(s => s.Count > 0)
            .ToList();
        if (used.Count > 1)
        {
            int first = used.Min(s => s.Index);
            string detail = string.Join(" / ", used.Select(s => $"{s.Name} {s.Count} 个"));
            report.Hits.Add(new LintHit
            {
                Id = "P4", Group = "P", Title = "引号风格混用", Severity = "info",
                Line = Position(text, first).Line, Col = Position(text, first).Col, Length = 1,
                Match = text[first].ToString(), Snippet = Snippet(text, first, 1),
                Detail = detail, SuggestScheme = "引号括号统一",
            });
        }

        // P5 括号全半角混用
        int half = RegexGuard.Create("[()]").Matches(text).Count;
        int full = RegexGuard.Create("[（）]").Matches(text).Count;
        if (half > 0 && full > 0)
        {
            var m = RegexGuard.Create("[()（）]").Match(text);
            report.Hits.Add(new LintHit
            {
                Id = "P5", Group = "P", Title = "括号全半角混用", Severity = "info",
                Line = Position(text, m.Index).Line, Col = Position(text, m.Index).Col, Length = 1,
                Match = m.Value, Snippet = Snippet(text, m.Index, 1),
                Detail = $"半角 {half} 个 / 全角 {full} 个", SuggestScheme = "引号括号统一",
            });
        }

        var paragraphs = Paragraphs(text);

        // C1 段落长度过于均一
        if (paragraphs.Count >= 5)
        {
            var lens = paragraphs.Select(p => (double)p.Text.Length).ToList();
            double mean = lens.Average(), cv = StdDev(lens, mean) / mean;
            if (cv < 0.25)
                report.Notes.Add(new LintNote
                {
                    Id = "C1",
                    Text = $"段落长度过于均一（变异系数 {cv:0.00}，{paragraphs.Count} 段）——人写文本段落长短通常更参差",
                });
        }

        // C2 句长分布过于整齐
        var sentences = RegexGuard.Create("(?<=[。！？…])").Split(text)
            .Select(s => s.Trim()).Where(s => s.Length >= 6).ToList();
        if (sentences.Count >= 10)
        {
            var lens = sentences.Select(s => (double)s.Length).ToList();
            double mean = lens.Average(), cv = StdDev(lens, mean) / mean;
            if (cv < 0.35)
                report.Notes.Add(new LintNote
                {
                    Id = "C2",
                    Text = $"句长分布过于整齐（变异系数 {cv:0.00}，{sentences.Count} 句）",
                });
        }

        // C3 段首两字高度重复
        if (paragraphs.Count >= 4)
        {
            var dup = paragraphs.Select(p => p.Text[..Math.Min(2, p.Text.Length)])
                .GroupBy(h => h).Where(g => g.Count() >= 3).ToList();
            if (dup.Count > 0)
                report.Notes.Add(new LintNote
                {
                    Id = "C3",
                    Text = "段首两字高度重复：" + string.Join("、", dup.Select(g => $"「{g.Key}」×{g.Count()}")),
                });
        }

        // C4 叹号密度偏高
        int exclam = text.Count(c => c == '！');
        if (text.Length >= 400 && exclam * 1000.0 / text.Length > 3)
            report.Notes.Add(new LintNote { Id = "C4", Text = $"叹号密度偏高：{exclam} 个 / {text.Length} 字" });

        // C5 序数词骨架密度
        int ordinals = OrdinalSkeleton.Matches(text).Count;
        if (ordinals >= 2)
            report.Notes.Add(new LintNote
            {
                Id = "C5",
                Text = $"序数词骨架「首先/其次/最后」出现 {ordinals} 次——结构化排版的典型痕迹",
            });
    }

    private static double StdDev(List<double> values, double mean)
    {
        double sum = values.Sum(v => (v - mean) * (v - mean));
        return Math.Sqrt(sum / values.Count);
    }
```

- [ ] **步骤 4：运行测试以确认通过**

运行：`dotnet test TextTool.Tests/TextTool.Tests.csproj -c Release --filter "AiToneLintServiceTests"`
预期：PASS（13 个）

- [ ] **步骤 5：提交**

```bash
git add TextTool.Core/AiToneLintService.cs TextTool.Tests/Services/AiToneLintServiceTests.cs
git commit -m "feat: AI 味检查引擎新增引号/括号与篇章统计算法规则"
```

### Task 8：人读渲染与不可见字符转义

**文件：**

- 创建：`TextTool.Core/LintTextFormatter.cs`
- 测试：`TextTool.Tests/Services/LintTextFormatterTests.cs`

**接口：**

- 消费：`LintReport`（任务 4）
- 产出：`public static class LintTextFormatter`：`string Format(LintReport report)`、`string Visible(string text)`

- [ ] **步骤 1：编写失败的测试**

```csharp
namespace TextTool.Tests.Services;

public class LintTextFormatterTests
{
    [Fact]
    public void Format_按Id分组并显示行列与建议()
    {
        var report = new LintReport
        {
            File = "a.md", Chars = 100,
            Hits = new List<LintHit>
            {
                new() { Id = "S1", Title = "对举句式", Line = 12, Col = 5, Snippet = "…不是A而是B…", Detail = "不是A而是B" },
                new() { Id = "S1", Title = "对举句式", Line = 20, Col = 1, Snippet = "…", Detail = "不是A而是B" },
                new() { Id = "L7", Title = "填充短语", Line = 3, Col = 2, Snippet = "…在这个时间点…", Hint = "现在" },
            },
            Notes = new List<LintNote> { new() { Id = "C1", Text = "段落长度过于均一" } },
        };

        var text = LintTextFormatter.Format(report);

        Assert.Contains("[命中] S1 对举句式（2 处）", text);
        Assert.Contains("第 12 行 第 5 列", text);
        Assert.Contains("<- 现在", text);            // Hint 随命中给出
        Assert.Contains("[统计]", text);
        Assert.Contains("只报位置", text);
    }

    [Fact]
    public void Format_无命中给出OK行()
    {
        var text = LintTextFormatter.Format(new LintReport { File = "a.md", Chars = 5 });

        Assert.Contains("[OK]", text);
    }

    [Fact]
    public void Visible_不可见字符转义为uXXXX()
    {
        var text = LintTextFormatter.Visible("前\u200b后\u00a0终");

        Assert.Equal("前\\u200b后\\u00a0终", text);
    }

    [Fact]
    public void Visible_正文中文原样保留()
    {
        Assert.Equal("正常中文", LintTextFormatter.Visible("正常中文"));
    }
}
```

- [ ] **步骤 2：运行测试以确认失败**

运行：`dotnet test TextTool.Tests/TextTool.Tests.csproj -c Release --filter "LintTextFormatterTests"`
预期：FAIL，提示 `LintTextFormatter` 不存在

- [ ] **步骤 3：编写最小实现**

`TextTool.Core/LintTextFormatter.cs`：

```csharp
using System.Text;

namespace TextTool.Services;

/// <summary>
/// 人读报告渲染。不可见字符必须转义：Windows 控制台 GBK 码页编码不出 U+200B，
/// 直接打印会抛异常，报隐形字符等于不报。
/// </summary>
public static class LintTextFormatter
{
    private static readonly HashSet<char> Escaped = new()
    {
        '\u200B', '\u200C', '\u200D', '\u200E', '\u200F', '\u2060', '\u00AD', '\u00A0', '\uFEFF',
    };

    public static string Format(LintReport report)
    {
        var sb = new StringBuilder();

        if (report.Hits.Count > 0)
        {
            foreach (var group in report.Hits.GroupBy(h => $"{h.Id} {h.Title}"))
            {
                sb.AppendLine($"[命中] {group.Key}（{group.Count()} 处）");
                foreach (var hit in group)
                {
                    string hint = string.IsNullOrEmpty(hit.Hint) ? "" : $"  <- {hit.Hint}";
                    string detail = string.IsNullOrEmpty(hit.Detail) ? "" : $"  <- {hit.Detail}";
                    sb.AppendLine($"       第 {hit.Line} 行 第 {hit.Col} 列  {Visible(hit.Snippet)}{hint}{detail}");
                }
            }
        }
        else
        {
            sb.AppendLine("[OK] 未发现可疑特征");
        }

        if (report.Notes.Count > 0)
        {
            sb.AppendLine("[统计]");
            foreach (var note in report.Notes)
                sb.AppendLine($"       - {note.Id} {note.Text}");
        }

        sb.Append($"[NOTE] 全文 {report.Chars} 字，命中 {report.Hits.Count} 处，" +
                  $"统计项 {report.Notes.Count} 条；本命令只报位置，改写由判断层完成");
        return sb.ToString();
    }

    public static string Visible(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
            sb.Append(Escaped.Contains(c) || c < 0x20 ? $"\\u{(int)c:x4}" : c);
        return sb.ToString();
    }
}
```

- [ ] **步骤 4：运行测试以确认通过**

运行：`dotnet test TextTool.Tests/TextTool.Tests.csproj -c Release --filter "LintTextFormatterTests"`
预期：PASS（4 个）

- [ ] **步骤 5：提交**

```bash
git add TextTool.Core/LintTextFormatter.cs TextTool.Tests/Services/LintTextFormatterTests.cs
git commit -m "feat: AI 味检查人读报告渲染与不可见字符转义"
```

### Task 9：`texttool lint` 子命令

**文件：**

- 修改：`TextTool.Cli/Program.cs`

**接口：**

- 消费：`LintRuleStore.Load()`、`AiToneLintService`、`LintTextFormatter`、`LintReportSet.ToJson()`
- 产出：CLI 子命令 `lint`，退出码 `0`/`1`/`2`

- [ ] **步骤 1：接上子命令并实现**

在 `Program.Main` 的 switch 中，`"join"` 之后追加一行：

```csharp
                "lint" => RunLint(args[1..]),
```

在 `RunJoin` 之后新增：

```csharp
    /// <summary>
    /// lint：只报不改的 AI 味检查。
    /// 退出码与其它子命令不同：0 = 无命中，1 = 有命中，2 = 用法/读取错误。
    /// </summary>
    private static int RunLint(string[] args)
    {
        try
        {
            // Windows 控制台默认 GBK 码页：报隐形字符/中文时可能编码失败，强制 UTF-8 输出
            try { Console.OutputEncoding = new UTF8Encoding(false); } catch (IOException) { }

            var files = new List<string>();
            bool json = false;
            string? only = null, minSeverity = null;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--json": json = true; break;
                    case "--only": only = ParseValue(args, ref i, "--only"); break;
                    case "--min-severity": minSeverity = ParseValue(args, ref i, "--min-severity"); break;
                    case "--help" or "-h": PrintLintHelp(); return 0;
                    default: files.Add(args[i]); break;
                }
            }

            if (files.Count == 0)
                throw new ArgumentException("lint 需要至少一个输入文件（- 表示 stdin）");
            if (minSeverity is not (null or "info" or "warn"))
                throw new ArgumentException("--min-severity 只接受 info 或 warn");

            var onlyIds = only is null
                ? null
                : only.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (onlyIds is not null)
            {
                var known = AiToneLintService.AllRuleIds();
                var unknown = onlyIds.Where(id => !known.Contains(id, StringComparer.OrdinalIgnoreCase)).ToList();
                if (unknown.Count > 0)
                    throw new ArgumentException($"未知规则 Id：{string.Join(", ", unknown)}");
            }

            var service = new AiToneLintService(LintRuleStore.Load());
            var reportSet = new LintReportSet();

            foreach (var file in files)
            {
                string text = file == "-" ? ReadStdin() : ReadFileForLint(file);
                var report = service.Scan(text, file == "-" ? "(stdin)" : Path.GetFileName(file))
                    .Filter(onlyIds, minSeverity);
                reportSet.Reports.Add(report);

                if (!json)
                    Console.WriteLine(LintTextFormatter.Format(report) + Environment.NewLine);
            }

            if (json)
                Console.WriteLine(reportSet.ToJson());

            bool anyHit = reportSet.Reports.Any(r => r.Hits.Count > 0);
            return anyHit ? 1 : 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"错误：{ex.Message}");
            return 2;
        }
    }

    private static string ReadFileForLint(string file)
    {
        if (!File.Exists(file))
            throw new ArgumentException($"文件不存在：{file}");
        var encoding = EncodingDetector.DetectStrict(file);
        return File.ReadAllText(file, encoding.Encoding);
    }

    /// <summary>stdin 按 UTF-8 读原始字节，不受控制台码页影响。</summary>
    private static string ReadStdin()
    {
        using var stream = Console.OpenStandardInput();
        using var reader = new StreamReader(stream, new UTF8Encoding(false));
        return reader.ReadToEnd();
    }

    private static void PrintLintHelp() =>
        Console.WriteLine("""
            lint 选项：
              --json                以 JSON 输出（契约见 doc/specs/2026-09-26-texttool-lint-design.md）
              --only <Id,...>       只跑指定规则，如 --only S1,L1
              --min-severity <info|warn>  只输出 warn 及以上（统计项不受影响）

            输入：文件路径可多个；传 - 表示从 stdin 读。

            退出码（与其它子命令不同）：0 = 无命中，1 = 有命中，2 = 用法/读取错误。
            命中判定基于过滤后的集合，因此 --min-severity warn 即「只在有 warn 时退 1」。
            """);
```

并在 `PrintUsage` 的命令列表中加一行：

```text
              texttool lint <文件...> [选项]        AI 味检查（只报不改）
```

- [ ] **步骤 2：人工验证四条路径**

```bash
dotnet build TextTool.sln -c Release -warnaserror
CLI=TextTool.Cli/bin/Release/net8.0/texttool.exe
printf '此外，此外，此外。这不是数据问题而是口径问题。\n' > /tmp/lint-sample.txt
$CLI lint /tmp/lint-sample.txt ; echo "退出码=$?"      # 期望：命中若干，退出码 1
$CLI lint /tmp/lint-sample.txt --min-severity warn ; echo "退出码=$?"
printf '干净的一段话。\n' > /tmp/lint-clean.txt
$CLI lint /tmp/lint-clean.txt ; echo "退出码=$?"       # 期望：[OK]，退出码 0
$CLI lint /tmp/lint-sample.txt --json | head -30        # 期望：PascalCase 的中文 JSON
$CLI lint /tmp/lint-sample.txt --only NOPE ; echo "退出码=$?"   # 期望：错误 + 退出码 2
```

- [ ] **步骤 3：跑全量门禁并提交**

```bash
dotnet test TextTool.Tests/TextTool.Tests.csproj -c Release
dotnet format TextTool.sln --verify-no-changes
git add TextTool.Cli/Program.cs
git commit -m "feat: 新增 texttool lint 子命令（只报不改，JSON 契约与退出码）"
```

### Task 10：文档与版本

**文件：**

- 修改：`README.md`
- 修改：`doc/TECH-DEBT.md`
- 修改：`Directory.Build.props`

**接口：**

- 消费：CLI 用法（任务 9）
- 产出：无代码接口

- [ ] **步骤 1：更新 README**

英文与中文两处功能概览各加一行 `lint`（只报不改的 AI 味检查）；英文与中文两处项目结构树在 `TextTool.Cli/` 一行补上 `lint`，并在 `TextTool.Core/` 列表中补 `LintRule.cs`、`LintReport.cs`、`LintTextFormatter.cs`、`AiToneLintService.cs`、`default_lint_rules.json`；命令用法段补 `lint` 的示例与**退出码差异**说明。

- [ ] **步骤 2：更新 TECH-DEBT 验证锚点**

`doc/TECH-DEBT.md` §4「验证锚点」中测试数期望值改为实测值：

```bash
grep -roE '\[(Fact|Theory)' TextTool.Tests --include=*.cs | wc -l
```

同时在该文件里登记 R0 的遗留项（内联硬编码兜底待删，见规格 §2 范围之外）。

- [ ] **步骤 3：改版本号**

`Directory.Build.props`：`<Version>2.4.4</Version>` → `<Version>2.5.0</Version>`。

- [ ] **步骤 4：跑门禁并提交**

```bash
dotnet build TextTool.sln -c Release -warnaserror
dotnet test TextTool.Tests/TextTool.Tests.csproj -c Release
git add README.md doc/TECH-DEBT.md Directory.Build.props
git commit -m "docs: 补充 lint 子命令文档与验证锚点，版本升至 2.5.0"
```

### Task 11：技能侧收编（仓外）

**文件：**

- 修改：`E:\work_zone\ClaudeCode\home\.claude\skills\HumanizerZh\SKILL.md`
- 删除：`E:\work_zone\ClaudeCode\home\.claude\skills\HumanizerZh\scripts\tone_lint.py` 与空目录 `scripts\__pycache__`

**接口：**

- 消费：`texttool lint --json` 的输出契约（任务 4、9）
- 产出：技能侧不再有 Python 检查脚本

- [ ] **步骤 1：改写 SKILL.md**

保留"两层、顺序不能反"的骨架，把判断层从 `tone_lint.py` 改为 `texttool lint`：

````markdown
## 判断层：texttool lint

只报位置、不改写。输出 JSON 契约，直接读 `Hits` 逐条处理。

```
<CLI> lint <文件> --json
<CLI> lint - --json          # 从 stdin 读
<CLI> lint <文件> --only S1,L1
```

退出码：`0` 无命中、`1` 有命中、`2` 用法或读取错误。能直接当 CI 关卡用。

命中分三组：`P` 机械层（标点与字符，`SuggestScheme` 字段告诉你该跑哪条方案）、
`L` 词汇层、`S` 句式层；`Notes` 是统计观察（段落/句长变异系数等），不计入退出码。
````

新旧码映射表写进技能：`P1/P2/P3/P6/P7`、`S1`–`S4` 原义保留；原 `S5 套话词` → `L1`；引号/括号混用由统计项提升为 `P4`/`P5`；其余统计项 → `C1`–`C5`。

同时说明：`suggestScheme` 里的 `去AI味标点归一` 需先跑 `install_scheme.py` 装到 exe 目录。

- [ ] **步骤 2：删除 Python 脚本**

```bash
cd "E:/work_zone/ClaudeCode/home/.claude/skills/HumanizerZh"
rm scripts/tone_lint.py
rmdir scripts/__pycache__ 2>/dev/null
ls -R .
```

预期：`scripts/` 下只剩 `install_scheme.py`。

- [ ] **步骤 3：真实文本验证**

```bash
CLI="E:/work_zone/Code/TextTool/TextTool.Cli/bin/Release/net8.0/texttool.exe"
"$CLI" lint /tmp/lint-sample.txt --json | head -40
```

逐条确认：命中可直接读懂；`SuggestScheme` 指向的方案名能在内置或已安装方案里解析；`Notes` 与 `Hits` 分离。

- [ ] **步骤 4：提交**

技能目录不在本仓版本控制下（非 git 仓库），本步只做文件落盘，无 git 提交。

## 决策文档

- **规则的表达方式：混合分层。** 词表/句式进 JSON（改词不改代码），统计与段落算法留 C#（数据表达不了变异系数）。边界写在规格 R1。
- **规则文件是裸数组 + PascalCase。** 复用 `JsonFileStore`/`AtomicFile`；PascalCase 对齐 `default_schemes.json` 与 `System.Text.Json` 默认绑定（camelCase 会静默绑不上）。
- **按 `Id` 合并，而非 `ReplaceSchemeStore` 的"外部非空即整体替换"。** 词表类规则的价值就在只写要改的那条。
- **`Patterns`/`Hints` 是数组。** 单 `pattern` 字段装不下 P1 的两条正则与 L1 的 26 条词；`Hints` 下标对齐 `Patterns`，让报告的 `Hint` 能精确到命中的那一条。
- **`Match` 与 `Snippet` 分开。** 词表规则必须让消费者知道命中了哪个词，上下文片段不足以表达。
- **退出码 0/1/2 与其它子命令不同。** 为 CI 关卡与技能编排保留；判定基于**过滤后**的集合，`--min-severity warn` 因此成为关卡旋钮。
- **`--only` 传未知 Id 报错退 2**，不静默忽略——拼错的规则名不该表现为"跑过了没问题"。
- **算法规则元数据写在代码内**，Id 仍占 `P4`/`P5`/`C` 命名空间，与数据规则共用一套报告字段，`--only` 与过滤逻辑不必分叉。
- **资源名常量用 `TextTool.` 前缀**（`RootNamespace`），并对齐 `PinnedRoots.cs` 的"缺失即抛"而非静默兜底。
- **段落口径 = 非空行**，与技能侧 `tone_lint` 一致；Markdown 软换行会被拆开，是已知简化。

## 测试决策

- **只测外部行为**：`Scan` 的命中（Id/行列/Match/数量）、`Filter` 的过滤、`Merge` 的覆盖、`Format` 的文本、`Visible` 的转义。不断言私有实现，不断言具体正则写法。
- **先例**：`TextTool.Tests/Services/` 下的现有测试（xUnit、`[Fact]`/`[Theory]`、中文测试方法名、`GlobalUsings.cs` 已导入 `TextTool.Services` 与 `Xunit`）。
- **能真正失败的测试**：R0 的资源可达性测试直查 `GetManifestResourceStream`，绕过 store 的兜底；`Patterns`/`Hints` 长度校验、正则语法错、未知 `--only` 都有专门用例。
- **位置正确性是重点**：重复段落中第二处的行列必须正确（历史上这类定位用"找首次出现"会错）。
- **CLI 层不加单元测试**：本仓现有 `TextTool.Tests` 只覆盖 Core，CLI 逻辑保持薄，靠任务 9 的人工四条路径验证。

## 范围之外

- GUI 第 6 个 Tab（引擎已是纯 Core 服务，无 UI 依赖，将来可直接接）
- 目录递归批量、`--fix` 与任何自动改写
- `humanize-scheme.json` 与内置方案重复的 74 条收编
- 删除 `ReplaceScheme.cs` / `VNCharacterScheme.cs` 的内联硬编码兜底（约 200 行重复数据）
- i18n：CLI 输出保持硬编码中文
- 上游 24 条目录里中文不适用的两条（标题大写、弯引号），以及 #3 句末动宾尾缀（建议的 `S7`）

## 进一步说明

- 规则是**活数据**：dogfood 后用 `lint_rules.json` 覆盖层调，不必改代码，也不必改内置文件。
- 误报是这个模块的主要风险，`Severity`（warn/info）与 `MinCount` 是仅有的两个旋钮，调参优先级高于加规则。
- 规格文件 `doc/specs/2026-09-26-texttool-lint-design.md` 是本计划的权威来源，本计划与它冲突时以规格为准。
