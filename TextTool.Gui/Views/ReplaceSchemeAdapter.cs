using System.Text.Json;
using Avalonia.Controls;
using TextTool.Services;

namespace TextTool.Gui.Views;

/// <summary>
/// 替换方案在通用勾选窗里的差异点：子节点一行一条规则，显示成 查找 到 替换，
/// 勾选粒度是单条规则。编辑走 <see cref="SchemeEditDialog"/>。
/// </summary>
public sealed class ReplaceSchemeAdapter : SchemeAdapterBase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>传入的方案在窗里深拷贝一份，之后只动副本。</summary>
    public ReplaceSchemeAdapter(IEnumerable<ReplaceScheme> schemes) : base(schemes.Cast<object>())
    {
    }

    public override string KeyPrefix => "Scheme";

    public override string Describe(object scheme)
    {
        ReplaceScheme value = Cast(scheme);
        string name = value.IsBuiltIn ? value.Name : "* " + value.Name;
        return string.IsNullOrEmpty(value.Description) ? name : name + " : " + value.Description;
    }

    public override IReadOnlyList<(string Text, object Payload)> Lines(object scheme) =>
        Cast(scheme).Rules
            .Select(rule => (Text: rule.Find + "  →  " + rule.Replace, Payload: (object)rule))
            .ToList();

    public override bool IsBuiltIn(object scheme) => Cast(scheme).IsBuiltIn;

    public override object CreateNew() => new ReplaceScheme();

    public override async Task<object?> OpenEditorAsync(Window owner, object scheme) =>
        await SchemeEditDialog.ShowAsync(owner, Cast(scheme));

    public override object Clone(object scheme) => Copy(Cast(scheme));

    public override string Serialize(IEnumerable<object> schemes) =>
        JsonSerializer.Serialize(schemes.Select(Cast).ToList(), JsonOptions);

    public override List<object>? Deserialize(string json) =>
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
