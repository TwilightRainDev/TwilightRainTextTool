// 配置读取的测试：替换规则、替换方案、角色方案三份文件的读取与合并。
//
// 用例不碰仓库里的配置文件：每一份都在临时目录里现写、用完删掉，靠环境变量指过去，
// 程序目录里有没有配置文件都不影响结果。
#include "doctest/doctest.h"

#include <cstdlib>
#include <filesystem>
#include <fstream>
#include <stdexcept>
#include <string>
#include <vector>

#ifdef _WIN32
#include <process.h>
#else
#include <unistd.h>
#endif

#include "engine/schemes.h"

namespace {

namespace fs = std::filesystem;

int current_process_id() {
#ifdef _WIN32
    return _getpid();
#else
    return static_cast<int>(::getpid());
#endif
}

// 临时目录：构造时建，析构时连里面的一起删。
class TempDir {
public:
    TempDir() {
        static int counter = 0;
        path_ = fs::temp_directory_path() /
                ("texttool_schemes_" + std::to_string(++counter) + "_" +
                 std::to_string(current_process_id()));
        std::error_code ignored;
        fs::remove_all(path_, ignored);
        fs::create_directories(path_);
    }
    ~TempDir() {
        std::error_code ignored;
        fs::remove_all(path_, ignored);
    }
    TempDir(const TempDir&) = delete;
    TempDir& operator=(const TempDir&) = delete;

    const fs::path& path() const { return path_; }

    // 写一份文件，返回它的路径。
    fs::path write(const std::string& name, const std::string& bytes) const {
        const fs::path file = path_ / name;
        std::ofstream out(file, std::ios::binary | std::ios::trunc);
        out.write(bytes.data(), static_cast<std::streamsize>(bytes.size()));
        out.close();
        return file;
    }

private:
    fs::path path_;
};

// 环境变量：指到临时文件上，测试结束后清掉。
class EnvVar {
public:
    EnvVar(const char* name, const std::string& value) : name_(name) {
        set(value);
    }
    ~EnvVar() { clear(); }
    EnvVar(const EnvVar&) = delete;
    EnvVar& operator=(const EnvVar&) = delete;

    void set(const std::string& value) const {
#ifdef _WIN32
        _putenv_s(name_.c_str(), value.c_str());
#else
        ::setenv(name_.c_str(), value.c_str(), 1);
#endif
    }
    void clear() const {
#ifdef _WIN32
        _putenv_s(name_.c_str(), "");
#else
        ::unsetenv(name_.c_str());
#endif
    }

private:
    std::string name_;
};

}  // namespace

TEST_CASE("替换规则：按 Find 与 Replace 读出来") {
    const TempDir dir;
    const fs::path file = dir.write("replace_rules.json",
                                    R"([{"Find": ",", "Replace": "，"}, {"Find": "...", "Replace": "……"}])");
    const EnvVar env("TEXTTOOL_REPLACE_RULES_JSON", file.string());

    std::vector<texttool::ReplaceRule> rules;
    std::string error;
    REQUIRE(texttool::load_replace_rules(nullptr, rules, error));
    CHECK(error.empty());
    REQUIRE(rules.size() == 2);
    CHECK(rules[0].first == ",");
    CHECK(rules[0].second == "，");
    CHECK(rules[1].first == "...");
    CHECK(rules[1].second == "……");
}

TEST_CASE("替换规则：文件不在就是空表，不算错") {
    const TempDir dir;
    const EnvVar env("TEXTTOOL_REPLACE_RULES_JSON", (dir.path() / "没有这个文件.json").string());

    std::vector<texttool::ReplaceRule> rules;
    std::string error;
    CHECK(texttool::load_replace_rules(nullptr, rules, error));
    CHECK(error.empty());
    CHECK(rules.empty());
}

TEST_CASE("替换规则：文件坏了就报损坏，不返回半份表") {
    const TempDir dir;
    const fs::path file = dir.write("replace_rules.json", R"([{"Find": ",", )");
    const EnvVar env("TEXTTOOL_REPLACE_RULES_JSON", file.string());

    std::vector<texttool::ReplaceRule> rules;
    std::string error;
    CHECK_FALSE(texttool::load_replace_rules(nullptr, rules, error));
    CHECK(error.find("配置文件损坏：replace_rules.json") != std::string::npos);
    CHECK(rules.empty());
}

TEST_CASE("替换规则：顶层不是数组也算损坏") {
    const TempDir dir;
    const fs::path file = dir.write("replace_rules.json", R"({"Find": ","})");
    const EnvVar env("TEXTTOOL_REPLACE_RULES_JSON", file.string());

    std::vector<texttool::ReplaceRule> rules;
    std::string error;
    CHECK_FALSE(texttool::load_replace_rules(nullptr, rules, error));
    CHECK(error.find("无法解析") != std::string::npos);
}

TEST_CASE("替换方案：按名字取到那一条，取不到就是空") {
    const TempDir dir;
    const fs::path file = dir.write(
        "replace_schemes.json",
        R"([{"Name": "第一套", "Rules": [{"Find": ",", "Replace": "，"}]},
            {"Name": "第二套", "Rules": [{"Find": ".", "Replace": "。"}]}])");
    const EnvVar env("TEXTTOOL_SCHEMES_JSON", file.string());

    std::vector<texttool::ReplaceScheme> schemes;
    std::string error;
    REQUIRE(texttool::load_replace_schemes(nullptr, schemes, error));
    REQUIRE(schemes.size() == 2);
    CHECK(schemes[0].name == "第一套");
    CHECK(schemes[1].name == "第二套");

    const texttool::ReplaceScheme* found = texttool::find_replace_scheme(schemes, "第二套");
    REQUIRE(found != nullptr);
    REQUIRE(found->rules.size() == 1);
    CHECK(found->rules[0].second == "。");
    CHECK(texttool::find_replace_scheme(schemes, "没有这一套") == nullptr);
}

TEST_CASE("角色方案：读角色名、路线名与场景标记") {
    const TempDir dir;
    const fs::path file = dir.write(
        "vn_schemes.json",
        R"([{"Name": "甲", "Characters": ["小满", "阿甲"], "RouteNames": ["春"], "ScenePattern": "── 春 ──"},
            {"Name": "乙", "Characters": ["阿乙"], "RouteNames": [], "ScenePattern": "   "},
            {"Name": "丙"}])");
    const EnvVar env("TEXTTOOL_VN_SCHEMES_JSON", file.string());

    std::vector<texttool::VnScheme> schemes;
    std::string error;
    REQUIRE(texttool::load_vn_schemes(nullptr, schemes, error));
    REQUIRE(schemes.size() == 3);
    CHECK(schemes[0].characters.size() == 2);
    CHECK(schemes[0].route_names.size() == 1);
    CHECK(schemes[0].scene_pattern == "── 春 ──");
    CHECK(schemes[2].characters.empty());
    CHECK(schemes[2].scene_pattern.empty());
}

TEST_CASE("角色方案并起来：角色名与路线名接着排，场景标记用竖线连成一条") {
    const std::vector<texttool::VnScheme> schemes = {
        {"甲", {"小满", "阿甲"}, {"春"}, "── 春 ──", "", false},
        {"乙", {"阿乙"}, {"夏"}, "── 夏 ──", "", false},
        {"丙", {"阿丙"}, {}, "   ", "", false},  // 场景标记是空白：这一份不参与拼接
    };

    texttool::VnOptions options;
    std::string error;
    REQUIRE(texttool::combine_vn_schemes(schemes, {"甲", "乙", "丙"}, options, error));
    CHECK(error.empty());
    CHECK(options.characters == std::vector<std::string>{"小满", "阿甲", "阿乙", "阿丙"});
    CHECK(options.route_names == std::vector<std::string>{"春", "夏"});
    CHECK(options.scene_pattern == "── 春 ──|── 夏 ──");
}

TEST_CASE("角色方案并起来：一份都没点名时场景标记留空，由引擎按通用写法认") {
    const std::vector<texttool::VnScheme> schemes = {{"甲", {"小满"}, {}, "", "", false}};

    texttool::VnOptions options;
    std::string error;
    REQUIRE(texttool::combine_vn_schemes(schemes, {"甲"}, options, error));
    CHECK(options.characters.size() == 1);
    CHECK(options.scene_pattern.empty());
}

TEST_CASE("角色方案并起来：名字找不到就说未找到方案") {
    const std::vector<texttool::VnScheme> schemes = {{"甲", {"小满"}, {}, "", "", false}};

    texttool::VnOptions options;
    std::string error;
    CHECK_FALSE(texttool::combine_vn_schemes(schemes, {"乙"}, options, error));
    CHECK(error == "未找到方案：乙");
}

TEST_CASE("程序目录：拿不到程序路径时退回当前工作目录") {
    CHECK(texttool::program_dir(nullptr) == fs::current_path());
}

TEST_CASE("角色方案：存下去再读回来，说明与内置标记一个不丢") {
    const TempDir dir;
    const fs::path file = dir.path() / "vn_schemes.json";
    const EnvVar env("TEXTTOOL_VN_SCHEMES_JSON", file.string());

    texttool::VnScheme first;
    first.name = "甲";
    first.description = "春之篇的全角色";
    first.is_built_in = true;
    first.characters = {"小满", "阿甲"};
    first.route_names = {"春"};
    first.scene_pattern = "── 春 ──";

    texttool::VnScheme second;
    second.name = "乙";
    second.characters = {"阿乙"};

    texttool::save_vn_schemes(nullptr, {first, second});

    std::vector<texttool::VnScheme> schemes;
    std::string error;
    REQUIRE(texttool::load_vn_schemes(nullptr, schemes, error));
    CHECK(error.empty());
    REQUIRE(schemes.size() == 2);

    CHECK(schemes[0].name == "甲");
    CHECK(schemes[0].description == "春之篇的全角色");
    CHECK(schemes[0].is_built_in);
    CHECK(schemes[0].characters == std::vector<std::string>{"小满", "阿甲"});
    CHECK(schemes[0].route_names == std::vector<std::string>{"春"});
    CHECK(schemes[0].scene_pattern == "── 春 ──");

    CHECK(schemes[1].name == "乙");
    CHECK(schemes[1].description.empty());
    CHECK_FALSE(schemes[1].is_built_in);
    CHECK(schemes[1].route_names.empty());
    CHECK(schemes[1].scene_pattern.empty());
}

TEST_CASE("角色方案：文件里缺说明与内置标记时按空说明、不是内置算") {
    const TempDir dir;
    const fs::path file = dir.write("vn_schemes.json",
                                     R"([{"Name": "甲", "Characters": ["小满"]},
                                         {"Name": "乙", "IsBuiltIn": "是"} ])");
    const EnvVar env("TEXTTOOL_VN_SCHEMES_JSON", file.string());

    std::vector<texttool::VnScheme> schemes;
    std::string error;
    REQUIRE(texttool::load_vn_schemes(nullptr, schemes, error));
    REQUIRE(schemes.size() == 2);
    CHECK(schemes[0].description.empty());
    CHECK_FALSE(schemes[0].is_built_in);
    // 写的不是 true/false（这里是「是」）也按不是内置算，不报错：旧文件不该因为这个读不进来。
    CHECK(schemes[1].description.empty());
    CHECK_FALSE(schemes[1].is_built_in);
}

TEST_CASE("角色方案：盘上写着是内置的那一份不许删") {
    const TempDir dir;
    const fs::path file = dir.write(
        "vn_schemes.json",
        R"([{"Name": "甲", "IsBuiltIn": true, "Characters": ["小满"]},
            {"Name": "乙", "Characters": ["阿乙"]}])");
    const EnvVar env("TEXTTOOL_VN_SCHEMES_JSON", file.string());

    std::ifstream before_stream(file, std::ios::binary);
    const std::string before((std::istreambuf_iterator<char>(before_stream)),
                             std::istreambuf_iterator<char>());

    texttool::VnScheme kept;
    kept.name = "乙";
    CHECK_THROWS_AS(texttool::save_vn_schemes(nullptr, {kept}), std::runtime_error);

    // 拒绝的时候盘上一个字没动。
    std::ifstream after_stream(file, std::ios::binary);
    const std::string after((std::istreambuf_iterator<char>(after_stream)),
                            std::istreambuf_iterator<char>());
    // Windows 上目标文件还被读句柄占着时，改名替换会被系统挡下（共享冲突）。
    // 后面那次保存要覆盖这一份，先把两个读句柄放掉，与真实使用「读完再存」的顺序一致。
    before_stream.close();
    after_stream.close();
    CHECK(after == before);

    // 内置那一份留着就能存。
    texttool::VnScheme built_in;
    built_in.name = "甲";
    built_in.is_built_in = true;
    built_in.characters = {"小满"};
    CHECK_NOTHROW(texttool::save_vn_schemes(nullptr, {built_in, kept}));
}

TEST_CASE("按行挑：只并勾中的那几行，场景标记仍然连成一条") {
    const std::vector<texttool::VnScheme> schemes = {
        {"甲", {"小满", "阿甲", "阿丙"}, {"春", "夏"}, "── 春 ──", "", false},
        {"乙", {"阿乙"}, {}, "── 夏 ──", "", false},
        {"丙", {"阿丁"}, {}, "", "", false},
    };

    texttool::VnOptions options;
    std::string error;

    texttool::VnSchemePick first;
    first.scheme = 0;
    first.characters = {0, 2};  // 小满与阿丙，跳过了中间的阿甲
    first.route_names = {1};    // 夏

    texttool::VnSchemePick second;
    second.scheme = 1;
    second.characters = {0};

    REQUIRE(texttool::combine_vn_scheme_picks(schemes, {first, second}, options, error));
    CHECK(error.empty());
    CHECK(options.characters == std::vector<std::string>{"小满", "阿丙", "阿乙"});
    CHECK(options.route_names == std::vector<std::string>{"夏"});
    CHECK(options.scene_pattern == "── 春 ──|── 夏 ──");
}

TEST_CASE("按行挑：一份里一行都没勾时只算上它的场景标记") {
    const std::vector<texttool::VnScheme> schemes = {{"甲", {}, {}, "── 春 ──", "", false}};

    texttool::VnOptions options;
    std::string error;
    const texttool::VnSchemePick only_patterns;
    REQUIRE(texttool::combine_vn_scheme_picks(schemes, {only_patterns}, options, error));
    CHECK(options.characters.empty());
    CHECK(options.route_names.empty());
    CHECK(options.scene_pattern == "── 春 ──");
}

TEST_CASE("按行挑：行号对不上时说明角色表改过了") {
    const std::vector<texttool::VnScheme> schemes = {{"甲", {"小满"}, {"春"}, "", "", false}};

    texttool::VnOptions options;
    std::string error;

    texttool::VnSchemePick too_many_characters;
    too_many_characters.characters = {1};
    CHECK_FALSE(texttool::combine_vn_scheme_picks(schemes, {too_many_characters}, options, error));
    CHECK(error == "角色表改过了：请再挑一次");

    texttool::VnSchemePick too_many_routes;
    too_many_routes.route_names = {1};
    CHECK_FALSE(texttool::combine_vn_scheme_picks(schemes, {too_many_routes}, options, error));
    CHECK(error == "角色表改过了：请再挑一次");

    texttool::VnSchemePick unknown_scheme;
    unknown_scheme.scheme = 3;
    CHECK_FALSE(texttool::combine_vn_scheme_picks(schemes, {unknown_scheme}, options, error));
    CHECK(error == "角色表改过了：请再挑一次");
}

TEST_CASE("按行挑：整份全勾与按名字点这份方案并出来的结果一样") {
    const std::vector<texttool::VnScheme> schemes = {
        {"甲", {"小满", "阿甲"}, {"春"}, "── 春 ──", "说明", true},
        {"乙", {"阿乙"}, {"夏"}, "", "", false},
    };

    texttool::VnOptions by_names;
    texttool::VnOptions by_lines;
    std::string error;
    REQUIRE(texttool::combine_vn_schemes(schemes, {"乙", "甲"}, by_names, error));

    texttool::VnSchemePick second;
    second.scheme = 1;
    second.characters = {0};
    second.route_names = {0};
    texttool::VnSchemePick first;
    first.scheme = 0;
    first.characters = {0, 1};
    first.route_names = {0};
    REQUIRE(texttool::combine_vn_scheme_picks(schemes, {second, first}, by_lines, error));

    CHECK(by_lines.characters == by_names.characters);
    CHECK(by_lines.route_names == by_names.route_names);
    CHECK(by_lines.scene_pattern == by_names.scene_pattern);
}
