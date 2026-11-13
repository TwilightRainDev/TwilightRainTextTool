#!/usr/bin/env python3
"""比两棵目录树，把「只差一个 BOM」的文件单独报出来。

处理命令的写盘按用户原编码写回，而 .NET 那边一律写带 BOM 的 UTF-8：
同一份 UTF-8 无 BOM 的稿子，两边正文一字不差，只有开头三个字节不同。
这类差异是有意的（不让用户的稿子处理完换个编码），所以单独计数，不算不一致。

用法：python3 tools/compare_tree.py <参考目录> <原生目录>
输出：每个真差异一行 DIFF <相对路径>；最后一行 SUMMARY <真差异数> <只差 BOM 数>
退出码：有真差异退 1，否则退 0。
"""

import os
import sys

BOM = b"\xef\xbb\xbf"


def files_under(root):
    found = {}
    for base, _, names in os.walk(root):
        for name in names:
            path = os.path.join(base, name)
            found[os.path.relpath(path, root)] = path
    return found


def main():
    if len(sys.argv) != 3:
        print("用法：compare_tree.py <参考目录> <原生目录>", file=sys.stderr)
        return 2
    reference, native = sys.argv[1], sys.argv[2]
    left = files_under(reference)
    right = files_under(native)

    real_diffs = 0
    bom_only = 0
    for rel in sorted(set(left) | set(right)):
        if rel not in left or rel not in right:
            print(f"DIFF {rel}: 只在一边存在")
            real_diffs += 1
            continue
        with open(left[rel], "rb") as f:
            a = f.read()
        with open(right[rel], "rb") as f:
            b = f.read()
        if a == b:
            continue
        # 只差开头一个 BOM 的，算有意不同
        strip = lambda data: data[3:] if data.startswith(BOM) else data
        if rel.endswith(".exit") or strip(a) != strip(b):
            print(f"DIFF {rel}")
            real_diffs += 1
        else:
            bom_only += 1

    print(f"SUMMARY {real_diffs} {bom_only}")
    return 1 if real_diffs else 0


if __name__ == "__main__":
    sys.exit(main())
