# OpenCleaner for Windows (WinUI 3)

The modern Windows 11 version of OpenCleaner: Fluent controls, a sidebar, Mica backdrop and
automatic light/dark theme. The older, dependency-free WinForms version is in `../windows/`.

Same features as the other versions: scan, review sizes, tick, confirm, clean. Every category
always shows a row (**Clean** when empty), plus a **Disk usage** page that only sends items to
the Recycle Bin.

## Build and run

Needs Windows 10 (1809+) or Windows 11 and the .NET 8 SDK:

    winget install Microsoft.DotNet.SDK.8
    build.bat
    publish\OpenCleaner.exe

The first build downloads the Windows App SDK packages (a few hundred MB). The result is a
self-contained folder: copy the whole `publish` folder to move it, not just the exe.
Admin-only items (Windows temp, update cache...) unlock after **Restart as administrator**.

## Status

Written on Linux and **not yet built on Windows**. The scanning logic in `Core.cs` is shared with
the WinForms version and compile-checked; the WinUI screens (`CleanView.cs`, `DiskView.cs`,
`MainWindow.*`) have not been compiled. Expect a build error or two on the first run: paste them
into the chat and they get fixed.

## Layout

| File | What |
|---|---|
| `Core.cs` | Items, scanners, cleaning, safety checks (no UI) |
| `CleanView.cs`, `DiskView.cs` | The two pages, built in code |
| `MainWindow.xaml(.cs)` | Window, header, sidebar navigation |
| `Ui.cs` | Theme brushes, cards and dialogs |
