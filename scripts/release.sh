#!/usr/bin/env bash
# 发一次版：校验版本与工作树，打 tag 推上去，等 CI 产出四份 zip，再交给 publish.sh 签名上传。
# 不传 -Version 就用 Directory.Build.props 里的版本。
set -uo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(dirname "$script_dir")"
# shellcheck source=release-assets.sh
. "$script_dir/release-assets.sh"
# shellcheck source=github-release.sh
. "$script_dir/github-release.sh"

version=""
skip_upload=false
skip_tag=false
dry_run=false
force=false
key_path="${TEXTTOOL_SIGN_KEY:-}"
repo="$TT_GITHUB_DEFAULT_REPO"
message=""

while [ $# -gt 0 ]; do
    case "$1" in
        -Version|--version) version="${2:-}"; shift 2 ;;
        -SkipUpload|--skip-upload) skip_upload=true; shift ;;
        -SkipTag|--skip-tag) skip_tag=true; shift ;;
        -DryRun|--dry-run) dry_run=true; shift ;;
        -Force|--force) force=true; shift ;;
        -KeyPath|--key-path) key_path="${2:-}"; shift 2 ;;
        -Repo|--repo) repo="${2:-}"; shift 2 ;;
        -Message|--message) message="${2:-}"; shift 2 ;;
        -h|--help) sed -n '2,3p' "${BASH_SOURCE[0]}"; exit 0 ;;
        *) echo "未知参数：$1" >&2; exit 2 ;;
    esac
done

props_version() {
    sed -n 's:.*<Version>\([0-9]\+\.[0-9]\+\.[0-9]\+\)</Version>.*:\1:p' \
        "$repo_root/Directory.Build.props" | head -1
}

if [ -z "$version" ]; then
    version="$(props_version)"
fi
if [ -z "$version" ]; then
    echo "Directory.Build.props 未找到 Version" >&2
    exit 1
fi

if ! [[ "$version" =~ ^v?[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    echo "版本号格式应为 x.y.z 或 vx.y.z" >&2
    exit 2
fi

raw="${version#v}"
tag="v$raw"
props_ver="$(props_version)"
sha="$(git -C "$repo_root" rev-parse HEAD)"
branch="$(git -C "$repo_root" rev-parse --abbrev-ref HEAD)"
dirty="$(git -C "$repo_root" status --porcelain)"
origin_sha=""
if git -C "$repo_root" rev-parse --verify origin/main >/dev/null 2>&1; then
    origin_sha="$(git -C "$repo_root" rev-parse origin/main)"
fi

if [ "$dry_run" = true ]; then
    echo "version=$raw"
    echo "tag=$tag"
    echo "sha=$sha"
    echo "branch=$branch"
    echo "dirty=$([ -n "$dirty" ] && echo True || echo False)"
    echo "props=$props_ver"
    exit 0
fi

if [ "$props_ver" != "$raw" ]; then
    echo "Directory.Build.props 为 $props_ver，与 tag $tag 不一致" >&2
    exit 1
fi

if [ -n "$dirty" ]; then
    echo "工作树不干净，拒绝发版" >&2
    exit 1
fi

if [ "$skip_tag" != true ]; then
    if [ "$branch" != "main" ]; then
        echo "当前分支是 $branch，发版须在 main" >&2
        exit 1
    fi
    if [ -z "$origin_sha" ]; then
        echo "没有 origin/main" >&2
        exit 1
    fi
    if [ "$sha" != "$origin_sha" ]; then
        echo "HEAD 与 origin/main 不一致，先推 main" >&2
        exit 1
    fi

    if git -C "$repo_root" show-ref --verify --quiet "refs/tags/$tag"; then
        echo "本地已有 $tag，改用 -SkipTag 只签名，或先处理旧 tag" >&2
        exit 1
    fi

    if git -C "$repo_root" ls-remote --exit-code --tags origin "refs/tags/$tag" >/dev/null 2>&1; then
        echo "远端已有 $tag，改用 -SkipTag 只签名" >&2
        exit 1
    fi

    [ -n "$message" ] || message="$tag"
    if ! git -C "$repo_root" tag -a "$tag" -m "$message"; then
        echo "创建 tag 失败" >&2
        exit 1
    fi
    echo "已创建 $tag，推送到 origin"
    tt_push_tag_with_token "$tag" || exit 1
fi

echo "等待 CI 完成 $tag （最多 30 分钟）"
deadline=$((SECONDS + 1800))
run_conclusion=""
run_url=""
while [ "$SECONDS" -lt "$deadline" ]; do
    tt_gh_workflow_runs "$repo" "$sha" || exit 1
    # 打 tag 触发的 run 以 tag 为 head_branch；同名分支的 run 不算。
    line="$(printf '%s' "$TT_BODY" | tt_json '
import sys
for run in d.get("workflow_runs") or []:
    if str(run.get("head_branch")) == "'"$tag"'":
        print("|".join([str(run.get("status")), str(run.get("conclusion") or ""), str(run.get("html_url") or "")]))
        break')"
    if [ -n "$line" ]; then
        IFS='|' read -r run_status run_conclusion run_url <<< "$line"
        if [ "$run_status" = "completed" ]; then
            break
        fi
    fi
    sleep 20
done

if [ -z "$run_conclusion" ]; then
    echo "等待 CI 超时" >&2
    exit 1
fi
if [ "$run_conclusion" != "success" ]; then
    echo "CI 未成功: $run_conclusion $run_url" >&2
    exit 1
fi
echo "CI 已成功: $run_url"

echo "等待 Release 资产"
missing_count=1
missing=()
while [ "$SECONDS" -lt "$deadline" ]; do
    if tt_gh_release_by_tag "$repo" "$tag"; then
        mapfile -t names < <(printf '%s' "$TT_BODY" | tt_json '
for asset in d.get("assets") or []:
    print(asset["name"])')
        mapfile -t missing < <(texttool_missing_release_assets "$raw" "${names[@]}")
        missing_count="${#missing[@]}"
        if [ "$missing_count" -eq 0 ]; then
            break
        fi
    fi
    sleep 20
done

if [ "$missing_count" -gt 0 ]; then
    hint=""
    if [ "${#missing[@]}" -gt 0 ]; then
        hint="，缺少：$(printf '%s, ' "${missing[@]}" | sed 's/, $//')"
    fi
    echo "未等到 $tag 的 GitHub Release 资产$hint" >&2
    exit 1
fi

pub_args=(-Version "$raw")
[ -n "$key_path" ] && pub_args+=(-KeyPath "$key_path")
pub_args+=(-Repo "$repo")
[ "$skip_upload" = true ] && pub_args+=(-SkipUpload)
[ "$force" = true ] && pub_args+=(-Force)

echo "调用 publish.sh"
bash "$script_dir/publish.sh" "${pub_args[@]}"
exit $?
