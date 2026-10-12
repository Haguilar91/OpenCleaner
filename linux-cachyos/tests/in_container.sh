#!/bin/sh
# Runs inside a distro container: installs the GUI bindings the app imports, a pkexec stand-in, then the tests.
set -e
PM="$1"
# pre-built images (tests/docker/) already have everything; plain distro images install it here
[ -x /usr/local/bin/pkexec ] && PREBUILT=1
[ -n "$PREBUILT" ] || case "$PM" in
  apt)
    export DEBIAN_FRONTEND=noninteractive
    rm -f /etc/apt/apt.conf.d/docker-clean
    apt-get update -qq
    apt-get install -y -qq --no-install-recommends python3 python3-gi gir1.2-gtk-4.0 gir1.2-adw-1 >/dev/null ;;
  dnf)
    dnf -y -q install python3 python3-gobject gtk4 libadwaita >/dev/null ;;
  pacman)
    # the download sandbox needs syscalls an emulated amd64 container lacks
    sed -i '/^\[options\]/a DisableSandbox' /etc/pacman.conf
    pacman -Syu --noconfirm --needed python python-gobject gtk4 libadwaita pacman-contrib >/dev/null ;;
esac
# the app asks pkexec for root; in the container we are root already
printf '#!/bin/sh\nexec "$@"\n' > /usr/local/bin/pkexec; chmod +x /usr/local/bin/pkexec
python3 "$(dirname "$0")/test_distro.py"
