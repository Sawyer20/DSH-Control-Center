@echo off
setlocal
title Pack DSH for other PCs
cd /d "%~dp0"

rem ---------------------------------------------------------------------
rem  Thin wrapper for pack-for-other-pc.ps1.
rem  This window never disappears on its own: it always shows the result
rem  and waits for a key.
rem ---------------------------------------------------------------------
rem  Prefer PowerShell 7 (pwsh); fall back to Windows PowerShell 5.1.
set "PS=powershell"
where pwsh >nul 2>nul && set "PS=pwsh"
echo Starting DSH pack (engine: %PS%) ...
echo.

%PS% -NoProfile -ExecutionPolicy Bypass -File "%~dp0pack-for-other-pc.ps1"
set "RC=%ERRORLEVEL%"

echo.
echo ---------------------------------------------------------------------
if "%RC%"=="0" (
  echo  Pack finished OK.
) else (
  echo  Pack FAILED with exit code %RC%. See the messages above.
)
echo ---------------------------------------------------------------------
echo.
pause
exit /b %RC%
