@echo off
rem Builds the WinUI 3 version of OpenCleaner into publish\<rid> (win-x64 by default).
rem Needs the .NET 8 SDK:  winget install Microsoft.DotNet.SDK.8
setlocal
cd /d "%~dp0"
rem Optional first argument: runtime id (win-x64, win-arm64 or win-x86). Default win-x64.
set RID=%~1
if "%RID%"=="" set RID=win-x64
dotnet --version >nul 2>&1
if errorlevel 1 (
  echo .NET SDK not found. Install it with:  winget install Microsoft.DotNet.SDK.8
  exit /b 1
)
rem One folder per architecture (publish\win-x64, publish\win-arm64, publish\win-x86) so builds never mix.
if exist "publish\%RID%" rmdir /s /q "publish\%RID%"
dotnet publish OpenCleaner.csproj -c Release -r %RID% --self-contained true -o "publish\%RID%"
if errorlevel 1 (
  echo.
  echo Build failed. Copy the errors above and send them to Claude.
  exit /b 1
)
echo.
echo Built publish\%RID%\OpenCleaner.exe  -  keep the whole "publish\%RID%" folder together.
