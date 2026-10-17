# Shared helpers for the GitCode publishing scripts. Dot-source this file; it defines functions only.

function Invoke-GitCodeNativeCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,

        [string[]]$Arguments
    )

    # Windows PowerShell turns redirected native stderr (for example GitCode's
    # "remote: Start Git Hooks Checking") into terminating errors under Stop.
    # Echo every line and let the exit code decide success instead.
    $ErrorActionPreference = "Continue"
    & $FilePath @Arguments 2>&1 | ForEach-Object { Write-Host ([string]$_) }
    return $LASTEXITCODE
}

function Get-GitCodePublicDownloadSize {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Url
    )

    # GitCode answers HEAD on release attachments with 404, so request one byte
    # and read the total size from Content-Range without downloading the file.
    $request = [System.Net.HttpWebRequest]::Create($Url)
    $request.Method = "GET"
    $request.AddRange([long]0, [long]0)
    $request.Timeout = 120000
    $request.ReadWriteTimeout = 120000
    $response = $null
    try {
        $response = $request.GetResponse()
        $statusCode = [int]$response.StatusCode
        if ($statusCode -eq 206) {
            $contentRange = [string]$response.Headers["Content-Range"]
            if ($contentRange -match '^bytes\s+\d+-\d+/(\d+)$') {
                return [long]$Matches[1]
            }

            throw "Content-Range header did not include the total size: $contentRange"
        }

        if ($statusCode -eq 200 -and $response.ContentLength -ge 0) {
            return [long]$response.ContentLength
        }

        throw "Unexpected response status $statusCode without a usable size."
    }
    finally {
        # Abort first so a server that ignored the range does not drain the full body.
        $request.Abort()
        if ($null -ne $response) {
            $response.Close()
        }
    }
}

function Assert-GitCodePublicDownloadSize {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Url,

        [Parameter(Mandatory = $true)]
        [long]$ExpectedSize,

        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    try {
        $actualSize = Get-GitCodePublicDownloadSize -Url $Url
    }
    catch {
        throw "$Name size check failed. $($_.Exception.Message)"
    }

    if ($actualSize -ne $ExpectedSize) {
        throw "$Name size mismatch. Expected $ExpectedSize, got $actualSize."
    }
}
