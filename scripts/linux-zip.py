#!/usr/bin/env python3
"""把 Linux 发布目录打成 zip，并把可执行位写进条目。

Compress-Archive 与多数 Windows 侧打包工具不写 Unix 权限，条目解到 Linux 上全是 0644，
启动器与界面都起不来（`Permission denied`）。权限位存在 zip 条目的 external attributes
高 16 位里，这里显式写：给定名字的启动器 0755，其余 0644。

用法：linux-zip.py <发布目录> <输出 zip> [可执行文件名...]
退出码：0 成功，2 用法或路径错误。
"""

import os
import sys
import time
import zipfile

S_IFREG = 0o100000
MODE_DEFAULT = 0o644
MODE_EXEC = 0o755


def main(argv):
    if len(argv) < 3:
        print("用法：linux-zip.py <发布目录> <输出 zip> [可执行文件名...]", file=sys.stderr)
        return 2

    src = os.path.abspath(argv[1])
    out = os.path.abspath(argv[2])
    exec_names = set(argv[3:])

    if not os.path.isdir(src):
        print("发布目录不存在：%s" % src, file=sys.stderr)
        return 2

    out_dir = os.path.dirname(out)
    if out_dir:
        os.makedirs(out_dir, exist_ok=True)

    count = 0
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as zf:
        for root, dirs, files in os.walk(src):
            dirs.sort()
            for name in sorted(files):
                path = os.path.join(root, name)
                rel = os.path.relpath(path, src).replace(os.sep, "/")
                info = zipfile.ZipInfo(rel, date_time=time.localtime(os.path.getmtime(path))[:6])
                info.external_attr = (S_IFREG | (MODE_EXEC if name in exec_names else MODE_DEFAULT)) << 16
                info.compress_type = zipfile.ZIP_DEFLATED
                with open(path, "rb") as fh:
                    zf.writestr(info, fh.read())
                count += 1

    print("打包 %s（%d 个文件，可执行：%s）" % (out, count, "、".join(sorted(exec_names)) or "无"))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
