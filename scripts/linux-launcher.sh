#!/usr/bin/env bash
# Linux 启动器。发行包里的 texttool 与 TextTool.Gui 就是本文件：发布时按产品名复制，
# 打包时给它可执行位。dll 名由脚本自身文件名决定，所以一份脚本同时服务命令行与图形界面。
#
# 不带 SDK 生成的 apphost：apphost 只认 DOTNET_ROOT、/etc/dotnet/install_location
# 与 /usr/share/dotnet 这几处固定位置。dotnet-install.sh 默认装到 ~/.dotnet，apphost
# 在那里找不到运行时，报 libhostfxr.so not found。dotnet 可执行文件（多路复用器）
# 自己知道根在哪，所以这里只找它，不猜运行时目录，也不解析 roll-forward。
set -uo pipefail

name=$(basename -- "$0")

# 经符号链接调用时按真实路径取：dll 与本脚本始终同目录。
self="$0"
if command -v readlink >/dev/null 2>&1; then
    resolved=$(readlink -f -- "$self" 2>/dev/null || true)
    [ -n "$resolved" ] && self="$resolved"
fi
dir=$(CDPATH='' cd -- "$(dirname -- "$self")" && pwd -P) || {
    echo "无法定位安装目录：$self" >&2
    exit 2
}
dll="$dir/$name.dll"
[ -f "$dll" ] || {
    echo "安装包不完整：缺少 $dll（应与本启动器同目录）" >&2
    exit 2
}

# 探测顺序：PATH → DOTNET_ROOT → 用户级安装 → 发行版与官方仓库 → snap → /opt。
# 只判断可执行，不判断版本：跑哪个运行时由用户环境决定，脚本不替用户挑。
find_dotnet() {
    local candidate
    if candidate=$(command -v dotnet 2>/dev/null) && [ -x "$candidate" ]; then
        printf '%s' "$candidate"
        return 0
    fi
    for candidate in \
        "${DOTNET_ROOT:+$DOTNET_ROOT/dotnet}" \
        "$HOME/.dotnet/dotnet" \
        /usr/share/dotnet/dotnet \
        /usr/lib/dotnet/dotnet \
        /snap/dotnet-sdk/current/dotnet \
        /opt/dotnet/dotnet
    do
        [ -n "$candidate" ] || continue
        if [ -x "$candidate" ]; then
            printf '%s' "$candidate"
            return 0
        fi
    done
    return 1
}

if ! dotnet=$(find_dotnet); then
    cat >&2 <<'EOF'
找不到 .NET 运行时。这个工具需要 .NET 8 或更新的运行时。

任选一种装法：
  1) 发行版仓库（Debian/Ubuntu，需要 root；包名各发行版略有不同，常见为 dotnet-runtime-8.0 或 dotnet8）
     sudo apt install dotnet-runtime-8.0
  2) 官方脚本装到用户目录（不需要 root，装完直接可用，不必设 DOTNET_ROOT）
     curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 8.0 --runtime dotnet
  3) 已经装在别处：把目录告诉本脚本
EOF
    printf '     DOTNET_ROOT=/你的/dotnet/目录 ./%s\n' "$name" >&2
    printf '\n下载页：https://dotnet.microsoft.com/download/dotnet/8.0\n' >&2
    exit 127
fi

exec "$dotnet" "$dll" "$@"
