using System.Text.Json;
using Avalonia.Controls;
using TextTool.Gui.Localization;
using TextTool.Services;

namespace TextTool.Gui.Views;

/// <summary>
/// 视觉小说角色方案在通用勾选窗里的差异点：子节点先列角色名，再列路线名，
/// 一行一个，勾选粒度是单个角色名或路线名。编辑走 <see cref="VNSchemeEditDialog"/>。
/// </summary>
public sealed class VnSchemeAdapter : ISchemeAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public string KeyPrefix => "VnScheme";

    /// <summary>顶层节点：方案名、说明，括号里是角色数与路线数，口径与旧角色方案窗一致。</summary>
    public string Describe(object scheme)
    {
        VNCharacterScheme value = Cast(scheme);
        string name = value.IsBuiltIn ? value.Name : "* " + value.Name;
        string head = string.IsNullOrEmpty(value.Description) ? name : name + " : " + value.Description;

        string info = value.Characters.Count > 0
            ? Loc.T("VnSchemeCharCount", value.Characters.Count)
            : Loc.T("VnSchemeCharNone");
        if (value.RouteNames.Count > 0)
            info += Loc.T("VnSchemeRouteCount", value.RouteNames.Count);

        return head + "  (" + info + ")";
    }

    /// <summary>角色名一行一个，路线名接在后面并带前缀，两者都能单独勾。</summary>
    public IReadOnlyList<(string Text, object Payload)> Lines(object scheme)
    {
        VNCharacterScheme value = Cast(scheme);
        var lines = new List<(string Text, object Payload)>();

        foreach (string character in value.Characters)
            lines.Add((character, character));

        string routePrefix = Loc.T("VnSchemeRoutePrefix");
        foreach (string route in value.RouteNames)
            lines.Add((routePrefix + route, route));

        return lines;
    }

    public bool IsBuiltIn(object scheme) => Cast(scheme).IsBuiltIn;

    public object CreateNew() => new VNCharacterScheme();

    public async Task<object?> EditAsync(Window owner, object scheme) =>
        await VNSchemeEditDialog.ShowAsync(owner, Cast(scheme));

    public object Clone(object scheme) => Copy(Cast(scheme));

    public string Serialize(IEnumerable<object> schemes) =>
        JsonSerializer.Serialize(schemes.Select(Cast).ToList(), JsonOptions);

    public List<object>? Deserialize(string json) =>
        JsonSerializer.Deserialize<List<VNCharacterScheme>>(json, JsonOptions)
            ?.Select(scheme => (object)scheme)
            .ToList();

    private static VNCharacterScheme Cast(object scheme) =>
        scheme as VNCharacterScheme
        ?? throw new ArgumentException("不是角色方案对象", nameof(scheme));

    /// <summary>深拷贝。json 里可能缺字段，这里一并补成空串、空表与空正则。</summary>
    private static VNCharacterScheme Copy(VNCharacterScheme scheme) => new()
    {
        Name = Text(scheme.Name),
        Description = Text(scheme.Description),
        IsBuiltIn = scheme.IsBuiltIn,
        Characters = scheme.Characters is { Count: > 0 } characters
            ? characters.Select(Text).ToList()
            : new List<string>(),
        RouteNames = scheme.RouteNames is { Count: > 0 } routes
            ? routes.Select(Text).ToList()
            : new List<string>(),
        ScenePattern = string.IsNullOrEmpty(scheme.ScenePattern) ? null : scheme.ScenePattern,
    };

    private static string Text(string? value) => value ?? "";
}
