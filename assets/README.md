# 界面自带的字体

- 文件：`fonts/TextToolWenKai-Regular.ttf`，家族名 TextTool WenKai。
- 底子：霞鹜文楷 Mono（LXGW WenKai Mono）1.522 版的子集，上游 https://github.com/lxgw/LxgwWenKai 。
  字形来自博客站网页用的那份 `LXGWWenKai.woff2`；界面文字里的「─」「×」「€」子集没有，从同版本的
  完整版里补了这三个字形。
- 许可证：SIL Open Font License 1.1，正文在 `fonts/OFL.txt`，版权与许可证声明留在字体文件里。

家族名不用「LXGW」「霞鹜」：子集化属于改动过的版本，保留字体名条款不许改动过的版本沿用这些名字，
上游的额外许可只放开网页字体分发这一种。改名字不动字形。

## 程序怎么用

字体文件放在程序所在的目录里，与 `texttool-gui` 并排；找不到再找程序目录下的 `fonts` 子目录；
都没有就退回系统自带的中文字体。字体不编进二进制，换字体不用重编。

## 怎么重做

`tools/make_font_asset.py` 用子集 woff2 与 1.522 版完整版 Mono TTF 重做这份 TTF，会核对原地
字形一个不少、轮廓与步进不变，再改家族名。完整版约 25MB，不进仓库，需要时从上游发布页下载。
需要 fontTools 与 brotli。

# 程序图标与作者头像

- `images/icon.png`：程序图标，128 × 128。同一个文件也用在小窗左上角与任务栏。
- `images/avatar.png`：作者头像，128 × 128。底子是归档里桌面版的 `Config/Archive/TextTool/Resources/TwilightRain.jpg`
  （200 × 200），缩到 128 见方后另存为 PNG：界面里画 64 见方，留一倍给高分屏。
- 两份都是作者自有素材，可随产品分发。
- 界面里按 64 见方显示，两张并排在关于页最上面。

## 程序怎么用

与界面字体同一个摆法：两份 PNG 复制到 `texttool-gui` 所在目录，程序启动时按这个位置找；
找不到再找程序目录下的 `images/` 与 `assets/images/`。找不到时关于页上就是没画图，不影响使用。

# 随包带的规则表

- 文件：`default_lint_rules.json`，检查判据的定义与阈值都在这里，引擎不另存副本。
- 与归档里桌面版 `TextTool.Core/default_lint_rules.json` 逐字节相同；从归档那天起，改规则改这一份。
- 构建时复制到两个程序旁边。程序先找环境变量 `TEXTTOOL_RULES_JSON`，再找程序所在目录这份，
  最后从那里逐层往上找六层里的 `assets/default_lint_rules.json`。

# 随包带的那份角色表

- 文件：`default_vn_schemes.json`，与归档里桌面版 `TextTool.Core/default_vn_schemes.json` 逐字节相同：
  四份内置角色表、一共 98 个角色名。桌面版把它编进程序；原生版按文件发，界面里那份清单
  打开时就能看到这四份，命令行 `vn --scheme` 也认。
- 界面改了角色表就写回同一目录下的 `vn_schemes.json`（有 `TEXTTOOL_VN_SCHEMES_JSON` 就写那里），
  这份默认表只读不动：删掉它等于把随包带的那四份一起丢了，下次打开清单就是空的。
- 内置的四份以后改也只能改这一份：桌面版已归档，不再往回带。
