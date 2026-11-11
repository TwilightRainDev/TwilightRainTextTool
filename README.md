# 桌面版文本工具（3.1.0，已归档）

文本工具的 .NET 版：引擎 `TextTool.Core`、界面 `TextTool.Gui`（Avalonia）、命令行 `TextTool.Cli`。
代码停在提交 `207dd38`，分支 `legacy-3.X`；这份说明是它之上另加的一条提交，不改代码。

客户要求 15MB 稿子的整进程峰值不超过 40MB，这一版实测 149.62MB，做不到，于是用 C++ 重写接手：
`HumanProject/TextTool.Native`。4.0 那轮减负做过的树留在 tag `v4.0.0-draft` 里。

## 状态

只读。`bin/`、`obj/` 是归档时的构建产物，别删：原生版的逐字节对照拿这里的命令行程序当参照。
`Directory.Build.props` 里的版本号停在 3.1.0。

`origin` 的 `main` 仍是这份代码。分支 `legacy-3.X` 只在本机建好，还没推：这台机器 HTTPS 到 GitHub 的 TLS
被中间代理掐断，也没有可用的 SSH 密钥。能连上时补一次，再把 `main` 腾给原生版。

```
git push origin legacy-3.X
```

## 原生版从这里拿什么

参考命令行 `TextTool.Cli/bin/Release/net8.0/texttool.dll`，四个处理命令与 `lint` 的期望输出都由它跑出来。
规则表 `TextTool.Core/default_lint_rules.json` 已复制进 `HumanProject/TextTool.Native/assets/`，
往后改规则改原生那一份，这一份是历史快照。重新编参考实现：

```
cd Config/Archive/TextTool
dotnet build TextTool.sln -c Release
```

原生版脚本默认指到这个目录；目录换位置时用 `TEXTTOOL_REF_DLL`、`REFERENCE_CLI` 指过去。

## 不再维护的东西

注释与文案不再修，`Docs/Debt/TECH-DEBT.md` 里的桌面版条目随归档撤掉。
判据本身没有作废：定义与阈值都在规则表里，原生版读同一份，检查的对外契约以原生版为准。
