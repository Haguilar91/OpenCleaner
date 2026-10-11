; Inno Setup script for the OpenCleaner (WinUI 3) installer.
; Build with installer\build_installer.bat, which publishes the app first.
; Manual build:  iscc /DArch=x64 /DAppVersion=1.0 opencleaner.iss   (Arch: x64, arm64 or x86)

#ifndef AppVersion
  #define AppVersion "1.0"
#endif
#ifndef Arch
  #define Arch "x64"
#endif
#define AppName "OpenCleaner"
#define SrcDir "..\windows-winui\publish\win-" + Arch

[Setup]
AppId={{E0F6C8DE-9317-440D-8DD0-0E3AD56799D6}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=OpenCleaner
AppPublisherURL=https://github.com/Haguilar91/OpenCleaner
AppSupportURL=https://github.com/Haguilar91/OpenCleaner/issues
; Per-user install by default: no admin prompt (the app asks for admin itself when needed)
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=..\dist
OutputBaseFilename=OpenCleaner-Setup-{#Arch}
SetupIconFile=..\windows-winui\icon.ico
UninstallDisplayIcon={app}\OpenCleaner.exe
LicenseFile=..\LICENSE
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
MinVersion=10.0.17763
#if Arch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#elif Arch == "x86"
; 32-bit build: also installs on 64-bit Windows, into Program Files (x86)
ArchitecturesAllowed=x86compatible
#else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; Flags: unchecked

[Files]
Source: "{#SrcDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\OpenCleaner.exe"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\OpenCleaner.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\OpenCleaner.exe"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent
