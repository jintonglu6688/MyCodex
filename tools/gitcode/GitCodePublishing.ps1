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

# The GitCode API, release and tag helpers below read the calling script's $Owner, $Repo, $RepoUrl, $Token,
# $gitCodeApiBase and $script:CachedGitCodeToken.
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
