using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace OpenCleaner;

// ------------------------------------------------------------------ installed programs
class AppEntry
{
    public string KeyPath, Name, Publisher, Version, InstallLocation, UninstallString;
    public long Size = -1;                          // bytes, -1 = not known yet
    public bool Machine;                            // installed for all users (HKLM): its uninstaller needs admin
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
                        e.Machine = hive == Registry.LocalMachine;
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
        string[] labels = { L.T("AppData\\Roaming (settings)"), L.T("AppData\\Local (cache and data)"), "ProgramData" };
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

    // Runs the program's own uninstaller (it shows its own window) and waits. Programs installed for
    // all users, such as Microsoft Office, need administrator rights, so Windows shows the UAC prompt.
    // Returns an error message, or null when it finished.
    public static string RunUninstaller(AppEntry a)
    {
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c \"" + Command(a) + "\"");
            psi.UseShellExecute = true;                 // required for the "runas" (UAC) verb
            psi.WindowStyle = ProcessWindowStyle.Hidden;
            if (a.Machine) psi.Verb = "runas";
            using (Process p = Process.Start(psi))
            {
                p.WaitForExit();
                if (p.ExitCode != 0 && p.ExitCode != 3010 && p.ExitCode != 1641)
                    return L.T("The uninstaller finished with code {0} (cancelled or failed).", p.ExitCode);
            }
            return null;
        }
        catch (Win32Exception ex)
        {
            if (ex.NativeErrorCode == 1223) return L.T("You cancelled the administrator prompt, so nothing was uninstalled.");
            return ex.Message;
        }
        catch (Exception ex) { return ex.Message; }
    }

    // True while the program's uninstall entry is still in the registry. Some uninstallers (Office's
    // Click-to-Run) hand off to another process and return at once; leftovers must wait until it is gone.
    public static bool StillInstalled(AppEntry a)
    {
        try
        {
            RegistryKey hive = a.KeyPath.StartsWith("HKCU\\") ? Registry.CurrentUser : Registry.LocalMachine;
            using (RegistryKey k = hive.OpenSubKey(a.KeyPath.Substring(5)))
                return k != null;
        }
        catch (Exception) { return false; }
    }

    // Deletes leftover folders that Windows protects (ProgramData, Program Files) in one elevated step,
    // after the same allowed-folder check as RemoveLeftover. Returns an error message, or null.
    public static string RemoveLeftoversAsAdmin(AppEntry a, List<string> paths)
    {
        List<string> quoted = new List<string>();
        foreach (string p in paths)
        {
            string why = RemoveLeftoverCheck(a, p);
            if (why != null) return why;
            quoted.Add("'" + p.Replace("'", "''") + "'");
        }
        if (quoted.Count == 0) return null;
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo("powershell.exe",
                "-NoProfile -NonInteractive -WindowStyle Hidden -Command \"Remove-Item -LiteralPath " +
                string.Join(",", quoted.ToArray()) + " -Recurse -Force\"");
            psi.UseShellExecute = true;
            psi.Verb = "runas";
            psi.WindowStyle = ProcessWindowStyle.Hidden;
            using (Process p = Process.Start(psi)) p.WaitForExit();
            foreach (string p in paths)
                if (Directory.Exists(p)) return L.T("Could not delete {0}", p);
            return null;
        }
        catch (Win32Exception ex)
        {
            if (ex.NativeErrorCode == 1223) return L.T("You cancelled the administrator prompt; those folders were left.");
            return ex.Message;
        }
        catch (Exception ex) { return ex.Message; }
    }

    // Permanently deletes a leftover folder (not the Recycle Bin), only inside AppData, ProgramData or the
    // install folder. Returns null when done, NeedsAdmin when Windows refused access, or an error message.
    public const string NeedsAdmin = "needs administrator";

    public static string RemoveLeftover(AppEntry a, string path)
    {
        string why = RemoveLeftoverCheck(a, path);
        if (why != null) return why;
        try { Directory.Delete(System.IO.Path.GetFullPath(path), true); return null; }
        catch (UnauthorizedAccessException) { return NeedsAdmin; }
        catch (Exception ex) { return ex.Message; }
    }

    // Null when the path is inside AppData, ProgramData or the program's install folder, else why not.
    static string RemoveLeftoverCheck(AppEntry a, string path)
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
        if (!ok) return L.T("Refused: outside the allowed folders");
        return null;
    }
}

