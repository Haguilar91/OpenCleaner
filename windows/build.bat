@echo off
rem Builds WinCleaner.exe with the C# compiler that ships with Windows (no downloads needed).
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo Could not find csc.exe. Turn on .NET Framework 4.x in Windows Features.
  exit /b 1
)
"%CSC%" /nologo /target:winexe /optimize+ /out:WinCleaner.exe /win32icon:icon.ico /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:Microsoft.VisualBasic.dll Program.cs
if errorlevel 1 (
  echo.
  echo Build failed. Copy the errors above and send them to Claude.
  exit /b 1
)
echo.
echo Built WinCleaner.exe  -  double-click it to run.
