[CmdletBinding()]
param(
    [string]$Version,
    [switch]$SkipUpload,
    [switch]$Force,
    [string]$KeyPath = "E:\work_zone\ApiKey\TextTool-signing.priv.pem",
    [string]$Repo = "TwilightRainDev/TwilightRainTextTool"
)

$ErrorActionPreference = "Stop"
if ($Version -notmatch '^v?\d+\.\d+\.\d+$') {
    [Console]::Error.WriteLine("版本号格式应为 x.y.z 或 vx.y.z")
    exit 2
}

try {
    $raw = $Version -replace '^v', ''
    $tag = "v$raw"
    $repoRoot = Split-Path -Parent $PSScriptRoot
    $pubKey = (Split-Path $KeyPath) + "\TextTool-signing.pub.pem"
    $work = Join-Path $env:TEMP ("texttool-publish-" + $raw)

    if (Test-Path -LiteralPath $work) {
        Remove-Item -LiteralPath $work -Recurse -Force
    }
    New-Item -ItemType Directory -Path $work | Out-Null

    Write-Host "下载 $tag 产物到 $work"
    & gh release download $tag --repo $Repo --pattern "TextTool-*-$raw-win-x64.zip*" --dir $work
    if ($LASTEXITCODE -ne 0) {
        [Console]::Error.WriteLine("gh release download 失败")
        exit 1
    }

    $zips = @(Get-ChildItem -Path $work -File | Where-Object {
        $_.Name -like "TextTool-*-$raw-win-x64.zip"
    })
    if ($zips.Count -eq 0) {
        [Console]::Error.WriteLine("未找到 zip")
        exit 1
    }

    foreach ($zip in $zips) {
        $shaPath = $zip.FullName + ".sha256"
        if (-not (Test-Path -LiteralPath $shaPath)) {
            [Console]::Error.WriteLine("缺少 $($zip.Name).sha256")
            exit 1
        }
    }

    $signerProj = Join-Path $repoRoot "tools\ReleaseSigner"
    Write-Host "构建 ReleaseSigner"
    & dotnet build $signerProj -c Release
    if ($LASTEXITCODE -ne 0) {
        [Console]::Error.WriteLine("ReleaseSigner 构建失败")
        exit 1
    }

    $signerExe = Join-Path $repoRoot "tools\ReleaseSigner\bin\Release\net8.0\ReleaseSigner.exe"
    if (-not (Test-Path -LiteralPath $signerExe)) {
        [Console]::Error.WriteLine("找不到 ReleaseSigner 可执行文件")
        exit 1
    }

    if (-not (Test-Path -LiteralPath $KeyPath)) {
        [Console]::Error.WriteLine("找不到私钥: $KeyPath")
        exit 1
    }
    if (-not (Test-Path -LiteralPath $pubKey)) {
        [Console]::Error.WriteLine("找不到公钥: $pubKey")
        exit 1
    }

    foreach ($zip in $zips) {
        Write-Host "签名 $($zip.Name)"
        & $signerExe sign $zip.FullName -k $KeyPath
        if ($LASTEXITCODE -ne 0) {
            [Console]::Error.WriteLine("签名失败: $($zip.Name)")
            exit 1
        }

        $sigPath = $zip.FullName + ".sig"
        Write-Host "verify $($zip.Name)"
        & $signerExe verify $zip.FullName $sigPath -k $pubKey
        if ($LASTEXITCODE -ne 0) {
            [Console]::Error.WriteLine("verify 失败: $($zip.Name)")
            exit 1
        }
    }

    $sigFiles = @($zips | ForEach-Object { $_.FullName + ".sig" })

    if ($SkipUpload) {
        Write-Host "已 -SkipUpload，不调用 gh release upload"
        Write-Host "检查清单：zip 存在 / .sha256 存在 / .sig 存在且 verify 通过 / 待执行：本地真实 update 冒烟"
        exit 0
    }

    $viewJson = & gh release view $tag --repo $Repo --json assets
    if ($LASTEXITCODE -ne 0) {
        [Console]::Error.WriteLine("无法读取 release 资产列表")
        exit 1
    }
    $view = $viewJson | ConvertFrom-Json
    $remoteNames = @()
    if ($null -ne $view.assets) {
        $remoteNames = @($view.assets | ForEach-Object { $_.name })
    }

    if (-not $Force) {
        foreach ($sig in $sigFiles) {
            $sigName = Split-Path -Leaf $sig
            if ($remoteNames -contains $sigName) {
                [Console]::Error.WriteLine(".sig 已在 release（$sigName），使用 -Force 覆盖")
                exit 1
            }
        }
    }

    $uploadArgs = @("release", "upload", $tag, "--repo", $Repo)
    if ($Force) {
        $uploadArgs += "--clobber"
    }
    foreach ($sig in $sigFiles) {
        $uploadArgs += $sig
    }
    Write-Host "上传 .sig 到 $tag"
    & gh @uploadArgs
    if ($LASTEXITCODE -ne 0) {
        [Console]::Error.WriteLine("gh release upload 失败")
        exit 1
    }

    Write-Host "检查清单：zip 存在 / .sha256 存在 / .sig 存在且 verify 通过 / 待执行：本地真实 update 冒烟"
    exit 0
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
