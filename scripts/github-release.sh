#!/usr/bin/env bash
# GitHub REST 助手，只被 source。走 curl，JSON 用 python3 解析，不调用 gh。
# 调用方要先 source release-assets.sh：本文件的资产函数用它的名单。

TT_GITHUB_DEFAULT_REPO="TwilightRainDev/TwilightRainTextTool"
TT_GITHUB_USER="TwilightRainDev"

# 令牌只从环境读，不进仓库、不进命令行参数。
tt_gh_token() {
    if [ -z "${TEXTTOOL_GITHUB_TOKEN:-}" ]; then
        echo "未设置 TEXTTOOL_GITHUB_TOKEN" >&2
        return 1
    fi
    printf '%s' "$TEXTTOOL_GITHUB_TOKEN"
}

# 解析 stdin 的 JSON，脚本片段里用变量 d（已解析的对象）。结果打到 stdout。
tt_json() {
    python3 -c 'import json,sys
d = json.load(sys.stdin)
'"$1"
}

# 发一次请求，结果放进 TT_STATUS 与 TT_BODY。
# 参数：方法 URL [响应落盘文件] [上传文件] [Content-Type] [Accept] [重试次数]
# 响应落盘时 TT_BODY 为空，调用方自己读文件。
# 502/503 按 2 秒、4 秒退避重试，其余状态码一律原样交回，由调用方判断。
tt_gh_http() {
    local method="$1" url="$2"
    local out_file="${3:-}" upload_file="${4:-}" content_type="${5:-application/octet-stream}"
    local accept="${6:-application/vnd.github+json}" retries="${7:-3}"
    local token attempt=1 code body_file

    TT_STATUS=0
    TT_BODY=""
    token="$(tt_gh_token)" || return 1

    while :; do
        local args=(-sS -L --max-redirs 5 -X "$method"
            -H "Authorization: Bearer $token"
            -H "Accept: $accept"
            -H "User-Agent: TextTool-release"
            -H "X-GitHub-Api-Version: 2022-11-28")

        if [ -n "$upload_file" ]; then
            args+=(-H "Content-Type: $content_type" --data-binary "@$upload_file")
        fi

        body_file=""
        if [ -n "$out_file" ]; then
            code="$(curl "${args[@]}" -o "$out_file" -w '%{http_code}' "$url")" || code=000
        else
            body_file="$(mktemp)"
            code="$(curl "${args[@]}" -o "$body_file" -w '%{http_code}' "$url")" || code=000
            [ -s "$body_file" ] && TT_BODY="$(cat "$body_file")"
            rm -f "$body_file"
        fi

        TT_STATUS="$code"
        if { [ "$code" = 502 ] || [ "$code" = 503 ]; } && [ "$attempt" -lt "$retries" ]; then
            sleep $((2 * attempt))
            attempt=$((attempt + 1))
            continue
        fi
        return 0
    done
}

# 读某个 tag 的 release，JSON 放进 TT_BODY。
tt_gh_release_by_tag() {
    local repo="$1" tag="$2"
    tt_gh_http GET "https://api.github.com/repos/$repo/releases/tags/$tag" || return 1
    if [ "$TT_STATUS" != 200 ]; then
        echo "读取 release $tag 失败 HTTP $TT_STATUS" >&2
        return 1
    fi
}

# 读某个 commit 的 workflow runs，JSON 放进 TT_BODY。
tt_gh_workflow_runs() {
    local repo="$1" sha="$2"
    tt_gh_http GET "https://api.github.com/repos/$repo/actions/runs?head_sha=$sha&per_page=20" || return 1
    if [ "$TT_STATUS" != 200 ]; then
        echo "读取 workflow runs 失败 HTTP $TT_STATUS" >&2
        return 1
    fi
}

# 名单里有、这份 release 上也就该有的资产，一并下到目标目录。
# .sha256 与 .sig 与 zip 同名加后缀，所以按前缀匹配就能一起下来。
# 参数：repo tag 版本号（不带 v） 目标目录
# 输出：落盘的文件路径，一行一个。
tt_gh_save_release_assets() {
    local repo="$1" tag="$2" raw="$3" dest="$4"
    local names name url
    names="$(tt_gh_release_by_tag "$repo" "$tag" && printf '%s' "$TT_BODY" | tt_json '
for asset in d.get("assets") or []:
    print(asset["name"], asset["url"])')" || return 1

    local prefixes
    prefixes="$(texttool_release_asset_names "$raw")"

    while read -r name url; do
        [ -n "$name" ] || continue
        local matched=false prefix
        while IFS= read -r prefix; do
            case "$name" in
                "$prefix"*) matched=true; break ;;
            esac
        done <<< "$prefixes"
        [ "$matched" = true ] || continue

        tt_gh_http GET "$url" "$dest/$name" "" "application/octet-stream" || return 1
        if [ "$TT_STATUS" != 200 ]; then
            echo "下载资产失败 $name HTTP $TT_STATUS" >&2
            return 1
        fi
        printf '%s\n' "$dest/$name"
    done <<< "$names"
}

# 按名字删一个已有资产；没这个资产就什么也不做。
tt_gh_remove_release_asset() {
    local repo="$1" release_json="$2" name="$3"
    local id
    id="$(printf '%s' "$release_json" | tt_json '
for asset in d.get("assets") or []:
    if asset["name"] == "'"$name"'":
        print(asset["id"])
        break')"
    [ -n "$id" ] || return 0

    tt_gh_http DELETE "https://api.github.com/repos/$repo/releases/assets/$id" || return 1
    if [ "$TT_STATUS" != 204 ] && [ "$TT_STATUS" != 200 ]; then
        echo "删除资产失败 $name HTTP $TT_STATUS" >&2
        return 1
    fi
}

# 传一个文件当资产。参数：repo release id 文件路径
tt_gh_add_release_asset() {
    local repo="$1" release_id="$2" file="$3"
    local name
    name="$(basename "$file")"
    tt_gh_http POST "https://uploads.github.com/repos/$repo/releases/$release_id/assets?name=$name" \
        "" "$file" || return 1
    if [ "$TT_STATUS" != 201 ]; then
        echo "上传资产失败 $name HTTP $TT_STATUS" >&2
        return 1
    fi
}

# 改 release 正文。参数：repo release id 正文文件（UTF-8 JSON，含 body 字段）
tt_gh_set_release_body() {
    local repo="$1" release_id="$2" body_file="$3"
    tt_gh_http PATCH "https://api.github.com/repos/$repo/releases/$release_id" \
        "" "$body_file" "application/json" || return 1
    if [ "$TT_STATUS" != 200 ]; then
        echo "更新 release 说明失败 HTTP $TT_STATUS" >&2
        return 1
    fi
}

# 带令牌推一个 tag。git 命令本身不带凭据，令牌只在这一次调用的请求头里。
tt_push_tag_with_token() {
    local tag="$1"
    local token b64
    token="$(tt_gh_token)" || return 1
    b64="$(printf '%s:%s' "$TT_GITHUB_USER" "$token" | base64 | tr -d '\n')"
    git -c http.extraheader="Authorization: Basic $b64" push origin "refs/tags/$tag" || {
        echo "推 tag $tag 失败" >&2
        return 1
    }
}
