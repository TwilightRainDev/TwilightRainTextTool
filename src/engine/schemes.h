// 配置文件的定位、解析与取用：替换规则、替换方案、角色方案。
//
// 命令行与界面都从这里读，两边看到的是同一份数据。命令行 replace 的 --scheme、vn 的
// --scheme，界面换标点页的规则列表与「从预设里挑」、剧本排版页的角色表，都走这里，
// 界面不再自己解析一遍 JSON。
//
// 三份文件的形状相同：顶层都是数组，一项一份方案，字段是这几项：
//   替换规则：每项有 Find 与 Replace 两项
//   替换方案：每项有 Name 与 Rules（规则数组）
//   角色方案：每项有 Name、Characters（角色名数组）、RouteNames（路线名数组）、
//             ScenePattern（场景标记正则）、Description（说明）、IsBuiltIn（是不是内置）
//   角色方案里说明与内置标记可以没有：没有就按空说明、不是内置算。
//
// 位置次序：环境变量指的那份（TEXTTOOL_REPLACE_RULES_JSON、TEXTTOOL_SCHEMES_JSON、
// TEXTTOOL_VN_SCHEMES_JSON），没有环境变量就按程序所在目录下的文件名找。
// 文件不在当首次使用，不算错；文件在但读不了或解析不了就报错，不静默降级成空表。
#pragma once

#include <cstddef>
#include <filesystem>
#include <string>
#include <utility>
#include <vector>

#include "engine/processing.h"

namespace texttool {

// 一条替换规则：找什么、换成什么。
using ReplaceRule = std::pair<std::string, std::string>;

struct ReplaceScheme {
    std::string name;
    std::vector<ReplaceRule> rules;
};

struct VnScheme {
    std::string name;
    std::vector<std::string> characters;
    std::vector<std::string> route_names;
    std::string scene_pattern;  // 方案里没给就留着空
    std::string description;    // 说明，界面上跟在名字后面写给人看；没写就是空的
    bool is_built_in = false;   // 随程序发的那几份：界面上不许删
};

// 挑中的一份角色表：哪一份，勾了里面哪几个角色名、哪几条路线名。
// 行号从 0 数，角色名的行号按 characters 的次序，路线名的行号按 route_names 的次序。
// picks 里出现一份就表示这一份用上了：连一行都没勾时，只算上它的场景标记。
// 一份都不出现就是一份都不用，按通用写法认场景。
struct VnSchemePick {
    std::size_t scheme = 0;
    std::vector<std::size_t> characters;
    std::vector<std::size_t> route_names;
};

// 程序所在目录：找配置文件从这里起头，拿不到 argv0 时用当前工作目录。
std::filesystem::path program_dir(const char* argv0);

// 三份配置文件的位置：有环境变量就写环境变量指的那份，否则写程序目录下的那份。
// 读与写用同一个位置：导入配置写进去以后，界面与命令行下一次读到的就是新的。
// 替换方案与角色方案还有一份随程序发的默认表，那份只读不写，不在这三个里。
std::filesystem::path replace_rules_file(const char* argv0);
std::filesystem::path replace_schemes_file(const char* argv0);
std::filesystem::path vn_schemes_file(const char* argv0);

// 界面里配的那张替换规则表。文件不在就是空表。
// 读不出来（读不了、解析不了、不是数组）返回 false，原因写进 error。
bool load_replace_rules(const char* argv0, std::vector<ReplaceRule>& out, std::string& error);

// 把一整张替换规则表写回 replace_rules_file 指的那份文件，命令行下一次读到的就是它。
// 写法与配置包里的规则那一项相同：顶层数组，一项一条，只有 Find 与 Replace。
// 写盘走 engine/io 的 write_text：先写临时文件再整体改名，写不进去时原来的文件一个字没动，
// 抛出的消息里带路径与原因。
void save_replace_rules(const char* argv0, const std::vector<ReplaceRule>& rules);

// 预设的替换方案表。三份候选文件按次序看，前一份不在或空表才看下一份。
bool load_replace_schemes(const char* argv0, std::vector<ReplaceScheme>& out, std::string& error);

// 把一整份替换方案表写回 replace_schemes_file 指的那份文件，界面改完就存这里，命令行
// 与界面下一次读到的都是同一份。写法与配置包里的方案那一项相同：顶层数组，一项一份方案，
// 只有 Name 与 Rules（规则数组里每项只有 Find 与 Replace），缩进两格，写 UTF-8。
// 写盘走 engine/io 的 write_text：先写临时文件再整体改名，写不进去时原来的文件一个字没动，
// 抛出的消息里带路径与原因。
void save_replace_schemes(const char* argv0, const std::vector<ReplaceScheme>& schemes);

// 预设的角色方案表，找法与替换方案相同。
bool load_vn_schemes(const char* argv0, std::vector<VnScheme>& out, std::string& error);

// 把一整份角色方案表写回 vn_schemes_file 指的那份文件，界面改完就存这里，命令行下一次
// 读到的是同一份。写法与配置包里角色方案那一项相同（Name、Description、IsBuiltIn、
// Characters、RouteNames、ScenePattern 都写），说明与内置标记一并写上，界面上存一次
// 不会把别人写的说明抹掉。写盘走 engine/io 的 write_text：先写临时文件再整体改名，
// 写不进去时原来的文件一个字没动。
// 盘上写着是内置的那几份不许删：新表里少了哪一份就抛 std::runtime_error，盘上不动。
void save_vn_schemes(const char* argv0, const std::vector<VnScheme>& schemes);

// 按名字取一份替换方案，找不到返回空指针。
const ReplaceScheme* find_replace_scheme(const std::vector<ReplaceScheme>& schemes,
                                         const std::string& name);

// 把几份角色方案并进排版参数：角色名与路线名按给的次序接起来，场景标记正则用 | 连成一条
// （方案里那条正则是一整串空白等于没给，这时不参与拼接）。
// 名字里有一份找不到时返回 false，error 写「未找到方案：名字」，options 可能已被改过一半，
// 调用方拿到 false 就别再用它。
bool combine_vn_schemes(const std::vector<VnScheme>& schemes,
                        const std::vector<std::string>& names, VnOptions& options,
                        std::string& error);

// 同上，交上来的是「哪几份、每份里勾了哪些行」：勾中的角色名与路线名按 picks 的次序、
// 行号的次序接起来，场景标记正则仍用 | 连成一条。勾的行号在方案里不存在时返回 false，
// error 写「角色表改过了：请再挑一次」（多半是文件被改过、行数变了）。
bool combine_vn_scheme_picks(const std::vector<VnScheme>& schemes,
                             const std::vector<VnSchemePick>& picks, VnOptions& options,
                             std::string& error);

}  // namespace texttool
