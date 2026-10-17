# Collect Code Coverage Script
# 收集测试覆盖率并生成报告

param(
    [string]$Configuration = "Debug",
    [string]$OutputDir = "coverage",
    [switch]$OpenReport
)

Write-Host "Collecting code coverage..." -ForegroundColor Cyan
Write-Host "Configuration: $Configuration
" -ForegroundColor Gray

# 清理旧的覆盖率数据
if (Test-Path $OutputDir) {
    Remove-Item $OutputDir -Recurse -Force
}
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

# 运行测试并收集覆盖率
Write-Host "Running tests with coverage collection..." -ForegroundColor Cyan
dotnet test MyCodex.sln 
    --configuration $Configuration 
    --collect:"XPlat Code Coverage" 
    --results-directory:"$OutputDir\raw" 
    --verbosity:minimal

if ($LASTEXITCODE -ne 0) {
    Write-Error "Tests failed!"
    exit $LASTEXITCODE
}

# 查找覆盖率文件
$coverageFiles = Get-ChildItem -Path "$OutputDir\raw" -Recurse -Filter "coverage.cobertura.xml"

if ($coverageFiles.Count -eq 0) {
    Write-Error "No coverage files found!"
    exit 1
}

Write-Host "
Found $($coverageFiles.Count) coverage file(s)" -ForegroundColor Green

# 生成报告
Write-Host "
Generating coverage report..." -ForegroundColor Cyan

$reportTypes = "Html;Badges;TextSummary"
$coverageFilePaths = ($coverageFiles | ForEach-Object { $_.FullName }) -join ";"

reportgenerator 
    -reports:"$coverageFilePaths" 
    -targetdir:"$OutputDir\report" 
    -reporttypes:$reportTypes 
    -verbosity:Info

if ($LASTEXITCODE -ne 0) {
    Write-Error "Report generation failed!"
    exit $LASTEXITCODE
}

# 读取摘要
$summaryFile = Join-Path $OutputDir "report\Summary.txt"
if (Test-Path $summaryFile) {
    Write-Host "
" -NoNewline
    Write-Host "Coverage Summary:" -ForegroundColor Green
    Write-Host "=================" -ForegroundColor Green
    Get-Content $summaryFile | Write-Host
}

# 显示报告位置
$reportPath = Join-Path $OutputDir "report\index.html"
Write-Host "
Coverage report generated:" -ForegroundColor Cyan
Write-Host "  $reportPath" -ForegroundColor White

# 打开报告
if ($OpenReport) {
    Write-Host "
Opening report in browser..." -ForegroundColor Cyan
    Start-Process $reportPath
}

Write-Host "
Done!" -ForegroundColor Green
