@echo off
rem Builds the WinUI app and wraps it in dist\OpenCleaner-Setup-<arch>.exe with Inno Setup.
rem   build_installer.bat [x64|arm64|x86] [version]      (default: this PC's architecture, 1.3)
rem Needs the .NET 8 SDK and Inno Setup 6:
rem   winget install Microsoft.DotNet.SDK.8
rem   winget install JRSoftware.InnoSetup
rem Build from a local drive (C:\...), not a VM shared folder: .NET fails on those.
setlocal
cd /d "%~dp0"

set ARCH=%~1
if "%ARCH%"=="" (
  if /i "%PROCESSOR_ARCHITECTURE%"=="ARM64" (set ARCH=arm64) else (set ARCH=x64)
)
if /i not "%ARCH%"=="x64" if /i not "%ARCH%"=="arm64" if /i not "%ARCH%"=="x86" (
  echo Unknown architecture "%ARCH%". Use x64, arm64 or x86.
  exit /b 1
)
set VERSION=%~2
if "%VERSION%"=="" set VERSION=1.3

rem ISCC may be set beforehand; otherwise look in the usual places, including a per-user
rem install under any profile (builds started remotely run as a different account)
if not defined ISCC (
  for %%P in ("%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" "%ProgramFiles%\Inno Setup 6\ISCC.exe") do (
    if exist %%P set "ISCC=%%~P"
  )
)
if not defined ISCC (
  for /d %%U in ("%SystemDrive%\Users\*") do (
    if exist "%%~U\AppData\Local\Programs\Inno Setup 6\ISCC.exe" set "ISCC=%%~U\AppData\Local\Programs\Inno Setup 6\ISCC.exe"
  )
)
if "%ISCC%"=="" (
  echo Inno Setup not found. Install it with:  winget install JRSoftware.InnoSetup
  exit /b 1
)

echo Building the %ARCH% app...
call ..\windows-winui\build.bat win-%ARCH%
if errorlevel 1 exit /b 1

echo.
echo Building the installer...
"%ISCC%" /Qp /DArch=%ARCH% /DAppVersion=%VERSION% opencleaner.iss
if errorlevel 1 (
  echo.
  echo Installer build failed. Copy the errors above and send them to Claude.
  exit /b 1
)
echo.
echo Built dist\OpenCleaner-Setup-%ARCH%.exe
