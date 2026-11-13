// 配置包：拼装、读回来、报错说法、写回三份配置文件。
//
// 测试不碰仓库里的配置文件与用户自己的配置：三份文件的路径都靠环境变量指到临时目录，
// 用完删掉。报错说法也钉住几处关键的，免得以后有人把「哪一份、什么毛病」改成一句「失败」。
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

#include "engine/config_pack.h"
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
                ("texttool_config_pack_" + std::to_string(++counter) + "_" +
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

private:
    fs::path path_;
};

// 环境变量：指到临时文件上，测试结束后清掉。
class EnvVar {
public:
    EnvVar(const char* name, const std::string& value) : name_(name) { set(value); }
    ~EnvVar() { clear(); }
    EnvVar(const EnvVar&) = delete;
    EnvVar& operator=(const EnvVar&) = delete;

private:
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

    std::string name_;
};

// 三份配置文件都指到临时目录里的那一份。
struct TempConfigs {
    explicit TempConfigs(const TempDir& dir)
        : rules("TEXTTOOL_REPLACE_RULES_JSON", (dir.path() / "replace_rules.json").string()),
          schemes("TEXTTOOL_SCHEMES_JSON", (dir.path() / "replace_schemes.json").string()),
          vn("TEXTTOOL_VN_SCHEMES_JSON", (dir.path() / "vn_schemes.json").string()) {}

    EnvVar rules;
    EnvVar schemes;
    EnvVar vn;
};

// 一份内容齐全的包，几个测试拿它当起点。
texttool::ConfigPackage sample_package() {
    texttool::ConfigPackage package;
    package.replace_rules = {{",", "，"}, {"...", "……"}};
    package.replace_schemes = {{"网文", {{"「", "“"}, {"」", "”"}}}, {"公文", {{"...", "……"}}}};
    package.vn_schemes = {{"第一幕", {"小满", "阿甲"}, {"春"}, "── 春 ──", "", false},
                          {"第二幕", {"阿乙"}, {}, "", "", false}};
    package.appearance = "dark";
    return package;
}

}  // namespace

TEST_CASE("配置包：拼出来的能原样读回来") {
    const texttool::ConfigPackage original = sample_package();
    const std::string text = texttool::build_config_package(original);

    texttool::ConfigPackage read;
    std::string error;
    REQUIRE(texttool::read_config_package(text, read, error));
    CHECK(error.empty());
    CHECK(read.replace_rules == original.replace_rules);
    REQUIRE(read.replace_schemes.size() == 2);
    CHECK(read.replace_schemes[0].name == "网文");
    CHECK(read.replace_schemes[0].rules == original.replace_schemes[0].rules);
    CHECK(read.replace_schemes[1].name == "公文");
    REQUIRE(read.vn_schemes.size() == 2);
    CHECK(read.vn_schemes[0].name == "第一幕");
    CHECK(read.vn_schemes[0].characters == std::vector<std::string>{"小满", "阿甲"});
    CHECK(read.vn_schemes[0].route_names == std::vector<std::string>{"春"});
    CHECK(read.vn_schemes[0].scene_pattern == "── 春 ──");
    CHECK(read.vn_schemes[1].route_names.empty());
    CHECK(read.appearance == "dark");
}

TEST_CASE("配置包：包里写的是汉字原样，不是转义") {
    texttool::ConfigPackage package;
    package.replace_rules = {{"，", "、"}};
    const std::string text = texttool::build_config_package(package);
    CHECK(text.find("，") != std::string::npos);
    CHECK(text.find("\\u") == std::string::npos);
}

TEST_CASE("配置包：读回来再拼一遍，内容不变") {
    const texttool::ConfigPackage original = sample_package();
    const std::string first = texttool::build_config_package(original);

    texttool::ConfigPackage read;
    std::string error;
    REQUIRE(texttool::read_config_package(first, read, error));

    texttool::ConfigPackage again;
    REQUIRE(texttool::read_config_package(texttool::build_config_package(read), again, error));
    CHECK(again.replace_rules == original.replace_rules);
    CHECK(again.replace_schemes.size() == original.replace_schemes.size());
    CHECK(again.vn_schemes.size() == original.vn_schemes.size());
    CHECK(again.appearance == original.appearance);
}

TEST_CASE("配置包：包里没写底色就留空，不当成出错") {
    texttool::ConfigPackage package = sample_package();
    package.appearance.clear();

    texttool::ConfigPackage read;
    std::string error;
    REQUIRE(texttool::read_config_package(texttool::build_config_package(package), read, error));
    CHECK(read.appearance.empty());
}

TEST_CASE("配置包：空内容说有内容才行") {
    texttool::ConfigPackage read;
    std::string error;
    CHECK_FALSE(texttool::read_config_package("", read, error));
    CHECK(error.find("空") != std::string::npos);
}

TEST_CASE("配置包：认不出的文件说认不出，不当作配置包") {
    texttool::ConfigPackage read;
    std::string error;
    CHECK_FALSE(texttool::read_config_package("这不是 JSON", read, error));
    CHECK(error.find("读不出内容") != std::string::npos);

    CHECK_FALSE(texttool::read_config_package(R"([1, 2, 3])", read, error));
    CHECK(error.find("不是本工具的配置包") != std::string::npos);

    CHECK_FALSE(texttool::read_config_package(R"({"Format": "别的东西"})", read, error));
    CHECK(error.find("不是本工具的配置包") != std::string::npos);
}

TEST_CASE("配置包：更新版本写的包说读不了") {
    texttool::ConfigPackage read;
    std::string error;
    CHECK_FALSE(texttool::read_config_package(
        R"({"Format": "texttool-config", "Version": 2})", read, error));
    CHECK(error.find("更新版本") != std::string::npos);
}

TEST_CASE("配置包：少一份就说少哪一份") {
    const std::string text = R"({
        "Format": "texttool-config",
        "Version": 1,
        "ReplaceRules": [],
        "VnSchemes": []
    })";
    texttool::ConfigPackage read;
    std::string error;
    CHECK_FALSE(texttool::read_config_package(text, read, error));
    CHECK(error.find("替换方案") != std::string::npos);
}

TEST_CASE("配置包：哪一份不对就说哪一份、第几项、缺什么") {
    texttool::ConfigPackage read;
    std::string error;

    const std::string bad_rule = R"({
        "Format": "texttool-config",
        "ReplaceRules": [{"Find": "，", "Replace": "、"}, {"Find": "。"}],
        "ReplaceSchemes": [],
        "VnSchemes": []
    })";
    CHECK_FALSE(texttool::read_config_package(bad_rule, read, error));
    CHECK(error.find("「替换规则」") != std::string::npos);
    CHECK(error.find("第 2 条") != std::string::npos);
    CHECK(error.find("Replace") != std::string::npos);

    const std::string bad_scheme = R"({
        "Format": "texttool-config",
        "ReplaceRules": [],
        "ReplaceSchemes": [{"Name": "网文", "Rules": [{"Find": "，", "Replace": 1}]}],
        "VnSchemes": []
    })";
    CHECK_FALSE(texttool::read_config_package(bad_scheme, read, error));
    CHECK(error.find("「替换方案」「网文」") != std::string::npos);
    CHECK(error.find("第 1 条") != std::string::npos);

    const std::string bad_vn = R"({
        "Format": "texttool-config",
        "ReplaceRules": [],
        "ReplaceSchemes": [],
        "VnSchemes": [{"Name": "第一幕", "Characters": "小满"}]
    })";
    CHECK_FALSE(texttool::read_config_package(bad_vn, read, error));
    CHECK(error.find("「角色方案」「第一幕」") != std::string::npos);
    CHECK(error.find("Characters") != std::string::npos);
}

TEST_CASE("配置包：底色只认两样写法") {
    const std::string text = R"({
        "Format": "texttool-config",
        "ReplaceRules": [],
        "ReplaceSchemes": [],
        "VnSchemes": [],
        "Appearance": "blue"
    })";
    texttool::ConfigPackage read;
    std::string error;
    CHECK_FALSE(texttool::read_config_package(text, read, error));
    CHECK(error.find("底色") != std::string::npos);
    CHECK(error.find("blue") != std::string::npos);
}

TEST_CASE("配置包：存成文件再读回来，内容一致") {
    const TempDir dir;
    const fs::path file = dir.path() / "config-package.json";
    const texttool::ConfigPackage original = sample_package();
    texttool::save_config_package(file.string(), original);

    texttool::ConfigPackage read;
    std::string error;
    REQUIRE(texttool::load_config_package(file.string(), read, error));
    CHECK(error.empty());
    CHECK(read.replace_rules == original.replace_rules);
    CHECK(read.replace_schemes.size() == original.replace_schemes.size());
    CHECK(read.vn_schemes.size() == original.vn_schemes.size());
    CHECK(read.appearance == original.appearance);
}

TEST_CASE("配置包：文件不在就说打不开") {
    const TempDir dir;
    texttool::ConfigPackage read;
    std::string error;
    CHECK_FALSE(texttool::load_config_package((dir.path() / "没有这个文件.json").string(), read,
                                              error));
    CHECK(error.find("打不开") != std::string::npos);
}

TEST_CASE("导入写回去的三份文件，原来那几个读法读得回来") {
    const TempDir dir;
    const TempConfigs configs(dir);

    const texttool::ConfigPackage original = sample_package();
    texttool::save_config_parts(nullptr, original);

    // 盘上三份文件都在，写法与命令行、界面读的那三份一样。
    CHECK(fs::is_regular_file(dir.path() / "replace_rules.json"));
    CHECK(fs::is_regular_file(dir.path() / "replace_schemes.json"));
    CHECK(fs::is_regular_file(dir.path() / "vn_schemes.json"));

    std::vector<texttool::ReplaceRule> rules;
    std::string error;
    REQUIRE(texttool::load_replace_rules(nullptr, rules, error));
    CHECK(rules == original.replace_rules);

    std::vector<texttool::ReplaceScheme> schemes;
    REQUIRE(texttool::load_replace_schemes(nullptr, schemes, error));
    REQUIRE(schemes.size() == 2);
    CHECK(schemes[1].name == "公文");
    CHECK(schemes[1].rules == original.replace_schemes[1].rules);

    std::vector<texttool::VnScheme> vn;
    REQUIRE(texttool::load_vn_schemes(nullptr, vn, error));
    REQUIRE(vn.size() == 2);
    CHECK(vn[0].characters == original.vn_schemes[0].characters);
    CHECK(vn[0].scene_pattern == original.vn_schemes[0].scene_pattern);
}

TEST_CASE("导出的那几样，读出来就是盘上那份内容") {
    const TempDir dir;
    const TempConfigs configs(dir);

    // 盘上先放一份，导出应该原样收进去。
    {
        const texttool::ConfigPackage on_disk = sample_package();
        texttool::save_config_parts(nullptr, on_disk);
    }

    texttool::ConfigPackage exported;
    std::string error;
    REQUIRE(texttool::load_config_parts(nullptr, exported, error));
    CHECK(error.empty());
    CHECK(exported.replace_rules.size() == 2);
    CHECK(exported.replace_schemes.size() == 2);
    CHECK(exported.vn_schemes.size() == 2);
    CHECK(exported.appearance.empty());  // 底色不在这三份文件里，由界面自己补
}

TEST_CASE("导入时有一份文件坏了：说清是哪一份，三份都不动") {
    const TempDir dir;
    const TempConfigs configs(dir);

    // 替换方案那份先放一份坏的，另外两份放好的。
    std::ofstream bad(dir.path() / "replace_schemes.json", std::ios::binary | std::ios::trunc);
    bad << R"([{"Name": "网文", )";
    bad.close();

    texttool::ConfigPackage package;
    std::string error;
    CHECK_FALSE(texttool::load_config_parts(nullptr, package, error));
    CHECK(error.find("replace_schemes.json") != std::string::npos);
    CHECK(error.find("配置文件损坏") != std::string::npos);
}

TEST_CASE("导入写不进去：说清是哪一份、写到哪儿") {
    const TempDir dir;
    // 指到一个还不存在的子目录：三份文件都写不进去。
    const EnvVar rules("TEXTTOOL_REPLACE_RULES_JSON",
                       (dir.path() / "missing-dir" / "replace_rules.json").string());
    const EnvVar schemes("TEXTTOOL_SCHEMES_JSON",
                         (dir.path() / "missing-dir" / "replace_schemes.json").string());
    const EnvVar vn("TEXTTOOL_VN_SCHEMES_JSON",
                    (dir.path() / "missing-dir" / "vn_schemes.json").string());

    bool thrown = false;
    try {
        texttool::save_config_parts(nullptr, sample_package());
    } catch (const std::exception& error) {
        thrown = true;
        const std::string message = error.what();
        CHECK(message.find("「替换规则」") != std::string::npos);
        CHECK(message.find("missing-dir") != std::string::npos);
    }
    CHECK(thrown);
}
