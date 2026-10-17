$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
. (Join-Path $repoRoot 'tools\gitcode\GitCodePublishing.ps1')

function Assert-True($Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

# A loopback HTTP server that answers each request by path and records the request headers.
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$listener.Start()
$baseUrl = 'http://127.0.0.1:{0}' -f $listener.LocalEndpoint.Port
$requests = [Collections.ArrayList]::Synchronized([Collections.ArrayList]::new())
$server = [PowerShell]::Create()
[void]$server.AddScript({
    param($Listener, $Requests)
    while ($true) {
        $client = $Listener.AcceptTcpClient()
        try {
            $stream = $client.GetStream()
            $reader = [IO.StreamReader]::new($stream, [Text.Encoding]::ASCII)
            $lines = @()
            while (-not [string]::IsNullOrEmpty(($line = $reader.ReadLine()))) { $lines += $line }
            [void]$Requests.Add($lines)
            $path = ($lines[0] -split ' ')[1]
            switch ($path) {
                '/range' { $head = "HTTP/1.1 206 Partial Content`r`nContent-Range: bytes 0-0/193250403`r`nContent-Length: 1`r`n"; $body = 'x' }
                '/ignored-range' { $head = "HTTP/1.1 200 OK`r`nContent-Length: 4096`r`n"; $body = 'partial body' }
                '/bad-range' { $head = "HTTP/1.1 206 Partial Content`r`nContent-Range: bytes 0-0/*`r`nContent-Length: 1`r`n"; $body = 'x' }
                default { $head = "HTTP/1.1 404 Not Found`r`nContent-Length: 0`r`n"; $body = '' }
            }
            $bytes = [Text.Encoding]::ASCII.GetBytes($head + "Connection: close`r`n`r`n" + $body)
            $stream.Write($bytes, 0, $bytes.Length)
            $stream.Flush()
        }
        catch { }
        finally { $client.Close() }
    }
}).AddArgument($listener).AddArgument($requests)
$serverRun = $server.BeginInvoke()

try {
    Assert-True ((Get-GitCodePublicDownloadSize -Url "$baseUrl/range") -eq 193250403) 'Total size was not read from Content-Range.'
    $first = @($requests[0])
    Assert-True ($first[0] -like 'GET /range *') 'Size check must use GET, not HEAD.'
    Assert-True (@($first | Where-Object { $_ -eq 'Range: bytes=0-0' }).Count -eq 1) 'Size check must request only the first byte.'

    Assert-True ((Get-GitCodePublicDownloadSize -Url "$baseUrl/ignored-range") -eq 4096) 'Content-Length fallback was not used when the range was ignored.'
    Assert-GitCodePublicDownloadSize -Url "$baseUrl/range" -ExpectedSize 193250403 -Name 'Installer'

    foreach ($case in @(
        @{ Path = '/range'; Size = 1; Expected = '*Installer size mismatch. Expected 1, got 193250403.*' },
        @{ Path = '/bad-range'; Size = 1; Expected = '*Installer size check failed.*Content-Range*' },
        @{ Path = '/missing'; Size = 1; Expected = '*Installer size check failed.*404*' })) {
        try {
            Assert-GitCodePublicDownloadSize -Url ($baseUrl + $case.Path) -ExpectedSize $case.Size -Name 'Installer'
            throw "No failure for $($case.Path)."
        }
        catch { Assert-True ($_.Exception.Message -like $case.Expected) ("Unexpected failure for $($case.Path): " + $_.Exception.Message) }
    }

    # Redirected native stderr must not abort the caller; only the exit code decides success.
    $output = & {
        $ErrorActionPreference = 'Stop'
        Invoke-GitCodeNativeCommand -FilePath 'cmd.exe' -Arguments @('/d', '/c', 'echo remote: Start Git Hooks Checking 1>&2 & exit /b 0')
        Invoke-GitCodeNativeCommand -FilePath 'cmd.exe' -Arguments @('/d', '/c', 'echo fatal: rejected 1>&2 & exit /b 3')
    } *>&1
    $codes = @($output | Where-Object { $_ -is [int] })
    $text = ($output | ForEach-Object { [string]$_ }) -join "`n"
    Assert-True ($codes.Count -eq 2 -and $codes[0] -eq 0 -and $codes[1] -eq 3) ('Unexpected native exit codes: ' + ($codes -join ','))
    Assert-True ($text -like '*remote: Start Git Hooks Checking*' -and $text -like '*fatal: rejected*') 'Native stderr was not echoed.'
    Write-Host 'GitCode publishing helper checks passed.'
}
finally {
    $listener.Stop()
    $server.Stop()
    $server.Dispose()
}
