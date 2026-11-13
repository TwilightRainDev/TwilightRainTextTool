// 行合并、文件拼接、标点替换的逐字节对照测试。
//
// 测试表、输入、期望输出都在 fixtures/processing/merge_join_replace/ 下：
//   cases.tsv        用例名、类型、输入、键=值（键可重复，rule 每列一条）
//   inputs/          输入正文；join 的输入是目录，引擎只收正文
//   expected/        期望输出的正文，由 make_goldens.sh 生成，不要手改
// 期望输出是 UTF-8 无 BOM 的整份正文（含行尾 \n），这里按键读字节、逐字节比对。
//
// 加测试只改 cases.tsv 再跑 make_goldens.sh，本文件不用动。
#include "doctest/doctest.h"

#include "engine/io.h"
#include "engine/processing.h"

#include <algorithm>
#include <cctype>
#include <filesystem>
#include <fstream>
#include <iterator>
#include <map>
#include <sstream>
#include <string>
#include <utility>
#include <vector>

namespace {

namespace fs = std::filesystem;

const fs::path kFixtureDir = fs::path(TEXTTOOL_FIXTURES_DIR) / "processing" / "merge_join_replace";

// 整份文件按字节读，不做换行翻译：期望输出里有什么就跟什么比。
std::string read_bytes(const fs::path& path) {
    std::ifstream in(path, std::ios::binary);
    REQUIRE_MESSAGE(in.good(), "打不开文件：" << path.string());
    std::ostringstream buffer;
    buffer << in.rdbuf();
    return buffer.str();
}

struct Case {
    std::string name;
    std::string kind;
    std::string input;
    std::map<std::string, std::string> values;             // 单值键，后写的覆盖前面的
    std::vector<std::pair<std::string, std::string>> rules; // rule 列，按出现顺序
};

std::vector<Case> load_cases() {
    std::string table = read_bytes(kFixtureDir / "cases.tsv");
    std::vector<Case> cases;
    std::istringstream stream(table);
    std::string line;
    while (std::getline(stream, line)) {
        if (!line.empty() && line.back() == '\r') line.pop_back();
        if (line.empty() || line[0] == '#') continue;

        std::vector<std::string> cols;
        std::string col;
        std::istringstream row(line);
        while (std::getline(row, col, '\t')) cols.push_back(col);
        REQUIRE_MESSAGE(cols.size() >= 3, "用例行不成四列：" << line);

        Case item;
        item.name = cols[0];
        item.kind = cols[1];
        item.input = cols[2];
        for (size_t i = 3; i < cols.size(); ++i) {
            if (cols[i].empty()) continue;
            size_t eq = cols[i].find('=');
            REQUIRE_MESSAGE(eq != std::string::npos, "用例 " << item.name << " 的列不成键=值：" << cols[i]);
            std::string key = cols[i].substr(0, eq);
            std::string value = cols[i].substr(eq + 1);
            if (key == "rule") {
                size_t arrow = value.find("=>");
                REQUIRE_MESSAGE(arrow != std::string::npos,
                                "用例 " << item.name << " 的规则不成 find=>replace：" << value);
                item.rules.emplace_back(value.substr(0, arrow), value.substr(arrow + 2));
            } else {
                item.values[key] = value;
            }
        }
        cases.push_back(std::move(item));
    }
    REQUIRE_MESSAGE(!cases.empty(), "用例表里一条用例都没有");
    return cases;
}

bool flag(const Case& item, const std::string& key, bool fallback) {
    auto it = item.values.find(key);
    if (it == item.values.end()) return fallback;
    return !it->second.empty() && it->second != "0";
}

std::string text(const Case& item, const std::string& key, const std::string& fallback) {
    auto it = item.values.find(key);
    return it == item.values.end() ? fallback : it->second;
}

int number(const Case& item, const std::string& key, int fallback) {
    auto it = item.values.find(key);
    return it == item.values.end() ? fallback : std::stoi(it->second);
}

texttool::MergeOptions merge_options(const Case& item) {
    texttool::MergeOptions options;
    options.threshold = number(item, "threshold", 20);
    options.by_chars = flag(item, "by_chars", true);
    options.fix_cjk = flag(item, "fix_cjk", true);
    options.fix_punct = flag(item, "fix_punct", false);
    options.punct_chars = text(item, "punct_chars", "，");
    options.no_merge = flag(item, "no_merge", false);
    options.no_merge_chars = text(item, "no_merge_chars", "");
    options.apply_replace = flag(item, "apply_replace", false);
    options.trim_leading_comma = flag(item, "trim_leading_comma", true);
    options.replace_rules = item.rules;
    return options;
}

// 拼接的输入是一个目录。挑文件与排顺序是命令行那一层的活，这里照同样的规矩来：
// 按文件名不分大小写升序。引擎只收排好的正文。
std::vector<std::string> join_parts(const fs::path& dir) {
    std::vector<fs::path> files;
    for (const auto& entry : fs::directory_iterator(dir)) {
        if (entry.is_regular_file()) files.push_back(entry.path());
    }
    std::sort(files.begin(), files.end(), [](const fs::path& a, const fs::path& b) {
        std::string left = a.filename().string();
        std::string right = b.filename().string();
        std::transform(left.begin(), left.end(), left.begin(),
                       [](unsigned char c) { return static_cast<char>(std::tolower(c)); });
        std::transform(right.begin(), right.end(), right.begin(),
                       [](unsigned char c) { return static_cast<char>(std::tolower(c)); });
        return left < right;
    });

    std::vector<std::string> parts;
    for (const auto& file : files) parts.push_back(read_bytes(file));
    return parts;
}

// 同一份 cases.tsv 跑三遍：merge / join / replace 各一个 TEST_CASE，
// 哪一类没对上，报错里点名是哪一行，别只报一个数字。
void run_cases(const std::string& kind) {
    int passed = 0;
    int failed = 0;
    for (const Case& item : load_cases()) {
        if (item.kind != kind) continue;

        std::string actual;
        std::string expected = read_bytes(kFixtureDir / "expected" / (item.name + ".txt"));
        if (kind == "merge") {
            actual = texttool::merge_lines(read_bytes(kFixtureDir / "inputs" / item.input),
                                           merge_options(item));
        } else if (kind == "replace") {
            texttool::ReplaceOptions options;
            options.rules = item.rules;
            actual = texttool::replace_punctuation(read_bytes(kFixtureDir / "inputs" / item.input),
                                                   options);
        } else if (kind == "join") {
            texttool::JoinOptions options;
            options.pattern = text(item, "pattern", "*.txt");
            actual = texttool::join_texts(join_parts(kFixtureDir / "inputs" / item.input), options);
        } else {
            FAIL("用例 " << item.name << " 的类型不认识：" << kind);
        }

        if (actual == expected) {
            ++passed;
            continue;
        }
        ++failed;
        CHECK_MESSAGE(false, "用例没对上：" << item.name << "\n  期望 " << expected.size()
                                            << " 字节：" << expected.substr(0, 300)
                                            << "\n  实际 " << actual.size()
                                            << " 字节：" << actual.substr(0, 300));
    }
    MESSAGE("[" << kind << "] 通过 " << passed << " 条，没通过 " << failed << " 条");
    CHECK(failed == 0);
    CHECK(passed > 0);
}

}  // namespace

TEST_CASE("行合并对上对照基准") { run_cases("merge"); }TEST_CASE("文件拼接对上对照基准") { run_cases("join"); }
TEST_CASE("标点替换对上对照基准") { run_cases("replace"); }

TEST_CASE("按字节合并时宽度按原稿编码算：GBK 稿里一个汉字两字节") {
    // 两行各五个汉字：GBK 十字节，UTF-8 十五字节。阈值十二落在两者之间，
    // 所以同一份稿子按 GBK 量会接成一行，按 UTF-8 量会断成两行。
    const std::string text = "一二三四五\n六七八九十\n";

    texttool::MergeOptions gbk;
    gbk.threshold = 12;
    gbk.by_chars = false;
    gbk.source_encoding = texttool::TextEncoding::Gbk;
    gbk.fix_cjk = false;
    gbk.trim_leading_comma = false;
    CHECK(texttool::merge_lines(text, gbk) == "一二三四五六七八九十\n");

    texttool::MergeOptions utf8 = gbk;
    utf8.source_encoding = texttool::TextEncoding::Utf8;
    CHECK(texttool::merge_lines(text, utf8) == text);
}

TEST_CASE("标点替换在博文里保留结构段：代码块、行内代码、链接地址都不动") {    const std::string text =
        "---\ntitle: 博文\ndate: 2024-01-01\n---\n"
        "正文一句, 带逗号\n\n"
        "```\ncode, a\n```\n"
        "行内 `a, b` 与链接 [字](http://x/a,b)\n"
        ":::[info] 指令, 行\n";

    texttool::ReplaceOptions protect;
    protect.rules = {{",", "，"}};
    CHECK(texttool::replace_punctuation(text, protect) ==
          "---\ntitle: 博文\ndate: 2024-01-01\n---\n"
          "正文一句， 带逗号\n\n"
          "```\ncode, a\n```\n"
          "行内 `a, b` 与链接 [字](http://x/a,b)\n"
          ":::[info] 指令, 行\n");

    // 行合并流水线按行套规则，看不到整份稿子的头尾，空格副本（结构区域全是空格的那份副本）那一档关着
    texttool::ReplaceOptions plain;
    plain.rules = {{",", "，"}};
    plain.protect_blog = false;
    CHECK(texttool::replace_punctuation(text, plain).find("code， a") != std::string::npos);
}

TEST_CASE("行合并按块喂与整份喂等价：块切在哪里都不影响结果") {
    // 从盘上逐段读就是这条路：读一块喂一块，块边界可能落在任何字节上，
    // 包括落在 一行的中间、\r 与 \n 之间、一个多字节字的中间。结果必须与一次喂完整份相同。
    const auto merge_in_chunks = [](const std::string& text, const texttool::MergeOptions& options,
                                    std::size_t chunk) {
        std::string result;
        texttool::MergePipeline pipeline(options, [&result](std::string_view piece) {
            result.append(piece);
        });
        for (std::size_t at = 0; at < text.size(); at += chunk) {
            const std::size_t take = std::min(chunk, text.size() - at);
            pipeline.feed(std::string_view(text).substr(at, take));
        }
        pipeline.finish();
        return result;
    };

    int checked = 0;
    for (const Case& item : load_cases()) {
        if (item.kind != "merge") continue;
        const std::string input = read_bytes(kFixtureDir / "inputs" / item.input);
        const texttool::MergeOptions options = merge_options(item);
        const std::string whole = texttool::merge_lines(input, options);
        for (const std::size_t chunk : {std::size_t{1}, std::size_t{2}, std::size_t{3},
                                        std::size_t{7}, std::size_t{64}}) {
            CHECK_MESSAGE(merge_in_chunks(input, options, chunk) == whole,
                          "用例 " << item.name << " 按 " << chunk << " 字节一块时与整份不一致");
            ++checked;
        }
    }
    MESSAGE("[分块] 比了 " << checked << " 次");
    CHECK(checked > 0);
}

TEST_CASE("行合并按块喂：\\r\\n 被块边界切开时不多出一行") {
    texttool::MergeOptions options;
    options.threshold = 1;  // 一行一断，看的就是切行本身
    options.fix_cjk = false;
    options.trim_leading_comma = false;
    const std::string text = "a\r\nb\rc\n";

    const std::string whole = texttool::merge_lines(text, options);
    CHECK(whole == "a\nb\nc\n");

    // 切在 \r 与 \n 之间：这一段末尾是孤零零的 \r，下一块开头才是 \n
    std::string result;
    texttool::MergePipeline pipeline(options, [&result](std::string_view piece) {
        result.append(piece);
    });
    pipeline.feed("a\r");
    pipeline.feed("\nb\rc\n");
    pipeline.finish();
    CHECK(result == whole);
}

TEST_CASE("行合并按块喂：最后一行没有行尾也照交") {
    texttool::MergeOptions options;
    options.fix_cjk = false;
    options.trim_leading_comma = false;

    const std::string whole = texttool::merge_lines("甲乙丙", options);
    CHECK(whole == "甲乙丙\n");

    std::string result;
    texttool::MergePipeline pipeline(options, [&result](std::string_view piece) {
        result.append(piece);
    });
    pipeline.feed("甲乙");
    pipeline.feed("丙");
    pipeline.finish();
    CHECK(result == whole);
}

namespace {

// 一份稿按 chunk 字节一块喂进换标点管线，结果攒成一份交回。
std::string replace_in_chunks(const std::string& text, const texttool::ReplaceOptions& options,
                              bool blog_post, std::size_t chunk) {
    std::string result;
    texttool::ReplacePipeline pipeline(options, blog_post,
                                       [&result](std::string_view piece) { result.append(piece); });
    for (std::size_t at = 0; at < text.size(); at += chunk) {
        const std::size_t take = std::min(chunk, text.size() - at);
        pipeline.feed(std::string_view(text).substr(at, take));
    }
    pipeline.finish();
    return result;
}

// 换标点写盘那条路先要判这份稿子算不算博文：按逐行状态机看一遍，与换标点时用的是同一个判法。
bool blog_post_of(const std::string& text) {
    texttool::BlogMaskScanner scanner;
    const auto skip = [](const texttool::BlogMaskScanner::LineMask&) {};
    scanner.feed(text, skip);
    scanner.finish(skip);
    return scanner.is_blog_post();
}

// 测试自己造一份稿，跑完删掉。
class TempDoc {
public:
    TempDoc(const std::string& name, const std::string& bytes) {
        path_ = std::filesystem::temp_directory_path() / ("texttool-replace-" + name);
        std::ofstream out(path_, std::ios::binary);
        out.write(bytes.data(), static_cast<std::streamsize>(bytes.size()));
    }
    ~TempDoc() {
        std::error_code ignored;
        std::filesystem::remove(target(), ignored);
        std::filesystem::remove(path_, ignored);
    }
    TempDoc(const TempDoc&) = delete;
    TempDoc& operator=(const TempDoc&) = delete;

    std::string path() const { return path_.string(); }
    std::string target() const { return (path_.parent_path() / ("out-" + path_.filename().string())).string(); }

private:
    std::filesystem::path path_;
};

}  // namespace

TEST_CASE("换标点按块喂与整份喂等价：博文稿、围栏未闭合、跳行注释跨段、末尾无换行") {
    // 从盘上逐段读就是这条路：读一块喂一块。块边界可能落在任何字节上：一行中间、
    // 一个字的中间、规则 find 的中间。结果必须与一次喂完整份逐字节相同。
    std::string long_frontmatter = "---\n";
    for (int line = 0; line < 205; ++line) long_frontmatter += "正文一行, 带逗号\n";
    long_frontmatter += "title: 两百行以后才出现的键\ndate: 2024-01-01\n";

    const std::vector<std::string> inputs = {
        "",
        "\n",
        "不是博文的一段, 一句. 另一句\n第二行, 也有逗号\n",
        // 博文稿：头部字段、围栏代码块、行内代码、链接地址、指令行都按结构遮
        "---\ntitle: 一份博文\ndate: 2024-01-01\n---\n正文一句, 带逗号\n\n```\ncode, a\n```\n"
        "行内 `a, b` 与链接 [字](http://x/a,b)\n:::[info] 指令, 行\n",
        // 围栏一直没关门：后面的行连同行内代码全在块里
        "---\ntitle: 围栏没关\ndate: 2024-01-01\n---\n正文, 一句\n```\ncode, 到这里还没关\n"
        "行内 `a, b` 也不成对\n",
        // 跳行 HTML 注释跨过好几行，注释里外各要一段
        "---\ntitle: 注释跨段\ndate: 2024-01-01\n---\n正文, 一句\n<!-- 注释从这里开始\n"
        "还在注释里, 不换\n-->\n注释外面, 要换\n",
        // 末尾没有换行
        "---\ntitle: 末尾无换行\ndate: 2024-01-01\n---\n最后一行没有换行, 逗号。",
        // 开头是 --- 但紧跟收尾、没有 YAML 键值行：不是头部字段，这几行按正文重放
        "---\n标题: 没有 YAML 键\n---\n分隔线后面是正文, 要换\n",
        // 两百行里都没有 YAML 键值行：压着的那几行一起按正文重放
        long_frontmatter,
        // 公式块、自动链接、引用式链接定义
        "---\ntitle: 块公式\ndate: 2024-01-01\n---\n$$\na, b\n$$\n正文 <https://x/a,b> 与"
        " [名字]: http://x/a,b\n",
        "甲\r\n乙, 丙\r\n\r\n丁, 戊。\r\n",
    };

    // 两套规则：一套是单个标点，一套带跨块才凑得齐的 find，检验规则链压着的尾巴。
    const std::vector<std::vector<std::pair<std::string, std::string>>> rule_sets = {
        {{",", "，"}, {".", "。"}},
        {{"，。", "。"}, {"……", "…"}, {", ", "，"}},
    };

    int checked = 0;
    for (const std::string& input : inputs) {
        const bool blog_post = blog_post_of(input);
        CHECK_MESSAGE(blog_post == texttool::BlogMask::create(input).is_blog_post(),
                      "逐行状态机与整份建遮罩对「是不是博文」的说法不一样");
        for (const auto& rules : rule_sets) {
            texttool::ReplaceOptions options;
            options.rules = rules;
            const std::string whole = texttool::replace_punctuation(input, options);
            for (const std::size_t chunk : {std::size_t{1}, std::size_t{2}, std::size_t{3},
                                            std::size_t{7}, std::size_t{64}, std::size_t{4096}}) {
                CHECK_MESSAGE(replace_in_chunks(input, options, blog_post, chunk) == whole,
                              "按 " << chunk << " 字节一块时与整份不一致");
                ++checked;
            }
        }
    }
    MESSAGE("[换标点分块] 比了 " << checked << " 次");
    CHECK(checked > 0);
}

TEST_CASE("换标点按块喂：关掉遮罩那一档也与整份喂一样") {
    const std::string text = "---\ntitle: 一份博文\ndate: 2024-01-01\n---\n正文, 一句\n```\ncode, a\n```\n";
    texttool::ReplaceOptions options;
    options.rules = {{",", "，"}};
    options.protect_blog = false;

    const std::string whole = texttool::replace_punctuation(text, options);
    CHECK(whole.find("code， a") != std::string::npos);  // 关掉空格副本，代码块里的逗号也换
    for (const std::size_t chunk : {std::size_t{1}, std::size_t{5}, std::size_t{64}})
        CHECK(replace_in_chunks(text, options, false, chunk) == whole);
}

TEST_CASE("换标点写盘：逐段写出来的字节与整份算完再写的一样") {
    // 编码、BOM 与换行按原样写回，两条路写出的文件逐字节相同。
    const std::vector<std::pair<std::string, std::string>> docs = {
        {"utf8.txt", "---\ntitle: 一份博文\ndate: 2024-01-01\n---\n正文, 一句\n```\ncode, a\n```\n"},
        {"bom.txt", "\xEF\xBB\xBF" "没有头部字段, 一句. 另一句\n"},
        {"gbk.txt", "\xD6\xD0\xCE\xC4\x2C \xD2\xBB\xBE\xE4\n"},  // GBK 的「中文, 一句」
        {"empty.txt", ""},
    };

    for (const auto& [name, bytes] : docs) {
        CAPTURE(name);
        texttool::ReplaceOptions options;
        options.rules = {{",", "，"}, {".", "。"}};

        TempDoc doc(name, bytes);
        const texttool::LoadedText loaded = texttool::load_text(doc.path());
        const std::string whole = texttool::replace_punctuation(loaded.text, options);
        texttool::write_text(doc.target() + "-whole", whole, loaded.encoding);

        const texttool::TextEncoding written =
            texttool::replace_punctuation_file(doc.path(), doc.target(), options);
        CHECK(written == loaded.encoding);

        std::ifstream streamed(doc.target(), std::ios::binary);
        const std::string got((std::istreambuf_iterator<char>(streamed)),
                              std::istreambuf_iterator<char>());
        std::ifstream by_hand(doc.target() + "-whole", std::ios::binary);
        const std::string want((std::istreambuf_iterator<char>(by_hand)),
                               std::istreambuf_iterator<char>());
        std::error_code ignored;
        std::filesystem::remove(doc.target() + "-whole", ignored);
        CHECK(got == want);
    }
}
