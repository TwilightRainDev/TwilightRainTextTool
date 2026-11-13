#!/usr/bin/env python3
"""按固定配方拼出一份约 15MB 的中文长稿，给内存与耗时测量用。

配方是「把三份仓库文档首尾相接，循环重复，够长就截」。同一份输入、同一台机器，
两次测量的数字才能互相比。稿件落在临时目录，不进仓库，也不留在源码树里。

用法：
    python3 tools/make_big_doc.py                    # 默认写到系统临时目录
    python3 tools/make_big_doc.py --out /tmp/big.txt --size 15728640
    python3 tools/make_big_doc.py --print-path       # 只打印路径，给脚本取用

退出码：0 成功；2 找不到任何一份来源文档。
"""

import argparse
import os
import sys
import tempfile
from pathlib import Path

DEFAULT_TARGET = 15_070_000  # 字节。客户口径是 15MB 稿件。

# 三份取自工作区的长中文文档：内容全是散文式说明，标点、段落、代码块都有，
# 与客户稿件的样子接近，也不含随机成分，同一台机器每次拼出来逐字节相同。
DEFAULT_SOURCES = [
    "Docs/Project/TextTool/ARCHITECTURE.md",
    "Docs/ChangeLog/Workspace.md",
    "Docs/Debt/TECH-DEBT.md",
]


def workspace_root() -> Path:
    return Path(__file__).resolve().parents[3]


def build(target: int, sources: list[Path]) -> bytes:
    parts = [p.read_bytes() for p in sources if p.is_file()]
    if not parts:
        raise SystemExit("找不到来源文档，先看看 Docs 下这几份还在不在")
    chunk = b"\n\n".join(parts)
    body = bytearray()
    while len(body) < target:
        body += chunk
    del body[target:]
    # 截在最后一个换行上，免得末尾留半个字，把解码的路子也带进测量
    cut = body.rfind(b"\n")
    if cut > 0:
        del body[cut + 1:]
    return bytes(body)


def main() -> int:
    parser = argparse.ArgumentParser(description="拼一份约 15MB 的中文长稿")
    parser.add_argument("--out", help="写到哪个文件；不给就写到系统临时目录")
    parser.add_argument("--size", type=int, default=DEFAULT_TARGET, help="目标字节数")
    parser.add_argument("--print-path", action="store_true", help="只打印路径")
    parser.add_argument("--source", action="append", help="来源文档，可给多次；不给用默认三份")
    args = parser.parse_args()

    root = workspace_root()
    names = args.source if args.source else DEFAULT_SOURCES
    sources = [Path(n) if os.path.isabs(n) else root / n for n in names]

    out = Path(args.out) if args.out else Path(tempfile.gettempdir()) / f"texttool-big-{args.size}.txt"
    if not out.is_file() or out.stat().st_size < args.size * 0.95:
        out.parent.mkdir(parents=True, exist_ok=True)
        out.write_bytes(build(args.size, sources))

    if args.print_path:
        print(out)
        return 0
    print(f"稿件：{out}")
    print(f"大小：{out.stat().st_size / 1e6:.2f} MB")
    print(f"来源：{'、'.join(p.name for p in sources if p.is_file())}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
