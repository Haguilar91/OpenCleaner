#!/bin/bash
# Builds a single-file executable with GTK 4, libadwaita, Python and the Adwaita icons bundled inside
# (PyInstaller). Needs on the build machine: python 3, python-gobject + cairo, gtk4, libadwaita,
# adwaita-icon-theme. For release builds use build_release_docker.sh (Debian 12 base, x86_64 + arm64).
set -euo pipefail
cd "$(dirname "$0")"

PY="$(command -v python3 || command -v python)"
"$PY" -m venv --system-site-packages .buildenv
.buildenv/bin/pip install -q pyinstaller

# PyInstaller's gi hooks bundle GTK 3 unless told otherwise, so pin GTK 4 in the spec.
.buildenv/bin/pyi-makespec --onefile --name opencleaner --specpath build opencleaner.py >/dev/null
.buildenv/bin/python - <<'PYEOF'
import re
p = "build/opencleaner.spec"
s = open(p).read()
cfg = ('hooksconfig={"gi": {"module-versions": {"Gtk": "4.0", "Gdk": "4.0", "Gsk": "4.0"}, '
       '"icons": ["Adwaita", "hicolor"], "themes": ["Adwaita"], "languages": ["en_US"]}},')
s, n = re.subn(r"hooksconfig=\{\},", cfg, s)
assert n == 1, "hooksconfig not found in the generated spec"
open(p, "w").write(s)
PYEOF
.buildenv/bin/pyinstaller --noconfirm --clean --distpath dist --workpath build build/opencleaner.spec

echo
echo "Built dist/opencleaner  -  run it with ./dist/opencleaner"
