using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;

namespace OpenCleaner;

// ------------------------------------------------------------------ updates (GitHub releases)
class ReleaseInfo
{
    public string Version, Notes, Page;
    public Dictionary<string, string> Assets = new Dictionary<string, string>();   // file name -> download URL
}

// Checks the GitHub releases of Haguilar91/OpenCleaner and downloads a file from the latest one, verified
// against the release's SHA256SUMS.txt. Written in C# 5 so the WinForms and WinUI apps can share it.
static class Updater
{
    const string Api = "https://api.github.com/repos/Haguilar91/OpenCleaner/releases/latest";

    public static string CurrentVersion
    {
        get
        {
            Version v = Assembly.GetExecutingAssembly().GetName().Version;
            return v.Major + "." + v.Minor + (v.Build > 0 ? "." + v.Build : "");
        }
    }

    static WebClient Client()
    {
        ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;   // TLS 1.2 on older .NET Framework
        WebClient wc = new WebClient();
        wc.Headers[HttpRequestHeader.UserAgent] = "OpenCleaner-Windows";
        wc.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
        return wc;
    }

    static List<int> Parts(string v)
    {
        List<int> parts = new List<int>();
        foreach (string p in v.Trim().TrimStart('v', 'V').Split('.'))
        {
            Match m = Regex.Match(p, @"^\d+");
            parts.Add(m.Success ? int.Parse(m.Value) : 0);
        }
        return parts;
    }

    public static bool IsNewer(string a, string b)
    {
        List<int> x = Parts(a), y = Parts(b);
        for (int i = 0; i < Math.Max(x.Count, y.Count); i++)
        {
            int p = i < x.Count ? x[i] : 0, q = i < y.Count ? y[i] : 0;
            if (p != q) return p > q;
        }
        return false;
    }

    static string Unescape(string s)
    {
        return Regex.Replace(s, @"\\(u[0-9a-fA-F]{4}|.)", m =>
        {
            string e = m.Groups[1].Value;
            if (e.Length == 5) return ((char)Convert.ToInt32(e.Substring(1), 16)).ToString();
            switch (e) { case "n": return "\n"; case "r": return ""; case "t": return "\t"; default: return e; }
        });
    }

    // The latest release, or throws.
    public static ReleaseInfo Latest()
    {
        string json;
        using (WebClient wc = Client()) json = wc.DownloadString(Api);
        Match tag = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"");
        if (!tag.Success) throw new InvalidOperationException("No release found on GitHub.");
        ReleaseInfo r = new ReleaseInfo();
        r.Version = tag.Groups[1].Value.TrimStart('v', 'V');
        r.Page = "https://github.com/Haguilar91/OpenCleaner/releases/tag/" + tag.Groups[1].Value;
        Match body = Regex.Match(json, "\"body\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
        r.Notes = body.Success ? Unescape(body.Groups[1].Value) : "";
        foreach (Match m in Regex.Matches(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]+)\""))
        {
            string url = m.Groups[1].Value;
            r.Assets[Uri.UnescapeDataString(url.Substring(url.LastIndexOf('/') + 1))] = url;
        }
        return r;
    }

    // Downloads the asset whose name ends with `suffix` into %TEMP% and checks its SHA-256 against
    // SHA256SUMS.txt. Returns the file path, or throws with a message for the user.
    public static string Download(ReleaseInfo r, string suffix)
    {
        string name = null;
        foreach (string n in r.Assets.Keys) if (n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) name = n;
        if (name == null) throw new InvalidOperationException("This release has no " + suffix.Trim('-') + " download.");
        if (!r.Assets.ContainsKey("SHA256SUMS.txt"))
            throw new InvalidOperationException("This release has no checksums, so it was not installed.");
        string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "OpenCleaner-update");
        Directory.CreateDirectory(dir);
        string file = System.IO.Path.Combine(dir, name);
        string sums;
        using (WebClient wc = Client())
        {
            sums = wc.DownloadString(r.Assets["SHA256SUMS.txt"]);
            wc.Headers.Remove(HttpRequestHeader.Accept);
            wc.DownloadFile(r.Assets[name], file);
        }
        string hex;
        using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
        using (FileStream fs = File.OpenRead(file))
            hex = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
        foreach (string line in sums.Split('\n'))
            if (line.Trim().StartsWith(hex) && line.Trim().EndsWith(name)) return file;
        File.Delete(file);
        throw new InvalidOperationException("The download did not match the release checksum, so it was not installed.");
    }
}

