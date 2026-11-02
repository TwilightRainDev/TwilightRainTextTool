using System.Text;
using OpenCCNET;
using OpenCCNET.ConversionPipeline;

namespace TextTool.Services;

/// <summary>
/// 繁转简归一：规则表与引擎词表都按简体字面写死，繁体稿直接比对大面积不响；
/// 扫描前先把字面归一，判据才认得出繁体用户交来的字。
///
/// 换字按词做，不按字做。逐字替换分不出语境：「代表著作」会被读成「代表着作」，
/// 而「穿著」「沿著」又确实该换成「穿着」「沿着」。词库与分词交给 OpenCC，两种都读得对。
///
/// 匹配在归一串上做，命中位置与片段按同一下标回原文取，报告交回读者看的仍是他们自己的字。
/// 这份契约要求归一串与原文逐位对齐（等长、下标通用），所以下面逐行转、逐行钉长度：
/// 一行转完等长就直接用；不等长的那一行退回逐字，逐字转不是恰好一个字的字保留原字。
/// 词条本身不含换行，按行切与整篇切的分词结果相同，逐行只是拿到了逐行判长度的机会。
/// </summary>
internal static class TraditionalChinese
{
    /// <summary>
    /// 加载词库约 0.4 秒、几十 MB 内存，挂在首次真要转换的那一刻：
    /// 不做繁简归一的子命令一次都不付这份钱。
    /// </summary>
    private static readonly Lazy<Converter> Loaded = new(Converter.Load, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>一个字的简体写法；本来就不是繁体写法时原样返回。</summary>
    internal static char Simplify(char c) => Loaded.Value.Simplify(c);

    /// <summary>
    /// 繁转简。返回的串与入参等长、逐位对齐，报告里的行列与片段靠这一点回原文取。
    /// </summary>
    internal static string ToSimplified(string text) => Loaded.Value.ToSimplified(text);

    /// <summary>
    /// 真跑转换的那一层。词典随包复制到程序目录，路径从程序所在目录拼：
    /// 图形界面、文件关联、启动器都可能从别处起，库的默认值是相对当前工作目录的 "Dictionary"，
    /// 那样找不到词库，用户看到的是一句与自己操作无关的报错。
    /// </summary>
    private sealed class Converter
    {
        /// <summary>几万字以下不分片：分片的固定开销比省下的时间多。</summary>
        private const int ChunkSize = 32_000;

        /// <summary>
        /// 「著」在繁体里是与「着」同一个字的两种写法，词库把这条改字关系放在台湾异体表里，
        /// 而那张表同时把简体字往异体上翻（么→幺、抬→擡）——拿它整篇转简体稿，
        /// 「什么」会变成「什幺」、含「抬」的判据全部失灵。这里只取那一条改字关系，
        /// 词级的保护交给词库的短语表（著作、著名、顯著…在表里原样对原样，最长匹配先认它们）。
        /// </summary>
        private readonly Dictionary<string, string> _zheToZhe = new() { ["著"] = "着" };

        internal static Converter Load()
        {
            string dictionaries = Path.Combine(AppContext.BaseDirectory, "Dictionary");
            if (!Directory.Exists(dictionaries))
                throw new InvalidOperationException(
                    $"安装包不完整：缺 {dictionaries}，繁转简用不了。重新解压或重新安装可以补齐；" +
                    "这个目录不进仓库，只随发布包和构建产物复制，单独拷 dll 必然缺。");

            // 转换走最大匹配分词，结巴的词表一个字都不读（一套 18 MB，发布包里没有）。
            // 库在启动时只检查这个目录在不在，建个空的给它，别让初始化当场失败。
            string jieba = Path.Combine(AppContext.BaseDirectory, "JiebaResource");
            Directory.CreateDirectory(jieba);

            // 最大匹配比结巴分词快一倍：两千多万字里两者只在个别词上分开，
            // 「著名学者著书立说」最大匹配保住「著书」，结巴读成「着书」。
            ZhConverter.Initialize(dictionaries, jieba, false, SegmentMode.MaxMatch);
            return new Converter();
        }

        internal string ToSimplified(string text) => ConvertLineByLine(text);

        internal char Simplify(char c)
        {
            if (char.IsSurrogate(c)) return c;
            string one = Convert(c.ToString());
            return one.Length == 1 ? one[0] : c;
        }

        /// <summary>
        /// 两段：先让词库的短语表定下「著」在哪些词里作 zhù（著作、顯著、著者…原样留着），
        /// 其余的「著」按「着」算；再让词库把繁体字面换成简体。
        /// 短语表认领的词段不再进后面的字表，所以两段各调一次：「顯著」那样只保护了一个字的词，
        /// 在同一次调用里剩下的字不会再被换成简体。
        /// </summary>
        private string Convert(string text)
        {
            // 短语表在本方向上唯一改字的地方是「著」，其余词条在繁体稿里改成标准繁体后又被第二段
            // 换回简体，绕一圈落回原字。行里没有「著」就直接进第二段，省一半查表。
            if (text.IndexOf('著') < 0) return ZhConverter.HantToHans(text);

            string phrases = new MaxMatchConversionPipeline(text)
                .ConvertBy(new[] { ZhConverter.ZhDictionary.TWVariantsRevPhrases, _zheToZhe })
                .GetResult();
            return ZhConverter.HantToHans(phrases);
        }

        /// <summary>
        /// 按行转，切点落在换行之后，片与片能在原顺序上拼回去。
        /// 转换是 CPU 密集的字典查表，多核按片并行，四千万字的整库靠这一步把多付的时间
        /// 压到与读盘同量级。
        /// </summary>
        private string ConvertLineByLine(string text)
        {
            int workers = Math.Clamp(text.Length / ChunkSize, 1, Environment.ProcessorCount);
            if (workers == 1) return ConvertRange(text, 0, text.Length);

            var bounds = new int[workers + 1];
            bounds[workers] = text.Length;
            for (int i = 1; i < workers; i++)
            {
                int cut = (int)((long)text.Length * i / workers);
                int newline = text.IndexOf('\n', cut);
                bounds[i] = newline < 0 ? text.Length : newline + 1;
            }

            var pieces = new string[workers];
            Parallel.For(0, workers, i => pieces[i] = ConvertRange(text, bounds[i], bounds[i + 1]));
            return string.Concat(pieces);
        }

        private string ConvertRange(string text, int start, int end)
        {
            var result = new StringBuilder(end - start);
            int i = start;
            while (i < end)
            {
                int newline = text.IndexOf('\n', i, end - i);
                int stop = newline < 0 ? end : newline + 1;
                AppendLine(result, text.AsSpan(i, stop - i));
                i = stop;
            }
            return result.ToString();
        }

        /// <summary>一行的归一并把长度钉回原样。</summary>
        private void AppendLine(StringBuilder result, ReadOnlySpan<char> line)
        {
            string raw = line.ToString();
            string converted = Convert(raw);
            if (converted.Length == raw.Length)
            {
                result.Append(converted);
                return;
            }

            // 这一行有字一换多（「鳽」换出来是两个字符），词级结果留不住长度，只能退回逐字。
            // 代价是这一行的词级消歧一起作废：逐字替换分不出语境，行里的「著作」会被读成「着作」。
            // 范围限于含这类字的行，别处不受牵动。
            foreach (char c in raw)
            {
                if (char.IsSurrogate(c))
                {
                    result.Append(c);
                    continue;
                }

                string one = Convert(c.ToString());
                result.Append(one.Length == 1 ? one : c);
            }
        }
    }
}
