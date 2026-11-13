// 多步链的驱动：命令行与界面都还没挂这条链，链本身只有单测跑得到，而
// tests/compare_chain.sh 要按命令行跑同一条链、与 fixtures/processing/chain 里的基准比。
// 这个程序只干一件事：按给的次序把链跑一遍，不打印结果，成败看退出码。
//
// 用法：chain_runner <源文件> <目标文件> <步骤>...
// 步骤名与命令行子命令同名，用的就是那条子命令不带选项时的参数：
//   merge    接回断行（MergeOptions 的默认值）
//   replace  换标点（规则从 replace_rules.json 读，与命令行 replace 不带 --scheme 时同一份，
//            位置见 engine/schemes.h：先看 TEXTTOOL_REPLACE_RULES_JSON，没有就是程序目录那份）
//   vn       剧本排版（VnOptions 的默认值：排版加补标点，每段上限 450）
//
// 参数没变，产物就该与按同样次序跑两遍命令行的产物逐字节相同。基准由命令行按同一次序
// 跑两步生成，这个程序只负责把同一条链跑一遍。
//
// 退出码：0 = 跑完，1 = 跑不动（原因写到标准错误），2 = 用法不对。
#include <cstdio>
#include <exception>
#include <string>
#include <vector>

#include "engine/chain.h"
#include "engine/schemes.h"

namespace {

int usage() {
    std::fputs(
        "用法：chain_runner <源文件> <目标文件> <步骤>...\n"
        "步骤：merge（接回断行）、replace（换标点）、vn（剧本排版），\n"
        "      每个都按命令行不带选项时的参数跑。\n",
        stderr);
    return 2;
}

}  // namespace

int main(int argc, char** argv) {
    if (argc < 4) return usage();

    std::vector<std::string> names(argv + 3, argv + argc);

    // 链里有换标点这一步才去读规则：其余步骤用不到，读不出来的规则不该挡住它们。
    std::vector<texttool::ReplaceRule> rules;
    for (const std::string& name : names) {
        if (name != "replace") continue;
        std::string error;
        if (!texttool::load_replace_rules(argv[0], rules, error)) {
            std::fprintf(stderr, "错误：%s\n", error.c_str());
            return 1;
        }
        // 命令行这时会报「没有可用的替换规则」并退 2，这里也停住，不拿空表跑出一条假产物。
        if (rules.empty()) {
            std::fputs("错误：没有可用的替换规则，用 TEXTTOOL_REPLACE_RULES_JSON 指到 replace_rules.json\n",
                       stderr);
            return 1;
        }
        break;
    }

    texttool::ChainRequest request;
    request.source = argv[1];
    request.target = argv[2];
    for (const std::string& name : names) {
        texttool::ChainStep step;
        if (name == "merge") {
            step.kind = texttool::ChainStepKind::MergeLines;
        } else if (name == "replace") {
            step.kind = texttool::ChainStepKind::ReplacePunctuation;
            step.replace.rules = rules;
        } else if (name == "vn") {
            step.kind = texttool::ChainStepKind::ReformatVn;
        } else {
            std::fprintf(stderr, "认不出的步骤：%s\n", name.c_str());
            return usage();
        }
        request.steps.push_back(std::move(step));
    }

    try {
        texttool::run_chain(request);
    } catch (const std::exception& ex) {
        std::fprintf(stderr, "错误：%s\n", ex.what());
        return 1;
    }
    return 0;
}
