# Analyze Code Metrics Script
# 分析代码度量指标

param(
    [string]$ProjectPath = "src",
    [string]$OutputFile = "code-metrics.csv"
)

Write-Host "Analyzing code metrics in $ProjectPath..." -ForegroundColor Cyan

$results = @()

Get-ChildItem -Path $ProjectPath -Recurse -Filter *.cs | 
    Where-Object { $_.FullName -notmatch '\\obj\\' -and $_.FullName -notmatch '\\bin\\' } |
    ForEach-Object {
        $file = $_
        $content = Get-Content $file.FullName -Raw
        $lines = (Get-Content $file.FullName).Count
        
        # 计算方法数
        $methodCount = ([regex]::Matches($content, '\b(public|private|protected|internal)\s+\w+\s+\w+\s*\(')).Count
        
        # 计算类数
        $classCount = ([regex]::Matches($content, '\bclass\s+\w+')).Count
        
        # 计算 if 语句数(粗略估算圈复杂度)
        $ifCount = ([regex]::Matches($content, '\bif\s*\(')).Count
        
        # 计算 for/foreach/while 循环
        $loopCount = ([regex]::Matches($content, '\b(for|foreach|while)\s*\(')).Count
        
        # 估算圈复杂度
        $estimatedComplexity = $methodCount + $ifCount + $loopCount
        
        $relativePath = $file.FullName.Replace((Get-Location).Path + "\", "")
        
        $results += [PSCustomObject]@{
            File = $relativePath
            Lines = $lines
            Classes = $classCount
            Methods = $methodCount
            IfStatements = $ifCount
            Loops = $loopCount
            EstimatedComplexity = $estimatedComplexity
        }
    }

# 导出结果
$results | Export-Csv $OutputFile -NoTypeInformation -Encoding UTF8

# 统计
Write-Host "
Code Metrics Summary:" -ForegroundColor Green
Write-Host "=====================" -ForegroundColor Green
Write-Host "Total files: $($results.Count)"
Write-Host "Total lines: $(($results | Measure-Object -Property Lines -Sum).Sum)"
Write-Host "Total classes: $(($results | Measure-Object -Property Classes -Sum).Sum)"
Write-Host "Total methods: $(($results | Measure-Object -Property Methods -Sum).Sum)"
Write-Host "Avg lines per file: $([Math]::Round(($results | Measure-Object -Property Lines -Average).Average, 0))"

Write-Host "
Top 10 Largest Files:" -ForegroundColor Yellow
$results | Sort-Object Lines -Descending | Select-Object -First 10 | Format-Table File, Lines, Classes, Methods -AutoSize

Write-Host "
Top 10 Most Complex Files:" -ForegroundColor Yellow
$results | Sort-Object EstimatedComplexity -Descending | Select-Object -First 10 | Format-Table File, EstimatedComplexity, IfStatements, Loops -AutoSize

Write-Host "
Files over 1000 lines:" -ForegroundColor Red
$largeFiles = $results | Where-Object { $_.Lines -gt 1000 }
Write-Host "$($largeFiles.Count) files"
$largeFiles | Format-Table File, Lines -AutoSize

Write-Host "
Results saved to: $OutputFile" -ForegroundColor Cyan
