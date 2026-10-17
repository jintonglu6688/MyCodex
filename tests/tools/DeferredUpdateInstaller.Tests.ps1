$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$guard = Join-Path $repoRoot 'tools\installer\Test-MyCodexResourceUsers.ps1'
$helper = Join-Path $repoRoot 'tools\update\Invoke-DeferredMyCodexUpdate.ps1'
foreach ($script in @($guard, $helper)) {
    $parseTokens = $null
    $parseErrors = $null
    [Management.Automation.Language.Parser]::ParseFile($script, [ref]$parseTokens, [ref]$parseErrors) | Out-Null
    if ($parseErrors.Count -ne 0) { throw ('Update script syntax failed: ' + $parseErrors[0].Message) }
}
$fakeRoot = 'C:\fixture\MyCodex'
. $guard -InstallRoot $fakeRoot -FunctionsOnly
function Assert-GuardResult([string]$RelativePath, [bool]$Expected) {
    $actual = Test-MyCodexGuardedImage -RelativePath $RelativePath
    if ($actual -ne $Expected) { throw ('Unexpected resource guard result for ' + $RelativePath) }
}
Assert-GuardResult 'MyCodex.exe' $true
Assert-GuardResult 'MyCodex.Dev.exe' $true
Assert-GuardResult 'runtime\codex\v1\codex.exe' $true
Assert-GuardResult 'runtime\helper\node.exe' $true
Assert-GuardResult 'runtime\auth-core\mycodex-auth-host.exe' $false
Assert-GuardResult 'runtime\relay\mycodex-relay.exe' $false
Assert-NoMyCodexResourceUsers -Root $fakeRoot -LockedImages @()
try {
    Assert-NoMyCodexResourceUsers -Root $fakeRoot -LockedImages @('MyCodex.exe')
    throw 'A live client was accepted for installer mutation.'
} catch { if ($_.Exception.Message -cne 'mycodex_resource_still_in_use') { throw } }
# The real scan of a missing installation must succeed without consulting process identities.
Assert-NoMyCodexResourceUsers -Root $fakeRoot
$source = [IO.File]::ReadAllText($helper)
if ($source -match 'Stop-Process|taskkill|\.Kill\(') { throw 'The update helper must never force-stop clients.' }
if ($source.IndexOf('Assert-NoMyCodexResourceUsers') -gt $source.IndexOf('$installer = Start-Process')) { throw 'Resource inspection must precede installer launch.' }
if ($source.IndexOf('Get-FileHash') -gt $source.IndexOf('$installer = Start-Process')) { throw 'Installer integrity verification must precede launch.' }
Write-Output 'Deferred installer syntax and 12 offline resource-guard checks passed. No process or installer was started.'
