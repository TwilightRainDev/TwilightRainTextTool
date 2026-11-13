// 视觉小说排版与 fixtures/processing/cases.tsv 指的期望输出逐字节对照。
//
// 测试表与期望输出由 fixtures/processing/make_goldens.sh 生成，加测试只改那张表。
// 表里的第 5 列是生成脚本用的方案名；测试只看第 6~8 列，
// 也就是引擎真正收到的角色名、路线名与场景正则。
#include "doctest/doctest.h"

#include "engine/io.h"
#include "engine/processing.h"

#include <algorithm>
#include <filesystem>
#include <fstream>
#include <iterator>
#include <sstream>
#include <stdexcept>
#include <string>
#include <vector>

using texttool::reformat_vn;
using texttool::VnOptions;

namespace {

std::string read_bytes(const std::string& path) {
    std::ifstream in(path, std::ios::binary);
    REQUIRE_MESSAGE(in, "读不到 " << path);
    std::ostringstream buffer;
    buffer << in.rdbuf();
    return buffer.str();
}

std::string fixture(const std::string& name) {
    return std::string(TEXTTOOL_FIXTURES_DIR) + "/processing/" + name;
}

std::vector<std::string> split(const std::string& text, char separator) {
    std::vector<std::string> out;
    std::size_t from = 0;
    while (true) {
        const std::size_t at = text.find(separator, from);
        if (at == std::string::npos) {
            out.push_back(text.substr(from));
            return out;
        }
        out.push_back(text.substr(from, at - from));
        from = at + 1;
    }
}

// 一列里有多项时用分号隔开，没有就写「-」。
std::vector<std::string> list_column(const std::string& cell) {
    if (cell.empty() || cell == "-") return {};
    return split(cell, ';');
}

struct VnCase {
    std::string name;
    std::string input;
    int max_paragraph = 450;
    bool do_reformat = true;
    bool do_fix_punct = true;
    std::vector<std::string> characters;
    std::vector<std::string> route_names;
    std::string scene_pattern;
};

// 读测试表。列说明见 cases.tsv 开头那几行。
std::vector<VnCase> load_cases() {
    std::vector<VnCase> cases;
    for (std::string line : split(read_bytes(fixture("cases.tsv")), '\n')) {
        if (!line.empty() && line.back() == '\r') line.pop_back();
        if (line.empty() || line[0] == '#') continue;

        const std::vector<std::string> cells = split(line, '\t');
        REQUIRE_MESSAGE(cells.size() == 8, "用例表这一行不是八列：" << line);

        VnCase item;
        item.name = cells[0];
        item.input = cells[1];
        if (cells[2] != "-") item.max_paragraph = std::stoi(cells[2]);

        const std::string& mode = cells[3];
        const bool known_mode = mode == "both" || mode == "reformat" || mode == "punct";
        REQUIRE_MESSAGE(known_mode, "认不出的模式：" << mode);
        item.do_reformat = mode != "punct";
        item.do_fix_punct = mode != "reformat";

        item.characters = list_column(cells[5]);
        item.route_names = list_column(cells[6]);
        if (cells[7] != "-") item.scene_pattern = cells[7];

        cases.push_back(std::move(item));
    }
    return cases;
}

// 失败时看得懂的一句话：差在第几个字节、附近各是什么。
std::string diff_note(const std::string& want, const std::string& got) {
    std::size_t at = 0;
    while (at < want.size() && at < got.size() && want[at] == got[at]) ++at;

    const std::size_t window = 40;
    auto around = [&](const std::string& text) {
        return text.substr(at, window);
    };

    std::ostringstream note;
    note << "第 " << at << " 字节起不同：期望「" << around(want) << "」共 " << want.size()
         << " 字节，实得「" << around(got) << "」共 " << got.size() << " 字节";
    return note.str();
}

// 把一段字重复若干遍，省得在测试里贴几百个字。
std::string repeat_text(const std::string& piece, std::size_t times) {
    std::string out;
    out.reserve(piece.size() * times);
    for (std::size_t i = 0; i < times; ++i) out.append(piece);
    return out;
}

// 排版结果里每一段的字数，按 UTF-16 单元数算，与引擎量段长的口径一致。
// 段与段之间空一行，所以按换行切开时会夹着空串，空串的段长是 0。
std::vector<std::size_t> paragraph_lengths(const std::string& text) {
    std::vector<std::size_t> lengths;
    std::size_t from = 0;
    while (true) {
        const std::size_t at = text.find('\n', from);
        const std::size_t end = at == std::string::npos ? text.size() : at;
        std::size_t units = 0;
        for (std::size_t i = from; i < end;) {
            const auto lead = static_cast<unsigned char>(text[i]);
            const std::size_t width =
                lead < 0x80 ? 1 : (lead >> 5) == 0x6 ? 2 : (lead >> 4) == 0xE ? 3 : 4;
            units += width == 4 ? 2 : 1;  // 补充平面一个字算两个单元
            i += width;
        }
        lengths.push_back(units);
        if (at == std::string::npos) return lengths;
        from = at + 1;
    }
}

// 段长列表写成一行，失败信息里直接看出是哪一段变了。
std::string describe_lengths(const std::vector<std::size_t>& lengths) {
    std::string out;
    for (std::size_t i = 0; i < lengths.size(); ++i) {
        if (i != 0) out.push_back('/');
        out += std::to_string(lengths[i]);
    }
    return out;
}

}  // namespace

TEST_CASE("视觉小说排版：用例表里的每一份都与基准逐字节一致") {
    const std::vector<VnCase> cases = load_cases();
    REQUIRE_MESSAGE(!cases.empty(), "用例表是空的");

    for (const VnCase& item : cases) {
        CAPTURE(item.name);
        const std::string text = read_bytes(fixture("inputs/" + item.input));
        const std::string want = read_bytes(fixture(item.name + ".expected.txt"));

        VnOptions options;
        options.do_reformat = item.do_reformat;
        options.do_fix_punct = item.do_fix_punct;
        options.max_paragraph = item.max_paragraph;
        options.characters = item.characters;
        options.route_names = item.route_names;
        options.scene_pattern = item.scene_pattern;

        const std::string got = reformat_vn(text, options);
        const bool same = got == want;
        INFO(diff_note(want, got));
        CHECK(same);
    }
}

TEST_CASE("视觉小说排版：每段上限必须大于 0") {
    VnOptions options;
    options.max_paragraph = 0;
    CHECK_THROWS_AS(reformat_vn("「早上好。」", options), std::invalid_argument);
}

// 切段时的一处边界：能切的最远位置是「上限减一」字，正好落满上限的那一刀不切，
// 于是会退回到前面那个句号。下面两条把这个边界的两侧各钉一次。
TEST_CASE("剧本排版切段：顶到每段上限的那一位不参与找切口") {
    // 392 个字后一个句号（切在它后面是 393 字），接着 56 个字后又一个句号，
    // 那个句号是第 450 个字，切在它后面就是 450 字，正好落满上限，不算数。
    // 于是这一段切在 393，剩下 358 字一段。
    const std::string text = repeat_text("甲", 392) + "。" + repeat_text("乙", 56) + "。" +
                             repeat_text("丙", 300) + "。";
    REQUIRE_MESSAGE(describe_lengths(paragraph_lengths(text)) == "751",
                    "稿件不是 751 字：" << describe_lengths(paragraph_lengths(text)));

    VnOptions options;
    const std::string got = describe_lengths(paragraph_lengths(reformat_vn(text, options)));
    CHECK_MESSAGE(got == "393/0/358", "段落字数：" << got);
}

TEST_CASE("剧本排版切段：切口落在上限前一位时照切") {
    // 第 449 个字是句号，切在它后面正好是 449 字，还在窗口里，照切。
    // 这一条守住窗口收窄之后不会连合法的最长切口一起排掉。
    const std::string text = repeat_text("甲", 448) + "。" + repeat_text("乙", 100) + "。";

    VnOptions options;
    const std::string got = describe_lengths(paragraph_lengths(reformat_vn(text, options)));
    CHECK_MESSAGE(got == "449/0/101", "段落字数：" << got);
}

namespace {

// 一份稿按 chunk 字节一块喂进排版管线，结果攒成一份交回。
std::string vn_in_chunks(const std::string& text, const VnOptions& options, std::size_t chunk) {
    std::string result;
    texttool::VnPipeline pipeline(options, [&result](std::string_view bytes) { result.append(bytes); });
    for (std::size_t at = 0; at < text.size(); at += chunk) {
        const std::size_t take = std::min(chunk, text.size() - at);
        pipeline.feed(std::string_view(text).substr(at, take));
    }
    pipeline.finish();
    return result;
}

// 测试自己造一份稿，跑完删掉。
class TempDoc {
public:
    TempDoc(const std::string& name, const std::string& bytes) {
        path_ = std::filesystem::temp_directory_path() / ("texttool-vn-" + name);
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
    std::string target() const {
        return (path_.parent_path() / ("out-" + path_.filename().string())).string();
    }

private:
    std::filesystem::path path_;
};

VnOptions options_of(const VnCase& item) {
    VnOptions options;
    options.do_reformat = item.do_reformat;
    options.do_fix_punct = item.do_fix_punct;
    options.max_paragraph = item.max_paragraph;
    options.characters = item.characters;
    options.route_names = item.route_names;
    options.scene_pattern = item.scene_pattern;
    return options;
}

}  // namespace

TEST_CASE("剧本排版按块喂与整份喂等价：块切在哪里都不影响结果") {
    // 从盘上逐段读就是这条路：读一块排一块。块边界可能落在任何字节上：一行中间、
    // 一个字的中间。结果必须与一次喂完整份逐字节相同。
    int checked = 0;
    for (const VnCase& item : load_cases()) {
        CAPTURE(item.name);
        const std::string text = read_bytes(fixture("inputs/" + item.input));
        const VnOptions options = options_of(item);
        const std::string whole = reformat_vn(text, options);
        for (const std::size_t chunk : {std::size_t{1}, std::size_t{2}, std::size_t{3},
                                        std::size_t{7}, std::size_t{64}, std::size_t{4096}}) {
            CHECK_MESSAGE(vn_in_chunks(text, options, chunk) == whole,
                          "按 " << chunk << " 字节一块时与整份不一致");
            ++checked;
        }
    }
    MESSAGE("[剧本排版分块] 比了 " << checked << " 次");
    CHECK(checked > 0);
}

TEST_CASE("剧本排版按块喂：末尾无换行、空稿、只有换行、\\r\\n 都与整份喂一样") {
    const std::vector<std::string> docs = {
        "还没有换行的一句话，作者写得很热闹却意犹未尽",
        "",
        "\n",
        "\n\n\n",
        "甲，乙\r\n丙。\r\n\r\n丁，戊。\r\n",
        "「早上好。」\n\n他说，天亮了。\n",
        "没有结尾标点的一句话",
    };
    VnOptions options;  // 默认：排版加补标点
    for (const std::string& text : docs) {
        CAPTURE(text.size());
        const std::string whole = reformat_vn(text, options);
        for (const std::size_t chunk : {std::size_t{1}, std::size_t{3}, std::size_t{64}})
            CHECK(vn_in_chunks(text, options, chunk) == whole);
    }
}

TEST_CASE("剧本排版写盘：逐段写出来的字节与整份排完再写的一样") {
    const std::vector<std::pair<std::string, std::string>> docs = {
        {"utf8.txt", "「早上好。」\n\n他说，天亮了。没有结尾标点\n"},
        {"bom.txt", "\xEF\xBB\xBF" "「早上好。」\n"},
        {"gbk.txt", "\xA1\xB8\xD4\xE7\xC9\xCF\xBA\xC3\xA1\xA3\xA1\xB9\n"},  // GBK 的「早上好。」
        {"empty.txt", ""},
    };

    for (const auto& [name, bytes] : docs) {
        CAPTURE(name);
        VnOptions options;
        TempDoc doc(name, bytes);

        const texttool::LoadedText loaded = texttool::load_text(doc.path());
        const std::string whole = reformat_vn(loaded.text, options);
        texttool::write_text(doc.target() + "-whole", whole, loaded.encoding);

        const texttool::TextEncoding written = texttool::reformat_vn_file(doc.path(), doc.target(), options);
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
