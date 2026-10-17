param(
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release",

    [string]$Owner = "gcw_SpGZ48lW",

    [string]$Repo = "mycodex-updates",

    [string]$RepoUrl,

    [string]$Token = $env:GITCODE_TOKEN,

    [string]$Channel = "stable",

    [string]$ReleaseNotesZhCn = "",

    [string]$ReleaseNotesEnUs = "",

    [string]$BundledCodexVersion = "latest",

    [string]$UniqueSuffix,

    [string]$StandardRuntimePath,

    [string]$StandardRuntimeVersion,

    [switch]$SkipInstallerBuild,

    [switch]$SkipStandardRuntimePublish,

    [switch]$SkipDistributionRepoPublish,

    [switch]$PrepareOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$versionPropsPath = Join-Path $repoRoot "Version.props"
$installerScript = Join-Path $repoRoot "tools\installer\build-installer.ps1"
$manifestScript = Join-Path $repoRoot "tools\update\New-MyCodexUpdateManifest.ps1"
$distributionScript = Join-Path $repoRoot "tools\gitcode\Publish-GitCodeDistributionRepo.ps1"
$runtimeScript = Join-Path $repoRoot "tools\gitcode\Publish-GitCodeRuntime.ps1"
$gitCodeApiBase = "https://api.gitcode.com/api/v5"
$script:CachedGitCodeToken = $null

if ([string]::IsNullOrWhiteSpace($RepoUrl)) {
    $RepoUrl = "https://gitcode.com/$Owner/$Repo.git"
}

if ([string]::IsNullOrWhiteSpace($Token)) {
    $Token = [Environment]::GetEnvironmentVariable("GITCODE_TOKEN", "User")
}

if ([string]::IsNullOrWhiteSpace($Token)) {
    $Token = [Environment]::GetEnvironmentVariable("GITCODE_TOKEN", "Machine")
}

function Get-AppVersion {
    param([string]$Path)

    $content = Get-Content -LiteralPath $Path -Raw
    $match = [regex]::Match($content, "<Version>([^<]+)</Version>")
    if (-not $match.Success) {
        throw "Unable to read <Version> from $Path"
    }

    return $match.Groups[1].Value.Trim()
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

function Test-RemoteTagExists {
    param([string]$TagName)

    $result = git ls-remote --tags $RepoUrl "refs/tags/$TagName"
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to query GitCode tag: $TagName"
    }

    return -not [string]::IsNullOrWhiteSpace(($result | Out-String).Trim())
}

function Remove-RemoteTagIfExists {
    param([string]$TagName)

    if (-not (Test-RemoteTagExists -TagName $TagName)) {
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

function Test-PublicDownloadSize {
    param(
        [string]$Url,
        [long]$ExpectedSize,
        [string]$Name
    )

    $response = Invoke-WebRequest -Uri $Url -Method Head -TimeoutSec 120 -UseBasicParsing
    $contentLength = @($response.Headers["Content-Length"]) |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($contentLength)) {
        throw "$Name size check failed. Content-Length header was not returned."
    }

    $actualSize = [long]$contentLength
    if ($actualSize -ne $ExpectedSize) {
        throw "$Name size mismatch. Expected $ExpectedSize, got $actualSize."
    }
}

$sourceRemotes = @(git remote)
if ($sourceRemotes -contains "gitcode") {
    throw "Source repository still has a 'gitcode' remote. Remove it first to avoid publishing source code to GitCode."
}

$version = Get-AppVersion -Path $versionPropsPath
$releaseTag = "app-v$version"
$stableTag = "updates-$Channel"
$packageBaseUrl = "$gitCodeApiBase/repos/$Owner/$Repo/releases/$releaseTag/attach_files/"
$installerPath = Join-Path $repoRoot ("artifacts\installer\MyCodex_v" + $version + "_Setup.exe")

if ([string]::IsNullOrWhiteSpace($UniqueSuffix)) {
    $UniqueSuffix = [DateTime]::UtcNow.ToString("yyyyMMddHHmmss")
}

if (-not $SkipInstallerBuild) {
    & $installerScript `
        -Configuration $Configuration `
        -BundledCodexVersion $BundledCodexVersion
    if ($LASTEXITCODE -ne 0) {
        throw "Installer build failed."
    }
}

if (-not (Test-Path -LiteralPath $installerPath)) {
    throw "Installer was not found: $installerPath"
}

$installerDirectory = Split-Path -Parent $installerPath
. (Join-Path $repoRoot 'tools\authentication-center\Test-AuthenticationHostPackage.ps1')
Test-AuthenticationInstallerReceipt -InstallerPath $installerPath
$uniqueInstallerName = "MyCodex_v$version`_Setup_$UniqueSuffix.exe"
$uniqueInstallerPath = Join-Path $installerDirectory $uniqueInstallerName
Copy-Item -LiteralPath $installerPath -Destination $uniqueInstallerPath -Force
Copy-Item -LiteralPath ($installerPath + '.auth.json') -Destination ($uniqueInstallerPath + '.auth.json') -Force

if (-not $PrepareOnly -and -not $SkipStandardRuntimePublish -and
    [string]::IsNullOrWhiteSpace($StandardRuntimePath) -and
    [string]::IsNullOrWhiteSpace($StandardRuntimeVersion)) {
    $runtimePublishArguments = @{
        Owner = $Owner
        Repo = $Repo
        RepoUrl = $RepoUrl
        Token = Get-GitCodeToken
    }
    if (-not [string]::IsNullOrWhiteSpace($BundledCodexVersion) -and
        -not [string]::Equals($BundledCodexVersion.Trim(), "latest", [StringComparison]::OrdinalIgnoreCase)) {
        $runtimePublishArguments.Version = $BundledCodexVersion.Trim()
    }

    & $runtimeScript @runtimePublishArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Standard runtime publish failed."
    }
}

$manifestPath = Join-Path $repoRoot "updates\$Channel.json"
& $manifestScript `
    -Version $version `
    -InstallerPath $uniqueInstallerPath `
    -PackageBaseUrl $packageBaseUrl `
    -Channel $Channel `
    -OutputPath $manifestPath `
    -ReleaseNotesZhCn $ReleaseNotesZhCn `
    -ReleaseNotesEnUs $ReleaseNotesEnUs `
    -StandardRuntimePath $StandardRuntimePath `
    -StandardRuntimeVersion $StandardRuntimeVersion `
    -AssetUrlSuffix "/download" `
    -PreserveExistingRuntimes `
    -EscapeNonAscii
if ($LASTEXITCODE -ne 0) {
    throw "Manifest generation failed."
}

if ($PrepareOnly) {
    Write-Host ""
    Write-Host "Release prepared without publishing:"
    Write-Host "  Version:   $version"
    Write-Host "  Installer: $uniqueInstallerPath"
    Write-Host "  Manifest:  $manifestPath"
    return
}

$gitCodeTokenForPublish = Get-GitCodeToken

if (-not $SkipDistributionRepoPublish) {
    & $distributionScript `
        -RepoUrl $RepoUrl `
        -CommitMessage "Publish MyCodex $version $Channel update manifest" `
        -GitUsername $Owner `
        -GitPassword $gitCodeTokenForPublish
    if ($LASTEXITCODE -ne 0) {
        throw "Distribution repository publish failed."
    }
}

Remove-RemoteTagIfExists -TagName $releaseTag
Remove-RemoteTagIfExists -TagName $stableTag

$releaseBody = "$ReleaseNotesZhCn`n`n$ReleaseNotesEnUs".Trim()
New-GitCodeRelease -TagName $releaseTag -Name "MyCodex $version" -Body $releaseBody
Send-GitCodeReleaseFile -TagName $releaseTag -Path $uniqueInstallerPath

New-GitCodeRelease -TagName $stableTag -Name "Stable Update Manifest" -Body "Stable manifest pointer for MyCodex update checks."
Send-GitCodeReleaseFile -TagName $stableTag -Path $manifestPath

$manifestUrl = "$gitCodeApiBase/repos/$Owner/$Repo/releases/$stableTag/attach_files/$Channel.json/download"
$manifest = Invoke-RestMethod -Uri $manifestUrl -TimeoutSec 120
if ($manifest.app.version -ne $version) {
    throw "Published manifest version mismatch. Expected $version, got $($manifest.app.version)."
}

$assetUrl = "$packageBaseUrl$($manifest.app.asset.url)"
Test-PublicDownloadSize -Url $assetUrl -ExpectedSize ([long]$manifest.app.asset.size) -Name "Installer"

Write-Host ""
Write-Host "GitCode release published:"
Write-Host "  Version:  $version"
Write-Host "  Manifest: $manifestUrl"
Write-Host "  Asset:    $assetUrl"
