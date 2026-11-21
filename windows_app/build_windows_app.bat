@echo off
REM EasyColab Windows one-click build (WinUI 3). Requires .NET 10 SDK + Python 3.10+ on PATH.
pwsh -NoProfile -ExecutionPolicy Bypass -File "%~dp0build_windows_app.ps1" %*
if errorlevel 1 (
  echo Build failed.
  pause
  exit /b 1
)
