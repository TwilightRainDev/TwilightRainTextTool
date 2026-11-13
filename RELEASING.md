# 发布

## 一次发布出什么

四份压缩包，加上每份旁边的校验值与签名：

| 文件 | 平台 | 内容 |
| --- | --- | --- |
| `texttool-windows-gui.zip` | Windows x64 | 界面与命令行，加规则表、角色表、字体、图标、头像、使用说明 |
| `texttool-windows-cli.zip` | Windows x64 | 命令行，加规则表、角色表、使用说明 |
| `texttool-kylin-gui.zip` | Linux x64（麒麟） | 界面与命令行，加规则表、角色表、字体、图标、头像、使用说明 |
| `texttool-kylin-cli.zip` | Linux x64（麒麟） | 命令行，加规则表、角色表、使用说明 |

每个 zip 旁边有两份附属文件：

- `<zip 名>.sha256`：打包脚本自动写，一行「小写 SHA256、两个空格、文件名」。给下载的人核对文件有没有传坏，不需要私钥。
- `<zip 名>.sig`：发布者用私钥对 zip 本体的签名，64 字节。

设计图、对照图与界面截图只在开发目录 `tools/probes/`，不进任何发布包。打包脚本只按名单取文件，另有一道检查：取到的路径里带 `probes` 就报错退出。

## 版本号从哪来

唯一来源是 `CMakeLists.txt` 的 `project(TextToolNative VERSION 4.0.0 ...)`。命令行的版本行、界面「关于」页与发布包名都从这里取。

改版本号时，`fixtures/cli/` 下钉着用法首行的三份期望输出要一起重出，否则逐字节对照会红：

- `fixtures/cli/help.expected.txt`
- `fixtures/cli/usage.expected.txt`
- `fixtures/cli/unknown_command.expected.txt`

## 构建

### Linux

```bash
cd HumanProject/TextTool.Native
export PATH=/home/twilightrain/Tools/cmake-env/bin:$PATH
cmake -S . -B build-linux -DCMAKE_BUILD_TYPE=Release \
      -DTEXTTOOL_FLTK_PREFIX=/home/twilightrain/Tools/fltk-1.4.5-linux
cmake --build build-linux -j
```

产物：`build-linux/texttool`、`build-linux/texttool-gui`。

### Windows

Windows 工具不接受网络共享路径当工作目录，构建目录要落在 Win 盘上。这台机器上的落点：

```bat
call E:\CodeApps\Cursor\cpp\Root\VC\Auxiliary\Build\vcvars64.bat
E:\Tools\cpp\cmake-3.30.5-windows-x86_64\bin\cmake.exe ^
  -S "\\wsl.localhost\Ubuntu\home\twilightrain\WorkZone\HumanProject\TextTool.Native" ^
  -B "E:\WorkZone\build\tt-win-gui2" ^
  -G "NMake Makefiles" -DCMAKE_BUILD_TYPE=Release ^
  -DTEXTTOOL_FLTK_PREFIX=E:/WorkZone/build/fltk-win/install
E:\Tools\cpp\cmake-3.30.5-windows-x86_64\bin\cmake.exe --build E:\WorkZone\build\tt-win-gui2 --config Release
```

产物：`E:\WorkZone\build\tt-win-gui2\texttool.exe` 与 `texttool-gui.exe`。界面程序的子系统是 Windows GUI，双击不弹控制台窗口；运行库静态链接，目标机器不需要装 VC++ 运行库。

## 打包

```bash
cd HumanProject/TextTool.Native
python3 tools/package.py build-linux kylin cli
python3 tools/package.py build-linux kylin gui
python3 tools/package.py /mnt/e/WorkZone/build/tt-win-gui2 windows cli
python3 tools/package.py /mnt/e/WorkZone/build/tt-win-gui2 windows gui
```

产物落在仓库的 `dist/`（已被版本库忽略）。每份 zip 打完会写同名 `.sha256`；压缩包按固定时间戳写，同一份产物重复打出来字节相同。单份包上限 10MB。

打完先自己核对一遍：

```bash
cd dist
for f in *.zip; do sha256sum -c "$f.sha256"; done
```

## 签名

签名工具在归档的桌面版里，用 .NET 8 启动：

```bash
SIGNER=/home/twilightrain/WorkZone/Config/Archive/TextTool/tools/ReleaseSigner/bin/Release/net8.0/ReleaseSigner.dll
```

私钥由发布者保管，放在仓库外面、只有本人可读的目录；不要复制进工作区，也不要写进任何文档。公钥与私钥同目录，文件名 `TextTool-signing.pub.pem`。

签名与验签：

```bash
cd HumanProject/TextTool.Native/dist
PRIV=/私钥目录/TextTool-signing.priv.pem
PUB=/私钥目录/TextTool-signing.pub.pem

set -e
for z in texttool-*.zip; do
  dotnet "$SIGNER" sign "$z" -k "$PRIV"
  dotnet "$SIGNER" verify "$z" "$z.sig" -k "$PUB"
done
```

私钥加了口令时，签名命令加 `--passphrase 口令`，或者设环境变量 `TEXTTOOL_SIGN_PASSPHRASE`。私钥只有 root 可读时，签名过程用 root 跑，跑完把 `.sig` 的属主与权限改回普通用户：

```bash
chown twilightrain:twilightrain *.zip.sig
chmod 644 *.zip.sig
```

验签应当对四份都输出「签名有效」。另外做一次负向检查：拿任意一份的 `.sig` 去验另一份 zip，应当输出「签名无效」，证明签名确实绑在各自的 zip 上。

签名是对 zip 本体做的：重新打过包，`.sha256` 会变，`.sig` 也必须重签，不能混用。

### 客户端验签

程序不含自动更新，也不在运行时验签；`.sig` 是发给下载者的发布凭据。以后要做客户端验签，用发布者手上这把公钥，它的 SPKI base64 是：

```text
MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEIzWVU8dpX9CjEDUiVO5HtQ+c+hL6jJbOwfG1kkxyfWJronzXnLwXzDbk4TFdzHM7HVFVS+qdWHdqdBd4zky6fg==
```

不要另外生成一对新密钥，否则与已发出的签名对不上。

## 交付前的检查

```bash
cd HumanProject/TextTool.Native

./build-linux/engine_tests              # Linux：320 项
bash tests/compare_cli.sh               # 命令行逐字节对照，183 份
bash tests/compare_processing.sh        # 四个处理命令，122 条（12 条有意不同）
TEXTTOOL_LAYOUT_CHECK=1 TEXTTOOL_UI_SCALE=1 ./build-linux/texttool-gui   # 三档尺寸、七页控件
python3 tools/acceptance_check.py --windows /mnt/e/WorkZone/build/tt-win-gui2/texttool.exe
```

Windows 上另跑一次 `engine_tests.exe`（320 项）与界面自检：

```bat
set TEXTTOOL_SELF_CHECK=E:\WorkZone\build\tt-test.txt
set TEXTTOOL_UI_SCALE=1
texttool-gui.exe > self.txt 2>&1
```

自检要在标准输出里看到源文字数、结果字数、耗时与写成的文件。

真机检查这几项不由构建机器决定：

- Windows：解压四份包里的界面包，双击能起、中文正常、深色标题栏跟着黑、处理一份稿子成功。
- 麒麟：输入法打得进字、缩放算出来是 1、处理页与五个高级页各跑一遍。
- 客户真稿：命令行与界面各跑一遍，记录内存最高值与耗时。

## 发布

没有自动出包流水线，四份包与附属文件由本机构建后手工上架。上架顺序：四份 zip、四份 `.sha256`、四份 `.sig`；发布说明从 `docs/ReleaseNotes.md` 取，写清这一版是 4.0、不兼容 3.x 的配置文件、自动更新与多语言与繁简转换都不做。
