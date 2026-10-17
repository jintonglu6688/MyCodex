# Batch Add Sealed Modifier Script
# 批量为多个文件添加 sealed 修饰符

param(
    [Parameter(Mandatory=$true)]
    [string]$CsvFile,
    
    [switch]$WhatIf,
    
    [string[]]$ExcludeClasses = @()
)

if (-not (Test-Path $CsvFile)) {
    Write-Error "CSV file not found: $CsvFile"
    exit 1
}

Write-Host "Reading unsealed classes from: $CsvFile" -ForegroundColor Cyan

$classes = Import-Csv $CsvFile

$fileGroups = $classes | Group-Object File

Write-Host "Found $($classes.Count) unsealed classes in $($fileGroups.Count) files
" -ForegroundColor Yellow

$processedFiles = 0
$totalModifications = 0

foreach ($group in $fileGroups) {
    $processedFiles++
    $filePath = $group.Name
    
    Write-Host "[$processedFiles/$($fileGroups.Count)] Processing: $filePath" -ForegroundColor Cyan
    
    if (-not (Test-Path $filePath)) {
        Write-Warning "File not found: $filePath"
        continue
    }
    
    # 调用单文件处理脚本
    $params = @{
        FilePath = $filePath
        ExcludeClasses = $ExcludeClasses
    }
    
    if ($WhatIf) {
        $params.WhatIf = $true
    }
    
    & "$PSScriptRoot\Add-SealedModifier.ps1" @params
    
    $totalModifications += $group.Count
    Write-Host ""
}

Write-Host "
Summary:" -ForegroundColor Green
Write-Host "Files processed: $processedFiles"
Write-Host "Total modifications: $totalModifications"

if ($WhatIf) {
    Write-Host "
[WhatIf] No files were actually modified" -ForegroundColor Yellow
    Write-Host "Run without -WhatIf to apply changes" -ForegroundColor Cyan
} else {
    Write-Host "
Done! Remember to:" -ForegroundColor Cyan
    Write-Host "  1. Build the solution: dotnet build MyCodex.sln"
    Write-Host "  2. Run tests: dotnet test MyCodex.sln"
    Write-Host "  3. Review changes: git diff"
    Write-Host "  4. Commit: git commit -m 'refactor: add sealed modifier'"
}
