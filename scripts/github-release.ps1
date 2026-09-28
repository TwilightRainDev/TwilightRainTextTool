# Shared GitHub REST helpers for TextTool release scripts.
# Dot-source only. Do not run as an entry point.

function Get-TextToolGithubToken {
    if (-not [string]::IsNullOrWhiteSpace($env:TEXTTOOL_GITHUB_TOKEN)) {
        return $env:TEXTTOOL_GITHUB_TOKEN.Trim()
    }
    $token_path = Join-Path "E:\work_zone\ApiKey" "GithubApiToken.txt"
    if (Test-Path -LiteralPath $token_path) {
        return ([IO.File]::ReadAllText($token_path)).Trim()
    }
    throw "未设置 TEXTTOOL_GITHUB_TOKEN，且默认凭据文件不存在"
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
            "-sS", "-X", $Method,
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

function Save-GithubReleaseAssets {
    param(
        [Parameter(Mandatory = $true)]$Release,
        [Parameter(Mandatory = $true)][string]$Raw,
        [Parameter(Mandatory = $true)][string]$DestDir
    )
    $saved = @()
    foreach ($asset in @($Release.assets)) {
        $name = [string]$asset.name
        if ($name -notlike "TextTool-*-$Raw-win-x64.zip*") {
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
