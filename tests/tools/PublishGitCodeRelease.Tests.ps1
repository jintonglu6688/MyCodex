$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))

function Assert-True($Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

# Runs the real release script against a fixture: every GitCode call is replaced by an event
# recorder, so the test checks what is published and in which order, offline.
function Invoke-ReleaseFixture([bool]$FailInstallerUpload) {
    $fixtureRoot = Join-Path $repoRoot ('temp\gitcode-release-test-' + [Guid]::NewGuid().ToString('N'))
    $global:gitCodeReleaseFixture = @{ Events = [Collections.Generic.List[string]]::new(); FailInstallerUpload = $FailInstallerUpload }
    try {
        foreach ($relative in @('tools\gitcode', 'tools\build', 'tools\update', 'tools\authentication-center', 'artifacts\installer', 'updates')) {
            New-Item -ItemType Directory -Path (Join-Path $fixtureRoot $relative) -Force | Out-Null
        }
        foreach ($relative in @('tools\gitcode\Publish-GitCodeRelease.ps1', 'tools\gitcode\GitCodePublishing.ps1', 'tools\build\ScriptHelpers.ps1')) {
            Copy-Item -LiteralPath (Join-Path $repoRoot $relative) -Destination (Join-Path $fixtureRoot $relative)
        }
        Set-Content -LiteralPath (Join-Path $fixtureRoot 'Version.props') -Encoding ASCII -Value '<Project><PropertyGroup><Version>9.9.9</Version></PropertyGroup></Project>'
        Set-Content -LiteralPath (Join-Path $fixtureRoot 'artifacts\installer\MyCodex_v9.9.9_Setup.exe') -Encoding ASCII -Value 'fixture installer'
        Set-Content -LiteralPath (Join-Path $fixtureRoot 'artifacts\installer\MyCodex_v9.9.9_Setup.exe.auth.json') -Encoding ASCII -Value '{}'
        Set-Content -LiteralPath (Join-Path $fixtureRoot 'tools\authentication-center\Test-AuthenticationHostPackage.ps1') -Encoding ASCII -Value 'function Test-AuthenticationInstallerReceipt { param($InstallerPath) }'
        Set-Content -LiteralPath (Join-Path $fixtureRoot 'tools\gitcode\Publish-GitCodeDistributionRepo.ps1') -Encoding ASCII -Value @'
param($RepoUrl, $CommitMessage, $GitUsername, $GitPassword)
$global:gitCodeReleaseFixture.Events.Add('distribution')
$global:LASTEXITCODE = 0
'@
        Set-Content -LiteralPath (Join-Path $fixtureRoot 'tools\update\New-MyCodexUpdateManifest.ps1') -Encoding ASCII -Value @'
param($Version, $InstallerPath, $PackageBaseUrl, $Channel, $OutputPath, $ReleaseNotesZhCn, $ReleaseNotesEnUs,
    $StandardRuntimePath, $StandardRuntimeVersion, $AssetUrlSuffix, [switch]$PreserveExistingRuntimes, [switch]$EscapeNonAscii)
$name = [IO.Path]::GetFileName($InstallerPath)
$manifest = @{ app = @{ version = $Version; asset = @{ url = "$name/download"; fileName = $name; size = (Get-Item -LiteralPath $InstallerPath).Length } } }
[IO.File]::WriteAllText($OutputPath, ($manifest | ConvertTo-Json -Depth 5))
$global:LASTEXITCODE = 0
'@
        # Later definitions win when the script dot-sources the helpers.
        Add-Content -LiteralPath (Join-Path $fixtureRoot 'tools\gitcode\GitCodePublishing.ps1') -Encoding ASCII -Value @'
function Get-GitCodeToken { 'fixture-token' }
function Remove-RemoteTagIfExists { param([string]$TagName) $global:gitCodeReleaseFixture.Events.Add("remove $TagName") }
function New-GitCodeRelease { param([string]$TagName, [string]$Name, [string]$Body) $global:gitCodeReleaseFixture.Events.Add("create $TagName") }
function Send-GitCodeReleaseFile {
    param([string]$TagName, [string]$Path)
    $name = [IO.Path]::GetFileName($Path)
    if ($global:gitCodeReleaseFixture.FailInstallerUpload -and $name.EndsWith('.exe')) { throw 'Injected installer upload failure' }
    $global:gitCodeReleaseFixture.Events.Add("upload $TagName/$name")
}
function Assert-GitCodePublicDownloadSize {
    param([string]$Url, [long]$ExpectedSize, [string]$Name)
    $global:gitCodeReleaseFixture.Events.Add("verify $Name")
}
'@

        Push-Location $fixtureRoot
        try {
            & (Join-Path $fixtureRoot 'tools\gitcode\Publish-GitCodeRelease.ps1') -Token 'fixture-token' -SkipInstallerBuild `
                -SkipStandardRuntimePublish -UniqueSuffix 'fixture' -ReleaseNotesZhCn 'zh' -ReleaseNotesEnUs 'en' | Out-Null
            return $null
        }
        catch { return $_.Exception.Message }
        finally { Pop-Location }
    }
    finally {
        if (Test-Path -LiteralPath $fixtureRoot) { Remove-Item -LiteralPath $fixtureRoot -Recurse -Force }
    }
}

# The script checks `git remote` for a gitcode remote; the fixture has none.
function git { param([Parameter(ValueFromRemainingArguments = $true)]$Arguments) 'origin'; $global:LASTEXITCODE = 0 }
function Invoke-RestMethod {
    param([string]$Uri, [string]$Method, $Body, $TimeoutSec)
    if ($Uri -like '*/releases/updates-stable/attach_files/stable.json/download') {
        # Serve the manifest this run uploaded; the fixture runs from its own root.
        return Get-Content -LiteralPath (Join-Path (Get-Location) 'updates\stable.json') -Raw | ConvertFrom-Json
    }
    throw "Unexpected fixture API request: $Uri"
}

$failure = Invoke-ReleaseFixture -FailInstallerUpload $false
Assert-True ($null -eq $failure) "Fixture release failed: $failure"
$events = @($global:gitCodeReleaseFixture.Events)
$expected = @('remove app-v9.9.9', 'create app-v9.9.9', 'upload app-v9.9.9/MyCodex_v9.9.9_Setup_fixture.exe', 'verify Installer',
    'distribution', 'remove updates-stable', 'create updates-stable', 'upload updates-stable/stable.json')
Assert-True (($events -join '|') -eq ($expected -join '|')) ('Unexpected publish order: ' + ($events -join ' > '))

# A failed installer upload must leave the live pointer and the distribution repository alone.
$failure = Invoke-ReleaseFixture -FailInstallerUpload $true
Assert-True ($failure -like '*Injected installer upload failure*') "Installer upload failure was not reported: $failure"
$events = @($global:gitCodeReleaseFixture.Events)
Assert-True (-not ($events -contains 'remove updates-stable')) ('The stable pointer was removed before the installer existed: ' + ($events -join ' > '))
Assert-True (-not ($events -contains 'distribution')) ('The distribution repository was pushed before the installer existed: ' + ($events -join ' > '))

Remove-Item Function:\git, Function:\Invoke-RestMethod
Write-Host 'GitCode release publish order checks passed.'
