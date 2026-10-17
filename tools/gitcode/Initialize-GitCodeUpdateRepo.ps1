param(
    [Parameter(Mandatory = $true)]
    [string]$RepoName,

    [Parameter(Mandatory = $true)]
    [string]$RepoPath,

    [string]$Description = "Public update distribution repository for MyCodex",

    [string]$Token = $env:GITCODE_TOKEN,

    [switch]$AddRemote,

    [string]$RemoteName = "gitcode"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($Token)) {
    throw "GitCode token is missing. Set `$env:GITCODE_TOKEN first."
}

$apiBase = "https://api.gitcode.com/api/v5"
$createRepoUri = "$apiBase/user/repos"

$body = @{
    name = $RepoName
    path = $RepoPath
    description = $Description
    private = "false"
    has_issues = "true"
    has_wiki = "false"
    auto_init = "true"
    default_branch = "main"
}

Write-Host "Creating public GitCode repository: $RepoPath"
$repo = Invoke-RestMethod `
    -Method Post `
    -Uri $createRepoUri `
    -Headers @{ "PRIVATE-TOKEN" = $Token } `
    -ContentType "application/x-www-form-urlencoded" `
    -Body $body

$fullName = $repo.full_name
if ([string]::IsNullOrWhiteSpace($fullName)) {
    if ($repo.namespace -and $repo.namespace.path) {
        $fullName = "$($repo.namespace.path)/$RepoPath"
    }
}

if ([string]::IsNullOrWhiteSpace($fullName)) {
    throw "Repository was created but the response did not include full_name."
}

$remoteUrl = "https://gitcode.com/$fullName.git"
Write-Host "Repository: https://gitcode.com/$fullName"

if ($AddRemote) {
    Write-Warning "Do not push the source repository to GitCode. Use tools\gitcode\Publish-GitCodeDistributionRepo.ps1 for distribution content."

    $existingRemote = git remote get-url $RemoteName 2>$null
    if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($existingRemote)) {
        git remote set-url $RemoteName $remoteUrl
    }
    else {
        git remote add $RemoteName $remoteUrl
    }

    if ($LASTEXITCODE -ne 0) {
        throw "Failed to configure git remote '$RemoteName'."
    }

    Write-Host "Git remote '$RemoteName' => $remoteUrl"
}

Write-Host ""
Write-Host "Next:"
Write-Host "  .\tools\gitcode\Publish-GitCodeDistributionRepo.ps1 -RepoUrl $remoteUrl"
