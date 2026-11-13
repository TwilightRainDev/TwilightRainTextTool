// 配置文件的定位、解析与取用。三份文件都是顶层数组、一项一份方案。
//
// 替换规则、替换方案与角色方案这三份界面都会写回（换标点页改完规则、改完方案存下来，
// 剧本排版页改完角色表存下来），写的是同一个位置：命令行下一次读到的就是界面上存下的
// 那几份，两边换出来的正文因此逐字节相同。
//
// 解析用工程里带的那份 JSON 库（third_party/json/json.hpp）：标准的 JSON 写法都能读，
// 别人用编辑器改过的文件不会因为数字写法或转义写法不同就读不进来。
//
// 报错文字：读不了、解析不了、顶层不是数组，都算「配置文件损坏」，
// 并告诉对方重新配置或删除该文件。文件不存在不算损坏，按首次使用处理。
#include "engine/schemes.h"

#include <algorithm>
#include <cstdlib>
#include <exception>
#include <filesystem>
#include <fstream>
#include <optional>
#include <stdexcept>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

#include "engine/io.h"
#include "json/json.hpp"

namespace texttool {
namespace {

namespace fs = std::filesystem;
using Json = nlohmann::json;

// 配置文件的通用说法：读不了与解析不了两种原因，后面的处理办法一样。
std::string broken_read(const char* file_name) {
    return "配置文件损坏：" + std::string(file_name) +
           "（读不了；请用「配置导入/导出」功能恢复，或删除该文件后重新配置）";
}

std::string broken_parse(const char* file_name) {
    return "配置文件损坏：" + std::string(file_name) +
           "（无法解析；请用「配置导入/导出」功能恢复，或删除该文件后重新配置）";
}

std::string read_all(std::istream& in) {
    std::string bytes;
    char buffer[64 * 1024];
    while (in) {
        in.read(buffer, sizeof buffer);
        bytes.append(buffer, static_cast<std::size_t>(in.gcount()));
    }
    return bytes;
}

// 读一份配置：文件不存在返回空（按首次使用处理）。
// 读得到但解析不了就报错，不返回半份数据。
std::optional<Json> load_json_file(const fs::path& path, const char* file_name,
                                   std::string& error) {
    std::error_code ignored;
    if (!fs::is_regular_file(path, ignored)) return std::nullopt;

    std::ifstream in(path, std::ios::binary);
    if (!in) {
        error = broken_read(file_name);
        return std::nullopt;
    }

    try {
        return Json::parse(read_all(in));
    } catch (const std::exception&) {
        // 解析出错与文件内容不是 UTF-8 都落在这里：两种都按「解析不了」报。
        error = broken_parse(file_name);
        return std::nullopt;
    }
}

// 顶层得是数组：不是数组的文件一律按损坏处理。
bool require_array(const Json& root, const char* file_name, std::string& error) {
    if (root.is_array()) return true;
    error = broken_parse(file_name);
    return false;
}

// 取一个字符串字段。字段不在、或者不是字符串时按空串算。
std::string text_field(const Json& object, const char* key) {
    if (!object.is_object()) return {};
    const auto found = object.find(key);
    if (found == object.end() || !found->is_string()) return {};
    return found->get<std::string>();
}

// 取一个字符串数组字段。不是数组、或者项不是字符串时都不收。
std::vector<std::string> text_list_field(const Json& object, const char* key) {
    std::vector<std::string> out;
    if (!object.is_object()) return out;
    const auto found = object.find(key);
    if (found == object.end() || !found->is_array()) return out;
    for (const Json& item : *found)
        if (item.is_string()) out.push_back(item.get<std::string>());
    return out;
}

// 取一个是非字段。字段不在、或者写的不是 true/false 时都按否算。
bool flag_field(const Json& object, const char* key) {
    if (!object.is_object()) return false;
    const auto found = object.find(key);
    if (found == object.end() || !found->is_boolean()) return false;
    return found->get<bool>();
}

// 一个规则数组：每项取 Find 与 Replace 两个字符串，其余形状的项跳过。
std::vector<ReplaceRule> rules_from(const Json& array) {
    std::vector<ReplaceRule> rules;
    if (!array.is_array()) return rules;
    for (const Json& item : array) {
        if (!item.is_object()) continue;
        rules.emplace_back(text_field(item, "Find"), text_field(item, "Replace"));
    }
    return rules;
}

// 场景标记正则：「方案里给了、并且不是一串空白」才算给了。
// 空白按 .NET 的口径算，全角空格、不换行空格、行分隔符也算空白。
bool is_blank(std::string_view text) {
    for (std::size_t at = 0; at < text.size();) {
        const auto lead = static_cast<unsigned char>(text[at]);
        if (lead == 0x20 || (lead >= 0x09 && lead <= 0x0D)) {
            ++at;
            continue;
        }
        // 不换行空格占两个字节，剩下三种空白符号占三个字节。
        if (text.substr(at).starts_with("\xC2\xA0")) {
            at += 2;
            continue;
        }
        if (text.substr(at).starts_with("\xE3\x80\x80") ||
            text.substr(at).starts_with("\xE2\x80\xA8") ||
            text.substr(at).starts_with("\xE2\x80\xA9")) {
            at += 3;
            continue;
        }
        return false;
    }
    return true;
}

// 环境变量指的那份文件；没指时返回空。
std::optional<fs::path> env_path(const char* name) {
    const char* value = std::getenv(name);
    if (value == nullptr || *value == '\0') return std::nullopt;
    return fs::path(value);
}

// 挑好的行号在方案里不存在时的那句话：多半是文件被改过，行数变了。
constexpr char kPicksStale[] = "角色表改过了：请再挑一次";

// 把勾中的那几行按给的次序接进 out。同一个行号勾了两遍只接一次。
bool append_picked(const std::vector<std::string>& names, const std::vector<std::size_t>& picked,
                   std::vector<std::string>& out, std::string& error) {
    std::vector<bool> taken(names.size(), false);
    for (const std::size_t line : picked) {
        if (line >= names.size()) {
            error = kPicksStale;
            return false;
        }
        if (taken[line]) continue;
        taken[line] = true;
        out.push_back(names[line]);
    }
    return true;
}

// 一份方案里每一行都勾上。整份用上时就是这个形状。
VnSchemePick whole_pick(const VnScheme& scheme, std::size_t index) {
    VnSchemePick pick;
    pick.scheme = index;
    pick.characters.resize(scheme.characters.size());
    for (std::size_t line = 0; line < pick.characters.size(); ++line) pick.characters[line] = line;
    pick.route_names.resize(scheme.route_names.size());
    for (std::size_t line = 0; line < pick.route_names.size(); ++line) pick.route_names[line] = line;
    return pick;
}

// 盘上那份方案表里，哪几个名字写着是内置的。文件不在、读不了、解析不了都当没有：
// 这时候护不住任何一份，保存仍然进行（与换标点的规则表「读不出来也允许存」同一个说法）。
std::vector<std::string> built_in_names_on_disk(const fs::path& path) {
    std::vector<std::string> names;
    std::error_code ignored;
    if (!fs::is_regular_file(path, ignored)) return names;

    std::ifstream in(path, std::ios::binary);
    if (!in) return names;

    Json root;
    try {
        root = Json::parse(read_all(in));
    } catch (const std::exception&) {
        return names;
    }
    if (!root.is_array()) return names;

    for (const Json& item : root) {
        if (!item.is_object()) continue;
        if (flag_field(item, "IsBuiltIn")) names.push_back(text_field(item, "Name"));
    }
    return names;
}

}  // namespace

fs::path program_dir(const char* argv0) {
    std::error_code ignored;
    fs::path start = fs::current_path(ignored);
    if (argv0 != nullptr && *argv0 != '\0') {
        const fs::path exe = fs::weakly_canonical(argv0, ignored);
        if (!ignored && exe.has_parent_path()) start = exe.parent_path();
    }
    return start;
}

fs::path replace_rules_file(const char* argv0) {
    return env_path("TEXTTOOL_REPLACE_RULES_JSON").value_or(program_dir(argv0) /
                                                             "replace_rules.json");
}

fs::path replace_schemes_file(const char* argv0) {
    return env_path("TEXTTOOL_SCHEMES_JSON").value_or(program_dir(argv0) /
                                                       "replace_schemes.json");
}

fs::path vn_schemes_file(const char* argv0) {
    return env_path("TEXTTOOL_VN_SCHEMES_JSON").value_or(program_dir(argv0) /
                                                          "vn_schemes.json");
}

bool load_replace_rules(const char* argv0, std::vector<ReplaceRule>& out, std::string& error) {
    out.clear();
    const fs::path path = replace_rules_file(argv0);

    const std::optional<Json> root = load_json_file(path, "replace_rules.json", error);
    if (!error.empty()) return false;
    if (!root) return true;  // 还没配过：空表
    if (!require_array(*root, "replace_rules.json", error)) return false;

    out = rules_from(*root);
    return true;
}

void save_replace_rules(const char* argv0, const std::vector<ReplaceRule>& rules) {
    Json array = Json::array();
    for (const ReplaceRule& rule : rules) {
        Json item = Json::object();
        item["Find"] = rule.first;
        item["Replace"] = rule.second;
        array.push_back(std::move(item));
    }

    std::string text;
    try {
        text = array.dump(2) + "\n";
    } catch (const std::exception&) {
        // 不能写成合法 UTF-8 的文字写不进 JSON：宁可不写，也不留一份读不回来的规则。
        throw std::runtime_error("这几条里有认不出的字符，没能存下来");
    }
    write_text(replace_rules_file(argv0).string(), text, TextEncoding::Utf8);
}

void save_replace_schemes(const char* argv0, const std::vector<ReplaceScheme>& schemes) {
    Json array = Json::array();
    for (const ReplaceScheme& scheme : schemes) {
        Json rules = Json::array();
        for (const ReplaceRule& rule : scheme.rules) {
            Json entry = Json::object();
            entry["Find"] = rule.first;
            entry["Replace"] = rule.second;
            rules.push_back(std::move(entry));
        }

        Json item = Json::object();
        item["Name"] = scheme.name;
        item["Rules"] = std::move(rules);
        array.push_back(std::move(item));
    }

    std::string text;
    try {
        text = array.dump(2) + "\n";
    } catch (const std::exception&) {
        // 不能写成合法 UTF-8 的文字写不进 JSON：宁可不写，也不留一份读不回来的方案。
        throw std::runtime_error("这几份方案里有认不出的字符，没能存下来");
    }
    write_text(replace_schemes_file(argv0).string(), text, TextEncoding::Utf8);
}

bool load_replace_schemes(const char* argv0, std::vector<ReplaceScheme>& out, std::string& error) {
    out.clear();

    std::vector<fs::path> candidates;
    if (const std::optional<fs::path> from_env = env_path("TEXTTOOL_SCHEMES_JSON")) {
        candidates.push_back(*from_env);
    } else {
        candidates.push_back(replace_schemes_file(argv0));
        candidates.push_back(program_dir(argv0) / "default_schemes.json");
    }

    for (const fs::path& path : candidates) {
        const std::optional<Json> root = load_json_file(path, "replace_schemes.json", error);
        if (!error.empty()) return false;
        if (!root) continue;
        if (!require_array(*root, "replace_schemes.json", error)) return false;

        for (const Json& item : *root) {
            if (!item.is_object()) continue;
            ReplaceScheme scheme;
            scheme.name = text_field(item, "Name");
            const auto rules = item.find("Rules");
            if (rules != item.end()) scheme.rules = rules_from(*rules);
            out.push_back(std::move(scheme));
        }
        // 文件在但空表：按对照基准的写法退到下一份候选文件。
        if (!out.empty()) return true;
    }
    return true;
}

bool load_vn_schemes(const char* argv0, std::vector<VnScheme>& out, std::string& error) {
    out.clear();

    std::vector<fs::path> candidates;
    if (const std::optional<fs::path> from_env = env_path("TEXTTOOL_VN_SCHEMES_JSON")) {
        candidates.push_back(*from_env);
    } else {
        candidates.push_back(vn_schemes_file(argv0));
        candidates.push_back(program_dir(argv0) / "default_vn_schemes.json");
    }

    for (const fs::path& path : candidates) {
        const std::optional<Json> root = load_json_file(path, "vn_schemes.json", error);
        if (!error.empty()) return false;
        if (!root) continue;
        if (!require_array(*root, "vn_schemes.json", error)) return false;

        for (const Json& item : *root) {
            if (!item.is_object()) continue;
            VnScheme scheme;
            scheme.name = text_field(item, "Name");
            scheme.characters = text_list_field(item, "Characters");
            scheme.route_names = text_list_field(item, "RouteNames");
            scheme.scene_pattern = text_field(item, "ScenePattern");
            scheme.description = text_field(item, "Description");
            scheme.is_built_in = flag_field(item, "IsBuiltIn");
            out.push_back(std::move(scheme));
        }
        // 文件在但空表：退回下一份候选文件，那边再没有就用通用写法排。
        if (!out.empty()) return true;
    }
    return true;
}

const ReplaceScheme* find_replace_scheme(const std::vector<ReplaceScheme>& schemes,
                                         const std::string& name) {
    for (const ReplaceScheme& scheme : schemes)
        if (scheme.name == name) return &scheme;
    return nullptr;
}

void save_vn_schemes(const char* argv0, const std::vector<VnScheme>& schemes) {
    const fs::path path = vn_schemes_file(argv0);

    for (const std::string& built_in : built_in_names_on_disk(path)) {
        const bool kept = std::any_of(schemes.begin(), schemes.end(),
                                      [&](const VnScheme& scheme) {
                                          return scheme.name == built_in;
                                      });
        if (!kept) throw std::runtime_error("内置的方案不能删：" + built_in);
    }

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

    std::string text;
    try {
        text = array.dump(2) + "\n";
    } catch (const std::exception&) {
        // 不能写成合法 UTF-8 的文字写不进 JSON：宁可不写，也不留一份读不回来的角色表。
        throw std::runtime_error("这几份角色表里有认不出的字符，没能存下来");
    }
    write_text(path.string(), text, TextEncoding::Utf8);
}

bool combine_vn_schemes(const std::vector<VnScheme>& schemes,
                        const std::vector<std::string>& names, VnOptions& options,
                        std::string& error) {
    std::vector<VnSchemePick> picks;
    picks.reserve(names.size());
    for (const std::string& wanted : names) {
        std::size_t index = schemes.size();
        for (std::size_t at = 0; at < schemes.size(); ++at)
            if (schemes[at].name == wanted) {
                index = at;
                break;
            }
        if (index == schemes.size()) {
            error = "未找到方案：" + wanted;
            return false;
        }
        picks.push_back(whole_pick(schemes[index], index));
    }
    return combine_vn_scheme_picks(schemes, picks, options, error);
}

bool combine_vn_scheme_picks(const std::vector<VnScheme>& schemes,
                             const std::vector<VnSchemePick>& picks, VnOptions& options,
                             std::string& error) {
    std::vector<std::string> scene_patterns;
    for (const VnSchemePick& pick : picks) {
        if (pick.scheme >= schemes.size()) {
            error = kPicksStale;
            return false;
        }
        const VnScheme& scheme = schemes[pick.scheme];
        if (!append_picked(scheme.characters, pick.characters, options.characters, error))
            return false;
        if (!append_picked(scheme.route_names, pick.route_names, options.route_names, error))
            return false;
        if (!is_blank(scheme.scene_pattern)) scene_patterns.push_back(scheme.scene_pattern);
    }

    // 一条都没给时留着空串，引擎那边按通用场景标记认。
    if (!scene_patterns.empty()) {
        std::string joined;
        for (const std::string& expression : scene_patterns) {
            if (!joined.empty()) joined += '|';
            joined += expression;
        }
        options.scene_pattern = std::move(joined);
    }
    return true;
}

}  // namespace texttool
