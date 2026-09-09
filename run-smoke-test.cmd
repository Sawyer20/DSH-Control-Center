@echo off
setlocal
title DSH smoke test (headless, no window)
cd /d "%~dp0"

rem Build and run smoke-probe.cs: constructs DshWindow, forces a full layout
rem pass and seals every ControlTemplate. Catches startup crashes that would
rem otherwise only show up as "the tray icon flashes and disappears".
rem NOTE: the probe briefly creates a tray icon; it is removed when it exits.

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo [ERROR] csc.exe was not found on this machine.
  pause
  exit /b 1
)
set "FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
if not exist "%FW%\WPF\PresentationFramework.dll" set "FW=%WINDIR%\Microsoft.NET\Framework\v4.0.30319"

if not exist "%~dp0logs" mkdir "%~dp0logs"

echo Compiling smoke probe ...
"%CSC%" /nologo /target:exe /main:SmokeProbe /codepage:65001 ^
  /r:%FW%\WPF\PresentationFramework.dll /r:%FW%\WPF\PresentationCore.dll ^
  /r:%FW%\WPF\WindowsBase.dll /r:%FW%\System.Xaml.dll ^
  /r:System.Drawing.dll /r:System.Management.dll /r:System.Web.Extensions.dll ^
  /r:%FW%\System.IO.Compression.dll /r:%FW%\System.IO.Compression.FileSystem.dll ^
  "/out:%~dp0logs\smoke-probe.exe" ^
  "%~dp0DSH.cs" "%~dp0WpfUI.cs" "%~dp0TrayNative.cs" "%~dp0Win11Backdrop.cs" "%~dp0JobKill.cs" "%~dp0Usage.cs" "%~dp0Diagnostics.cs" "%~dp0Sessions.cs" "%~dp0Notifications.cs" "%~dp0Mux.cs" "%~dp0Backup.cs" "%~dp0PetScene.cs" "%~dp0PetWindow.cs" "%~dp0smoke-probe.cs"
if errorlevel 1 (
  echo.
  echo [ERROR] smoke probe failed to compile.
  pause
  exit /b 1
)

echo.
echo Running smoke probe ...
"%~dp0logs\smoke-probe.exe"
echo.
pause
