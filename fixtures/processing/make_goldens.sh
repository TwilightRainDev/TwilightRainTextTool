#!/usr/bin/env bash
# 跑参考实现，重新生成本目录下的 <用例名>.expected.txt。跑法：
#   bash fixtures/processing/make_goldens.sh
#   REFERENCE_CLI=/别的路径/texttool.dll bash fixtures/processing/make_goldens.sh
#
# 参考实现要先生成：cd Config/Archive/TextTool && dotnet build TextTool.sln -c Release
#
# 用例表是 cases.tsv，按那里的第 2~5 列拼出参考实现的命令行。
# 参考实现写出来的文件带 UTF-8 BOM，这里去掉 BOM 再存：
# 引擎接口收发的正文本来就不含 BOM。
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
dll="${REFERENCE_CLI:-$here/../../../../Config/Archive/TextTool/TextTool.Cli/bin/Release/net8.0/texttool.dll}"

if [ ! -f "$dll" ]; then
  echo "找不到参考实现：$dll（先 dotnet build，或用 REFERENCE_CLI 指过去）" >&2
  exit 1
fi

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

while IFS=$'\t' read -r name input max_para mode scheme _rest; do
  case "$name" in ''|'#'*) continue;; esac

  rm -f "$work"/*
  cp "$here/inputs/$input" "$work/$input"

  argv=()
  [ "$max_para" != "-" ] && argv+=(--max-para "$max_para")
  [ "$mode" = "reformat" ] && argv+=(--reformat-only)
  [ "$mode" = "punct" ] && argv+=(--punct-only)
  [ "$scheme" != "-" ] && argv+=(--scheme "$scheme")

  ( cd "$work" && dotnet "$dll" vn "$input" "${argv[@]}" > /dev/null )

  # 参考实现把结果写在源文件旁边，名字是 <主名>_Processed.<扩展名>。
  out="$work/$(basename "${input%.*}")_Processed.${input##*.}"
  if [ ! -f "$out" ]; then
    echo "参考实现没产出结果：$name" >&2
    exit 1
  fi

  if [ "$(head -c 3 "$out" | od -An -tx1 | tr -d ' \n')" = "efbbbf" ]; then
    tail -c +4 "$out" > "$here/$name.expected.txt"
  else
    cp "$out" "$here/$name.expected.txt"
  fi
  echo "[基准] $name"
done < "$here/cases.tsv"
