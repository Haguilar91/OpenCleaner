# Takes the README screenshots of both Windows apps (WinUI and WinForms):
#   windows-<app>-clean.png, -disk.png, -map.png, -apps.png
# Run it on Windows from a folder that holds the "windows" and "windows-winui" source folders:
#   powershell -ExecutionPolicy Bypass -File take_screenshots.ps1 [-Lang es]
# -Lang shows the apps in that language (files are then named windows-<app>-<lang>-*.png).
# It copies the sources to %TEMP% and builds there (fast local drive), makes a sample folder for
# Disk usage (so no real file names show up), opens each page with the --tab/--root/--map launch
# options and saves just the window. Do not touch the mouse or keyboard while it runs.
param(
    [string]$Out = (Join-Path $PSScriptRoot "out"),
    [ValidateSet("both", "winui", "winforms")][string]$App = "both",
    [switch]$SkipBuild,
    [string]$Lang = "",
    [int]$CleanWait = 25,   # seconds: the Clean page scans on open
    [int]$DiskWait = 8,
    [int]$AppsWait = 30     # the Apps page measures every program folder
)
$ErrorActionPreference = "Stop"

Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @"
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
public static class Shot
{
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);

    public static void Save(IntPtr h, string file)
    {
        ShowWindow(h, 9);   // restore if minimized
        SetWindowPos(h, IntPtr.Zero, 20, 20, 0, 0, 0x0001 | 0x0004);   // move to the top-left, keep size and z-order
        SetForegroundWindow(h);
        System.Threading.Thread.Sleep(800);
        RECT r;
        DwmGetWindowAttribute(h, 9, out r, Marshal.SizeOf(typeof(RECT)));   // frame bounds without the drop shadow
        using (Bitmap b = new Bitmap(r.Right - r.Left, r.Bottom - r.Top))
        {
            using (Graphics g = Graphics.FromImage(b))
                g.CopyFromScreen(r.Left, r.Top, 0, 0, b.Size);
            b.Save(file, ImageFormat.Png);
        }
    }
}
"@
[Shot]::SetProcessDPIAware() | Out-Null

$work = Join-Path $env:TEMP "OpenCleaner-shots"
$src = Join-Path $work "src"
New-Item -ItemType Directory -Force -Path $Out, $src | Out-Null

# ---- build
$rid = if ($env:PROCESSOR_ARCHITECTURE -eq "ARM64") { "win-arm64" } elseif ([Environment]::Is64BitOperatingSystem) { "win-x64" } else { "win-x86" }
$winforms = Join-Path $src "windows\OpenCleaner.exe"
$winui = Join-Path $src "windows-winui\publish\$rid\OpenCleaner.exe"
if (-not $SkipBuild) {
    foreach ($dir in "windows", "windows-winui") {
        $from = Join-Path $PSScriptRoot $dir
        if (-not (Test-Path $from)) { $from = Join-Path (Split-Path $PSScriptRoot) $dir }   # script inside windows\
        if (-not (Test-Path $from)) { throw "Cannot find the '$dir' source folder next to this script." }
        robocopy $from (Join-Path $src $dir) /E /XD bin obj publish /NFL /NDL /NJH /NJS /NP | Out-Null
    }
    if ($App -ne "winui") {
        Write-Host "Building WinForms..."
        cmd /c "`"$src\windows\build.bat`"" | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "WinForms build failed" }
    }
    if ($App -ne "winforms") {
        Write-Host "Building WinUI ($rid, about a minute)..."
        cmd /c "`"$src\windows-winui\build.bat`" $rid" | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "WinUI build failed" }
    }
}

# ---- sample folder for Disk usage (sizes are allocated, contents are zeros)
$sample = Join-Path $work "Sample"
function New-SampleFile([string]$rel, [double]$mb) {
    $path = Join-Path $sample $rel
    if ((Test-Path $path) -and (Get-Item $path).Length -eq [long]($mb * 1MB)) { return }
    New-Item -ItemType Directory -Force -Path (Split-Path $path) | Out-Null
    $fs = [IO.File]::Create($path)
    $fs.SetLength([long]($mb * 1MB))
    $fs.Close()
}
Write-Host "Preparing the sample folder..."
$files = [ordered]@{
    "Videos\Holiday 2025.mp4" = 420; "Videos\Birthday party.mov" = 260; "Videos\Screen recording.mp4" = 95
    "Music\Live at the park.flac" = 48; "Music\Road trip mix.mp3" = 12; "Music\Podcast episode 12.mp3" = 64
    "Pictures\Beach.jpg" = 6; "Pictures\Mountains.png" = 9; "Pictures\Family\Portrait.jpg" = 5; "Pictures\Family\Dinner.heic" = 4
    "Pictures\Raw\DSC_0042.dng" = 38; "Pictures\Raw\DSC_0043.dng" = 37
    "Documents\Taxes 2025.pdf" = 3; "Documents\Thesis.docx" = 2; "Documents\Budget.xlsx" = 1; "Documents\Slides.pptx" = 22
    "Downloads\Ubuntu-24.04-desktop.iso" = 580; "Downloads\Photos backup.zip" = 310; "Downloads\Setup.msi" = 75; "Downloads\Notes.txt" = 0.2
    "Projects\website\assets.zip" = 40; "Projects\website\video-intro.webm" = 55; "Projects\game\build.bin" = 120
    "Old backup.7z" = 240; "Recording.wav" = 90; "Report.pdf" = 8; "Wallpaper.png" = 14; "Installer.exe" = 66
}
foreach ($f in $files.Keys) { New-SampleFile $f $files[$f] }

# ---- shots
function Take([string]$exe, [string]$file, [string]$launchArgs, [int]$wait) {
    Write-Host ("  " + (Split-Path $file -Leaf) + " (" + $wait + " s)")
    $p = Start-Process -FilePath $exe -ArgumentList $launchArgs -PassThru
    try {
        $deadline = (Get-Date).AddSeconds(30)
        while ($p.MainWindowHandle -eq [IntPtr]::Zero) {
            if ((Get-Date) -gt $deadline) { throw "The window did not open: $exe" }
            Start-Sleep -Milliseconds 300
            $p.Refresh()
        }
        Start-Sleep -Seconds $wait
        [Shot]::Save($p.MainWindowHandle, $file)
    }
    finally {
        Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 1
    }
}

$apps = @()
if ($App -ne "winforms") { $apps += , @("winui", $winui) }
if ($App -ne "winui") { $apps += , @("winforms", $winforms) }
foreach ($a in $apps) {
    $name = $a[0]; $exe = $a[1]
    if (-not (Test-Path $exe)) { throw "Not built: $exe (run without -SkipBuild)" }
    Write-Host "Screenshots: $name"
    $tag = if ($Lang) { "windows-$name-$Lang" } else { "windows-$name" }
    $extra = if ($Lang) { " --lang $Lang" } else { "" }
    Take $exe (Join-Path $Out "$tag-clean.png") ("--tab clean" + $extra) $CleanWait
    Take $exe (Join-Path $Out "$tag-disk.png") ("--tab disk --root `"$sample`"" + $extra) $DiskWait
    Take $exe (Join-Path $Out "$tag-map.png") ("--tab disk --root `"$sample`" --map" + $extra) $DiskWait
    Take $exe (Join-Path $Out "$tag-apps.png") ("--tab apps" + $extra) $AppsWait
}
Write-Host ""
Write-Host "Done. Screenshots are in $Out"
