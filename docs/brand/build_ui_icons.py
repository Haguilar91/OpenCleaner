#!/usr/bin/env python3
"""Builds every app's UI icons from the Lucide masters in docs/brand/ui-icons/ (one icon set for all apps).

Lucide icons are 2 px strokes with round caps and joins. Android draws them natively (vector drawables).
GTK, WinUI and SwiftUI colour icons by *filling* them, so for those the strokes are turned into filled
outlines here: every segment becomes a rectangle and every cap or corner a small disc, all wound the same
way, so a non-zero fill paints exactly the stroked shape. The result is a path of M/L/Z commands only.

Writes:
  android/app/src/main/res/drawable/ic_<name>.xml                    stroke vectors
  linux-cachyos/icons/hicolor/scalable/actions/opencleaner-*-symbolic.svg   (names in LINUX below)
  macos/Sources/OpenCleaner/Icons.swift                               enum Icon + LucideIcon view
  windows-winui/Icons.cs                                              Icons.Make(Icons.Folder, 16)

Needs only fontTools (python3 -m pip install fonttools). Run: python3 docs/brand/build_ui_icons.py
"""
import glob
import math
import os
import re

from fontTools.pens.recordingPen import RecordingPen
from fontTools.svgLib.path import parse_path

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
MASTERS = os.path.join(ROOT, "docs", "brand", "ui-icons")
HALF = 1.0          # half the Lucide stroke width (2 px on a 24 px grid)

# Linux keeps its existing icon names (the code uses them); value = master name
LINUX = {"go-up": "up", "go-next": "chevron-right", "folder": "folder", "text-x-generic": "file",
         "user-trash": "delete", "view-refresh": "refresh", "edit-clear-all": "clean",
         "drive-harddisk": "storage", "view-app-grid": "apps", "view-grid": "map", "view-reveal": "review",
         "software-update-available": "update", "object-select": "check", "view-conceal": "hide"}


def load():
    icons = {}
    for f in sorted(glob.glob(os.path.join(MASTERS, "*.svg"))):
        text = open(f).read()
        name = os.path.basename(f)[:-4]
        icons[name] = (re.search(r'Lucide icon "([^"]+)"', text).group(1), re.findall(r'<path d="([^"]+)"', text))
    return icons


# ---------------------------------------------------------------- stroke -> filled outline
def flatten(d):
    """SVG path -> list of (points, closed) polylines."""
    pen = RecordingPen()
    parse_path(d, pen)
    lines, pts, cur = [], [], None
    for op, args in pen.value:
        if op == "moveTo":
            if len(pts) > 1:
                lines.append((pts, False))
            pts = [args[0]]
        elif op == "lineTo":
            pts.append(args[0])
        elif op in ("curveTo", "qCurveTo"):
            p0 = pts[-1]
            ctrl = (p0,) + tuple(args)
            n = max(4, int(sum(math.dist(ctrl[i], ctrl[i + 1]) for i in range(len(ctrl) - 1)) / 1.0))
            for k in range(1, n + 1):
                pts.append(bezier(ctrl, k / n))
        elif op == "closePath":
            lines.append((pts, True))
            pts = []
        elif op == "endPath":
            if len(pts) > 1:
                lines.append((pts, False))
            pts = []
    if len(pts) > 1:
        lines.append((pts, False))
    out = []
    for pts, closed in lines:   # drop repeated points
        clean = [pts[0]] + [p for a, p in zip(pts, pts[1:]) if math.dist(a, p) > 1e-6]
        if closed and len(clean) > 1 and math.dist(clean[0], clean[-1]) < 1e-6:
            clean.pop()
        out.append((clean, closed))
    return out


def bezier(c, t):
    while len(c) > 1:
        c = [((1 - t) * a[0] + t * b[0], (1 - t) * a[1] + t * b[1]) for a, b in zip(c, c[1:])]
    return c[0]


def area(poly):
    return sum(a[0] * b[1] - b[0] * a[1] for a, b in zip(poly, poly[1:] + poly[:1])) / 2


def wound(poly):
    """Same winding for every piece (positive area with y pointing down), so non-zero fill = union."""
    return poly if area(poly) > 0 else poly[::-1]


def disc(c, sides=12):
    return wound([(c[0] + HALF * math.cos(2 * math.pi * k / sides),
                   c[1] + HALF * math.sin(2 * math.pi * k / sides)) for k in range(sides)])


def outline(paths):
    pieces = []
    for d in paths:
        for pts, closed in flatten(d):
            if len(pts) == 1:
                pieces.append(disc(pts[0]))
                continue
            segs = list(zip(pts, pts[1:])) + ([(pts[-1], pts[0])] if closed else [])
            for a, b in segs:
                length = math.dist(a, b)
                nx, ny = (a[1] - b[1]) / length * HALF, (b[0] - a[0]) / length * HALF
                pieces.append(wound([(a[0] + nx, a[1] + ny), (b[0] + nx, b[1] + ny),
                                     (b[0] - nx, b[1] - ny), (a[0] - nx, a[1] - ny)]))
            # round caps at open ends, round joins where the line turns (straight-ish curve steps need none)
            n = len(pts)
            for i, p in enumerate(pts):
                if not closed and i in (0, n - 1):
                    pieces.append(disc(p))
                    continue
                prev, nxt = pts[i - 1], pts[(i + 1) % n]
                a1 = math.atan2(p[1] - prev[1], p[0] - prev[0])
                a2 = math.atan2(nxt[1] - p[1], nxt[0] - p[0])
                turn = abs((a2 - a1 + math.pi) % (2 * math.pi) - math.pi)
                if turn > math.radians(12):
                    pieces.append(disc(p))
    return "".join("M" + "L".join(f"{num(x)} {num(y)}" for x, y in poly) + "Z" for poly in pieces)


def num(v):
    s = f"{v:.2f}".rstrip("0").rstrip(".")
    return "0" if s in ("-0", "") else s


# ---------------------------------------------------------------- writers
def write(path, text):
    path = os.path.join(ROOT, path)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    if not os.path.exists(path) or open(path).read() != text:
        open(path, "w").write(text)
        print("wrote", os.path.relpath(path, ROOT))


def android(name, lucide, paths):
    body = "".join(f'''    <path
        android:pathData="{d}"
        android:strokeColor="#FF000000"
        android:strokeWidth="2"
        android:strokeLineCap="round"
        android:strokeLineJoin="round" />
''' for d in paths)
    return f'''<?xml version="1.0" encoding="utf-8"?>
<!-- Lucide icon "{lucide}" (ISC license, https://lucide.dev) -->
<vector xmlns:android="http://schemas.android.com/apk/res/android"
    android:width="24dp"
    android:height="24dp"
    android:viewportWidth="24"
    android:viewportHeight="24">
{body}</vector>
'''


def camel(name):
    parts = name.split("-")
    return parts[0] + "".join(p.title() for p in parts[1:])


def main():
    icons = load()
    outlines = {name: outline(paths) for name, (_, paths) in icons.items()}
    note = "Generated by docs/brand/build_ui_icons.py from the Lucide icons in docs/brand/ui-icons (ISC license). Do not edit."

    used_on_android = {os.path.basename(f)[3:-4] for f in glob.glob(os.path.join(ROOT, "android/app/src/main/res/drawable/ic_*.xml"))}
    for name, (lucide, paths) in icons.items():
        if name in used_on_android:      # only icons the Android app already ships
            write(f"android/app/src/main/res/drawable/ic_{name}.xml", android(name, lucide, paths))

    for linux_name, name in LINUX.items():
        svg = (f'<!-- {note} Lucide "{icons[name][0]}" -->\n'
               f'<svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24">'
               f'<path fill="#2e3436" d="{outlines[name]}"/></svg>\n')
        write(f"linux-cachyos/icons/hicolor/scalable/actions/opencleaner-{linux_name}-symbolic.svg", svg)

    cases = "\n".join(f'    case {camel(n)} = "{outlines[n]}"' for n in icons)
    write("macos/Sources/OpenCleaner/Icons.swift", f'''import AppKit
import SwiftUI

// {note}

/// The shared OpenCleaner UI icons (Lucide), as filled outlines on a 24-point grid.
enum Icon: String {{
{cases}
}}

/// A Lucide icon in the current foreground style, the same set the other OpenCleaner apps use.
struct LucideIcon: View {{
    let icon: Icon
    var size: CGFloat = 16

    init(_ icon: Icon, size: CGFloat = 16) {{
        self.icon = icon
        self.size = size
    }}

    var body: some View {{
        IconShape(data: icon.rawValue).fill(style: FillStyle(eoFill: false)).frame(width: size, height: size)
    }}

    /// The icon as a template image, for controls that only show images (segmented pickers, menus).
    static func image(_ icon: Icon, size: CGFloat = 16) -> NSImage {{
        let image = NSImage(size: NSSize(width: size, height: size), flipped: true) {{ rect in
            guard let ctx = NSGraphicsContext.current?.cgContext else {{ return false }}
            ctx.addPath(IconShape(data: icon.rawValue).path(in: rect).cgPath)
            ctx.setFillColor(NSColor.black.cgColor)
            ctx.fillPath()
            return true
        }}
        image.isTemplate = true
        return image
    }}
}}

/// Parses the generated outline (only M, L and Z with absolute coordinates) and scales it to the frame.
struct IconShape: Shape {{
    let data: String

    func path(in rect: CGRect) -> Path {{
        let s = min(rect.width, rect.height) / 24
        var path = Path()
        var command: Character = "M"
        var numbers: [CGFloat] = []
        var token = ""

        func flushNumber() {{
            if let v = Double(token) {{ numbers.append(CGFloat(v)) }}
            token = ""
        }}
        func flushPoint() {{
            guard numbers.count == 2 else {{ return }}
            let p = CGPoint(x: rect.minX + numbers[0] * s, y: rect.minY + numbers[1] * s)
            if command == "M" {{ path.move(to: p) }} else {{ path.addLine(to: p) }}
            numbers = []
        }}

        for ch in data {{
            if ch == "M" || ch == "L" || ch == "Z" {{
                flushNumber()
                flushPoint()
                if ch == "Z" {{ path.closeSubpath() }} else {{ command = ch }}
            }} else if ch == " " {{
                flushNumber()
            }} else {{
                token.append(ch)
            }}
        }}
        flushNumber()
        flushPoint()
        return path
    }}
}}
''')

    consts = "\n".join(f'    public const string {camel(n)[0].upper() + camel(n)[1:]} = "{outlines[n]}";' for n in icons)
    write("windows-winui/Icons.cs", f'''using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;

namespace OpenCleaner;

// {note}

/// <summary>The shared OpenCleaner UI icons (Lucide), as filled outlines on a 24-unit grid.</summary>
static class Icons
{{
{consts}

    /// <summary>An icon of the given size that follows the theme's foreground colour.</summary>
    public static PathIcon Make(string data, double size = 16)
    {{
        var geometry = (Geometry)XamlBindingHelper.ConvertValue(typeof(Geometry), "F1 " + data);
        geometry.Transform = new ScaleTransform {{ ScaleX = size / 24, ScaleY = size / 24 }};
        return new PathIcon {{ Data = geometry, Width = size, Height = size }};
    }}
}}
''')


if __name__ == "__main__":
    main()
