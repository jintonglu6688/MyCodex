param([Parameter(Mandatory = $true)][string]$RequestPath)

$ErrorActionPreference = 'Stop'
$mutex = $null
$hasMutex = $false
$request = $null
$validated = $false
$directory = [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($RequestPath))
$log = Join-Path $directory 'update.log'

function Write-UpdateStatus([string]$Status) {
    [IO.File]::AppendAllText($log, ([DateTime]::UtcNow.ToString('o') + ' ' + $Status + "`r`n"), [Text.UTF8Encoding]::new($false))
}

function Get-TextHash([string]$Text) {
    $hash = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes($Text))).Replace('-', '') }
    finally { $hash.Dispose() }
}

function Test-ExactProcess([object]$Record) {
    if ($null -eq $Record -or $Record.ProcessId -le 0 -or $Record.StartTimeUtcTicks -le 0 -or $Record.UserSid -ne [Security.Principal.WindowsIdentity]::GetCurrent().User.Value) { throw 'update_owner_identity_invalid' }
    $process = Get-Process -Id $Record.ProcessId -ErrorAction SilentlyContinue
    if ($null -eq $process) { return $false }
    try {
        if ($process.HasExited) { return $false }
        if ($process.StartTime.ToUniversalTime().Ticks -ne $Record.StartTimeUtcTicks -or $process.MainModule.FileName -ine $Record.ExecutablePath) { return $false }
        $candidate = Get-CimInstance Win32_Process -Filter ('ProcessId = ' + [int]$Record.ProcessId)
        if ($null -eq $candidate) { return $false }
        $owner = Invoke-CimMethod -InputObject $candidate -MethodName GetOwnerSid
        if ($owner.ReturnValue -ne 0 -or $owner.Sid -ne $Record.UserSid) { throw 'update_owner_inspection_failed' }
        return $true
    } catch {
        $remaining = Get-Process -Id $Record.ProcessId -ErrorAction SilentlyContinue
        if ($null -eq $remaining) { return $false }
        $remaining.Dispose()
        throw
    } finally { $process.Dispose() }
}

try {
    if ([IO.Path]::GetFileName($RequestPath) -cne 'active.json' -or (Get-Item -LiteralPath $RequestPath).Length -gt 32768) { throw 'update_request_invalid' }
    $request = Get-Content -Raw -Encoding UTF8 -LiteralPath $RequestPath | ConvertFrom-Json
    if ($request.SchemaVersion -ne 1 -or $request.OperationId -cnotmatch '^[a-f0-9]{32}$' -or
        $request.MutexName -cnotmatch '^Global\\MyCodex\.Target\.[A-F0-9]{64}$' -or
        $request.ExpiresUtcTicks -le [DateTime]::UtcNow.Ticks -or $request.ExpiresUtcTicks -gt [DateTime]::UtcNow.AddMinutes(11).Ticks) { throw 'update_request_invalid' }
    $installation = [IO.Path]::GetFullPath($request.InstallationDirectory).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $expectedDirectory = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) ('MyCodex\update-coordination\' + (Get-TextHash $installation.ToUpperInvariant()))
    if ($directory -ine [IO.Path]::GetFullPath($expectedDirectory) -or
        [IO.Path]::GetDirectoryName($request.Parent.ExecutablePath) -ine $installation -or
        [IO.Path]::GetFileName($request.Parent.ExecutablePath) -ine 'MyCodex.exe' -or
        -not (Test-ExactProcess $request.Parent)) { throw 'update_installation_identity_invalid' }
    $validated = $true
    $prefix = Join-Path $directory $request.OperationId
    $cancel = $prefix + '.cancel'
    $commit = $prefix + '.commit'
    $guard = $prefix + '.guard.ps1'
    if (-not (Test-Path -LiteralPath $guard -PathType Leaf)) { throw 'update_resource_guard_missing' }
    . $guard -InstallRoot $installation -FunctionsOnly
    $current = Get-Process -Id $PID
    try {
        $request.Helper = [pscustomobject]@{
            ProcessId = $PID; ExecutablePath = $current.MainModule.FileName; StartTimeUtcTicks = $current.StartTime.ToUniversalTime().Ticks
            UserSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
        }
    } finally { $current.Dispose() }
    $temporary = $prefix + '.json.tmp'
    [IO.File]::WriteAllText($temporary, ($request | ConvertTo-Json -Depth 8 -Compress), [Text.UTF8Encoding]::new($false))
    [IO.File]::Replace($temporary, $RequestPath, $null)
    [IO.File]::WriteAllText(($prefix + '.ready'), $request.OperationId, [Text.UTF8Encoding]::new($false))
    Write-UpdateStatus 'Helper ready; waiting for normal target exit.'
    while (Test-ExactProcess $request.Parent) {
        if ((Test-Path -LiteralPath $cancel) -or [DateTime]::UtcNow.Ticks -gt $request.ExpiresUtcTicks) { throw 'update_exit_cancelled_or_timed_out' }
        Start-Sleep -Milliseconds 200
    }
    # Commit is written before the target closes; a crash during scheduling must not authorize installation.
    if (-not (Test-Path -LiteralPath $commit) -or (Test-Path -LiteralPath $cancel)) { throw 'update_exit_not_committed' }
    $mutex = [Threading.Mutex]::new($false, [string]$request.MutexName)
    try { $hasMutex = $mutex.WaitOne(30000) } catch [Threading.AbandonedMutexException] { $hasMutex = $true }
    if (-not $hasMutex) { throw 'update_coordinator_busy' }
    $drainDeadline = [DateTime]::UtcNow.AddSeconds(30)
    while ($true) {
        if (Test-Path -LiteralPath $cancel) { throw 'update_cancelled' }
        try { Assert-NoMyCodexResourceUsers -Root $installation; break }
        catch {
            if ([DateTime]::UtcNow -ge $drainDeadline) { throw }
            Start-Sleep -Milliseconds 200
        }
    }
    foreach ($root in @($request.SharedRoots)) {
        $records = Join-Path ([IO.Path]::GetFullPath($root)) 'targets\.instances'
        if (-not (Test-Path -LiteralPath $records)) { continue }
        foreach ($file in @(Get-ChildItem -LiteralPath $records -Filter 'MyCodex.Target.*.json' -File)) {
            if ($file.Length -gt 65536) { throw 'update_instance_record_invalid' }
            $record = Get-Content -Raw -Encoding UTF8 -LiteralPath $file.FullName | ConvertFrom-Json
            if ([IO.Path]::GetDirectoryName($record.ExecutablePath) -ieq $installation -and (Test-ExactProcess $record)) { throw 'update_target_still_running' }
        }
    }
    if ((Get-FileHash -LiteralPath $request.InstallerPath -Algorithm SHA256).Hash -ine $request.InstallerSha256) { throw 'update_installer_integrity_changed' }
    if (Test-Path -LiteralPath $cancel) { throw 'update_cancelled' }
    Write-UpdateStatus 'All resource users exited; starting installer.'
    # The installer is intentionally visible for the user to review and control.
    $installer = Start-Process -FilePath $request.InstallerPath -ArgumentList @('/MYCODEXDEFERRED=1', ('/DIR="' + $installation + '"')) -PassThru
    try { $installer.WaitForExit(); Write-UpdateStatus ('Installer finished with exit code ' + $installer.ExitCode + '.') }
    finally { $installer.Dispose() }
} catch {
    $code = if ($_.Exception.Message -cmatch '^update_[a-z_]+$') { $_.Exception.Message } else { 'update_helper_failed' }
    if ($_.Exception.Message -in @('mycodex_resource_still_in_use', 'resource_owner_identity_unavailable')) {
        $code = 'A client or shared runtime is still running or cannot be verified. No installer was started; exit it normally and retry.'
    }
    if ($validated) { Write-UpdateStatus $code } else { [Console]::Error.WriteLine($code) }
} finally {
    if ($validated -and $request -and (Test-Path -LiteralPath $RequestPath)) {
        try {
            $actual = Get-Content -Raw -Encoding UTF8 -LiteralPath $RequestPath | ConvertFrom-Json
            if ($actual.OperationId -ceq $request.OperationId) { Remove-Item -LiteralPath $RequestPath }
        } catch { }
    }
    if ($hasMutex) { $mutex.ReleaseMutex() }
    if ($mutex) { $mutex.Dispose() }
    if ($validated -and $prefix) {
        foreach ($suffix in @('.ps1', '.guard.ps1', '.ready', '.commit', '.cancel', '.json.tmp')) {
            $owned = $prefix + $suffix
            if (Test-Path -LiteralPath $owned -PathType Leaf) { Remove-Item -LiteralPath $owned }
        }
    }
}
