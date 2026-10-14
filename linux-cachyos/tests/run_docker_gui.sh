#!/bin/sh
# Launches the real window headlessly and saves screenshots to tests/out/<name>-*.png.
#   tests/run_docker_gui.sh [name]      name: ubuntu (default), debian12, debian13, fedora, rocky, arch
cd "$(dirname "$0")" || exit 1
NAME="${1:-ubuntu}"
case "$NAME" in fedora|rocky) PM=dnf ;; arch|arch-x86) PM=pacman ;; *) PM=apt ;; esac
VOL=ro; OUTVOL=""; command -v getenforce >/dev/null 2>&1 && [ "$(getenforce)" = Enforcing ] && VOL=ro,z && OUTVOL=":z"
mkdir -p out
docker build -q -t "opencleaner-test:$NAME" -f "docker/Dockerfile.$NAME" docker >/dev/null || exit 1
docker run --rm -v "$(cd ../.. && pwd):/src:$VOL" -v "$(pwd)/out:/out$OUTVOL" -e TAG="$NAME" \
  "opencleaner-test:$NAME" sh /src/linux-cachyos/tests/in_container_gui.sh "$PM" | tee "out/$NAME.gui.log"
# exit status for CI: the window must have walked through every page without a Python error or GTK critical
grep -q "screenshot apps-sorted" "out/$NAME.gui.log" && ! grep -qE "Traceback|CRITICAL" "out/$NAME.gui.log"
