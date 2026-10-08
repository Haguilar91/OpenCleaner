# Mac Cleaner

A SwiftUI app that finds reclaimable disk space on macOS, shows it with sizes, and
deletes only what you tick and confirm. It is the macOS sibling of the CachyOS Cleaner.

**Status:** builds on macOS 13+. The cleaning flows have not been fully tested, so read the
confirmation list before cleaning.

## Build and run

    xcode-select --install      # once, if you have no Swift toolchain
    ./build_app.sh
    open build/MacCleaner.app

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
