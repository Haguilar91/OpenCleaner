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
| `windows-winui/` | C# WinUI 3, .NET 8, UI built in code | `build.bat [win-x64|win-arm64|win-x86]` then `publish\<rid>\OpenCleaner.exe` |
| `installer/` | Inno Setup | `installer\build_installer.bat` builds the WinUI app and `dist\OpenCleaner-Setup-<arch>.exe` (Windows only, build on a local drive) |
| `android/` | Kotlin + Jetpack Compose, Gradle | Android Studio, or `gradle assembleDebug` |
| `transfer/` | git-ignored: prebuilt archives/APK/exe for copying between machines | |

## Status (update this when it changes)

| App | State |
|---|---|
| Linux | Release 1.2. Distro logic tested in Docker (Debian 12/13, Ubuntu 24.04, Fedora 42, Rocky 9, Arch ARM + x86_64) and read-only on a real Fedora 44 laptop; real window walked through under Xvfb (Debian 12, Ubuntu, Fedora, Arch). Real cleaning/uninstall and the CachyOS laptop not retested since the distro work |
| macOS | 1.2 builds and runs (preview, player and map tried by the user; Apps page rendered here with real apps); cleaning and uninstall flows not fully tested |
| Windows WinForms | 1.2 compiles in the UTM VM (Windows 11 ARM64); the new preview, map, sort and Apps page have not been run |
| Windows WinUI 3 | 1.2 compiles and the x64/arm64/x86 Inno Setup installers build in the VM; the new features have not been run. Needs `app.manifest` (PerMonitorV2 DPI + Win10 supportedOS) or it renders blurry and the mouse wheel does not scroll |
| Android | 1.2 (versionCode 11) debug APK; preview, player and map tried on a Pixel; sort/Top and Apps sort are new |
| Linux binary | `build_release_docker.sh`: x86_64 + arm64 single files (~28 MB, Debian 12 base). Open the window on systems without GTK/Python (need the host's Wayland/X11/GL libs). Media playback inside the binary untested |

Highest-value next steps: exercise the real cleaning flows on Windows (WinForms and WinUI) and
macOS with actual junk; add Steam rows (shader cache, browser cache, partial downloads) to the Linux app (macOS and
Windows already have them).

## Shared design rules (keep all apps consistent)

- **File preview** (all apps): in Disk usage, clicking a file opens a preview (image, start of a text
  file, size/date/location, Open, Delete/Trash); in Clean, each item with files has a Review list
  whose files open the same preview. Android: `PreviewDialog`/`ReviewDialog` in `Screens.kt`;
  macOS: `Preview.swift`; Linux: `show_preview`/`show_review`; WinForms: `PreviewForm`/`ReviewForm`;
  WinUI: `Preview.cs`. macOS preview also plays mp4/mp3/flac (AppKit AVPlayerView; SwiftUI VideoPlayer crashes on macOS 27). Disk map (treemap), sort + Top 5/10/50/100 and measuring-progress % exist in Android, macOS, Linux, WinForms and WinUI (WinForms has no media player). Apps page (installed apps, size, last used, sort + Top N, uninstall with leftover review): Android (system uninstall prompt only), macOS (`AppsView.swift`), Linux (`AppsPage`, pacman + Flatpak), Windows (`Apps` class registry-based; the same code lives in `windows/Program.cs` and `windows-winui/AppsCore.cs`, keep in sync). Uninstall deletes PERMANENTLY (no Trash/Recycle Bin, by the user's choice) after a review dialog with a clear warning, and only inside the allowed folders (macOS: /Applications, ~/Applications, ~/Library; Linux: home; Windows: AppData, ProgramData, the install folder). The Linux app detects the distro (`detect_distro`, `build_scanners`): Arch/CachyOS/Omarchy = pacman, Debian/Ubuntu/Mint/Raspbian = apt (+ Snap), Fedora/Rocky = dnf, Bazzite = image-based (no system package ops, Flatpak only); detection logic is unit-checked, the per-distro scanners are not yet run on those distros. Not yet compiled/run: Linux, WinForms, WinUI (all of the above was written blind).

- **Nothing is deleted until the user ticks items and confirms** in a dialog listing them.
- **Every category always shows a row**: "Clean" (green) when nothing to remove, "Scan failed"
  (red) when a scanner throws. Never hide a category.
- Risky items start **unticked**: save-game prefixes (Proton), device backups, orphaned
  packages, Recycle Bin/Trash, browser/app caches that may be in use, partial downloads,
  WhatsApp sent copies, large files.
- Each cleaning item can only delete inside its own allowed folders; anything else is refused
  (Linux/macOS: inside `$HOME`; Windows: per-item roots; Android: shared storage and own cache,
  never `Android/data` or `Android/obb`). Files in use are skipped and counted, never forced.
- Disk usage deletes follow a setting: **Move to Trash (default)** or Delete permanently (remembered:
  macOS UserDefaults, Linux `~/.config/opencleaner/settings.json`, WinUI `%AppData%\OpenCleaner\settings.txt`,
  WinForms `HKCU\Software\OpenCleaner`). Permanent: Linux/macOS only inside `$HOME`; Windows refuses drive
  roots and Windows/Program Files/ProgramData. Android has no trash: always permanent, with a warning.
  Multi-select = list checkboxes + right-click (desktop) / long-press (Android) on map blocks, one shared selection.
- Uninstall is always permanent. Windows: HKLM (all-users) programs run their uninstaller with UAC
  ("runas"); leftovers wait until the uninstall registry key is gone (Office's uninstaller returns early);
  protected leftovers are removed in one elevated PowerShell step. macOS: root-owned apps (Office) go
  through the AppleScript administrator prompt.
- In-app updates from GitHub releases (`api.github.com/repos/Haguilar91/OpenCleaner/releases/latest`):
  asset picked by name suffix (`-Setup-<arch>.exe`, `-Windows-portable.exe`, `-macOS.zip`,
  `-linux-<arch>`, `-android.apk` for the phone app, `-androidtv.apk` for the TV app) and verified against `SHA256SUMS.txt`, so **every release must ship
  `SHA256SUMS.txt` and keep these file names**. Version comes from the app (Info.plist, AssemblyVersion,
  `VERSION` in opencleaner.py, versionName).
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
- **App icons come from `docs/brand/svg/`** (see `docs/brand/README.md`). Never edit the per-app
  icon files by hand: change the SVGs and run `python3 docs/brand/build_icons.py`. Drive icon at
  64 px and up, sweep mark at 48 px and below.
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
- **Linux testing uses Docker** (`linux-cachyos/tests/`, see its README): `run_docker_tests.sh` runs the
  distro logic against real apt/dnf/pacman (Debian 12/13, Ubuntu 24.04, Fedora 42, Rocky 9, Arch Linux ARM)
  in pre-built images `opencleaner-test:<name>` (three in parallel),
  `run_docker_gui.sh` launches the real window under Xvfb and saves screenshots. Keep the images cached;
  start Docker Desktop to test and quit it afterwards. Rocky 9 has Python 3.9, so `opencleaner.py` needs
  `from __future__ import annotations` for the `X | None` hints.
- **Linux app supports GTK 4.8 / libadwaita 1.2** (Debian 12, Raspberry Pi OS) through small shims at the
  top of `opencleaner.py` (`AlertDialog`, `Dialog`, `ToolbarView`, `launch_file`, `load_css`). Use those
  names, never `Adw.AlertDialog` / `Adw.Dialog` / `Adw.ToolbarView` / `Gtk.FileLauncher` directly.
- The Windows VM in UTM ("Windows") can run commands from the Mac: `utmctl exec Windows --cmd ...`
  returns at once (write output to a file, then `utmctl file pull`); dotnet is at
  `C:\Program Files\dotnet\dotnet.exe` (not on that session's PATH). No desktop/screenshots from there.
  Commands run as SYSTEM: NuGet downloads fail there, so set `NUGET_PACKAGES=C:\Users\Hector\.nuget\packages`;
  Inno Setup is found under `C:\Users\Hector\AppData\Local\Programs\Inno Setup 6` (build_installer.bat looks there).
  To get files out, copy them to the shared folder at `\\localhost@9843\DavWWWRoot` (= `~/Winshared` on the Mac,
  seconds); `utmctl file pull` works but is very slow for large files.
- Release builds: `linux-cachyos/build_release_docker.sh <version>` (x86_64 + arm64 single-file binaries, Debian 12
  base; they need the host's display libraries, which every desktop has). Outputs go to `dist/release-<version>/`.
- Dialogs and prompts that need passwords, tokens or license acceptance (sudo, SDK licenses, git
  push) are done by the user, never by Claude.

## Working agreements

- Commit locally when asked; the user pushes (GitHub token). End commit messages with the
  Co-Authored-By line from the session instructions.
- Ask before downloads, installs (`sudo pacman`, SDKs) and anything outward-facing.
- When a platform cannot be built on the current machine, say so plainly and ask the user to
  paste build errors rather than claiming it works.
