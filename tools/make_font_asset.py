#!/usr/bin/env python3
"""把自带的界面字体从两份上游文件做出来，重新生成 assets/fonts 下那份 TTF。

要两份输入：

   1. 已经子集化过的霞鹜文楷（博客站上给网页用的那份 woff2）。
      界面要用的几千个汉字来自它，字形的取舍以它为准。
   2. 霞鹜文楷 Mono 的完整版 TTF，同一个版本号（1.522）。
      界面文字里有「─」「×」「€」三个字（预览里的场景标记、重复次数、GBK 的 0x80），
      给定的子集不含这三个。缺的三个字形从完整版里取。

做四件事，然后写进 assets/fonts/：

   1. woff2 解成 TTF；
   2. 从完整版里取出缺的三个字形；
   3. 把三个字形合进子集，其余字形逐点保持不变（合完会核对每个字形的轮廓与步进）；
   4. 改名字表里的家族名与 PostScript 名。

第 4 步是许可证要求的：子集化属于「改动过的版本」，Open Font License 保留字体名
（霞鹜、霞鶩、落霞孤鹜、落霞孤鶩、LXGW）不许用在改动过的版本上，上游额外许可只放开
「只用于网页字体分发」这一种，自带分发的桌面程序不在其中。所以家族名改成
TextTool WenKai，版权声明与许可证原样保留。

用法（需要 fontTools 与 brotli）：
    python3 tools/make_font_asset.py \
        --subset <子集 woff2> --full <完整版 Mono TTF> --out assets/fonts/
"""

import argparse
import sys

from fontTools.merge import Merger
from fontTools.ttLib import TTFont

# 程序会往界面文字里写、而给定子集里没有的三个字形。
REQUIRED_CODEPOINTS = (0x00D7, 0x20AC, 0x2500)

NEW_FAMILY = "TextTool WenKai"
NEW_PS_NAME = "TextToolWenKai-Regular"
NEW_UNIQUE_ID = "TextTool WenKai: subset of LXGW WenKai Mono 1.522"
DESCRIPTION = (
    "Subset of LXGW WenKai Mono 1.522 for the TwilightRain text tool user interface. "
    "Contains the characters the program displays; not a general purpose font."
)
LICENSE_TEXT = "SIL Open Font License 1.1"
LICENSE_URL = "https://openfontlicense.org"


def convert(woff2, path):
    font = TTFont(woff2)
    font.flavor = None
    font.save(path)
    return font


def extract(full, path):
    """从完整版里只取出缺的三个字形，单独存一份小字体用于合并。"""
    font = TTFont(full)
    cmap = font.getBestCmap()
    from fontTools import subset

    options = subset.Options()
    options.layout_features = []
    options.drop_tables += ["GSUB", "GPOS"]
    subsetter = subset.Subsetter(options)
    subsetter.populate(unicodes=[cp for cp in REQUIRED_CODEPOINTS if cp in cmap])
    subsetter.subset(font)
    font.save(path)


def merge(subset_path, extra_path, out_path):
    merged = Merger().merge([subset_path, extra_path])
    merged["head"].unitsPerEm = 1000
    merged.save(out_path)
    return merged


def rename(font):
    """改家族名与 PostScript 名，版权与版本原样留着。"""
    family = NEW_FAMILY
    records = {
        1: family,
        3: NEW_UNIQUE_ID,
        4: family,
        6: NEW_PS_NAME,
        10: DESCRIPTION,
        13: LICENSE_TEXT,
        14: LICENSE_URL,
    }
    name_table = font["name"]
    for name_id, value in records.items():
        name_table.setName(value, name_id, 3, 1, 0x409)
    name_table.setName(family, 16, 3, 1, 0x409)
    name_table.setName("Regular", 17, 3, 1, 0x409)


def check(subset, merged):
    """核对合并结果：原子集的字形一个不少、轮廓与步进一致，三个字形补上了。"""
    base = TTFont(subset)
    glyphs_before = set(base.getGlyphOrder())
    glyphs_after = set(merged.getGlyphOrder())
    missing = glyphs_before - glyphs_after
    if missing:
        raise SystemExit(f"合并后少了字形：{sorted(missing)[:10]}")

    for name in base.getGlyphOrder():
        if base["hmtx"][name] != merged["hmtx"][name]:
            raise SystemExit(f"合并后步进变了：{name}")
        a = base["glyf"][name]
        b = merged["glyf"][name]
        if a.numberOfContours != b.numberOfContours:
            raise SystemExit(f"合并后轮廓数变了：{name}")
        if a.numberOfContours > 0:
            if (list(a.coordinates) != list(b.coordinates)
                    or list(a.flags) != list(b.flags)
                    or list(a.endPtsOfContours) != list(b.endPtsOfContours)):
                raise SystemExit(f"合并后轮廓点变了：{name}")

    cmap = set(merged.getBestCmap())
    absent = [hex(cp) for cp in REQUIRED_CODEPOINTS if cp not in cmap]
    if absent:
        raise SystemExit(f"缺的字形没补上：{absent}")

    families = {str(rec) for rec in merged["name"].names if rec.nameID in (1, 4, 16)}
    if families != {NEW_FAMILY}:
        raise SystemExit(f"家族名没改对：{families}")


def main():
    parser = argparse.ArgumentParser(description="做一份自带界面字体")
    parser.add_argument("--subset", required=True, help="子集化的 woff2")
    parser.add_argument("--full", required=True, help="完整版 Mono TTF，版本要与子集一致")
    parser.add_argument("--out", default="assets/fonts", help="写到哪里，默认 assets/fonts")
    parser.add_argument("--work", default="/tmp/texttool-font", help="中间文件放哪")
    parser.add_argument("--name", default="TextToolWenKai-Regular.ttf", help="产出的文件名")
    args = parser.parse_args()

    import os
    import tempfile

    os.makedirs(args.work, exist_ok=True)
    os.makedirs(args.out, exist_ok=True)

    with tempfile.TemporaryDirectory() as tmp:
        subset_path = os.path.join(tmp, "subset.ttf")
        extra_path = os.path.join(tmp, "extra.ttf")
        merged_path = os.path.join(args.work, "merged.ttf")

        convert(args.subset, subset_path)
        extract(args.full, extra_path)
        merged = merge(subset_path, extra_path, merged_path)
        rename(merged)
        check(subset_path, merged)
        out_path = os.path.join(args.out, args.name)
        merged.save(out_path)

    size = os.path.getsize(out_path)
    print(f"写出 {out_path}，{size} 字节")
    return 0


if __name__ == "__main__":
    sys.exit(main())
