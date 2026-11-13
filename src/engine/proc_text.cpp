// 行合并、文件拼接、标点替换三个功能。三者都是文本进文本出，实现放同一处。
//
// 处理顺序固定：行合并、中文截断修复、标点截断修复、去行首逗号、标点替换。
// 换顺序会改变结果，而结果与 fixtures/processing 下的期望输出是按字节比的。
// 三个功能都能整份一次喂，也能按块一块一块喂
// （`MergePipeline`、`ReplacePipeline` 配 `TextFileReader` 与 `TextWriter`，内存里不攒整份）。
// 换标点还多一道：这份稿子算不算博文要看完全文，所以写盘那条路先按行状态机看一遍，
// 再从盘上从头读一遍边遮边换（`replace_punctuation_file`）。
// 行分隔统一是 \n，输出末尾带 \n（与 fixtures/processing 下的期望输出逐字节相同）。
//
// 宽度与行尾都按 .NET 的字符粒度算，也就是 UTF-16 码元：基本平面的字算一个，
// 辅助平面的字（UTF-8 四字节）算两个。按字数合并时下面一律按码元数，别改成按码点数，
// 宽度阈值和「行尾是不是汉字」的判定会与期望输出对不上。
// 按字节数合并时宽度按原稿编码量（MergeOptions::source_encoding）：一个汉字在 GBK 稿里占
// 两字节，按 UTF-8 的字节量算是三字节，断行位置就会不同。
#include "engine/processing.h"

#include <cstddef>
#include <cstdint>
#include <string>
#include <unordered_set>
#include <utility>
#include <vector>

#include "engine/io.h"
#include "engine/markdown.h"

namespace texttool {
namespace {

// 取一个 UTF-8 字符，返回吃掉的字节数。非法序列按单字节原样返回，不抛也不丢。
std::size_t decode_utf8(std::string_view text, std::size_t pos, char32_t& code_point) {
    auto lead = static_cast<unsigned char>(text[pos]);
    if (lead < 0x80) {
        code_point = lead;
        return 1;
    }

    std::size_t length = 0;
    char32_t value = 0;
    if ((lead & 0xE0) == 0xC0) {
        length = 2;
        value = lead & 0x1F;
    } else if ((lead & 0xF0) == 0xE0) {
        length = 3;
        value = lead & 0x0F;
    } else if ((lead & 0xF8) == 0xF0) {
        length = 4;
        value = lead & 0x07;
    } else {
        code_point = lead;
        return 1;
    }

    if (pos + length > text.size()) {
        code_point = lead;
        return 1;
    }
    for (std::size_t offset = 1; offset < length; ++offset) {
        auto next = static_cast<unsigned char>(text[pos + offset]);
        if ((next & 0xC0) != 0x80) {
            code_point = lead;
            return 1;
        }
        value = (value << 6) | (next & 0x3F);
    }
    code_point = value;
    return length;
}

// 正文占多少 UTF-16 码元。
std::size_t utf16_length(std::string_view text) {
    std::size_t units = 0;
    for (std::size_t pos = 0; pos < text.size();) {
        char32_t code_point = 0;
        pos += decode_utf8(text, pos, code_point);
        units += code_point >= 0x10000 ? 2 : 1;
    }
    return units;
}

// 一串字符按其 UTF-16 码元摊开成集合。辅助平面的字摊成高低两个代理，
// 这样「行尾字符是否在集合里」的比较与按码元算的做法一致。
std::unordered_set<std::uint32_t> to_units(std::string_view text) {
    std::unordered_set<std::uint32_t> units;
    for (std::size_t pos = 0; pos < text.size();) {
        char32_t code_point = 0;
        pos += decode_utf8(text, pos, code_point);
        if (code_point >= 0x10000) {
            std::uint32_t value = static_cast<std::uint32_t>(code_point) - 0x10000;
            units.insert(0xD800 + (value >> 10));
            units.insert(0xDC00 + (value & 0x3FF));
        } else {
            units.insert(code_point);
        }
    }
    return units;
}

// 空白的判定与 .NET 的 char.IsWhiteSpace 同一组：换行、制表、各档间隔符都算。
bool is_space_unit(std::uint32_t unit) {
    return (unit >= 0x09 && unit <= 0x0D) || unit == 0x20 || unit == 0x85 || unit == 0xA0 ||
           unit == 0x1680 || (unit >= 0x2000 && unit <= 0x200A) || unit == 0x2028 ||
           unit == 0x2029 || unit == 0x202F || unit == 0x205F || unit == 0x3000;
}

// 汉字（不含标点）。圈子里还常列 0x20000..0x2A6DF 一段，但这里比的是单个码元，
// 辅助平面的字在这一层是代理对，永远命中不了，所以只留基本平面这三段，结果相同。
bool is_cjk_unit(std::uint32_t unit) {
    return (unit >= 0x4E00 && unit <= 0x9FFF) || (unit >= 0x3400 && unit <= 0x4DBF) ||
           (unit >= 0xF900 && unit <= 0xFAFF);
}

// 行尾（跳过尾部空白）那一个码元。全是空白或空串时返回 false。
// 辅助平面的字取低位代理，与从后往前逐码元扫的结果一致。
bool last_unit(std::string_view text, std::uint32_t& unit) {
    std::size_t end = text.size();
    while (end > 0) {
        std::size_t start = end - 1;
        while (start > 0 && (static_cast<unsigned char>(text[start]) & 0xC0) == 0x80) --start;
        char32_t code_point = 0;
        decode_utf8(text, start, code_point);
        std::uint32_t current =
            code_point >= 0x10000 ? 0xDC00 + ((static_cast<std::uint32_t>(code_point) - 0x10000) & 0x3FF)
                                  : static_cast<std::uint32_t>(code_point);
        if (!is_space_unit(current)) {
            unit = current;
            return true;
        }
        end = start;
    }
    return false;
}

// 行尾（跳过尾部空白）是否落在字符集里。集合为空时一律 false。
bool ends_with_any(std::string_view text, const std::unordered_set<std::uint32_t>& units) {
    if (units.empty()) return false;
    std::uint32_t unit = 0;
    if (!last_unit(text, unit)) return false;
    return units.count(unit) != 0;
}

// 中文截断断段修复：行尾是汉字就接着拼下一行；行尾落在 no_merge 字符集里就断开。
// no_merge 优先于汉字规则。全是空白的累积文本直接断开。
bool should_flush_cjk(std::string_view accumulated, const std::unordered_set<std::uint32_t>& no_merge,
                      bool has_no_merge) {
    std::uint32_t unit = 0;
    if (!last_unit(accumulated, unit)) return true;
    if (has_no_merge && no_merge.count(unit) != 0) return true;
    return !is_cjk_unit(unit);
}

// 按 .NET 的 File.ReadAllLines 切行：\r\n、\n、\r 都算行尾，
// 末尾那个行尾不额外产生一个空行，中间连着两个行尾才产生空行。
// 切行本身在 MergePipeline::feed 里，那边可以按块喂，切出来的行不复制。

// 规则按顺序一条条套在整段上，前一条的结果是后一条的输入。
// 走 RuleChain 那一条：逐段套与整份套是同一份代码，两条路的结果因此不会分家。
std::string apply_rules(std::string_view text,
                        const std::vector<std::pair<std::string, std::string>>& rules) {
    std::string result;
    result.reserve(text.size());
    RuleChain chain(rules, [&result](std::string_view chunk) { result.append(chunk); });
    chain.feed(text);
    chain.flush();
    return result;
}

}  // namespace

RuleChain::RuleChain(const std::vector<std::pair<std::string, std::string>>& rules,
                     TextChunkSink sink)
    : sink_(std::move(sink)) {
    // find 为空的规则什么也换不成，不进这条链，后面的档位因此也不会被它占掉。
    stages_.reserve(rules.size());
    for (const auto& [find, replace] : rules) {
        if (find.empty()) continue;
        stages_.push_back(Stage{find, replace, {}});
    }
}

void RuleChain::feed(std::string_view utf8) { push(0, utf8); }

void RuleChain::flush() {
    // 一档一档往下交：第 0 档的尾巴交给第 1 档之后，第 1 档的尾巴会更长，接着轮到它。
    for (std::size_t index = 0; index < stages_.size(); ++index) {
        if (stages_[index].pending.empty()) continue;
        std::string rest = std::move(stages_[index].pending);
        stages_[index].pending.clear();
        push(index + 1, rest);
    }
}

void RuleChain::push(std::size_t index, std::string_view bytes) {
    if (index >= stages_.size()) {
        if (!bytes.empty()) sink_(bytes);
        return;
    }

    Stage& stage = stages_[index];
    stage.pending.append(bytes);

    // 末尾不足 find 长度的那一截先留着：下一个字节可能让它凑成一处命中。
    const std::size_t limit = stage.pending.size() >= stage.find.size()
                                  ? stage.pending.size() - stage.find.size() + 1
                                  : 0;
    std::size_t pos = 0;
    std::size_t plain = 0;  // 还没交出去的连续普通字节，从哪儿起
    while (pos < limit) {
        if (stage.pending.compare(pos, stage.find.size(), stage.find) != 0) {
            ++pos;
            continue;
        }
        if (pos > plain)
            push(index + 1, std::string_view(stage.pending).substr(plain, pos - plain));
        push(index + 1, stage.replace);  // 换上去的内容不再参与这一条的匹配
        pos += stage.find.size();
        plain = pos;
    }
    if (plain < pos) push(index + 1, std::string_view(stage.pending).substr(plain, pos - plain));
    // 一处命中会把 pos 推过 limit，交出去的是 pos 之前那一段；剩下不足 find 长度的那一截留到下次。
    stage.pending.erase(0, pos);
}

void merge_lines_into(std::string_view text, const MergeOptions& options,
                      const TextChunkSink& sink) {
    MergePipeline pipeline(options, sink);
    pipeline.feed(text);
    pipeline.finish();
}

MergePipeline::MergePipeline(const MergeOptions& options, TextChunkSink sink)
    : options_(options), sink_(std::move(sink)) {
    // 行合并与后面两步共用同一份不合并字符集（预编译一份传下去）。
    has_no_merge_ = options_.no_merge && !options_.no_merge_chars.empty();
    if (has_no_merge_) no_merge_ = to_units(options_.no_merge_chars);
    has_punct_ = options_.fix_punct && !options_.punct_chars.empty();
    if (has_punct_) punct_ = to_units(options_.punct_chars);
}

void MergePipeline::feed(std::string_view utf8) {
    line_buffer_.append(utf8);

    // 行尾与 .NET 的 File.ReadAllLines 同一套：\r\n、\n、\r 都算行尾。
    // scanned_ 是上一次扫到这里，长行跨了好几块时不用从头重扫。
    std::size_t start = 0;
    std::size_t pos = scanned_;
    while (pos < line_buffer_.size()) {
        const char current = line_buffer_[pos];
        if (current == '\r') {
            // 这一块的末尾是 \r 时还不知道下一块开头是不是 \n，留给下一块再定。
            if (pos + 1 >= line_buffer_.size()) break;
            const std::size_t length = pos - start;
            handle_line(std::string_view(line_buffer_).substr(start, length));
            pos += line_buffer_[pos + 1] == '\n' ? 2 : 1;
            start = pos;
        } else if (current == '\n') {
            const std::size_t length = pos - start;
            handle_line(std::string_view(line_buffer_).substr(start, length));
            ++pos;
            start = pos;
        } else {
            ++pos;
        }
    }

    // 算过的行不用再留着，只留没走完的这一行（可能很长，也可能一个字节都没有）。
    line_buffer_.erase(0, start);
    scanned_ = pos - start;
}

// 没攒够一段的那几行在收尾时必须交出去：先交 accumulated_，再交后面两步等着的那一行。
void MergePipeline::finish() {
    // 最后这一行没有行尾也要交；末尾孤零零一个 \r 本身就是行尾，它前面那行照交。
    // 缓冲区里从下标 0 起就是这一行（已经算过的行在前面的 feed 里剪掉了）。
    std::string_view rest(line_buffer_);
    if (!rest.empty()) {
        if (rest.back() == '\r') rest.remove_suffix(1);
        handle_line(rest);
    }
    line_buffer_.clear();
    scanned_ = 0;

    if (!accumulated_.empty()) {
        std::string ready = std::move(accumulated_);
        accumulated_.clear();
        width_ = 0;
        after_cjk(std::move(ready));
    }
    // 还没等来下一行的那两行照样交出去；空的那一行不交，空稿子因此不出任何字符。
    if (!cjk_pending_.empty()) after_punct(std::move(cjk_pending_));
    cjk_pending_.clear();
    if (!punct_pending_.empty()) finish_line(std::move(punct_pending_));
    punct_pending_.clear();
}

void MergePipeline::handle_line(std::string_view line) {
    // 第一步：短行逐个拼，拼到阈值就往下走；行尾落在不合并字符集里也立刻往下走。
    // 宽度按这一行加，不回头重扫已经拼过的字。
    accumulated_.append(line);
    width_ += options_.by_chars ? utf16_length(line)
                                : encoded_byte_count(line, options_.source_encoding);
    if (static_cast<long long>(width_) >= options_.threshold ||
        (has_no_merge_ && ends_with_any(line, no_merge_))) {
        std::string ready = std::move(accumulated_);
        accumulated_.clear();
        width_ = 0;
        after_cjk(std::move(ready));
    }
}

void MergePipeline::after_cjk(std::string line) {
    // 中文截断断段修复：行尾是汉字就接着拼下一行。
    if (!options_.fix_cjk) {
        after_punct(std::move(line));
        return;
    }
    cjk_pending_ += line;
    if (should_flush_cjk(cjk_pending_, no_merge_, has_no_merge_)) {
        std::string ready = std::move(cjk_pending_);
        cjk_pending_.clear();
        after_punct(std::move(ready));
    }
}

void MergePipeline::after_punct(std::string line) {
    // 标点截断断段修复：行尾落在不合并字符集里就不拼，哪怕它是合并字符。
    if (!has_punct_) {
        finish_line(std::move(line));
        return;
    }
    punct_pending_ += line;
    if (ends_with_any(punct_pending_, no_merge_) || !ends_with_any(punct_pending_, punct_)) {
        std::string ready = std::move(punct_pending_);
        punct_pending_.clear();
        finish_line(std::move(ready));
    }
}

void MergePipeline::finish_line(std::string line) {
    // 最后一步：去掉行首那一个逗号、逐行套替换规则，补上换行交出去。
    // 套规则看不到整份稿子的头尾，空格副本（结构区域全是空格的那份副本）在这一档不开。
    constexpr std::string_view kComma = "，";  // U+FF0C
    if (options_.trim_leading_comma && line.starts_with(kComma)) line.erase(0, kComma.size());
    if (options_.apply_replace && !options_.replace_rules.empty())
        line = apply_rules(line, options_.replace_rules);
    line += '\n';
    sink_(std::string_view(line));
}

std::string merge_lines(std::string_view text, const MergeOptions& options) {
    std::string result;
    // 合并后一般比原文略小，按原文字节数一次预留够，省得边拼边扩容翻倍。
    result.reserve(text.size());
    merge_lines_into(text, options, [&result](std::string_view chunk) { result.append(chunk); });
    return result;
}

std::string join_texts(const std::vector<std::string>& parts, const JoinOptions& options) {
    // pattern 只是命令行那一层挑文件用的，引擎收到的是已经挑好、排好序的正文。
    (void)options;

    std::size_t total = 0;
    for (const auto& part : parts) total += part.size() + 1;
    std::string result;
    result.reserve(total);
    for (const auto& part : parts) {
        result += part;
        // 文件之间要有换行分隔：本份正文末尾已经是 \n 就不再补。
        if (!part.ends_with('\n')) result += '\n';
    }
    return result;
}

std::string replace_punctuation(std::string_view text, const ReplaceOptions& options) {
    // 博文结构原样保留：稿子按空格副本切成一段一段，没遮的地方套规则，遮住的地方原封拼回去。
    // 规则本来就在一条串上连着套，按段套与整串套在段内等价；跨段才成立的替换不做。
    if (!options.protect_blog) return apply_rules(text, options.rules);

    const BlogMask mask = BlogMask::create(text);
    if (!mask.is_blog_post() || mask.masked_chars() == 0) return apply_rules(text, options.rules);

    std::string result;
    result.reserve(text.size());
    std::size_t run_start = 0;
    for (std::size_t i = 1; i <= text.size(); ++i) {
        if (i < text.size() && mask.is_masked(i) == mask.is_masked(run_start)) continue;
        const std::string_view run = text.substr(run_start, i - run_start);
        if (mask.is_masked(run_start)) {
            result.append(run);
        } else {
            result.append(apply_rules(run, options.rules));
        }
        run_start = i;
    }
    return result;
}

ReplacePipeline::ReplacePipeline(const ReplaceOptions& options, bool blog_post, TextChunkSink sink)
    : masking_(blog_post && options.protect_blog),
      sink_(std::move(sink)),
      chain_(options.rules, [this](std::string_view chunk) { sink_(chunk); }) {}

void ReplacePipeline::feed(std::string_view utf8) {
    if (!masking_) {
        chain_.feed(utf8);  // 不是博文：一个字都不遮，整份接着套规则
        return;
    }
    scanner_.feed(utf8, [this](const BlogMaskScanner::LineMask& line) { handle_line(line); });
}

void ReplacePipeline::finish() {
    if (masking_) scanner_.finish([this](const BlogMaskScanner::LineMask& line) { handle_line(line); });
    chain_.flush();
}

void ReplacePipeline::write_masked(std::string_view bytes) {
    if (bytes.empty()) return;
    // 遮住的那一段要原样接在规则算出来的后面：先让规则那条路把压着的尾巴交干净。
    chain_.flush();
    sink_(bytes);
}

void ReplacePipeline::handle_line(const BlogMaskScanner::LineMask& line) {
    if (line.whole_line) {
        // 整行连换行一起遮：原样交出去，不经过规则。
        write_masked(line.line);
        if (line.has_newline) write_masked("\n");
        return;
    }

    // 行内：遮住的那几段原样，没遮的连它后面的换行一起套规则。
    std::size_t pos = 0;
    for (const BlogMaskScanner::Span& span : line.spans) {
        chain_.feed(line.line.substr(pos, span.begin - pos));
        write_masked(line.line.substr(span.begin, span.end - span.begin));
        pos = span.end;
    }
    chain_.feed(line.line.substr(pos));
    if (line.has_newline) chain_.feed("\n");
}

TextEncoding replace_punctuation_file(const std::string& source, const std::string& target,
                                     const ReplaceOptions& options) {
    TextFileReader reader(source);

    // 第一遍只看结构：这份稿子算不算博文要看完全文，先按行状态机读一遍，不攒正文。
    // 判出来以后再从头读一遍去换；两趟之间文件被别人改过的话，第二趟读到什么就处理什么。
    bool blog_post = false;
    const auto skip = [](const BlogMaskScanner::LineMask&) {};
    if (options.protect_blog) {
        BlogMaskScanner scanner;
        std::string_view chunk;
        while (reader.next(chunk)) scanner.feed(chunk, skip);
        scanner.finish(skip);
        blog_post = scanner.is_blog_post();
        reader.rewind();
    }

    TextWriter writer(target, reader.encoding());
    ReplacePipeline pipeline(options, blog_post,
                             [&writer](std::string_view bytes) { writer.write(bytes); });
    std::string_view chunk;
    while (reader.next(chunk)) pipeline.feed(chunk);
    pipeline.finish();
    writer.commit();
    return reader.encoding();
}

}  // namespace texttool
