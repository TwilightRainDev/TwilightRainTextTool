// 一次扫完一批字面量串：三字节前缀索引 + 整串核对。
//
// 为什么要有这一层：规则表里有的规则带几十个词，一个词往前扫一遍全文，
// 15MB 的稿子上光「找开头」就是几十趟全篇扫描。这些词是死的字面量，把它们的开头收成一张
// 前缀索引，整篇只走一趟，剩下的工作只在「开头对得上」的那些位置上做。
//
// 为什么前缀取三字节而不是首字节：汉字稿上首字节几乎一样（UTF-8 里汉字的首字节落在
// 0xE4..0xE9 那一小段），按首字节筛等于不筛，实测 15MB 要走两百毫秒。
// 一个汉字正好三字节，三个字节合起来才分得开，索引一遍只要十来毫秒。
// 顺带一个好处：三个字节都不命中时能整字跳三格——那一格是汉字的头，它后面两格是续字节
// （0x80..0xBF），而候选串的首字节永远是成形的 UTF-8 首字节（不会是续字节），
// 所以那两格一定不是起点，跳过去不会漏。
//
// 结果与逐条扫完全相同（每个词各自的推进方式与结果都不变），只是少扫了几十遍。
// 这一层只做「哪里可能是起点」：判定仍交给各条模式自己的匹配器，
// 唯一的例外是「整条模式就是一串字面量」的那些，它们命中什么由整串比的结果直接给出。
#pragma once

#include <array>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <string>
#include <string_view>
#include <vector>

namespace texttool {

// 建好之后只读，可以放心在多份稿子上反复用（一份规则的编译结果就是这么复用的）。
class NeedleScanner {
public:
    // 建索引。needles 的第 i 串编号就是 i；重复的串各占一个编号，命中时按各自的编号各报一次
    // （同一个词可能被两条模式共用，各自的建议改法不同，不能合并）。
    void build(const std::vector<std::string>& needles);

    bool empty() const { return bucket_items_.empty() && short_items_.empty(); }

    // 建进来的串有多少条（编号从 0 数到它减一）。
    std::size_t size() const { return lengths_.size(); }

    // 扫 haystack[from, size)：每命中一串就回调 on_hit(串编号, 起点, 终点)。
    // 起点升序回调；同一个起点上有多串命中时按建索引时的先后回调，调用方不必依赖这个次序。
    // on_progress 每走 64KB 调一次（传当前位置），调用方拿它查时限，不必自己数。
    //
    // 命中一定满足 begin >= from 且 end <= haystack.size()：串是从 from 起往后长出来的，
    // 起点在 from 之前的命中这里不报（与「从 from 起往后找」的语义一致）。
    template <class OnHit, class OnProgress>
    void scan(std::string_view haystack, std::size_t from, OnHit&& on_hit,
              OnProgress&& on_progress) const {
        if (from >= haystack.size() || (bucket_items_.empty() && short_items_.empty())) return;
        const char* data = haystack.data();
        const std::size_t size = haystack.size();
        // 每走这么多字节回头看一眼时限：一次查表几十纳秒，摊到 64KB 上一次可以忽略。
        constexpr std::size_t kProgressBytes = 1 << 16;
        std::size_t next_progress = (from | (kProgressBytes - 1)) + 1;

        std::size_t pos = from;
        // 末尾留三格给慢路：快路一次读四字节、看前三字节
        const std::size_t fast_end = size > kSlotBytes ? size - kSlotBytes : 0;
        while (pos < size) {
            if (pos < fast_end) {
                std::uint32_t word = 0;
                std::memcpy(&word, data + pos, sizeof(word));
                const std::uint32_t slot = slot_of(word);
                if (slot_begin_[slot] != slot_begin_[slot + 1])
                    at_candidate(bucket_items_, slot_begin_[slot], slot_begin_[slot + 1], data,
                                 size, pos, on_hit);
                // 一到两字节的那几串按首字节分桶：这类串很少（单个标点、单个字母），
                // 首字节对不上就整桶都不必看。
                if (has_short_ && short_first_[word & 0xFF] != 0) {
                    const auto first = static_cast<std::size_t>(word & 0xFF);
                    at_candidate(short_items_, short_begin_[first], short_begin_[first + 1], data,
                                 size, pos, on_hit);
                }
                pos += advance_of(word);
            } else {
                // 末尾几格凑不满四字节：拿不出三字节前缀，就每一串都当候选比一遍。
                // 只有最后几格走这里，代价可以忽略，换来的是不必为边界写第二套判据。
                for (std::uint32_t index = 0; index < needles_.size(); ++index)
                    if (match_at(index, data, size, pos)) on_hit(index, pos, pos + lengths_[index]);
                ++pos;
            }
            if (pos >= next_progress) {
                on_progress(pos);
                next_progress = pos + kProgressBytes;
            }
        }
    }

private:
    // 前缀索引的格子数：取 12 位，一张表（slot_begin_）16KB。
    // 一份规则的编译结果一张，十五条算法规则不占，一百来条数据规则全建起来约 1.6MB。
    static constexpr std::size_t kSlotBits = 12;
    static constexpr std::size_t kSlotCount = std::size_t{1} << kSlotBits;
    // 三字节一节：快路一次读四字节，末尾留这几格走慢路。
    static constexpr std::size_t kSlotBytes = 3;
    // 整串比的时候先看头八个字节，短串一次比完。
    static constexpr std::size_t kHeadBytes = 8;

    struct Head {
        std::uint64_t word = 0;  // 前八字节（小端），不足八字节的补零
        std::uint64_t mask = 0;  // 有效字节对应的掩码
    };

    // 三字节前缀落到哪一格：乘一个常数取高位。撞格的只会多留候选，不会漏。
    static std::uint32_t slot_of(std::uint32_t word) {
        return ((word & 0xFFFFFFu) * 2654435761u) >> (32 - kSlotBits);
    }

    // 快路一次走几个字节：这一格是三字节汉字的头、且后两格确实是续字节，就整字跳三格。
    static std::size_t advance_of(std::uint32_t word) {
        const std::uint32_t first = word & 0xFFu;
        if (first >= 0xE0 && first <= 0xEF && (word & 0xC000u) == 0x8000u &&
            (word & 0xC00000u) == 0x800000u)
            return 3;
        return 1;
    }

    // 编号 index 那一串在 at 处对不对得上。
    bool match_at(std::uint32_t index, const char* data, std::size_t size, std::size_t at) const {
        const std::size_t length = lengths_[index];
        if (at + length > size) return false;
        if (length > kHeadBytes) {
            std::uint64_t word = 0;
            std::memcpy(&word, data + at, kHeadBytes);
            if ((word & heads_[index].mask) != heads_[index].word) return false;
            return std::memcmp(data + at + kHeadBytes, needles_[index].data() + kHeadBytes,
                               length - kHeadBytes) == 0;
        }
        std::uint64_t word = 0;
        std::memcpy(&word, data + at, length);
        return (word & heads_[index].mask) == heads_[index].word;
    }

    // 把桶里 [begin, end) 这几串在 at 处各比一遍。items 是这张表自己的串编号表：
    // 三字节以上的在 bucket_items_，一到两字节的在 short_items_。
    template <class OnHit>
    void at_candidate(const std::vector<std::uint32_t>& items, std::uint32_t begin,
                      std::uint32_t end, const char* data, std::size_t size, std::size_t at,
                      OnHit& on_hit) const {
        for (std::uint32_t i = begin; i < end; ++i) {
            const std::uint32_t index = items[i];
            if (match_at(index, data, size, at)) on_hit(index, at, at + lengths_[index]);
        }
    }

    std::vector<std::string> needles_;
    std::vector<std::uint32_t> lengths_;
    std::vector<Head> heads_;
    // 按三字节前缀分桶：slot_begin_[格号] 到 slot_begin_[格号 + 1] 是这一格里的串编号
    // 在 bucket_items_ 里的区间。三字节及以上的串走这里。
    std::vector<std::uint32_t> slot_begin_;
    std::vector<std::uint32_t> bucket_items_;
    // 一到两字节的串走这张：按首字节分桶，首字节都对不上就整桶不看。
    std::array<std::uint32_t, 257> short_begin_{};
    std::vector<std::uint32_t> short_items_;
    std::array<std::uint8_t, 256> short_first_{};
    bool has_short_ = false;
};

}  // namespace texttool
