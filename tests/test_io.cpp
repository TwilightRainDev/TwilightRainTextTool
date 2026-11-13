// 读稿接口的测试：只看文件开头判编码的那一条，与读整份稿判出来的是不是同一个编码，
// 以及文件读不了时两边报的是不是同一句。
//
// 字节序列写死成十六进制，不靠实现算出来。中 = U+4E2D，文 = U+6587；
// UTF-8 是 E4 B8 AD E6 96 87，GBK 是 D6 D0 CE C4。
#include <filesystem>
#include <fstream>
#include <stdexcept>
#include <string>

#include "doctest/doctest.h"
#include "engine/encoding.h"
#include "engine/io.h"

namespace {

namespace fs = std::filesystem;

using texttool::TextEncoding;

// 测试自己造一份稿，跑完删掉。
class TempFile {
public:
    TempFile(const std::string& name, const std::string& bytes) {
        path_ = fs::temp_directory_path() / ("texttool-io-" + name);
        std::ofstream out(path_, std::ios::binary);
        out.write(bytes.data(), static_cast<std::streamsize>(bytes.size()));
    }
    ~TempFile() {
        std::error_code ignored;
        fs::remove(path_, ignored);
    }
    TempFile(const TempFile&) = delete;
    TempFile& operator=(const TempFile&) = delete;

    std::string path() const { return path_.string(); }

private:
    fs::path path_;
};

const std::string kUtf8Han = "\xE4\xB8\xAD\xE6\x96\x87";
const std::string kGbkHan = "\xD6\xD0\xCE\xC4";
const std::string kUtf8BomHan = "\xEF\xBB\xBF\xE4\xB8\xAD\xE6\x96\x87";
const std::string kUtf16LeBomHan = "\xFF\xFE\x2D\x4E\x87\x65";  // U+FEFF + 中文
const std::string kUtf16BeBomHan = "\xFE\xFF\x4E\x2D\x65\x87";

}  // namespace

TEST_CASE("只看开头判编码：与读整份稿判出来的一致") {
    const auto check = [](const std::string& name, const std::string& bytes, TextEncoding want) {
        TempFile file(name, bytes);
        CHECK(texttool::probe_file_encoding(file.path()) == want);
        CHECK(texttool::load_text(file.path()).encoding == want);
    };

    check("utf8.txt", kUtf8Han, TextEncoding::Utf8);
    check("utf8-bom.txt", kUtf8BomHan, TextEncoding::Utf8Bom);
    check("utf16-le.txt", kUtf16LeBomHan, TextEncoding::Utf16Le);
    check("utf16-be.txt", kUtf16BeBomHan, TextEncoding::Utf16Be);
    check("gbk.txt", kGbkHan, TextEncoding::Gbk);
    check("empty.txt", std::string(), TextEncoding::Utf8);
    check("ascii.txt", std::string("abc\r\n"), TextEncoding::Utf8);
}

TEST_CASE("开头 4096 字节的切点落在半个字上，仍按 UTF-8") {
    // 前 4094 个字节是 ASCII，「中」的三个字节从第 4094 个字节起，于是第 4096 个字节
    // 正好是它的第二个续字节：判开头时末尾那半个字不算错。判全篇也一样，整篇是合法 UTF-8。
    const std::string bytes = std::string(4094, 'a') + kUtf8Han + std::string(6000, 'b');
    REQUIRE(bytes.size() > 4096);
    TempFile file("cut-utf8.txt", bytes);
    CHECK(texttool::probe_file_encoding(file.path()) == TextEncoding::Utf8);
    CHECK(texttool::load_text(file.path()).encoding == TextEncoding::Utf8);
}

TEST_CASE("GBK 稿开头切在半个字上，仍按 GBK") {
    // 同样是 4094 个 ASCII 后面接 GBK 的双字节字：第 4096 个字节落在它的第二个字节上。
    const std::string bytes = std::string(4094, 'a') + kGbkHan + std::string(6000, 'b');
    REQUIRE(bytes.size() > 4096);
    TempFile file("cut-gbk.txt", bytes);
    CHECK(texttool::probe_file_encoding(file.path()) == TextEncoding::Gbk);
    CHECK(texttool::load_text(file.path()).encoding == TextEncoding::Gbk);
}

TEST_CASE("开头是 UTF-8、后面有坏字节：只看开头给 Utf8，读整份稿给 Gbk") {
    // 这是两条判法的已知差别：看开头的不再往下复验。界面显示编码用前者，写回用后者判出来的编码。
    const std::string bytes = kUtf8Han + std::string(5000, 'a') + "\xFF";
    TempFile file("head-only.txt", bytes);
    CHECK(texttool::probe_file_encoding(file.path()) == TextEncoding::Utf8);
    CHECK(texttool::load_text(file.path()).encoding == TextEncoding::Gbk);
}

TEST_CASE("文件不存在时与读整份稿报同一句") {
    const std::string path = (fs::temp_directory_path() / "texttool-io-missing.txt").string();
    std::error_code ignored;
    fs::remove(path, ignored);

    const auto message_of = [](const auto& call) {
        try {
            call();
        } catch (const std::runtime_error& error) {
            return std::string(error.what());
        }
        return std::string();
    };

    const std::string probe_message =
        message_of([&path]() { texttool::probe_file_encoding(path); });
    const std::string load_message = message_of([&path]() { texttool::load_text(path); });
    CHECK(probe_message == "文件不存在：" + path);
    CHECK(load_message == probe_message);
}

// ---------------------------------------------------------------- 共用的字符串小工具

TEST_CASE("去掉首尾空白只认空格、制表符、回车、换行") {
    CHECK(texttool::trim_whitespace("  a b \t\r\n") == "a b");
    CHECK(texttool::trim_whitespace("\t\r\n  ") == "");
    CHECK(texttool::trim_whitespace("a\u3000b") == "a\u3000b");  // 全角空格不算
    CHECK(texttool::trim_whitespace("") == "");
}

TEST_CASE("取路径最后一段：命令行的切法与界面的切法分开") {
    // 命令行只按 / 切：Linux 上反斜杠是普通字符，切出来的名字才对得上
    CHECK(texttool::file_name_of("a/b/c.txt") == "c.txt");
    CHECK(texttool::file_name_of("c.txt") == "c.txt");
    CHECK(texttool::file_name_of("dir\\c.txt") == "dir\\c.txt");
    CHECK(texttool::file_name_of("a/b/") == "");
    // 界面里可能收到 Windows 写法，反斜杠也当分隔符
    CHECK(texttool::file_name_of_any("dir\\c.txt") == "c.txt");
    CHECK(texttool::file_name_of_any("a/b\\c.txt") == "c.txt");
    CHECK(texttool::file_name_of_any("c.txt") == "c.txt");
}

// ---------------------------------------------------------------- 目录展开

TEST_CASE("递归收目录：按路径排序、只收指定的后缀、软链接只算一份") {
    const std::string root = (fs::temp_directory_path() / "texttool-scan-case").generic_string();
    std::error_code ignored;
    fs::remove_all(root, ignored);
    fs::create_directories(root + "/sub/nested");
    const auto put = [](const std::string& path) {
        std::ofstream out(path, std::ios::binary);
        out << "稿子";
    };
    put(root + "/b.txt");
    put(root + "/a.txt");
    put(root + "/a.md");
    put(root + "/c.dat");       // 后缀不在名单上，不收
    put(root + "/sub/d.txt");
    put(root + "/sub/nested/e.md");

    // 软链接：一条指向同一份稿（只算一份），一条指回上级（不能绕圈）
    bool linked = true;
    fs::create_symlink(root + "/a.txt", root + "/zz-link.txt", ignored);
    if (ignored) linked = false;
    ignored.clear();
    fs::create_directory_symlink(root + "/sub", root + "/sub/up", ignored);
    if (ignored) linked = false;

    const texttool::DirectoryScan scan = texttool::scan_documents(root, {".txt", ".md"});
    CHECK(scan.unreadable.empty());
    REQUIRE(scan.files.size() == 5);
    // 按路径排序，子目录里的排在后面
    CHECK(scan.files[0] == root + "/a.md");
    CHECK(scan.files[1] == root + "/a.txt");
    CHECK(scan.files[2] == root + "/b.txt");
    CHECK(scan.files[3] == root + "/sub/d.txt");
    CHECK(scan.files[4] == root + "/sub/nested/e.md");
    // 软链接建得出来时：指向 a.txt 的那一条不算第二份，指回上级的链接也没有多收
    if (linked) {
        for (const std::string& file : scan.files)
            CHECK(file.find("zz-link.txt") == std::string::npos);
    }

    fs::remove_all(root, ignored);
}

TEST_CASE("目录展开：路径不是目录时收不到稿子，并把这个路径报给上层") {
    const std::string path = (fs::temp_directory_path() / "texttool-scan-missing").string();
    std::error_code ignored;
    fs::remove_all(path, ignored);

    const texttool::DirectoryScan scan = texttool::scan_documents(path, {".txt"});
    CHECK(scan.files.empty());
    REQUIRE(scan.unreadable.size() == 1);
    CHECK(scan.unreadable[0] == path);
}

// ---------------------------------------------------------------- 逐块读

TEST_CASE("逐块读与整份读给出同一份正文与同一个编码") {
    // 每一种编码都要过一遍：交出来的正文与 load_text 读到的逐字节相同，
    // 交出去的块都切在字的边界上，按块数出来的字数与按整份数的一模一样。
    const auto check = [](const std::string& name, const std::string& bytes, TextEncoding want) {
        TempFile file(name, bytes);
        const texttool::LoadedText whole = texttool::load_text(file.path());
        REQUIRE(whole.encoding == want);

        texttool::TextFileReader reader(file.path());
        CHECK(reader.encoding() == want);
        std::string joined;
        std::string_view chunk;
        while (reader.next(chunk)) joined.append(chunk);
        CHECK(joined == whole.text);
        CHECK(reader.text_chars() == texttool::utf8_char_count(whole.text));
    };

    check("chunk-utf8.txt", kUtf8Han, TextEncoding::Utf8);
    check("chunk-utf8-bom.txt", kUtf8BomHan, TextEncoding::Utf8Bom);
    check("chunk-utf16-le.txt", kUtf16LeBomHan, TextEncoding::Utf16Le);
    check("chunk-utf16-be.txt", kUtf16BeBomHan, TextEncoding::Utf16Be);
    check("chunk-gbk.txt", kGbkHan, TextEncoding::Gbk);
    check("chunk-empty.txt", std::string(), TextEncoding::Utf8);
}

TEST_CASE("逐块读：稿子比一块长、切点落在半个字上也不出错") {
    // 一块是 64KB。多字节字一直掺到块尾，切点总会落在某个字的中间。
    std::string bytes;
    for (int i = 0; i < 4000; ++i) {
        bytes += "第";
        bytes += std::to_string(i % 97);
        bytes += "段中文与 abc 混排，";
    }
    REQUIRE(bytes.size() > 64 * 1024);

    const auto check = [](const std::string& name, const std::string& body, TextEncoding want) {
        TempFile file(name, body);
        const texttool::LoadedText whole = texttool::load_text(file.path());
        REQUIRE(whole.encoding == want);

        texttool::TextFileReader reader(file.path());
        CHECK(reader.encoding() == want);
        std::string joined;
        std::string_view chunk;
        while (reader.next(chunk)) joined.append(chunk);
        CHECK(joined == whole.text);
        CHECK(reader.text_chars() == texttool::utf8_char_count(whole.text));
    };

    check("many-utf8.txt", bytes, TextEncoding::Utf8);

    // GBK 那一份：同样的正文换成国标码写盘，一个字两个字节
    check("many-gbk.txt", "\xD6\xD0\xCE\xC4" + std::string(70 * 1024, 'a'), TextEncoding::Gbk);
}

TEST_CASE("逐块读：末尾被切断的一个字按坏字节处理，与整份读一致") {
    // 头 4KB 之后有一个坏字节：整份判下来是 GBK，逐块读也得走 GBK，两条路的正文相同。
    const std::string bytes = kUtf8Han + std::string(5000, 'a') + "\xFF";
    TempFile file("chunk-bad.txt", bytes);
    const texttool::LoadedText whole = texttool::load_text(file.path());
    CHECK(whole.encoding == TextEncoding::Gbk);
    texttool::TextFileReader reader(file.path());
    CHECK(reader.encoding() == TextEncoding::Gbk);
    std::string joined;
    std::string_view chunk;
    while (reader.next(chunk)) joined.append(chunk);
    CHECK(joined == whole.text);
}

TEST_CASE("逐块读：文件不存在时与读整份稿报同一句") {
    const std::string path = (fs::temp_directory_path() / "texttool-io-missing-chunk.txt").string();
    std::error_code ignored;
    fs::remove(path, ignored);
    try {
        texttool::TextFileReader reader(path);
        FAIL("文件不存在时应该抛错");
    } catch (const std::runtime_error& error) {
        CHECK(std::string(error.what()) == "文件不存在：" + path);
    }
}
