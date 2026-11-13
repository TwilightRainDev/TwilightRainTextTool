#!/usr/bin/env bash
# 多步链与 fixtures/processing/chain 下基准的逐字节对照。跑法：
#   bash tests/compare_chain.sh          # 比全部用例
#   TEXTTOOL_BIN=build-linux/texttool bash tests/compare_chain.sh
#   CHAIN_RUNNER=build-linux/chain_runner bash tests/compare_chain.sh
#
# 每条用例在一份干净的临时目录里跑一条链：稿件复制成 in.txt，产物叫 out.txt。
# 比的是 out.txt 与 expected/<用例名>.expected.txt 逐字节一致。表里第 4 列写了 lint 的用例
# 另比两样：产物再跑一遍 lint --json 的标准输出逐字节，以及它的退出码。
#
# 链还没挂到命令行与界面上，跑它要 build-linux/chain_runner（只给这份脚本用）。
# 基准由 fixtures/processing/chain/make_goldens.sh 跑命令行生成，不要手改。
# 参考实现只有单步、跑不了链，这几个基准是原生命令行按同一次序跑两步得到的。
#
# 换标点那一步的规则由 TEXTTOOL_REPLACE_RULES_JSON 指到本目录的 replace_rules.json：
# 基准与链读的是同一份，换出来的标点才谈得上逐个字节比。
set -uo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
fixtures="$root/fixtures/processing/chain"
bin="${TEXTTOOL_BIN:-$root/build-linux/texttool}"
runner="${CHAIN_RUNNER:-$root/build-linux/chain_runner}"

if [ ! -x "$bin" ]; then
  echo "找不到命令行程序：$bin（先 cmake --build build-linux，或用 TEXTTOOL_BIN 指过去）" >&2
  exit 1
fi
if [ ! -x "$runner" ]; then
  echo "找不到链的驱动：$runner（先 cmake --build build-linux，或用 CHAIN_RUNNER 指过去）" >&2
  exit 1
fi

export TEXTTOOL_REPLACE_RULES_JSON="$fixtures/replace_rules.json"

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

failed=0
checked=0

# 一份产物对一次：$1 标签 $2 实际文件 $3 期望文件
same() {
  checked=$((checked + 1))
  if cmp -s "$2" "$3"; then
    return 0
  fi
  failed=$((failed + 1))
  echo "  对不上：$1"
  diff -u "$3" "$2" | head -40
  return 1
}

# 一条用例：$1 用例名 $2 稿件 $3 步骤（逗号隔开）$4 检查（lint 或 -）
run_case() {
  local name="$1" input="$2" steps="$3" check="$4"
  local dir="$work/$name"
  mkdir -p "$dir"
  cp "$fixtures/inputs/$input" "$dir/in.txt"

  local -a step_names
  IFS=',' read -r -a step_names <<< "$steps"

  local ok=1
  local code
  ( cd "$dir" && "$runner" in.txt out.txt "${step_names[@]}" > .stdout 2> .stderr )
  code=$?
  if [ "$code" -ne 0 ]; then
    failed=$((failed + 1))
    checked=$((checked + 1))
    echo "[不一致] $name"
    echo "  跑链退出码 $code"
    sed 's/^/  /' "$dir/.stderr"
    return 0
  fi

  same "产物" "$dir/out.txt" "$fixtures/expected/$name.expected.txt" || ok=0

  # 检查不进链：链跑完拿到 out.txt，再拿这个文件名跑一遍 lint。基准里的 File 字段就是 out.txt，
  # 所以两边都要在跑目录里用同一个名字、从同一个位置跑。
  if [ "$check" = "lint" ]; then
    ( cd "$dir" && "$bin" lint out.txt --json > lint.json 2> lint.err )
    code=$?
    local want_code
    want_code="$(sed -n '1p' "$fixtures/expected/$name.expected.exit")"
    checked=$((checked + 1))
    if [ "$code" != "$want_code" ]; then
      failed=$((failed + 1))
      ok=0
      echo "  lint 退出码 $code，基准 $want_code"
    fi
    same "lint --json" "$dir/lint.json" "$fixtures/expected/$name.expected.json" || ok=0
  fi

  if [ "$ok" = 1 ]; then echo "[一致] $name"; else echo "[不一致] $name"; fi
}

echo "命令行：  $bin"
echo "链的驱动：$runner"
echo "替换规则：$TEXTTOOL_REPLACE_RULES_JSON"

while IFS=$'\t' read -r name input steps check; do
  case "$name" in ''|'#'*) continue ;; esac
  [ -n "${check:-}" ] || check=-
  run_case "$name" "$input" "$steps" "$check"
done < "$fixtures/cases.tsv"

if [ "$failed" -ne 0 ]; then
  echo "不一致 $failed 处（比了 $checked 份产物）" >&2
  exit 1
fi
echo "全部一致：比了 $checked 份产物"
