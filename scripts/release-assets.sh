#!/usr/bin/env bash
# 发布产物的名单与齐活判据，只被 source，不单独执行。
#
# 名单必须与 TextTool.Core/SelfUpdater 的 GetGuiAssetName、GetCliAssetName 拼出的名字逐字一致：
# 客户端按名字找包，签名与等待按名字核对，两边差一个字就会出现「客户端找得到却没人产出」
# 或「产出了却没人签名」。CI 的打包与核对、publish.sh、release.sh 都从这里取。

# 四份 zip：GUI 与 CLI 各有 win-x64 与 linux-x64。
# 参数：版本号，不带前导 v。
texttool_release_asset_names() {
    local raw="$1"
    printf '%s\n' \
        "TextTool-GUI-$raw-win-x64.zip" \
        "TextTool-GUI-$raw-linux-x64.zip" \
        "TextTool-CLI-$raw-win-x64.zip" \
        "TextTool-CLI-$raw-linux-x64.zip"
}

# 齐活判据：名单里每份 zip 都要在，且各自带一份 .sha256。
# 参数：版本号（不带 v），之后是已有文件名。
# 输出：缺失的名字，一行一个；缺的是 zip 就点 zip 的名，zip 在而校验和缺就点 .sha256 的名。
# 四份齐活时无输出。
texttool_missing_release_assets() {
    local raw="$1"
    shift
    local name present sha item
    while IFS= read -r name; do
        present=false
        sha=false
        for item in "$@"; do
            [ "$item" = "$name" ] && present=true
            [ "$item" = "$name.sha256" ] && sha=true
        done
        if [ "$present" != true ]; then
            printf '%s\n' "$name"
        elif [ "$sha" != true ]; then
            printf '%s\n' "$name.sha256"
        fi
    done < <(texttool_release_asset_names "$raw")
}
