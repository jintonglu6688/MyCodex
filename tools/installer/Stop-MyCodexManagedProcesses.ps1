param(
    [Parameter(Mandatory = $true)]
    [string]$InstallRoot,
    [switch]$LegacyNodeOnly,
    [switch]$ForceClients,
    [string]$UninstallerPath
)

$ErrorActionPreference = 'Stop'
$script:stopFailed = $false

function Stop-VerifiedProcess {
    param([int]$ProcessId)

    try {
        Stop-Process -Id $ProcessId -Force -ErrorAction Stop
        Wait-Process -Id $ProcessId -Timeout 5 -ErrorAction SilentlyContinue
        if (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue) {
            $script:stopFailed = $true
        }
    } catch {
        $script:stopFailed = $true
    }
}

$root = [System.IO.Path]::GetFullPath($InstallRoot).TrimEnd('\')
$relayRuntime = [System.IO.Path]::GetFullPath((Join-Path $root 'runtime\relay\mycodex-relay.exe'))
$legacyRelayRuntime = [System.IO.Path]::GetFullPath((Join-Path $root 'relay\mycodex-relay.exe'))

try {
    . (Join-Path $PSScriptRoot 'Test-MyCodexResourceUsers.ps1') -InstallRoot $root -FunctionsOnly -UninstallerPath $UninstallerPath
    if ($ForceClients) {
        # Only Setup may request this after explicit consent, while blocking new startups.
        if ($LegacyNodeOnly -or -not (Test-Path -LiteralPath (Join-Path $root 'MyCodex.exe') -PathType Leaf) -or
            $root -eq [IO.Path]::GetPathRoot($root).TrimEnd('\')) { throw 'force_close_installation_invalid' }
        $gate = [Threading.Mutex]::new($false, (Get-MyCodexUpdateMutexName $root))
        try {
            try { $available = $gate.WaitOne(0) } catch [Threading.AbandonedMutexException] { $available = $true }
            if ($available) { $gate.ReleaseMutex(); throw 'force_close_installation_not_reserved' }
        } finally { $gate.Dispose() }
        $processes = [Collections.Generic.List[Diagnostics.Process]]::new()
        try {
            $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
            $self = [Diagnostics.Process]::GetCurrentProcess()
            try { $session = $self.SessionId } finally { $self.Dispose() }
            $runtimePrefix = (Join-Path $root 'runtime') + '\'
            $clientPaths = @((Join-Path $root 'MyCodex.exe'), (Join-Path $root 'MyCodex.Dev.exe'))
            foreach ($candidate in @(Get-CimInstance Win32_Process)) {
                $path = [string]$candidate.ExecutablePath
                if (-not $path) { $path = Resolve-MyCodexProcessPath -ProcessId $candidate.ProcessId }
                if (-not $path) { continue } # The final image guard still blocks unidentified owners.
                $path = [IO.Path]::GetFullPath($path)
                $runtime = $path.StartsWith($runtimePrefix, [StringComparison]::OrdinalIgnoreCase)
                # Authentication/Relay retain their existing maintenance protocols.
                if ($path -notin $clientPaths -and (-not $runtime -or
                    [IO.Path]::GetFileName($path) -in @('mycodex-auth-host.exe', 'mycodex-relay.exe'))) { continue }
                try { $process = [Diagnostics.Process]::GetProcessById($candidate.ProcessId) }
                catch [ArgumentException] { continue }
                $retained = $false
                try {
                    if ($process.HasExited) { continue }
                    $handle = $process.Handle
                    $actualPath = Resolve-MyCodexProcessPath -ProcessId $candidate.ProcessId
                    if ($process.HasExited) { continue }
                    if ($actualPath -ine $path) { throw 'force_close_identity_changed' }
                    $owner = Invoke-CimMethod -InputObject $candidate -MethodName GetOwnerSid
                    if ($owner.ReturnValue -ne 0 -or $owner.Sid -ne $sid -or $process.SessionId -ne $session) { throw 'force_close_wrong_owner' }
                    $processes.Add($process)
                    $retained = $true
                } finally { if (-not $retained) { $process.Dispose() } }
            }
            # Validate the complete scope before stopping any process; hold exact handles through Kill.
            foreach ($process in $processes) {
                try { if (-not $process.HasExited) { $process.Kill() } }
                catch [InvalidOperationException] { if (-not $process.HasExited) { throw } }
                if (-not $process.WaitForExit(5000)) { throw 'force_close_process_did_not_exit' }
            }
        } finally { foreach ($process in $processes) { $process.Dispose() } }
        Assert-NoMyCodexResourceUsers -Root $root -UninstallerPath $UninstallerPath
        exit 0
    }
    if ($LegacyNodeOnly) {
        # Restore the old installer's retired-Node cleanup only after every client
        # has saved and actually exited; these runtimes are no longer shipped.
        $gate = [Threading.Mutex]::new($false, (Get-MyCodexUpdateMutexName $root))
        try {
            try { $available = $gate.WaitOne(0) } catch [Threading.AbandonedMutexException] { $available = $true }
            if ($available) { $gate.ReleaseMutex(); throw 'legacy_node_installation_not_reserved' }
        } finally { $gate.Dispose() }
        Assert-NoMyCodexResourceUsers -Root $root -AllowLegacyNodeCleanup -UninstallerPath $UninstallerPath
        $nodes = [Collections.Generic.List[Diagnostics.Process]]::new()
        try {
            $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
            $self = [Diagnostics.Process]::GetCurrentProcess()
            try { $session = $self.SessionId } finally { $self.Dispose() }
            foreach ($candidate in @(Get-CimInstance Win32_Process -Filter "Name = 'node.exe'")) {
                $path = [string]$candidate.ExecutablePath
                if (-not $path) { $path = Resolve-MyCodexProcessPath -ProcessId $candidate.ProcessId }
                if (-not (Test-MyCodexLegacyNodePath $path $root)) { continue }
                try { $process = [Diagnostics.Process]::GetProcessById($candidate.ProcessId) }
                catch [ArgumentException] { continue }
                $retained = $false
                try {
                    if ($process.HasExited) { continue }
                    # Hold the actual process object through identity checks and Kill;
                    # never issue a later Stop-Process against a potentially reused PID.
                    $handle = $process.Handle
                    $actualPath = Resolve-MyCodexProcessPath -ProcessId $candidate.ProcessId
                    if ($process.HasExited) { continue }
                    if (-not (Test-MyCodexLegacyNodePath $actualPath $root) -or $actualPath -ine $path) { throw 'legacy_node_identity_changed' }
                    $owner = Invoke-CimMethod -InputObject $candidate -MethodName GetOwnerSid
                    if ($owner.ReturnValue -ne 0 -or $owner.Sid -ne $sid -or $process.SessionId -ne $session) { throw 'legacy_node_wrong_owner' }
                    $nodes.Add($process)
                    $retained = $true
                } finally { if (-not $retained) { $process.Dispose() } }
            }
            Assert-NoMyCodexResourceUsers -Root $root -AllowLegacyNodeCleanup -UninstallerPath $UninstallerPath
            foreach ($process in $nodes) {
                try { if (-not $process.HasExited) { $process.Kill() } }
                catch [InvalidOperationException] { if (-not $process.HasExited) { throw } }
                if (-not $process.WaitForExit(5000)) { throw 'legacy_node_did_not_exit' }
            }
        } finally { foreach ($process in $nodes) { $process.Dispose() } }
        # Strict recheck includes newly appeared Node processes too.
        Assert-NoMyCodexResourceUsers -Root $root -UninstallerPath $UninstallerPath
        exit 0
    }
    foreach ($candidate in @(Get-CimInstance Win32_Process -Filter "Name = 'mycodex-relay.exe'")) {
        $path = [string]$candidate.ExecutablePath
        if ([string]::IsNullOrWhiteSpace($path)) {
            $path = Resolve-MyCodexProcessPath -ProcessId $candidate.ProcessId
            if (-not $path) {
                try { $remaining = [Diagnostics.Process]::GetProcessById($candidate.ProcessId); $remaining.Dispose() }
                catch [ArgumentException] { continue }
                # An unreadable process can only be this installation's Relay if one of its images is in use.
                if (-not (Test-MyCodexImageLocked $relayRuntime) -and -not (Test-MyCodexImageLocked $legacyRelayRuntime)) { continue }
                throw 'managed_process_identity_unavailable'
            }
        }
        $executablePath = [System.IO.Path]::GetFullPath($path)

        if (-not [string]::Equals($executablePath, $relayRuntime, [System.StringComparison]::OrdinalIgnoreCase) -and
            -not [string]::Equals($executablePath, $legacyRelayRuntime, [System.StringComparison]::OrdinalIgnoreCase)) {
            continue
        }

        Stop-VerifiedProcess -ProcessId $candidate.ProcessId
    }
} catch {
    [Console]::Error.WriteLine('managed_process_inspection_failed')
    exit 2
}

if ($script:stopFailed) {
    exit 1
}

exit 0
