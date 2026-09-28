[CmdletBinding()]
param(
    [string]$Version,
    [switch]$SkipUpload,
    [switch]$SkipTag,
    [switch]$DryRun,
    [switch]$Force,
    [string]$KeyPath = "E:\work_zone\ApiKey\TextTool-signing.priv.pem",
    [string]$Repo = "TwilightRainDev/TwilightRainTextTool",
    [string]$Message
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "github-release.ps1")

$repoRoot = Split-Path -Parent $PSScriptRoot

function Get-PropsVersion {
    $props_path = Join-Path $repoRoot "Directory.Build.props"
    $text = [IO.File]::ReadAllText($props_path)
    $m = [regex]::Match($text, '<Version>([0-9]+\.[0-9]+\.[0-9]+)</Version>')
    if (-not $m.Success) {
        throw "Directory.Build.props 未找到 Version"
    }
    return $m.Groups[1].Value
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = Get-PropsVersion
}

if ($Version -notmatch '^v?\d+\.\d+\.\d+$') {
    [Console]::Error.WriteLine("版本号格式应为 x.y.z 或 vx.y.z")
    exit 2
}

try {
    $raw = $Version -replace '^v', ''
    $tag = "v$raw"
    $props_ver = Get-PropsVersion
    $sha = (git -C $repoRoot rev-parse HEAD).Trim()
    $branch = (git -C $repoRoot rev-parse --abbrev-ref HEAD).Trim()
    $dirty = (git -C $repoRoot status --porcelain)
    $origin_sha = $null
    git -C $repoRoot rev-parse --verify origin/main 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) {
        $origin_sha = (git -C $repoRoot rev-parse origin/main).Trim()
    }

    if ($DryRun) {
        Write-Host "version=$raw"
        Write-Host "tag=$tag"
        Write-Host "sha=$sha"
        Write-Host "branch=$branch"
        Write-Host "dirty=$([bool]$dirty)"
        Write-Host "props=$props_ver"
        exit 0
    }

    if ($props_ver -ne $raw) {
        [Console]::Error.WriteLine("Directory.Build.props 为 $props_ver，与 tag $tag 不一致")
        exit 1
    }

    if ($dirty) {
        [Console]::Error.WriteLine("工作树不干净，拒绝发版")
        exit 1
    }

    if (-not $SkipTag) {
        if ($branch -ne "main") {
            [Console]::Error.WriteLine("当前分支是 $branch，发版须在 main")
            exit 1
        }
        if (-not $origin_sha) {
            [Console]::Error.WriteLine("没有 origin/main")
            exit 1
        }
        if ($sha -ne $origin_sha) {
            [Console]::Error.WriteLine("HEAD 与 origin/main 不一致，先推 main")
            exit 1
        }

        git -C $repoRoot show-ref --verify --quiet "refs/tags/$tag"
        if ($LASTEXITCODE -eq 0) {
            [Console]::Error.WriteLine("本地已有 $tag，改用 -SkipTag 只签名，或先处理旧 tag")
            exit 1
        }

        git -C $repoRoot ls-remote --exit-code --tags origin "refs/tags/$tag" 2>$null | Out-Null
        if ($LASTEXITCODE -eq 0) {
            [Console]::Error.WriteLine("远端已有 $tag，改用 -SkipTag 只签名")
            exit 1
        }

        if ([string]::IsNullOrWhiteSpace($Message)) {
            $Message = $tag
        }
        git -C $repoRoot tag -a $tag -m $Message
        if ($LASTEXITCODE -ne 0) {
            [Console]::Error.WriteLine("创建 tag 失败")
            exit 1
        }
        Write-Host "已创建 $tag，推送到 origin"
        Push-GitTagWithToken -Tag $tag
    }

    Write-Host "等待 CI 完成 $tag （最多 30 分钟）"
    $deadline = (Get-Date).AddMinutes(30)
    $run = $null
    while ((Get-Date) -lt $deadline) {
        $payload = Get-GithubWorkflowRuns -Repo $Repo -Sha $sha
        $found = $null
        foreach ($item in @($payload.workflow_runs)) {
            if ([string]$item.head_branch -eq $tag) {
                $found = $item
                break
            }
        }
        if ($null -ne $found -and [string]$found.status -eq "completed") {
            $run = $found
            break
        }
        Start-Sleep -Seconds 20
    }

    if ($null -eq $run) {
        [Console]::Error.WriteLine("等待 CI 超时")
        exit 1
    }
    if ([string]$run.conclusion -ne "success") {
        [Console]::Error.WriteLine("CI 未成功: $($run.conclusion) $($run.html_url)")
        exit 1
    }
    Write-Host "CI 已成功: $($run.html_url)"

    Write-Host "等待 Release 资产"
    $release = $null
    while ((Get-Date) -lt $deadline) {
        try {
            $release = Get-GithubReleaseByTag -Repo $Repo -Tag $tag
            $names = @()
            if ($null -ne $release.assets) {
                $names = @($release.assets | ForEach-Object { $_.name })
            }
            $need = @(
                "TextTool-GUI-$raw-win-x64.zip",
                "TextTool-GUI-$raw-win-x64.zip.sha256",
                "TextTool-CLI-$raw-win-x64.zip",
                "TextTool-CLI-$raw-win-x64.zip.sha256"
            )
            $ok = $true
            foreach ($n in $need) {
                if ($names -notcontains $n) { $ok = $false }
            }
            if ($ok) { break }
        }
        catch {
            $release = $null
        }
        Start-Sleep -Seconds 20
    }

    if ($null -eq $release) {
        [Console]::Error.WriteLine("未等到 $tag 的 GitHub Release 资产")
        exit 1
    }

    $publish = Join-Path $PSScriptRoot "publish.ps1"
    $pub_args = @(
        "-NoProfile", "-ExecutionPolicy", "Bypass",
        "-File", $publish,
        "-Version", $raw,
        "-KeyPath", $KeyPath,
        "-Repo", $Repo
    )
    if ($SkipUpload) { $pub_args += "-SkipUpload" }
    if ($Force) { $pub_args += "-Force" }

    Write-Host "调用 publish.ps1"
    & powershell.exe @pub_args
    exit $LASTEXITCODE
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
