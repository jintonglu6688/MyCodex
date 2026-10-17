# Find Unsealed Classes Script
# 查找所有未使用 sealed 修饰符的具体类

param(
    [string]$ProjectPath = "src",
    [string]$OutputFile = "unsealed-classes.csv"
)

Write-Host "Scanning for unsealed classes in $ProjectPath..." -ForegroundColor Cyan

$results = @()
$totalFiles = 0
$scannedFiles = 0

# 获取所有 .cs 文件
$files = Get-ChildItem -Path $ProjectPath -Recurse -Filter *.cs | 
    Where-Object { $_.FullName -notmatch '\\obj\\' -and $_.FullName -notmatch '\\bin\\' }

$totalFiles = $files.Count

foreach ($file in $files) {
    $scannedFiles++
    Write-Progress -Activity "Scanning files" -Status "$scannedFiles of $totalFiles" -PercentComplete (($scannedFiles / $totalFiles) * 100)
    
    $content = Get-Content $file.FullName -Raw
    
    # 跳过接口、枚举、委托
    if ($content -match 'interface\s+\w+' -or 
        $content -match 'enum\s+\w+' -or 
        $content -match 'delegate\s+') {
        continue
    }
    
    # 查找类声明
    $classMatches = [regex]::Matches($content, '(?m)^\s*(public|internal)\s+(abstract\s+)?(sealed\s+)?class\s+(\w+)')
    
    foreach ($match in $classMatches) {
        $visibility = $match.Groups[1].Value
        $isAbstract = $match.Groups[2].Success
        $isSealed = $match.Groups[3].Success
        $className = $match.Groups[4].Value
        
        # 如果是具体类(非抽象)且未 sealed
        if (-not $isAbstract -and -not $isSealed) {
            $relativePath = $file.FullName.Replace((Get-Location).Path + "\", "")
            
            $results += [PSCustomObject]@{
                File = $relativePath
                Class = $className
                Visibility = $visibility
                LineNumber = ($content.Substring(0, $match.Index) -split "
").Count
            }
        }
    }
}

Write-Progress -Activity "Scanning files" -Completed

# 导出结果
$results | Export-Csv $OutputFile -NoTypeInformation -Encoding UTF8

# 统计
$byProject = $results | Group-Object { $_.File.Split('\')[0..1] -join '\' } | 
    Select-Object Name, Count | 
    Sort-Object Count -Descending

Write-Host "
Summary:" -ForegroundColor Green
Write-Host "Total files scanned: $totalFiles"
Write-Host "Unsealed classes found: $($results.Count)" -ForegroundColor Yellow
Write-Host "
By Project:" -ForegroundColor Green
$byProject | Format-Table -AutoSize

Write-Host "
Results saved to: $OutputFile" -ForegroundColor Cyan
Write-Host "
Top 10 files with most unsealed classes:" -ForegroundColor Green
$results | Group-Object File | 
    Select-Object Name, Count | 
    Sort-Object Count -Descending | 
    Select-Object -First 10 | 
    Format-Table -AutoSize
