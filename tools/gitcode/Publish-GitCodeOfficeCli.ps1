param(
    [string]$Owner = "gcw_SpGZ48lW",

    [string]$Repo = "mycodex-updates",

    [string]$RepoUrl,

    [string]$Token = $env:GITCODE_TOKEN,

    [string]$GitHubApiUrl = "https://api.github.com/repos/iOfficeAI/OfficeCLI/releases/latest",

    [string]$WorkDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$gitCodeApiBase = "https://api.gitcode.com/api/v5"
$script:CachedGitCodeToken = $null

if ([string]::IsNullOrWhiteSpace($RepoUrl)) {
    $RepoUrl = "https://gitcode.com/$Owner/$Repo.git"
}

if ([string]::IsNullOrWhiteSpace($WorkDirectory)) {
    $WorkDirectory = Join-Path $repoRoot "artifacts\officecli-gitcode"
}

function Get-GitCodeToken {
    if (-not [string]::IsNullOrWhiteSpace($script:CachedGitCodeToken)) {
        return $script:CachedGitCodeToken
    }

    if (-not [string]::IsNullOrWhiteSpace($Token)) {
        $script:CachedGitCodeToken = $Token.Trim()
        return $script:CachedGitCodeToken
    }

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = "git"
    $psi.Arguments = "credential-manager get"
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.UseShellExecute = $false
    $psi.EnvironmentVariables["GCM_INTERACTIVE"] = "Never"
    $psi.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0"
    $process = [System.Diagnostics.Process]::Start($psi)
    $process.StandardInput.Write("protocol=https`nhost=gitcode.com`nusername=$Owner`n`n")
    $process.StandardInput.Close()
    if (-not $process.WaitForExit(15000)) {
        try {
            $process.Kill()
        }
        catch {
        }

        throw "Timed out while reading GitCode token from Git Credential Manager. Set `$env:GITCODE_TOKEN and retry."
    }

    $output = $process.StandardOutput.ReadToEnd()
    $errorOutput = $process.StandardError.ReadToEnd()
    if ($process.ExitCode -ne 0) {
        throw "Unable to read GitCode token from Git Credential Manager: $errorOutput"
    }

    $passwordLine = $output -split "`n" |
        Where-Object { $_ -like "password=*" } |
        Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($passwordLine)) {
        throw "GitCode token is missing. Set `$env:GITCODE_TOKEN or store credentials in Git Credential Manager."
    }

    $script:CachedGitCodeToken = $passwordLine.Substring(9).Trim()
    return $script:CachedGitCodeToken
}

function Invoke-GitCodeAuthenticatedGit {
    param([string[]]$Arguments)

    $tokenValue = Get-GitCodeToken
    $previousTerminalPrompt = $env:GIT_TERMINAL_PROMPT
    $previousGcmInteractive = $env:GCM_INTERACTIVE
    $previousAskPass = $env:GIT_ASKPASS
    $previousAskPassUsername = $env:GITCODE_ASKPASS_USERNAME
    $previousAskPassPassword = $env:GITCODE_ASKPASS_PASSWORD
    $askPassPath = $null

    try {
        $askPassPath = Join-Path ([System.IO.Path]::GetTempPath()) ("mycodex-gitcode-askpass-{0}.cmd" -f [System.Guid]::NewGuid().ToString("N"))
        Set-Content -LiteralPath $askPassPath -Encoding ASCII -Value @"
@echo off
echo %~1 | findstr /I "Username" >nul
if not errorlevel 1 (
  echo %GITCODE_ASKPASS_USERNAME%
  exit /b 0
)
echo %GITCODE_ASKPASS_PASSWORD%
"@
        $env:GIT_TERMINAL_PROMPT = "0"
        $env:GCM_INTERACTIVE = "Never"
        $env:GIT_ASKPASS = $askPassPath
        $env:GITCODE_ASKPASS_USERNAME = $Owner
        $env:GITCODE_ASKPASS_PASSWORD = $tokenValue

        $gitArgs = @("-c", "credential.helper=") + $Arguments
        & git @gitArgs
        if ($LASTEXITCODE -ne 0) {
            throw "GitCode git command failed: git $($Arguments -join ' ')"
        }
    }
    finally {
        if ($null -eq $previousTerminalPrompt) { Remove-Item Env:\GIT_TERMINAL_PROMPT -ErrorAction SilentlyContinue } else { $env:GIT_TERMINAL_PROMPT = $previousTerminalPrompt }
        if ($null -eq $previousGcmInteractive) { Remove-Item Env:\GCM_INTERACTIVE -ErrorAction SilentlyContinue } else { $env:GCM_INTERACTIVE = $previousGcmInteractive }
        if ($null -eq $previousAskPass) { Remove-Item Env:\GIT_ASKPASS -ErrorAction SilentlyContinue } else { $env:GIT_ASKPASS = $previousAskPass }
        if ($null -eq $previousAskPassUsername) { Remove-Item Env:\GITCODE_ASKPASS_USERNAME -ErrorAction SilentlyContinue } else { $env:GITCODE_ASKPASS_USERNAME = $previousAskPassUsername }
        if ($null -eq $previousAskPassPassword) { Remove-Item Env:\GITCODE_ASKPASS_PASSWORD -ErrorAction SilentlyContinue } else { $env:GITCODE_ASKPASS_PASSWORD = $previousAskPassPassword }
        if ($askPassPath -and (Test-Path -LiteralPath $askPassPath)) {
            Remove-Item -LiteralPath $askPassPath -Force
        }
    }
}

function Invoke-GitCodeApi {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Method,

        [Parameter(Mandatory = $true)]
        [string]$Path,

        [hashtable]$Body
    )

    $requestBody = @{}
    if ($Body -ne $null) {
        foreach ($key in $Body.Keys) {
            $requestBody[$key] = $Body[$key]
        }
    }

    $requestBody.access_token = Get-GitCodeToken
    return Invoke-RestMethod `
        -Method $Method `
        -Uri "$gitCodeApiBase/repos/$Owner/$Repo/$Path" `
        -Body $requestBody `
        -TimeoutSec 120
}

function Remove-RemoteTagIfExists {
    param([string]$TagName)

    $result = git ls-remote --tags $RepoUrl "refs/tags/$TagName"
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to query GitCode tag: $TagName"
    }

    if ([string]::IsNullOrWhiteSpace(($result | Out-String).Trim())) {
        return
    }

    Invoke-GitCodeAuthenticatedGit -Arguments @("push", $RepoUrl, ":refs/tags/$TagName")
}

function New-GitCodeRelease {
    param(
        [string]$TagName,
        [string]$Name,
        [string]$Body
    )

    [void](Invoke-GitCodeApi -Method Post -Path "releases" -Body @{
        tag_name = $TagName
        name = $Name
        body = $Body
        target_commitish = "main"
    })
}

function Send-GitCodeReleaseFile {
    param(
        [string]$TagName,
        [string]$Path
    )

    $resolvedPath = (Resolve-Path -LiteralPath $Path).Path
    $fileName = Split-Path $resolvedPath -Leaf
    $tokenValue = [uri]::EscapeDataString((Get-GitCodeToken))
    $encodedFileName = [uri]::EscapeDataString($fileName)
    $uploadInfo = Invoke-RestMethod `
        -Method Get `
        -Uri "$gitCodeApiBase/repos/$Owner/$Repo/releases/$TagName/upload_url?access_token=$tokenValue&file_name=$encodedFileName" `
        -TimeoutSec 120

    $headers = @{}
    foreach ($property in $uploadInfo.headers.PSObject.Properties) {
        $headers[$property.Name] = [string]$property.Value
    }

    [void](Invoke-WebRequest `
        -Method Put `
        -Uri $uploadInfo.url `
        -Headers $headers `
        -InFile $resolvedPath `
        -ContentType "application/octet-stream" `
        -TimeoutSec 900 `
        -UseBasicParsing)

    Write-Host "Uploaded Release asset: $TagName / $fileName"
}

function Get-RequiredAssetUrl {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Release,

        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    foreach ($asset in $Release.assets) {
        if ($asset.name -eq $Name -and -not [string]::IsNullOrWhiteSpace($asset.browser_download_url)) {
            return [string]$asset.browser_download_url
        }
    }

    throw "OfficeCLI release asset missing: $Name"
}

function Get-Sha256 {
    param([string]$Path)

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-ExpectedSha256 {
    param(
        [string]$ShaText,
        [string]$FileName
    )

    foreach ($line in ($ShaText -split "`r?`n")) {
        $trimmed = $line.Trim()
        if ($trimmed.EndsWith($FileName, [System.StringComparison]::OrdinalIgnoreCase)) {
            $parts = $trimmed -split "\s+"
            if ($parts.Length -gt 0 -and $parts[0].Length -ge 64) {
                return $parts[0].ToLowerInvariant()
            }
        }
    }

    throw "SHA256SUMS does not contain $FileName"
}

function Assert-Sha256 {
    param(
        [string]$Path,
        [string]$ExpectedSha256
    )

    $actual = Get-Sha256 -Path $Path
    if ($actual -ne $ExpectedSha256.ToLowerInvariant()) {
        throw "SHA256 mismatch for $Path. Expected $ExpectedSha256, got $actual."
    }
}

function Write-Utf8NoBom {
    param(
        [string]$Path,
        [string]$Content
    )

    $encoding = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllText($Path, $Content, $encoding)
}

function Download-File {
    param(
        [string]$Url,
        [string]$Path
    )

    Invoke-WebRequest -Uri $Url -OutFile $Path -TimeoutSec 900 -UseBasicParsing
}

$sourceRemotes = git remote
if ($sourceRemotes -contains "gitcode") {
    throw "Source repository still has a 'gitcode' remote. Remove it first to avoid publishing source code to GitCode."
}

New-Item -ItemType Directory -Path $WorkDirectory -Force | Out-Null

$release = Invoke-RestMethod -Uri $GitHubApiUrl -Headers @{ "User-Agent" = "MyCodex" } -TimeoutSec 120 -UseBasicParsing
if ([string]::IsNullOrWhiteSpace($release.tag_name)) {
    throw "Unable to read OfficeCLI release tag from GitHub response."
}

$tagName = [string]$release.tag_name
$version = $tagName.Trim()
if ($version.StartsWith("v", [System.StringComparison]::OrdinalIgnoreCase)) {
    $version = $version.Substring(1)
}

$releaseTag = "officecli-v$version"
$stableTag = "officecli-stable"
$x64Name = "officecli-win-x64.exe"
$arm64Name = "officecli-win-arm64.exe"
$shaName = "SHA256SUMS"
$skillName = "SKILL.md"

$x64Url = Get-RequiredAssetUrl -Release $release -Name $x64Name
$arm64Url = Get-RequiredAssetUrl -Release $release -Name $arm64Name
$shaUrl = Get-RequiredAssetUrl -Release $release -Name $shaName
$skillUrl = "https://raw.githubusercontent.com/iOfficeAI/OfficeCLI/$tagName/SKILL.md"

$x64Path = Join-Path $WorkDirectory $x64Name
$arm64Path = Join-Path $WorkDirectory $arm64Name
$shaPath = Join-Path $WorkDirectory $shaName
$skillPath = Join-Path $WorkDirectory $skillName
$manifestPath = Join-Path $WorkDirectory "officecli.json"

Download-File -Url $shaUrl -Path $shaPath
$shaText = Get-Content -LiteralPath $shaPath -Raw -Encoding UTF8

Download-File -Url $x64Url -Path $x64Path
Assert-Sha256 -Path $x64Path -ExpectedSha256 (Get-ExpectedSha256 -ShaText $shaText -FileName $x64Name)

Download-File -Url $arm64Url -Path $arm64Path
Assert-Sha256 -Path $arm64Path -ExpectedSha256 (Get-ExpectedSha256 -ShaText $shaText -FileName $arm64Name)

Download-File -Url $skillUrl -Path $skillPath
$skillSha256 = Get-Sha256 -Path $skillPath

Remove-RemoteTagIfExists -TagName $releaseTag
New-GitCodeRelease -TagName $releaseTag -Name "OfficeCLI $version" -Body "OfficeCLI mirror assets for MyCodex."
Send-GitCodeReleaseFile -TagName $releaseTag -Path $x64Path
Send-GitCodeReleaseFile -TagName $releaseTag -Path $arm64Path
Send-GitCodeReleaseFile -TagName $releaseTag -Path $skillPath

$assetBaseUrl = "$gitCodeApiBase/repos/$Owner/$Repo/releases/$releaseTag/attach_files"
$manifest = [ordered]@{
    version = $version
    tag = $tagName
    generatedAt = [DateTime]::UtcNow.ToString("o", [System.Globalization.CultureInfo]::InvariantCulture)
    assets = [ordered]@{
        "win-x64" = [ordered]@{
            fileName = $x64Name
            url = "$assetBaseUrl/$x64Name/download"
            sha256 = Get-Sha256 -Path $x64Path
        }
        "win-arm64" = [ordered]@{
            fileName = $arm64Name
            url = "$assetBaseUrl/$arm64Name/download"
            sha256 = Get-Sha256 -Path $arm64Path
        }
    }
    skill = [ordered]@{
        fileName = $skillName
        url = "$assetBaseUrl/$skillName/download"
        sha256 = $skillSha256
    }
}

Write-Utf8NoBom -Path $manifestPath -Content (($manifest | ConvertTo-Json -Depth 10) + [Environment]::NewLine)

Remove-RemoteTagIfExists -TagName $stableTag
New-GitCodeRelease -TagName $stableTag -Name "OfficeCLI Stable Manifest" -Body "Stable OfficeCLI mirror manifest for MyCodex."
Send-GitCodeReleaseFile -TagName $stableTag -Path $manifestPath

Write-Host "OfficeCLI GitCode mirror published:"
Write-Host "  Version: $version"
Write-Host "  Release: $releaseTag"
Write-Host "  Manifest: $gitCodeApiBase/repos/$Owner/$Repo/releases/$stableTag/attach_files/officecli.json/download"
