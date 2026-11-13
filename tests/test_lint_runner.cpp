// 判据执行：按规则表扫出命中。
//
// 每条数据规则（规则表里 97 条正则 + 3 条词面）配一个正例与一个反例：正例钉住处数、
// 第一处命中的行列与原文，反例要求一处都不报。样本与期望值是按判据逐个对过的：
// 同一份规则表、同一个档位、--only 点名同一条规则，跑出来的结果原样写在这里的表里。
//
// 跳过语义、MinCount、TailChars、重叠去重、超时、排序各有测试，样本与 fixtures/cli 里的稿件同一份。
#include <algorithm>
#include <chrono>
#include <cstddef>
#include <cstdio>
#include <fstream>
#include <limits>
#include <sstream>
#include <stdexcept>
#include <string>
#include <string_view>
#include <vector>

#include "doctest/doctest.h"
#include "engine/encoding.h"
#include "engine/lint_runner.h"
#include "engine/report.h"
#include "engine/rules.h"
#include "json/json.hpp"

namespace {

using texttool::LintHit;
using texttool::LintOptions;
using texttool::LintOutcome;
using texttool::LintRule;
using texttool::RuleKind;

// 规则表只读一次：每条测试都重新读盘，测到的就不只是引擎了。
const std::vector<LintRule>& rule_table() {
    static const std::vector<LintRule> rules = texttool::load_rules(TEXTTOOL_RULES_JSON);
    return rules;
}

std::string read_file(const std::string& path) {
    std::ifstream in(path, std::ios::binary);
    REQUIRE_MESSAGE(in.good(), "打不开文件：", path);
    std::ostringstream buf;
    buf << in.rdbuf();
    return buf.str();
}

// 重复一个字串，用来拼长度样本（汉字是多字节，不能按 char 重复）。
std::string repeat(const std::string& unit, std::size_t times) {
    std::string out;
    for (std::size_t i = 0; i < times; ++i) out += unit;
    return out;
}

LintOutcome scan(const std::vector<std::string>& only, std::string_view genre, std::string_view text) {
    LintOptions options;
    options.only_rules = only;
    options.genre = std::string(genre);
    return texttool::run_lint(rule_table(), text, options);
}

LintOutcome scan_one(std::string_view id, std::string_view genre, std::string_view text) {
    return scan({std::string(id)}, genre, text);
}

// 命中只带规则下标与字节区间：编号回规则表查，原文按区间从稿子里切。
const LintRule& rule_of(const LintOutcome& outcome, const LintHit& hit) {
    REQUIRE(outcome.rules != nullptr);
    return (*outcome.rules)[hit.rule];
}

std::string_view match_of(const LintOutcome& outcome, const LintHit& hit) {
    return outcome.fields().match_of(hit);
}

std::size_t count_of(const LintOutcome& outcome, std::string_view id) {
    std::size_t count = 0;
    for (const auto& hit : outcome.hits)
        if (rule_of(outcome, hit).id == id) ++count;
    return count;
}

// 一条数据规则的样本。hits/line/column/match 说的是正例的第一处命中所在。
struct RuleSample {
    const char* id;
    const char* genre;  // 空表示不限档位
    const char* positive;
    const char* negative;
    std::size_t hits;
    std::size_t line;
    std::size_t column;
    const char* match;
};

const RuleSample kRuleSamples[] = {
    {"repeated-punctuation", "",
     "他停了一下，等一下。。。我看看。，又继续往下写。",
     "等一下...我看看。",
     1, 1, 10, "。。。"},
    {"ellipsis-style", "",
     "他停了一下，等一下。。。我看看。，又继续往下写。",
     "我抬头看了看表，还早，就又坐下了。",
     1, 1, 10, "。。。"},
    {"mixed-punctuation-width", "",
     "他停了一下，,㐀，又继续往下写。",
     "他推门进屋，把外套挂在门后，屋里一个人也没有。",
     1, 1, 6, "，,"},
    {"dash-and-tilde", "",
     "他停了一下，一 -- 一，又继续往下写。",
     "结论是这一条先放着。",
     1, 1, 7, "一 -- 一"},
    {"dash-run", "doc",
     "他停了一下，——，——，又继续往下写。",
     "那双闪烁着智慧的眼眸，像两颗看穿了所有套路的宝石。",
     2, 1, 7, "——"},
    {"dash-pause-in-reply", "agent",
     "它的前缀赫然写着——那个旧名字。",
     "论文的数据截至 2024 年 6 月，全文于同年 10 月修订。",
     1, 1, 9, "——"},
    {"tilde-run", "doc",
     "他停了一下，~~，又继续往下写。",
     "他推门进屋，把外套挂在门后，屋里一个人也没有。",
     1, 1, 7, "~~"},
    {"invisible-characters", "",
     "这里有\u200B隐形字符。",
     "这段话说得干净利落，没有问题。",
     1, 1, 4, "\u200B"},
    {"emoji-and-symbols", "",
     "他停了一下，☀，又继续往下写。",
     "WSL2 上核 SHA256，注意 UTF8 与 GPT4 的差别",
     1, 1, 7, "☀"},
    {"foreign-script-mix", "",
     "他停了一下，Ѐ，又继续往下写。",
     "他推门进屋，把外套挂在门后，屋里一个人也没有。",
     1, 1, 7, "Ѐ"},
    {"char-face", "doc",
     "流水线是 读文件 → 合并 → 写输出。",
     "见 [关于本站](https://twilight-rain.example/about/) 与 [另一处](a--b)。",
     2, 1, 10, "→"},
    {"cliche-words", "",
     "他停了一下，a<b&c'd+e>f`g\"赋能，又继续往下写。",
     "他推门进屋，把外套挂在门后，屋里一个人也没有。",
     1, 1, 21, "赋能"},
    {"deck-speak", "doc",
     "他停了一下，降本，降本，又继续往下写。",
     "他推门进屋，把外套挂在门后，屋里一个人也没有。",
     2, 1, 7, "降本"},
    {"ai-frequent-words", "doc",
     "他停了一下，此外，此外，此外。，又继续往下写。",
     "这不是数据问题而是口径问题。",
     3, 1, 7, "此外"},
    {"vague-attribution", "",
     "他停了一下，行业报告显示，又继续往下写。",
     "他推门进屋，把外套挂在门后，屋里一个人也没有。",
     1, 1, 7, "行业报告显示"},
    {"training-cutoff-disclaimer", "",
     "我的知识截止到 2024 年 6 月，之后的进展我不了解。",
     "论文的数据截至 2024 年 6 月，全文于同年 10 月修订。",
     1, 1, 1, "我的知识截止到 2024 年"},
    {"chatbot-artifacts", "",
     "他停了一下，希望这对您有所帮助，又继续往下写。",
     "他推门进屋，把外套挂在门后，屋里一个人也没有。",
     1, 1, 7, "希望这对您有所帮助"},
    {"sycophancy", "",
     "他停了一下，好问题！我看看。，又继续往下写。",
     "他问了一句，对方想了很久才回答。",
     1, 1, 7, "好问题！"},
    {"filler-phrases", "",
     "他停了一下，…在这个时间点…，又继续往下写。",
     "他抬头看了看表，时间还早。",
     1, 1, 8, "在这个时间点"},
    {"parallel-contrast", "",
     "他停了一下，这不是A， 而是B。，又继续往下写。",
     "他推门进屋，把外套挂在门后，屋里一个人也没有。",
     1, 1, 8, "不是A， 而是"},
    {"triadic-parallel", "",
     "我将用最简单、最直接、最不绕弯子的语言告诉你。",
     "他买了米、面、油，还有一袋盐。",
     1, 1, 4, "最简单、最直接、最不绕弯子的语言"},
    {"paragraph-ordinals", "doc",
     "他停了一下，首先|其次|最后，又继续往下写。",
     "先把账算清，然后谈方案。",
     3, 1, 7, "首先"},
    {"elevated-ending", "doc",
     "他停了一下，未来可期，又继续往下写。",
     "这一步做完，剩下的明天再说。",
     1, 1, 7, "未来可期"},
    {"stacked-modifiers", "doc",
     "他停了一下，的的的，又继续往下写。",
     "他推门进屋，把外套挂在门后，屋里一个人也没有。",
     1, 1, 7, "的的的"},
    {"fiction-modifier-chain", "fiction",
     "门外站着一个穿着湿透工装、头发黏在额角、眼神浑浊得像隔夜茶水的、从北方小镇来的、在雨夜里迷路的、名叫阿川的青年。",
     "银铃般的笑声、富有磁性的声音、深邃的眼眸。",
     1, 1, 31, "的、从北方小镇来的、在雨夜里迷路的、名叫阿川的"},
    {"copula-avoidance", "",
     "他停了一下，作为的象征，又继续往下写。",
     "他推门进屋，把外套挂在门后，屋里一个人也没有。",
     1, 1, 7, "作为的象征"},
    {"inline-title-list", "",
     "- **要点**：说明文字",
     "这一行是普通叙述，不是要点列表。",
     1, 1, 1, "- **要点**："},
    {"forced-reversal", "",
     "一大段文字会分散用户注意力，决策点被埋在里面，人机沟通效率反而下降。",
     "他推门进屋，把外套挂在门后，屋里一个人也没有。",
     1, 1, 30, "反而"},
    {"accounting-cliche", "",
     "他停了一下，算账，又继续往下写。",
     "nvidia-smi 报的是驻留量，于是读数反而变小，看起来还有余量。",
     1, 1, 7, "算账"},
    {"accounting-word-misuse", "",
     "它写了一遍自己怎么修坏了自己，这件事本身值得记一笔。",
     "值得记下来的细节都写在本子上。",
     1, 1, 21, "值得记一笔"},
    {"adverb-overuse", "",
     "他停了一下，猛地，猛地，又继续往下写。",
     "他推门进屋，把外套挂在门后，屋里一个人也没有。",
     2, 1, 7, "猛地"},
    {"violent-verbs", "doc",
     "他停了一下，砍一刀，又继续往下写。",
     "他推门进屋，把外套挂在门后，屋里一个人也没有。",
     1, 1, 7, "砍一刀"},
    {"hedging-ending", "",
     "以上数据仅供参考，具体以合同为准。",
     "需要首先说明的是，以下表述仅旨在提供一种非穷尽性的分析视角，而不应被理解为对任何结论的无条件确认；",
     1, 1, 5, "仅供参考"},
    {"disclaimer-stack", "",
     "此处不排除其他解释的可能性，也不宜视为唯一、最终的答案，具体仍以现场核实的结果为准。",
     "需要首先说明的是，以下表述仅旨在提供一种非穷尽性的分析视角，而不应被理解为对任何结论的无条件确认；",
     3, 1, 3, "不排除"},
    {"hedge-denial-pile", "doc",
     "测试通过不代表代码正确，测试失败不代表代码错误，哈希一致不代表项目可用。",
     "这不完全等同于他不同意，但也不能说明他反对。",
     3, 1, 5, "不代表"},
    {"anthropomorphic-subject", "doc",
     "他停了一下，需求问的是，又继续往下写。",
     "他推门进屋，把外套挂在门后，屋里一个人也没有。",
     1, 1, 7, "需求问的是"},
    {"doc-process-residue", "doc",
     "他停了一下，第一阶段完成，又继续往下写。",
     "产物是 TextTool-GUI-3.0.1-win-x64.zip，旁边还有 TextTool-CLI-3.0.1-win-x64.zip。",
     1, 1, 7, "第一阶段完成"},
    {"no-debt-declaration", "doc",
     "他停了一下，无未解决项，又继续往下写。",
     "这条不是缺陷：边界就是三次。",
     1, 1, 7, "无未解决项"},
    {"completion-log", "doc",
     "他停了一下，当时留下的，又继续往下写。",
     "见 [关于本站](https://twilight-rain.example/about/) 与 [另一处](a--b)。",
     1, 1, 7, "当时留下的"},
    {"supersession", "doc",
     "他停了一下，原为三十秒，现为，又继续往下写。",
     "这条不是缺陷：边界就是三次。",
     1, 1, 7, "原为三十秒，现为"},
    {"tombstone-of-deletion", "doc",
     "他停了一下，盘上已无旧快照，又继续往下写。",
     "这条不是缺陷：边界就是三次。",
     1, 1, 7, "盘上已无旧快照"},
    {"re-report-suppression", "doc",
     "这条不是缺陷：边界就是三次。",
     "这条已经说过了，不再重复。",
     1, 1, 3, "不是缺陷"},
    {"assistant-persona", "",
     "他停了一下，敏锐到让人直呼内行。，又继续往下写。",
     "他看透了一切，还是照常吃饭睡觉。",
     1, 1, 12, "直呼内行"},
    {"meta-commentary-marker", "",
     "他停了一下，这一条，我认，又继续往下写。",
     "他推门进屋，把外套挂在门后，屋里一个人也没有。",
     1, 1, 7, "这一条，我认"},
    {"fiction-body-tics", "fiction",
     "他停了一下，眼中闪过，又继续往下写。",
     "产物是 TextTool-GUI-3.0.1-win-x64.zip，旁边还有 TextTool-CLI-3.0.1-win-x64.zip。",
     1, 1, 7, "眼中闪过"},
    {"fiction-physio-tics", "fiction",
     "他停了一下，他瞳孔骤然收缩。，又继续往下写。",
     "他的瞳孔在背光里缩了一下，随即又放开。",
     1, 1, 8, "瞳孔骤然收缩"},
    {"fiction-permission-consoling", "fiction",
     "帕利卡里：「不过，那也不意味着你必须永远待在那里。」",
     "帕利卡里：「你是不想再管理那么多事情了，还是发现自己其实根本不喜欢命令别人？」",
     1, 1, 11, "也不意味着你必须"},
    {"fiction-choice-diagnosis", "fiction",
     "帕利卡里：「你是不想再管理那么多事情了，还是发现自己其实根本不喜欢命令别人？」",
     "帕利卡里：「不过，那也不意味着你必须永远待在那里。」",
     1, 1, 27, "其实根本不喜欢命令别人"},
    {"fiction-beat-action", "fiction",
     "他停了一下，他停了一会儿，又说，又继续往下写。",
     "帕利卡里：「你是不想再管理那么多事情了，还是发现自己其实根本不喜欢命令别人？」",
     1, 1, 7, "他停了一会儿，又说"},
    {"fiction-sensation-tics", "fiction",
     "他停了一下，我的大脑一片空白。，又继续往下写。",
     "脑子一时没转过来，他停了几秒。",
     1, 1, 9, "大脑一片空白"},
    {"fiction-synonym-chain", "fiction",
     "她目光一凛，眼神一凛，把话咽了回去。",
     "她声音很轻，像是在说一件小事。",
     2, 1, 2, "目光一凛"},
    {"fiction-atmosphere-props", "fiction",
     "空气仿佛凝滞，空气仿佛凝滞",
     "他的瞳孔在背光里缩了一下，随即又放开。",
     2, 1, 1, "空气仿佛凝滞"},
    {"fiction-dominant-register", "fiction",
     "他停了一下，不容置疑，又继续往下写。",
     "他的瞳孔在背光里缩了一下，随即又放开。",
     1, 1, 7, "不容置疑"},
    {"fiction-filler-modifiers", "fiction",
     "他停了一下，一丝，一丝，一丝，又继续往下写。",
     "他推门进屋，把外套挂在门后，屋里一个人也没有。",
     3, 1, 7, "一丝"},
    {"fiction-dead-metaphor", "fiction",
     "他停了一下，仿佛一般，又继续往下写。",
     "他推门进屋，把外套挂在门后，屋里一个人也没有。",
     1, 1, 7, "仿佛一般"},
    {"fiction-emotion-telling", "fiction",
     "他停了一下，他这才明白，又继续往下写。",
     "他想了一会儿，才把手放下。",
     1, 1, 7, "他这才明白"},
    {"fiction-vague-nominalization", "fiction",
     "他停了一下，有一种的东西，又继续往下写。",
     "他推门进屋，把外套挂在门后，屋里一个人也没有。",
     1, 1, 7, "有一种的东西"},
    {"fiction-vague-simile", "fiction",
     "他停了一下，像是的感觉，又继续往下写。",
     "他推门进屋，把外套挂在门后，屋里一个人也没有。",
     1, 1, 7, "像是的感觉"},
    {"fiction-community-taste", "fiction",
     "他停了一下，像石子投，又继续往下写。",
     "nvidia-smi 报的是驻留量，于是读数反而变小，看起来还有余量。",
     1, 1, 7, "像石子投"},
    {"fiction-proportion-eyes", "fiction",
     "他眼里带着一分不屑、两分不满。",
     "这牛排三分熟，八分饱就够了。",
     2, 1, 6, "一分不屑"},
    {"fiction-text-self-reference", "fiction",
     "这段文字读起来刺眼。这篇文章里满屏都是同一个句式，我盯着那篇文章看了很久。",
     "那句话像石子落进湖里，他的心跳像齿轮卡住，眼前像蒙了一层潮水，连镜子里的脸都像羽毛。",
     4, 1, 1, "这段文字"},
    {"fiction-dialogue-reference", "fiction",
     "他停了一下，这句话，这句话，又继续往下写。",
     "那句话像石子落进湖里，他的心跳像齿轮卡住，眼前像蒙了一层潮水，连镜子里的脸都像羽毛。",
     2, 1, 7, "这句话"},
    {"fiction-narrator-formula", "fiction",
     "话说天下大势。且说那一日。却说这人来了。",
     "他推门进屋，把外套挂在门后。",
     3, 1, 1, "话说"},
    {"fiction-metaphor-lexicon", "fiction",
     "那句话像石子落进湖里，他的心跳像齿轮卡住，眼前像蒙了一层潮水，连镜子里的脸都像羽毛。",
     "这段文字读起来刺眼。这篇文章里满屏都是同一个句式，我盯着那篇文章看了很久。",
     5, 1, 5, "石子"},
    {"latin-word-in-cjk", "fiction",
     "他打开 iPhone 看了一眼。",
     "他推门进屋，把外套挂在门后，屋里一个人也没有。",
     1, 1, 3, "开 iPhone 看"},
    {"fiction-bold-template", "fiction",
     "系统判定：**低熵表达，情感裸奔，价格协商未经修辞镀层。**",
     "那一行字印得特别重，像有人用铅笔描过两遍。",
     1, 1, 6, "**低熵表达，情感裸奔，价格协商未经修辞镀层。**"},
    {"fiction-machine-numbers", "fiction",
     "磨损0.5毫米道德镀层。",
     "系统判定：**低熵表达，情感裸奔，价格协商未经修辞镀层。**",
     1, 1, 3, "0.5毫米"},
    {"synopsis-formula", "fiction",
     "他停了一下，在这个里，每个人，又继续往下写。",
     "他推门进屋，把外套挂在门后，屋里一个人也没有。",
     1, 1, 7, "在这个里，每个人"},
    {"trailer-ending", "fiction",
     "所有的答案都将在接下来的探险中揭晓。",
     "她问了一句，没人回答，风把窗帘吹得鼓起来。",
     1, 1, 4, "答案都将在接下来的探险中揭晓"},
    {"outline-residue", "fiction",
     "悬念1：法师公会的真正目的是什么？",
     "而他的冒险旅程也才刚刚开始，前方还有更多的未知和挑战等待着他……",
     1, 1, 1, "悬念1："},
    {"synopsis-register", "fiction",
     "他停了一下，探索，探索，又继续往下写。",
     "而他的冒险旅程也才刚刚开始，前方还有更多的未知和挑战等待着他……",
     2, 1, 7, "探索"},
    {"connective-scene-shift", "fiction",
     "他停了一下，然而，在，然而，在，又继续往下写。",
     "而他的冒险旅程也才刚刚开始，前方还有更多的未知和挑战等待着他……",
     2, 1, 7, "然而，在"},
    {"agent-commit-hash", "agent",
     "提交 9f7761a，把那条判据从文档档收回来了。",
     "产物是 TextTool-GUI-3.0.1-win-x64.zip，旁边还有 TextTool-CLI-3.0.1-win-x64.zip。",
     1, 1, 4, "9f7761a"},
    {"agent-code-list", "agent",
     "内置 19 条数据规则（P1 P2 P3 P6 P7 P8 / L1–L7 / S1–S6）。",
     "产物是 TextTool-GUI-3.0.1-win-x64.zip，旁边还有 TextTool-CLI-3.0.1-win-x64.zip。",
     10, 1, 13, "P1"},
    {"agent-path-list", "agent",
     "规则在 TextTool.Core/default_lint_rules.json，测试在 LintRuleStoreTests.cs。",
     "跑 texttool lint --only parallel-contrast --json 看命中。",
     2, 1, 5, "TextTool.Core/default_lint_rules.json"},
    {"agent-cli-option", "agent",
     "跑 texttool lint --only parallel-contrast --json 看命中。",
     "产物是 TextTool-GUI-3.0.1-win-x64.zip，旁边还有 TextTool-CLI-3.0.1-win-x64.zip。",
     2, 1, 17, "--only"},
    {"agent-smoke-jargon", "agent",
     "他停了一下，agent-smoke-jargon，又继续往下写。",
     "产物是 TextTool-GUI-3.0.1-win-x64.zip，旁边还有 TextTool-CLI-3.0.1-win-x64.zip。",
     1, 1, 13, "smoke"},
    {"agent-test-jargon", "agent",
     "加四条回归用例把这次体检测出来的行为钉住。",
     "产物是 TextTool-GUI-3.0.1-win-x64.zip，旁边还有 TextTool-CLI-3.0.1-win-x64.zip。",
     2, 1, 4, "回归"},
    {"agent-process-jargon", "agent",
     "加四条回归用例把这次体检测出来的行为钉住。",
     "产物是 TextTool-GUI-3.0.1-win-x64.zip，旁边还有 TextTool-CLI-3.0.1-win-x64.zip。",
     1, 1, 11, "体检"},
    {"agent-inline-code", "agent",
     "`1`，`1`，`1`，`1`，`1`",
     "产物是 TextTool-GUI-3.0.1-win-x64.zip，旁边还有 TextTool-CLI-3.0.1-win-x64.zip。",
     5, 1, 1, "`1`"},
    {"agent-truncated-path", "agent",
     "改 src/engine/… 这一段就行。",
     "改 src/engine/rules.cpp 这一段就行。",
     1, 1, 3, "src/engine/…"},
    {"numbered-reference", "agent",
     "这条判据按 ADR-12 定的。",
     "任务完成了，阶段性的东西明天再说。",
     1, 1, 7, "ADR-12"},
    {"agent-conclusion-first", "agent",
     "他停了一下，先说结论，又继续往下写。",
     "WSL2 上核 SHA256，注意 UTF8 与 GPT4 的差别",
     1, 1, 7, "先说结论"},
    {"agent-permission-ask", "agent",
     "他停了一下，要不要我，又继续往下写。",
     "他推门进屋，把外套挂在门后，屋里一个人也没有。",
     1, 1, 7, "要不要我"},
    {"agent-one-char-commitment", "agent",
     "他停了一下，这一条，我认，又继续往下写。",
     "他推门进屋，把外套挂在门后，屋里一个人也没有。",
     1, 1, 7, "这一条，我认"},
    {"agent-receipt-commitment", "agent",
     "他停了一下，我记下了，又继续往下写。",
     "他推门进屋，把外套挂在门后，屋里一个人也没有。",
     1, 1, 7, "我记下了"},
    {"agent-template-preamble", "agent",
     "他停了一下，具有一定的启发性，又继续往下写。",
     "他推门进屋，把外套挂在门后，屋里一个人也没有。",
     1, 1, 7, "具有一定的启发性"},
    {"agent-weight-comparison", "agent",
     "他停了一下，这句话很稳，又继续往下写。",
     "他推门进屋，把外套挂在门后，屋里一个人也没有。",
     1, 1, 7, "这句话很稳"},
    {"agent-keepsake", "agent",
     "他停了一下，这话我记着，又继续往下写。",
     "他推门进屋，把外套挂在门后，屋里一个人也没有。",
     1, 1, 7, "这话我记着"},
    {"agent-measure-invite", "agent",
     "他停了一下，你自己掂量，又继续往下写。",
     "他推门进屋，把外套挂在门后，屋里一个人也没有。",
     1, 1, 7, "你自己掂量"},
    {"agent-caretaker-line", "agent",
     "他停了一下，回去睡，又继续往下写。",
     "他推门进屋，把外套挂在门后，屋里一个人也没有。",
     1, 1, 7, "回去睡"},
    {"agent-probe-question", "agent",
     "他停了一下，问别人还是问自己，又继续往下写。",
     "他推门进屋，把外套挂在门后，屋里一个人也没有。",
     1, 1, 7, "问别人还是问自己"},
    {"agent-bare-verb-closer", "agent",
     "他停了一下，接上，又继续往下写。",
     "他推门进屋，把外套挂在门后，屋里一个人也没有。",
     1, 1, 7, "接上"},
    {"agent-agreement-opener", "agent",
     "你说得对，这确实是模板化句式的典型特征。",
     "嗯，用户提出了一个观点，内容是关于模板化句式。",
     1, 1, 1, "你说得对"},
    {"agent-deliberation-leak", "agent",
     "嗯，用户提出了一个观点，内容是关于模板化句式。",
     "你说得对，这确实是模板化句式的典型特征。",
     1, 1, 3, "用户提出"},
    {"agent-reassurance-pile", "agent",
     "他停了一下，稳稳地接住，稳稳地接住，又继续往下写。",
     "他推门进屋，把外套挂在门后，屋里一个人也没有。",
     2, 1, 7, "稳稳地接住"},
    {"agent-self-intro", "agent",
     "作为一个大语言模型，我必须诚实地说：这个方案有风险。",
     "一句话总结：先做能跑通的那条。让我直接告诉您原因。",
     2, 1, 1, "作为一个大语言模型"},
    {"agent-error-paste", "doc",
     "页面提示服务器繁忙，再试又显示请稍后再试。",
     "作为一个大语言模型，我必须诚实地说：这个方案有风险。",
     2, 1, 5, "服务器繁忙"},
    {"agent-flattery", "agent",
     "你非常敏锐，一下就看到了关键。",
     "作为一个大语言模型，我必须诚实地说：这个方案有风险。",
     1, 1, 1, "你非常敏锐"},
    {"agent-conclusion-preamble", "agent",
     "一句话总结：先做能跑通的那条。让我直接告诉您原因。",
     "作为一个大语言模型，我必须诚实地说：这个方案有风险。",
     2, 1, 1, "一句话总结"},
};

std::string compare(const std::string& what, std::size_t local, std::size_t reference) {
    return what + "，本地 " + std::to_string(local) + "，对照基准 " + std::to_string(reference);
}

}  // namespace

TEST_CASE("每条数据规则的正例与反例") {
    for (const auto& sample : kRuleSamples) {
        INFO((std::string("规则 ") + sample.id));
        const LintOutcome positive = scan_one(sample.id, sample.genre, sample.positive);
        REQUIRE_MESSAGE(!positive.hits.empty(), "正例一处都没报");
        CHECK_MESSAGE(positive.hits.size() == sample.hits,
                      compare("处数", positive.hits.size(), sample.hits));
        const LintHit& first = positive.hits.front();
        CHECK(rule_of(positive, first).id == sample.id);
        CHECK_MESSAGE(first.line == sample.line, compare("行号", first.line, sample.line));
        CHECK_MESSAGE(first.column == sample.column, compare("列号", first.column, sample.column));
        CHECK_MESSAGE(match_of(positive, first) == sample.match,
                      ("命中原文，本地「" + std::string(match_of(positive, first)) +
                       "」，对照基准「" + sample.match + "」"));

        const LintOutcome negative = scan_one(sample.id, sample.genre, sample.negative);
        CHECK_MESSAGE(negative.hits.empty(), compare("反例处数", negative.hits.size(), 0));
    }
}

TEST_CASE("全文字数按 UTF-16 码元") {
    // 基本平面以外的一个字符占两个码元，与报告里数行列用的是一个数法
    CHECK(scan_one("cliche-words", "", "😀ab").units == 4);
    CHECK(scan_one("cliche-words", "", "第一行\n第二行有 赋能 二字").units == 14);
}

TEST_CASE("Genres：不在档位里的规则不跑") {
    const std::string text = "他停了一下，~~，又继续往下写。";
    CHECK(count_of(scan({}, "doc", text), "tilde-run") > 0);
    CHECK(count_of(scan({}, "fiction", text), "tilde-run") == 0);

    // 点名一条被档位挡掉的规则要报错：否则读者看到的是零命中，而真相是这条规则不在这个档
    bool thrown = false;
    try {
        scan({"tilde-run"}, "fiction", text);
    } catch (const std::runtime_error& error) {
        thrown = true;
        CHECK(std::string(error.what()).find("tilde-run") != std::string::npos);
    }
    CHECK(thrown);
}

TEST_CASE("DefaultOff：不点名不跑，点名就跑") {
    const std::string text = "他打开 iPhone 看了一眼。";
    CHECK(count_of(scan({}, "fiction", text), "latin-word-in-cjk") == 0);
    CHECK(count_of(scan({"latin-word-in-cjk"}, "fiction", text), "latin-word-in-cjk") > 0);
}

TEST_CASE("SkipInMarkdown：markdown 口径下整条规则不跑") {
    const std::string line_mode = "- **要点**：说明文字";
    const LintOutcome as_lines = scan_one("inline-title-list", "", line_mode);
    CHECK(as_lines.paragraph_mode == "line");
    CHECK(count_of(as_lines, "inline-title-list") == 1);

    // 空行占行数 40%，按 markdown 方式读：这一条判据在 markdown 里是标准写法，整条不跑
    const std::string as_markdown = "- **要点**：说明文字\n\n下一段。\n\n尾段。\n";
    const LintOutcome as_md = scan_one("inline-title-list", "", as_markdown);
    CHECK(as_md.paragraph_mode == "markdown");
    CHECK(as_md.hits.empty());
}

TEST_CASE("SkipInBlog：博文稿上不跑，去掉头部字段就跑") {
    const std::string body = "流水线是 读文件 → 合并 → 写输出。";
    CHECK(count_of(scan({}, "", body), "char-face") > 0);

    // 头部字段里同时有 title 与 date 就是博文；没指定档位时这类判据按博文规则不跑
    const std::string blog = "---\ntitle: 一篇博文\ndate: 2024-06-01\n---\n\n" + body;
    CHECK(count_of(scan({}, "", blog), "char-face") == 0);
}

TEST_CASE("博文结构遮罩：结构区域不进规则，也不进字数") {
    // 正文里一处「反而」，代码块里一处「反而」与一处「赋能」：认成博文之后代码块整段遮掉，
    // 字数为原文全长减去整行被遮的部分（头部字段四行、围栏两行、代码一行）。
    const std::string blog =
        "---\ntitle: 一篇博文\ndate: 2024-06-01\n---\n\n结构之外反而好看。\n\n```\n反而 赋能\n```\n";
    const LintOutcome masked = scan({}, "", blog);
    CHECK(masked.units == 12);
    CHECK(count_of(masked, "forced-reversal") == 1);
    CHECK(count_of(masked, "cliche-words") == 0);
    // 命中原文从原文上切，不是结构区域那份副本里的空格
    REQUIRE(masked.hits.size() == 1);
    CHECK(match_of(masked, masked.hits.front()) == "反而");

    // 同一段文字去掉头部字段就不是博文：一个字都不遮，代码块里那两处照旧报出来
    const std::string plain = "结构之外反而好看。\n\n```\n反而 赋能\n```\n";
    const LintOutcome whole = scan({}, "", plain);
    CHECK(whole.units == 25);
    CHECK(count_of(whole, "forced-reversal") == 2);
    CHECK(count_of(whole, "cliche-words") == 1);
}

TEST_CASE("跨行 HTML 注释里的 :: 不算指令行") {
    // 注释跨行时，注释里那行以 :: 开头的不是指令行，这份稿子就不是博文；
    // 光是头部字段里只有 title 一行也不算博文。两样都不成立，char-face 就没被跳过。
    const std::string text =
        "---\ntitle: 一篇文档\n---\n\n<!-- 注释从这里开始\n:: 不是指令行\n-->\n\n流水线是 读文件 → 合并。\n";
    CHECK(count_of(scan({}, "", text), "char-face") > 0);

    // 同样一行 :: 摆在注释外面就是指令行：成了博文，这条判据按博文规则不跑
    const std::string directive =
        "---\ntitle: 一篇文档\n---\n\n:: 指令行\n\n流水线是 读文件 → 合并。\n";
    CHECK(count_of(scan({}, "", directive), "char-face") == 0);
}

TEST_CASE("MinCount 按段判定") {
    const std::string three = "首先看这件事。\n其次看那件事。";
    const std::string two = "首先看这件事，其次看那件事。";
    CHECK(scan_one("paragraph-ordinals", "doc", three).hits.empty());
    CHECK(count_of(scan_one("paragraph-ordinals", "doc", two), "paragraph-ordinals") == 2);
}

TEST_CASE("TailChars 只看段末") {
    const std::string tail_of_paragraph = "未来可期" + repeat("字", 40);
    const std::string end_of_paragraph = repeat("字", 40) + "未来可期";
    CHECK(scan_one("elevated-ending", "doc", tail_of_paragraph).hits.empty());
    CHECK(count_of(scan_one("elevated-ending", "doc", end_of_paragraph), "elevated-ending") == 1);

    // 窗口按整段算：markdown 方式下两行合成一段，命中落在段首，整段 45 字超出 30 字窗口
    const std::string markdown = "未来可期\n" + repeat("字", 40) + "\n\n尾段。\n";
    CHECK(scan_one("elevated-ending", "doc", markdown).hits.empty());
}

TEST_CASE("触发密度按千字处数判") {
    // triadic-parallel 要求千字一处以上：同一处命中放在长篇里不够密，就不报
    const std::string one = "它更快、更稳、更省心。";
    const std::string paragraph = "他推门进屋，把外套挂在门后。";
    CHECK(scan_one("triadic-parallel", "", one + repeat(paragraph, 200)).hits.empty());
    CHECK(count_of(scan_one("triadic-parallel", "", one + repeat(paragraph, 20)),
                   "triadic-parallel") == 1);
}

TEST_CASE("同一条规则里重叠的候选只留一处") {
    // 两张词表咬住同一段文字时数的是同一处证据：留起点靠前的，另一处让掉
    LintRule rule;
    rule.id = "overlap-probe";
    rule.group = "probe";
    rule.title = "重叠探针";
    rule.kind = RuleKind::Regex;
    rule.patterns = {"口径", "收口"};
    rule.detail = "d";

    const LintOutcome outcome = texttool::run_lint({rule}, "验收口径", LintOptions{});
    REQUIRE(outcome.hits.size() == 1);
    CHECK(match_of(outcome, outcome.hits[0]) == "收口");
    CHECK(outcome.hits[0].column == 2);

    // 候选多到 32 条以上时走的是另一条路径（前缀最大值树），取舍必须与小清单逐条比一致：
    // 每处「验收口径」里两条模式互相咬住，只剩一处
    for (std::size_t groups : {std::size_t{10}, std::size_t{40}}) {
        CAPTURE(groups);
        const std::string many = repeat("验收口径", groups);
        const LintOutcome bulk = texttool::run_lint({rule}, many, LintOptions{});
        CHECK(bulk.hits.size() == groups);
        for (const auto& hit : bulk.hits) CHECK(match_of(bulk, hit) == "收口");
    }
}

TEST_CASE("min_severity 只留警告级") {
    const std::string text = read_file(std::string(TEXTTOOL_FIXTURES_DIR) + "/report/mixed_severity.txt");
    LintOptions options;
    options.min_severity = "warn";

    const LintOutcome outcome = texttool::run_lint(rule_table(), text, options);
    REQUIRE(!outcome.hits.empty());
    for (const auto& hit : outcome.hits) CHECK(rule_of(outcome, hit).severity == "warn");

    // 不设这一项时提示级也在
    const LintOutcome all = texttool::run_lint(rule_table(), text, LintOptions{});
    bool saw_info = false;
    for (const auto& hit : all.hits) saw_info = saw_info || rule_of(all, hit).severity != "warn";
    CHECK(saw_info);
}

TEST_CASE("单条规则超时抛错并带规则编号") {
    LintRule rule;
    rule.id = "slow-probe";
    rule.group = "probe";
    rule.title = "超时探针";
    rule.kind = RuleKind::Regex;
    rule.patterns = {"x"};
    rule.detail = "d";

    const std::string text(200'000, 'x');
    LintOptions options;
    options.rule_time_budget = std::chrono::milliseconds(0);

    bool thrown = false;
    try {
        texttool::run_lint({rule}, text, options);
    } catch (const std::runtime_error& error) {
        thrown = true;
        const std::string message = error.what();
        CHECK(message.find("slow-probe") != std::string::npos);
    }
    CHECK(thrown);
}

TEST_CASE("同位置的多条命中保持规则表先后") {
    // repeated-punctuation 与 ellipsis-style 在「。。。。」上是同跨度双报，次序不得被排序打乱
    const std::vector<std::string> order = {"repeated-punctuation", "ellipsis-style"};
    const LintOutcome outcome = scan(order, "", "反复。。。。");
    REQUIRE(outcome.hits.size() == 2);
    CHECK(rule_of(outcome, outcome.hits[0]).id == "repeated-punctuation");
    CHECK(rule_of(outcome, outcome.hits[1]).id == "ellipsis-style");
    CHECK(outcome.hits[0].column == 3);
    CHECK(outcome.hits[1].column == 3);
}

TEST_CASE("命中顺序与对照基准一致") {
    struct Fixture {
        const char* name;
        const char* suffix;
    };
    const Fixture fixtures[] = {{"mixed_severity", ".txt"}, {"real_doc_architecture", ".md"}};
    const std::string dir = std::string(TEXTTOOL_FIXTURES_DIR) + "/report/";

    for (const auto& fixture : fixtures) {
        CAPTURE(fixture.name);
        const std::string text = read_file(dir + fixture.name + fixture.suffix);
        const auto expected =
            nlohmann::json::parse(read_file(dir + std::string(fixture.name) + ".expected.json"));

        // 整表按 (行, 列) 全局升序排过；三类规则都在同一趟里跑，比的是整表
        std::vector<std::string> want;
        for (const auto& hit : expected.at("Reports").at(0).at("Hits")) {
            const std::string id = hit.at("Id").get<std::string>();
            want.push_back(id + "@" + std::to_string(hit.at("Line").get<int>()) + ":" +
                           std::to_string(hit.at("Col").get<int>()));
        }

        REQUIRE_MESSAGE(!want.empty(), "对照基准那份基准里没有命中，这条用例就白跑了");

        const LintOutcome outcome = texttool::run_lint(rule_table(), text, LintOptions{});
        std::vector<std::string> got;
        for (const auto& hit : outcome.hits)
            got.push_back(rule_of(outcome, hit).id + "@" + std::to_string(hit.line) + ":" +
                          std::to_string(hit.column));

        REQUIRE(want.size() == got.size());
        for (std::size_t i = 0; i < want.size(); ++i) {
            INFO("第 " << i << " 处：对照基准 " << want[i] << "，本地 " << got[i]);
            CHECK(want[i] == got[i]);
            if (want[i] != got[i]) break;
        }
    }
}

// 结尾带一个被截断的多字节字符时，整篇会被判成 GBK 并照 GBK 解，
// 解出来的正文与原来完全不同，命中也会多得多。这条钉的是耗时：
// 有一条算法规则按句子反复找同一条模式，一次「这一段里没有」要把后面的整篇重扫一遍，
// 句数乘以篇长就是平方级：4MB 的稿子从四秒涨到七十多秒。
// 现在一份稿件上每条模式最多真扫一遍，这类输入与合法输入同一个量级。
//
// 样本按真实情形摆：每句都带一个比况词，合法那份每句就地找得着，很快停下；
// 换成 GBK 解之后比况词全没了，每一句都得扫到篇末才敢说没有：那才是平方级。
// 所以这条走命令行同一条路：先判编码、再解成 UTF-8，然后才是一趟检查。
// 比率取两倍：坏字节那份的命中本来就多得多，去重与排报告的工作量比合法那份大；
// 平方级在这里是几百倍，两倍这条线卡得开。两次各先跑一遍热身，量的是第二遍。
TEST_CASE("结尾截断的多字节字符不会让检查变成平方级") {
    const std::string sentence = "好像，" + repeat("这一段话写得很长但只有逗号", 10);
    const std::string legal_bytes = repeat(sentence + "\n", 5000);  // 2MB 的正文
    // 把最后一个字切掉两字节：切在汉字中间，末尾留下一个不完整的多字节序列
    const std::string broken_bytes = legal_bytes.substr(0, legal_bytes.size() - 2);
    REQUIRE(texttool::detect_encoding(legal_bytes) == texttool::TextEncoding::Utf8);
    REQUIRE(texttool::detect_encoding(broken_bytes) == texttool::TextEncoding::Gbk);

    const std::vector<std::string> only{"fiction-simile-pile"};
    const auto measure = [&only](const std::string& bytes) {
        // 命令行读稿那两步：判编码、解成 UTF-8。坏字节的代价先在这一步体现，
        // 解出来的正文再影响后面那一趟检查。
        const auto decode = [&bytes]() {
            return texttool::to_utf8(bytes, texttool::detect_encoding(bytes));
        };
        const std::string warm = decode();
        scan(only, "", warm);

        const auto started = std::chrono::steady_clock::now();
        const std::string text = decode();
        scan(only, "", text);
        return std::chrono::duration<double>(std::chrono::steady_clock::now() - started).count();
    };

    const double legal_seconds = measure(legal_bytes);
    const double broken_seconds = measure(broken_bytes);
    INFO("合法那份 " << legal_seconds << " 秒，截断那份 " << broken_seconds << " 秒");
    CHECK(broken_seconds <= legal_seconds * 2.0 + 0.02);  // 加上少量固定开销的余量
}

// ---------------------------------------------------------------- 一次运行只编译一遍

namespace {

// 两份稿子的命中逐项拿来比：规则下标、字节区间、行列、长度、次序。命中存的是下标与区间，
// 没有现成的相等比较，逐项写出比的就这几样。
void require_same_hits(const LintOutcome& a, const LintOutcome& b) {
    REQUIRE(a.hits.size() == b.hits.size());
    for (std::size_t i = 0; i < a.hits.size(); ++i) {
        CAPTURE(i);
        CHECK(a.hits[i].rule == b.hits[i].rule);
        CHECK(a.hits[i].begin == b.hits[i].begin);
        CHECK(a.hits[i].end == b.hits[i].end);
        CHECK(a.hits[i].line == b.hits[i].line);
        CHECK(a.hits[i].column == b.hits[i].column);
        CHECK(a.hits[i].length == b.hits[i].length);
        CHECK(a.hits[i].order == b.hits[i].order);
    }
    CHECK(a.units == b.units);
    CHECK(a.paragraph_mode == b.paragraph_mode);
}

}  // namespace

TEST_CASE("编译一次跑两份稿子：结果与每份各编译一遍相同") {
    const std::vector<LintRule>& rules = rule_table();
    const std::string first = read_file(std::string(TEXTTOOL_FIXTURES_DIR) + "/cli/short.txt");
    const std::string second = read_file(std::string(TEXTTOOL_FIXTURES_DIR) + "/cli/long.txt");

    texttool::CompiledRules compiled(rules);
    LintOptions options;
    const LintOutcome compiled_first = texttool::run_lint(compiled, first, options);
    const LintOutcome compiled_second = texttool::run_lint(compiled, second, options);

    const LintOutcome plain_first = texttool::run_lint(rules, first, options);
    const LintOutcome plain_second = texttool::run_lint(rules, second, options);

    require_same_hits(compiled_first, plain_first);
    require_same_hits(compiled_second, plain_second);
    CHECK(compiled_first.rules == &rules);
    CHECK(compiled_second.rules == &rules);
}

TEST_CASE("编译按需进行：被跳过的规则不编译，编译时才会发现的问题也就不报") {
    // 密度基准写了尚未实现的那一种：编译那一刻才报错。规则标了 SkipInMarkdown，
    // 按 markdown 读的稿子上它不跑，也就没有机会报；换成一行一段的稿子就要报。
    LintRule broken;
    broken.id = "bad-density-probe";
    broken.group = "probe";
    broken.title = "密度基准探针";
    broken.kind = RuleKind::Regex;
    broken.patterns = {"探针"};
    broken.detail = "d";
    broken.scope = "document";
    broken.skip_in_markdown = true;
    broken.density = texttool::LintDensity{"window", 1.0, 3};
    const std::vector<LintRule> rules{broken};

    std::string markdown;
    for (int i = 0; i < 200; ++i) markdown += "一行字\n\n";
    markdown += "探针探针探针探针\n";
    CHECK_NOTHROW(texttool::run_lint(rules, markdown, LintOptions{}));

    std::string plain;
    for (int i = 0; i < 200; ++i) plain += "一行字\n";
    plain += "探针探针探针探针\n";
    CHECK_THROWS_AS(texttool::run_lint(rules, plain, LintOptions{}), std::runtime_error);
}

TEST_CASE("基线差分：只留相对基线新增的命中，并按规则给一行新增处数") {
    // 样本与期望值是按判据对过的：同一份规则表、同两段文字，人读报告里只剩一行命中，
    // 统计段写两条新增处数，报告开头写写前那一份的命中总处数。
    const std::string base = "值得注意的是，这不是重点。\n总的来说，这是一种赋能。\n";
    const std::string body = base + "这不仅仅是效率问题，而是范式转变。\n";

    LintOptions base_options;
    base_options.role = texttool::LintScanRole::Baseline;
    base_options.cut_to_named = false;
    const LintOutcome base_outcome = texttool::run_lint(rule_table(), base, base_options);
    const texttool::BaselineCounts counts = texttool::count_baseline(base_outcome);
    CHECK(counts.total_hits == 2);

    LintOptions body_options;
    body_options.role = texttool::LintScanRole::Compared;
    body_options.cut_to_named = false;
    LintOutcome body_outcome = texttool::run_lint(rule_table(), body, body_options);
    texttool::apply_baseline(body_outcome, counts, {}, "info");

    REQUIRE(body_outcome.hits.size() == 1);
    CHECK(rule_of(body_outcome, body_outcome.hits.front()).id == "elevated-ending");
    CHECK(body_outcome.hits.front().line == 3);
    CHECK(body_outcome.hits.front().column == 2);
    REQUIRE(body_outcome.baseline_hits.has_value());
    CHECK(*body_outcome.baseline_hits == 2);

    // 统计段：写前那一份里出现过的规则各一行，按规则编号的字节序排
    REQUIRE(body_outcome.notes.size() == 2);
    CHECK(body_outcome.notes[0].rule_id == "cliche-words");
    CHECK(body_outcome.notes[0].text == "相对基线新增 0 处");
    CHECK(body_outcome.notes[1].rule_id == "elevated-ending");
    CHECK(body_outcome.notes[1].text == "相对基线新增 1 处");
}

TEST_CASE("基线差分：按净增算密度门限的规则，单扫一份报错，对着基线时差分之后才判") {
    const std::vector<LintRule> rules = texttool::parse_rules(R"TXT([
      {"Id":"delta-words","Group":"lexis","Title":"净增词","Kind":"literal",
       "LiteralPatterns":["赋能"],"Detail":"净增","Severity":"info","Scope":"document",
       "Density":{"Basis":"delta","Min":100}},
      {"Id":"plain-words","Group":"lexis","Title":"普通词","Kind":"literal",
       "LiteralPatterns":["重点"],"Detail":"普通","Severity":"info","Scope":"document"}
    ])TXT");

    // 手上没有写前那一份时算不出净增，响亮报错而不是退回按全篇判
    CHECK_THROWS_AS(texttool::run_lint(rules, "赋能\n", LintOptions{}), std::runtime_error);

    const auto scan = [](const std::vector<LintRule>& table, const std::string& text,
                         texttool::LintScanRole role) {
        LintOptions options;
        options.role = role;
        options.cut_to_named = false;
        return texttool::run_lint(table, text, options);
    };

    // 写前那一份里只有「重点」：「赋能」在写后新出现，算新增一处
    const texttool::BaselineCounts counts =
        texttool::count_baseline(scan(rules, "重点\n", texttool::LintScanRole::Baseline));
    CHECK(counts.total_hits == 1);

    LintOutcome body = scan(rules, "赋能\n", texttool::LintScanRole::Compared);
    texttool::apply_baseline(body, counts, {}, "info");
    REQUIRE(body.hits.size() == 1);
    CHECK(rule_of(body, body.hits.front()).id == "delta-words");
    REQUIRE(body.notes.size() == 2);
    CHECK(body.notes[0].rule_id == "delta-words");
    CHECK(body.notes[0].text == "相对基线新增 1 处");
    CHECK(body.notes[1].rule_id == "plain-words");
    CHECK(body.notes[1].text == "相对基线新增 0 处");

    // 门限拉高到净增密度够不着：这一条整条撤掉，新增处数那一行写 0
    std::vector<LintRule> strict = rules;
    strict[0].density = texttool::LintDensity{"delta", 1000.0, std::nullopt};
    LintOutcome dropped = scan(strict, "赋能\n", texttool::LintScanRole::Compared);
    texttool::apply_baseline(dropped, counts, {}, "info");
    CHECK(dropped.hits.empty());
    REQUIRE(dropped.notes.size() == 2);
    CHECK(dropped.notes[0].rule_id == "delta-words");
    CHECK(dropped.notes[0].text == "相对基线新增 0 处");
}

TEST_CASE("同一条规则最多列到上限为止，真实处数照旧数清") {
    // 封顶只管列几处：命中表里一条规则留前 kMaxListedHitsPerRule 处，留的是最早的那几处；
    // 真实处数记在 rule_hits 里，判据、密度门限与报告里的总数都按它算。
    const std::vector<LintRule> rules = texttool::parse_rules(R"TXT([
      {"Id":"probe-word","Group":"probe","Title":"探针词","Kind":"literal",
       "LiteralPatterns":["针"],"Detail":"探针","Severity":"info","Scope":"document"}
    ])TXT");
    const std::string text = repeat("针", 250);

    const LintOutcome outcome = texttool::run_lint(rules, text, LintOptions{});
    CHECK(outcome.hits.size() == texttool::kMaxListedHitsPerRule);
    CHECK(outcome.hits.front().begin == 0);
    CHECK(outcome.hits.back().begin == (texttool::kMaxListedHitsPerRule - 1) * 3);

    REQUIRE(outcome.rule_hits.size() == 1);
    CHECK(outcome.rule_hits[0] == 250);
}

TEST_CASE("要写日志的一趟留全命中，裁给报告时真实处数不动") {
    const std::vector<LintRule> rules = texttool::parse_rules(R"TXT([
      {"Id":"probe-word","Group":"probe","Title":"探针词","Kind":"literal",
       "LiteralPatterns":["针"],"Detail":"探针","Severity":"info","Scope":"document"}
    ])TXT");
    const std::string text = repeat("针", 250);

    LintOptions options;
    options.keep_all_hits = true;
    const LintOutcome outcome = texttool::run_lint(rules, text, options);
    CHECK(outcome.hits.size() == 250);

    texttool::ReportInput full = texttool::make_report("probe", LintOutcome(outcome), {}, false);
    CHECK(full.hits.size() == 250);
    texttool::cap_report_hits(full);
    CHECK(full.hits.size() == texttool::kMaxListedHitsPerRule);
    REQUIRE(full.rule_hits.size() == 1);
    CHECK(full.rule_hits[0] == 250);
}

TEST_CASE("带基线那一趟不封顶：净增位置先裁出来，封顶排在裁之后") {
    const std::vector<LintRule> rules = texttool::parse_rules(R"TXT([
      {"Id":"probe-word","Group":"probe","Title":"探针词","Kind":"literal",
       "LiteralPatterns":["针"],"Detail":"探针","Severity":"info","Scope":"document"}
    ])TXT");
    const std::string text = repeat("针", 250);

    // 写前那一份：按全表数，不封顶（它只用来产计数）
    LintOptions before;
    before.role = texttool::LintScanRole::Baseline;
    before.cut_to_named = false;
    const LintOutcome base = texttool::run_lint(rules, "针\n", before);
    REQUIRE(base.hits.size() == 1);
    const texttool::BaselineCounts counts = texttool::count_baseline(base);
    CHECK(counts.total_hits == 1);

    // 正文那一份：也要留全命中，净增是在完整命中上裁出来的
    LintOptions after;
    after.role = texttool::LintScanRole::Compared;
    after.cut_to_named = false;
    LintOutcome body = texttool::run_lint(rules, text, after);
    CHECK(body.hits.size() == 250);
    REQUIRE(body.rule_hits.size() == 1);
    CHECK(body.rule_hits[0] == 250);

    texttool::apply_baseline(body, counts, {}, "info");
    CHECK(body.hits.size() == 249);   // 抵掉写前那一处
    CHECK(body.rule_hits[0] == 249);  // 真实处数按裁完的表重数
    REQUIRE(body.notes.size() == 1);
    CHECK(body.notes[0].text == "相对基线新增 249 处");
}

// 一份把算法命中的条数推过 65536 那道线的稿子，三段并排：
//
// 第 1、2 行是同一句连着写两遍，逐字重复报一处，说明是「与第 N 行第 M 列逐字相同」；
// 第 4 行是一串复读串（「ab」连写四遍，逗号断开），六万五千四百多处，把计数推过那道线；
// 第 6 行是短句硬连要报的那种段落。
//
// 六万多处不能省：命中里的附加串下标原先按 16 位存，第 65536 处往后绕回，
// 后面那些命中读到的是前面别的命中那一格。这个数就是那道线的位置。
// 句子按句末标点切，所以那句重复的写成两行整句，别粘在别的话后面。
const std::string& wrap_sample() {
    static const std::string text = [] {
        std::string out = "同一句话在这里说了两遍。\n同一句话在这里说了两遍。\n\n";
        for (int i = 0; i < 65541; ++i) out += "abababab，";
        out += "\n\n他起身；穿衣，洗脸，拿包，出门，下楼，买饭，上车。\n";
        return out;
    }();
    return text;
}

std::string_view detail_of(const LintOutcome& outcome, const LintHit& hit) {
    return outcome.fields().detail_of(hit);
}

// 规则表里某条规则的命中处数；规则不在表里就当 0。
std::size_t hits_of(const LintOutcome& outcome, std::string_view id) {
    for (std::size_t i = 0; i < outcome.rules->size(); ++i)
        if ((*outcome.rules)[i].id == id) return outcome.rule_hits[i];
    return 0;
}

TEST_CASE("越过 65536 处之后，命中的说明仍指着它自己那一处") {
    // 两条算法规则一前一后：复读串先跑、自己就报了六万五千多处，逐字重复那一处落在线那侧。
    // 说明里的行列要是它自己的首现，不能是前面别的命中那一格里的位置。
    const std::vector<LintRule> rules = texttool::parse_rules(R"TXT([
      {"Id":"repeated-token-run","Group":"structure","Title":"复读串","Kind":"algorithm",
       "AlgorithmName":"repeated-token-run","Detail":"「{0}」连写 {1} 次","Severity":"warn"},
      {"Id":"repeated-verbatim","Group":"structure","Title":"逐字重复","Kind":"algorithm",
       "AlgorithmName":"repeated-verbatim","Detail":"与第 {0} 行第 {1} 列逐字相同","Severity":"warn"}
    ])TXT");
    const LintOutcome outcome = texttool::run_lint(rules, wrap_sample(), LintOptions{});

    // 前一条自己就越过了那道线：线那侧说的是后面那条规则的命中
    CHECK(hits_of(outcome, "repeated-token-run") > 65536);
    REQUIRE(hits_of(outcome, "repeated-verbatim") == 1);

    const LintHit* verbatim = nullptr;
    for (const LintHit& hit : outcome.hits)
        if (rule_of(outcome, hit).id == "repeated-verbatim") verbatim = &hit;
    REQUIRE(verbatim != nullptr);
    // 第 2 行那句，首现在第 1 行：说明里的行号是它自己指的，不是这一处自己的行号
    CHECK(verbatim->line == 2);
    CHECK(detail_of(outcome, *verbatim) == "与第 1 行第 1 列逐字相同");
}

TEST_CASE("越过 65536 处之后，后一条规则拿到的仍是它自己的说明") {
    // 单扫一份时每条规则只列前 200 处，所以越线的是后面那条规则的头几处：
    // 复读串先跑，短句硬连那一处落到线那侧，说明要是它自己的，模板也得是它自己的。
    const LintOutcome outcome =
        scan({"repeated-token-run", "choppy-clauses"}, "doc", wrap_sample());

    CHECK(hits_of(outcome, "repeated-token-run") > 65536);
    const LintHit* choppy = nullptr;
    for (const LintHit& hit : outcome.hits)
        if (rule_of(outcome, hit).id == "choppy-clauses") choppy = &hit;
    REQUIRE(choppy != nullptr);
    CHECK(choppy->line == 6);
    CHECK(detail_of(outcome, *choppy) ==
          "这一段切成 8 个短促分句，平均 2.1 字，靠分号与破折号硬接——成分全省了，读者得自己补主语和宾语");
}
