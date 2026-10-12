namespace OpenCleaner;

/// <summary>Tiny key=value settings file in %AppData%\OpenCleaner (the app is unpackaged, so no ApplicationData).</summary>
static class Settings
{
    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenCleaner", "settings.txt");

    private static Dictionary<string, string> Read()
    {
        var d = new Dictionary<string, string>();
        try
        {
            foreach (string line in File.ReadAllLines(FilePath))
            {
                int i = line.IndexOf('=');
                if (i > 0) d[line[..i]] = line[(i + 1)..];
            }
        }
        catch (Exception)
        {
            // no settings yet
        }
        return d;
    }

    public static string Get(string key, string fallback) => Read().TryGetValue(key, out string v) ? v : fallback;

    public static void Set(string key, string value)
    {
        var d = Read();
        d[key] = value;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllLines(FilePath, d.Select(kv => kv.Key + "=" + kv.Value));
        }
        catch (Exception)
        {
            // settings are a convenience; never fail because of them
        }
    }
}
