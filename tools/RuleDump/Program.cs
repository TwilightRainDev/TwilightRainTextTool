// 用法：dotnet run --project tools/RuleDump > fixtures/rules_3_1.json
// 输出的字段名与原生版的 LintRule 一一对应，多一个少一个都算不一致。
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TextTool.Services;

var rules = LintRuleStore.GetDefaultRules();

var rows = rules.Select(r => new Dictionary<string, object?>
{
    ["id"] = r.Id,
    ["group"] = r.Group,
    ["title"] = r.Title,
    ["detail"] = r.Detail,
    ["kind"] = r.Kind,
    ["algorithmName"] = r.AlgorithmName,
    ["patterns"] = r.Patterns,
    ["literalPatterns"] = r.LiteralPatterns,
    ["hints"] = r.Hints,
    ["suggestScheme"] = r.SuggestScheme,
    ["skipInMarkdown"] = r.SkipInMarkdown,
    ["skipInBlog"] = r.SkipInBlog,
    ["scope"] = r.Scope,
    ["genres"] = r.Genres,
    ["tailChars"] = r.TailChars,
    ["minCount"] = r.MinCount,
    ["severity"] = r.Severity,
    ["blocksReply"] = r.BlocksReply,
    ["replyMinCount"] = r.ReplyMinCount,
    ["collapseAtDensity"] = r.CollapseAtDensity,
    ["density"] = r.Density is null ? null : new Dictionary<string, object?>
    {
        ["basis"] = r.Density.Basis,
        ["min"] = r.Density.Min,
        ["window"] = r.Density.Window,
    },
    ["minDensityPerKChar"] = r.MinDensityPerKChar,
    ["defaultOff"] = r.DefaultOff,
}).ToList();

var options = new JsonSerializerOptions { WriteIndented = true };
Console.Out.Write(JsonSerializer.Serialize(rows, options));
