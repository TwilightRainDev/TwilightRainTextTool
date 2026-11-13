#!/usr/bin/env python3
"""验收前把三条硬指标量出来：做活的耗时、整进程峰值内存、发布压缩包的体积。

三条的来源：
   耗时：同一台机器、同一份 15MB 稿件，新程序不许比 .NET 那边慢。
   内存：客户口径，15MB 稿件干活时整进程峰值不超过 40MB（读 /proc 的 VmRSS）。
         命令行与界面各量一行：界面靠 TEXTTOOL_SELF_CHECK 自己填稿、自己处理、自己写盘、自己退出，
         与用户点「开始处理」走同一条路。
   体积：一份发布压缩包解压前不超过 10MB。

每次验收前跑这一条命令，三项都出数字并当场判过线，超了直接回非零退出码，
拿不到数字的项也回非零（免得没量到被当成通过）。

用法：
    python3 tools/acceptance_check.py
    python3 tools/acceptance_check.py --doc /tmp/稿件.txt --verbose
    python3 tools/acceptance_check.py --native ./build-linux/texttool --skip-reference
    python3 tools/acceptance_check.py --gui ./build-linux/texttool-gui

界面那一行要有 X 会话（DISPLAY 指过去）；没有 DISPLAY 时界面起不来，那一行会写「没量到」。

退出码：0 三项都在线内；2 有项超线；3 有项没量到。
"""

import argparse
import json
import os
import re
import subprocess
import sys
import tempfile
import zipfile
from pathlib import Path

MEMORY_LIMIT_MB = 40.0     # 客户口径
# 耗时的判定带一条噪声带：两个程序中位数差不到这么多秒，就算持平。
# 5.7 秒上下时实测同一程序连跑几次会差 0.2 秒上下，差得比噪声还小的结论没有意义。
TIME_NOISE_SECONDS = 0.2
PACKAGE_LIMIT_MB = 10.0    # 一份发布压缩包
# 对照用的 dll：要么用 TEXTTOOL_REF_DLL 指过去，要么就摆在下面这个路径上（桌面版已归档）。
# 两样都没有时这一行写「没配」，不拿旧机器上的临时目录兜底。
REF_DLL_RELATIVE = "../../Config/Archive/TextTool/TextTool.Cli/bin/Release/net8.0/texttool.dll"
RULES_JSON = "assets/default_lint_rules.json"
# 一份压缩包里放什么：两个程序、规则表、界面字体、程序图标与作者头像
# （字体与这两张图都和程序同一个目录，压缩包里也这样摆）。
PACKAGE_FILES = ["texttool", "texttool-gui", "TextToolWenKai-Regular.ttf", "icon.png", "avatar.png",
                 "default_vn_schemes.json"]


def here() -> Path:
    return Path(__file__).resolve().parents[1]


def run_capture(command, verbose):
    if verbose:
        print("  跑：" + " ".join(str(c) for c in command))
    done = subprocess.run(command, capture_output=True, text=True)
    if done.returncode not in (0, 1) and verbose:  # lint 有命中就回 1，属正常
        print(f"  退出码 {done.returncode}，stderr：{done.stderr.strip()[:200]}")
    return done


def measure(binary_command, doc, limit_mb, verbose, repeat=1, extra_env=None):
    """用 measure_peak.py 跑一遍（或几遍取中位数），取峰值内存与耗时。

    耗时只差百分之几时，单次测量会被机器上的其他活儿带偏，所以取几次的中位数：
    结论要能重复，不能这次过下次不过。峰值取几次里最大的那次，并带回那次的内存分项
    （程序自申请、映射的文件页），超标时能看出是程序自己吃掉的还是映射进来的。
    """
    root = here()
    command = [sys.executable, str(root / "tools" / "measure_peak.py")]
    if limit_mb is not None:
        command += ["--limit", str(limit_mb)]
    for name, value in (extra_env or {}).items():
        command += ["--env", f"{name}={value}"]
    command += list(binary_command)
    peak = {"memory_mb": None, "anon_mb": None, "file_mb": None}
    seconds = []
    for _ in range(repeat):
        done = run_capture(command, verbose)
        memory = re.search(r"峰值 RSS\s*：\s*([0-9.]+) MB", done.stdout)
        anon = re.search(r"程序自申请：\s*([0-9.]+) MB", done.stdout)
        mapped = re.search(r"映射的文件页：\s*([0-9.]+) MB", done.stdout)
        elapsed = re.search(r"运行时长：([0-9.]+)s", done.stdout)
        if memory:
            value = float(memory.group(1))
            if peak["memory_mb"] is None or value >= peak["memory_mb"]:
                peak = {
                    "memory_mb": value,
                    "anon_mb": float(anon.group(1)) if anon else None,
                    "file_mb": float(mapped.group(1)) if mapped else None,
                }
        if elapsed:
            seconds.append(float(elapsed.group(1)))
    def median(values):
        if not values:
            return None
        ordered = sorted(values)
        return ordered[len(ordered) // 2]
    peak["seconds"] = median(seconds)
    peak["spread"] = (min(seconds), max(seconds)) if seconds else None
    return peak


def big_doc(doc, verbose):
    if doc is not None:
        return Path(doc)
    root = here()
    done = run_capture([sys.executable, str(root / "tools" / "make_big_doc.py"), "--print-path"], verbose)
    path = Path(done.stdout.strip())
    if not path.is_file():
        raise SystemExit("拼不出 15MB 稿件，先看 tools/make_big_doc.py 能不能跑")
    return path


def reference_dll(root):
    """对照用的 dll：只认环境变量 TEXTTOOL_REF_DLL 与仓库相对路径，取不到返回 None。"""
    if os.environ.get("TEXTTOOL_REF_DLL"):
        return Path(os.environ["TEXTTOOL_REF_DLL"])
    candidate = (root / REF_DLL_RELATIVE).resolve()
    return candidate if candidate.is_file() else None


def package_size(root, native_binary, windows=False):
    """把一份发布压缩包压出来量体积。打包内容和真发的一致：两个程序加规则表与素材。"""
    binary_dir = Path(native_binary).resolve().parent
    suffix = ".exe" if windows else ""
    names = [name + suffix if name.startswith("texttool") else name for name in PACKAGE_FILES]
    members = []
    for name in names:
        candidate = binary_dir / name
        if candidate.is_file():
            members.append(candidate)
    rules = (root / RULES_JSON).resolve()
    if rules.is_file():
        members.append(rules)
    if not members:
        return None, "构建产物还没生成，先跑一次构建"

    with tempfile.TemporaryDirectory() as tmp:
        archive = Path(tmp) / "package.zip"
        with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as zf:
            for member in members:
                zf.write(member, arcname=member.name)
        return archive.stat().st_size / 1e6, "、".join(m.name for m in members)


def main() -> int:
    parser = argparse.ArgumentParser(description="量出验收三条硬指标并判过线")
    parser.add_argument("--native", default=str(here() / "build-linux" / "texttool"),
                        help="新程序的命令行可执行文件")
    parser.add_argument("--gui", default=str(here() / "build-linux" / "texttool-gui"),
                        help="界面可执行文件；量它自己跑一份稿时的峰值内存，不给就按构建目录找")
    parser.add_argument("--doc", help="用哪份稿件；不给就按固定配方拼一份 15MB 的")
    parser.add_argument("--skip-reference", action="store_true", help="不跑对照基准，耗时只报数不判")
    parser.add_argument("--repeat", type=int, default=5,
                        help="耗时取几遍的中位数，默认 5；只跑一遍就写 1")
    parser.add_argument("--verbose", action="store_true", help="把跑过的命令打印出来")
    parser.add_argument("--windows", help="Windows 构建目录里的 texttool.exe；给了就一并量 Windows 版发布包")
    args = parser.parse_args()

    root = here()
    native = Path(args.native)
    if not native.is_file():
        print(f"找不到新程序：{native}，先构建")
        return 3

    doc = big_doc(args.doc, args.verbose)
    doc_mb = doc.stat().st_size / 1e6
    print(f"稿件：{doc}（{doc_mb:.2f} MB）")
    print(f"程序：{native}")

    rows = []

    # 一、内存与耗时：新程序
    print("量内存与耗时（新程序）……")
    native_run = measure([str(native), "lint", str(doc)], doc, MEMORY_LIMIT_MB, args.verbose, args.repeat)
    rows.append(("峰值内存（新程序）", native_run["memory_mb"], MEMORY_LIMIT_MB, "MB"))

    # 二、界面的峰值内存：让界面自己把这份稿填进「接回断行」页、处理后写盘、再自己退出。
    # 跑的路径与用户点「开始处理」相同，所以这个数就是用户处理这份稿时的峰值。
    # 界面起不来（没有 DISPLAY）或者自检没写成，拿不到数字，这一行按「没量到」处理。
    gui = Path(args.gui)
    gui_run = {"memory_mb": None, "anon_mb": None, "file_mb": None}
    if gui.is_file():
        print("量内存（界面自检模式）……")
        gui_run = measure([str(gui)], doc, MEMORY_LIMIT_MB, args.verbose, 1,
                          extra_env={"TEXTTOOL_SELF_CHECK": str(doc)})
    rows.append(("峰值内存（界面）", gui_run["memory_mb"], MEMORY_LIMIT_MB, "MB"))

    # 三、耗时上限：同一份稿、同一台机器上的 .NET 命令行
    ceiling = None
    ref_missing = False
    if not args.skip_reference:
        dll = reference_dll(root)
        dotnet = subprocess.run(["which", "dotnet"], capture_output=True, text=True).stdout.strip()
        if dll is not None and dotnet:
            print("量耗时（对照）……")
            ref_run = measure([dotnet, str(dll), "lint", str(doc)], doc, None, args.verbose, args.repeat)
            ceiling = ref_run["seconds"]
            rows.append(("峰值内存（对照基准）", ref_run["memory_mb"], None, "MB"))
        else:
            ref_missing = True
            rows.append(("耗时上限（对照 dll 没配）", None, None, "秒"))
    time_row_index = len(rows)
    rows.append(("耗时（新程序）", native_run["seconds"], ceiling, "秒"))

    # 四、发布压缩包体积
    print("量发布压缩包体积……")
    size_mb, members = package_size(root, args.native)
    rows.append(("发布压缩包（麒麟版）", size_mb, PACKAGE_LIMIT_MB, "MB"))
    if members:
        print(f"  包内：{members}")
    if args.windows:
        win_mb, win_members = package_size(root, args.windows, windows=True)
        rows.append(("发布压缩包（Windows 版）", win_mb, PACKAGE_LIMIT_MB, "MB"))
        if win_members:
            print(f"  Windows 包内：{win_members}")

    print()
    print(f"{'项目':<26}{'实测':>10}{'上限':>10}  结论")
    print("-" * 68)
    over = False
    unverified = False
    for name, value, limit, unit in rows:
        if value is None:
            shown_value = "未量到"
            shown_limit = "-" if limit is None else f"{limit:.2f} {unit}"
            verdict = "没量到"
            unverified = True
        else:
            shown_value = f"{value:.2f} {unit}"
            if limit is None:
                shown_limit = "-"
                verdict = "只报数"
            else:
                shown_limit = f"{limit:.2f} {unit}"
                verdict = "在线内" if value <= limit else "超线"
                over = over or value > limit
        # 耗时这一行单独判：差值在噪声带以内算持平，不算超线也不算更快。
        if name.startswith("耗时（新程序）") and value is not None and limit is not None:
            gap = value - limit
            if abs(gap) <= TIME_NOISE_SECONDS:
                verdict = "持平"
            elif gap > 0:
                verdict = "超线"
                over = True
            else:
                verdict = "在线内"
        print(f"{name:<26}{shown_value:>10}{shown_limit:>10}  {verdict}")

    if gui_run["memory_mb"] is not None and gui_run["anon_mb"] is not None:
        print()
        print("  界面那一行的分项（取峰值最高的那一轮）：")
        print(f"    程序自申请：{gui_run['anon_mb']:.2f} MB    映射的文件页：{gui_run['file_mb']:.2f} MB")

    if native_run.get("spread") and native_run["spread"][0] != native_run["spread"][1]:
        low, high = native_run["spread"]
        print(f"  耗时几遍：最少 {low:.2f} 秒，最多 {high:.2f} 秒，表里取的是中位数。")
        print(f"  耗时的判定：与基准差不到 {TIME_NOISE_SECONDS:.2f} 秒算持平，差得再多才判快慢。")

    # 没量到的项单列出来，写清差什么，别让「没量到」看起来像通过
    missing_notes = []
    if not args.windows:
        missing_notes.append("  Windows 版压缩包：没给 --windows <构建目录里的 texttool.exe>，这一行没量。")
    if ref_missing:
        missing_notes.append("  耗时的对照 dll 没配：设 TEXTTOOL_REF_DLL 指过去，"
                             f"或者把它编到 {REF_DLL_RELATIVE}（相对仓库根）。")
    if not gui.is_file():
        missing_notes.append(f"  界面的峰值内存：找不到界面程序 {gui}，用 --gui 指过去，或者先构建界面。")
    elif gui_run["memory_mb"] is None:
        missing_notes.append("  界面的峰值内存：界面自检没跑完。先确认 DISPLAY 指向一个能用的 X 会话；")
        missing_notes.append(f"    单独跑这一条看原因：DISPLAY=:0 TEXTTOOL_SELF_CHECK={doc} {gui}")
    if missing_notes:
        print()
        print("没有量到的：")
        for note in missing_notes:
            print(note)

    print()
    if over:
        print("结论：有指标超线，不能验收。")
        return 2
    if unverified:
        print("结论：有指标没量到，不算通过。")
        return 3
    print("结论：三项都在线内。")
    return 0


if __name__ == "__main__":
    sys.exit(main())
