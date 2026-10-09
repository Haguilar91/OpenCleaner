@echo off
rem Builds the WinUI 3 version of OpenCleaner into the "publish" folder.
rem Needs the .NET 8 SDK:  winget install Microsoft.DotNet.SDK.8
setlocal
cd /d "%~dp0"
dotnet --version >nul 2>&1
if errorlevel 1 (
  echo .NET SDK not found. Install it with:  winget install Microsoft.DotNet.SDK.8
  exit /b 1
)
dotnet publish OpenCleaner.csproj -c Release -r win-x64 --self-contained true -o publish
if errorlevel 1 (
  echo.
  echo Build failed. Copy the errors above and send them to Claude.
  exit /b 1
)
echo.
echo Built publish\OpenCleaner.exe  -  keep the whole "publish" folder together.
