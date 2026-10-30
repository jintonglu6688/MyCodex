# Shared by the build, installer, and release scripts. Dot-source it; it defines functions only.

function Get-AppVersion {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Version.props was not found: $Path"
    }

    $content = Get-Content -LiteralPath $Path -Raw
    $match = [regex]::Match($content, "<Version>([^<]+)</Version>")
    if (-not $match.Success) {
        throw "Unable to read <Version> from $Path"
    }

    return $match.Groups[1].Value.Trim()
}

# Only paths strictly inside Root count. Root itself, with or without a trailing separator, does not,
# so a cleanup can never delete the directory it is confined to.
function Test-IsPathUnderRoot {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Root
    )

    $resolvedPath = [System.IO.Path]::GetFullPath($Path).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    $resolvedRoot = [System.IO.Path]::GetFullPath($Root).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    $rootWithSeparator = $resolvedRoot + [System.IO.Path]::DirectorySeparatorChar

    return $resolvedPath -ne $resolvedRoot -and
        $resolvedPath.StartsWith($rootWithSeparator, [System.StringComparison]::OrdinalIgnoreCase)
}

function Remove-DirectorySafely {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$AllowedRoot
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    $resolvedPath = (Resolve-Path -LiteralPath $Path).Path
    $resolvedRoot = (Resolve-Path -LiteralPath $AllowedRoot).Path
    if (-not (Test-IsPathUnderRoot -Path $resolvedPath -Root $resolvedRoot)) {
        throw "Refusing to delete outside allowed root: $resolvedPath"
    }

    Remove-Item -LiteralPath $resolvedPath -Recurse -Force
}
