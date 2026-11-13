// 视觉小说排版：一句一行、补标点、限制每段字数。
//
// 输出与 fixtures/processing 下的期望输出逐字节相同。
// 三个开关的组合与命令行一致：两个都开是「先排版、再把结果补标点」，
// 只开排版就只排版，只开补标点就只补标点。
//
// 文字一律按「字符」处理，不按字节：长度按 .NET 的 UTF-16 单元数算，
// 切点一律落在字符边界上：emoji 这类字不切成半个，要切就往前挪到一个完整的字。
// 约定的输入是合法 UTF-8，非法字节也不崩：各自当成一个字符原样带过去。
//
// 逐段做：一行读进来就判、就并进当前这一段，一段攒够就排好交出去（`VnPipeline`）。
// 宽字符只解当前这一行与正在攒的这一段，整份稿子转成宽字符那一份不再存在；
// 15MB 的稿子因此不必先摊成几十兆的宽字符再动手。
#include "engine/processing.h"

#include "engine/io.h"
#include "engine/regex.h"

#include <algorithm>
#include <optional>
#include <stdexcept>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

namespace texttool {
namespace {

// ---------------------------------------------------------------- 文本

using Text = std::u32string;

// 非法 UTF-8 字节的存法：编码回字节时原样还原，保证非 UTF-8 输入也一字不差地进出。
constexpr char32_t kRawByteBase = 0x110000;

constexpr char32_t kOpenQuote = U'「';
constexpr char32_t kCloseQuote = U'」';
constexpr char32_t kComma = U'，';
constexpr char32_t kFullStop = U'。';
constexpr char32_t kNewline = U'\n';
constexpr char32_t kCarriageReturn = U'\r';
constexpr char32_t kByteOrderMark = U'\uFEFF';

// 场景标记的通用模式：`── 场景 ──` 里的那一段。
constexpr std::string_view kDefaultScenePattern = "(?<=── )\\S+(?= ──)";

// 超长段落切削时，切口离段首至少这么远：避免把一句话切成很短的渣。
// 这就是默认值，调用方不给这个参数。
constexpr std::size_t kMinParaLength = 80;

Text decode_text(std::string_view bytes) {
    Text text;
    text.reserve(bytes.size());
    for (std::size_t at = 0; at < bytes.size();) {
        const auto lead = static_cast<unsigned char>(bytes[at]);
        std::size_t width = 0;
        char32_t code = 0;
        if (lead < 0x80) {
            width = 1;
            code = lead;
        } else if ((lead & 0xE0) == 0xC0) {
            width = 2;
            code = lead & 0x1F;
        } else if ((lead & 0xF0) == 0xE0) {
            width = 3;
            code = lead & 0x0F;
        } else if ((lead & 0xF8) == 0xF0) {
            width = 4;
            code = lead & 0x07;
        }

        bool valid = width != 0 && at + width <= bytes.size();
        for (std::size_t k = 1; valid && k < width; ++k) {
            const auto next = static_cast<unsigned char>(bytes[at + k]);
            if ((next & 0xC0) != 0x80) {
                valid = false;
                break;
            }
            code = (code << 6) | (next & 0x3F);
        }
        if (!valid) {
            text.push_back(kRawByteBase + lead);
            ++at;
            continue;
        }
        text.push_back(code);
        at += width;
    }
    return text;
}

std::string encode_text(const Text& text) {
    std::string bytes;
    bytes.reserve(text.size() * 3);
    for (const char32_t code : text) {
        if (code >= kRawByteBase) {
            bytes.push_back(static_cast<char>(code - kRawByteBase));
        } else if (code < 0x80) {
            bytes.push_back(static_cast<char>(code));
        } else if (code < 0x800) {
            bytes.push_back(static_cast<char>(0xC0 | (code >> 6)));
            bytes.push_back(static_cast<char>(0x80 | (code & 0x3F)));
        } else if (code < 0x10000) {
            bytes.push_back(static_cast<char>(0xE0 | (code >> 12)));
            bytes.push_back(static_cast<char>(0x80 | ((code >> 6) & 0x3F)));
            bytes.push_back(static_cast<char>(0x80 | (code & 0x3F)));
        } else {
            bytes.push_back(static_cast<char>(0xF0 | (code >> 18)));
            bytes.push_back(static_cast<char>(0x80 | ((code >> 12) & 0x3F)));
            bytes.push_back(static_cast<char>(0x80 | ((code >> 6) & 0x3F)));
            bytes.push_back(static_cast<char>(0x80 | (code & 0x3F)));
        }
    }
    return bytes;
}

// 长度就是 .NET 的 UTF-16 单元数：补充平面（U+10000 以上）的一个字算两个。
// 行宽、段长这类判断都按它算，emoji 之类的地方才不会算短。
std::size_t held_units(const Text& text) {
    std::size_t units = 0;
    for (const char32_t code : text) units += code >= 0x10000 ? 2 : 1;
    return units;
}

// 每个字符的 UTF-16 单元起点。切削与「不超过多少字」都按单元算，
// 但要切的地方必须落在字符边界上，所以两边得能互相换算。
std::vector<std::size_t> unit_starts(const Text& text) {
    std::vector<std::size_t> starts;
    starts.reserve(text.size());
    std::size_t units = 0;
    for (const char32_t code : text) {
        starts.push_back(units);
        units += code >= 0x10000 ? 2 : 1;
    }
    return starts;
}

// 单元下标换成字符下标：返回完整的、起点在 units 之前的字符个数。
// 单元下标正好落在某个字的中间时往前取整，宁可不切，也不把一个字切成半个。
std::size_t characters_before_unit(const std::vector<std::size_t>& starts, std::size_t units) {
    return static_cast<std::size_t>(
        std::lower_bound(starts.begin(), starts.end(), units) - starts.begin());
}

// 字节数换算成字符数：切点从正则那一层回来是字节偏移，上层一律按字符数走。
std::size_t count_characters(std::string_view bytes) { return decode_text(bytes).size(); }

// 与 .NET char.IsWhiteSpace 同一组（含全角空格）。
bool is_space(char32_t code) {
    if (code == U' ' || (code >= U'\t' && code <= U'\r')) return true;
    if (code == 0x85 || code == 0xA0 || code == 0x1680) return true;
    if (code >= 0x2000 && code <= 0x200A) return true;
    return code == 0x2028 || code == 0x2029 || code == 0x202F || code == 0x205F ||
           code == 0x3000;
}

Text trim_start(const Text& text) {
    std::size_t from = 0;
    while (from < text.size() && is_space(text[from])) ++from;
    return text.substr(from);
}

Text trim_end(const Text& text) {
    std::size_t to = text.size();
    while (to > 0 && is_space(text[to - 1])) --to;
    return text.substr(0, to);
}

Text trim(const Text& text) { return trim_start(trim_end(text)); }

// .NET 的 Trim(char)：只剥指定的字符。
Text trim_chars(const Text& text, std::u32string_view chars) {
    const auto listed = [&](char32_t code) {
        return chars.find(code) != std::u32string_view::npos;
    };
    std::size_t from = 0;
    std::size_t to = text.size();
    while (from < to && listed(text[from])) ++from;
    while (to > from && listed(text[to - 1])) --to;
    return text.substr(from, to - from);
}

// .NET 的 TrimEnd(char)：只剥结尾的指定字符。
Text trim_chars_end(const Text& text, char32_t code) {
    std::size_t to = text.size();
    while (to > 0 && text[to - 1] == code) --to;
    return text.substr(0, to);
}

// 收尾引号：补标点时要越过的那些。
bool is_closing_quote(char32_t code) {
    return code == kCloseQuote || code == U'』' || code == U'\'' || code == U'"';
}

bool contains(const Text& hay, std::u32string_view needle) {
    return hay.find(needle) != Text::npos;
}

// .NET 的 string.Replace：从左到右换掉所有不重叠的出现。
Text replace_all(const Text& text, std::u32string_view from, std::u32string_view to) {
    if (from.empty()) return text;
    Text out;
    std::size_t at = 0;
    while (at < text.size()) {
        const std::size_t hit = text.find(from, at);
        if (hit == Text::npos) break;
        out.append(text, at, hit - at);
        out.append(to);
        at = hit + from.size();
    }
    out.append(text, at, Text::npos);
    return out;
}

// 「句末标点」：这些字结尾的段落不再补句号。
bool is_sentence_ender(char32_t code) {
    switch (code) {
        case U'。':
        case U'！':
        case U'？':
        case U'；':
        case U'：':
        case U'」':
        case U'）':
        case U'】':
        case U'』':
        case U'》':
        case U'、':
        case U'…':
        case U'—':
        case U'～':
        case U'·':
        case U'.':
        case U'!':
        case U'?':
        case U';':
        case U':':
            return true;
        default:
            return false;
    }
}

// 补标点那一层认的句子结尾：句号、叹号、问号与省略号。
bool is_dialogue_ender(char32_t code) {
    return code == U'。' || code == U'！' || code == U'？' || code == U'…';
}

// 切削段落时认的三个句末标点：只看这三个，别的标点不够硬。
bool is_cut_ender(char32_t code) {
    return code == U'。' || code == U'！' || code == U'？';
}

// 把「同一个字符连成一串」压成一个：只处理 chars 里的字，run 达到 min_run 才压。
Text collapse_runs(const Text& text, std::u32string_view chars, std::size_t min_run,
                   std::u32string_view replacement) {
    Text out;
    for (std::size_t at = 0; at < text.size();) {
        if (chars.find(text[at]) == std::u32string_view::npos) {
            out.push_back(text[at]);
            ++at;
            continue;
        }
        std::size_t run = 1;
        while (at + run < text.size() && text[at + run] == text[at]) ++run;
        if (run >= min_run)
            out += replacement;
        else
            out.append(text, at, run);
        at += run;
    }
    return out;
}

// 常见标点重复的修法：先成对地并，再把剩下的长串压掉。
Text fix_punctuation(const Text& text) {
    Text out = text;
    out = replace_all(out, U"。。", U"。");
    out = replace_all(out, U"，，", U"，");
    out = replace_all(out, U"？？", U"？");
    out = replace_all(out, U"！！", U"！");
    out = replace_all(out, U"，。", U"。");
    out = replace_all(out, U"。？", U"？");
    out = replace_all(out, U"，？", U"？");
    out = replace_all(out, U"。，", U"，");

    // 连着一串同一个句末标点只留一个；三个以上点号收成一个省略号，两个以上省略号收成两个。
    Text squashed;
    for (std::size_t at = 0; at < out.size();) {
        const char32_t code = out[at];
        if (!is_cut_ender(code)) {
            squashed.push_back(code);
            ++at;
            continue;
        }
        while (at < out.size() && out[at] == code) ++at;
        squashed.push_back(code);
    }
    squashed = collapse_runs(squashed, U".", 3, U"…");
    squashed = collapse_runs(squashed, U"…", 2, U"……");
    return squashed;
}

// ---------------------------------------------------------------- 对话模式

// 「角色」「台词」：某个「之后遇到第一个」紧跟一个「。
// 找不到返回 Text::npos。给的是匹配起点，与 .NET 的 Match.Index 同义。
std::size_t role_quote_start(const Text& text) {
    std::size_t open = 0;
    while (true) {
        open = text.find(kOpenQuote, open);
        if (open == Text::npos) return Text::npos;
        const std::size_t close = text.find(kCloseQuote, open + 1);
        if (close == Text::npos) return Text::npos;
        if (close + 1 < text.size() && text[close + 1] == kOpenQuote) return open;
        open = close + 1;
    }
}

// 「角色」「台词」格式的台词行：以「开头，并且某处是「…」「。
bool is_role_quote_line(const Text& line) {
    return !line.empty() && line[0] == kOpenQuote && role_quote_start(line) != Text::npos;
}

// ---------------------------------------------------------------- 段落

enum class LineKind { Empty, Heading, Scene, Dialogue, Mixed, Narrative };

enum class PartKind { Heading, Scene, Dialogue, Narrative };

enum class MatchState { None, Found };
struct SceneMatch {
    MatchState state = MatchState::None;
    std::size_t begin = 0;   // 字符起点
    std::size_t length = 0;  // 字符数
};

// ---------------------------------------------------------------- 补标点

// 对话行里最后一个 」前少了句末标点时补一个。不是「角色」「台词」那种格式的行原样返回。
// 行尾的 \r 只管判，不算进结果；不用补点的行连 \r 一起原样返回。
std::string fix_dialogue_line(std::string_view bytes) {
    const std::string unchanged(bytes);
    const Text line = trim_chars_end(decode_text(bytes), kCarriageReturn);
    if (!is_role_quote_line(line)) return unchanged;

    const std::size_t close = line.rfind(kCloseQuote);
    if (close == Text::npos) return unchanged;

    // 引号后面还有话（「…」她说）：后面已经跟了句末标点就不补。
    if (close + 1 < line.size()) {
        const Text tail = trim_end(line.substr(close + 1));
        if (!tail.empty() && is_dialogue_ender(tail.back())) return unchanged;
    }

    // 越过末尾连着的一串引号，看它前面那个字。
    std::size_t before = close;
    while (before > 0 && is_closing_quote(line[before - 1])) --before;
    if (before == 0 || is_dialogue_ender(line[before - 1])) return unchanged;

    Text fixed = line.substr(0, close);
    fixed.push_back(kFullStop);
    fixed += line.substr(close);
    return encode_text(fixed);
}

}  // namespace

// ---------------------------------------------------------------- 逐段排版

// 逐段排版的实现：正文一块一块喂进来，一行判完并进当前这一段，一段排够就排好交给 sink。
// 内存里只有当前这一行（按宽字符解出来的那一行）、正在攒的这一段、正在切出来的那几片，
// 以及「上一段是什么」这一格记忆：整份稿子转成宽字符那一份不再存在。
struct VnPipeline::Impl {
    Impl(const VnOptions& options, TextChunkSink sink);

    void feed(std::string_view bytes);
    void finish();

private:
    void handle_line(std::string_view raw, bool has_newline);
    void handle_heading(const Text& line);
    void handle_dialogue(const Text& line);
    void handle_mixed(const Text& line);
    void process_scene_line(const Text& line);
    void append_paragraph();

    void write_part(PartKind kind, const Text& text);
    void write_element(const Text& text);
    void write_line(std::string_view bytes);
    void write_newline();
    void write_raw_line(std::string_view bytes, bool has_newline);

    bool is_separator(const Text& line) const;
    bool is_route_heading(const Text& line) const;
    bool has_dialogue(const Text& line) const;
    std::size_t dialogue_start(const Text& line) const;
    SceneMatch match_scene(const Text& line) const;
    std::pair<Text, Text> extract_dialogue(const Text& line) const;
    std::vector<Text> split_long_paragraph(const Text& text) const;
    std::size_t find_cut(const Text& text, const std::vector<std::size_t>& starts,
                         std::size_t first, std::size_t last) const;
    template <typename Visit>
    void for_each_character(const Text& line, Visit&& visit) const;

    bool do_reformat_ = true;
    bool do_fix_punct_ = true;
    std::size_t max_paragraph_ = 450;
    std::vector<Text> characters_;
    std::vector<Text> route_names_;
    std::optional<Pattern> scene_;
    TextChunkSink sink_;

    std::string buffer_;  // 上一块末尾还没走完的那一行（原始字节）
    Text paragraph_;      // 正在攒的这一段：叙事行一行一行并进来
    bool first_line_ = true;      // 下一行是稿子的第一行（字节序标记要在这里剥）
    bool has_previous_ = false;   // 已经交出去过一段
    PartKind previous_ = PartKind::Narrative;
    bool wrote_element_ = false;  // 输出里已经有过一项
    int newline_run_ = 0;         // 连着写了几个换行
};

VnPipeline::Impl::Impl(const VnOptions& options, TextChunkSink sink) : sink_(std::move(sink)) {
    // 上限 0 会让切削那一层永远切不动，宁可在这里就报出来。
    if (options.max_paragraph < 1) throw std::invalid_argument("每段上限必须大于 0");
    max_paragraph_ = static_cast<std::size_t>(options.max_paragraph);
    do_reformat_ = options.do_reformat;
    do_fix_punct_ = options.do_fix_punct;

    for (const std::string& name : options.characters) {
        Text runes = decode_text(name);
        if (runes.empty()) continue;
        if (std::find(characters_.begin(), characters_.end(), runes) != characters_.end()) continue;
        characters_.push_back(std::move(runes));
    }
    std::stable_sort(characters_.begin(), characters_.end(),
                     [](const Text& a, const Text& b) { return a.size() > b.size(); });

    for (const std::string& name : options.route_names) {
        Text runes = decode_text(name);
        if (!runes.empty()) route_names_.push_back(std::move(runes));
    }

    // 只补标点时不排版，场景标记那一条正则也就不编：模式写坏了不该挡住只补标点这条路。
    if (!do_reformat_) return;
    const std::string pattern =
        options.scene_pattern.empty() ? std::string(kDefaultScenePattern) : options.scene_pattern;
    scene_.emplace(pattern);
}

void VnPipeline::Impl::feed(std::string_view bytes) {
    buffer_.append(bytes);
    std::size_t start = 0;
    while (true) {
        const std::size_t at = buffer_.find('\n', start);
        if (at == std::string::npos) break;
        handle_line(std::string_view(buffer_).substr(start, at - start), true);
        start = at + 1;
    }
    buffer_.erase(0, start);
}

void VnPipeline::Impl::finish() {
    // 末尾那一行没有换行也要排；末尾是换行时它是一个空行，与整份按行切一样。
    handle_line(buffer_, false);
    buffer_.clear();
    // 攒到最后的这一段在这儿收尾：稿子末尾没有空行时，它就是唯一交出去的那一段。
    if (do_reformat_) append_paragraph();
}

void VnPipeline::Impl::handle_line(std::string_view raw, bool has_newline) {
    if (!do_reformat_) {
        // 只补标点：一行一行原样过一遍补标点那一层，换行照旧。
        write_raw_line(raw, has_newline);
        return;
    }

    // 行尾那一串 \r 不算字。
    std::size_t end = raw.size();
    while (end > 0 && raw[end - 1] == '\r') --end;
    std::string_view bytes = raw.substr(0, end);

    // 正文不该带字节序标记：调用方按编码读过一遍了，这里只是兜住直接喂进来的字节。
    if (first_line_ && bytes.starts_with("\xEF\xBB\xBF")) bytes.remove_prefix(3);
    first_line_ = false;

    const Text line = trim(decode_text(bytes));
    if (line.empty() || is_separator(line)) {
        // 空行与装饰分隔线：当前这一段到此为止。
        append_paragraph();
        return;
    }
    if (is_route_heading(line)) {
        handle_heading(line);
        return;
    }
    if (match_scene(line).state == MatchState::Found) {
        process_scene_line(line);
        return;
    }
    // 以「开头的行都算台词：一口气说了好几句，或者只是一句引文。
    if (is_role_quote_line(line) || line[0] == kOpenQuote) {
        handle_dialogue(line);
        return;
    }
    if (has_dialogue(line)) {
        handle_mixed(line);
        return;
    }
    paragraph_ += line;
}

void VnPipeline::Impl::handle_heading(const Text& line) {
    append_paragraph();
    Text clean = trim(trim_chars(line, U"─"));
    for (const Text& name : route_names_) {
        if (contains(clean, name)) {
            clean = name;
            break;
        }
    }
    write_part(PartKind::Heading, clean);
}

void VnPipeline::Impl::handle_dialogue(const Text& line) {
    append_paragraph();
    write_part(PartKind::Dialogue, fix_punctuation(line));
}

// 把混在叙事里的台词拆出去：台词起点之后整条尾巴都算台词，前面留着当叙事。
void VnPipeline::Impl::handle_mixed(const Text& line) {
    auto [narr, dial] = extract_dialogue(line);
    if (!trim(narr).empty()) paragraph_ += narr;
    if (!trim(dial).empty()) {
        append_paragraph();
        write_part(PartKind::Dialogue, fix_punctuation(dial));
    }
}

void VnPipeline::Impl::process_scene_line(const Text& line) {
    // 先用原始文本匹配，保证与前面认场景的判定一致；路线名随后从场景文本里剥掉，
    // 反过来先替换再匹配会匹配不上。
    const SceneMatch match = match_scene(line);
    Text label = match.state == MatchState::Found ? line.substr(match.begin, match.length)
                                                  : trim(trim_chars(line, U"─"));

    Text route_name;
    for (const Text& name : route_names_) {
        if (!contains(label, name)) continue;
        Text rest = trim(replace_all(label, name, U""));
        // 剥完还剩内容，说明路线名嵌在场景文本里；剥完是空的，说明这条标记本身就是路线名。
        if (!rest.empty()) {
            route_name = name;
            label = std::move(rest);
        }
        break;
    }

    if (match.state == MatchState::Found && match.begin > 0) {
        Text lead = trim(trim_chars(trim(line.substr(0, match.begin)), U"─「」"));
        if (!lead.empty()) paragraph_ += lead;
    }

    append_paragraph();
    if (!route_name.empty()) write_part(PartKind::Heading, route_name);
    if (!label.empty()) write_part(PartKind::Scene, label);
}

// 把攒着的叙事收成一段：补标点、超长再切。空的一段什么也不做。
void VnPipeline::Impl::append_paragraph() {
    if (paragraph_.empty()) return;
    Text paragraph = trim(paragraph_);
    paragraph_.clear();
    if (paragraph.empty()) return;

    paragraph = fix_punctuation(paragraph);
    if (!is_sentence_ender(paragraph.back())) paragraph.push_back(kFullStop);
    for (Text& piece : split_long_paragraph(paragraph)) write_part(PartKind::Narrative, piece);
}

// 交一段排好的段落。段落之间的空行按上一段是什么定，与整份攒完再拼一样。
void VnPipeline::Impl::write_part(PartKind kind, const Text& text) {
    switch (kind) {
        case PartKind::Heading:
            if (has_previous_) write_element(Text());
            write_element(Text(U"# ") + text);
            break;
        case PartKind::Scene:
            if (has_previous_ && previous_ != PartKind::Scene) write_element(Text());
            write_element(Text(U"─── ") + text + Text(U" ───"));
            break;
        case PartKind::Dialogue:
            if (has_previous_ && previous_ != PartKind::Dialogue) write_element(Text());
            write_element(text);
            break;
        case PartKind::Narrative:
            if (has_previous_) write_element(Text());
            write_element(text);
            break;
    }
    previous_ = kind;
    has_previous_ = true;
}

// 交输出里的一项：项与项之间隔一个换行，空的那一项就是空行。
void VnPipeline::Impl::write_element(const Text& text) {
    if (wrote_element_) write_newline();
    wrote_element_ = true;
    write_line(encode_text(text));
}

// 交一行内容。补标点那一层按行改，改完写到 sink。
void VnPipeline::Impl::write_line(std::string_view bytes) {
    if (bytes.empty()) return;  // 空行那一项只有换行，没有内容
    newline_run_ = 0;
    if (do_fix_punct_) {
        sink_(fix_dialogue_line(bytes));
        return;
    }
    sink_(bytes);
}

// 项与项之间的换行。连续三个以上压成两个：段落之间只留一个空行。
void VnPipeline::Impl::write_newline() {
    if (newline_run_ >= 2) return;
    sink_("\n");
    ++newline_run_;
}

// 只补标点那条路：这一行原样过一遍补标点，换行照旧补上。
void VnPipeline::Impl::write_raw_line(std::string_view bytes, bool has_newline) {
    if (do_fix_punct_) {
        sink_(fix_dialogue_line(bytes));
    } else {
        sink_(bytes);
    }
    if (has_newline) sink_("\n");
}

bool VnPipeline::Impl::is_separator(const Text& line) const {
    if (line.size() < 4) return false;
    for (const char32_t code : line) {
        if (code != U'★' && code != U'＝' && code != U'=') return false;
    }
    return true;
}

bool VnPipeline::Impl::is_route_heading(const Text& line) const {
    if (line[0] == kOpenQuote) return false;
    if (match_scene(line).state == MatchState::Found) return false;
    for (const Text& name : route_names_) {
        if (contains(line, name)) return held_units(line) <= 30;
    }
    return false;
}

template <typename Visit>
void VnPipeline::Impl::for_each_character(const Text& line, Visit&& visit) const {
    std::size_t at = 0;
    while (at < line.size()) {
        std::size_t hit = 0;
        for (const Text& name : characters_) {
            if (name.size() <= line.size() - at && line.compare(at, name.size(), name) == 0) {
                hit = name.size();
                break;
            }
        }
        if (hit == 0) {
            ++at;
            continue;
        }
        if (!visit(at, at + hit)) return;
        at += hit;
    }
}

// 台词从哪个字开始；整行没有台词时返回 Text::npos。
// 先认「角色」「台词」，再认角色名紧跟「（或隔着逗号）的写法。
std::size_t VnPipeline::Impl::dialogue_start(const Text& line) const {
    if (line.find(kOpenQuote) == Text::npos) return Text::npos;

    const std::size_t quote = role_quote_start(line);
    if (quote != Text::npos) return quote;

    std::size_t found = Text::npos;
    for_each_character(line, [&](std::size_t begin, std::size_t end) {
        // 角色名「台词」
        if (end < line.size() && line[end] == kOpenQuote) {
            found = begin;
            return false;
        }
        // 角色名，「台词」
        if (end + 1 < line.size() && line[end] == kComma && line[end + 1] == kOpenQuote) {
            found = begin;
            return false;
        }
        return true;
    });
    return found;
}

bool VnPipeline::Impl::has_dialogue(const Text& line) const {
    return dialogue_start(line) != Text::npos;
}

SceneMatch VnPipeline::Impl::match_scene(const Text& line) const {
    SceneMatch out;
    if (!scene_) return out;

    const std::string bytes = encode_text(line);
    const MatchRange range = scene_->search(bytes, 0, MatchLimits{});
    if (range.begin == kNoMatch) return out;

    out.state = MatchState::Found;
    out.begin = count_characters(std::string_view(bytes).substr(0, range.begin));
    out.length =
        count_characters(std::string_view(bytes).substr(range.begin, range.end - range.begin));
    return out;
}

// 把混在叙事里的台词拆出去：台词起点之后整条尾巴都算台词，前面留着当叙事。
std::pair<Text, Text> VnPipeline::Impl::extract_dialogue(const Text& line) const {
    const std::size_t start = dialogue_start(line);
    if (start == Text::npos) return {line, Text()};
    return {line.substr(0, start), line.substr(start)};
}

// 超长段落按句末标点切开，免得出现一大堵文字墙。
// 切点是字符下标，位置按 UTF-16 单元算，一轮只扫一个段长，
// 整段下来是线性的：一个自然段很长时也不会变成平方级。
std::vector<Text> VnPipeline::Impl::split_long_paragraph(const Text& text) const {
    std::vector<Text> pieces;
    const std::size_t total = held_units(text);
    if (total <= max_paragraph_) {
        pieces.push_back(text);
        return pieces;
    }

    const std::vector<std::size_t> starts = unit_starts(text);
    // 剩下的段落从第几个单元起算；切点与剩余长度都靠它换算，整段线性。
    const auto units_from = [&](std::size_t index) {
        return index < starts.size() ? starts[index] : total;
    };

    std::size_t from = 0;
    while (total - units_from(from) > max_paragraph_) {
        const std::size_t base = units_from(from);
        // 能切的最远位置是「段首 + 上限 - 1」：切点后面那一段正好落满上限的那一刀不切。
        // 上限在构造时已校验 ≥ 1，这里的减一不会下溢。
        std::size_t cut =
            find_cut(text, starts, base + kMinParaLength - 1, base + max_paragraph_ - 1);
        // 一个句末标点都没有时才整个上限照切，这一刀可以正好是上限那么长。
        if (cut == Text::npos) cut = characters_before_unit(starts, base + max_paragraph_);
        Text piece = trim(text.substr(from, cut - from));
        if (!piece.empty()) pieces.push_back(std::move(piece));

        from = cut;
        while (from < text.size() && is_space(text[from])) ++from;
    }
    if (from < text.size()) {
        Text piece = trim(text.substr(from));
        if (!piece.empty()) pieces.push_back(std::move(piece));
    }
    return pieces;
}

// 在 [first, last] 这段单元区间里从后往前找一句话的结尾，返回它后面那个字符的下标。
// 两端都含在内，所以调用方传的 last 就是要允许切到的最远位置。
// 找不着返回 Text::npos。「」嵌套深度不为 0 的地方不切，免得把一句台词劈成两半。
std::size_t VnPipeline::Impl::find_cut(const Text& text, const std::vector<std::size_t>& starts,
                                      std::size_t first, std::size_t last) const {
    int depth = 0;
    std::size_t index = characters_before_unit(starts, last + 1);
    while (index > 0) {
        --index;
        if (starts[index] < first) return Text::npos;
        if (text[index] == kCloseQuote) ++depth;
        else if (text[index] == kOpenQuote) --depth;
        if (depth == 0 && index > 0 && is_cut_ender(text[index - 1])) return index;
    }
    return Text::npos;
}

VnPipeline::VnPipeline(const VnOptions& options, TextChunkSink sink)
    : impl_(std::make_unique<Impl>(options, std::move(sink))) {}

VnPipeline::~VnPipeline() = default;

void VnPipeline::feed(std::string_view utf8) { impl_->feed(utf8); }

void VnPipeline::finish() { impl_->finish(); }

std::string reformat_vn(std::string_view text, const VnOptions& options) {
    // 整份那条路也是同一条管线：喂一整块进去，结果攒成一份交回。
    std::string result;
    VnPipeline pipeline(options, [&result](std::string_view bytes) { result.append(bytes); });
    pipeline.feed(text);
    pipeline.finish();
    return result;
}

TextEncoding reformat_vn_file(const std::string& source, const std::string& target,
                             const VnOptions& options) {
    if (options.max_paragraph < 1) throw std::invalid_argument("每段上限必须大于 0");

    TextFileReader reader(source);
    TextWriter writer(target, reader.encoding());
    VnPipeline pipeline(options, [&writer](std::string_view bytes) { writer.write(bytes); });
    std::string_view chunk;
    while (reader.next(chunk)) pipeline.feed(chunk);
    pipeline.finish();
    writer.commit();
    return reader.encoding();
}

}  // namespace texttool
