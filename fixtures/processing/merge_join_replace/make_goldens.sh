#!/usr/bin/env bash
# 跑参考实现，重新生成 expected/<用例名>.txt。跑法：
#   bash fixtures/processing/merge_join_replace/make_goldens.sh
#
# 参考实现要先生成：cd Config/Archive/TextTool && dotnet build TextTool.sln -c Release
# 这里调的是 refharness（直接连 TextTool.Core），不走命令行：
# no_merge 与 apply_replace 只在界面上有，命令行接不到，得在库这一层跑。
# 生成器写的是 UTF-8 无 BOM，和引擎接口收发的正文一致，所以没有去 BOM 这一步。
#
# 生成器编出来的 bin/ 与 obj/ 跑完就删，源码树里只留 expected/。
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
trap 'rm -rf "$here/refharness/bin" "$here/refharness/obj"' EXIT

dotnet run --project "$here/refharness/refharness.csproj" -c Release -- "$here"
