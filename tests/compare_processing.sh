#!/usr/bin/env bash
# merge、replace、join、vn 四个子命令与另一边的 .NET 命令行的逐字节对照。跑法：
#   bash tests/compare_processing.sh          # 比全部测试
#   TEXTTOOL_BIN=build-linux/texttool bash tests/compare_processing.sh
#   TEXTTOOL_REF_DLL=/path/to/texttool.dll bash tests/compare_processing.sh
#
# 一条测试在两个干净的目录里各跑一次：一边是 .NET 命令行（dotnet 跑脚本里 ref 指的那份
# texttool.dll，可用 TEXTTOOL_REF_DLL 指过去），一边是原生命令行。跑完比四样：
# stdout、stderr、退出码，以及跑目录里每个文件的字节。输出文件叫什么、.bak 怎么轮换、
# --overwrite 之后原文件长什么样，都在这份比对里。
# 两边用的稿件是同一份，所以目录里任何不同都是真的不同。
#
# 测试写在 fixtures/processing/cli/cases.tsv：strict 项必须一致，known 项是
# 已知不一致，只打印不判红（原因也写在表里）。一有 strict 项对不上就退 1。
#
# 替换规则方案两边必须读同一份文件：.NET 那边只认程序目录下的 replace_schemes.json，
# 原生用 TEXTTOOL_SCHEMES_JSON 指过去，于是两边看到的是同一串方案名与规则。
# 角色方案（vn 的 --scheme）同理：.NET 那边读程序目录下的 vn_schemes.json，没有就用编进
# 程序里的默认方案，原生用 TEXTTOOL_VN_SCHEMES_JSON 指到同一份默认方案文件。
#
# 第 2 列里以 + 开头的项是「在跑目录里建这个子目录」，用来给 join 摆一个干净的目录：
# 跑目录根部有脚本自己写的 .stdout/.stderr/.exit，通配符宽的 --pattern 会把它们也收进去。
# 以 link: 开头的项是建一条软链接，写法 link:目标>名字（目标按跑目录里的相对路径算）。
set -uo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
fixtures="$root/fixtures/processing/cli"
bin="${TEXTTOOL_BIN:-$root/build-linux/texttool}"
ref="${TEXTTOOL_REF_DLL:-$root/../../Config/Archive/TextTool/TextTool.Cli/bin/Release/net8.0/texttool.dll}"
ref_bin_dir="$(dirname "$ref")"

if [ ! -x "$bin" ]; then
  echo "找不到命令行程序：$bin（先 cmake --build build-linux，或用 TEXTTOOL_BIN 指过去）" >&2
  exit 1
fi
if [ ! -f "$ref" ]; then
  echo "找不到参考实现：$ref（先在 Config/Archive/TextTool 里跑 dotnet build TextTool.sln -c Release，或用 TEXTTOOL_REF_DLL 指过去）" >&2
  exit 1
fi
if ! command -v dotnet > /dev/null 2>&1; then
  echo "找不到 dotnet，跑不了参考实现" >&2
  exit 1
fi

# 方案文件优先用 .NET 那边程序目录里的 replace_schemes.json（它读的就是这一份）；那份不在时
# 退回 TextTool.Core 里的默认方案文件，两边仍然一致。
schemes="$ref_bin_dir/replace_schemes.json"
if [ ! -f "$schemes" ]; then
  schemes="$root/../../Config/Archive/TextTool/TextTool.Core/default_schemes.json"
fi
if [ ! -f "$schemes" ]; then
  echo "找不到替换方案文件：$schemes" >&2
  exit 1
fi

# 角色方案：.NET 那边程序目录里有就以那份为准（它读的就是这一份），
# 没有就退回它编进程序里的默认方案，对应 TextTool.Core/default_vn_schemes.json。
vn_schemes="$ref_bin_dir/vn_schemes.json"
if [ ! -f "$vn_schemes" ]; then
  vn_schemes="$root/../../Config/Archive/TextTool/TextTool.Core/default_vn_schemes.json"
fi
if [ ! -f "$vn_schemes" ]; then
  echo "找不到角色方案文件：$vn_schemes" >&2
  exit 1
fi

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

run_ref() { ( cd "$1" && shift && dotnet "$ref" "$@" ); }
run_native() { ( cd "$1" && shift && "$bin" "$@" ); }

failed=0
known=0
checked=0

# 一条测试：$1 用例名 $2 稿件列表 $3 参数 $4 strict|known $5 已知不一致的原因
run_case() {
  local name="$1" files="$2" args="$3" mode="$4" note="$5"
  local -a argv
  eval "argv=($args)"

  local side dir
  for side in ref native; do
    dir="$work/$name/$side"
    mkdir -p "$dir"
    local item src dst
    if [ "$files" != "-" ]; then
      local IFS=';'
      for item in $files; do
        if [ "${item#+}" != "$item" ]; then
          mkdir -p "$dir/${item#+}"
          continue
        fi
        if [ "${item#link:}" != "$item" ]; then
          local target link_name
          target="${item%%>*}"; target="${target#link:}"
          link_name="${item#*>}"
          ln -s "$target" "$dir/$link_name"
          continue
        fi
        src="${item%%>*}"
        dst="${item#*>}"
        [ "$dst" = "$item" ] && dst="$src"
        mkdir -p "$(dirname "$dir/$dst")"
        cp "$fixtures/inputs/$src" "$dir/$dst" || { echo "稿件缺失：$src" >&2; exit 1; }
      done
      unset IFS
    fi
    if [ "$side" = ref ]; then
      run_ref "$dir" "${argv[@]}" > "$dir/.stdout" 2> "$dir/.stderr"
    else
      run_native "$dir" "${argv[@]}" > "$dir/.stdout" 2> "$dir/.stderr"
    fi
    echo $? > "$dir/.exit"
  done

  checked=$((checked + 1))
  local ok=1 detail=""
  local a="$work/$name/ref" b="$work/$name/native"
  if ! cmp -s "$a/.stdout" "$b/.stdout"; then ok=0; detail+="stdout"$'\n'; fi
  if ! cmp -s "$a/.stderr" "$b/.stderr"; then ok=0; detail+="stderr"$'\n'; fi
  if ! cmp -s "$a/.exit" "$b/.exit"; then ok=0; detail+="退出码 $(( $(cat "$b/.exit") )) vs 参考 $(( $(cat "$a/.exit") ))"$'\n'; fi
  local tree
  # 只差一个 BOM 的文件算有意不同：写盘按用户原编码写回，.NET 那边一律写带 BOM 的 UTF-8。
  tree="$(python3 "$(dirname "$0")/../tools/compare_tree.py" "$a" "$b" 2>&1 | grep '^DIFF' | sed -e "s#$a#参考#g" -e "s#$b#原生#g")"
  if [ -n "$tree" ]; then ok=0; detail+="$tree"$'\n'; fi

  if [ "$ok" = 1 ]; then
    echo "[一致] $name"
    return 0
  fi
  if [ "$mode" = known ]; then
    known=$((known + 1))
    echo "[已知不一致] $name：$note"
    printf '%s' "$detail" | sed 's/^/    /'
    return 0
  fi
  failed=$((failed + 1))
  echo "[不一致] $name"
  printf '%s' "$detail" | sed 's/^/    /'
  echo "    参数：$args"
  diff -u "$a/.stdout" "$b/.stdout" | head -20 | sed 's/^/    /' 2>/dev/null
  return 0
}

echo "原生命令行：$bin"
echo "参考实现：  dotnet $ref"
echo "替换方案：  $schemes"
echo "角色方案：  $vn_schemes"
[ -f "$ref_bin_dir/replace_rules.json" ] && echo "替换规则：  $ref_bin_dir/replace_rules.json（两边都读这一份）"

# 原生这一侧按 .NET 那边的位置读方案与规则：环境变量指过去，别让两边看的文件不同。
export TEXTTOOL_SCHEMES_JSON="$schemes"
export TEXTTOOL_REPLACE_RULES_JSON="$ref_bin_dir/replace_rules.json"
export TEXTTOOL_VN_SCHEMES_JSON="$vn_schemes"

while IFS=$'\t' read -r name files args mode note; do
  case "$name" in ''|'#'*) continue ;; esac
  [ -n "${mode:-}" ] || mode=strict
  run_case "$name" "${files:--}" "${args:-}" "$mode" "${note:--}"
done < "$fixtures/cases.tsv"

if [ "$failed" -ne 0 ]; then
  echo "不一致 $failed 处（比了 $checked 条用例，另有 $known 条已知不一致）" >&2
  exit 1
fi
echo "全部一致：比了 $checked 条用例（其中 $known 条已知不一致，见上方说明）"
