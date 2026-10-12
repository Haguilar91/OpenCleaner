# OpenCleaner for Linux

GTK 4 + libadwaita, one Python file (`opencleaner.py`). It detects the distro from `/etc/os-release`
and uses its package manager (see the main README for what it cleans per distro).

## Run

    python3 opencleaner.py

Needs Python 3.9+, GTK 4.8+, libadwaita 1.2+ and the GObject bindings with cairo (for the disk map):

| Distro | Install |
|---|---|
| Arch, CachyOS, Omarchy | `sudo pacman -S python-gobject gtk4 libadwaita` |
| Debian 12+, Ubuntu 24.04+, Linux Mint 22+, Raspberry Pi OS | `sudo apt install python3-gi python3-gi-cairo gir1.2-gtk-4.0 gir1.2-adw-1` |
| Fedora, Rocky Linux | `sudo dnf install python3-gobject gtk4 libadwaita` |
| Bazzite | already there (or run it from a toolbox/distrobox) |

The built-in player needs the GStreamer plugins for your media (most desktops have them).
On GTK below 4.12 / libadwaita below 1.5 (Debian 12, Raspberry Pi OS) the app uses older dialog
widgets that look slightly different but work the same.

A single-file executable with GTK bundled: `./build_binary.sh` (PyInstaller, ~100 MB).

## Tests

`tests/` runs the app's distro logic and its real window in Docker containers, see
[tests/README.md](tests/README.md).
