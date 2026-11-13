#!/usr/bin/env bash
# 重出命令行的逐字节基准。稿件改了、用例改了、参考实现动了，都重跑这一遍，
# 不要手改 .expected.*——那等于把基准改成自己的输出。
#
# 两个参考构建各管一半，与人读报告层的基准同一套口径：
#   * 人读报告、帮助文本、stderr、plain 用例的退出码用 4.0.0-draft 的构建（用词以 4.0 为准）；
#   * --json 输出与 lint、json 两类用例的第二次退出码用 3.1（提交 207dd38）的构建，JSON 契约不动。
# 4.0 的构建不进参考仓库的工作区，用 git archive 导到临时目录再编：
#   ref=/home/twilightrain/WorkZone/Config/Archive/TextTool
#   mkdir -p /tmp/tt40 && git -C "$ref" archive v4.0.0-draft | tar -x -C /tmp/tt40
#   dotnet build /tmp/tt40/TextTool.Cli/TextTool.Cli.csproj -c Release
# 路径不一样时用 TEXTTOOL_REF_4_0_DLL 指过去。
#
# 用例表在 cases.tsv，每个用例出这几份：
#   <用例>.expected.txt   人读报告（或帮助文本、stdout）
#   <用例>.expected.err   stderr（lint 用例取第一次运行的）
#   <用例>.expected.exit  退出码，lint 用例两行、plain 用例一行
#   <用例>.expected.json  --json 的输出，只有 lint 与 json 两类用例有
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

# 目录用例里有一个读不了的子目录。权限不进仓库，这里临时拿掉再改回去，换台机器重出基也一样。
lock_unreadable() { chmod 000 lint_dir/secret 2>/dev/null || true; }
unlock_unreadable() { chmod 755 lint_dir/secret 2>/dev/null || true; }
lock_unreadable
trap 'unlock_unreadable' EXIT

# merge_help 这一份不用参考实现生成：原生版多了三个参考实现没有的开关
# （--no-merge、--no-merge-chars、--apply-replace），帮助文本因此与参考实现不同。
# 那一份由原生程序自己出，改了帮助就跟着重出：
#   build-linux/texttool merge --help > merge_help.expected.txt
while IFS=$'\t' read -r name kind args; do
  case "$name" in ''|'#'*|'merge_help') continue ;; esac
  [ -n "${kind:-}" ] || continue

  eval "argv=(${args:-})"

  set +e
  dotnet "$dll40" "${argv[@]}" > "$name.expected.txt" 2> "$name.expected.err"
  text_code=$?
  set -e

  if [ "$kind" = "lint" ] || [ "$kind" = "json" ] || [ "$kind" = "log" ]; then
    set +e
    # --json 那一次的 stderr 不进基准：比对只认第一次运行的 stderr（见 cases.tsv 的说明）。
    dotnet "$dll31" "${argv[@]}" --json > "$name.expected.json" 2> /dev/null
    json_code=$?
    set -e
    if [ "$kind" = "log" ]; then
      # 命中日志：同样参数再加 --log。日志文件名固定成「用例名.jsonl」，
      # 与对照脚本用的是同一个名字，stderr 那一行也比得了。
      rm -f "$name.jsonl"
      set +e
      dotnet "$dll40" "${argv[@]}" --log "$name.jsonl" > /dev/null 2> "$name.expected.log.err"
      log_code=$?
      set -e
      mv "$name.jsonl" "$name.expected.jsonl"
      printf '%s\n%s\n%s\n' "$text_code" "$json_code" "$log_code" > "$name.expected.exit"
    else
      printf '%s\n%s\n' "$text_code" "$json_code" > "$name.expected.exit"
    fi
  else
    printf '%s\n' "$text_code" > "$name.expected.exit"
    rm -f "$name.expected.json"
  fi
  echo "$name -> $text_code${json_code:+ $json_code}${log_code:+ $log_code}"
  json_code=""
  log_code=""
done < cases.tsv
