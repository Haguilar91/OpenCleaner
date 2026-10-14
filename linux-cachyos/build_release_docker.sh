#!/bin/sh
# Builds the single-file Linux executables in Docker: x86_64 and arm64 (Raspberry Pi, ARM laptops).
#   ./build_release_docker.sh [version]      output: ../dist/release-<version>/opencleaner-<version>-linux-<arch>
#   ARCHS=amd64 ./build_release_docker.sh 1.2   only one architecture (build each natively where possible)
set -e
cd "$(dirname "$0")"
VERSION="${1:-1.4}"
OUT="$(cd .. && pwd)/dist/release-$VERSION"
mkdir -p "$OUT"
for arch in ${ARCHS:-amd64 arm64}; do
  name=$([ "$arch" = amd64 ] && echo x86_64 || echo arm64)
  echo "== $name"
  docker build -q --platform "linux/$arch" -t "opencleaner-build:$arch" -f docker/Dockerfile.build docker >/dev/null
  docker run --rm --platform "linux/$arch" -v "$(pwd):/src:ro" -v "$OUT:/out" "opencleaner-build:$arch" sh -c \
    "cp -r /src /work && cd /work && rm -rf .buildenv build dist && ./build_binary.sh >/tmp/build.log 2>&1 \
     || { tail -30 /tmp/build.log; exit 1; }; cp dist/opencleaner /out/opencleaner-$VERSION-linux-$name"
  ls -lh "$OUT/opencleaner-$VERSION-linux-$name"
done
