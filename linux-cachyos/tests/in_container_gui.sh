#!/bin/sh
# Inside a container: install GTK + Xvfb + ImageMagick, then run gui_smoke.py under a virtual screen.
set -e
PM="$1"
# pre-built images (tests/docker/) already have everything; plain distro images install it here
[ -x /usr/local/bin/pkexec ] && PREBUILT=1
[ -n "$PREBUILT" ] || case "$PM" in
  apt)
    export DEBIAN_FRONTEND=noninteractive
    apt-get update -qq
    apt-get install -y -qq --no-install-recommends python3 python3-gi python3-gi-cairo gir1.2-gtk-4.0 gir1.2-adw-1 \
      xvfb xauth imagemagick dbus adwaita-icon-theme fonts-dejavu-core xterm >/dev/null ;;
  dnf)
    dnf -y -q install python3 python3-gobject gtk4 libadwaita xorg-x11-server-Xvfb xorg-x11-xauth ImageMagick dbus-daemon \
      adwaita-icon-theme dejavu-sans-fonts xterm >/dev/null ;;
esac
export GSK_RENDERER=cairo GDK_BACKEND=x11 NO_AT_BRIDGE=1
timeout 180 dbus-run-session -- xvfb-run -a -s "-screen 0 1200x860x24" python3 "$(dirname "$0")/gui_smoke.py" 2>&1 | tail -60
