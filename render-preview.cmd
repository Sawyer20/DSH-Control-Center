@echo off
setlocal
title DSH UI preview render (offscreen, no window)
cd /d "%~dp0"

rem Renders the real DshWindow offscreen to PNG so the UI can be reviewed
rem without launching the app. Safe to run while another DSH instance is in use.
rem Output: logs\preview-light.png, logs\preview-dark.png (+ -scrolled variants)

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

echo Compiling render probe ...
"%CSC%" /nologo /target:exe /main:RenderProbe /codepage:65001 ^
  /r:%FW%\WPF\PresentationFramework.dll /r:%FW%\WPF\PresentationCore.dll ^
  /r:%FW%\WPF\WindowsBase.dll /r:%FW%\System.Xaml.dll ^
  /r:System.Drawing.dll /r:System.Management.dll /r:System.Web.Extensions.dll ^
  /r:%FW%\System.IO.Compression.dll /r:%FW%\System.IO.Compression.FileSystem.dll ^
  "/out:%~dp0logs\render-probe.exe" ^
  "%~dp0DSH.cs" "%~dp0WpfUI.cs" "%~dp0TrayNative.cs" "%~dp0Win11Backdrop.cs" "%~dp0JobKill.cs" "%~dp0Usage.cs" "%~dp0Diagnostics.cs" "%~dp0Sessions.cs" "%~dp0Notifications.cs" "%~dp0Mux.cs" "%~dp0Backup.cs" "%~dp0PetScene.cs" "%~dp0PetWindow.cs" "%~dp0render-probe.cs"
if errorlevel 1 (
  echo.
  echo [ERROR] render probe failed to compile.
  pause
  exit /b 1
)

echo.
echo Rendering light + dark previews ...
"%~dp0logs\render-probe.exe" light
"%~dp0logs\render-probe.exe" dark
echo.
echo Open the PNGs in: %~dp0logs
start "" "%~dp0logs"
pause
