# Shared GitHub REST helpers for TextTool release scripts.
# Dot-source only. Do not run as an entry point.

function Get-TextToolGithubToken {
    if (-not [string]::IsNullOrWhiteSpace($env:TEXTTOOL_GITHUB_TOKEN)) {
        return $env:TEXTTOOL_GITHUB_TOKEN.Trim()
    }
    throw "未设置 TEXTTOOL_GITHUB_TOKEN"
}

function Get-CurlExe {
    $curl = Get-Command "curl.exe" -ErrorAction SilentlyContinue
    if ($null -eq $curl) {
        throw "找不到 curl.exe"
    }
    return $curl.Source
}

function Invoke-GithubHttp {
    param(
        [string]$Method = "GET",
        [Parameter(Mandatory = $true)][string]$Url,
        [string]$OutFile,
        [string]$UploadFile,
        [string]$ContentType,
        [string]$Accept = "application/vnd.github+json",
        [int]$Retries = 3
    )

    $token = Get-TextToolGithubToken
    $curl_exe = Get-CurlExe
    $attempt = 0
    $code = 0
    $tmp = $null

    while ($attempt -lt $Retries) {
        $attempt++
        $args = @(
            "-sS", "-L", "--max-redirs", "5",
            "-X", $Method,
            "-H", "Authorization: Bearer $token",
            "-H", "Accept: $Accept",
            "-H", "User-Agent: TextTool-release",
            "-H", "X-GitHub-Api-Version: 2022-11-28"
        )

        if ($UploadFile) {
            if (-not $ContentType) { $ContentType = "application/octet-stream" }
            $args += "-H", "Content-Type: $ContentType"
            $args += "--data-binary", "@$UploadFile"
        }

        if ($OutFile) {
            $args += "-o", $OutFile, "-w", "%{http_code}", $Url
            $code_text = & $curl_exe @args
            $code = [int]$code_text
        }
        else {
            $tmp = Join-Path $env:TEMP ("texttool-gh-" + [guid]::NewGuid().ToString() + ".json")
            $args += "-o", $tmp, "-w", "%{http_code}", $Url
            $code_text = & $curl_exe @args
            $code = [int]$code_text
        }

        if ($code -eq 502 -or $code -eq 503) {
            if ($attempt -lt $Retries) {
                Start-Sleep -Seconds (2 * $attempt)
                continue
            }
        }
        break
    }

    $body = $null
    if ($tmp -and (Test-Path -LiteralPath $tmp)) {
        $body = [IO.File]::ReadAllText($tmp, [Text.Encoding]::UTF8)
        Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
    }

    return @{
        Status = $code
        Body   = $body
    }
}

function Get-GithubReleaseByTag {
    param(
        [Parameter(Mandatory = $true)][string]$Repo,
        [Parameter(Mandatory = $true)][string]$Tag
    )
    $url = "https://api.github.com/repos/$Repo/releases/tags/$Tag"
    $res = Invoke-GithubHttp -Url $url
    if ($res.Status -ne 200) {
        throw "读取 release $Tag 失败 HTTP $($res.Status)"
    }
    return $res.Body | ConvertFrom-Json
}

function Get-GithubWorkflowRuns {
    param(
        [Parameter(Mandatory = $true)][string]$Repo,
        [Parameter(Mandatory = $true)][string]$Sha
    )
    $url = "https://api.github.com/repos/$Repo/actions/runs?head_sha=$Sha&per_page=20"
    $res = Invoke-GithubHttp -Url $url
    if ($res.Status -ne 200) {
        throw "读取 workflow runs 失败 HTTP $($res.Status)"
    }
    return $res.Body | ConvertFrom-Json
}

function Get-TextToolReleaseAssetNames {
    # 发布产物名单，也是这份名单的唯一来源：CI 的打包步骤、release.ps1 的等待清单、
    # publish.ps1 的签名清单都从这里取，别处不许再抄一份。
    # 命名必须与 TextTool.Core/SelfUpdater.GetGuiAssetName 和 GetCliAssetName 一致，
    # 否则客户端找得到的包和签名脚本管得到的包会对不上。
    param(
        [Parameter(Mandatory = $true)][string]$Raw
    )
    return @(
        "TextTool-GUI-$Raw-win-x64.zip",
        "TextTool-GUI-$Raw-linux-x64.zip",
        "TextTool-CLI-$Raw-win-x64.zip",
        "TextTool-CLI-$Raw-linux-x64.zip"
    )
}

function Get-TextToolMissingReleaseAssets {
    # 产物齐活判据：名单里每个 zip 都要在，且各自带一份 .sha256。
    # 返回缺失的名字（含 .sha256 后缀）；空数组表示齐活。缺一套就点名，不静默放过。
    param(
        [Parameter(Mandatory = $true)][string]$Raw,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]]$Present
    )
    $missing = @()
    foreach ($name in @(Get-TextToolReleaseAssetNames -Raw $Raw)) {
        if ($Present -notcontains $name) {
            $missing += $name
        }
        elseif ($Present -notcontains "$name.sha256") {
            $missing += "$name.sha256"
        }
    }
    return $missing
}

function Save-GithubReleaseAssets {
    param(
        [Parameter(Mandatory = $true)]$Release,
        [Parameter(Mandatory = $true)][string]$Raw,
        [Parameter(Mandatory = $true)][string]$DestDir
    )
    $prefixes = Get-TextToolReleaseAssetNames -Raw $Raw
    $saved = @()
    foreach ($asset in @($Release.assets)) {
        $name = [string]$asset.name
        # 尾部的 * 让 .sha256 / .sig 一起下来，它们跟 zip 同名。
        $matched = $false
        foreach ($prefix in $prefixes) {
            if ($name -like "$prefix*") { $matched = $true; break }
        }
        if (-not $matched) {
            continue
        }
        $dest = Join-Path $DestDir $name
        $res = Invoke-GithubHttp -Url $asset.url -OutFile $dest -Accept "application/octet-stream"
        if ($res.Status -ne 200) {
            throw "下载资产失败 $name HTTP $($res.Status)"
        }
        $saved += $dest
    }
    return $saved
}

function Remove-GithubReleaseAssetByName {
    param(
        [Parameter(Mandatory = $true)][string]$Repo,
        [Parameter(Mandatory = $true)]$Release,
        [Parameter(Mandatory = $true)][string]$Name
    )
    foreach ($asset in @($Release.assets)) {
        if ([string]$asset.name -eq $Name) {
            $url = "https://api.github.com/repos/$Repo/releases/assets/$($asset.id)"
            $res = Invoke-GithubHttp -Method "DELETE" -Url $url
            if ($res.Status -ne 204 -and $res.Status -ne 200) {
                throw "删除资产失败 $Name HTTP $($res.Status)"
            }
            return
        }
    }
}

function Add-GithubReleaseAsset {
    param(
        [Parameter(Mandatory = $true)][string]$Repo,
        [Parameter(Mandatory = $true)][long]$ReleaseId,
        [Parameter(Mandatory = $true)][string]$FilePath
    )
    $name = Split-Path -Leaf $FilePath
    $url = "https://uploads.github.com/repos/$Repo/releases/$ReleaseId/assets?name=$name"
    $res = Invoke-GithubHttp -Method "POST" -Url $url -UploadFile $FilePath -Accept "application/vnd.github+json"
    if ($res.Status -ne 201) {
        throw "上传资产失败 $name HTTP $($res.Status)"
    }
}

function Push-GitTagWithToken {
    param(
        [Parameter(Mandatory = $true)][string]$Tag
    )
    $token = Get-TextToolGithubToken
    $pair = "TwilightRainDev:$token"
    $b64 = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes($pair))
    git -c http.extraheader="Authorization: Basic $b64" push origin "refs/tags/$Tag"
    if ($LASTEXITCODE -ne 0) {
        throw "推 tag $Tag 失败"
    }
}

function Set-GithubReleaseBody {
    param(
        [Parameter(Mandatory = $true)][string]$Repo,
        [Parameter(Mandatory = $true)][long]$ReleaseId,
        [Parameter(Mandatory = $true)][string]$Body
    )
    $tmp = Join-Path $env:TEMP ("texttool-body-" + [guid]::NewGuid().ToString() + ".json")
    # 本机 Windows PowerShell 5.1 的 ConvertTo-Json 不转义非 ASCII（中文原字符落盘，实测），
    # 因此必须用无 BOM UTF-8 写入——换成默认编码或 ASCII 会损坏中文；curl 按字节上传。
    [IO.File]::WriteAllText($tmp, (@{ body = $Body } | ConvertTo-Json -Compress), (New-Object Text.UTF8Encoding($false)))
    try {
        $url = "https://api.github.com/repos/$Repo/releases/$ReleaseId"
        $res = Invoke-GithubHttp -Method "PATCH" -Url $url -UploadFile $tmp -ContentType "application/json"
        if ($res.Status -ne 200) {
            throw "更新 release 说明失败 HTTP $($res.Status)"
        }
    }
    finally {
        Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
    }
}
