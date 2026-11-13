#!/usr/bin/env python3
"""启动一个程序，每 20 毫秒采样一次内存，报峰值。

这是客户验收那条口径的实现：15MB 稿件、峰值不超过 40MB。
口径是整进程峰值（Linux 读 /proc/<pid>/status 的 VmRSS 与 RssAnon、RssFile 分项），
不剔除共享库与字体文件的映射页；把这几项一起报出来，是为了峰值超标时能看出是程序自己吃掉的还是映射进来的。

报出来的峰值取两样里大的：20 毫秒采样到的最高 VmRSS，与内核自己记的最高水位 VmHWM。
短于 20 毫秒的尖峰采样会漏（界面解一份 GBK 稿就属于这种），VmHWM 漏不了。

用法：
    python3 tools/measure_peak.py ./build-linux/texttool lint 稿件.txt
    python3 tools/measure_peak.py --limit 40 ./build-linux/texttool lint 稿件.txt
    python3 tools/measure_peak.py --seconds 8 --limit 40 ./build-linux/texttool-gui
    python3 tools/measure_peak.py --limit 40 \
        --env TEXTTOOL_SELF_CHECK=/tmp/稿件.txt ./build-linux/texttool-gui

--env 只给这一个子进程加变量（可以给多次），父进程自己的环境不动。
界面要人点按钮才干活，量它的内存就靠 TEXTTOOL_SELF_CHECK 让它自己跑一份稿再退出。

退出码：程序本身跑完返回 0；给了 --limit 而峰值超线返回 2；程序自身非零退出就原样传出。
"""

import argparse
import os
import subprocess
import sys
import time

SAMPLING_INTERVAL = 0.02  # 秒，客户口径写的是 20 毫秒


def sample(pid):
    """读一次进程内存，返回 {字段: KB}。进程已退出时返回空。"""
    values = {}
    try:
        with open(f"/proc/{pid}/status", encoding="utf-8") as status:
            for line in status:
                key, _, rest = line.partition(":")
                if key in ("VmRSS", "VmHWM", "RssAnon", "RssFile", "RssShmem"):
                    values[key] = int(rest.split()[0])
    except (FileNotFoundError, ProcessLookupError):
        return {}
    return values


def run(command, limit_mb, seconds, extra_env):
    started = time.time()
    env = None
    if extra_env:
        env = dict(os.environ)
        env.update(extra_env)
    try:
        proc = subprocess.Popen(command, env=env)
    except FileNotFoundError:
        print(f"找不到程序：{command[0]}", file=sys.stderr)
        return 1

    peak = {}
    count = 0
    while proc.poll() is None:
        # 界面程序不会自己退出，给 --seconds 就到点收工；命令行程序跑完自然结束，不用它。
        if seconds is not None and time.time() - started >= seconds:
            proc.terminate()
            try:
                proc.wait(timeout=5)
            except subprocess.TimeoutExpired:
                proc.kill()
        current = sample(proc.pid)
        if current:
            count += 1
            for key, value in current.items():
                peak[key] = max(peak.get(key, 0), value)
        time.sleep(SAMPLING_INTERVAL)
    exit_code = proc.returncode

    elapsed = time.time() - started
    sampled_mb = peak.get("VmRSS", 0) / 1024
    hwm_mb = peak.get("VmHWM", 0) / 1024
    peak_rss_mb = max(sampled_mb, hwm_mb)  # VmHWM 只可能不小于采到的值
    print(f"命令：{' '.join(command)}")
    print(f"退出码：{exit_code}    运行时长：{elapsed:.2f}s    采样点数：{count}")
    if peak:
        print(f"峰值 RSS  ：{peak_rss_mb:6.2f} MB"
              f"（进程自记最高 {hwm_mb:.2f}，20 毫秒采样最高 {sampled_mb:.2f}）")
        print(f"  程序自申请：{peak.get('RssAnon', 0) / 1024:6.2f} MB（采样最高）")
        print(f"  映射的文件页：{peak.get('RssFile', 0) / 1024:6.2f} MB（采样最高）")
        print(f"  共享内存页：{peak.get('RssShmem', 0) / 1024:6.2f} MB（采样最高）")
    else:
        print("采样期间进程已经结束，没有取到内存数据")

    if limit_mb is not None and peak:
        if peak_rss_mb > limit_mb:
            print(f"超过门槛：{peak_rss_mb:.2f} MB > {limit_mb} MB")
            return 2
        print(f"门槛内：{peak_rss_mb:.2f} MB <= {limit_mb} MB")
    return exit_code


def main():
    parser = argparse.ArgumentParser(description="采样程序的峰值内存")
    parser.add_argument("--limit", type=float, default=None, help="峰值上限，单位 MB；超过时退出码为 2")
    parser.add_argument("--seconds", type=float, default=None,
                        help="跑多少秒后结束程序，界面这类不会自己退出的程序要用它")
    parser.add_argument("--env", action="append", default=[], metavar="名字=值",
                        help="给子进程加一个环境变量，可以给多次；父进程自己的环境不变")
    parser.add_argument("command", nargs=argparse.REMAINDER, help="要启动的程序与参数")
    args = parser.parse_args()
    if not args.command:
        parser.print_help()
        return 1

    extra_env = {}
    for item in args.env:
        name, _, value = item.partition("=")
        if not name:
            print(f"--env 要写成 名字=值，认不出：{item}", file=sys.stderr)
            return 1
        extra_env[name] = value
    return run(args.command, args.limit, args.seconds, extra_env)


if __name__ == "__main__":
    sys.exit(main())
