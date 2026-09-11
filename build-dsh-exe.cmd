@echo off
setlocal
title Build DSH.exe (WPF)
cd /d "%~dp0"

rem Compile the WPF (code-only, no XAML) DSH launcher with the built-in
rem .NET Framework csc. GUI target; no console window at run time.
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo [ERROR] csc.exe was not found on this machine.
  echo         If you cannot build DSH.exe, start-dsh.cmd is the fallback.
  pause
  exit /b 1
)
set "FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
if not exist "%FW%\WPF\PresentationFramework.dll" set "FW=%WINDIR%\Microsoft.NET\Framework\v4.0.30319"

rem Embed app.ico (fallback DSH.ico) as the exe file icon when present.
set "ICON="
if exist "%~dp0app.ico" (
  set "ICON=/win32icon:%~dp0app.ico"
) else (
  if exist "%~dp0DSH.ico" set "ICON=/win32icon:%~dp0DSH.ico"
)

echo Compiling DSH.exe (WPF, BuildId inside DSH.cs) ...
"%CSC%" /nologo /optimize+ /target:winexe /codepage:65001 %ICON% ^
  /win32manifest:"%~dp0app.manifest" ^
  /r:%FW%\WPF\PresentationFramework.dll /r:%FW%\WPF\PresentationCore.dll ^
  /r:%FW%\WPF\WindowsBase.dll /r:%FW%\System.Xaml.dll ^
  /r:System.Drawing.dll /r:System.Management.dll /r:System.Web.Extensions.dll ^
  /r:%FW%\System.IO.Compression.dll /r:%FW%\System.IO.Compression.FileSystem.dll ^
  "/out:%~dp0DSH.build.exe" "%~dp0DSH.cs" "%~dp0WpfUI.cs" "%~dp0TrayNative.cs" "%~dp0Win11Backdrop.cs" "%~dp0JobKill.cs" "%~dp0Usage.cs" "%~dp0Diagnostics.cs" "%~dp0Sessions.cs" "%~dp0Notifications.cs" "%~dp0Mux.cs" "%~dp0Backup.cs" "%~dp0PetScene.cs" "%~dp0PetWindow.cs" "%~dp0Settings.cs" "%~dp0Workspace.cs"
if errorlevel 1 (
  echo.
  echo [ERROR] Compilation failed.
  pause
  exit /b 1
)

rem Place the build at DSH.exe. A running DSH locks DSH.exe against
rem overwriting, but Windows DOES allow renaming a running executable - so the
rem old build is moved aside and the new one dropped in. The running instance
rem keeps working from DSH-old.exe; the next launch uses the new build.
if exist "%~dp0DSH-old.exe" del /q "%~dp0DSH-old.exe" >nul 2>&1
move /y "%~dp0DSH.build.exe" "%~dp0DSH.exe" >nul 2>&1
if errorlevel 1 (
  ren "%~dp0DSH.exe" "DSH-old.exe" >nul 2>&1
  move /y "%~dp0DSH.build.exe" "%~dp0DSH.exe" >nul 2>&1
  if errorlevel 1 (
    echo.
    echo [ERROR] Could not place DSH.exe - the file is still locked.
    pause
    exit /b 1
  )
  echo Note: DSH was running. Old build kept as DSH-old.exe; new build is DSH.exe.
  echo       Restart DSH when convenient to pick it up.
)

echo.
echo Built: %~dp0DSH.exe
echo  - WPF Fluent UI, DPI-aware (PerMonitorV2 manifest).
echo  - X closes to the tray; tray menu "Exit (stop service)" stops DSH.
pause
