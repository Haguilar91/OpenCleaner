"""Runs the real GTK window under Xvfb in a container, walks through every page and saves screenshots.

Needs DISPLAY (xvfb-run), ImageMagick's `import`, and /out for screenshots. Fails on any Python
traceback or GTK critical in the output."""
import importlib.util
import os
import subprocess
import sys

spec = importlib.util.spec_from_file_location("oc", os.path.join(os.path.dirname(__file__), "..", "opencleaner.py"))
oc = importlib.util.module_from_spec(spec)
sys.modules["oc"] = oc
spec.loader.exec_module(oc)
from gi.repository import GLib  # noqa: E402

OUT = os.environ.get("OUT", "/out")
TAG = os.environ.get("TAG", "linux")
os.makedirs(OUT, exist_ok=True)
state = {"win": None}


def shot(name):
    subprocess.run(["import", "-window", "root", f"{OUT}/{TAG}-{name}.png"], check=False)
    print("screenshot", name, flush=True)


class App(oc.App):
    def do_activate(self):
        super().do_activate()
        state["win"] = self.props.active_window
        win = state["win"]
        if os.environ.get("TAG") == "docs":
            win.set_default_size(1180, 820)     # docs screenshots: wide enough for every label
        steps = [
            (8, "clean", lambda: None),
            (2, "disk-list", lambda: (win.views.set_visible_child_name("explore"),
                                      win.explorer.set_root(os.environ.get("SHOT_ROOT", "/usr")))),
            (6, "disk-map", lambda: win.explorer.map_btn.set_active(True)),
            (2, "disk-sort-oldest", lambda: win.explorer.sort_dd.set_selected(1)),
            (2, "disk-top5", lambda: win.explorer.top_dd.set_selected(1)),
            (2, "disk-selected", lambda: (win.explorer.map_btn.set_active(False), win.explorer.top_dd.set_selected(0),
                                          win.explorer._set_selected([e["path"] for e in win.explorer.ordered()[:2]]))),
            (2, "map-selected", lambda: win.explorer.map_btn.set_active(True)),
            (2, "permanent-mode", lambda: win.explorer.delete_dd.set_selected(1)),
            (2, "apps", lambda: win.views.set_visible_child_name("apps")),
            (3, "apps-sorted", lambda: win.apps_page.sort_dd.set_selected(1)),
        ]
        t = 0
        for delay, name, action in steps:
            t += delay

            def run(name=name, action=action):
                try:
                    action()
                except Exception as e:  # noqa: BLE001
                    print("Traceback: step", name, repr(e), flush=True)
                GLib.timeout_add_seconds(2, lambda: (shot(name), False)[1])
                return False
            GLib.timeout_add_seconds(t, run)
        GLib.timeout_add_seconds(t + 5, lambda: (self.quit(), False)[1])


sys.exit(App().run([]))
