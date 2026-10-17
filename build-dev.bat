@echo off
setlocal EnableExtensions

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\build\Build-MyCodex.ps1" -Profile Dev
exit /b %errorlevel%
