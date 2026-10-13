<p align="center"><img src="docs/brand/png/opencleaner-light-256.png" width="128" alt="OpenCleaner icon"></p>

# OpenCleaner

Open-source disk cleaners for **Linux (CachyOS/Arch)**, **macOS**, **Windows** and **Android**.
Each one scans for reclaimable space, shows what it found with sizes, and deletes only what
you tick and confirm. Every category always shows a row, with **Clean ✓** when there is
nothing to remove, plus a disk-usage browser to find what is really taking space, with a
**preview of any file before you decide to delete it**.

| Folder | App | Run it |
|---|---|---|
| `linux-cachyos/` | OpenCleaner for Linux (GTK4 + libadwaita, Python) | `python3 linux-cachyos/opencleaner.py`, or build a single-file executable with everything bundled (~100 MB): `linux-cachyos/build_binary.sh` |
| `macos/` | OpenCleaner for macOS (SwiftUI) | on a Mac: `cd macos && ./build_app.sh` |
| `windows/` | OpenCleaner for Windows (C# WinForms) | on Windows: double-click `windows/build.bat`, then `OpenCleaner.exe` |
| `windows-winui/` | OpenCleaner for Windows, modern WinUI 3 look (needs .NET 8 SDK) | on Windows: `windows-winui\build.bat`, then `publish\win-x64\OpenCleaner.exe` |
| `installer/` | Windows installer (Inno Setup) for the WinUI app: x64, arm64 or x86 | on Windows: `installer\build_installer.bat`, then `dist\OpenCleaner-Setup-<arch>.exe` (see [installer/README.md](installer/README.md)) |
| `android/` | OpenCleaner for Android phones (Kotlin + Compose) and **for Android TV / Google TV / Fire TV** (same project, `tv` flavor, remote-friendly) | `gradle assemblePhoneDebug` / `gradle assembleTvDebug`, then install the APK |

## Features

| | Android | macOS | Linux | Windows |
|---|:-:|:-:|:-:|:-:|
| Scan, review sizes, tick, confirm, clean | yes | yes | yes | yes |
| Disk-usage browser | yes | yes | yes | yes |
| **Preview a file before deleting** (image, text, details, Open with, Delete / Trash) | yes | yes | yes* | yes* |
| **Review** the files inside a Clean item before ticking it | yes | yes | yes* | yes* |
| **Built-in player** for mp4, mp3, flac and more | yes | yes | yes* (GStreamer) | WinUI yes*, WinForms no (Open launches your player) |
| **Disk map** (WinDirStat-style treemap, click to drill down) | yes | yes | yes* | yes* |
| **Sort** by size, oldest, newest or file type, and show the **Top** 5 / 10 / 50 / 100 | yes | yes | yes* | yes* |
| Progress percentage while sizes are measured | yes | yes | yes* | yes* |
| **Apps**: installed apps with size and last used, sort and Top N | yes | yes | yes* | yes* |
| **Uninstall** with a review of leftover files (permanent delete, with a warning) | system prompt only | yes | yes* | yes* |
| Uninstall apps installed for all users (asks for the administrator password / UAC) | n/a | yes | yes* (pkexec) | yes* |
| **Select several files or folders** (list checkboxes, or right-click / long-press in the map) and delete them at once | yes | yes | yes* | yes* |
| **Delete setting**: Move to Trash (default) or Delete permanently | permanent only | yes | yes* | yes* |
| **Update from GitHub releases** inside the app (download checked against SHA256SUMS.txt) | yes | yes | yes* | yes* |

\* written but not yet built or run on that platform; see Status.

Apps page: lists installed apps with their size and when they were last used, sortable by size,
least or most recently used, or name, with the same Top 5 / 10 / 50 / 100 choice. On desktop,
Uninstall shows the app plus its leftover files first (caches, preferences, settings folders;
anything that may hold your data starts unticked) with a clear warning, and after you confirm
**deletes the ticked items permanently: nothing goes to the Trash or Recycle Bin and it cannot be
undone**. Android can only open the system's own uninstall prompt, which is permanent too. Last used
comes from Spotlight on macOS, usage access on Android, and is approximate on Linux (program file
access time) and Windows (Explorer's launch history).

How the disk map works: it draws the current folder as colored blocks sized by space (folders,
images, video, audio, documents, archives, other). Click a folder to open it, click a file to
preview it. Sort and Top apply to both the list and the map, so "Oldest first + Top 10" shows
the 10 oldest items.

## Screenshots

macOS, Linux, Android and Android TV screenshots are from 1.3; the Windows ones are older.

Linux (`linux-cachyos/`):

<p>
  <img src="docs/screenshots/linux-clean.png" width="49%" alt="Linux: Clean view">
  <img src="docs/screenshots/linux-disk.png" width="49%" alt="Linux: Disk usage list">
  <img src="docs/screenshots/linux-map.png" width="49%" alt="Linux: disk map with a selected folder">
  <img src="docs/screenshots/linux-apps.png" width="49%" alt="Linux: Apps page">
</p>

macOS (`macos/`):

<p>
  <img src="docs/screenshots/macos-clean.png" width="49%" alt="macOS: Clean tab">
  <img src="docs/screenshots/macos-disk.png" width="49%" alt="macOS: Disk usage list">
  <img src="docs/screenshots/macos-map.png" width="49%" alt="macOS: disk map">
  <img src="docs/screenshots/macos-apps.png" width="49%" alt="macOS: Apps page">
</p>

Android (`android/`):

<p>
  <img src="docs/screenshots/android-clean.png" width="24%" alt="Android: Clean tab">
  <img src="docs/screenshots/android-storage.png" width="24%" alt="Android: Storage tab">
  <img src="docs/screenshots/android-map.png" width="24%" alt="Android: disk map with two folders selected">
  <img src="docs/screenshots/android-apps.png" width="24%" alt="Android: Apps tab">
</p>

Android TV / Google TV / Fire TV (`android/`, `tv` build), all driven with the remote:

<p>
  <img src="docs/screenshots/android-tv-storage.png" width="49%" alt="TV: Storage list">
  <img src="docs/screenshots/android-tv-map.png" width="49%" alt="TV: disk map with a selected block">
  <img src="docs/screenshots/android-tv-clean.png" width="49%" alt="TV: Clean">
  <img src="docs/screenshots/android-tv-apps.png" width="49%" alt="TV: Apps">
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

- **Linux:** detects the distro from `/etc/os-release` and uses its package manager. Arch family
  (Arch, CachyOS, Omarchy): pacman cache and orphans. Debian family (Debian, Ubuntu, Linux Mint,
  Raspberry Pi OS): APT cache, unused packages (autoremove), old Snap revisions. Fedora family
  (Fedora, Rocky): DNF cache and unused packages. Image-based systems (Bazzite): system packages
  are left alone (managed by image updates), Flatpak only. On every distro: systemd journal,
  unused Flatpak runtimes, browser crash-recovery backups, large `~/.cache` folders, Trash, leftover
  Steam Proton prefixes. The detected distro and package manager show in the header.
- **macOS:** Homebrew, logs, app caches, Trash, Xcode data, simulators, iOS backups, Steam.
- **Android:** own cache, thumbnails, old APKs, WhatsApp sent copies, large files, per-app sizes
  (limited by what Android allows without root).
- **Windows:** temp folders, Windows Update cache, crash dumps, Recycle Bin, browser caches,
  developer caches, Steam.

## Updates

Each app checks the latest release on GitHub when it starts (one request to `api.github.com`; nothing
about you or your files is sent), and has a "Check for updates" button. An update is downloaded from the
release, checked against the release's `SHA256SUMS.txt`, and installed: the Windows installer upgrades
in place, the portable Windows build and the Linux single-file build replace themselves, the Mac app
replaces itself and relaunches, and Android opens its own installer. Linux running from source shows
the release page instead.

## Safety

- Nothing is deleted until you tick items and confirm.
- Each item can only delete inside its own allowed folders; anything else is refused.
- Files in use are skipped, never forced.
- Deleting in Disk usage moves items to the Trash / Recycle Bin by default; a setting switches it to
  permanent delete (Android has no trash, so there it is always permanent and says so). Permanent
  deletes are limited to your home folder (Linux/macOS) or refuse drive roots and system folders
  (Windows). Uninstalling always deletes permanently, after a review and a warning.
- Items that might hold something you want (save-game prefixes, device backups, orphaned
  packages, caches of running apps) start unticked.

## Status

| App | Status |
|---|---|
| Linux | Run and tested on CachyOS (GNOME) before the newer changes; preview, player, map, sort and branding are written but not yet run |
| macOS | Builds and runs on macOS 13+: preview, player and map tried; sort / Top and the progress percentage are new and untried; cleaning flows not fully tested yet |
| Windows (WinForms) | Runs and scans on Windows 11; preview, map and sort are written but not yet built or tested; cleaning not fully tested yet |
| Windows (WinUI 3) | Builds, installs and runs on Windows 11 (ARM64 VM); preview, player, map and sort are written after that build and not yet tested; cleaning not fully tested yet |
| Windows installer | Builds with Inno Setup and installs on Windows 11 (ARM64 VM); only one architecture has been tried, the others not yet; unsigned, so SmartScreen warns |
| Android | Runs on a Pixel: preview, player and map tried; sort / Top and the progress percentage are new and untried |

## Branding

New icon (a silver hard disk with a blue sweep), colors (Trust Blue `#2563EB`) and the Ubuntu
font. Source files and rules are in [docs/brand/](docs/brand/README.md); the app icons are
generated from those SVGs. UI icons on Android are [Lucide](https://lucide.dev) (ISC).

Use at your own risk and read the confirmation list before cleaning. Bug reports and pull
requests welcome.

## License

MIT, see [LICENSE](LICENSE).
