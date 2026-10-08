#!/usr/bin/env python3
"""CachyOS Cleaner: scan for reclaimable disk space, review it, then clean.

Nothing is deleted until you tick items and confirm. Root-level actions go
through pkexec (polkit prompt), so the app itself never runs as root.
"""
import glob
import os
import re
import shutil
import subprocess
import threading
from dataclasses import dataclass, field

import gi

gi.require_version("Gtk", "4.0")
gi.require_version("Adw", "1")
from gi.repository import Adw, GLib, Gtk, Pango  # noqa: E402

HOME = os.path.expanduser("~")
JOURNAL_KEEP_MB = 200
CACHE_MIN_BYTES = 100 * 1024 * 1024

UNITS = {"B": 1, "K": 1024, "KIB": 1024, "M": 1024**2, "MIB": 1024**2,
         "G": 1024**3, "GIB": 1024**3, "T": 1024**4, "TIB": 1024**4}


def fmt_size(n):
    if n is None:
        return "size unknown"
    for unit in ("B", "KiB", "MiB", "GiB", "TiB"):
        if n < 1024 or unit == "TiB":
            return f"{n:.0f} {unit}" if unit == "B" else f"{n:.1f} {unit}"
        n /= 1024


def parse_size(text):
    m = re.search(r"([\d.]+)\s*([KMGT]i?B?|B)\b", text, re.I)
    if not m:
        return None
    return int(float(m.group(1)) * UNITS[m.group(2).upper()])


def run(cmd, **kw):
    return subprocess.run(cmd, capture_output=True, text=True, **kw)


def du_bytes_x(path):
    """Size of a folder tree, staying on one filesystem."""
    r = run(["du", "-sxb", "--", path])
    try:
        return int(r.stdout.split()[0])
    except (IndexError, ValueError):
        return None


def du_bytes(path):
    r = run(["du", "-sb", "--", path])
    try:
        return int(r.stdout.split()[0])
    except (IndexError, ValueError):
        return None


@dataclass
class Item:
    group: str
    title: str
    subtitle: str
    size: int | None
    selected: bool = False
    paths: list = field(default_factory=list)   # user-owned paths to delete
    root_script: str | None = None              # run via pkexec sh -c
    user_cmd: list | None = None                # run as the user
    confirm_note: str = ""
    empty: bool = False                         # checked, nothing to clean
    failed: bool = False


# ---------------------------------------------------------------- scanners
def scan_browser_backups():
    items = []
    for p in sorted(glob.glob(os.path.join(HOME, ".config", "*-backup-crashrecovery-*"))):
        if os.path.isdir(p) and not os.path.islink(p):
            items.append(Item("Browser crash-recovery backups", os.path.basename(p),
                              "Left behind by browser crash recovery",
                              du_bytes(p), True, paths=[p]))
    return items


def scan_pacman_cache():
    if not shutil.which("paccache"):
        return []
    total, found = 0, False
    for flags in (["-dk1"], ["-duk0"]):
        out = run(["paccache", *flags]).stdout
        m = re.search(r"disk space saved:\s*([\d.]+\s*\w+)", out)
        if m:
            found = True
            total += parse_size(m.group(1)) or 0
    stale = glob.glob("/var/cache/pacman/pkg/download-*")
    if not found and not stale:
        return []
    note = "Old package versions and uninstalled packages"
    if stale:
        note += f", plus {len(stale)} interrupted download folder(s)"
    script = ("[ -e /var/lib/pacman/db.lck ] && { echo 'pacman is busy'; exit 1; }; "
              "paccache -rk1; paccache -ruk0; rm -rf /var/cache/pacman/pkg/download-*")
    return [Item("System", "Pacman package cache", note, total if found else None,
                 True, root_script=script)]


def scan_orphans():
    r = run(["pacman", "-Qdtq"])
    names = r.stdout.split()
    if not names:
        return []
    info = run(["pacman", "-Qi", *names]).stdout
    size = sum(parse_size(m) or 0 for m in re.findall(r"Installed Size\s*:\s*(.+)", info))
    return [Item("System", f"Orphaned packages ({len(names)})",
                 ", ".join(names[:8]) + (" …" if len(names) > 8 else ""),
                 size, False,
                 root_script="pacman -Rns --noconfirm " + " ".join(names),
                 confirm_note="Removes: " + ", ".join(names))]


def scan_journal():
    r = run(["journalctl", "--disk-usage"])
    m = re.search(r"take up ([\d.]+\s*\w+)", r.stdout)
    if not m:
        return []
    used = parse_size(m.group(1)) or 0
    reclaim = max(0, used - JOURNAL_KEEP_MB * 1024**2)
    if reclaim < 10 * 1024**2:
        return []
    return [Item("System", "Systemd journal",
                 f"Shrinks logs to {JOURNAL_KEEP_MB} MiB (currently {fmt_size(used)})",
                 reclaim, True,
                 root_script=f"journalctl --vacuum-size={JOURNAL_KEEP_MB}M")]


def scan_flatpak():
    if not shutil.which("flatpak"):
        return []
    r = run(["flatpak", "uninstall", "--unused"], input="n\n")
    out = r.stdout + r.stderr
    if "nothing unused" in out.lower():
        return []
    return [Item("Apps", "Unused Flatpak runtimes",
                 "Runtimes no installed Flatpak app needs", None, True,
                 user_cmd=["flatpak", "uninstall", "--unused", "-y"])]


def scan_user_cache():
    items = []
    base = os.path.join(HOME, ".cache")
    if os.path.isdir(base):
        for name in sorted(os.listdir(base)):
            p = os.path.join(base, name)
            if os.path.isdir(p) and not os.path.islink(p):
                size = du_bytes(p)
                if size and size >= CACHE_MIN_BYTES:
                    items.append(Item("Large app caches (~/.cache)", name,
                                      "Rebuilt automatically by the app", size, False,
                                      paths=[p]))
    trash = os.path.join(HOME, ".local/share/Trash")
    if os.path.isdir(trash):
        size = du_bytes(trash)
        if size and size > 1024**2:
            items.append(Item("Large app caches (~/.cache)", "Trash",
                              "Permanently deletes everything in your trash", size, False,
                              paths=[os.path.join(trash, "files"), os.path.join(trash, "info")]))
    return items


NAME_CACHE = os.path.join(HOME, ".cache", "cachy-cleaner", "steam_names.json")


def steam_name(appid):
    """Game name for a Steam app id: local cache first, then the Steam store API."""
    import json
    import urllib.request
    try:
        cache = json.load(open(NAME_CACHE))
    except (OSError, ValueError):
        cache = {}
    key = str(appid)
    if key in cache:
        return cache[key]
    try:
        url = f"https://store.steampowered.com/api/appdetails?appids={key}&filters=basic"
        with urllib.request.urlopen(url, timeout=4) as r:
            data = json.load(r)[key]
        name = data["data"]["name"] if data.get("success") else None
    except Exception:  # noqa: BLE001  offline, delisted, rate-limited
        return None
    cache[key] = name or f"App {key} (not in Steam store, maybe a tool)"
    try:
        os.makedirs(os.path.dirname(NAME_CACHE), exist_ok=True)
        json.dump(cache, open(NAME_CACHE, "w"))
    except OSError:
        pass
    return cache[key]


def scan_steam_prefixes():
    """Proton prefixes for Steam games that are no longer installed anywhere."""
    steamapps = os.path.join(HOME, ".local/share/Steam/steamapps")
    compat = os.path.join(steamapps, "compatdata")
    if not os.path.isdir(compat):
        return []
    try:
        vdf = open(os.path.join(steamapps, "libraryfolders.vdf")).read()
    except OSError:
        return []
    libs = re.findall(r'"path"\s+"([^"]+)"', vdf)
    missing = [l for l in libs if not os.path.isdir(l)]
    installed = set()
    for lib in libs:
        for f in glob.glob(os.path.join(lib, "steamapps", "appmanifest_*.acf")):
            m = re.search(r"appmanifest_(\d+)", f)
            if m:
                installed.add(int(m.group(1)))
    warn = " Some Steam library drives are not mounted, so this game may live there." if missing else ""
    items = []
    for d in sorted(os.listdir(compat)):
        # non-Steam shortcuts use huge ids; leave those alone
        if d.isdigit() and 0 < int(d) < 2**31 and int(d) not in installed:
            p = os.path.join(compat, d)
            name = steam_name(int(d))
            title = f"{name}" if name else f"Unknown game (app {d})"
            items.append(Item("Steam leftovers", title,
                              f"Proton prefix, app {d}. Game not installed; may hold Windows save games." + warn,
                              du_bytes(p), False, paths=[p]))
    return items


# (scanner, group, label shown when the scanner finds nothing)
SCANNERS = [
    (scan_pacman_cache, "System", "Pacman package cache"),
    (scan_orphans, "System", "Orphaned packages"),
    (scan_journal, "System", "Systemd journal"),
    (scan_flatpak, "Apps", "Unused Flatpak runtimes"),
    (scan_browser_backups, "Browser crash-recovery backups", "Crash-recovery backups"),
    (scan_user_cache, "Large app caches (~/.cache)", "Large caches and Trash"),
    (scan_steam_prefixes, "Steam leftovers", "Orphaned Proton prefixes"),
]


# ---------------------------------------------------------------- cleaning
def safe_remove(path):
    """Delete a path only if it is inside $HOME; never follow symlinks."""
    real_parent = os.path.realpath(os.path.dirname(path))
    if not (real_parent + os.sep).startswith(HOME + os.sep) or real_parent == HOME:
        raise PermissionError(f"refusing to delete outside home: {path}")
    if os.path.islink(path) or os.path.isfile(path):
        os.remove(path)
    elif os.path.isdir(path):
        shutil.rmtree(path)
        if os.path.basename(path) in ("files", "info"):  # keep trash skeleton
            os.makedirs(path, exist_ok=True)


def clean_item(item):
    """Return (ok, message)."""
    try:
        for p in item.paths:
            safe_remove(p)
        if item.root_script:
            r = run(["pkexec", "sh", "-c", item.root_script])
            if r.returncode != 0:
                return False, (r.stderr or r.stdout).strip() or "cancelled or failed"
        if item.user_cmd:
            r = run(item.user_cmd)
            if r.returncode != 0:
                return False, (r.stderr or r.stdout).strip() or "failed"
        return True, "done"
    except Exception as e:  # noqa: BLE001
        return False, str(e)


# ---------------------------------------------------------------- UI

class Explorer(Gtk.Box):
    """Browse folders by size. Deleting only ever moves things to the trash."""

    def __init__(self, win):
        super().__init__(orientation=Gtk.Orientation.VERTICAL)
        self.win = win
        self.root = HOME
        self.path = HOME
        self.sizes = {}
        self.gen = 0

        bar = Gtk.Box(spacing=6, margin_top=8, margin_bottom=8, margin_start=12, margin_end=12)
        self.up_btn = Gtk.Button(icon_name="go-up-symbolic", tooltip_text="Parent folder")
        self.up_btn.connect("clicked", lambda *_: self.go(os.path.dirname(self.path)))
        home_btn = Gtk.Button(label="Home")
        home_btn.connect("clicked", lambda *_: self.set_root(HOME))
        sys_btn = Gtk.Button(label="System (/)")
        sys_btn.connect("clicked", lambda *_: self.set_root("/"))
        self.path_label = Gtk.Label(xalign=0, hexpand=True)
        self.path_label.set_ellipsize(Pango.EllipsizeMode.START)
        for w in (self.up_btn, home_btn, sys_btn, self.path_label):
            bar.append(w)
        self.append(bar)

        self.status = Gtk.Label(xalign=0, margin_start=16, margin_bottom=4,
                                css_classes=["dim-label"])
        self.append(self.status)
        self.list = Gtk.ListBox(selection_mode=Gtk.SelectionMode.NONE,
                                css_classes=["boxed-list"], margin_start=12,
                                margin_end=12, margin_bottom=12)
        self.list.set_sort_func(self._sort)
        sw = Gtk.ScrolledWindow(vexpand=True)
        sw.set_child(self.list)
        self.append(sw)
        self.go(HOME)

    def _sort(self, a, b):
        sa, sb = self.sizes.get(a.path, -1), self.sizes.get(b.path, -1)
        return (sb > sa) - (sb < sa)

    def set_root(self, path):
        self.root = path
        self.go(path)

    def reload(self):
        self.go(self.path)

    def go(self, path):
        if not path.startswith(self.root):
            path = self.root
        self.path = path
        self.gen += 1
        gen = self.gen
        self.path_label.set_label(path)
        self.up_btn.set_sensitive(path != self.root)
        self.sizes = {}
        while (r := self.list.get_row_at_index(0)) is not None:
            self.list.remove(r)
        try:
            entries = list(os.scandir(path))
        except OSError as e:
            self.status.set_label(f"Cannot read folder: {e.strerror}")
            return
        self.status.set_label("Measuring…")
        self.pending = len(entries)
        self.max_size = 1
        self.bars = []
        for e in entries:
            self._add_row(e)
        if not entries:
            self.status.set_label("Empty folder")

        def work():
            from concurrent.futures import ThreadPoolExecutor, as_completed
            def measure(e):
                try:
                    if e.is_dir(follow_symlinks=False):
                        return e.path, du_bytes_x(e.path)
                    return e.path, e.stat(follow_symlinks=False).st_size
                except OSError:
                    return e.path, None
            with ThreadPoolExecutor(4) as ex:
                for f in as_completed([ex.submit(measure, e) for e in entries]):
                    if self.gen != gen:
                        return
                    GLib.idle_add(self._got_size, gen, *f.result())
        threading.Thread(target=work, daemon=True).start()

    def _add_row(self, entry):
        is_dir = entry.is_dir(follow_symlinks=False)
        row = Adw.ActionRow(title=GLib.markup_escape_text(entry.name),
                            activatable=is_dir)
        row.path = entry.path
        row.add_prefix(Gtk.Image(icon_name="folder-symbolic" if is_dir
                                 else "text-x-generic-symbolic"))
        row.size_label = Gtk.Label(label="…", css_classes=["dim-label"],
                                   width_chars=10, xalign=1)
        row.bar = Gtk.LevelBar(min_value=0, max_value=1, value=0, width_request=90,
                               valign=Gtk.Align.CENTER)
        trash = Gtk.Button(icon_name="user-trash-symbolic", valign=Gtk.Align.CENTER,
                           css_classes=["flat"], tooltip_text="Move to trash")
        trash.connect("clicked", lambda *_: self.confirm_trash(entry.path))
        for w in (row.bar, row.size_label, trash):
            row.add_suffix(w)
        if is_dir:
            row.add_suffix(Gtk.Image(icon_name="go-next-symbolic"))
            row.connect("activated", lambda *_: self.go(entry.path))
        self.list.append(row)
        self.bars.append(row)

    def _got_size(self, gen, path, size):
        if gen != self.gen:
            return
        self.sizes[path] = size if size is not None else 0
        self.max_size = max(self.max_size, size or 0)
        for row in self.bars:
            if row.path == path:
                row.size_label.set_label(fmt_size(size) if size is not None else "?")
        for row in self.bars:
            row.bar.set_value((self.sizes.get(row.path, 0)) / self.max_size)
        self.pending -= 1
        self.list.invalidate_sort()
        total = sum(self.sizes.values())
        self.status.set_label(("Measuring… " if self.pending else "") + f"Total {fmt_size(total)}")

    def confirm_trash(self, path):
        dlg = Adw.AlertDialog(heading="Move to trash?", body=path)
        dlg.add_response("cancel", "Cancel")
        dlg.add_response("trash", "Move to trash")
        dlg.set_response_appearance("trash", Adw.ResponseAppearance.DESTRUCTIVE)
        dlg.set_default_response("cancel")
        dlg.connect("response", lambda d, r: r == "trash" and self.trash(path))
        dlg.present(self.win)

    def trash(self, path):
        def work():
            r = run(["gio", "trash", "--", path])
            GLib.idle_add(self._trashed, path, r)
        threading.Thread(target=work, daemon=True).start()

    def _trashed(self, path, r):
        if r.returncode == 0:
            self.win.toast.add_toast(Adw.Toast(title="Moved to trash (empty trash to free space)"))
            self.win.update_free()
            self.reload()
        else:
            dlg = Adw.AlertDialog(heading="Could not trash", body=(r.stderr or "failed").strip())
            dlg.add_response("ok", "OK")
            dlg.present(self.win)


class Window(Adw.ApplicationWindow):
    def __init__(self, app):
        super().__init__(application=app, title="CachyOS Cleaner",
                         default_width=720, default_height=780)
        self.rows = []  # (item, check_button)
        self.busy = False

        toolbar = Adw.ToolbarView()
        header = Adw.HeaderBar()
        self.scan_btn = Gtk.Button(icon_name="view-refresh-symbolic", tooltip_text="Rescan")
        self.scan_btn.connect("clicked", self.on_refresh)
        header.pack_start(self.scan_btn)
        self.free_label = Gtk.Label(css_classes=["dim-label"])
        header.pack_end(self.free_label)
        self.views = Adw.ViewStack()
        header.set_title_widget(Adw.ViewSwitcher(
            stack=self.views, policy=Adw.ViewSwitcherPolicy.WIDE))
        toolbar.add_top_bar(header)

        self.toast = Adw.ToastOverlay()
        self.stack = Gtk.Stack()
        self.spinner_page = Adw.StatusPage(title="Scanning…",
                                           description="Measuring reclaimable space")
        self.spinner_page.set_child(Gtk.Spinner(spinning=True, width_request=32,
                                                height_request=32))
        self.page = Adw.PreferencesPage()
        self.empty_page = Adw.StatusPage(icon_name="emblem-ok-symbolic",
                                         title="Nothing to clean",
                                         description="No reclaimable space found.")
        self.stack.add_named(self.spinner_page, "scan")
        self.stack.add_named(self.page, "list")
        self.stack.add_named(self.empty_page, "empty")
        self.toast.set_child(self.stack)

        bar = Gtk.Box(spacing=12, margin_top=10, margin_bottom=10,
                      margin_start=16, margin_end=16)
        self.total_label = Gtk.Label(xalign=0, hexpand=True)
        self.clean_btn = Gtk.Button(label="Clean selected", css_classes=["suggested-action"])
        self.clean_btn.connect("clicked", self.on_clean)
        bar.append(self.total_label)
        bar.append(self.clean_btn)
        clean_box = Gtk.Box(orientation=Gtk.Orientation.VERTICAL)
        self.toast.set_vexpand(True)
        clean_box.append(self.toast)
        clean_box.append(bar)
        self.explorer = Explorer(self)
        self.views.add_titled_with_icon(clean_box, "clean", "Clean", "edit-clear-all-symbolic")
        self.views.add_titled_with_icon(self.explorer, "explore", "Disk usage",
                                        "drive-harddisk-symbolic")
        toolbar.set_content(self.views)
        self.set_content(toolbar)
        self.free_before = None
        self.update_free()
        self.scan()

    def update_free(self):
        u = shutil.disk_usage("/")
        pct = round(u.used / u.total * 100)
        self.free_label.set_label(f"/ {fmt_size(u.free)} free · {pct}% used")
        return u.free

    def on_refresh(self, *_):
        if self.views.get_visible_child_name() == "explore":
            self.explorer.reload()
            self.update_free()
        else:
            self.scan()

    # -- scanning
    def scan(self):
        if self.busy:
            return
        self.busy = True
        self.clean_btn.set_sensitive(False)
        self.scan_btn.set_sensitive(False)
        self.stack.set_visible_child_name("scan")

        def work():
            items = []
            for fn, group, label in SCANNERS:
                try:
                    found = fn()
                    items += found or [Item(group, label, "Nothing to clean", 0, empty=True)]
                except Exception as e:  # noqa: BLE001
                    items.append(Item(group, label, f"Scan failed: {e}", None,
                                      empty=True, failed=True))
            GLib.idle_add(self.show_items, items)

        threading.Thread(target=work, daemon=True).start()

    def show_items(self, items):
        self.busy = False
        self.scan_btn.set_sensitive(True)
        self.stack.remove(self.page)
        self.page = Adw.PreferencesPage()
        self.stack.add_named(self.page, "list")
        self.rows = []
        groups = {}
        for it in items:
            g = groups.get(it.group)
            if g is None:
                g = groups[it.group] = Adw.PreferencesGroup(title=it.group)
                self.page.add(g)
            row = Adw.ActionRow(title=GLib.markup_escape_text(it.title),
                                subtitle=GLib.markup_escape_text(it.subtitle),
                                subtitle_lines=2)
            check = Gtk.CheckButton(active=it.selected and not it.empty,
                                    valign=Gtk.Align.CENTER, sensitive=not it.empty)
            check.connect("toggled", lambda *_: self.update_total())
            row.add_prefix(check)
            if it.empty:
                text, css = ("Scan failed", "error") if it.failed else ("Clean ✓", "success")
                row.add_suffix(Gtk.Label(label=text, css_classes=[css]))
            else:
                row.set_activatable_widget(check)
                row.add_suffix(Gtk.Label(label=fmt_size(it.size), css_classes=["dim-label"]))
            g.add(row)
            self.rows.append((it, check))
        self.stack.set_visible_child_name("list")
        self.update_total()

    def selected(self):
        return [it for it, c in self.rows if c.get_active()]

    def update_total(self):
        sel = self.selected()
        known = sum(i.size or 0 for i in sel)
        unknown = any(i.size is None for i in sel)
        self.total_label.set_label(
            f"{len(sel)} selected · {fmt_size(known)}" + (" + more" if unknown else ""))
        self.clean_btn.set_sensitive(bool(sel) and not self.busy)

    # -- cleaning
    def on_clean(self, *_):
        sel = self.selected()
        lines = []
        for it in sel:
            lines.append(f"• {it.title} ({fmt_size(it.size)})")
            if it.confirm_note:
                lines.append(f"   {it.confirm_note}")
        dlg = Adw.AlertDialog(heading="Delete selected items?",
                              body="This cannot be undone.\n\n" + "\n".join(lines))
        dlg.add_response("cancel", "Cancel")
        dlg.add_response("clean", "Clean")
        dlg.set_response_appearance("clean", Adw.ResponseAppearance.DESTRUCTIVE)
        dlg.set_default_response("cancel")
        dlg.connect("response", lambda d, r: r == "clean" and self.do_clean(sel))
        dlg.present(self)

    def do_clean(self, sel):
        self.busy = True
        self.free_before = self.update_free()
        self.clean_btn.set_sensitive(False)
        self.scan_btn.set_sensitive(False)
        self.spinner_page.set_title("Cleaning…")
        self.stack.set_visible_child_name("scan")

        def work():
            results = [(it, *clean_item(it)) for it in sel]
            GLib.idle_add(self.clean_done, results)

        threading.Thread(target=work, daemon=True).start()

    def clean_done(self, results):
        self.busy = False
        self.spinner_page.set_title("Scanning…")
        failed = [(it, msg) for it, ok, msg in results if not ok]
        if failed:
            body = "\n".join(f"• {it.title}: {msg[:200]}" for it, msg in failed)
            dlg = Adw.AlertDialog(heading="Some items failed", body=body)
            dlg.add_response("ok", "OK")
            dlg.present(self)
        else:
            freed = self.update_free() - (self.free_before or 0)
            msg = "Cleaning finished" + (f" · freed {fmt_size(freed)}" if freed > 0 else "")
            self.toast.add_toast(Adw.Toast(title=msg))
        self.update_free()
        self.explorer.reload()
        self.scan()


class App(Adw.Application):
    def __init__(self):
        super().__init__(application_id="local.cachy.Cleaner")

    def do_startup(self):
        Adw.Application.do_startup(self)
        Gtk.Window.set_default_icon_name("local.cachy.Cleaner")

    def do_activate(self):
        (self.props.active_window or Window(self)).present()


if __name__ == "__main__":
    App().run()
