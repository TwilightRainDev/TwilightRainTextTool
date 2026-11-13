// 目录展开的测试：收哪些后缀、按什么次序、软链接怎么算一份、读不了的目录怎么报。
//
// 样本在临时目录里现造：目录树、隐藏的子目录、指回上级的链接、断了的那条、
// 后缀大写的那份、不是稿子的那份。跟命令行收目录是同一个函数。
#include <chrono>
#include <filesystem>
#include <fstream>
#include <stdexcept>
#include <string>
#include <vector>

#include "doctest/doctest.h"
#include "engine/lint_inputs.h"

namespace {

namespace fs = std::filesystem;

// 一条测试一棵树：建在临时目录里，析构时整棵删掉。
class TempTree {
public:
    TempTree() {
        static int counter = 0;
        const auto stamp = std::chrono::steady_clock::now().time_since_epoch().count();
        root_ = fs::temp_directory_path() /
                ("texttool-lint-inputs-" + std::to_string(stamp) + "-" +
                 std::to_string(++counter));
        std::error_code ignored;
        fs::remove_all(root_, ignored);
        fs::create_directories(root_);
    }
    ~TempTree() {
        std::error_code ignored;
        // 权限改回来才删得掉
        fs::permissions(root_, fs::perms::owner_all, fs::perm_options::add, ignored);
        fs::remove_all(root_, ignored);
    }
    TempTree(const TempTree&) = delete;
    TempTree& operator=(const TempTree&) = delete;

    std::string root() const { return root_.string(); }

    std::string file(const std::string& relative, const std::string& text = "正文\n") const {
        const fs::path path = root_ / relative;
        std::error_code ignored;
        fs::create_directories(path.parent_path(), ignored);
        std::ofstream out(path, std::ios::binary);
        out << text;
        return path.string();
    }

    std::string directory(const std::string& relative) const {
        const fs::path path = root_ / relative;
        std::error_code ignored;
        fs::create_directories(path, ignored);
        return path.string();
    }

    bool link(const std::string& target, const std::string& relative) const {
        std::error_code ec;
        fs::create_symlink(target, root_ / relative, ec);
        return !ec;
    }

private:
    fs::path root_;
};

// 相对路径写成 / 分隔，比较时不必关心跑在哪台机器上。
std::string relative_to(const std::string& root, const std::string& path) {
    return fs::path(path).lexically_relative(fs::path(root)).generic_string();
}

}  // namespace

TEST_CASE("目录展开：只收 .txt 与 .md，按相对路径排序，软链接只算一份") {
#if defined(_WIN32)
    return;  // Windows 上建软链接要开发者模式或管理员权限，这一条在那边跳过
#endif
    TempTree tree;
    tree.file("a_first.txt");
    tree.file("b_upper.MD");
    tree.file("sub/c_nested.txt");
    tree.file("zzz/d_last.md");
    tree.file("notes.rst");
    REQUIRE(tree.link("../a_first.txt", "sub/link_dup.txt"));
    REQUIRE(tree.link(".", "loop"));        // 指回上级的链接
    REQUIRE(tree.link("nowhere.txt", "dangling.txt"));  // 断掉的软链接：当稿件收，读的时候才报

    const std::vector<std::string> files =
        texttool::collect_lint_inputs({tree.root()}).files;
    std::vector<std::string> shown;
    for (const std::string& file : files) shown.push_back(relative_to(tree.root(), file));

    // 相对路径排序：a_first.txt、b_upper.MD、dangling.txt、sub/c_nested.txt、zzz/d_last.md。
    // sub/link_dup.txt 与 a_first.txt 是同一份稿，按真实路径只留先出现的那一条。
    CHECK(shown == std::vector<std::string>{"a_first.txt", "b_upper.MD", "dangling.txt",
                                            "sub/c_nested.txt", "zzz/d_last.md"});
}

TEST_CASE("目录展开：同一个目录给两次、真身与软链接各给一次，都只检一次") {
#if defined(_WIN32)
    return;  // 同上：这一条靠软链接区分真身与别名
#endif
    TempTree tree;
    const std::string file = tree.file("one.txt");
    REQUIRE(tree.link(file, "alias.txt"));
    // 同一个目录内部按相对路径排，alias.txt 在 one.txt 前面，留下的就是它
    const std::string alias = (fs::path(tree.root()) / "alias.txt").generic_string();

    const std::vector<std::string> files =
        texttool::collect_lint_inputs({tree.root(), tree.root(), file}).files;
    REQUIRE(files.size() == 1);
    CHECK(files.front() == alias);
}

TEST_CASE("目录展开：一个 .txt 或 .md 都收不到时点名那个目录") {
    TempTree tree;
    tree.file("notes.rst");
    tree.file("sub/readme");

    CHECK_THROWS_WITH_AS(texttool::collect_lint_inputs({tree.root()}),
                         ("目录下没有 .txt 或 .md 文件：" + tree.root()).c_str(),
                         std::runtime_error);
}

TEST_CASE("目录展开：读不了的子目录点名报出来，其余照收") {
#if defined(_WIN32)
    return;  // Windows 上改权限位造不出「读不了的目录」，这一条在那边跳过
#endif
    TempTree tree;
    tree.file("keep.txt");
    const std::string secret = fs::path(tree.directory("secret")).generic_string();
    tree.file("secret/hidden.txt");

    std::error_code ec;
    fs::permissions(secret, fs::perms::none, fs::perm_options::replace, ec);
    if (ec) return;  // 这台机器改不了权限（例如以管理员身份跑），这一条跳过

    std::vector<std::string> warnings;
    const texttool::LintInputFiles collected = texttool::collect_lint_inputs(
        {tree.root()},
        [](const std::string& message, void* context) {
            static_cast<std::vector<std::string>*>(context)->push_back(message);
        },
        &warnings);
    fs::permissions(secret, fs::perms::owner_all, fs::perm_options::replace, ec);

    CHECK(collected.files.size() == 1);
    CHECK(collected.unreadable == 1);
    REQUIRE(warnings.size() == 1);
    CHECK(warnings.front().find("读不了目录，跳过：" + secret) == 0);
    CHECK(warnings.front().find("没有访问权限") != std::string::npos);
}

TEST_CASE("目录展开：文件与 - 按输入顺序进清单，同名文件只收一次") {
    TempTree tree;
    const std::string folder = tree.directory("empty");
    (void)folder;

    const std::string file = tree.file("one.md");
    const std::vector<std::string> files =
        texttool::collect_lint_inputs({"-", file, file, "no_such_file.md"}).files;
    REQUIRE(files.size() == 3);
    CHECK(files[0] == "-");
    CHECK(files[1] == file);
    CHECK(files[2] == "no_such_file.md");
}
