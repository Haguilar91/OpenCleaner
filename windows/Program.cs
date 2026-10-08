// Win Cleaner: scan for reclaimable disk space on Windows, review it, then clean.
// Written in C# 5 so the compiler built into Windows (csc.exe) can build it. See build.bat.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.VisualBasic.FileIO;

namespace WinCleaner
{
    // ------------------------------------------------------------------ model
    class Item
    {
        public string Group, Title, Subtitle, Note = "";
        public long Size = -1;                 // -1 = unknown
        public bool Selected, Empty, Failed, NeedsAdmin;
        public List<string> Paths = new List<string>();   // files/folders to delete
        public List<string> Roots = new List<string>();   // deletes are refused outside these
        public Action Before, After;
        public Func<string> Action;            // extra step; returns an error message or null
    }

    class CleanResult
    {
        public string Title, Error;
        public int Skipped;
    }

    // ------------------------------------------------------------------ helpers
    static class Util
    {
        public static string Fmt(long bytes)
        {
            if (bytes < 0) return "size unknown";
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double v = bytes;
            int i = 0;
            while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
            return i == 0 ? ((long)v) + " B" : v.ToString("0.0") + " " + units[i];
        }

        public static bool IsReparse(string path)
        {
            try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
            catch { return false; }
        }

        public static bool IsDir(string path)
        {
            try { return (File.GetAttributes(path) & FileAttributes.Directory) != 0; }
            catch { return false; }
        }

        // Size of a file or folder tree. Junctions and symlinks count as zero.
        public static long Size(string path)
        {
            try
            {
                FileAttributes a = File.GetAttributes(path);
                if ((a & FileAttributes.ReparsePoint) != 0) return 0;
                if ((a & FileAttributes.Directory) == 0) return new FileInfo(path).Length;
            }
            catch { return 0; }

            long total = 0;
            try
            {
                foreach (string f in Directory.GetFiles(path))
                {
                    try { total += new FileInfo(f).Length; } catch { }
                }
                foreach (string d in Directory.GetDirectories(path))
                {
                    total += Size(d);
                }
            }
            catch { }
            return total;
        }

        public static List<string> Children(string dir)
        {
            try { return Directory.GetFileSystemEntries(dir).ToList(); }
            catch { return new List<string>(); }
        }

        public static long SizeOfAll(IEnumerable<string> paths)
        {
            long t = 0;
            foreach (string p in paths) t += Size(p);
            return t;
        }

        // Deletes as much as possible; returns how many entries were skipped (in use / denied).
        public static int DeleteTree(string path)
        {
            int skipped = 0;
            try
            {
                if (IsDir(path))
                {
                    if (IsReparse(path)) { Directory.Delete(path); return 0; }
                    foreach (string c in Children(path)) skipped += DeleteTree(c);
                    try { Directory.Delete(path); } catch { skipped++; }
                }
                else
                {
                    try { File.SetAttributes(path, FileAttributes.Normal); } catch { }
                    File.Delete(path);
                }
            }
            catch { skipped++; }
            return skipped;
        }

        // True only if path is strictly inside one of the roots.
        public static bool Allowed(string path, List<string> roots)
        {
            string full;
            try { full = Path.GetFullPath(path).TrimEnd('\\'); } catch { return false; }
            foreach (string r in roots)
            {
                string root = Path.GetFullPath(r).TrimEnd('\\');
                if (root.Length < 8) continue;   // never a drive root
                if (full.Length > root.Length + 1 &&
                    full.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        public static string Env(string name)
        {
            return Environment.GetEnvironmentVariable(name) ?? "";
        }

        public static void RunQuiet(string exe, string args)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(exe, args);
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                using (Process p = Process.Start(psi)) { p.WaitForExit(30000); }
            }
            catch { }
        }

        public static bool IsAdmin()
        {
            try
            {
                return new WindowsPrincipal(WindowsIdentity.GetCurrent())
                    .IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        public static string SystemDrive()
        {
            return Path.GetPathRoot(Environment.SystemDirectory);
        }

        public static long FreeSpace()
        {
            try { return new DriveInfo(SystemDrive()).AvailableFreeSpace; } catch { return 0; }
        }
    }

    static class Native
    {
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct SHQUERYRBINFO
        {
            public int cbSize;
            public long i64Size;
            public long i64NumItems;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern int SHQueryRecycleBin(string root, ref SHQUERYRBINFO info);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern int SHEmptyRecycleBin(IntPtr hwnd, string root, uint flags);

        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();
    }

    // ------------------------------------------------------------------ scanners
    class ScanDef
    {
        public Func<List<Item>> Fn;
        public string Group, Label;
        public ScanDef(Func<List<Item>> fn, string group, string label) { Fn = fn; Group = group; Label = label; }
    }

    static class Scanners
    {
        const long CacheMin = 50L * 1024 * 1024;

        static string Win { get { return Util.Env("SystemRoot"); } }
        static string Local { get { return Util.Env("LOCALAPPDATA"); } }
        static string Profile { get { return Util.Env("USERPROFILE"); } }

        public static List<Item> RunAll()
        {
            List<ScanDef> defs = new List<ScanDef>();
            defs.Add(new ScanDef(UserTemp, "Temporary files", "Your temp folder"));
            defs.Add(new ScanDef(WindowsTemp, "Temporary files", "Windows temp folder"));
            defs.Add(new ScanDef(WindowsUpdate, "Windows", "Windows Update download cache"));
            defs.Add(new ScanDef(DeliveryOpt, "Windows", "Delivery Optimization cache"));
            defs.Add(new ScanDef(CrashDumps, "Windows", "Crash dumps and error reports"));
            defs.Add(new ScanDef(RecycleBin, "Windows", "Recycle Bin"));
            defs.Add(new ScanDef(BrowserCaches, "Browser caches", "Browser caches"));
            defs.Add(new ScanDef(DevCaches, "Developer caches", "Package manager caches"));
            defs.Add(new ScanDef(Steam, "Steam", "Steam leftovers"));

            List<Item> all = new List<Item>();
            foreach (ScanDef d in defs)
            {
                try
                {
                    List<Item> found = d.Fn();
                    if (found.Count == 0)
                    {
                        Item e = new Item();
                        e.Group = d.Group; e.Title = d.Label; e.Subtitle = "Nothing to clean";
                        e.Size = 0; e.Empty = true;
                        all.Add(e);
                    }
                    else all.AddRange(found);
                }
                catch (Exception ex)
                {
                    Item e = new Item();
                    e.Group = d.Group; e.Title = d.Label; e.Subtitle = "Scan failed: " + ex.Message;
                    e.Empty = true; e.Failed = true;
                    all.Add(e);
                }
            }
            return all;
        }

        public static CleanResult Clean(Item it)
        {
            CleanResult r = new CleanResult();
            r.Title = it.Title;
            try
            {
                if (it.Before != null) it.Before();
                foreach (string p in it.Paths)
                {
                    if (!Util.Allowed(p, it.Roots))
                    {
                        r.Error = "Refusing to delete outside the allowed folders: " + p;
                        break;
                    }
                    r.Skipped += Util.DeleteTree(p);
                }
                if (r.Error == null && it.Action != null) r.Error = it.Action();
            }
            catch (Exception ex) { r.Error = ex.Message; }
            finally
            {
                try { if (it.After != null) it.After(); } catch { }
            }
            return r;
        }

        static Item Folder(string group, string title, string subtitle, string root,
                           List<string> paths, bool selected, bool admin)
        {
            Item it = new Item();
            it.Group = group; it.Title = title; it.Subtitle = subtitle;
            it.Paths = paths; it.Roots.Add(root);
            it.Size = Util.SizeOfAll(paths);
            it.Selected = selected; it.NeedsAdmin = admin;
            return it;
        }

        static List<Item> One(Item it) { List<Item> l = new List<Item>(); l.Add(it); return l; }

        static List<Item> UserTemp()
        {
            string temp = Path.GetTempPath().TrimEnd('\\');
            List<string> kids = Util.Children(temp);
            Item it = Folder("Temporary files", "Your temp folder", temp + "  (files in use are skipped)",
                             temp, kids, true, false);
            return it.Size < 5L * 1024 * 1024 ? new List<Item>() : One(it);
        }

        static List<Item> WindowsTemp()
        {
            string dir = Path.Combine(Win, "Temp");
            List<string> kids = Util.Children(dir);
            Item it = Folder("Temporary files", "Windows temp folder", dir, dir, kids, true, true);
            return it.Size < 5L * 1024 * 1024 ? new List<Item>() : One(it);
        }

        static List<Item> WindowsUpdate()
        {
            string dir = Path.Combine(Win, "SoftwareDistribution\\Download");
            if (!Directory.Exists(dir)) return new List<Item>();
            Item it = Folder("Windows", "Windows Update download cache",
                             "Already-installed update files. Windows re-downloads what it needs.",
                             dir, Util.Children(dir), true, true);
            if (it.Size < 20L * 1024 * 1024) return new List<Item>();
            it.Before = delegate { Util.RunQuiet("net.exe", "stop wuauserv"); Util.RunQuiet("net.exe", "stop bits"); };
            it.After = delegate { Util.RunQuiet("net.exe", "start bits"); Util.RunQuiet("net.exe", "start wuauserv"); };
            return One(it);
        }

        static List<Item> DeliveryOpt()
        {
            string dir = Path.Combine(Win,
                "ServiceProfiles\\NetworkService\\AppData\\Local\\Microsoft\\Windows\\DeliveryOptimization\\Cache");
            if (!Directory.Exists(dir)) return new List<Item>();
            Item it = Folder("Windows", "Delivery Optimization cache",
                             "Update files cached for sharing with other PCs", dir, Util.Children(dir), true, true);
            return it.Size < 20L * 1024 * 1024 ? new List<Item>() : One(it);
        }

        static List<Item> CrashDumps()
        {
            List<Item> list = new List<Item>();

            string user = Path.Combine(Local, "CrashDumps");
            if (Directory.Exists(user))
            {
                Item it = Folder("Windows", "Your crash dumps", user, user, Util.Children(user), true, false);
                if (it.Size > 1024 * 1024) list.Add(it);
            }

            string progData = Util.Env("ProgramData");
            string[] sysDirs = {
                Path.Combine(Win, "Minidump"),
                Path.Combine(progData, "Microsoft\\Windows\\WER\\ReportArchive"),
                Path.Combine(progData, "Microsoft\\Windows\\WER\\ReportQueue"),
            };
            Item sys = new Item();
            sys.Group = "Windows"; sys.Title = "System crash dumps and error reports";
            sys.Subtitle = "Minidumps and Windows Error Reporting archives";
            sys.NeedsAdmin = true; sys.Selected = true;
            foreach (string d in sysDirs)
            {
                if (!Directory.Exists(d)) continue;
                sys.Roots.Add(d);
                sys.Paths.AddRange(Util.Children(d));
            }
            string mem = Path.Combine(Win, "MEMORY.DMP");
            if (File.Exists(mem)) { sys.Roots.Add(Win); sys.Paths.Add(mem); }
            sys.Size = Util.SizeOfAll(sys.Paths);
            if (sys.Size > 1024 * 1024) list.Add(sys);
            return list;
        }

        static List<Item> RecycleBin()
        {
            Native.SHQUERYRBINFO info = new Native.SHQUERYRBINFO();
            info.cbSize = Marshal.SizeOf(typeof(Native.SHQUERYRBINFO));
            if (Native.SHQueryRecycleBin(null, ref info) != 0 || info.i64NumItems == 0)
                return new List<Item>();
            Item it = new Item();
            it.Group = "Windows"; it.Title = "Recycle Bin";
            it.Subtitle = info.i64NumItems + " items. Permanently deletes everything in the Recycle Bin.";
            it.Size = info.i64Size;
            it.Action = delegate
            {
                int hr = Native.SHEmptyRecycleBin(IntPtr.Zero, null, 0x7);
                return hr == 0 ? null : "Could not empty the Recycle Bin (error " + hr + ")";
            };
            return One(it);
        }

        static List<Item> BrowserCaches()
        {
            List<Item> list = new List<Item>();
            string[][] chromium = new string[][] {
                new string[] { "Google Chrome", "chrome", Path.Combine(Local, "Google\\Chrome\\User Data") },
                new string[] { "Microsoft Edge", "msedge", Path.Combine(Local, "Microsoft\\Edge\\User Data") },
                new string[] { "Brave", "brave", Path.Combine(Local, "BraveSoftware\\Brave-Browser\\User Data") },
            };
            foreach (string[] b in chromium)
            {
                if (!Directory.Exists(b[2])) continue;
                List<string> paths = new List<string>();
                foreach (string profile in Util.Children(b[2]))
                {
                    string n = Path.GetFileName(profile);
                    if (n != "Default" && !n.StartsWith("Profile ")) continue;
                    foreach (string c in new string[] { "Cache", "Code Cache", "GPUCache" })
                    {
                        string p = Path.Combine(profile, c);
                        if (Directory.Exists(p)) paths.Add(p);
                    }
                }
                AddBrowser(list, b[0], b[1], b[2], paths);
            }

            string ff = Path.Combine(Local, "Mozilla\\Firefox\\Profiles");
            if (Directory.Exists(ff))
            {
                List<string> paths = new List<string>();
                foreach (string profile in Util.Children(ff))
                {
                    string p = Path.Combine(profile, "cache2");
                    if (Directory.Exists(p)) paths.Add(p);
                }
                AddBrowser(list, "Firefox", "firefox", ff, paths);
            }
            return list;
        }

        static void AddBrowser(List<Item> list, string name, string process, string root, List<string> paths)
        {
            long size = Util.SizeOfAll(paths);
            if (size < CacheMin) return;
            bool running = Process.GetProcessesByName(process).Length > 0;
            Item it = new Item();
            it.Group = "Browser caches"; it.Title = name + " cache";
            it.Subtitle = running ? "Running now. Close it first, or in-use files are skipped."
                                  : "Cached pages and images. Logins and history are kept.";
            it.Paths = paths; it.Roots.Add(root); it.Size = size;
            list.Add(it);
        }


        // ---------------------------------------------------------- Steam
        static string SteamRoot()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey("Software\\Valve\\Steam"))
                {
                    if (k == null) return null;
                    string p = k.GetValue("SteamPath") as string;
                    return p == null ? null : p.Replace('/', '\\');
                }
            }
            catch { return null; }
        }

        static List<string> SteamLibraries(string root, out bool missing)
        {
            missing = false;
            List<string> libs = new List<string>();
            libs.Add(root);
            try
            {
                string vdf = File.ReadAllText(Path.Combine(root, "steamapps\\libraryfolders.vdf"));
                foreach (Match m in Regex.Matches(vdf, "\"path\"\\s+\"([^\"]+)\""))
                {
                    string p = m.Groups[1].Value.Replace("\\\\", "\\");
                    if (p.TrimEnd('\\').Equals(root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) continue;
                    if (Directory.Exists(p)) libs.Add(p); else missing = true;
                }
            }
            catch { }
            return libs;
        }

        // Game name from the Steam store, cached on disk. Null when offline.
        static string StoreName(string id)
        {
            string cacheFile = Path.Combine(Local, "WinCleaner\\steam_names.txt");
            Dictionary<string, string> cache = new Dictionary<string, string>();
            try
            {
                foreach (string line in File.ReadAllLines(cacheFile))
                {
                    int t = line.IndexOf('\t');
                    if (t > 0) cache[line.Substring(0, t)] = line.Substring(t + 1);
                }
            }
            catch { }
            string v;
            if (cache.TryGetValue(id, out v)) return v;

            string name = null;
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;   // TLS 1.2
                HttpWebRequest rq = (HttpWebRequest)WebRequest.Create(
                    "https://store.steampowered.com/api/appdetails?appids=" + id + "&filters=basic");
                rq.Timeout = 4000;
                using (WebResponse rs = rq.GetResponse())
                using (StreamReader sr = new StreamReader(rs.GetResponseStream(), Encoding.UTF8))
                {
                    Match m = Regex.Match(sr.ReadToEnd(), "\"name\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
                    if (m.Success) name = Regex.Unescape(m.Groups[1].Value);
                }
            }
            catch { return null; }   // offline or blocked: show the id, try again next time
            v = name ?? ("App " + id + " (not in Steam store)");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(cacheFile));
                File.AppendAllText(cacheFile, id + "\t" + v + "\n");
            }
            catch { }
            return v;
        }

        static List<Item> Steam()
        {
            List<Item> list = new List<Item>();
            string root = SteamRoot();
            if (root == null || !Directory.Exists(root)) return list;

            bool missing;
            List<string> libs = SteamLibraries(root, out missing);
            HashSet<string> installed = new HashSet<string>();
            foreach (string lib in libs)
            {
                string sa = Path.Combine(lib, "steamapps");
                if (!Directory.Exists(sa)) continue;
                foreach (string f in Directory.GetFiles(sa, "appmanifest_*.acf"))
                {
                    Match m = Regex.Match(Path.GetFileName(f), "appmanifest_(\\d+)\\.acf");
                    if (m.Success) installed.Add(m.Groups[1].Value);
                }
            }
            string warn = missing ? " Some Steam library drives are not connected, so the game may live there." : "";

            // shader caches of games that are no longer installed
            foreach (string lib in libs)
            {
                string sc = Path.Combine(lib, "steamapps\\shadercache");
                if (!Directory.Exists(sc)) continue;
                foreach (string dir in Util.Children(sc))
                {
                    string id = Path.GetFileName(dir);
                    if (!Regex.IsMatch(id, "^\\d+$") || installed.Contains(id)) continue;
                    long size = Util.Size(dir);
                    if (size < 1024 * 1024) continue;
                    string name = StoreName(id);
                    Item it = new Item();
                    it.Group = "Steam";
                    it.Title = "Shader cache: " + (name ?? ("app " + id));
                    it.Subtitle = "Game not installed (app " + id + "). Steam rebuilds this if you reinstall." + warn;
                    it.Paths.Add(dir); it.Roots.Add(sc); it.Size = size; it.Selected = true;
                    list.Add(it);
                }
            }

            bool running = Process.GetProcessesByName("steam").Length > 0;

            string html = Path.Combine(root, "config\\htmlcache");
            if (Directory.Exists(html))
            {
                List<string> kids = Util.Children(html);
                long size = Util.SizeOfAll(kids);
                if (size >= CacheMin)
                {
                    Item it = new Item();
                    it.Group = "Steam"; it.Title = "Steam browser cache";
                    it.Subtitle = running ? "Steam is running. Close it first, or in-use files are skipped."
                                          : "Cache of the Steam store and community pages";
                    it.Paths = kids; it.Roots.Add(html); it.Size = size;
                    list.Add(it);
                }
            }

            Item part = new Item();
            part.Group = "Steam"; part.Title = "Unfinished game downloads";
            part.Subtitle = "Partial downloads. Don't clean while a download you want to resume is paused.";
            foreach (string lib in libs)
            {
                foreach (string sub in new string[] { "downloading", "temp" })
                {
                    string d = Path.Combine(lib, "steamapps\\" + sub);
                    if (!Directory.Exists(d)) continue;
                    part.Roots.Add(d);
                    part.Paths.AddRange(Util.Children(d));
                }
            }
            part.Size = Util.SizeOfAll(part.Paths);
            if (part.Size >= 10L * 1024 * 1024) list.Add(part);
            return list;
        }

        static List<Item> DevCaches()
        {
            List<Item> list = new List<Item>();
            string[][] spots = new string[][] {
                new string[] { "npm cache", Path.Combine(Local, "npm-cache") },
                new string[] { "pip cache", Path.Combine(Local, "pip\\Cache") },
                new string[] { "Yarn cache", Path.Combine(Local, "Yarn\\Cache") },
                new string[] { "NuGet cache", Path.Combine(Local, "NuGet\\v3-cache") },
                new string[] { "Gradle caches", Path.Combine(Profile, ".gradle\\caches") },
            };
            foreach (string[] s in spots)
            {
                if (!Directory.Exists(s[1])) continue;
                List<string> kids = Util.Children(s[1]);
                long size = Util.SizeOfAll(kids);
                if (size < CacheMin) continue;
                Item it = new Item();
                it.Group = "Developer caches"; it.Title = s[0];
                it.Subtitle = "Re-downloaded automatically when needed";
                it.Paths = kids; it.Roots.Add(s[1]); it.Size = size;
                list.Add(it);
            }
            return list;
        }
    }

    // ------------------------------------------------------------------ UI
    class SizeComparer : System.Collections.IComparer
    {
        public int Compare(object a, object b)
        {
            long sa = ((ListViewItem)a).Tag is long ? (long)((ListViewItem)a).Tag : -1;
            long sb = ((ListViewItem)b).Tag is long ? (long)((ListViewItem)b).Tag : -1;
            return sb.CompareTo(sa);
        }
    }

    class MainForm : Form
    {
        readonly bool isAdmin = Util.IsAdmin();
        TabControl tabs = new TabControl();
        Label freeLabel = new Label();

        // Clean tab
        ListView cleanList = new ListView();
        Label summary = new Label();
        Button scanBtn = new Button(), cleanBtn = new Button(), adminBtn = new Button();
        List<Item> items = new List<Item>();
        bool busy, populating;

        // Disk usage tab
        ListView diskList = new ListView();
        Label pathLabel = new Label(), diskStatus = new Label();
        Button upBtn = new Button(), homeBtn = new Button(), trashBtn = new Button();
        ComboBox driveBox = new ComboBox();
        string root, current;
        int gen;
        long totalSize;
        int pending;
        DateTime lastSort = DateTime.MinValue;

        public MainForm()
        {
            Text = "Win Cleaner";
            Width = 940; Height = 680;
            MinimumSize = new Size(760, 520);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            Panel header = new Panel();
            header.Dock = DockStyle.Top; header.Height = 34;
            freeLabel.Dock = DockStyle.Right; freeLabel.Width = 320;
            freeLabel.TextAlign = ContentAlignment.MiddleRight;
            freeLabel.ForeColor = Color.DimGray;
            Label title = new Label();
            title.Text = "  Win Cleaner" + (isAdmin ? "  (administrator)" : "");
            title.Dock = DockStyle.Fill; title.TextAlign = ContentAlignment.MiddleLeft;
            title.Font = new Font("Segoe UI Semibold", 11f);
            header.Controls.Add(title);
            header.Controls.Add(freeLabel);

            tabs.Dock = DockStyle.Fill;
            TabPage cleanTab = new TabPage("Clean");
            TabPage diskTab = new TabPage("Disk usage");
            tabs.TabPages.Add(cleanTab);
            tabs.TabPages.Add(diskTab);
            Controls.Add(tabs);
            Controls.Add(header);
            header.BringToFront();   // keep header above the tab control

            BuildCleanTab(cleanTab);
            BuildDiskTab(diskTab);
            UpdateFree();
            Shown += delegate { Scan(); LoadDisk(Environment.GetEnvironmentVariable("USERPROFILE")); };
        }

        void UpdateFree()
        {
            try
            {
                DriveInfo d = new DriveInfo(Util.SystemDrive());
                double used = 100.0 * (d.TotalSize - d.AvailableFreeSpace) / d.TotalSize;
                freeLabel.Text = Util.SystemDrive() + "  " + Util.Fmt(d.AvailableFreeSpace) + " free  |  "
                                 + used.ToString("0") + "% used   ";
            }
            catch { }
        }

        // ---------------------------------------------------------- clean tab
        void BuildCleanTab(TabPage page)
        {
            cleanList.Dock = DockStyle.Fill;
            cleanList.View = View.Details;
            cleanList.CheckBoxes = true;
            cleanList.FullRowSelect = true;
            cleanList.ShowGroups = true;
            cleanList.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            cleanList.Columns.Add("Item", 260);
            cleanList.Columns.Add("Details", 440);
            cleanList.Columns.Add("Size", 110, HorizontalAlignment.Right);
            cleanList.ItemCheck += OnItemCheck;
            cleanList.ItemChecked += delegate { if (!populating) UpdateSummary(); };

            Panel bar = new Panel();
            bar.Dock = DockStyle.Bottom; bar.Height = 50;

            scanBtn.Text = "Rescan"; scanBtn.SetBounds(10, 10, 90, 30);
            scanBtn.Click += delegate { Scan(); };
            adminBtn.Text = "Restart as administrator"; adminBtn.SetBounds(110, 10, 170, 30);
            adminBtn.Visible = !isAdmin;
            adminBtn.Click += OnRestartAdmin;
            cleanBtn.Text = "Clean selected"; cleanBtn.Width = 130; cleanBtn.Height = 30;
            cleanBtn.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            cleanBtn.Click += OnClean;
            summary.AutoSize = false; summary.Width = 260; summary.Height = 30;
            summary.TextAlign = ContentAlignment.MiddleRight;
            summary.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            bar.Controls.Add(scanBtn); bar.Controls.Add(adminBtn);
            bar.Controls.Add(summary); bar.Controls.Add(cleanBtn);
            bar.Resize += delegate
            {
                cleanBtn.Location = new Point(bar.Width - cleanBtn.Width - 10, 10);
                summary.Location = new Point(cleanBtn.Left - summary.Width - 10, 10);
            };
            page.Controls.Add(cleanList);
            page.Controls.Add(bar);
            cleanList.BringToFront();
        }

        bool Disabled(Item it) { return it.Empty || (it.NeedsAdmin && !isAdmin); }

        void OnItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (populating) return;
            Item it = cleanList.Items[e.Index].Tag as Item;
            if (it != null && Disabled(it)) e.NewValue = e.CurrentValue;
        }

        List<Item> Selected()
        {
            List<Item> sel = new List<Item>();
            foreach (ListViewItem lvi in cleanList.Items)
            {
                Item it = lvi.Tag as Item;
                if (lvi.Checked && it != null && !Disabled(it)) sel.Add(it);
            }
            return sel;
        }

        void UpdateSummary()
        {
            List<Item> sel = Selected();
            long known = 0; bool unknown = false;
            foreach (Item it in sel) { if (it.Size >= 0) known += it.Size; else unknown = true; }
            summary.Text = sel.Count + " selected  |  " + Util.Fmt(known) + (unknown ? " + more" : "");
            cleanBtn.Enabled = sel.Count > 0 && !busy;
        }

        void SetBusy(bool on, string text)
        {
            busy = on;
            scanBtn.Enabled = !on;
            cleanBtn.Enabled = !on;
            UseWaitCursor = on;
            if (on) summary.Text = text;
        }

        async void Scan()
        {
            if (busy) return;
            SetBusy(true, "Scanning...");
            List<Item> found;
            try { found = await Task.Run(() => Scanners.RunAll()); }
            catch (Exception ex) { found = new List<Item>(); MessageBox.Show(ex.Message); }
            items = found;
            Populate();
            SetBusy(false, "");
            UpdateSummary();
            UpdateFree();
        }

        void Populate()
        {
            populating = true;
            cleanList.BeginUpdate();
            cleanList.Items.Clear();
            cleanList.Groups.Clear();
            Dictionary<string, ListViewGroup> groups = new Dictionary<string, ListViewGroup>();
            foreach (Item it in items)
            {
                ListViewGroup g;
                if (!groups.TryGetValue(it.Group, out g))
                {
                    g = new ListViewGroup(it.Group);
                    groups[it.Group] = g;
                    cleanList.Groups.Add(g);
                }
                string sizeText = it.Empty ? (it.Failed ? "Scan failed" : "Clean ✓")
                                : (it.NeedsAdmin && !isAdmin ? "Needs admin" : Util.Fmt(it.Size));
                ListViewItem lvi = new ListViewItem(it.Title);
                lvi.SubItems.Add(it.Subtitle);
                lvi.SubItems.Add(sizeText);
                lvi.Group = g;
                lvi.Tag = it;
                lvi.Checked = it.Selected && !Disabled(it);
                if (it.Empty) lvi.ForeColor = it.Failed ? Color.Firebrick : Color.SeaGreen;
                else if (Disabled(it)) lvi.ForeColor = Color.Gray;
                cleanList.Items.Add(lvi);
            }
            cleanList.EndUpdate();
            populating = false;
        }

        async void OnClean(object sender, EventArgs e)
        {
            List<Item> sel = Selected();
            if (sel.Count == 0 || busy) return;
            StringBuilder sb = new StringBuilder("This cannot be undone.\n\n");
            foreach (Item it in sel) sb.AppendLine("  • " + it.Title + "  (" + Util.Fmt(it.Size) + ")");
            if (MessageBox.Show(this, sb.ToString(), "Delete selected items?",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2)
                != DialogResult.Yes) return;

            long before = Util.FreeSpace();
            SetBusy(true, "Cleaning...");
            List<CleanResult> results = await Task.Run(() =>
            {
                List<CleanResult> rs = new List<CleanResult>();
                foreach (Item it in sel) rs.Add(Scanners.Clean(it));
                return rs;
            });
            SetBusy(false, "");
            UpdateFree();

            long freed = Util.FreeSpace() - before;
            StringBuilder msg = new StringBuilder("Cleaning finished");
            if (freed > 0) msg.Append("  |  freed " + Util.Fmt(freed));
            int skipped = results.Sum(r => r.Skipped);
            if (skipped > 0) msg.Append("\n\n" + skipped + " file(s) were in use and skipped.");
            foreach (CleanResult r in results)
                if (r.Error != null) msg.Append("\n\n" + r.Title + ": " + r.Error);
            MessageBox.Show(this, msg.ToString(), "Win Cleaner");
            Scan();
            LoadDisk(current);
        }

        void OnRestartAdmin(object sender, EventArgs e)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(Application.ExecutablePath);
                psi.Verb = "runas"; psi.UseShellExecute = true;
                Process.Start(psi);
                Close();
            }
            catch (Win32Exception) { /* user cancelled the UAC prompt */ }
        }

        // ---------------------------------------------------------- disk usage tab
        void BuildDiskTab(TabPage page)
        {
            Panel top = new Panel();
            top.Dock = DockStyle.Top; top.Height = 40;
            upBtn.Text = "Up"; upBtn.SetBounds(10, 6, 50, 28);
            upBtn.Click += delegate
            {
                string parent = Path.GetDirectoryName(current.TrimEnd('\\'));
                if (parent != null) LoadDisk(parent);
            };
            homeBtn.Text = "My folder"; homeBtn.SetBounds(68, 6, 80, 28);
            homeBtn.Click += delegate
            {
                root = Util.Env("USERPROFILE");
                LoadDisk(root);
            };
            driveBox.DropDownStyle = ComboBoxStyle.DropDownList;
            driveBox.SetBounds(156, 8, 70, 28);
            foreach (DriveInfo d in DriveInfo.GetDrives())
                if (d.DriveType == DriveType.Fixed && d.IsReady) driveBox.Items.Add(d.Name);
            driveBox.SelectedIndexChanged += delegate
            {
                root = driveBox.SelectedItem.ToString();
                LoadDisk(root);
            };
            pathLabel.SetBounds(236, 6, 600, 28);
            pathLabel.TextAlign = ContentAlignment.MiddleLeft;
            pathLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            top.Controls.Add(upBtn); top.Controls.Add(homeBtn);
            top.Controls.Add(driveBox); top.Controls.Add(pathLabel);

            diskList.Dock = DockStyle.Fill;
            diskList.View = View.Details;
            diskList.FullRowSelect = true;
            diskList.MultiSelect = false;
            diskList.Columns.Add("Name", 460);
            diskList.Columns.Add("Type", 80);
            diskList.Columns.Add("Size", 110, HorizontalAlignment.Right);
            diskList.ListViewItemSorter = new SizeComparer();
            diskList.DoubleClick += delegate
            {
                if (diskList.SelectedItems.Count == 0) return;
                string p = diskList.SelectedItems[0].Name;
                if (Util.IsDir(p)) LoadDisk(p);
            };

            Panel bottom = new Panel();
            bottom.Dock = DockStyle.Bottom; bottom.Height = 44;
            trashBtn.Text = "Move selected to Recycle Bin"; trashBtn.SetBounds(10, 7, 210, 30);
            trashBtn.Click += OnTrash;
            diskStatus.SetBounds(230, 7, 600, 30);
            diskStatus.TextAlign = ContentAlignment.MiddleLeft;
            diskStatus.ForeColor = Color.DimGray;
            bottom.Controls.Add(trashBtn); bottom.Controls.Add(diskStatus);

            page.Controls.Add(diskList);
            page.Controls.Add(top);
            page.Controls.Add(bottom);
            diskList.BringToFront();

            root = Util.Env("USERPROFILE");
        }

        void LoadDisk(string path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;
            current = path;
            pathLabel.Text = path;
            upBtn.Enabled = Path.GetDirectoryName(path.TrimEnd('\\')) != null;
            int myGen = ++gen;
            diskList.BeginUpdate();
            diskList.Items.Clear();
            List<string> entries = Util.Children(path);
            foreach (string e in entries)
            {
                bool dir = Util.IsDir(e);
                ListViewItem lvi = new ListViewItem(Path.GetFileName(e));
                lvi.Name = e;
                lvi.Tag = -1L;
                lvi.SubItems.Add(dir ? "Folder" : "File");
                lvi.SubItems.Add("...");
                diskList.Items.Add(lvi);
            }
            diskList.EndUpdate();
            totalSize = 0; pending = entries.Count;
            diskStatus.Text = entries.Count == 0 ? "Empty folder" : "Measuring...";

            if (entries.Count == 0) return;
            Task.Run(() =>
            {
                Parallel.ForEach(entries, new ParallelOptions { MaxDegreeOfParallelism = 4 }, e =>
                {
                    if (gen != myGen) return;
                    long s = Util.Size(e);
                    try { BeginInvoke(new Action(() => SetSize(myGen, e, s))); } catch { }
                });
            });
        }

        void SetSize(int myGen, string path, long size)
        {
            if (myGen != gen) return;
            ListViewItem[] found = diskList.Items.Find(path, false);
            if (found.Length > 0)
            {
                found[0].Tag = size;
                found[0].SubItems[2].Text = Util.Fmt(size);
            }
            totalSize += size;
            pending--;
            if (pending == 0 || (DateTime.Now - lastSort).TotalMilliseconds > 400)
            {
                diskList.Sort();
                lastSort = DateTime.Now;
            }
            diskStatus.Text = (pending > 0 ? "Measuring...  " : "") + "Total " + Util.Fmt(totalSize);
        }

        void OnTrash(object sender, EventArgs e)
        {
            if (diskList.SelectedItems.Count == 0) return;
            string p = diskList.SelectedItems[0].Name;
            if (MessageBox.Show(this, p, "Move to Recycle Bin?", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) != DialogResult.Yes) return;
            try
            {
                if (Util.IsDir(p))
                    FileSystem.DeleteDirectory(p, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                else
                    FileSystem.DeleteFile(p, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not move to Recycle Bin"); }
            UpdateFree();
            LoadDisk(current);
        }
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            try { Native.SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
