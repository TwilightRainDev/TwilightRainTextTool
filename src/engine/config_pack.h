// 界面配置的导入导出：一次搬走替换规则、替换方案、角色方案与底色。
//
// 包是一份 JSON 文件，顶层写着这几项（没列出的项不认识也不管，留给下一版加东西）：
//
//   Format          固定写 texttool-config，用来认出这是不是本工具的配置包
//   Version         格式版本，这一版写 1
//   Created         导出那一刻的日期与时刻，只是给人看
//   ReplaceRules    替换规则，每项有 Find 与 Replace
//   ReplaceSchemes  替换方案，每项有 Name 与 Rules（里面还是 Find 与 Replace）
//   VnSchemes       角色方案，每项有 Name、Characters、RouteNames、ScenePattern，
//                   说明 Description 与内置标记 IsBuiltIn 可以没有（按空说明、不是内置算）
//   Appearance      底色，写 light 或 dark；不写就表示包里没带这一项
//
// 四样都要在：少一份就按包不完整报错，不拿空表去换掉用户现有的东西。
// 读的时候逐项验，第一处不对就停下，消息里写清是哪一份、第几项、什么毛病；
// 全部读通之前盘上一个字都不写，所以导入失败不会留下改了一半的配置。
//
// 写盘走 engine/io 的 write_text：先写临时文件再改名，与处理稿件时同一套。
#pragma once

#include <string>
#include <string_view>
#include <vector>

#include "engine/schemes.h"

namespace texttool {

// 一份配置包的全部内容。appearance 是 light、dark，或空串表示包里没写这一项。
struct ConfigPackage {
    std::vector<ReplaceRule> replace_rules;
    std::vector<ReplaceScheme> replace_schemes;
    std::vector<VnScheme> vn_schemes;
    std::string appearance;
};

// 把四样拼成一份包，返回整份 JSON 文本（UTF-8，不带 BOM，缩进两格，汉字原样写）。
// 生成时间取当前时钟，只写给人看。
std::string build_config_package(const ConfigPackage& package);

// 读一份包的字节。读不通返回 false，error 里是给人看的一句话。
bool read_config_package(std::string_view bytes, ConfigPackage& package, std::string& error);

// 读一份包的文件。文件不存在、读不了、读不通都返回 false，error 里说清原因。
bool load_config_package(const std::string& path, ConfigPackage& package, std::string& error);

// 把一份包写到 path。写不进去抛 std::runtime_error，消息里带路径。
void save_config_package(const std::string& path, const ConfigPackage& package);

// 现在盘上的四样：三份配置文件按 load_replace_rules、load_replace_schemes、
// load_vn_schemes 的找法读，底色留空（那是界面自己记的项，由调用方补）。
// 有一份文件坏了返回 false，error 里是那份文件原来的报错。
bool load_config_parts(const char* argv0, ConfigPackage& package, std::string& error);

// 把包里的三份配置写进程序目录（或环境变量指的那份文件），位置与 load_config_parts 相同。
// 写不进去抛 std::runtime_error，消息里写清是哪一份、写到哪儿、什么原因。
void save_config_parts(const char* argv0, const ConfigPackage& package);

}  // namespace texttool
