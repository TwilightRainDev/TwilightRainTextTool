#!/usr/bin/env bash
# 重出逐字节基准。加了新样本或参考实现动了之后跑这一遍，
# 不要手改 .expected.*——那等于把基准改成自己的输出。
#
# 两个参考实现各管一半：
#   * 人读报告（<用例>.expected.txt）用 4.0.0-draft 的构建出，用词以 4.0 为准；
#   * --json 输出与两次运行的退出码用 3.1（提交 207dd38）的构建出，JSON 契约不动。
# 两份构建在同一批稿件上给出的命中、全文长度、退出码逐字节相同，所以人读报告与 JSON
# 说的是同一批数据；改样本后重跑本脚本，两边会一起重出。
#
# 4.0 的构建不进参考实现的仓库工作区：用 git archive 导到临时目录再编，
# 参考仓库不会被改动。默认路径是 /tmp/tt40，换机器或清理过临时目录就重新造一遍：
#   ref=/home/twilightrain/WorkZone/Config/Archive/TextTool
#   mkdir -p /tmp/tt40 && git -C "$ref" archive v4.0.0-draft | tar -x -C /tmp/tt40
#   dotnet build /tmp/tt40/TextTool.Cli/TextTool.Cli.csproj -c Release
# 路径不一样时用 TEXTTOOL_REF_4_0_DLL 指过去。
#
# 每个用例出三份：<用例>.expected.txt（人读）、<用例>.expected.json（--json）、
# <用例>.expected.exit（两行，分别是两次运行的退出码）。
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ref="${TEXTTOOL_REF_REPO:-/home/twilightrain/WorkZone/Config/Archive/TextTool}"
dll31="$ref/TextTool.Cli/bin/Release/net8.0/texttool.dll"
dll40="${TEXTTOOL_REF_4_0_DLL:-/tmp/tt40/TextTool.Cli/bin/Release/net8.0/texttool.dll}"

if [ ! -f "$dll31" ]; then
  echo "先编 3.1 参考实现：cd $ref && dotnet build TextTool.Cli/TextTool.Cli.csproj -c Release" >&2
  exit 1
fi
if [ ! -f "$dll40" ]; then
  echo "先按本文件开头的三行命令造 4.0 构建：$dll40 不存在" >&2
  exit 1
fi

# 稿件名一律用不带目录的文件名：报告里 File 字段就是它，带绝对路径的话基准换台机器就失效。
cd "$here"
: > make_goldens.stderr

# 用例名与稿件名相同；baseline 另走下面那一条。
cases="no_hits mixed mixed_severity escaping exotic_codepoints crlf collapse real_doc_agents real_doc_architecture"

run() {
  local case="$1"
  shift

  set +e
  dotnet "$dll40" lint "$@" > "$case.expected.txt" 2>> make_goldens.stderr
  local text_code=$?
  set -e

  set +e
  dotnet "$dll31" lint "$@" --json > "$case.expected.json" 2>> make_goldens.stderr
  local json_code=$?
  set -e

  printf '%s\n%s\n' "$text_code" "$json_code" > "$case.expected.exit"
  echo "$case -> exit $text_code $json_code"
}

for case in $cases; do
  src="$case.txt"
  [ -f "$src" ] || src="$case.md"
  run "$case" "$src"
done

# 带基线：报告只列新增位置，开头多一行对照，JSON 多出 BaselineHits 字段。
run baseline baseline_after.txt --baseline baseline_before.txt
