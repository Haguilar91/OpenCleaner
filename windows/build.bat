@echo off
rem Builds OpenCleaner.exe with the C# compiler that ships with Windows (no downloads needed).
setlocal
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo Could not find csc.exe. Turn on .NET Framework 4.x in Windows Features.
  exit /b 1
)
"%CSC%" /nologo /target:winexe /optimize+ /out:OpenCleaner.exe /win32icon:icon.ico /r:System.Windows.Forms.dll /r:System.Drawing.dll Program.cs
if errorlevel 1 (
  echo.
  echo Build failed. Copy the errors above and send them to Claude.
  exit /b 1
)
echo.
echo Built OpenCleaner.exe  -  double-click it to run.
