using System.Text.Json;
using Avalonia.Controls;
using TextTool.Services;

namespace TextTool.Gui.Views;

/// <summary>
/// 替换方案在通用勾选窗里的差异点：子节点一行一条规则，显示成 查找 到 替换，
/// 勾选粒度是单条规则。编辑走 <see cref="SchemeEditDialog"/>。
/// </summary>
public sealed class ReplaceSchemeAdapter : ISchemeAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public string KeyPrefix => "Scheme";

    public string Describe(object scheme)
    {
        ReplaceScheme value = Cast(scheme);
        string name = value.IsBuiltIn ? value.Name : "* " + value.Name;
        return string.IsNullOrEmpty(value.Description) ? name : name + " : " + value.Description;
    }

    public IReadOnlyList<(string Text, object Payload)> Lines(object scheme) =>
        Cast(scheme).Rules
            .Select(rule => (Text: rule.Find + "  →  " + rule.Replace, Payload: (object)rule))
            .ToList();

    public bool IsBuiltIn(object scheme) => Cast(scheme).IsBuiltIn;

    public object CreateNew() => new ReplaceScheme();

    public async Task<object?> EditAsync(Window owner, object scheme) =>
        await SchemeEditDialog.ShowAsync(owner, Cast(scheme));

    public object Clone(object scheme) => Copy(Cast(scheme));

    public string Serialize(IEnumerable<object> schemes) =>
        JsonSerializer.Serialize(schemes.Select(Cast).ToList(), JsonOptions);

    public List<object>? Deserialize(string json) =>
        JsonSerializer.Deserialize<List<ReplaceScheme>>(json, JsonOptions)
            ?.Select(scheme => (object)scheme)
            .ToList();

    private static ReplaceScheme Cast(object scheme) =>
        scheme as ReplaceScheme
        ?? throw new ArgumentException("不是替换方案对象", nameof(scheme));

    /// <summary>深拷贝。json 里可能缺字段，这里一并补成空串与空表。</summary>
    private static ReplaceScheme Copy(ReplaceScheme scheme) => new()
    {
        Name = Text(scheme.Name),
        Description = Text(scheme.Description),
        IsBuiltIn = scheme.IsBuiltIn,
        Rules = scheme.Rules is { Count: > 0 } rules
            ? rules.Select(rule => new ReplaceRule
            {
                Find = Text(rule.Find),
                Replace = Text(rule.Replace),
            }).ToList()
            : new List<ReplaceRule>(),
    };

    private static string Text(string? value) => value ?? "";
}
