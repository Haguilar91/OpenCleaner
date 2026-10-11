# OpenCleaner Windows installer

Wraps the WinUI 3 app in a normal `Setup.exe` (Inno Setup): per-user install (no admin prompt),
Start Menu entry, optional desktop shortcut, entry in Apps & features, clean uninstall.

    winget install Microsoft.DotNet.SDK.8
    winget install JRSoftware.InnoSetup
    build_installer.bat            :: this PC's architecture
    build_installer.bat x64        :: or arm64 or x86, and an optional version: build_installer.bat x64 1.1

Output: `dist\OpenCleaner-Setup-x64.exe` (or `-arm64`, `-x86`). `dist\` is git-ignored.

- Build from a local drive (`C:\...`), not a VM shared folder: .NET fails on those.
- The app is self-contained, so users need no .NET or Windows App SDK. Expect about 100-150 MB.
- The installer is unsigned, so SmartScreen shows "Unknown publisher" until a code-signing
  certificate is used. Click More info, then Run anyway.
- The x64 installer also runs on Windows 11 ARM64 (emulated); the arm64 one is native only.
- The x86 installer is for 32-bit Windows 10; it also installs on 64-bit Windows (Program Files (x86)).
