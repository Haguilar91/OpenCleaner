# OpenCleaner

Open-source disk cleaners for **Linux (CachyOS/Arch)**, **macOS**, **Windows** and **Android**.
Each one scans for reclaimable space, shows what it found with sizes, and deletes only what
you tick and confirm. Every category always shows a row, with **Clean ✓** when there is
nothing to remove, plus a disk-usage browser to find what is really taking space.

| Folder | App | Run it |
|---|---|---|
| `linux-cachyos/` | OpenCleaner for Linux (GTK4 + libadwaita, Python) | `python3 linux-cachyos/opencleaner.py`, or build a single-file executable with everything bundled (~100 MB): `linux-cachyos/build_binary.sh` |
| `macos/` | OpenCleaner for macOS (SwiftUI) | on a Mac: `cd macos && ./build_app.sh` |
| `windows/` | OpenCleaner for Windows (C# WinForms) | on Windows: double-click `windows/build.bat`, then `OpenCleaner.exe` |
| `windows-winui/` | OpenCleaner for Windows, modern WinUI 3 look (needs .NET 8 SDK) | on Windows: `windows-winui\build.bat`, then `publish\OpenCleaner.exe` |
| `android/` | OpenCleaner for Android (Kotlin + Compose) | build with Android Studio or `gradle assembleDebug`, then install the APK |

## Screenshots

Linux (`linux-cachyos/`):

<p>
  <img src="docs/screenshots/linux-clean.png" width="49%" alt="Linux: Clean view">
  <img src="docs/screenshots/linux-disk.png" width="49%" alt="Linux: Disk usage view">
</p>

macOS (`macos/`):

<p>
  <img src="docs/screenshots/macos-clean.png" width="49%" alt="macOS: Clean tab">
  <img src="docs/screenshots/macos-disk.png" width="49%" alt="macOS: Disk usage tab">
</p>

Android (`android/`):

<p>
  <img src="docs/screenshots/android-clean.png" width="32%" alt="Android: Clean tab">
  <img src="docs/screenshots/android-storage.png" width="32%" alt="Android: Storage tab">
  <img src="docs/screenshots/android-apps.png" width="32%" alt="Android: Apps tab">
</p>

Windows, WinUI 3 (`windows-winui/`):

<p>
  <img src="docs/screenshots/windows-winui-clean.png" width="49%" alt="WinUI: Clean page">
  <img src="docs/screenshots/windows-winui-disk.png" width="49%" alt="WinUI: Disk usage page">
</p>

Windows, WinForms (`windows/`):

<p>
  <img src="docs/screenshots/windows-winforms-clean.png" width="49%" alt="WinForms: Clean tab">
  <img src="docs/screenshots/windows-winforms-disk.png" width="49%" alt="WinForms: Disk usage tab">
</p>

## What they clean

- **Linux:** pacman cache and orphans, systemd journal, unused Flatpak runtimes, browser
  crash-recovery backups, large `~/.cache` folders, Trash, leftover Steam Proton prefixes.
- **macOS:** Homebrew, logs, app caches, Trash, Xcode data, simulators, iOS backups, Steam.
- **Android:** own cache, thumbnails, old APKs, WhatsApp sent copies, large files, per-app sizes
  (limited by what Android allows without root).
- **Windows:** temp folders, Windows Update cache, crash dumps, Recycle Bin, browser caches,
  developer caches, Steam.

## Safety

- Nothing is deleted until you tick items and confirm.
- Each item can only delete inside its own allowed folders; anything else is refused.
- Files in use are skipped, never forced.
- The disk-usage tabs only move items to the Trash / Recycle Bin.
- Items that might hold something you want (save-game prefixes, device backups, orphaned
  packages, caches of running apps) start unticked.

## Status

| App | Status |
|---|---|
| Linux | Run and tested on CachyOS (GNOME) |
| macOS | Builds on a Mac (macOS 13+); cleaning flows not fully tested yet |
| Windows (WinForms) | Runs and scans on Windows 11; cleaning not fully tested yet |
| Windows (WinUI 3) | Builds and runs on Windows 11; cleaning not fully tested yet |
| Android | Runs on a Pixel; features work |

Use at your own risk and read the confirmation list before cleaning. Bug reports and pull
requests welcome.

## License

MIT, see [LICENSE](LICENSE).
