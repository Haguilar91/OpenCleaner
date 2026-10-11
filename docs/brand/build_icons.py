#!/usr/bin/env python3
"""Regenerate every app icon in the repo from the master SVGs in docs/brand/svg.

Rule: the drive icon is used at 64 px and up, the sweep mark at 48 px and below.

Needs: pip install playwright pillow && python -m playwright install chromium
Run from anywhere: python3 docs/brand/build_icons.py
"""
import asyncio
import os
import shutil
from pathlib import Path

from PIL import Image
from playwright.async_api import async_playwright

ROOT = Path(__file__).resolve().parents[2]
BRAND = ROOT / "docs" / "brand"
SVG = BRAND / "svg"
PNG = BRAND / "png"

TILE = "96 96 832 832"  # the tile edge to edge, no shadow margin (Android legacy launcher)


def svg_text(name, viewbox=None):
    text = (SVG / f"{name}.svg").read_text()
    if viewbox:
        text = text.replace('viewBox="0 0 1024 1024"', f'viewBox="{viewbox}"')
    return text


async def main():
    PNG.mkdir(exist_ok=True)
    async with async_playwright() as p:
        browser = await p.chromium.launch()
        page = await browser.new_page()

        async def render(text, size, out):
            text = text.replace('width="1024" height="1024"', f'width="{size}" height="{size}"')
            if 'width="' not in text.split(">", 1)[0]:
                text = text.replace("<svg ", f'<svg width="{size}" height="{size}" ', 1)
            await page.set_viewport_size({"width": size, "height": size})
            await page.set_content(f'<html><body style="margin:0;background:transparent">{text}</body></html>')
            await page.screenshot(path=str(out), omit_background=True,
                                  clip={"x": 0, "y": 0, "width": size, "height": size})

        # 1. Brand PNGs (light + dark)
        for theme in ("light", "dark"):
            for s in (1024, 512, 256, 128, 64):
                await render(svg_text(f"opencleaner-icon-{theme}"), s, PNG / f"opencleaner-{theme}-{s}.png")
            for s in (48, 32, 24, 16):
                await render(svg_text(f"opencleaner-mark-{theme}"), s, PNG / f"opencleaner-{theme}-{s}.png")

        # 2. Android: adaptive foreground (108 dp) + legacy launcher (48 dp)
        res = ROOT / "android/app/src/main/res"
        for dpi, scale in (("mdpi", 1), ("hdpi", 1.5), ("xhdpi", 2), ("xxhdpi", 3), ("xxxhdpi", 4)):
            await render(svg_text("opencleaner-icon-foreground"), int(108 * scale),
                         res / f"mipmap-{dpi}/ic_launcher_foreground.png")
            await render(svg_text("opencleaner-icon-light", TILE), int(48 * scale),
                         res / f"mipmap-{dpi}/ic_launcher.png")
        await browser.close()

    light = lambda s: Image.open(PNG / f"opencleaner-light-{s}.png")

    # 3. Windows (WinForms + WinUI): one .ico with every size
    sizes = [16, 24, 32, 48, 64, 128, 256]
    for target in ("windows/icon.ico", "windows-winui/icon.ico"):
        light(256).save(ROOT / target, sizes=[(s, s) for s in sizes],
                        append_images=[light(s) for s in sizes[:-1]])

    # 4. macOS iconset (build_app.sh turns it into AppIcon.icns with iconutil)
    iconset = ROOT / "macos/Resources/AppIcon.iconset"
    for base in (16, 32, 128, 256, 512):
        shutil.copy(PNG / f"opencleaner-light-{base}.png", iconset / f"icon_{base}x{base}.png")
        shutil.copy(PNG / f"opencleaner-light-{base * 2}.png", iconset / f"icon_{base}x{base}@2x.png")
    shutil.copy(SVG / "opencleaner-icon-light.svg", ROOT / "macos/Resources/icon.svg")

    # 5. Linux
    shutil.copy(SVG / "opencleaner-icon-light.svg", ROOT / "linux-cachyos/icon.svg")
    print("Icons regenerated.")


if __name__ == "__main__":
    asyncio.run(main())
