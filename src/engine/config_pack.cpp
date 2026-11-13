// 配置包的拼装与读取。报错文字以「给人看」为准：先说哪一份，再说第几项，最后说缺什么。
#include "engine/config_pack.h"

#include <ctime>
#include <exception>
#include <fstream>
#include <stdexcept>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

#include "engine/io.h"
#include "json/json.hpp"

namespace texttool {
namespace {

using Json = nlohmann::json;

constexpr const char* kFormatName = "texttool-config";
constexpr int kFormatVersion = 1;

// 导出那一刻的日期与时刻，写法与界面上看到的一致：年月日在前，时分在后。
std::string current_time_text() {
    const std::time_t now = std::time(nullptr);
    std::tm parts{};
#ifdef _WIN32
    localtime_s(&parts, &now);
#else
    localtime_r(&now, &parts);
#endif
    const auto two = [](int value) {
        return value < 10 ? "0" + std::to_string(value) : std::to_string(value);
    };
    return std::to_string(parts.tm_year + 1900) + "-" + two(parts.tm_mon + 1) + "-" +
           two(parts.tm_mday) + " " + two(parts.tm_hour) + ":" + two(parts.tm_min);
}

// ---------------------------------------------------------------- 拼 JSON

Json rules_to_json(const std::vector<ReplaceRule>& rules) {
    Json array = Json::array();
    for (const ReplaceRule& rule : rules) {
        Json item = Json::object();
        item["Find"] = rule.first;
        item["Replace"] = rule.second;
        array.push_back(std::move(item));
    }
    return array;
}

Json replace_schemes_to_json(const std::vector<ReplaceScheme>& schemes) {
    Json array = Json::array();
    for (const ReplaceScheme& scheme : schemes) {
        Json item = Json::object();
        item["Name"] = scheme.name;
        item["Rules"] = rules_to_json(scheme.rules);
        array.push_back(std::move(item));
    }
    return array;
}

Json vn_schemes_to_json(const std::vector<VnScheme>& schemes) {
    Json array = Json::array();
    for (const VnScheme& scheme : schemes) {
        Json item = Json::object();
        item["Name"] = scheme.name;
        item["Description"] = scheme.description;
        item["IsBuiltIn"] = scheme.is_built_in;
        item["Characters"] = scheme.characters;
        item["RouteNames"] = scheme.route_names;
        item["ScenePattern"] = scheme.scene_pattern;
        array.push_back(std::move(item));
    }
    return array;
}

// ---------------------------------------------------------------- 读的规矩

// 「第几项」在消息里的写法：从 1 数，与界面上数条目的方式一样。
std::string item_where(const std::string& part, std::size_t index) {
    return part + "第 " + std::to_string(index) + " ";
}

// 取一个必写的字符串。
bool read_required_text(const Json& item, const char* key, const std::string& where,
                        std::string& out, std::string& error) {
    const auto found = item.find(key);
    if (found == item.end()) {
        error = where + "没有写「" + key + "」";
        return false;
    }
    if (!found->is_string()) {
        error = where + "的「" + key + "」不是一个字符串";
        return false;
    }
    out = found->get<std::string>();
    return true;
}

// 取一个可以没有的字符串：不在就留空，写着但不是字符串就报错。
bool read_optional_text(const Json& item, const char* key, const std::string& where,
                        std::string& out, std::string& error) {
    const auto found = item.find(key);
    if (found == item.end()) {
        out.clear();
        return true;
    }
    if (!found->is_string()) {
        error = where + "的「" + key + "」不是一个字符串";
        return false;
    }
    out = found->get<std::string>();
    return true;
}

// 取一个可以没有的是非字段：不在就按否算，写着但不是 true/false 就报错。
bool read_optional_flag(const Json& item, const char* key, const std::string& where, bool& out,
                        std::string& error) {
    const auto found = item.find(key);
    if (found == item.end()) {
        out = false;
        return true;
    }
    if (!found->is_boolean()) {
        error = where + "的「" + key + "」不是 true 或 false";
        return false;
    }
    out = found->get<bool>();
    return true;
}

// 取一个字符串列表：不在就留空；写着但不是列表、或者里面有不是字符串的项就报错。
bool read_text_list(const Json& item, const char* key, const std::string& where,
                    std::vector<std::string>& out, std::string& error) {
    out.clear();
    const auto found = item.find(key);
    if (found == item.end()) return true;
    if (!found->is_array()) {
        error = where + "的「" + key + "」不是一个列表";
        return false;
    }
    for (const Json& entry : *found) {
        if (!entry.is_string()) {
            error = where + "的「" + key + "」第 " + std::to_string(out.size() + 1) +
                    " 项不是一个字符串";
            return false;
        }
        out.push_back(entry.get<std::string>());
    }
    return true;
}

// 规则列表。part 是这些规则在包里的叫法，拼在错误消息的「第 2 条」前面。
bool read_rules(const Json& array, const std::string& part, std::vector<ReplaceRule>& out,
                std::string& error) {
    out.clear();
    if (!array.is_array()) {
        error = part + "不是一个列表";
        return false;
    }
    for (std::size_t i = 0; i < array.size(); ++i) {
        const Json& item = array[i];
        const std::string where = item_where(part, i + 1) + "条";
        if (!item.is_object()) {
            error = where + "不是一个规则";
            return false;
        }
        ReplaceRule rule;
        if (!read_required_text(item, "Find", where, rule.first, error)) return false;
        if (!read_required_text(item, "Replace", where, rule.second, error)) return false;
        out.push_back(std::move(rule));
    }
    return true;
}

// 顶层得是对象，Format 与 Version 得对上。
bool check_header(const Json& root, std::string& error) {
    if (!root.is_object()) {
        error = "这一份不是本工具的配置包：最外层的样子不对";
        return false;
    }

    const auto format = root.find("Format");
    if (format == root.end()) {
        error = "这一份不是本工具的配置包：没有写「Format」";
        return false;
    }
    if (!format->is_string() || format->get<std::string>() != kFormatName) {
        error = "这一份不是本工具的配置包：「Format」写的不是「" + std::string(kFormatName) + "」";
        return false;
    }

    const auto version = root.find("Version");
    if (version == root.end()) return true;  // 没写就按这一版认
    if (!version->is_number_integer()) {
        error = "这一份配置包的「Version」不是一个整数，认不出是哪个版本";
        return false;
    }
    const int number = version->get<int>();
    if (number > kFormatVersion) {
        error = "这一份配置包是更新版本的工具写的（格式 " + std::to_string(number) +
                "），这个版本读不了";
        return false;
    }
    if (number < 1) {
        error = "这一份配置包的「Version」写的是 " + std::to_string(number) + "，认不出来";
        return false;
    }
    return true;
}

// 一份必有的列表。
bool require_array(const Json& root, const char* key, const std::string& part, const Json*& out,
                   std::string& error) {
    const auto found = root.find(key);
    if (found == root.end()) {
        error = "这一份配置包里没有「" + part + "」这一份，导入不了";
        return false;
    }
    if (!found->is_array()) {
        error = "这一份配置包里「" + part + "」这一份不是一个列表";
        return false;
    }
    out = &*found;
    return true;
}

}  // namespace

std::string build_config_package(const ConfigPackage& package) {
    Json root = Json::object();
    root["Format"] = kFormatName;
    root["Version"] = kFormatVersion;
    root["Created"] = current_time_text();
    root["ReplaceRules"] = rules_to_json(package.replace_rules);
    root["ReplaceSchemes"] = replace_schemes_to_json(package.replace_schemes);
    root["VnSchemes"] = vn_schemes_to_json(package.vn_schemes);
    if (!package.appearance.empty()) root["Appearance"] = package.appearance;
    return root.dump(2) + "\n";
}

bool read_config_package(std::string_view bytes, ConfigPackage& package, std::string& error) {
    error.clear();
    package = ConfigPackage{};

    if (bytes.empty()) {
        error = "这一份配置包是空的，里面没有内容";
        return false;
    }

    Json root;
    try {
        root = Json::parse(bytes.begin(), bytes.end());
    } catch (const std::exception&) {
        // 解析出错与文件内容不是 UTF-8 都落在这里：两种都按「读不出内容」报。
        error = "这一份配置包读不出内容：可能不是配置包，也可能被改坏了";
        return false;
    }

    if (!check_header(root, error)) return false;

    const Json* rules = nullptr;
    if (!require_array(root, "ReplaceRules", "替换规则", rules, error)) return false;
    if (!read_rules(*rules, "「替换规则」", package.replace_rules, error)) return false;

    const Json* replace_schemes = nullptr;
    if (!require_array(root, "ReplaceSchemes", "替换方案", replace_schemes, error)) return false;
    for (std::size_t i = 0; i < replace_schemes->size(); ++i) {
        const Json& item = (*replace_schemes)[i];
        const std::string where = item_where("「替换方案」", i + 1) + "份";
        if (!item.is_object()) {
            error = where + "不是一个方案";
            return false;
        }
        ReplaceScheme scheme;
        if (!read_required_text(item, "Name", where, scheme.name, error)) return false;
        const auto rules_item = item.find("Rules");
        if (rules_item != item.end()) {
            const std::string inside = "「替换方案」「" + scheme.name + "」";
            if (!read_rules(*rules_item, inside, scheme.rules, error)) return false;
        }
        package.replace_schemes.push_back(std::move(scheme));
    }

    const Json* vn_schemes = nullptr;
    if (!require_array(root, "VnSchemes", "角色方案", vn_schemes, error)) return false;
    for (std::size_t i = 0; i < vn_schemes->size(); ++i) {
        const Json& item = (*vn_schemes)[i];
        const std::string where = item_where("「角色方案」", i + 1) + "份";
        if (!item.is_object()) {
            error = where + "不是一个方案";
            return false;
        }
        VnScheme scheme;
        if (!read_required_text(item, "Name", where, scheme.name, error)) return false;
        const std::string inside = "「角色方案」「" + scheme.name + "」";
        if (!read_optional_text(item, "Description", inside, scheme.description, error))
            return false;
        if (!read_optional_flag(item, "IsBuiltIn", inside, scheme.is_built_in, error))
            return false;
        if (!read_text_list(item, "Characters", inside, scheme.characters, error)) return false;
        if (!read_text_list(item, "RouteNames", inside, scheme.route_names, error)) return false;
        if (!read_optional_text(item, "ScenePattern", inside, scheme.scene_pattern, error))
            return false;
        package.vn_schemes.push_back(std::move(scheme));
    }

    const auto appearance = root.find("Appearance");
    if (appearance != root.end()) {
        if (!appearance->is_string()) {
            error = "这一份配置包里「底色」不是一个字符串";
            return false;
        }
        const std::string value = appearance->get<std::string>();
        if (value != "light" && value != "dark") {
            error = "这一份配置包里「底色」写的是「" + value + "」，只认 light 与 dark";
            return false;
        }
        package.appearance = value;
    }
    return true;
}

bool load_config_package(const std::string& path, ConfigPackage& package, std::string& error) {
    error.clear();
    std::ifstream in(path, std::ios::binary);
    if (!in) {
        error = "这一份配置包打不开：" + path;
        return false;
    }
    std::string bytes;
    char buffer[64 * 1024];
    while (in) {
        in.read(buffer, sizeof buffer);
        bytes.append(buffer, static_cast<std::size_t>(in.gcount()));
    }
    if (in.bad()) {
        error = "这一份配置包读到一半出错了：" + path;
        return false;
    }

    std::string read_error;
    if (!read_config_package(bytes, package, read_error)) {
        error = path + "：" + read_error;
        return false;
    }
    return true;
}

void save_config_package(const std::string& path, const ConfigPackage& package) {
    std::string text;
    try {
        text = build_config_package(package);
    } catch (const std::exception&) {
        throw std::runtime_error("这一份配置里有认不出的字符，写不成配置包");
    }
    write_text(path, text, TextEncoding::Utf8);
}

bool load_config_parts(const char* argv0, ConfigPackage& package, std::string& error) {
    package = ConfigPackage{};
    if (!load_replace_rules(argv0, package.replace_rules, error)) return false;
    if (!load_replace_schemes(argv0, package.replace_schemes, error)) return false;
    if (!load_vn_schemes(argv0, package.vn_schemes, error)) return false;
    return true;
}

void save_config_parts(const char* argv0, const ConfigPackage& package) {
    // 三份内容先在内存里备齐：备的时候出错，盘上一个字都没动。
    const std::string rules_text = rules_to_json(package.replace_rules).dump(2) + "\n";
    const std::string schemes_text = replace_schemes_to_json(package.replace_schemes).dump(2) + "\n";
    const std::string vn_text = vn_schemes_to_json(package.vn_schemes).dump(2) + "\n";

    struct Part {
        const char* name;  // 给人看的名字
        std::filesystem::path path;
        const std::string* text;
    };
    const Part parts[] = {
        {"替换规则", replace_rules_file(argv0), &rules_text},
        {"替换方案", replace_schemes_file(argv0), &schemes_text},
        {"角色方案", vn_schemes_file(argv0), &vn_text},
    };

    for (const Part& part : parts) {
        try {
            write_text(part.path.string(), *part.text, TextEncoding::Utf8);
        } catch (const std::exception& failure) {
            throw std::runtime_error("「" + std::string(part.name) + "」那一份写不进去（" +
                                     part.path.string() + "）：" + failure.what());
        }
    }
}

}  // namespace texttool
