# Linux tests (Docker)

Checks the Linux app against real distros in containers. Needs Docker (Docker Desktop, Colima or
OrbStack).

    tests/run_docker_tests.sh                # all distros, three at a time; logs in tests/out/<name>.log
    tests/run_docker_tests.sh fedora arch    # only some: debian12 debian13 ubuntu fedora rocky arch
    tests/run_docker_gui.sh                  # real window under Xvfb (Ubuntu), screenshots in tests/out/
    tests/run_docker_gui.sh debian12         # same on another distro

The first run builds one test image per distro from `tests/docker/Dockerfile.*` (`opencleaner-test:<name>`)
with GTK, Xvfb and the test tools already installed. Later runs reuse them, so they skip the downloads.
Change a Dockerfile and the next run rebuilds just that image. Remove them with
`docker rmi $(docker images -q opencleaner-test)`.

## What the logic tests check (`test_distro.py`, as root inside the container)

| Check | How |
|---|---|
| Distro detection | `detect_distro()` must report the expected family and package manager |
| Package cache | APT: a fake `.deb` is found and `apt-get clean` removes it. DNF: `dnf makecache` then the cache is found and `dnf clean all` runs. Pacman: the scanner runs |
| Unused packages | `tree` is installed and marked as a dependency; the autoremove scanner must list it |
| Apps page data | `xterm` is installed; its `.desktop` file must map to the `xterm` package with a size |
| Uninstall | the real uninstall command runs (`pkexec` is a pass-through stub, the container is root) and `xterm` must be gone |

Matrix: Debian 12 and 13, Ubuntu 24.04, Fedora 42, Rocky Linux 9 and Arch. Arch has no official arm64
image, so on Apple silicon it uses the community image `menci/archlinuxarm:base` (Arch Linux ARM, its
own repositories; same pacman code paths). Everything runs natively, nothing is emulated.

## Not covered

- systemd journal and the real polkit password prompt (containers have no systemd/polkit).
- Linux Mint, Raspberry Pi OS, Omarchy and Bazzite have no official images; their detection is
  covered by the `os-release` samples in the unit check, and they use the same apt / pacman / Flatpak
  code paths as Ubuntu, Debian and Arch.
