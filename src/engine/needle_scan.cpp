// 三字节前缀索引的建索引部分。扫描循环是热的，写在头文件里（见 needle_scan.h）。
#include "engine/needle_scan.h"

#include <array>
#include <cstddef>
#include <cstdint>
#include <string>
#include <vector>

namespace texttool {

void NeedleScanner::build(const std::vector<std::string>& needles) {
    needles_ = needles;
    lengths_.clear();
    heads_.clear();
    bucket_items_.clear();
    slot_begin_.assign(kSlotCount + 2, 0);  // kSlotCount 个格，外加末位
    short_items_.clear();
    short_begin_.fill(0);
    short_first_.fill(0);
    has_short_ = false;

    lengths_.reserve(needles_.size());
    heads_.reserve(needles_.size());
    for (const std::string& needle : needles_) {
        lengths_.push_back(static_cast<std::uint32_t>(needle.size()));
        Head head;
        const std::size_t take = needle.size() < kHeadBytes ? needle.size() : kHeadBytes;
        if (take != 0) std::memcpy(&head.word, needle.data(), take);
        for (std::size_t i = 0; i < take; ++i)
            head.mask |= static_cast<std::uint64_t>(0xFF) << (i * 8);
        heads_.push_back(head);
    }

    // 三字节及以上的串按三字节前缀分桶；一到两字节的串三字节前缀没有意义
    // （后面补的零对不上正文），另按首字节分桶。空串没有起点可找，两边都不进。
    std::vector<std::uint32_t> slots(needles_.size(), kSlotCount);
    std::array<std::uint32_t, kSlotCount + 1> long_counts{};
    std::array<std::uint32_t, 256> short_counts{};
    for (std::uint32_t index = 0; index < needles_.size(); ++index) {
        const std::string& needle = needles_[index];
        if (needle.size() >= kSlotBytes) {
            std::uint32_t word = 0;
            std::memcpy(&word, needle.data(), sizeof(word));
            slots[index] = slot_of(word);
            ++long_counts[slots[index]];
        } else if (!needle.empty()) {
            const auto first = static_cast<unsigned char>(needle.front());
            ++short_counts[first];
            short_first_[first] = 1;
            has_short_ = true;
        }
    }

    std::uint32_t running = 0;
    for (std::size_t slot = 0; slot < long_counts.size(); ++slot) {
        slot_begin_[slot] = running;
        running += long_counts[slot];
    }
    slot_begin_[long_counts.size()] = running;
    bucket_items_.resize(running);
    std::array<std::uint32_t, kSlotCount + 1> long_cursor{};
    for (std::size_t slot = 0; slot < long_cursor.size(); ++slot)
        long_cursor[slot] = slot_begin_[slot];
    for (std::uint32_t index = 0; index < needles_.size(); ++index) {
        if (slots[index] == kSlotCount) continue;
        bucket_items_[long_cursor[slots[index]]++] = index;
    }

    running = 0;
    for (std::size_t byte = 0; byte < 256; ++byte) {
        short_begin_[byte] = running;
        running += short_counts[byte];
    }
    short_begin_[256] = running;
    short_items_.resize(running);
    std::array<std::uint32_t, 256> short_cursor{};
    for (std::size_t byte = 0; byte < 256; ++byte) short_cursor[byte] = short_begin_[byte];
    for (std::uint32_t index = 0; index < needles_.size(); ++index) {
        const std::string& needle = needles_[index];
        if (needle.empty() || needle.size() >= kSlotBytes) continue;
        short_items_[short_cursor[static_cast<unsigned char>(needle.front())]++] = index;
    }
}

}  // namespace texttool
