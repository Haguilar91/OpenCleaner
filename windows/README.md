# OpenCleaner for Windows

Finds reclaimable disk space on Windows, shows it with sizes, and deletes only what you
tick and confirm. Windows version of OpenCleaner.

**Status: written on Linux, compile-checked with Mono under C# 5 rules, but never run on
Windows.** Expect to fix a small thing or two on first launch.

## Build and run

**Quick start:** `opencleaner-windows.zip` also contains a prebuilt `OpenCleaner.exe` (built with Mono's
compiler on Linux). It is unsigned, so SmartScreen will warn: More info > Run anyway.
To build it yourself instead:

1. Unzip the folder anywhere.
2. Double-click `build.bat` (uses the C# compiler built into Windows 10/11, no installs).
3. Double-click `OpenCleaner.exe`.

SmartScreen may warn about an unknown app, since you built it yourself: More info > Run anyway.

## What it cleans

| Category | Default | Needs admin |
|---|---|---|
| Your temp folder | ticked | no |
| Windows temp folder | ticked | yes |
| Windows Update download cache (stops/starts the update service) | ticked | yes |
| Delivery Optimization cache | ticked | yes |
| Your crash dumps | ticked | no |
| System crash dumps and error reports | ticked | yes |
| Recycle Bin | unticked | no |
| Chrome / Edge / Brave / Firefox caches (logins and history kept) | unticked | no |
| npm, pip, Yarn, NuGet, Gradle caches | unticked | no |
| Steam: shader caches of uninstalled games (named), browser cache, unfinished downloads | first ticked | no |

Every category always shows a row, with **Clean ✓** when there is nothing to remove.
Admin-only rows are greyed out until you click **Restart as administrator**.
The **Disk usage** tab browses folders by size; its button sends items to the Recycle Bin
(recoverable), never a permanent delete.

## Safety

- Each cleaning item can only delete inside its own allowed folders; anything else is refused.
- Files that are in use are skipped and counted, never forced.
- Not touched: Windows.old, WinSxS, system files. Use Windows "Disk Cleanup" for Windows.old.
