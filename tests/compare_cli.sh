#!/usr/bin/env bash
# 命令行与 fixtures/cli 下期望输出的逐字节对照。跑法：
#   bash tests/compare_cli.sh          # 比全部测试，红在哪开哪
#   TEXTTOOL_BIN=build-linux/texttool bash tests/compare_cli.sh
#
# 比三样：stdout、stderr、退出码。lint 与 json 两类测试跑两次：先跑人读输出，再加 --json 跑一次；
# log 类测试跑三次：人读、--json、再加 --log 跑一次并比日志文件；plain 测试只跑一次。
# 期望输出由 fixtures/cli/make_goldens.sh 生成，不要手改。
#
# 一有对不上就退 1，并打印第一处分歧；全对上退 0，并打印每个测试的结果。
set -uo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cli="$root/fixtures/cli"
bin="${TEXTTOOL_BIN:-$root/build-linux/texttool}"

if [ ! -x "$bin" ]; then
  echo "找不到命令行程序：$bin（先 cmake --build build-linux，或用 TEXTTOOL_BIN 指过去）" >&2
  exit 1
fi

# 稿件名与报告里的 File 字段都按相对路径，脚本必须在 fixtures/cli 里跑。
cd "$cli"

# 目录测试里有一个读不了的子目录。权限不进仓库，两个脚本自己设，换台机器重跑也一致；
# 跑完改回去，免得下一次手工看样本时打不开。
lock_unreadable() { chmod 000 lint_dir/secret 2>/dev/null || true; }
unlock_unreadable() { chmod 755 lint_dir/secret 2>/dev/null || true; }
lock_unreadable

work="$(mktemp -d)"
trap 'rm -rf "$work"; unlock_unreadable' EXIT

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

# 一个测试：$1 用例名 $2 类型 $3 参数（shell 写法）
run_case() {
  local name="$1" kind="$2" args="$3"
  local -a argv
  eval "argv=($args)"

  local ok=1
  set +e
  "$bin" "${argv[@]}" > "$work/out" 2> "$work/err"
  local code=$?
  set -e

  local want_code
  want_code="$(sed -n '1p' "$name.expected.exit")"
  if [ "$code" != "$want_code" ]; then
    failed=$((failed + 1)); ok=0
    echo "  退出码 $code，基准 $want_code"
  fi
  same "stdout" "$work/out" "$name.expected.txt" || ok=0
  same "stderr" "$work/err" "$name.expected.err" || ok=0

  if [ "$kind" = "lint" ] || [ "$kind" = "json" ] || [ "$kind" = "log" ]; then
    set +e
    "$bin" "${argv[@]}" --json > "$work/json" 2> /dev/null
    code=$?
    set -e

    want_code="$(sed -n '2p' "$name.expected.exit")"
    if [ "$code" != "$want_code" ]; then
      failed=$((failed + 1)); ok=0
      echo "  退出码（--json）$code，基准 $want_code"
    fi
    same "stdout（--json）" "$work/json" "$name.expected.json" || ok=0
  fi

  # 命中日志：同样参数再加 --log，日志文件与期望输出逐字节比。
  # 文件名固定成「用例名.jsonl」：两边用的是同一个名字，stderr 那一行也比得了。
  if [ "$kind" = "log" ]; then
    rm -f "$name.jsonl"
    set +e
    "$bin" "${argv[@]}" --log "$name.jsonl" > "$work/log_out" 2> "$work/log_err"
    code=$?
    set -e

    want_code="$(sed -n '3p' "$name.expected.exit")"
    if [ "$code" != "$want_code" ]; then
      failed=$((failed + 1)); ok=0
      echo "  退出码（--log）$code，基准 $want_code"
    fi
    same "stdout（--log）" "$work/log_out" "$name.expected.txt" || ok=0
    same "stderr（--log）" "$work/log_err" "$name.expected.log.err" || ok=0
    same "命中日志" "$name.jsonl" "$name.expected.jsonl" || ok=0
    rm -f "$name.jsonl"
  fi

  if [ "$ok" = 1 ]; then echo "[一致] $name"; else echo "[不一致] $name"; fi
}

echo "命令行：$bin"
while IFS=$'\t' read -r name kind args; do
  case "$name" in ''|'#'*) continue ;; esac
  [ -n "${kind:-}" ] || continue
  run_case "$name" "$kind" "${args:-}"
done < cases.tsv

if [ "$failed" -ne 0 ]; then
  echo "不一致 $failed 处（比了 $checked 份产物）" >&2
  exit 1
fi
echo "全部一致：比了 $checked 份产物"
