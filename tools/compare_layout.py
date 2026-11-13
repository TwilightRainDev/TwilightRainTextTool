#!/usr/bin/env python3
"""按元素位置比两版界面的同一页截图。

做法：把图转成灰度，逐行数「有墨迹的像素」，连成一条条的横带就是界面上的元素行；
每行再报出墨迹的横向起止，用来看标签列、输入框、按钮有没有对齐。
比的是位置与有无，不比颜色与字体。

用法：
    python3 tools/compare_layout.py <桌面版目录> <原生版目录> [--desktop-crop-top 30]

两个目录里放同名的 page-1.png 到 page-6.png；桌面版截图含窗口标题栏，默认裁掉顶部 30 像素再比。
"""

import argparse
import os
import sys

from PIL import Image

INK_THRESHOLD = 160   # 灰度低于它算墨迹
MIN_BAND_HEIGHT = 3   # 少于三像素的横带当噪点丢掉：页签下划线与状态条分隔线都是这类细线
MATCH_WINDOW = 12     # 纵向差超过这么多像素就不算同一行


def ink_bands(image):
    """返回 [(起 y, 止 y, 最左 x, 最右 x)]，每条是一次元素行。"""
    gray = image.convert("L")
    width, height = gray.size
    pixels = gray.load()

    rows = []
    for y in range(height):
        left, right, count = None, None, 0
        for x in range(width):
            if pixels[x, y] < INK_THRESHOLD:
                count += 1
                if left is None:
                    left = x
                right = x
        rows.append((count, left, right))

    bands = []
    start = None
    for y, (count, left, right) in enumerate(rows):
        if count > 0 and start is None:
            start = y
        elif count == 0 and start is not None:
            bands.append((start, y - 1))
            start = None
    if start is not None:
        bands.append((start, height - 1))

    result = []
    for top, bottom in bands:
        if bottom - top + 1 < MIN_BAND_HEIGHT:
            continue
        lefts = [rows[y][1] for y in range(top, bottom + 1) if rows[y][1] is not None]
        rights = [rows[y][2] for y in range(top, bottom + 1) if rows[y][2] is not None]
        result.append((top, bottom, min(lefts), max(rights)))
    return result


def main():
    parser = argparse.ArgumentParser(description="按元素位置比两版界面的截图")
    parser.add_argument("desktop_dir", help="桌面版截图所在目录")
    parser.add_argument("native_dir", help="原生版截图所在目录")
    parser.add_argument("--desktop-crop-top", type=int, default=30, help="桌面版截图顶部要裁掉多少像素")
    parser.add_argument("--desktop-inset", type=int, default=6,
                        help="桌面版截图四边要各去掉多少像素：那一圈是窗口边框，会被当成墨迹")
    parser.add_argument("--pages", type=int, default=6, help="比几页")
    args = parser.parse_args()

    exit_code = 0
    for page in range(1, args.pages + 1):
        name = f"page-{page}.png"
        desktop_path = os.path.join(args.desktop_dir, name)
        native_path = os.path.join(args.native_dir, name)
        if not (os.path.exists(desktop_path) and os.path.exists(native_path)):
            print(f"第 {page} 页：缺图（{desktop_path} 或 {native_path}）")
            exit_code = 1
            continue

        desktop_img = Image.open(desktop_path)
        if args.desktop_crop_top or args.desktop_inset:
            inset = args.desktop_inset
            width, height = desktop_img.size
            desktop_img = desktop_img.crop(
                (inset, args.desktop_crop_top, width - inset, height - inset))
        native_img = Image.open(native_path)

        desktop_bands = ink_bands(desktop_img)
        native_bands = ink_bands(native_img)

        print(f"===== 第 {page} 页 =====")
        print(f"桌面版 {desktop_img.size[0]}x{desktop_img.size[1]}，{len(desktop_bands)} 条元素行")
        print(f"原生版 {native_img.size[0]}x{native_img.size[1]}，{len(native_bands)} 条元素行")

        # 按纵向位置就近配对，不按序号：一侧多出一行时，后面的行不会整串错位。
        matched_native = set()
        unmatched_desktop = []
        for desk in desktop_bands:
            best = None
            best_distance = None
            for index, nat in enumerate(native_bands):
                if index in matched_native:
                    continue
                distance = abs(nat[0] - desk[0])
                if best_distance is None or distance < best_distance:
                    best_distance, best = distance, index
            if best is None or best_distance > MATCH_WINDOW:
                unmatched_desktop.append(desk)
                continue
            matched_native.add(best)
            nat = native_bands[best]
            dy = nat[0] - desk[0]
            dx = nat[2] - desk[2]
            flag = "" if (abs(dy) <= 2 and abs(dx) <= 4) else "  <-- 位置不同"
            if flag:
                exit_code = 1
            print(f"  配对：桌面版 y {desk[0]}-{desk[1]} x {desk[2]}-{desk[3]}"
                  f"   原生版 y {nat[0]}-{nat[1]} x {nat[2]}-{nat[3]}"
                  f"   差 dy={dy:+d} dx={dx:+d}{flag}")

        for desk in unmatched_desktop:
            exit_code = 1
            print(f"  桌面版多一行：y {desk[0]}-{desk[1]} x {desk[2]}-{desk[3]}，原生版这里没有")
        for index, nat in enumerate(native_bands):
            if index not in matched_native:
                exit_code = 1
                print(f"  原生版多一行：y {nat[0]}-{nat[1]} x {nat[2]}-{nat[3]}，桌面版这里没有")
        print()

    return exit_code


if __name__ == "__main__":
    sys.exit(main())
