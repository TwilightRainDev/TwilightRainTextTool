// 多步串联：把几个处理步骤串成一条链，一份稿子进去，一个文件出来。
//
// 引擎里已有三个单步管线：接回断行（MergePipeline）、换标点（ReplacePipeline）、
// 剧本排版（VnPipeline）。一条链就是按调用方给的次序把其中几步接起来跑：上一步的产物
// 写成一个临时文件，下一步把它当一份真文件重新读进来。链里有几步、谁先谁后，全看调用方
// 交进来的那组步骤，引擎不知道界面上勾了哪几个框、怎么排的序。命令行以后挂同一条链，
// 只是换一个参数的来处。
//
// 为什么每一步都要落盘：
//   换标点本来就要读两遍（先判这份稿子算不算博文），而「每一步的输入是一份真文件」是
//   逐字节对照那套保证成立的前提——同一份输入、同一组参数，输出必须与单步处理相同。
//   顺带也是内存的前提：上一步的整份结果从不留在内存里，峰值只与「一块正文加当前这一行」
//   有关，与稿件长短无关。调用方要做的只有一件：别自己把整份稿读进来再交给链。
//
// 「检查是不是机器写的」不进链：它读产物、出报告文本，不写文件，是旁路产物。
//
// 失败与中断的四条规矩（下面几个接口的形状就是按它们定的）：
//   一、按停止只在两步之间收手：正在写的那一步一定写完，再停；临时文件删掉，目标文件不动。
//   二、最后一步改名时目标已存在：覆盖，先留 .bak，与单步处理同一套写盘规矩。
//   三、进程被强杀留下的临时文件：下次启动扫一遍目标目录，名字对得上、写它的那个进程
//       已经不在了的删掉。
//   四、预览与正式跑用不同的临时名前缀：两边同时开，谁也不会盖掉谁的文件。
//
// 编码：每一步都按它读到的那份稿的编码读、按同一个编码写，与单步处理一致。中间产物也按
// 这个编码写，所以下一步重新探测到的还是同一个编码（探测只看字节，而这几步只改行尾与标点，
// 不会把一份不是合法 UTF-8 的稿子改成合法 UTF-8）。
#pragma once

#include <cstddef>
#include <functional>
#include <string>
#include <string_view>
#include <vector>

#include "engine/encoding.h"
#include "engine/processing.h"

namespace texttool {

// 链里一步是哪一种处理。三种各对应一个单步管线。
enum class ChainStepKind { MergeLines, ReplacePunctuation, ReformatVn };

// 写给人看的步骤名：接回断行、换标点、剧本排版。错误消息与日志用它。
std::string_view chain_step_name(ChainStepKind kind);

// 链里的一步。按 kind 只读对应的那一份参数，另外两份不看。
struct ChainStep {
    ChainStepKind kind = ChainStepKind::MergeLines;
    MergeOptions merge;      // kind 是 MergeLines 时用这一份
    ReplaceOptions replace;  // kind 是 ReplacePunctuation 时用这一份
    VnOptions vn;            // kind 是 ReformatVn 时用这一份
};

// 一条链要跑的东西，参数都已解析好，引擎不再回头问任何设置。
struct ChainRequest {
    std::string source;            // 输入的稿子
    std::string target;            // 目标文件；临时文件就写在它所在的那个目录里
    std::vector<ChainStep> steps;  // 按这次序跑，至少要一步
    // 目标已存在时先留一份 .bak 再覆盖。与单步处理的「覆盖并备份」是同一套
    // （backup_before_overwrite：原文件不存在或是空文件时不留）。
    bool backup = true;
    // 两步之间问一次：返回真就不再跑下一步。正在写的那一步写完才问，写到一半不会被打断。
    // 空表示不按停止。
    std::function<bool()> should_stop;
};

struct ChainResult {
    TextEncoding encoding = TextEncoding::Utf8;  // 产物按哪个编码写的
    std::size_t finished_steps = 0;              // 跑完了几步
    bool stopped = false;   // 按了停止：目标文件一个字没动，临时文件也删干净了
};

// 跑一条链：成功就把最后一步的产物改名成 target（target 已存在则覆盖，backup 为真时先留 .bak）。
// 任何一步失败都抛 std::runtime_error，消息里带第几步与那个步骤的名字；抛之前这一趟的
// 临时文件已经删掉，原稿与目标文件都没被动过。
// 按停止不抛：结果里 stopped 为真，finished_steps 是停止前跑完的步数。
ChainResult run_chain(const ChainRequest& request);

// 预览：跑同一条链，产物留在临时文件里（不碰目标文件，也不留 .bak），读开头若干字交回，
// 读完把临时文件删掉。request.target 只用它所在的目录，临时文件写在那里，目标文件本身不写。
struct ChainPreview {
    std::string text;   // 产物开头那一段，已解成 UTF-8
    TextEncoding encoding = TextEncoding::Utf8;
    bool truncated = false;  // 后面还有，完整结果要正式跑一遍才有
    bool stopped = false;    // 按了停止，text 为空
};

// 读够 max_chars 个字就停手，时间与内存都按 max_chars 算，与稿子多长无关。
// 失败与 run_chain 一样抛；按停止不抛。max_chars 为 0 时 text 为空、truncated 按后面还有没有定。
ChainPreview preview_chain(const ChainRequest& request, std::size_t max_chars);

// 删掉目录里强杀那次留下的临时文件：名字对得上（两套前缀之一，加进程号与步序号）、
// 写它的那个进程已经不在了的，删掉。进程还在的留着，那是另一份正在跑的活。
// 返回删掉了几份。目录读不了就当没有，返回 0：启动时扫一遍，不该因为这个起不来。
//
// 名字形状是 .texttool-tmp-<进程号>-<步序号>（正式跑）与 .texttool-preview-<进程号>-<步序号>
// （预览），步序号从 1 数。以点开头是为了在目录列表里排在前面，一眼看得出来是临时产物。
std::size_t sweep_stale_chain_temps(const std::string& directory);

}  // namespace texttool
