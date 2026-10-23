$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$guard = Join-Path $repoRoot 'tools\installer\Test-MyCodexResourceUsers.ps1'
$parseTokens = $null
$parseErrors = $null
[Management.Automation.Language.Parser]::ParseFile($guard, [ref]$parseTokens, [ref]$parseErrors) | Out-Null
if ($parseErrors.Count -ne 0) { throw ('Resource guard syntax failed: ' + $parseErrors[0].Message) }
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
Write-Output 'Resource guard syntax and offline image checks passed. No process or installer was started.'
