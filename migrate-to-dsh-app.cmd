@echo off
setlocal
title DSH Migration (to DSH-App)
cd /d "%~dp0"

rem ---------------------------------------------------------------------
rem  Thin wrapper for migrate-dsh-app.ps1.
rem  This window NEVER disappears on its own: it always shows the result
rem  and waits for a key, so you can read every step.
rem ---------------------------------------------------------------------
rem  Prefer PowerShell 7 (pwsh); fall back to Windows PowerShell 5.1.
set "PS=powershell"
where pwsh >nul 2>nul && set "PS=pwsh"
echo Starting DSH migration (engine: %PS%) ...
echo.

%PS% -NoProfile -ExecutionPolicy Bypass -File "%~dp0migrate-dsh-app.ps1"
set "RC=%ERRORLEVEL%"

echo.
echo ---------------------------------------------------------------------
if "%RC%"=="0" (
  echo  DSH migration finished OK.
) else (
  echo  DSH migration FAILED with exit code %RC%. See the messages above.
)
echo ---------------------------------------------------------------------
echo.
pause
exit /b %RC%
