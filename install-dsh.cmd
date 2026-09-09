@echo off
setlocal
title DSH Installer
cd /d "%~dp0"

echo ============================================================
echo  DSH one-click setup
echo  Ensures Node.js + pnpm, installs DSH and creates the
echo  desktop shortcut. No administrator rights needed.
echo  Native modules are prebuilt - no Visual Studio / Python needed.
echo ============================================================
echo.

rem  Prefer PowerShell 7 (pwsh) when present; fall back to Windows PowerShell 5.1
rem  so a fresh target PC that has never seen pwsh can still install.
set "PS=powershell"
where pwsh >nul 2>nul && set "PS=pwsh"
echo  PowerShell engine: %PS%
echo.

%PS% -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup.ps1"
set "RC=%ERRORLEVEL%"

if not "%RC%"=="0" (
  echo.
  echo [ERROR] Setup failed with code %RC%. See the messages above.
  echo.
  pause
  exit /b %RC%
)

echo.
echo ============================================================
echo  Done. The "DSH" shortcut is on your desktop.
echo  Double-click it to start DSH and open the browser.
echo ============================================================
echo.
pause
