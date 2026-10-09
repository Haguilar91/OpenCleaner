# OpenCleaner: notes for Claude

OpenCleaner is one disk cleaner per OS with the same design: **scan, review sizes, tick, confirm,
clean**, plus a disk-usage browser. Repo: github.com/Haguilar91/OpenCleaner (MIT).
Naming is always **OpenCleaner** (no "Mac Cleaner" / "Win Cleaner" / "CachyOS Cleaner").

## Layout and how to run

| Folder | Stack | Build / run |
|---|---|---|
| `linux-cachyos/` | Python, GTK4 + libadwaita (`opencleaner.py`, one file) | `python3 linux-cachyos/opencleaner.py`; standalone binary: `build_binary.sh` (PyInstaller, bundles GTK4/libadwaita; host tools get the original `LD_LIBRARY_PATH` via `host_env()`) |
| `macos/` | SwiftUI, Swift package (`Sources/OpenCleaner`) | `./build_app.sh` then `open build/OpenCleaner.app` |
| `windows/` | C# WinForms, single `Program.cs`, **C# 5 only** | `build.bat` (uses Windows' built-in `csc.exe`) |
| `windows-winui/` | C# WinUI 3, .NET 8, UI built in code | `build.bat` then `publish\OpenCleaner.exe` |
| `android/` | Kotlin + Jetpack Compose, Gradle | Android Studio, or `gradle assembleDebug` |
| `transfer/` | git-ignored: prebuilt archives/APK/exe for copying between machines | |

## Status (update this when it changes)

| App | State |
|---|---|
| Linux | Run and tested on CachyOS (GNOME) |
| macOS | Builds on a Mac (needed fixes for Command Line Tools only builds, see pitfalls); cleaning flows not fully tested |
| Windows WinForms | Runs and scans on Windows 11 (125% scaling); cleaning not fully tested |
| Windows WinUI 3 | Builds and runs on Windows 11. Needs `app.manifest` (PerMonitorV2 DPI + Win10 supportedOS) or it renders blurry and the mouse wheel does not scroll |
| Android | Builds (debug APK), runs on a Pixel; features work per the user |
| Linux binary | `build_binary.sh` makes a ~100 MB PyInstaller single file that runs here; untested on other distros |

Highest-value next steps: exercise the real cleaning flows on Windows (WinForms and WinUI) and
macOS with actual junk; add Steam rows (shader cache, browser cache, partial downloads) to the Linux app (macOS and
Windows already have them).

## Shared design rules (keep all apps consistent)

- **Nothing is deleted until the user ticks items and confirms** in a dialog listing them.
- **Every category always shows a row**: "Clean" (green) when nothing to remove, "Scan failed"
  (red) when a scanner throws. Never hide a category.
- Risky items start **unticked**: save-game prefixes (Proton), device backups, orphaned
  packages, Recycle Bin/Trash, browser/app caches that may be in use, partial downloads,
  WhatsApp sent copies, large files.
- Each cleaning item can only delete inside its own allowed folders; anything else is refused
  (Linux/macOS: inside `$HOME`; Windows: per-item roots; Android: shared storage and own cache,
  never `Android/data` or `Android/obb`). Files in use are skipped and counted, never forced.
- The disk-usage views only move to Trash/Recycle Bin (desktop). Android has no trash for files, so
  it warns that deletes are permanent.
- Show free space and, after cleaning, how much was freed.
- Steam game names come from `store.steampowered.com/api/appdetails`, cached on disk
  (`opencleaner/steam_names`); unknown ids show as "App <id>". Installed apps are detected from
  every library in `libraryfolders.vdf`; if a library drive is missing, rows warn the game may live there.
- Admin/root actions go through the OS prompt (`pkexec` on Linux, UAC "Restart as administrator"
  on Windows); the apps never run as root by default.

## Pitfalls we already hit (don't repeat)

- **WinForms docking runs back to front**: the Fill control must be `BringToFront()`, header/bar
  controls behind it. Getting this wrong hides the tab strip under the header.
- **Windows build must stay C# 5** (no `$""`, `?.`, `nameof`, expression-bodied members) because
  `build.bat` uses the compiler that ships with Windows. Check with
  `mcs -langversion:5 -target:winexe ... Program.cs` (Mono has no `Microsoft.VisualBasic`; the
  Recycle Bin call loads it by reflection for that reason).
- `windows-winui/Core.cs` is a **port of the logic in `windows/Program.cs`** (same scanners). Fix
  bugs in both, or refactor to share.
- **SwiftUI `@State` fails with Command Line Tools only** (the macro plugin is missing): state
  lives in `ObservableObject`s (`@Published`) instead. Keep long expressions short or the Swift
  type-checker times out.
- **WinForms layout is in 96 DPI pixels**: the form uses `AutoScaleMode.Dpi` (set at the end of the
  constructor, inside `SuspendLayout/ResumeLayout`) to scale control bounds; list column widths and
  row heights are not auto-scaled, so wrap them in `S(px)`. Without this, text clips at 125%+.
- Screenshots live in `docs/screenshots/` (`<platform>-clean.png`, `<platform>-disk.png`).
- The macOS UI deliberately mirrors the Linux window: header bar (name + refresh, segmented
  Clean / Disk usage switch, free space), grouped rounded cards, bottom bar with the summary and a
  prominent Clean button. Selected tab lives in `CleanerStore.tab` (no `@State`). The window uses
  `.hiddenTitleBar`, so the header has 82pt of leading padding for the traffic-light buttons.
  The Mac screenshots in `docs/screenshots/` predate this restyle.
- Wine's list view does not draw groups and may not render some glyphs, so judge the look on real
  Windows, not Wine.
- **Android**: since Android 11 an app cannot clear other apps' caches or read their private data.
  The Apps tab ranks apps by size and opens each app's settings page instead. The "All files access"
  and "Usage access" permissions are granted by the user in Settings.
- Android build recipe that worked: JDK 17, Gradle 8.7, AGP 8.5.2, Kotlin 2.0.20 (Compose compiler
  plugin), compileSdk/targetSdk 34, minSdk 30, Compose BOM 2024.09.03.
- Dialogs and prompts that need passwords, tokens or license acceptance (sudo, SDK licenses, git
  push) are done by the user, never by Claude.

## Working agreements

- Commit locally when asked; the user pushes (GitHub token). End commit messages with the
  Co-Authored-By line from the session instructions.
- Ask before downloads, installs (`sudo pacman`, SDKs) and anything outward-facing.
- When a platform cannot be built on the current machine, say so plainly and ask the user to
  paste build errors rather than claiming it works.
