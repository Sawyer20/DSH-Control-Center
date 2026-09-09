@echo off
setlocal EnableExtensions
title DSH Launcher

rem ------------------------------------------------------------------
rem  DSH fallback launcher (start-dsh.cmd)
rem  The desktop shortcut normally uses DSH.exe (compiled, no cmd or
rem  powershell). This .cmd is kept as a fallback.
rem  - Uses the portable Node.js in .tools\node when present, otherwise
rem    a node.exe from PATH.
rem  - If the DSH web server (port 3080) is already running, opens the
rem    browser once.
rem  - Otherwise starts the server in a minimized console window titled
rem    "DSH Web Server" (closing it stops the server); the DSH server
rem    opens the browser itself on a fresh start, so this script does
rem    NOT open a second tab.
rem  Uses Windows' built-in curl.exe for checks - no powershell.
rem ------------------------------------------------------------------

cd /d "%~dp0"

set "URL=http://127.0.0.1:3080"
set "BIN=node_modules\@deepseek-ai\dsh\lib\bin.js"
set "NODE="

if not exist "%BIN%" (
  echo [ERROR] dsh is not installed in %CD%
  echo         Double-click install-dsh.cmd first.
  pause
  exit /b 1
)

rem ---- locate Node.js: portable toolchain first, then PATH ----
if exist "%~dp0.tools\node\node.exe" set "NODE=%~dp0.tools\node\node.exe"
if not defined NODE (
  where node >nul 2>nul
  if not errorlevel 1 set "NODE=node"
)
if not defined NODE (
  echo [ERROR] node.exe not found.
  echo         Double-click install-dsh.cmd once to set up a portable Node.js.
  pause
  exit /b 1
)

rem ---- Already running? Then just open the browser ----
curl -s -o nul -m 2 "%URL%" >nul 2>&1
if not errorlevel 1 goto open

rem ---- Start the DSH web server in a minimized console window ----
start "DSH Web Server" /min cmd /k "cd /d ""%~dp0"" && ""%NODE%"" ""%~dp0%BIN%"" web"

rem ---- Wait until the server answers (curl retries for ~3 minutes) ----
rem The DSH server itself opens the browser on a fresh start, so we do
rem NOT open a second tab here.
curl -s -o nul -m 5 --retry 176 --retry-delay 1 --retry-all-errors "%URL%" >nul 2>&1
if errorlevel 1 (
  echo [ERROR] DSH server did not become ready within ~3 minutes.
  echo         Please check the "DSH Web Server" window for details.
  pause
  exit /b 1
)
exit /b 0

:open
start "" "%URL%"
exit /b 0
