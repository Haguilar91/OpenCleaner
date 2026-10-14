#!/usr/bin/env python3
"""Reads the version of every app and checks they agree (and match the release tag, when given).

Usage: versions.py [tag]      prints the short version (e.g. 1.4) used in release file names.
The in-app updaters compare these numbers with the release tag, so a mismatch would make an app
offer the same update forever. Trailing ".0" parts are ignored: 1.4 == 1.4.0 == 1.4.0.0.
"""
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]

SOURCES = [
    ("Linux (opencleaner.py VERSION)", "linux-cachyos/opencleaner.py", r'^VERSION = "([^"]+)"'),
    ("macOS (Info.plist)", "macos/Resources/Info.plist",
     r"<key>CFBundleShortVersionString</key>\s*<string>([^<]+)</string>"),
    ("WinForms (AssemblyVersion)", "windows/Program.cs", r'\[assembly: AssemblyVersion\("([^"]+)"\)\]'),
    ("WinUI (csproj Version)", "windows-winui/OpenCleaner.csproj", r"<Version>([^<]+)</Version>"),
    ("Android (versionName)", "android/app/build.gradle.kts", r'versionName = "([^"]+)"'),
]


def short(v):
    parts = v.strip().lstrip("vV").split(".")
    while len(parts) > 2 and parts[-1] == "0":
        parts.pop()
    return ".".join(parts)


def main():
    found = {}
    for label, path, pattern in SOURCES:
        m = re.search(pattern, (ROOT / path).read_text(encoding="utf-8"), re.M)
        if not m:
            sys.exit(f"version not found: {label} in {path}")
        found[label] = short(m.group(1))
    for label, v in found.items():
        print(f"  {label}: {v}", file=sys.stderr)
    versions = set(found.values())
    if len(versions) != 1:
        sys.exit("the apps disagree on the version; update them all before releasing")
    version = versions.pop()
    if len(sys.argv) > 1 and sys.argv[1]:
        tag = short(sys.argv[1].split("/")[-1])
        if tag != version:
            sys.exit(f"tag {sys.argv[1]} does not match the apps' version {version}")
    print(version)


if __name__ == "__main__":
    main()
