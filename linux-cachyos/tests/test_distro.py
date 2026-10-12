"""Runs inside a distro container (as root). Checks the distro logic of opencleaner.py against the real
package manager: detection, cache and orphan scanners, app owner/size lookup and the uninstall command.

Env: EXPECT_FAMILY, EXPECT_PM (what detection must report), TEST_PKG (a small GUI app with a .desktop file)."""
import importlib.util
import os
import shutil
import subprocess
import sys

spec = importlib.util.spec_from_file_location("oc", os.path.join(os.path.dirname(__file__), "..", "opencleaner.py"))
oc = importlib.util.module_from_spec(spec)
sys.modules["oc"] = oc          # dataclasses look the module up here
spec.loader.exec_module(oc)

failures = []


def check(name, ok, detail=""):
    print(("PASS " if ok else "FAIL ") + name + (f"  [{detail}]" if detail else ""), flush=True)
    if not ok:
        failures.append(name)


d = oc.DISTRO
print(f"detected: id={d.id} family={d.family} pm={d.pm} immutable={d.immutable} label={d.label}")
check("family", d.family == os.environ.get("EXPECT_FAMILY", d.family), d.family)
check("package manager", d.pm == os.environ.get("EXPECT_PM", d.pm), str(d.pm))

pm = d.pm
pkg = os.environ.get("TEST_PKG", "xterm")
sh = lambda *c: subprocess.run(c, capture_output=True, text=True)

# ---- package cache scanner
if pm == "apt":
    os.makedirs("/var/cache/apt/archives", exist_ok=True)
    with open("/var/cache/apt/archives/oc-test_1_all.deb", "wb") as f:
        f.write(b"0" * 2 * 1024 * 1024)
    items = oc.scan_apt_cache()
    check("apt cache found", bool(items) and items[0].size >= 2 * 1024 * 1024, str(items[0].size if items else None))
    check("apt cache command", bool(items) and items[0].root_script == "apt-get clean")
elif pm == "dnf":
    sh("dnf", "-y", "-q", "makecache")
    items = oc.scan_dnf_cache()
    check("dnf cache found", bool(items) and items[0].size > 1024**2, str(items[0].size if items else None))
elif pm == "pacman":
    check("pacman cache scanner runs", isinstance(oc.scan_pacman_cache(), list))

# ---- install the test app, mark a leaf package as unneeded, look for it
if pm == "apt":
    sh("apt-get", "update", "-qq")
    r = sh("apt-get", "install", "-y", "--no-install-recommends", pkg, "tree")
    sh("apt-mark", "auto", "tree")
elif pm == "dnf":
    r = sh("dnf", "-y", "install", pkg, "tree")
    # dnf5 calls it "mark dependency", dnf4 "mark remove"
    if sh("dnf", "-y", "mark", "dependency", "tree").returncode != 0:
        sh("dnf", "-y", "mark", "remove", "tree")
elif pm == "pacman":
    r = sh("pacman", "-S", "--noconfirm", pkg, "tree")
    sh("pacman", "-D", "--asdeps", "tree")
check(f"install {pkg}", r.returncode == 0, r.stderr[-160:])

if pm == "apt":
    items = oc.scan_apt_orphans()
elif pm == "dnf":
    items = oc.scan_dnf_unneeded()
elif pm == "pacman":
    items = oc.scan_orphans()
else:
    items = []
named = " ".join(i.subtitle + i.confirm_note for i in items)
check("unused-package scanner lists the marked package", "tree" in named, named[:100])

# ---- apps page data: owner package and size of the .desktop file
files_cmd = {"apt": ["dpkg", "-L", pkg], "dnf": ["rpm", "-ql", pkg], "pacman": ["pacman", "-Ql", pkg]}.get(pm)
listing = sh(*files_cmd).stdout.split() if files_cmd else []
desktops = [f for f in listing if f.startswith("/usr/share/applications/") and f.endswith(".desktop")][:1]
check(f"{pkg} .desktop file exists", bool(desktops), str(desktops))
if desktops and pm:
    owners, sizes = oc.native_owners_sizes(pm, desktops)
    check("owner package found", pkg in owners, str(owners))
    check("package size found", sizes.get(pkg, 0) > 0, str(sizes))

# ---- uninstall command (pkexec is a pass-through stub in the container)
if pm:
    cmd = oc.native_uninstall_cmd(pm, pkg)
    print("uninstall command:", " ".join(cmd))
    r = oc.run(cmd)
    gone = sh({"apt": "dpkg", "dnf": "rpm", "pacman": "pacman"}[pm], *({"apt": ["-s", pkg], "dnf": ["-q", pkg], "pacman": ["-Q", pkg]}[pm]))
    installed = gone.returncode == 0 and (pm != "apt" or "install ok installed" in gone.stdout)
    check("uninstall removed the package", r.returncode == 0 and not installed, (r.stderr or r.stdout)[-160:])

# ---- the cache clean commands, last
if pm == "apt":
    r = sh("apt-get", "clean")
    check("apt-get clean runs", r.returncode == 0 and not os.path.exists("/var/cache/apt/archives/oc-test_1_all.deb"), r.stderr[-120:])
elif pm == "dnf":
    r = sh("dnf", "clean", "all")
    check("dnf clean all runs", r.returncode == 0, r.stderr[-120:])

print("RESULT:", "ALL PASSED" if not failures else f"{len(failures)} FAILED: {failures}")
sys.exit(1 if failures else 0)
