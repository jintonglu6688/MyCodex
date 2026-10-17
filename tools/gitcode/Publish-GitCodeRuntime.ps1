param(
    [ValidateSet("Bundled")]
    [string]$Source = "Bundled",

    [string]$Version,

    [string]$Owner = "gcw_SpGZ48lW",

    [string]$Repo = "mycodex-updates",

    [string]$RepoUrl,

    [string]$Token = $env:GITCODE_TOKEN,

    [string]$RegistryBaseUrl = "https://registry.npmjs.org",

    [ValidateSet("x86_64-pc-windows-msvc", "aarch64-pc-windows-msvc")]
    [string[]]$TargetTriple = @("x86_64-pc-windows-msvc"),

    [string]$ManifestPath,

    [switch]$SkipDistributionRepoPublish
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$gitCodeApiBase = "https://api.gitcode.com/api/v5"
$distributionScript = Join-Path $repoRoot "tools\gitcode\Publish-GitCodeDistributionRepo.ps1"
$prepareScript = Join-Path $repoRoot "tools\runtime\prepare-bundled-codex-runtime.ps1"
. (Join-Path $repoRoot "tools\gitcode\GitCodePublishing.ps1")

if ([string]::IsNullOrWhiteSpace($RepoUrl)) {
    $RepoUrl = "https://gitcode.com/$Owner/$Repo.git"
}

if ([string]::IsNullOrWhiteSpace($ManifestPath)) {
    $ManifestPath = Join-Path $repoRoot "updates\stable.json"
}

function Get-GitCodeToken {
    if (-not [string]::IsNullOrWhiteSpace($Token)) {
        return $Token.Trim()
    }

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = "git"
    $psi.Arguments = "credential-manager get"
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.UseShellExecute = $false
    $process = [System.Diagnostics.Process]::Start($psi)
    $process.StandardInput.Write("protocol=https`nhost=gitcode.com`nusername=$Owner`n`n")
    $process.StandardInput.Close()
    $output = $process.StandardOutput.ReadToEnd()
    $errorOutput = $process.StandardError.ReadToEnd()
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) {
        throw "Unable to read GitCode token from Git Credential Manager: $errorOutput"
    }

    $passwordLine = $output -split "`n" |
        Where-Object { $_ -like "password=*" } |
        Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($passwordLine)) {
        throw "GitCode token is missing. Set `$env:GITCODE_TOKEN or store credentials in Git Credential Manager."
    }

    return $passwordLine.Substring(9).Trim()
}

function Invoke-GitCodeGit {
    param([string[]]$Arguments)

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
        $env:GITCODE_ASKPASS_PASSWORD = Get-GitCodeToken

        $gitArgs = @("-c", "credential.helper=") + $Arguments
        $exitCode = Invoke-GitCodeNativeCommand -FilePath "git" -Arguments $gitArgs
        if ($exitCode -ne 0) {
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

    Invoke-GitCodeGit -Arguments @("push", $RepoUrl, ":refs/tags/$TagName")
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

function ConvertTo-AsciiJson {
    param([string]$Json)

    $builder = [System.Text.StringBuilder]::new($Json.Length)
    foreach ($character in $Json.ToCharArray()) {
        $code = [int][char]$character
        if ($code -le 127) {
            [void]$builder.Append($character)
        }
        else {
            [void]$builder.Append(("\u{0:x4}" -f $code))
        }
    }

    return $builder.ToString()
}

function Write-Manifest {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Manifest,

        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $json = ConvertTo-AsciiJson -Json ($Manifest | ConvertTo-Json -Depth 12)
    $encoding = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllText($Path, $json.Replace("`r`n", "`n") + "`n", $encoding)
}

function Add-OrUpdateRuntimePackage {
    param(
        [object]$Manifest,
        [string]$RuntimeSource,
        [string]$RuntimeVersion,
        [string]$TargetTriple,
        [object]$Package,
        [string]$ReleaseTag
    )

    if ($null -eq $Manifest.runtimes) {
        $Manifest | Add-Member -NotePropertyName runtimes -NotePropertyValue @()
    }

    $items = @($Manifest.runtimes)
    $match = $items |
        Where-Object { $_.source -eq $RuntimeSource -and $_.targetTriple -eq $TargetTriple } |
        Select-Object -First 1
    if ($null -eq $match) {
        $match = [pscustomobject]@{
            source = $RuntimeSource
            version = $RuntimeVersion
            targetTriple = $TargetTriple
            packageFormat = "npm-tgz"
            asset = $null
        }
        $Manifest.runtimes = @($items + $match)
    }

    $match.version = $RuntimeVersion
    $match.packageFormat = "npm-tgz"
    $asset = [pscustomobject]@{
        url = "$gitCodeApiBase/repos/$Owner/$Repo/releases/$ReleaseTag/attach_files/$($Package.FileName)/download"
        fileName = $Package.FileName
        sha256 = $Package.Sha256
        size = $Package.Size
    }
    if ($match.PSObject.Properties.Name -contains "asset") {
        $match.asset = $asset
    }
    else {
        $match | Add-Member -NotePropertyName asset -NotePropertyValue $asset
    }
}

$sourceRemotes = @(git -C $repoRoot remote)
if ($sourceRemotes -contains "gitcode") {
    throw "Source repository still has a 'gitcode' remote. Remove it first to avoid publishing source code to GitCode."
}

# Read credentials and the existing manifest before updating local files.
$Token = Get-GitCodeToken
$manifest = Get-Content -LiteralPath $ManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$version = if ([string]::IsNullOrWhiteSpace($Version)) { 'latest' } else { $Version.Trim() }
$targetTriples = @($TargetTriple | Select-Object -Unique)
if ($targetTriples.Count -eq 0) { throw 'At least one runtime architecture is required.' }
$runtimeArtifactDirectory = Join-Path $repoRoot "artifacts\runtime\$Source"

$packages = @{}
for ($index = 0; $index -lt $targetTriples.Count; $index++) {
    $targetTripleKey = [string]$targetTriples[$index]
    $package = & $prepareScript `
        -Version $version `
        -TargetTriple $targetTripleKey `
        -RegistryBaseUrl $RegistryBaseUrl `
        -DestinationRoot $repoRoot `
        -ArchiveDirectory $runtimeArtifactDirectory `
        -PruneOldVersions:($index -eq $targetTriples.Count - 1)
    # Resolve latest once; all architectures and the uploaded archives use that same version.
    $version = $package.Version
    $packages[$targetTripleKey] = $package
}
$releaseTag = "runtime-standard-$version"

Remove-RemoteTagIfExists -TagName $releaseTag
New-GitCodeRelease `
    -TagName $releaseTag `
    -Name "$Source runtime $version" `
    -Body "MyCodex $Source runtime package $version."

foreach ($packagePath in @($packages.Values | ForEach-Object { $_.Path } | Select-Object -Unique)) {
    Send-GitCodeReleaseFile -TagName $releaseTag -Path $packagePath
}

$manifest.generatedAt = [DateTimeOffset]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
foreach ($targetTriple in $targetTriples) {
    $targetTripleKey = [string]$targetTriple
    if (-not $packages.ContainsKey($targetTripleKey) -or $null -eq $packages[$targetTripleKey]) {
        throw "Runtime package was not prepared for $targetTripleKey."
    }

    Add-OrUpdateRuntimePackage `
        -Manifest $manifest `
        -RuntimeSource $Source `
        -RuntimeVersion $version `
        -TargetTriple $targetTripleKey `
        -Package ($packages[$targetTripleKey]) `
        -ReleaseTag $releaseTag
}
Write-Manifest -Manifest $manifest -Path $ManifestPath

if (-not $SkipDistributionRepoPublish) {
    & $distributionScript `
        -RepoUrl $RepoUrl `
        -CommitMessage "Publish MyCodex $Source runtime $version" `
        -GitUsername $Owner `
        -GitPassword (Get-GitCodeToken)
    if ($LASTEXITCODE -ne 0) {
        throw "Distribution repository publish failed."
    }
}

Remove-RemoteTagIfExists -TagName "updates-stable"
New-GitCodeRelease `
    -TagName "updates-stable" `
    -Name "Stable Update Manifest" `
    -Body "Stable manifest pointer for MyCodex update checks."
Send-GitCodeReleaseFile -TagName "updates-stable" -Path $ManifestPath

foreach ($targetTriple in $targetTriples) {
    $package = $packages[[string]$targetTriple]
    $url = "$gitCodeApiBase/repos/$Owner/$Repo/releases/$releaseTag/attach_files/$($package.FileName)/download"
    Assert-GitCodePublicDownloadSize -Url $url -ExpectedSize ([long]$package.Size) -Name "Runtime package $targetTriple"
}

Write-Host ""
Write-Host "GitCode runtime published:"
Write-Host "  Source:  $Source"
Write-Host "  Version: $version"
Write-Host "  Release: $releaseTag"
