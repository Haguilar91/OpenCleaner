# OpenCleaner for macOS

A SwiftUI app that finds reclaimable disk space on macOS, shows it with sizes, and
deletes only what you tick and confirm. It is the macOS version of OpenCleaner.

**Status:** builds and runs on macOS 13+. The cleaning flows have not been fully tested, so read the
confirmation list before cleaning.

## Features

- **Disk usage** as a list or a **disk map** (WinDirStat-style treemap: click a folder to drill in,
  click a file to preview it). **Sort** by biggest, oldest, newest or file type and show the **Top**
  5 / 10 / 50 / 100 or all. A percentage shows while sizes are measured.
- **Preview** a file before deleting it: images, start of text files, details, and a built-in
  **player for mp4, mp3, flac** and other audio/video. Reveal in Finder, Open, Move to Trash.
- **Apps** page: apps in /Applications and ~/Applications with size and last used (from Spotlight),
  sort by size / least or most recently used / name, Top 5 / 10 / 50 / 100, and **Uninstall** with a
  review of the app plus its leftovers in ~/Library (**deleted permanently, not moved to the Trash**, with a warning; data folders start unticked).
- **Review** the files inside a Clean item (eye button) before ticking it.

## Build and run

    xcode-select --install      # once, if you have no Swift toolchain
    ./build_app.sh
    open build/OpenCleaner.app

Needs macOS 13 or newer. You can also open the folder in Xcode (File > Open) and press Run.

## What it cleans

| Category | Default |
|---|---|
| Homebrew download cache and old versions (`brew cleanup -s`) | ticked |
| Homebrew unneeded dependencies (`brew autoremove`) | unticked |
| `~/Library/Logs` (logs, crash reports) | ticked |
| Large app caches in `~/Library/Caches` (100 MB+, skips `com.apple.*`) | unticked |
| Trash | unticked |
| Xcode DerivedData | ticked |
| Xcode iOS DeviceSupport and Archives | unticked |
| Unavailable iOS simulators | ticked |
| iPhone/iPad backups (one row per device) | unticked |
| Steam: shader caches of uninstalled games (named), browser cache, unfinished downloads | first ticked |

Every category always shows a row: **Clean ✓** when there is nothing to remove.
The **Disk Usage** tab browses Home or the whole disk by size; its trash button moves
items to the Trash (recoverable), it never deletes permanently.

## Notes

- Nothing needs admin rights. Deletes are refused outside your home folder.
- To see the Trash and some Library folders, give the app **Full Disk Access**
  (System Settings > Privacy & Security > Full Disk Access).
- Never deleted: `com.apple.*` caches, system folders, anything outside `~`.
