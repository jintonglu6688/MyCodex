@echo off
setlocal EnableExtensions

set "ROOT=%~dp0"
cd /d "%ROOT%"

set "MYCODEX_PACKAGE_ROOT=%ROOT%"
powershell -NoProfile -Command "$path = Join-Path $env:MYCODEX_PACKAGE_ROOT 'Release\MyCodex.exe'; if (Test-Path -LiteralPath $path) { try { $stream = [IO.File]::Open($path, 'Open', 'ReadWrite', 'None'); $stream.Dispose() } catch { [Console]::Error.WriteLine('ERROR: Release\MyCodex.exe is in use. Close MyCodex and retry.'); exit 1 } }"
if errorlevel 1 exit /b %errorlevel%

echo [1/2] Building fresh Release output...
dotnet build "%ROOT%src\MyCodex.WinForms\MyCodex.WinForms.csproj" -c Release -p:Platform=x64 -v:minimal
if errorlevel 1 exit /b %errorlevel%

echo [2/2] Creating installer from Release output...
powershell -NoProfile -ExecutionPolicy Bypass -File "%ROOT%tools\installer\build-installer.ps1" -SourceDir "%ROOT%Release" -SkipBuild %*
exit /b %errorlevel%
