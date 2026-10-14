#!/bin/sh
# Screenshots for docs/screenshots: the real window under Xvfb with a sample home folder (no real user files).
#   tests/docs_shots.sh [name] [lang]   name: a test image (default arch-x86); lang: es for Spanish
#   PNGs land in tests/out/docs-*.png (docs-es-*.png with a language)
cd "$(dirname "$0")" || exit 1
NAME="${1:-arch-x86}"
LANG_CODE="${2:-}"
TAG=docs${LANG_CODE:+-$LANG_CODE}
case "$NAME" in fedora|rocky) PM=dnf ;; arch|arch-x86) PM=pacman ;; *) PM=apt ;; esac
VOL=ro; OUTVOL=""; command -v getenforce >/dev/null 2>&1 && [ "$(getenforce)" = Enforcing ] && VOL=ro,z && OUTVOL=":z"
mkdir -p out
docker run --rm -v "$(cd ../.. && pwd):/src:$VOL" -v "$(pwd)/out:/out$OUTVOL" -e TAG="$TAG" -e LANGUAGE="$LANG_CODE" -e SHOT_ROOT=/root \
  "opencleaner-test:$NAME" sh -c '
    mkdir -p /root/Videos/Holidays /root/Pictures/2025 /root/Downloads /root/Music /root/Documents /root/.cache/thumbnails
    head -c 380000000 /dev/zero > /root/Videos/Holidays/beach-4k.mp4
    head -c 210000000 /dev/zero > /root/Videos/old-recording.mkv
    head -c 260000000 /dev/zero > /root/Downloads/ubuntu-24.04.iso
    head -c 95000000 /dev/zero > /root/Downloads/game-installer.zip
    for i in 1 2 3 4 5 6; do head -c 9000000 /dev/zero > /root/Pictures/2025/IMG_00$i.jpg; done
    head -c 42000000 /dev/zero > /root/Music/album.flac
    head -c 6000000 /dev/zero > /root/Documents/report.pdf
    head -c 150000000 /dev/zero > /root/.cache/thumbnails/cache.bin
    sh /src/linux-cachyos/tests/in_container_gui.sh '"$PM"
ls -la out | grep docs-
