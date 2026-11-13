#!/usr/bin/env bash
# 跑命令行重新生成本目录 expected/ 下的基准。跑法：
#   bash fixtures/processing/chain/make_goldens.sh
#   TEXTTOOL_BIN=/别的路径/texttool bash fixtures/processing/chain/make_goldens.sh
#
# 一条用例按 cases.tsv 第 3 列的次序把命令行子命令串起来跑，与人工敲的命令一样：
# 一步一个子命令，产物换个名字交给下一步，最后一步的产物就是基准。每一步都用的默认参数。
# 换标点那一步的规则从本目录的 replace_rules.json 读（TEXTTOOL_REPLACE_RULES_JSON 指过去），
# 对照脚本跑链时读的也是这一份，两边换的是同一批标点。
#
# 参考实现没有多步链，只有单步：这份基准是原生命令行按顺序跑两步得到的，不来自第二份实现。
# 单步本身对不对由 tests/compare_processing.sh 对参考实现守（同一个 merge、replace、vn）。
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
bin="${TEXTTOOL_BIN:-$here/../../../build-linux/texttool}"

if [ ! -x "$bin" ]; then
  echo "找不到命令行程序：$bin（先 cmake --build build-linux，或用 TEXTTOOL_BIN 指过去）" >&2
  exit 1
fi

# 命令行写盘时把产物写成 <主名>_Processed.<扩展名>，摆到下一步要用的名字上。
processed_name() {
  local name dir stem ext
  dir="$(dirname "$1")"
  name="$(basename "$1")"
  if [ "${name##*.}" = "$name" ]; then
    printf '%s/%s_Processed' "$dir" "$name"
  else
    stem="${name%.*}"
    ext="${name##*.}"
    printf '%s/%s_Processed.%s' "$dir" "$stem" "$ext"
  fi
}

export TEXTTOOL_REPLACE_RULES_JSON="$here/replace_rules.json"

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

mkdir -p "$here/expected"

while IFS=$'\t' read -r name input steps check; do
  case "$name" in ''|'#'*) continue ;; esac
  [ -n "${check:-}" ] || check=-

  dir="$work/$name"
  mkdir -p "$dir"
  cp "$here/inputs/$input" "$dir/in.txt"

  (
    cd "$dir"
    current=in.txt
    index=0
    IFS=',' read -r -a step_names <<< "$steps"
    for step in "${step_names[@]}"; do
      index=$((index + 1))
      case "$step" in
        merge)   "$bin" merge "$current" > /dev/null ;;
        replace) "$bin" replace "$current" > /dev/null ;;
        vn)      "$bin" vn "$current" > /dev/null ;;
        *) echo "认不出的步骤：$step" >&2; exit 1 ;;
      esac
      mv "$(processed_name "$current")" "s$index.txt"
      current="s$index.txt"
    done
    mv "$current" out.txt
    cp out.txt "$here/expected/$name.expected.txt"

    # 检查不进链：产物先落到 out.txt，再拿这个文件名跑一遍 lint，基准里的 File 字段就是它。
    if [ "$check" = "lint" ]; then
      set +e
      "$bin" lint out.txt --json > "$here/expected/$name.expected.json"
      code=$?
      set -e
      # 0 是没有命中，1 是有命中。这两个都算跑通了；2 是用法或读取错误，说明基准不该这么出。
      if [ "$code" -ne 0 ] && [ "$code" -ne 1 ]; then
        echo "lint 跑不通（退出码 $code）：$name" >&2
        exit 1
      fi
      echo "$code" > "$here/expected/$name.expected.exit"
    fi
  )

  echo "[基准] $name"
done < "$here/cases.tsv"
