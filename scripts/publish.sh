#!/usr/bin/env bash
# 给既有 release 的四份 zip 签名，把 .sig 传上去，并把升级提示并入 release 正文。
# 产物由 CI 在 tag 上产出，本脚本只下载、签名、上传。
set -uo pipefail

# 下载与上传走 GitHub REST（api.github.com / uploads.github.com），不调用 gh。
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(dirname "$script_dir")"
# shellcheck source=release-assets.sh
. "$script_dir/release-assets.sh"
# shellcheck source=github-release.sh
. "$script_dir/github-release.sh"

version=""
skip_upload=false
force=false
key_path="${TEXTTOOL_SIGN_KEY:-}"
repo="$TT_GITHUB_DEFAULT_REPO"

while [ $# -gt 0 ]; do
    case "$1" in
        -Version|--version) version="${2:-}"; shift 2 ;;
        -SkipUpload|--skip-upload) skip_upload=true; shift ;;
        -Force|--force) force=true; shift ;;
        -KeyPath|--key-path) key_path="${2:-}"; shift 2 ;;
        -Repo|--repo) repo="${2:-}"; shift 2 ;;
        -h|--help) sed -n '2,3p' "${BASH_SOURCE[0]}"; exit 0 ;;
        *) echo "未知参数：$1" >&2; exit 2 ;;
    esac
done

if ! [[ "$version" =~ ^v?[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    echo "版本号格式应为 x.y.z 或 vx.y.z" >&2
    exit 2
fi

raw="${version#v}"
tag="v$raw"

if [ -z "$key_path" ]; then
    echo "未设置 TEXTTOOL_SIGN_KEY，也没有传 -KeyPath" >&2
    exit 1
fi
pub_key="$(dirname "$key_path")/TextTool-signing.pub.pem"

work="$(mktemp -d)"
cleanup() { rm -rf "$work"; }
trap cleanup EXIT

echo "下载 $tag 产物到 $work"
mapfile -t saved < <(tt_gh_save_release_assets "$repo" "$tag" "$raw" "$work")
if [ "${#saved[@]}" -eq 0 ]; then
    echo "未找到 zip" >&2
    exit 1
fi

# 两个 RID 的 GUI 与 CLI 四份产物必须齐活：缺任何一份（或它的 .sha256）都直接退出，
# 不签一半、也不把只签了 win 当成发完了。
mapfile -t present < <(find "$work" -maxdepth 1 -type f -printf '%f\n')
mapfile -t missing < <(texttool_missing_release_assets "$raw" "${present[@]}")
if [ "${#missing[@]}" -gt 0 ]; then
    echo "产物不齐，缺少：$(printf '%s, ' "${missing[@]}" | sed 's/, $//')" >&2
    exit 1
fi

mapfile -t zips < <(texttool_release_asset_names "$raw" | while IFS= read -r name; do
    [ -f "$work/$name" ] && printf '%s\n' "$work/$name"
done)

signer_proj="$repo_root/tools/ReleaseSigner"
echo "构建 ReleaseSigner"
if ! dotnet build "$signer_proj" -c Release > "$work/signer-build.log" 2>&1; then
    echo "ReleaseSigner 构建失败" >&2
    tail -20 "$work/signer-build.log" >&2
    exit 1
fi

signer="$signer_proj/bin/Release/net8.0/ReleaseSigner"
[ -x "$signer" ] || signer="$signer_proj/bin/Release/net8.0/ReleaseSigner.exe"
if [ ! -x "$signer" ]; then
    echo "找不到 ReleaseSigner 可执行文件" >&2
    exit 1
fi

if [ ! -f "$key_path" ]; then
    echo "找不到私钥: $key_path" >&2
    exit 1
fi
if [ ! -f "$pub_key" ]; then
    echo "找不到公钥: $pub_key" >&2
    exit 1
fi

for zip in "${zips[@]}"; do
    echo "签名 $(basename "$zip")"
    if ! "$signer" sign "$zip" -k "$key_path"; then
        echo "签名失败: $(basename "$zip")" >&2
        exit 1
    fi

    echo "verify $(basename "$zip")"
    if ! "$signer" verify "$zip" "$zip.sig" -k "$pub_key"; then
        echo "verify 失败: $(basename "$zip")" >&2
        exit 1
    fi
done

sig_files=()
for zip in "${zips[@]}"; do
    sig_files+=("$zip.sig")
done

if [ "$skip_upload" = true ]; then
    echo "已 -SkipUpload，不调用 GitHub 上传"
    echo "检查清单：zip 存在 / .sha256 存在 / .sig 存在且 verify 通过 / 待执行：release 说明并入升级提示 / 本地真实 update 冒烟"
    exit 0
fi

tt_gh_release_by_tag "$repo" "$tag" || exit 1
release_json="$TT_BODY"
release_id="$(printf '%s' "$release_json" | tt_json 'print(d["id"])')"
mapfile -t remote_names < <(printf '%s' "$release_json" | tt_json '
for asset in d.get("assets") or []:
    print(asset["name"])')

if [ "$force" != true ]; then
    for sig in "${sig_files[@]}"; do
        sig_name="$(basename "$sig")"
        for name in "${remote_names[@]}"; do
            if [ "$name" = "$sig_name" ]; then
                echo ".sig 已在 release（$sig_name），使用 -Force 覆盖" >&2
                exit 1
            fi
        done
    done
fi

echo "上传 .sig 到 $tag"
for sig in "${sig_files[@]}"; do
    sig_name="$(basename "$sig")"
    if [ "$force" = true ]; then
        for name in "${remote_names[@]}"; do
            [ "$name" = "$sig_name" ] && tt_gh_remove_release_asset "$repo" "$release_json" "$sig_name"
        done
    fi
    tt_gh_add_release_asset "$repo" "$release_id" "$sig" || exit 1
done

# 升级提示跟维护文档一起放，不在仓库里：仓库分发出去只有代码，读者要看的那份住在工作区的文档目录。
# 路径从仓库位置推出来，工作区整体搬走也不用改。
notes_path="$(cd "$repo_root/../.." && pwd)/Docs/Project/TextTool/docs/ReleaseNotes.md"
if [ -f "$notes_path" ]; then
    # 幂等判据：并入时写入一段稳定哨兵注释，重复执行只认哨兵——标题行改动或
    # CRLF 归一都不会让它失配；正文里已包含同一段文字时也跳过。
    sentinel="<!-- texttool-upgrade-notes -->"
    current="$(printf '%s' "$release_json" | tt_json 'print(d.get("body") or "")')"
    note="$(sed -e 's/\r$//' "$notes_path" | sed -e '/./,$!d' | sed -e :a -e '/^\n*$/{$d;N;};/\n$/ba')"
    normalized_current="$(printf '%s' "$current" | sed -e 's/\r$//')"

    if printf '%s' "$normalized_current" | grep -qF "$sentinel" ||
        { [ -n "$note" ] && printf '%s' "$normalized_current" | grep -qF "$note"; }; then
        echo "release 说明已含升级提示，跳过"
    else
        if [ -z "${normalized_current//[[:space:]]/}" ]; then
            merged="$sentinel"$'\n\n'"$note"
        else
            merged="$sentinel"$'\n\n'"$note"$'\n\n---\n\n'"$current"
        fi
        python3 -c 'import json,sys
print(json.dumps({"body": sys.argv[1]}, ensure_ascii=False))' "$merged" > "$work/release-body.json"
        tt_gh_set_release_body "$repo" "$release_id" "$work/release-body.json" || exit 1
        echo "已把升级提示并入 release 说明"
    fi
else
    echo "警告：未找到升级提示文件（$notes_path），已跳过并入 release 说明；发布继续" >&2
fi

echo "检查清单：zip 存在 / .sha256 存在 / .sig 存在且 verify 通过 / release 说明已含升级提示 / 待执行：本地真实 update 冒烟"
