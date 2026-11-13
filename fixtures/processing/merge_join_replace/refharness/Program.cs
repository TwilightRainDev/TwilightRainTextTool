// 参考实现的驱动：按 cases.tsv 跑 TextTool.Core 里那几段处理逻辑，把结果写成基准文件。
//
// 命令行的 merge 有 --no-cjk / --fix-punct / --no-trim-comma，no_merge 与 apply_replace
// 只在界面上有、命令行没有，所以要在这里直接调库：命令行接不到的那两档也得有参考输出可比。
// 行合并走的仍是 ProcessingPipeline.Run 本身，不另写一份步骤顺序；写盘落在临时目录里，跑完就删。
//
// 只认 UTF-8 无 BOM 的样本，基准存的时候也去掉 BOM：引擎的接口收发的正文本来就不含 BOM。
using System.Text;
using TextTool.Services;

var encoding = new UTF8Encoding(false);
string here = AppContext.BaseDirectory;
// 参数：用例表所在目录。没给就按源码树的位置往上找。
string root = args.Length > 0 ? args[0] : FindRoot();
string casesPath = Path.Combine(root, "cases.tsv");
string inputsDir = Path.Combine(root, "inputs");
string expectedDir = Path.Combine(root, "expected");
Directory.CreateDirectory(expectedDir);

var work = Path.Combine(Path.GetTempPath(), "ttharness-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(work);
try
{
    int made = 0;
    foreach (var line in File.ReadAllLines(casesPath))
    {
        if (line.Length == 0 || line.StartsWith("#")) continue;
        var cols = line.Split('\t');
        if (cols.Length < 3) continue;

        string name = cols[0];
        string kind = cols[1];
        string input = cols[2];
        var options = new Dictionary<string, List<string>>();
        for (int i = 3; i < cols.Length; i++)
        {
            if (cols[i].Length == 0) continue;
            int eq = cols[i].IndexOf('=');
            if (eq < 0) throw new ArgumentException($"用例 {name} 的列不成键=值：{cols[i]}");
            string key = cols[i][..eq];
            string value = cols[i][(eq + 1)..];
            if (!options.TryGetValue(key, out var list)) options[key] = list = new List<string>();
            list.Add(value);
        }

        string result = kind switch
        {
            "merge" => RunMerge(name, Path.Combine(inputsDir, input), options),
            "replace" => RunReplace(name, Path.Combine(inputsDir, input), options),
            "join" => RunJoin(name, Path.Combine(inputsDir, input), options),
            _ => throw new ArgumentException($"用例 {name} 的类型不认识：{kind}"),
        };

        File.WriteAllText(Path.Combine(expectedDir, name + ".txt"), result, encoding);
        Console.WriteLine($"[基准] {name}");
        made++;
    }
    Console.WriteLine($"共 {made} 份，写在 {expectedDir}");
}
finally
{
    Directory.Delete(work, recursive: true);
}

string FindRoot()
{
    // 从输出目录往上找 cases.tsv，找不到就报错，不猜。
    string? dir = here;
    while (dir is not null)
    {
        if (File.Exists(Path.Combine(dir, "cases.tsv"))) return dir;
        dir = Path.GetDirectoryName(dir);
    }
    throw new DirectoryNotFoundException("找不到 cases.tsv，请把用例表的目录作为第一个参数传进来");
}

string RunMerge(string name, string inputPath, Dictionary<string, List<string>> o)
{
    var mergeOptions = new MergeOptions
    {
        Threshold = Int(o, "threshold", 20),
        Mode = Flag(o, "by_chars", true) ? MergeMode.CharCount : MergeMode.ByteCount,
    };
    var post = new PostProcessOptions(
        FixCjk: Flag(o, "fix_cjk", true),
        FixPunct: Flag(o, "fix_punct", false),
        PunctChars: Text(o, "punct_chars", "，"),
        NoMerge: Flag(o, "no_merge", false),
        NoMergeChars: Text(o, "no_merge_chars", ""),
        ApplyReplace: Flag(o, "apply_replace", false),
        TrimLeadingComma: Flag(o, "trim_leading_comma", true),
        Rules: Rules(o));

    // 参考实现把结果写在源文件旁边，先在临时目录里跑一份，读出来就把临时产物删掉。
    string dir = Path.Combine(work, name);
    Directory.CreateDirectory(dir);
    string copy = Path.Combine(dir, Path.GetFileName(inputPath));
    File.Copy(inputPath, copy);
    var result = ProcessingPipeline.Run(copy, encoding, mergeOptions, post);
    string text = File.ReadAllText(result.OutputPath, encoding);
    Directory.Delete(dir, recursive: true);
    return text;
}

string RunReplace(string name, string inputPath, Dictionary<string, List<string>> o) =>
    // 行合并流水线里也是这么调的（protectBlogRegions: false）。
    // 命令行 replace 命令传的是 true，博文稿里结构那几段会原样留着，这里不比那一档。
    PunctuationReplacer.Apply(File.ReadAllText(inputPath, encoding), Rules(o), protectBlogRegions: false);

string RunJoin(string name, string inputDir, Dictionary<string, List<string>> o)
{
    string dir = Path.Combine(work, name);
    Directory.CreateDirectory(dir);
    foreach (var file in Directory.GetFiles(inputDir))
        File.Copy(file, Path.Combine(dir, Path.GetFileName(file)));

    var (outputPath, _) = FileJoiner.Join(dir, Text(o, "pattern", "*.txt"), "joined.txt");
    string text = File.ReadAllText(outputPath, encoding);
    Directory.Delete(dir, recursive: true);
    return text;
}

List<ReplaceRule> Rules(Dictionary<string, List<string>> o)
{
    var rules = new List<ReplaceRule>();
    if (!o.TryGetValue("rule", out var raw)) return rules;
    foreach (var item in raw)
    {
        int arrow = item.IndexOf("=>", StringComparison.Ordinal);
        if (arrow < 0) throw new ArgumentException($"规则不成 find=>replace：{item}");
        rules.Add(new ReplaceRule { Find = item[..arrow], Replace = item[(arrow + 2)..] });
    }
    return rules;
}

string Text(Dictionary<string, List<string>> o, string key, string fallback) =>
    o.TryGetValue(key, out var list) ? list[^1] : fallback;

bool Flag(Dictionary<string, List<string>> o, string key, bool fallback) =>
    Text(o, key, fallback ? "1" : "0") is var v && v != "0" && v.Length > 0;

int Int(Dictionary<string, List<string>> o, string key, int fallback) =>
    int.Parse(Text(o, key, fallback.ToString()));
