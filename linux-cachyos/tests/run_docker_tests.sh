#!/bin/sh
# Runs the distro logic tests in pre-built containers, three at a time.
#   tests/run_docker_tests.sh                 all distros
#   tests/run_docker_tests.sh fedora arch     only some (names below)
# The first run builds the images (tests/docker/Dockerfile.*); later runs reuse them.
# Logs: tests/out/<name>.log
cd "$(dirname "$0")" || exit 1
TESTS="$(pwd)"
ROOT="$(cd ../.. && pwd)"
mkdir -p out
MATRIX="debian12 debian apt
debian13 debian apt
ubuntu debian apt
fedora fedora dnf
rocky fedora dnf
arch arch pacman"
# only when asked for by name (x86_64 hosts):
EXTRA="arch-x86 arch pacman"
# SELinux hosts (Fedora) only let containers read mounts that are labelled for them
VOL=ro; command -v getenforce >/dev/null 2>&1 && [ "$(getenforce)" = Enforcing ] && VOL=ro,z
if [ $# -gt 0 ]; then
  picked=""
  for want in "$@"; do
    line="$(printf '%s\n%s\n' "$MATRIX" "$EXTRA" | grep "^$want ")" || { echo "unknown distro: $want"; exit 1; }
    picked="$picked$line
"
  done
  MATRIX="$(printf '%s' "$picked" | sed '/^$/d')"
fi

echo "$MATRIX" | while read -r name family pm; do
  printf 'building opencleaner-test:%s ... ' "$name"
  docker build -q -t "opencleaner-test:$name" -f "docker/Dockerfile.$name" docker >"out/$name.build.log" 2>&1 \
    && echo ok || { echo "FAILED (see out/$name.build.log)"; }
done

start=$(date +%s)
echo "$MATRIX" | xargs -P 3 -L 1 sh -c '
  name=$0; family=$1; pm=$2
  docker run --rm -v "'"$ROOT"':/src:'"$VOL"'" -e EXPECT_FAMILY="$family" -e EXPECT_PM="$pm" \
    "opencleaner-test:$name" sh /src/linux-cachyos/tests/in_container.sh "$pm" > "'"$TESTS"'/out/$name.log" 2>&1
  printf "%-9s %s\n" "$name" "$(grep "^RESULT" "'"$TESTS"'/out/$name.log" || echo "no result, see out/$name.log")"
'
echo "tests took $(( $(date +%s) - start ))s"
grep -h "^FAIL" out/*.log 2>/dev/null | sed 's/^/  /'
# exit status for CI: every picked distro must report ALL PASSED
status=0
for name in $(echo "$MATRIX" | cut -d' ' -f1); do
  grep -q "^RESULT: ALL PASSED" "out/$name.log" 2>/dev/null || status=1
done
exit $status
