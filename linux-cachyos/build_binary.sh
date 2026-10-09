#!/bin/bash
# Builds a single-file executable with GTK 4, libadwaita and Python bundled inside (PyInstaller).
# Needs on the build machine: python, python-gobject, gtk4, libadwaita (the app's normal deps).
set -euo pipefail
cd "$(dirname "$0")"

python -m venv --system-site-packages .buildenv
.buildenv/bin/pip install -q pyinstaller
.buildenv/bin/pyinstaller --onefile --noconfirm --clean --name opencleaner \
    --distpath dist --workpath build --specpath build opencleaner.py

echo
echo "Built dist/opencleaner  -  run it with ./dist/opencleaner"
