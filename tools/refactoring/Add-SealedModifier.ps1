# Add Sealed Modifier Script
# 为指定文件中的具体类添加 sealed 修饰符

param(
    [Parameter(Mandatory=$true)]
    [string]$FilePath,
    
    [switch]$WhatIf,
    
    [string[]]$ExcludeClasses = @()
)

if (-not (Test-Path $FilePath)) {
    Write-Error "File not found: $FilePath"
    exit 1
}

Write-Host "Processing: $FilePath" -ForegroundColor Cyan

$content = Get-Content $FilePath -Raw
$originalContent = $content
$modifications = 0

# 查找所有类声明
$classPattern = '(?m)^(\s*)(public|internal)(\s+)(class)(\s+)(\w+)'

$matches = [regex]::Matches($content, $classPattern)

foreach ($match in $matches) {
    $indent = $match.Groups[1].Value
    $visibility = $match.Groups[2].Value
    $space1 = $match.Groups[3].Value
    $classKeyword = $match.Groups[4].Value
    $space2 = $match.Groups[5].Value
    $className = $match.Groups[6].Value
    
    # 检查是否在排除列表中
    if ($ExcludeClasses -contains $className) {
        Write-Host "  Skipping excluded class: $className" -ForegroundColor Yellow
        continue
    }
    
    # 检查前面是否已经有 sealed 或 abstract
    $beforeMatch = $content.Substring(0, $match.Index)
    $lineStart = $beforeMatch.LastIndexOf("
") + 1
    $lineContent = $content.Substring($lineStart, $match.Index - $lineStart + $match.Length)
    
    if ($lineContent -match '\b(sealed|abstract)\b') {
        continue
    }
    
    # 添加 sealed
    $oldText = $match.Value
    $newText = "$indent$visibility sealed$space1$classKeyword$space2$className"
    
    $content = $content.Replace($oldText, $newText)
    $modifications++
    
    Write-Host "  + Added sealed to: $className" -ForegroundColor Green
}

if ($modifications -eq 0) {
    Write-Host "  No modifications needed" -ForegroundColor Gray
    exit 0
}

if ($WhatIf) {
    Write-Host "
[WhatIf] Would modify $modifications class(es)" -ForegroundColor Yellow
    Write-Host "
Preview of changes:" -ForegroundColor Cyan
    
    # 显示差异(简化版)
    $originalLines = $originalContent -split "
"
    $newLines = $content -split "
"
    
    for ($i = 0; $i -lt [Math]::Min($originalLines.Count, $newLines.Count); $i++) {
        if ($originalLines[$i] -ne $newLines[$i]) {
            Write-Host "Line $($i + 1):" -ForegroundColor Yellow
            Write-Host "  - $($originalLines[$i])" -ForegroundColor Red
            Write-Host "  + $($newLines[$i])" -ForegroundColor Green
        }
    }
} else {
    # 写入文件
    Set-Content -Path $FilePath -Value $content -NoNewline -Encoding UTF8
    Write-Host "
Modified $modifications class(es) in $FilePath" -ForegroundColor Green
}
