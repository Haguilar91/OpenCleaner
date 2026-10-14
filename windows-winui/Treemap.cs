using Windows.UI;

namespace OpenCleaner;

/// <summary>Squarified treemap layout plus the file-type colors used by the disk map.</summary>
static class Treemap
{
    /// <summary>Legend order; also the order used by the "File type" sort.</summary>
    public static readonly (string Label, Color Color)[] Kinds =
    {
        (L.T("Folders"), Color.FromArgb(255, 37, 99, 235)),
        (L.T("Images"), Color.FromArgb(255, 46, 158, 91)),
        (L.T("Video"), Color.FromArgb(255, 140, 84, 204)),
        (L.T("Audio"), Color.FromArgb(255, 224, 133, 36)),
        (L.T("Documents"), Color.FromArgb(255, 26, 153, 158)),
        (L.T("Archives"), Color.FromArgb(255, 199, 69, 69)),
        (L.T("Other"), Color.FromArgb(255, 107, 117, 138)),
    };

    private static readonly string[][] KindExt =
    {
        null,
        new[] { ".jpg", ".jpeg", ".png", ".gif", ".heic", ".webp", ".bmp", ".tif", ".tiff", ".svg", ".raw", ".dng" },
        new[] { ".mp4", ".mov", ".m4v", ".mkv", ".avi", ".webm", ".wmv" },
        new[] { ".mp3", ".flac", ".m4a", ".aac", ".wav", ".ogg", ".opus", ".wma" },
        new[] { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".md", ".csv", ".rtf", ".epub" },
        new[] { ".zip", ".iso", ".7z", ".rar", ".tar", ".gz", ".cab", ".msi" },
    };

    /// <summary>Index into <see cref="Kinds"/>.</summary>
    public static int KindOf(string path, bool isDir)
    {
        if (isDir) return 0;
        string ext = Path.GetExtension(path).ToLowerInvariant();
        for (int k = 1; k < KindExt.Length; k++)
            if (Array.IndexOf(KindExt[k], ext) >= 0) return k;
        return 6;
    }

    /// <summary>sizes must be positive and sorted largest first. Returns one rectangle per size, in order.</summary>
    public static List<(double X, double Y, double W, double H)> Layout(IReadOnlyList<double> sizes, double width, double height)
    {
        var result = new List<(double, double, double, double)>();
        double total = sizes.Sum();
        if (total <= 0 || width <= 0 || height <= 0) return result;
        double scale = width * height / total;
        var areas = sizes.Select(s => s * scale).ToList();
        double x = 0, y = 0, w = width, h = height;
        int i = 0;
        while (i < areas.Count)
        {
            double side = Math.Min(w, h);
            int end = i + 1;
            while (end < areas.Count && Worst(areas.GetRange(i, end + 1 - i), side) <= Worst(areas.GetRange(i, end - i), side))
                end++;
            var row = areas.GetRange(i, end - i);
            double sum = row.Sum();
            if (w >= h)                       // column on the left
            {
                double cw = sum / h, cy = y;
                foreach (double a in row)
                {
                    double ch = a / cw;
                    result.Add((x, cy, cw, ch));
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
                    result.Add((cx, y, rw, rh));
                    cx += rw;
                }
                y += rh;
                h = Math.Max(h - rh, 0);
            }
            i = end;
        }
        return result;
    }

    private static double Worst(List<double> row, double side)
    {
        double s = row.Sum(), mx = row.Max(), mn = row.Min();
        if (s <= 0 || mn <= 0) return double.MaxValue;
        return Math.Max(side * side * mx / (s * s), s * s / (side * side * mn));
    }
}
