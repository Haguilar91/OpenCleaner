// OpenCleaner: scan for reclaimable disk space on Windows, review it, then clean.
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
using System.Reflection;

[assembly: AssemblyTitle("OpenCleaner")]
[assembly: AssemblyProduct("OpenCleaner")]
[assembly: AssemblyVersion("1.2.0.0")]
[assembly: AssemblyFileVersion("1.2.0.0")]

namespace OpenCleaner
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

        // Uses Windows' own Microsoft.VisualBasic.FileIO.FileSystem through reflection, so the
        // program can be compiled on systems that do not have that library (e.g. Mono on Linux).
        public static void SendToRecycleBin(string path)
        {
            Assembly vb = Assembly.Load(
                "Microsoft.VisualBasic, Version=10.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a");
            Type fs = vb.GetType("Microsoft.VisualBasic.FileIO.FileSystem", true);
            Type ui = vb.GetType("Microsoft.VisualBasic.FileIO.UIOption", true);
            Type rec = vb.GetType("Microsoft.VisualBasic.FileIO.RecycleOption", true);
            object onlyErrors = Enum.Parse(ui, "OnlyErrorDialogs");
            object toBin = Enum.Parse(rec, "SendToRecycleBin");
            string method = IsDir(path) ? "DeleteDirectory" : "DeleteFile";
            try
            {
                fs.GetMethod(method, new Type[] { typeof(string), ui, rec })
                  .Invoke(null, new object[] { path, onlyErrors, toBin });
            }
            catch (TargetInvocationException ex)
            {
                throw ex.InnerException ?? ex;
            }
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
            string cacheFile = Path.Combine(Local, "OpenCleaner\\steam_names.txt");
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
    // ------------------------------------------------------------------ disk map
    class DiskEntry
    {
        public string Path;
        public bool IsDir;
        public DateTime Modified;
        public long Size = -1;                 // -1 = not measured yet
    }

    // Squarified treemap layout plus the file-type colours used by the disk map.
    static class Treemap
    {
        public static readonly string[] KindNames = { "Folders", "Images", "Video", "Audio", "Documents", "Archives", "Other" };
        public static readonly Color[] KindColors =
        {
            Color.FromArgb(37, 99, 235), Color.FromArgb(46, 158, 91), Color.FromArgb(140, 84, 204),
            Color.FromArgb(224, 133, 36), Color.FromArgb(26, 153, 158), Color.FromArgb(199, 69, 69),
            Color.FromArgb(107, 117, 138)
        };
        static readonly string[][] KindExt =
        {
            null,
            new string[] { ".jpg", ".jpeg", ".png", ".gif", ".heic", ".webp", ".bmp", ".tif", ".tiff", ".svg", ".raw", ".dng" },
            new string[] { ".mp4", ".mov", ".m4v", ".mkv", ".avi", ".webm", ".wmv" },
            new string[] { ".mp3", ".flac", ".m4a", ".aac", ".wav", ".ogg", ".opus", ".wma" },
            new string[] { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".md", ".csv", ".rtf", ".epub" },
            new string[] { ".zip", ".iso", ".7z", ".rar", ".tar", ".gz", ".cab", ".msi" }
        };

        // Index into KindNames / KindColors.
        public static int KindOf(string path, bool isDir)
        {
            if (isDir) return 0;
            string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            for (int k = 1; k < KindExt.Length; k++)
                if (Array.IndexOf(KindExt[k], ext) >= 0) return k;
            return 6;
        }

        // sizes must be positive and sorted largest first. Returns one rectangle per size, in order.
        public static List<RectangleF> Layout(List<double> sizes, float width, float height)
        {
            List<RectangleF> result = new List<RectangleF>();
            double total = sizes.Sum();
            if (total <= 0 || width <= 0 || height <= 0) return result;
            double scale = (double)width * height / total;
            List<double> areas = sizes.Select(s => s * scale).ToList();
            double x = 0, y = 0, w = width, h = height;
            int i = 0;
            while (i < areas.Count)
            {
                double side = Math.Min(w, h);
                int end = i + 1;
                while (end < areas.Count && Worst(areas.GetRange(i, end + 1 - i), side) <= Worst(areas.GetRange(i, end - i), side))
                    end++;
                List<double> row = areas.GetRange(i, end - i);
                double sum = row.Sum();
                if (w >= h)                       // column on the left
                {
                    double cw = sum / h, cy = y;
                    foreach (double a in row)
                    {
                        double ch = a / cw;
                        result.Add(new RectangleF((float)x, (float)cy, (float)cw, (float)ch));
                        cy += ch;
                    }
                    x += cw;
                    w = Math.Max(w - cw, 0);
                }
                else                              // row on the top
                {
                    double rh = sum / w, cx = x;
                    foreach (double a in row)
                    {
                        double rw = a / rh;
                        result.Add(new RectangleF((float)cx, (float)y, (float)rw, (float)rh));
                        cx += rw;
                    }
                    y += rh;
                    h = Math.Max(h - rh, 0);
                }
                i = end;
            }
            return result;
        }

        static double Worst(List<double> row, double side)
        {
            double s = row.Sum(), mx = row.Max(), mn = row.Min();
            if (s <= 0 || mn <= 0) return double.MaxValue;
            return Math.Max(side * side * mx / (s * s), s * s / (side * side * mn));
        }
    }

    // Draws the treemap: Items must be positive-size entries, largest first. Click a block to raise BlockClicked.
    class MapPanel : Panel
    {
        public List<DiskEntry> Items = new List<DiskEntry>();
        public event Action<DiskEntry> BlockClicked;
        List<RectangleF> rects = new List<RectangleF>();

        public MapPanel()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            BackColor = Color.White;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            const int legendH = 28, pad = 10;
            float w = ClientSize.Width - 2 * pad, h = ClientSize.Height - legendH - pad;
            List<double> sizes = new List<double>();
            foreach (DiskEntry d in Items) sizes.Add(d.Size);
            rects = Treemap.Layout(sizes, w, h);
            using (Font f = new Font("Segoe UI", 8.5f))
            using (StringFormat sf = new StringFormat())
            {
                sf.Trimming = StringTrimming.EllipsisCharacter;
                sf.FormatFlags = StringFormatFlags.NoWrap;
                for (int i = 0; i < rects.Count && i < Items.Count; i++)
                {
                    RectangleF r = rects[i];
                    r.Offset(pad, pad);
                    rects[i] = r;
                    using (SolidBrush b = new SolidBrush(Treemap.KindColors[Treemap.KindOf(Items[i].Path, Items[i].IsDir)]))
                        g.FillRectangle(b, r.X + 1, r.Y + 1, Math.Max(r.Width - 2, 0), Math.Max(r.Height - 2, 0));
                    if (r.Width > 64 && r.Height > 34)
                    {
                        string name = System.IO.Path.GetFileName(Items[i].Path);
                        g.DrawString(name, f, Brushes.White, new RectangleF(r.X + 5, r.Y + 4, r.Width - 10, 16), sf);
                        g.DrawString(Util.Fmt(Items[i].Size), f, Brushes.WhiteSmoke, new RectangleF(r.X + 5, r.Y + 19, r.Width - 10, 16), sf);
                    }
                }
                float lx = pad;
                for (int k = 0; k < Treemap.KindNames.Length; k++)
                {
                    using (SolidBrush b = new SolidBrush(Treemap.KindColors[k]))
                        g.FillRectangle(b, lx, ClientSize.Height - legendH + 6, 10, 10);
                    g.DrawString(Treemap.KindNames[k], f, Brushes.DimGray, lx + 14, ClientSize.Height - legendH + 3);
                    lx += 14 + g.MeasureString(Treemap.KindNames[k], f).Width + 8;
                }
            }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            for (int i = 0; i < rects.Count && i < Items.Count; i++)
            {
                if (rects[i].Contains(e.Location))
                {
                    if (BlockClicked != null) BlockClicked(Items[i]);
                    return;
                }
            }
        }
    }

    class SizeComparer : System.Collections.IComparer
    {
        public int Compare(object a, object b)
        {
            long sa = ((ListViewItem)a).Tag is long ? (long)((ListViewItem)a).Tag : -1;
            long sb = ((ListViewItem)b).Tag is long ? (long)((ListViewItem)b).Tag : -1;
            return sb.CompareTo(sa);
        }
    }

    // ------------------------------------------------------------------ file preview
    // Shows one file (image, or the start of a text file) so the user can decide whether to delete it.
    class PreviewForm : Form
    {
        static readonly string[] ImageExt = { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".ico", ".tif", ".tiff" };
        static readonly string[] TextExt = { ".txt", ".log", ".md", ".json", ".xml", ".csv", ".html", ".htm",
            ".ini", ".conf", ".cfg", ".yaml", ".yml", ".cs", ".py", ".js", ".sh", ".bat", ".cmd", ".ps1",
            ".srt", ".vcf", ".gpx", ".config" };

        public bool TrashRequested;
        readonly string path;

        public PreviewForm(string path, bool canTrash)
        {
            this.path = path;
            Text = Path.GetFileName(path);
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(520, 470);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;

            Panel buttons = new Panel();
            buttons.Dock = DockStyle.Bottom; buttons.Height = 48;
            Label info = new Label();
            info.Dock = DockStyle.Bottom; info.Height = 64;
            info.Padding = new Padding(12, 6, 12, 0);
            info.ForeColor = Color.DimGray;
            info.Text = InfoText();

            Control view = BuildView();
            view.Dock = DockStyle.Fill;

            Button close = new Button();
            close.Text = "Close"; close.SetBounds(430, 9, 80, 30);
            close.DialogResult = DialogResult.Cancel;
            Button open = new Button();
            open.Text = "Open"; open.SetBounds(344, 9, 80, 30);
            open.Click += delegate { Launch(path); };
            Button reveal = new Button();
            reveal.Text = "Show in folder"; reveal.SetBounds(236, 9, 102, 30);
            reveal.Click += delegate { Launch("explorer.exe", "/select,\"" + path + "\""); };
            buttons.Controls.Add(close); buttons.Controls.Add(open); buttons.Controls.Add(reveal);
            if (canTrash)
            {
                Button trash = new Button();
                trash.Text = "Move to Recycle Bin"; trash.SetBounds(10, 9, 150, 30);
                trash.ForeColor = Color.Firebrick;
                trash.Click += delegate { TrashRequested = true; Close(); };
                buttons.Controls.Add(trash);
            }
            CancelButton = close;

            Controls.Add(view);
            Controls.Add(info);
            Controls.Add(buttons);
            view.BringToFront();
        }

        static void Launch(string file, string args)
        {
            try { Process.Start(file, args); } catch (Exception) { }
        }
        static void Launch(string file)
        {
            try { Process.Start(file); } catch (Exception) { }
        }

        string InfoText()
        {
            try
            {
                FileInfo fi = new FileInfo(path);
                return Util.Fmt(fi.Length) + "  |  " + (fi.Extension.Length > 0 ? fi.Extension : "file")
                    + "\r\n" + fi.LastWriteTime.ToString("g") + "\r\n" + fi.DirectoryName;
            }
            catch (Exception) { return path; }
        }

        Control BuildView()
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            try
            {
                FileInfo fi = new FileInfo(path);
                if (Array.IndexOf(ImageExt, ext) >= 0 && fi.Length < 60L * 1024 * 1024)
                {
                    Image img;
                    using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (Image raw = Image.FromStream(fs))
                        img = new Bitmap(raw);      // copy, so the file is not left locked
                    PictureBox pb = new PictureBox();
                    pb.Image = img; pb.SizeMode = PictureBoxSizeMode.Zoom;
                    pb.BackColor = Color.FromArgb(242, 244, 248);
                    return pb;
                }
                if (Array.IndexOf(TextExt, ext) >= 0)
                {
                    byte[] buf = new byte[4000];
                    int n;
                    using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        n = fs.Read(buf, 0, buf.Length);
                    if (Array.IndexOf(buf, (byte)0, 0, n) < 0)
                    {
                        TextBox tb = new TextBox();
                        tb.Multiline = true; tb.ReadOnly = true; tb.ScrollBars = ScrollBars.Vertical;
                        tb.Font = new Font("Consolas", 9f);
                        tb.BackColor = Color.White;
                        tb.Text = Encoding.UTF8.GetString(buf, 0, n).Replace("\r\n", "\n").Replace("\n", "\r\n");
                        tb.Select(0, 0);
                        return tb;
                    }
                }
            }
            catch (Exception) { /* fall through to the no-preview message */ }
            Label none = new Label();
            none.Text = "No preview for this file type.\r\nUse Open to view it in another app.";
            none.TextAlign = ContentAlignment.MiddleCenter;
            none.ForeColor = Color.DimGray;
            return none;
        }

        // Returns true if the user asked to move the file to the Recycle Bin.
        public static bool Ask(IWin32Window owner, string path, bool canTrash)
        {
            using (PreviewForm f = new PreviewForm(path, canTrash))
            {
                f.ShowDialog(owner);
                return f.TrashRequested;
            }
        }
    }

    // Lists what a Clean item would delete; double-click a file to preview it.
    class ReviewForm : Form
    {
        public ReviewForm(Item item)
        {
            Text = item.Title;
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(560, 440);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;

            Label hint = new Label();
            hint.Dock = DockStyle.Top; hint.Height = 28;
            hint.Padding = new Padding(10, 8, 0, 0);
            hint.ForeColor = Color.DimGray;
            hint.Text = "Double-click a file to preview it.";

            ListView lv = new ListView();
            lv.Dock = DockStyle.Fill;
            lv.View = View.Details; lv.FullRowSelect = true; lv.MultiSelect = false;
            lv.HeaderStyle = ColumnHeaderStyle.None;
            lv.BorderStyle = BorderStyle.None;
            lv.Columns.Add("Name", 400);
            lv.Columns.Add("Size", 120, HorizontalAlignment.Right);
            int shown = Math.Min(item.Paths.Count, 200);
            for (int i = 0; i < shown; i++)
            {
                string p = item.Paths[i];
                bool dir = Util.IsDir(p);
                ListViewItem lvi = new ListViewItem(Path.GetFileName(p));
                lvi.Name = p;
                long len = 0;
                if (!dir) { try { len = new FileInfo(p).Length; } catch (Exception) { } }
                lvi.SubItems.Add(dir ? "folder" : Util.Fmt(len));
                lv.Items.Add(lvi);
            }
            if (item.Paths.Count > shown)
            {
                ListViewItem more = new ListViewItem("...and " + (item.Paths.Count - shown) + " more");
                more.ForeColor = Color.DimGray;
                lv.Items.Add(more);
            }
            lv.DoubleClick += delegate
            {
                if (lv.SelectedItems.Count == 0) return;
                string p = lv.SelectedItems[0].Name;
                if (p.Length > 0 && !Util.IsDir(p) && File.Exists(p)) PreviewForm.Ask(this, p, false);
            };

            Panel bar = new Panel();
            bar.Dock = DockStyle.Bottom; bar.Height = 44;
            Button close = new Button();
            close.Text = "Close"; close.SetBounds(470, 7, 80, 30);
            close.DialogResult = DialogResult.Cancel;
            bar.Controls.Add(close);
            CancelButton = close;

            Controls.Add(lv);
            Controls.Add(hint);
            Controls.Add(bar);
            lv.BringToFront();
        }
    }

    // ------------------------------------------------------------------ installed programs
    class AppEntry
    {
        public string KeyPath, Name, Publisher, Version, InstallLocation, UninstallString;
        public long Size = -1;                          // bytes, -1 = not known yet
        public DateTime LastUsed = DateTime.MinValue;   // MinValue = unknown
    }

    class LeftoverItem
    {
        public string Path, Detail;
        public long Size;
        public bool Selected;
    }

    // Installed programs (the same list as Settings > Apps), their size, when they were last used, the
    // folders they leave behind, and how to remove them.
    static class Apps
    {
        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";

        public static List<AppEntry> Installed()
        {
            List<AppEntry> list = new List<AppEntry>();
            Dictionary<string, bool> seen = new Dictionary<string, bool>();
            Read(Registry.LocalMachine, UninstallKey, list, seen);
            Read(Registry.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", list, seen);
            Read(Registry.CurrentUser, UninstallKey, list, seen);
            return list;
        }

        static void Read(RegistryKey hive, string path, List<AppEntry> list, Dictionary<string, bool> seen)
        {
            try
            {
                using (RegistryKey k = hive.OpenSubKey(path))
                {
                    if (k == null) return;
                    foreach (string sub in k.GetSubKeyNames())
                    {
                        using (RegistryKey a = k.OpenSubKey(sub))
                        {
                            if (a == null) continue;
                            string name = a.GetValue("DisplayName") as string;
                            string us = a.GetValue("UninstallString") as string;
                            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(us)) continue;
                            object sys = a.GetValue("SystemComponent");
                            if (sys is int && (int)sys == 1) continue;
                            if (a.GetValue("ParentKeyName") != null) continue;          // updates and patches
                            string rel = a.GetValue("ReleaseType") as string;
                            if (rel == "Update" || rel == "Hotfix" || rel == "Security Update") continue;
                            string ver = (a.GetValue("DisplayVersion") as string) ?? "";
                            string id = name + "|" + ver;
                            if (seen.ContainsKey(id)) continue;
                            seen[id] = true;

                            AppEntry e = new AppEntry();
                            e.KeyPath = (hive == Registry.CurrentUser ? "HKCU\\" : "HKLM\\") + path + "\\" + sub;
                            e.Name = name.Trim();
                            e.Publisher = (a.GetValue("Publisher") as string) ?? "";
                            e.Version = ver;
                            e.UninstallString = us;
                            e.InstallLocation = LocationOf(a);
                            object est = a.GetValue("EstimatedSize");
                            if (est is int && (int)est > 0) e.Size = (long)(int)est * 1024;
                            list.Add(e);
                        }
                    }
                }
            }
            catch (Exception) { }
        }

        // Where the program lives: InstallLocation, else the folder of its icon.
        static string LocationOf(RegistryKey a)
        {
            string loc = (a.GetValue("InstallLocation") as string ?? "").Trim().Trim('"').TrimEnd('\\');
            if (loc.Length < 4 || !Directory.Exists(loc))
            {
                string icon = (a.GetValue("DisplayIcon") as string ?? "").Trim().Trim('"');
                int comma = icon.LastIndexOf(',');
                if (comma > 2) icon = icon.Substring(0, comma);
                try { loc = icon.Length > 3 ? System.IO.Path.GetDirectoryName(icon) : ""; }
                catch (Exception) { loc = ""; }
            }
            return IsSafeLocation(loc) ? loc : "";
        }

        // Only program folders, never a drive root or the Windows folder.
        static bool IsSafeLocation(string p)
        {
            if (string.IsNullOrEmpty(p) || !Directory.Exists(p)) return false;
            string full = System.IO.Path.GetFullPath(p).TrimEnd('\\');
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\');
            if (full.Length < 4 || full.StartsWith(win, StringComparison.OrdinalIgnoreCase)) return false;
            string[] roots =
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
            };
            foreach (string r in roots)
                if (r.Length > 3 && full.StartsWith(r.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)
                    && full.Length > r.TrimEnd('\\').Length + 1) return true;
            return false;
        }

        // Fills in a size for programs that do not report one.
        public static void Measure(AppEntry e)
        {
            if (e.Size > 0 || e.InstallLocation.Length == 0) return;
            e.Size = Util.Size(e.InstallLocation);
        }

        // Windows keeps no "last used" date for programs. The nearest thing is the UserAssist list
        // (what Explorer launched, per user): take the newest launch of anything inside the install folder.
        public static void FillLastUsed(List<AppEntry> apps)
        {
            Dictionary<string, DateTime> runs = UserAssist();
            foreach (AppEntry a in apps)
            {
                if (a.InstallLocation.Length == 0) continue;
                string prefix = a.InstallLocation.TrimEnd('\\') + "\\";
                DateTime best = DateTime.MinValue;
                foreach (KeyValuePair<string, DateTime> kv in runs)
                    if (kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && kv.Value > best) best = kv.Value;
                a.LastUsed = best;
            }
        }

        static string Rot13(string s)
        {
            char[] c = s.ToCharArray();
            for (int i = 0; i < c.Length; i++)
            {
                char ch = c[i];
                if (ch >= 'a' && ch <= 'z') c[i] = (char)((ch - 'a' + 13) % 26 + 'a');
                else if (ch >= 'A' && ch <= 'Z') c[i] = (char)((ch - 'A' + 13) % 26 + 'A');
            }
            return new string(c);
        }

        static Dictionary<string, DateTime> UserAssist()
        {
            Dictionary<string, DateTime> result = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string> known = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            known["{6D809377-6AF0-444B-8957-A3773F02200E}"] = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            known["{7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E}"] = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            known["{F1B32785-6FBA-4FCF-9D55-7B8E7F157091}"] = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            known["{3EB685DB-65F9-4CF6-A03A-E3EF65729F3D}"] = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            known["{905E63B6-C1BF-494E-B29C-65B732D3D21A}"] = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            try
            {
                using (RegistryKey ua = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\UserAssist"))
                {
                    if (ua == null) return result;
                    foreach (string guid in ua.GetSubKeyNames())
                    {
                        using (RegistryKey count = ua.OpenSubKey(guid + "\\Count"))
                        {
                            if (count == null) continue;
                            foreach (string raw in count.GetValueNames())
                            {
                                byte[] data = count.GetValue(raw) as byte[];
                                if (data == null || data.Length < 68) continue;
                                long ft = BitConverter.ToInt64(data, 60);                // last run, FILETIME
                                if (ft <= 0) continue;
                                string path = Rot13(raw);
                                int close = path.IndexOf('}');
                                if (path.StartsWith("{") && close > 0)
                                {
                                    string repl;
                                    if (known.TryGetValue(path.Substring(0, close + 1), out repl))
                                        path = repl + path.Substring(close + 1);
                                }
                                DateTime when;
                                try { when = DateTime.FromFileTime(ft); } catch (Exception) { continue; }
                                DateTime old;
                                if (!result.TryGetValue(path, out old) || when > old) result[path] = when;
                            }
                        }
                    }
                }
            }
            catch (Exception) { }
            return result;
        }

        // Folders the program leaves in AppData and ProgramData. Strict matches only: the program's
        // exact name (or Publisher\Name), or the name of its install folder.
        public static List<LeftoverItem> Leftovers(AppEntry a)
        {
            List<LeftoverItem> found = new List<LeftoverItem>();
            List<string> names = new List<string>();
            names.Add(a.Name);
            if (a.InstallLocation.Length > 0)
            {
                string leaf = System.IO.Path.GetFileName(a.InstallLocation);
                if (leaf.Length >= 4 && !names.Contains(leaf)) names.Add(leaf);
            }
            string[] roots =
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
            };
            string[] labels = { "AppData\\Roaming (settings)", "AppData\\Local (cache and data)", "ProgramData" };
            for (int i = 0; i < roots.Length; i++)
            {
                List<string> candidates = new List<string>();
                foreach (string n in names)
                {
                    candidates.Add(System.IO.Path.Combine(roots[i], n));
                    if (a.Publisher.Length > 0) candidates.Add(System.IO.Path.Combine(System.IO.Path.Combine(roots[i], a.Publisher), n));
                }
                foreach (string c in candidates)
                {
                    try
                    {
                        if (!Directory.Exists(c) || (File.GetAttributes(c) & FileAttributes.ReparsePoint) != 0) continue;
                        LeftoverItem it = new LeftoverItem();
                        it.Path = c;
                        it.Detail = labels[i];
                        it.Size = Util.Size(c);
                        it.Selected = i == 1;       // Local is mostly cache; Roaming and ProgramData may hold settings
                        found.Add(it);
                    }
                    catch (Exception) { }
                }
            }
            return found;
        }

        // The command Windows would run: an MSI "/I" (install) entry becomes "/X" (uninstall).
        static string Command(AppEntry a)
        {
            string us = a.UninstallString.Trim();
            if (us.IndexOf("msiexec", StringComparison.OrdinalIgnoreCase) >= 0)
                us = Regex.Replace(us, @"/I(?=\{)", "/X", RegexOptions.IgnoreCase);
            return us;
        }

        // Runs the program's own uninstaller (it shows its own window and may ask for administrator)
        // and waits. Returns an error message, or null when it finished.
        public static string RunUninstaller(AppEntry a)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c \"" + Command(a) + "\"");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                using (Process p = Process.Start(psi))
                {
                    p.WaitForExit();
                    if (p.ExitCode != 0 && p.ExitCode != 3010 && p.ExitCode != 1641)
                        return "The uninstaller finished with code " + p.ExitCode + " (cancelled or failed).";
                }
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        // Permanently deletes a leftover folder (not the Recycle Bin), only inside AppData, ProgramData or the install folder.
        public static string RemoveLeftover(AppEntry a, string path)
        {
            string full;
            try { full = System.IO.Path.GetFullPath(path).TrimEnd('\\'); }
            catch (Exception ex) { return ex.Message; }
            List<string> roots = new List<string>();
            roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
            roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
            if (a.InstallLocation.Length > 0) roots.Add(a.InstallLocation);
            bool ok = false;
            foreach (string r in roots)
            {
                string rr = r.TrimEnd('\\');
                if (rr.Length > 3 && (full.StartsWith(rr + "\\", StringComparison.OrdinalIgnoreCase)
                    || (a.InstallLocation.Length > 0 && full.Equals(rr, StringComparison.OrdinalIgnoreCase)
                        && rr.Equals(a.InstallLocation.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)))) ok = true;
            }
            if (!ok) return "Refused: outside the allowed folders";
            try { Directory.Delete(full, true); return null; }
            catch (Exception ex) { return ex.Message; }
        }
    }

    // Review before uninstalling: lists leftover folders with checkboxes. Nothing happens until Delete.
    class UninstallForm : Form
    {
        public List<string> Picked = new List<string>();
        readonly CheckedListBox box = new CheckedListBox();
        readonly List<LeftoverItem> items;

        public UninstallForm(AppEntry app, List<LeftoverItem> left)
        {
            items = left;
            Text = "Uninstall " + app.Name + "?";
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(620, 400);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;

            Label note = new Label();
            note.Dock = DockStyle.Top; note.Height = 74;
            note.Padding = new Padding(10, 8, 10, 0);
            note.ForeColor = Color.Firebrick;
            note.Text = "WARNING: this deletes permanently. Nothing goes to the Recycle Bin and it cannot be undone.\r\n"
                      + "Windows runs the program's own uninstaller (it may ask for administrator), then the ticked folders are deleted. "
                      + "Roaming and ProgramData folders may hold settings, so they start unticked.";

            box.Dock = DockStyle.Fill;
            box.CheckOnClick = true;
            box.BorderStyle = BorderStyle.None;
            foreach (LeftoverItem it in left)
                box.Items.Add(it.Path + "   (" + Util.Fmt(it.Size) + ", " + it.Detail + ")", it.Selected);
            if (left.Count == 0) box.Items.Add("No leftover folders found.", false);

            Panel bar = new Panel();
            bar.Dock = DockStyle.Bottom; bar.Height = 46;
            Button go = new Button();
            go.Text = "Delete"; go.SetBounds(430, 8, 84, 30);
            go.ForeColor = Color.Firebrick;
            go.Click += delegate
            {
                for (int i = 0; i < items.Count && i < box.Items.Count; i++)
                    if (box.GetItemChecked(i)) Picked.Add(items[i].Path);
                DialogResult = DialogResult.OK;
            };
            Button cancel = new Button();
            cancel.Text = "Cancel"; cancel.SetBounds(522, 8, 84, 30);
            cancel.DialogResult = DialogResult.Cancel;
            bar.Controls.Add(go); bar.Controls.Add(cancel);
            CancelButton = cancel;

            Controls.Add(box);
            Controls.Add(note);
            Controls.Add(bar);
            box.BringToFront();
        }
    }

    class MainForm : Form
    {
        readonly bool isAdmin = Util.IsAdmin();
        static readonly Color Brand = Color.FromArgb(37, 99, 235);   // Trust Blue #2563EB
        static readonly Color Green = Color.FromArgb(30, 140, 80);
        TabControl tabs = new TabControl();
        Label freeLabel = new Label();

        // Clean tab
        ListView cleanList = new ListView();
        Label summary = new Label();
        Button scanBtn = new Button(), cleanBtn = new Button(), adminBtn = new Button(), reviewBtn = new Button();
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
        List<DiskEntry> disk = new List<DiskEntry>();
        Dictionary<string, DiskEntry> diskByPath = new Dictionary<string, DiskEntry>();
        ComboBox sortBox = new ComboBox(), topBox = new ComboBox();
        CheckBox mapBtn = new CheckBox();
        ProgressBar diskProgress = new ProgressBar();
        MapPanel mapPanel = new MapPanel();
        int measured, itemCount;

        // Apps tab
        ListView appList = new ListView();
        ComboBox appSortBox = new ComboBox(), appTopBox = new ComboBox();
        Label appStatus = new Label();
        Button appUninstallBtn = new Button(), appRescanBtn = new Button();
        List<AppEntry> appEntries = new List<AppEntry>();
        int appGen, appMeasured, appToMeasure;
        static readonly int[] TopCounts = { 0, 5, 10, 50, 100 };     // 0 = all

        public MainForm()
        {
            SuspendLayout();
            Text = "OpenCleaner";
            Width = 940; Height = 680;
            MinimumSize = new Size(760, 520);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            Panel header = new Panel();
            header.Dock = DockStyle.Top; header.Height = 52; header.BackColor = Brand;
            PictureBox logo = new PictureBox();
            logo.SetBounds(12, 10, 32, 32); logo.SizeMode = PictureBoxSizeMode.Zoom;
            logo.BackColor = Color.Transparent;
            try { logo.Image = Icon.ExtractAssociatedIcon(Application.ExecutablePath).ToBitmap(); } catch { }
            Label title = new Label();
            title.Text = "OpenCleaner" + (isAdmin ? "   (administrator)" : "");
            title.SetBounds(52, 0, 420, 52); title.TextAlign = ContentAlignment.MiddleLeft;
            title.Font = new Font("Segoe UI Semibold", 13f); title.ForeColor = Color.White;
            freeLabel.Dock = DockStyle.Right; freeLabel.Width = 340;
            freeLabel.TextAlign = ContentAlignment.MiddleRight;
            freeLabel.ForeColor = Color.White; freeLabel.Font = new Font("Segoe UI", 10f);
            header.Controls.Add(logo);
            header.Controls.Add(title);
            header.Controls.Add(freeLabel);

            tabs.Dock = DockStyle.Fill;
            tabs.Padding = new Point(22, 6);
            tabs.Font = new Font("Segoe UI", 10f);
            TabPage cleanTab = new TabPage("Clean");
            TabPage diskTab = new TabPage("Disk usage");
            tabs.TabPages.Add(cleanTab);
            tabs.TabPages.Add(diskTab);
            TabPage appsTab = new TabPage("Apps");
            tabs.TabPages.Add(appsTab);
            Controls.Add(header);
            Controls.Add(tabs);
            tabs.BringToFront();     // docking runs back to front: the Fill control goes last

            BuildCleanTab(cleanTab);
            BuildDiskTab(diskTab);
            BuildAppsTab(appsTab);
            UpdateFree();
            Shown += delegate { Scan(); LoadDisk(Environment.GetEnvironmentVariable("USERPROFILE")); LoadApps(); };

            // Layout above is in 96 DPI pixels; scale it to the screen (fonts already scale on their own).
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            ResumeLayout(false);
        }

        // Pixel sizes that auto-scaling does not touch (list columns, row heights) go through S().
        static readonly float dpiScale = ScreenScale();
        static float ScreenScale()
        {
            try { using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) return g.DpiX / 96f; }
            catch { return 1f; }
        }
        static int S(int px) { return (int)Math.Round(px * dpiScale); }

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
            cleanList.Font = new Font("Segoe UI", 10f);
            cleanList.BorderStyle = BorderStyle.None;
            cleanList.SmallImageList = RowHeight(S(30));
            cleanList.Columns.Add("Item", S(280));
            cleanList.Columns.Add("Details", S(400));
            cleanList.Columns.Add("Size", S(130), HorizontalAlignment.Right);
            cleanList.Resize += delegate { FitColumns(cleanList, S(280), S(130)); };
            cleanList.ItemCheck += OnItemCheck;
            cleanList.ItemChecked += delegate { if (!populating) UpdateSummary(); };

            Panel bar = new Panel();
            bar.Dock = DockStyle.Bottom; bar.Height = 56;

            scanBtn.Text = "Rescan"; scanBtn.SetBounds(12, 12, 90, 32);
            StyleButton(scanBtn, false);
            scanBtn.Click += delegate { Scan(); };
            adminBtn.Text = "Restart as administrator"; adminBtn.SetBounds(112, 12, 190, 32);
            StyleButton(adminBtn, false);
            adminBtn.Visible = !isAdmin;
            adminBtn.Click += OnRestartAdmin;
            reviewBtn.Text = "Review files..."; reviewBtn.SetBounds(312, 12, 120, 32);
            StyleButton(reviewBtn, false);
            reviewBtn.Enabled = false;
            reviewBtn.Click += delegate { ReviewCurrent(); };
            cleanList.SelectedIndexChanged += delegate { reviewBtn.Enabled = CurrentItem() != null; };
            cleanList.DoubleClick += delegate { ReviewCurrent(); };
            cleanBtn.Text = "Clean selected"; cleanBtn.Width = 150; cleanBtn.Height = 34;
            StyleButton(cleanBtn, true);
            cleanBtn.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            cleanBtn.Click += OnClean;
            summary.AutoSize = false; summary.Width = 280; summary.Height = 32;
            summary.TextAlign = ContentAlignment.MiddleRight;
            summary.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            bar.Controls.Add(scanBtn); bar.Controls.Add(adminBtn); bar.Controls.Add(reviewBtn);
            bar.Controls.Add(summary); bar.Controls.Add(cleanBtn);
            bar.Resize += delegate
            {
                cleanBtn.Location = new Point(bar.Width - cleanBtn.Width - S(12), S(11));
                summary.Location = new Point(cleanBtn.Left - summary.Width - S(10), S(12));
            };
            page.Controls.Add(cleanList);
            page.Controls.Add(bar);
            cleanList.BringToFront();
        }

        // The highlighted Clean row's item, if it has files that can be reviewed.
        Item CurrentItem()
        {
            if (cleanList.SelectedItems.Count == 0) return null;
            Item it = cleanList.SelectedItems[0].Tag as Item;
            return (it != null && !it.Empty && it.Paths.Count > 0) ? it : null;
        }

        void ReviewCurrent()
        {
            Item it = CurrentItem();
            if (it == null) return;
            using (ReviewForm f = new ReviewForm(it)) f.ShowDialog(this);
        }

        // A 1px-wide image list is the standard trick to make ListView rows taller.
        static ImageList RowHeight(int h)
        {
            ImageList il = new ImageList();
            il.ImageSize = new Size(1, h);
            return il;
        }

        // Keeps the middle "Details" column stretching to fill the list.
        static void FitColumns(ListView lv, int first, int last)
        {
            int w = lv.ClientSize.Width - first - last - 4;
            if (w > S(150)) lv.Columns[1].Width = w;
        }

        static void StyleButton(Button b, bool primary)
        {
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = primary ? 0 : 1;
            b.FlatAppearance.BorderColor = Color.Silver;
            b.BackColor = primary ? Brand : Color.White;
            b.ForeColor = primary ? Color.White : Color.Black;
            b.Font = new Font("Segoe UI", 9.5f);
            b.Cursor = Cursors.Hand;
            b.UseVisualStyleBackColor = false;
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
                string sizeText = it.Empty ? (it.Failed ? "Scan failed" : "Clean")
                                : (it.NeedsAdmin && !isAdmin ? "Needs admin" : Util.Fmt(it.Size));
                ListViewItem lvi = new ListViewItem(it.Title);
                lvi.SubItems.Add(it.Subtitle);
                lvi.SubItems.Add(sizeText);
                lvi.Group = g;
                lvi.Tag = it;
                lvi.Checked = it.Selected && !Disabled(it);
                if (it.Empty) lvi.ForeColor = it.Failed ? Color.Firebrick : Green;
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
            MessageBox.Show(this, msg.ToString(), "OpenCleaner");
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

        // ---------------------------------------------------------- apps tab
        void BuildAppsTab(TabPage page)
        {
            Panel tools = new Panel();
            tools.Dock = DockStyle.Top; tools.Height = 40;
            Label sortLabel = new Label(); sortLabel.Text = "Sort:"; sortLabel.SetBounds(10, 12, 34, 20);
            appSortBox.DropDownStyle = ComboBoxStyle.DropDownList;
            appSortBox.Items.AddRange(new object[] { "Biggest first", "Least recently used", "Recently used", "Name" });
            appSortBox.SelectedIndex = 0; appSortBox.SetBounds(46, 8, 150, 28);
            Label topLabel = new Label(); topLabel.Text = "Show:"; topLabel.SetBounds(206, 12, 40, 20);
            appTopBox.DropDownStyle = ComboBoxStyle.DropDownList;
            appTopBox.Items.AddRange(new object[] { "All", "Top 5", "Top 10", "Top 50", "Top 100" });
            appTopBox.SelectedIndex = 0; appTopBox.SetBounds(248, 8, 80, 28);
            appSortBox.SelectedIndexChanged += delegate { RenderApps(); };
            appTopBox.SelectedIndexChanged += delegate { RenderApps(); };
            appRescanBtn.Text = "Rescan"; appRescanBtn.SetBounds(340, 7, 80, 28);
            appRescanBtn.Click += delegate { LoadApps(); };
            appUninstallBtn.Text = "Uninstall..."; appUninstallBtn.SetBounds(430, 7, 100, 28);
            appUninstallBtn.Enabled = false;
            appUninstallBtn.Click += delegate { UninstallSelected(); };
            tools.Controls.Add(sortLabel); tools.Controls.Add(appSortBox);
            tools.Controls.Add(topLabel); tools.Controls.Add(appTopBox);
            tools.Controls.Add(appRescanBtn); tools.Controls.Add(appUninstallBtn);

            appList.Dock = DockStyle.Fill;
            appList.View = View.Details;
            appList.FullRowSelect = true;
            appList.MultiSelect = false;
            appList.Font = new Font("Segoe UI", 10f);
            appList.BorderStyle = BorderStyle.None;
            appList.SmallImageList = RowHeight(S(28));
            appList.Columns.Add("Program", S(380));
            appList.Columns.Add("Last used", S(150));
            appList.Columns.Add("Size", S(110), HorizontalAlignment.Right);
            appList.Resize += delegate
            {
                int w = appList.ClientSize.Width - S(150) - S(110) - 4;
                if (w > S(150)) appList.Columns[0].Width = w;
            };
            appList.SelectedIndexChanged += delegate { appUninstallBtn.Enabled = appList.SelectedItems.Count > 0; };
            appList.DoubleClick += delegate { UninstallSelected(); };

            Panel bottom = new Panel();
            bottom.Dock = DockStyle.Bottom; bottom.Height = 34;
            appStatus.SetBounds(10, 6, 800, 24);
            appStatus.ForeColor = Color.DimGray;
            bottom.Controls.Add(appStatus);

            page.Controls.Add(appList);
            page.Controls.Add(tools);
            page.Controls.Add(bottom);
            appList.BringToFront();
        }

        async void LoadApps()
        {
            int myGen = ++appGen;
            appStatus.Text = "Reading installed programs...";
            List<AppEntry> found = await Task.Run(() =>
            {
                List<AppEntry> l = Apps.Installed();
                Apps.FillLastUsed(l);
                return l;
            });
            if (myGen != appGen) return;
            appEntries = found;
            RenderApps();

            // sizes that Windows does not report are measured from the install folder
            List<AppEntry> pending = found.Where(a => a.Size <= 0 && a.InstallLocation.Length > 0).ToList();
            appToMeasure = pending.Count; appMeasured = 0;
            if (pending.Count == 0) return;
            await Task.Run(() =>
            {
                Parallel.ForEach(pending, new ParallelOptions { MaxDegreeOfParallelism = 4 }, a =>
                {
                    if (appGen != myGen) return;
                    Apps.Measure(a);
                    try
                    {
                        BeginInvoke(new Action(() =>
                        {
                            if (appGen != myGen) return;
                            appMeasured++;
                            if (appMeasured == appToMeasure || appMeasured % 5 == 0) RenderApps();
                        }));
                    }
                    catch (Exception) { }
                });
            });
        }

        static string Ago(DateTime d)
        {
            if (d == DateTime.MinValue) return "unknown";
            double days = (DateTime.Now - d).TotalDays;
            if (days < 1) return "today";
            if (days < 2) return "yesterday";
            if (days < 60) return (int)days + " days ago";
            if (days < 730) return (int)(days / 30) + " months ago";
            return (int)(days / 365) + " years ago";
        }

        // Programs in the chosen order, cut to the top N.
        List<AppEntry> OrderedApps()
        {
            List<AppEntry> list = new List<AppEntry>(appEntries);
            switch (appSortBox.SelectedIndex)
            {
                case 1: list.Sort((a, b) => a.LastUsed.CompareTo(b.LastUsed)); break;
                case 2: list.Sort((a, b) => b.LastUsed.CompareTo(a.LastUsed)); break;
                case 3: list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase)); break;
                default: list.Sort((a, b) => b.Size.CompareTo(a.Size)); break;
            }
            int n = TopCounts[appTopBox.SelectedIndex < 0 ? 0 : appTopBox.SelectedIndex];
            if (n > 0 && list.Count > n) list = list.GetRange(0, n);
            return list;
        }

        void RenderApps()
        {
            string keep = appList.SelectedItems.Count > 0 ? appList.SelectedItems[0].Name : null;
            appList.BeginUpdate();
            appList.Items.Clear();
            foreach (AppEntry a in OrderedApps())
            {
                ListViewItem lvi = new ListViewItem(a.Name + (a.Version.Length > 0 ? "  " + a.Version : ""));
                lvi.Name = a.KeyPath;
                lvi.SubItems.Add(Ago(a.LastUsed));
                lvi.SubItems.Add(a.Size > 0 ? Util.Fmt(a.Size) : "...");
                appList.Items.Add(lvi);
                if (a.KeyPath == keep) lvi.Selected = true;
            }
            appList.EndUpdate();
            long total = 0;
            foreach (AppEntry a in appEntries) if (a.Size > 0) total += a.Size;
            string pct = appToMeasure > 0 && appMeasured < appToMeasure
                ? "Measuring " + (appMeasured * 100 / appToMeasure) + "% (" + appMeasured + " of " + appToMeasure + ")  |  " : "";
            appStatus.Text = pct + appEntries.Count + " programs  |  " + Util.Fmt(total) + "  |  last used is approximate";
        }

        // Review first: the program's own uninstaller runs, then the ticked leftover folders are deleted permanently.
        async void UninstallSelected()
        {
            if (appList.SelectedItems.Count == 0 || busy) return;
            string key = appList.SelectedItems[0].Name;
            AppEntry app = appEntries.FirstOrDefault(a => a.KeyPath == key);
            if (app == null) return;
            List<LeftoverItem> left = await Task.Run(() => Apps.Leftovers(app));
            List<string> picked;
            using (UninstallForm f = new UninstallForm(app, left))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                picked = f.Picked;
            }
            SetBusy(true, "Uninstalling " + app.Name + "...");
            string err = await Task.Run(() => Apps.RunUninstaller(app));
            if (err != null)
            {
                SetBusy(false, "");
                MessageBox.Show(this, err, "Uninstall");
                return;
            }
            if (app.InstallLocation.Length > 0 && Directory.Exists(app.InstallLocation)) picked.Add(app.InstallLocation);
            List<string> failed = new List<string>();
            int moved = 0;
            foreach (string p in picked)
            {
                string path = p;
                string e = await Task.Run(() => Apps.RemoveLeftover(app, path));
                if (e == null) moved++; else failed.Add(p + ": " + e);
            }
            SetBusy(false, "");
            string msg = app.Name + " was uninstalled." + (moved > 0 ? " " + moved + " folder(s) deleted." : "");
            if (failed.Count > 0) msg += "\n\nCould not remove:\n" + string.Join("\n", failed.ToArray());
            MessageBox.Show(this, msg, "Uninstall finished");
            UpdateFree();
            LoadApps();
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
            diskList.Font = new Font("Segoe UI", 10f);
            diskList.BorderStyle = BorderStyle.None;
            diskList.SmallImageList = RowHeight(S(28));
            diskList.Columns.Add("Name", S(460));
            diskList.Columns.Add("Type", S(90));
            diskList.Columns.Add("Size", S(130), HorizontalAlignment.Right);
            diskList.Resize += delegate
            {
                int w = diskList.ClientSize.Width - S(90) - S(130) - 4;
                if (w > S(150)) diskList.Columns[0].Width = w;
            };
            diskList.DoubleClick += delegate
            {
                if (diskList.SelectedItems.Count == 0) return;
                OpenDiskItem(diskList.SelectedItems[0].Name);
            };

            // sort, top N and the disk map toggle
            Panel tools = new Panel();
            tools.Dock = DockStyle.Top; tools.Height = 38;
            Label sortLabel = new Label(); sortLabel.Text = "Sort:"; sortLabel.SetBounds(10, 11, 34, 20);
            sortBox.DropDownStyle = ComboBoxStyle.DropDownList;
            sortBox.Items.AddRange(new object[] { "Biggest first", "Oldest first", "Newest first", "File type" });
            sortBox.SelectedIndex = 0; sortBox.SetBounds(46, 8, 120, 28);
            Label topLabel = new Label(); topLabel.Text = "Show:"; topLabel.SetBounds(180, 11, 40, 20);
            topBox.DropDownStyle = ComboBoxStyle.DropDownList;
            topBox.Items.AddRange(new object[] { "All", "Top 5", "Top 10", "Top 50", "Top 100" });
            topBox.SelectedIndex = 0; topBox.SetBounds(222, 8, 80, 28);
            sortBox.SelectedIndexChanged += delegate { RefreshDisk(); };
            topBox.SelectedIndexChanged += delegate { RefreshDisk(); };
            mapBtn.Appearance = Appearance.Button; mapBtn.Text = "Disk map";
            mapBtn.TextAlign = ContentAlignment.MiddleCenter;
            mapBtn.SetBounds(316, 6, 90, 28);
            mapBtn.CheckedChanged += delegate
            {
                diskList.Visible = !mapBtn.Checked;
                mapPanel.Visible = mapBtn.Checked;
                RefreshDisk();
            };
            diskProgress.SetBounds(420, 11, 300, 16);
            diskProgress.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            diskProgress.Visible = false;
            tools.Controls.Add(sortLabel); tools.Controls.Add(sortBox);
            tools.Controls.Add(topLabel); tools.Controls.Add(topBox);
            tools.Controls.Add(mapBtn); tools.Controls.Add(diskProgress);

            mapPanel.Dock = DockStyle.Fill;
            mapPanel.Visible = false;
            mapPanel.BlockClicked += delegate(DiskEntry d) { OpenDiskItem(d.Path); };

            Panel bottom = new Panel();
            bottom.Dock = DockStyle.Bottom; bottom.Height = 44;
            trashBtn.Text = "Move selected to Recycle Bin"; trashBtn.SetBounds(10, 7, 210, 30);
            trashBtn.Click += OnTrash;
            diskStatus.SetBounds(230, 7, 600, 30);
            diskStatus.TextAlign = ContentAlignment.MiddleLeft;
            diskStatus.ForeColor = Color.DimGray;
            bottom.Controls.Add(trashBtn); bottom.Controls.Add(diskStatus);

            page.Controls.Add(diskList);
            page.Controls.Add(mapPanel);
            page.Controls.Add(tools);
            page.Controls.Add(top);
            page.Controls.Add(bottom);
            diskList.BringToFront();
            mapPanel.BringToFront();

            root = Util.Env("USERPROFILE");
        }

        void LoadDisk(string path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;
            current = path;
            pathLabel.Text = path;
            upBtn.Enabled = Path.GetDirectoryName(path.TrimEnd('\\')) != null;
            int myGen = ++gen;
            List<string> entries = Util.Children(path);
            disk.Clear();
            diskByPath.Clear();
            foreach (string e in entries)
            {
                DiskEntry d = new DiskEntry();
                d.Path = e;
                d.IsDir = Util.IsDir(e);
                try { d.Modified = d.IsDir ? Directory.GetLastWriteTime(e) : File.GetLastWriteTime(e); }
                catch (Exception) { d.Modified = DateTime.MinValue; }
                disk.Add(d);
                diskByPath[e] = d;
            }
            totalSize = 0; pending = entries.Count;
            measured = 0; itemCount = entries.Count;
            diskProgress.Maximum = Math.Max(itemCount, 1);
            diskProgress.Value = 0;
            diskProgress.Visible = itemCount > 0;
            diskStatus.Text = entries.Count == 0 ? "Empty folder" : "Measuring 0% (0 of " + itemCount + " items)";
            RefreshDisk();

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
            DiskEntry d;
            if (myGen != gen || !diskByPath.TryGetValue(path, out d)) return;
            d.Size = size;
            totalSize += size;
            pending--;
            measured++;
            if (pending == 0 || (DateTime.Now - lastSort).TotalMilliseconds > 400)
            {
                RefreshDisk();
                lastSort = DateTime.Now;
            }
            int pct = itemCount == 0 ? 100 : measured * 100 / itemCount;
            diskStatus.Text = (pending > 0 ? "Measuring " + pct + "% (" + measured + " of " + itemCount + " items)  |  " : "")
                              + "Total " + Util.Fmt(totalSize);
            diskProgress.Value = Math.Min(measured, diskProgress.Maximum);
            diskProgress.Visible = pending > 0;
        }

        // Entries in the chosen order, cut to the top N (list and map both use this).
        List<DiskEntry> Ordered()
        {
            List<DiskEntry> list = new List<DiskEntry>(disk);
            switch (sortBox.SelectedIndex)
            {
                case 1: list.Sort((a, b) => a.Modified.CompareTo(b.Modified)); break;
                case 2: list.Sort((a, b) => b.Modified.CompareTo(a.Modified)); break;
                case 3:
                    list.Sort((a, b) =>
                    {
                        int ka = Treemap.KindOf(a.Path, a.IsDir), kb = Treemap.KindOf(b.Path, b.IsDir);
                        return ka != kb ? ka.CompareTo(kb) : b.Size.CompareTo(a.Size);
                    });
                    break;
                default: list.Sort((a, b) => b.Size.CompareTo(a.Size)); break;
            }
            int n = TopCounts[topBox.SelectedIndex < 0 ? 0 : topBox.SelectedIndex];
            if (n > 0 && list.Count > n) list = list.GetRange(0, n);
            return list;
        }

        void RefreshDisk()
        {
            List<DiskEntry> ordered = Ordered();
            string keep = diskList.SelectedItems.Count > 0 ? diskList.SelectedItems[0].Name : null;
            diskList.BeginUpdate();
            diskList.Items.Clear();
            foreach (DiskEntry d in ordered)
            {
                string name = Path.GetFileName(d.Path);
                ListViewItem lvi = new ListViewItem(string.IsNullOrEmpty(name) ? d.Path : name);
                lvi.Name = d.Path;
                lvi.Tag = d.Size;
                lvi.SubItems.Add(d.IsDir ? "Folder" : "File");
                lvi.SubItems.Add(d.Size < 0 ? "..." : Util.Fmt(d.Size));
                diskList.Items.Add(lvi);
                if (d.Path == keep) lvi.Selected = true;
            }
            diskList.EndUpdate();

            // the map draws the same selection, largest first, at most 150 blocks
            List<DiskEntry> shown = new List<DiskEntry>();
            foreach (DiskEntry d in ordered) if (d.Size > 0) shown.Add(d);
            shown.Sort((a, b) => b.Size.CompareTo(a.Size));
            if (shown.Count > 150) shown = shown.GetRange(0, 150);
            mapPanel.Items = shown;
            mapPanel.Invalidate();
        }

        // Folder: open it. File: preview it, with a Recycle Bin option.
        void OpenDiskItem(string p)
        {
            if (Util.IsDir(p)) { LoadDisk(p); return; }
            if (File.Exists(p) && PreviewForm.Ask(this, p, true)
                && MessageBox.Show(this, p, "Move to Recycle Bin?", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) == DialogResult.Yes) TrashPath(p);
        }

        void OnTrash(object sender, EventArgs e)
        {
            if (diskList.SelectedItems.Count == 0) return;
            string p = diskList.SelectedItems[0].Name;
            if (MessageBox.Show(this, p, "Move to Recycle Bin?", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) != DialogResult.Yes) return;
            TrashPath(p);
        }

        void TrashPath(string p)
        {
            try
            {
                Util.SendToRecycleBin(p);
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
