#!/usr/bin/env python3
"""把一份构建产目录打成可以直接解包就用的发布压缩包。

用法：
    python3 tools/package.py build-linux kylin gui
    python3 tools/package.py build-linux kylin cli
    python3 tools/package.py C:/.../tt-win windows gui --out dist

三个位置参数依次是「程序所在目录」「平台名」「含不含界面」（cli 或 gui）。
平台名里带 windows 就按 .exe 找程序、不给可执行位；其余按无后缀处理。
压缩包的默认落点是仓库下的 dist/，文件名是 texttool-<平台名>-<cli|gui>.zip。

包里带什么：
  cli  texttool、default_lint_rules.json
  gui  在上面两份之外，加 texttool-gui、界面字体、程序图标、作者头像、默认角色表
两份都另带一份 使用说明.txt，内容按含不含界面分别写。

规则表与素材先找程序目录（构建时已复制过去的那些），找不到再回仓库里取，
这样打 Windows 产出的目录时，素材与规则表不必再手工摆一遍。

压缩包按固定时间戳写，同一份产物跑两次得到的字节完全一样，好对比。
退出码：0 打好了；1 该带的文件少了；2 体积超过上限。
"""

import argparse
import hashlib
import sys
import zipfile
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]
SIZE_LIMIT = 10 * 1024 * 1024  # 10MB，按 1024 进制

# 随构建产出的素材：构建时复制到程序旁边，仓库里也各有一份原件。
# 键是包里用的名字，值是仓库里的原件位置（程序目录里没有时才用）。
# 规则表按同一套找法，位置写在下面的 RULES_SOURCES。
ASSET_SOURCES = {
    "default_vn_schemes.json": "assets/default_vn_schemes.json",
    "TextToolWenKai-Regular.ttf": "assets/fonts/TextToolWenKai-Regular.ttf",
    "icon.png": "assets/images/icon.png",
    "avatar.png": "assets/images/avatar.png",
}

RULES_SOURCES = ["assets/default_lint_rules.json"]

CLI_FILES = ["default_lint_rules.json", "default_vn_schemes.json"]
GUI_FILES = [
    "default_lint_rules.json",
    "TextToolWenKai-Regular.ttf",
    "icon.png",
    "avatar.png",
    "default_vn_schemes.json",
]

CLI_README = """\
文本工具（命令行版）：不用安装，整个文件夹拷到别的电脑上也能直接用。

启动：打开命令行窗口，进到这个文件夹，敲 texttool 回车，就会列出它能做的事。
处理一份稿子，例如：texttool merge 稿子.txt（Windows 上是 texttool.exe merge 稿子.txt）。

结果写在哪：处理完会在原稿旁边多出一个新文件，名字是原稿名加 _Processed，
  例如 稿子_Processed.txt。
会不会动原文件：不会。默认只读原稿；只有你明确加上 --overwrite 才会覆盖它。
字太小：窗口里的字号由系统的显示缩放决定；也可以把窗口拉大，或者按住 Ctrl 滚滚轮。
遇到问题：先看程序打印的提示，它写清了哪一行哪一处；还不行就把提示截图发给我们。
"""

GUI_README = """\
文本工具（界面版）：不用安装，整个文件夹拷到别的电脑上也能直接用，
  图标、字体、角色表都在这一个文件夹里，别单独把程序拷走。

启动：双击 texttool-gui（Windows 上是 texttool-gui.exe）。
怎么用：把稿子拖进窗口，或者按「选文件…」挑一份；最上面六个页签各管一件事。

结果写在哪：处理完先给你看一眼预览，觉得可以就按「保存…」挑个位置存下来。
会不会动原文件：不会。这个工具只读你的稿子，存下来的是另一个新文件。
字小怎么办：界面会按屏幕自动放大；还是嫌小，就在系统的显示设置里把缩放调大一档再打开。
遇到问题：先看窗口最底下一行的提示，再看「关于」页；还不行就把提示截图发给我们。
"""


def find_asset(name, program_dir):
    """先找程序目录（构建时复制到程序旁边的），再回仓库取原件。"""
    local = program_dir / name
    if local.is_file():
        return local
    candidates = RULES_SOURCES if name == "default_lint_rules.json" else [ASSET_SOURCES[name]]
    for candidate in candidates:
        path = (REPO_ROOT / candidate).resolve()
        if path.is_file():
            return path
    return None


def collect(program_dir, platform, gui):
    exe_suffix = ".exe" if "windows" in platform.lower() else ""
    names = ["texttool" + exe_suffix]
    if gui:
        names.append("texttool-gui" + exe_suffix)
    names += GUI_FILES if gui else CLI_FILES

    found, missing = [], []
    for name in names:
        path = find_asset(name, program_dir)
        (found if path else missing).append((name, path))
    return found, missing


def write_zip(zip_path, members, gui, executable):
    """members 是 [(包内文件名, 源路径)]。按固定时间戳写，两次跑出来字节一样。"""
    zip_path.parent.mkdir(parents=True, exist_ok=True)
    readme = (GUI_README if gui else CLI_README).encode("utf-8")
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for name, source in members:
            info = zipfile.ZipInfo(name, date_time=(1980, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = (0o755 if name in executable else 0o644) << 16
            archive.writestr(info, source.read_bytes())
        info = zipfile.ZipInfo("使用说明.txt", date_time=(1980, 1, 1, 0, 0, 0))
        info.compress_type = zipfile.ZIP_DEFLATED
        info.external_attr = 0o644 << 16
        archive.writestr(info, readme)


def main():
    parser = argparse.ArgumentParser(description="打一份发布压缩包")
    parser.add_argument("program_dir", help="程序所在目录，比如 build-linux")
    parser.add_argument("platform", help="平台名，比如 kylin、windows")
    parser.add_argument("kind", choices=["cli", "gui"], help="cli = 只有命令行，gui = 带界面")
    parser.add_argument("--out", default=str(REPO_ROOT / "dist"), help="压缩包写到哪个目录")
    args = parser.parse_args()

    program_dir = Path(args.program_dir).resolve()
    if not program_dir.is_dir():
        print(f"找不到程序目录：{program_dir}", file=sys.stderr)
        return 1

    gui = args.kind == "gui"
    found, missing = collect(program_dir, args.platform, gui)
    if missing:
        for name, _ in missing:
            print(f"缺文件：{name}", file=sys.stderr)
        return 1

    # 设计图、对照图、界面截图只给开发者核对版式用，绝不进发布包。
    # 名单是按名字取的，正常取不到它们；这一道是防以后有人把名单改成按目录收。
    leaked = [source for _, source in found if "probes" in str(source).lower()]
    if leaked:
        for source in leaked:
            print(f"开发用的图片混进包里了：{source}", file=sys.stderr)
        return 1

    exe_suffix = ".exe" if "windows" in args.platform.lower() else ""
    executable = {f"texttool{exe_suffix}"} | ({f"texttool-gui{exe_suffix}"} if gui else set())
    zip_path = Path(args.out) / f"texttool-{args.platform}-{args.kind}.zip"
    write_zip(zip_path, found, gui, executable)

    # 校验和跟包放在一起：一行「哈希、两个空格、文件名」，与旧的发布脚本写出来的格式相同。
    # 签名工具另出 .sig；这一份是给下载的人自己核对用的，不需要私钥。
    digest = hashlib.sha256(zip_path.read_bytes()).hexdigest()
    checksum = zip_path.parent / (zip_path.name + ".sha256")
    checksum.write_text(f"{digest}  {zip_path.name}\n", encoding="utf-8")

    size = zip_path.stat().st_size
    print(f"校验和：{checksum}")
    print(f"包：{zip_path}")
    for name, source in found:
        print(f"  {name:<28} {source.stat().st_size:>9} 字节")
    print(f"  使用说明.txt                 {len((GUI_README if gui else CLI_README).encode('utf-8')):>9} 字节")
    print(f"压缩包体积：{size} 字节（{size / (1024 * 1024):.2f} MiB），上限 {SIZE_LIMIT} 字节")
    if size > SIZE_LIMIT:
        print("超过 10MB 上限", file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    sys.exit(main())
