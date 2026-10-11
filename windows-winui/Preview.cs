using System.Diagnostics;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;

namespace OpenCleaner;

/// <summary>Previews files so the user can decide whether to delete them (image, start of a text file, details).</summary>
static class Preview
{
    private static readonly HashSet<string> ImageExt = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".ico", ".tif", ".tiff", ".webp" };

    private static readonly HashSet<string> TextExt = new(StringComparer.OrdinalIgnoreCase)
        { ".txt", ".log", ".md", ".json", ".xml", ".csv", ".html", ".htm", ".ini", ".conf", ".cfg", ".yaml",
          ".yml", ".cs", ".py", ".js", ".sh", ".bat", ".cmd", ".ps1", ".srt", ".vcf", ".gpx", ".config" };

    private static void Launch(string file, string args = null)
    {
        try
        {
            var psi = new ProcessStartInfo(file) { UseShellExecute = true };
            if (args != null) psi.Arguments = args;
            Process.Start(psi);
        }
        catch (Exception)
        {
            // no app can open this file
        }
    }

    private static async Task<UIElement> BuildView(string path)
    {
        string ext = Path.GetExtension(path);
        try
        {
            var fi = new FileInfo(path);
            if (ImageExt.Contains(ext) && fi.Length < 60L * 1024 * 1024)
            {
                var file = await StorageFile.GetFileFromPathAsync(path);
                using var stream = await file.OpenReadAsync();
                var bmp = new BitmapImage { DecodePixelWidth = 800 };
                await bmp.SetSourceAsync(stream);
                return new Image { Source = bmp, MaxHeight = 320, Stretch = Stretch.Uniform };
            }
            if (TextExt.Contains(ext))
            {
                var buf = new byte[4000];
                int n;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    n = fs.Read(buf, 0, buf.Length);
                if (Array.IndexOf(buf, (byte)0, 0, n) < 0)
                {
                    return new ScrollViewer
                    {
                        MaxHeight = 240,
                        Content = new TextBlock
                        {
                            Text = Encoding.UTF8.GetString(buf, 0, n),
                            FontFamily = new FontFamily("Consolas"),
                            FontSize = 12,
                            IsTextSelectionEnabled = true,
                        },
                    };
                }
            }
        }
        catch (Exception)
        {
            // fall through to the no-preview message
        }
        return new TextBlock
        {
            Text = "No preview for this file type. Use Open to view it in another app.",
            Foreground = Ui.Res("TextFillColorSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap,
        };
    }

    private static string Info(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            return $"{Util.Fmt(fi.Length)}  |  {(fi.Extension.Length > 0 ? fi.Extension : "file")}\n"
                 + $"{fi.LastWriteTime:g}\n{fi.DirectoryName}";
        }
        catch (Exception)
        {
            return path;
        }
    }

    /// <summary>Shows one file. Returns true if the user chose Move to Recycle Bin (only offered when canTrash).</summary>
    public static async Task<bool> Show(XamlRoot root, string path, bool canTrash)
    {
        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(await BuildView(path));
        body.Children.Add(new TextBlock
        {
            Text = Info(path),
            Style = Ui.Sty("CaptionTextBlockStyle"),
            Foreground = Ui.Res("TextFillColorSecondaryBrush"),
            IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.Wrap,
        });

        var dlg = new ContentDialog
        {
            Title = Path.GetFileName(path),
            Content = body,
            SecondaryButtonText = "Open",
            CloseButtonText = "Close",
            XamlRoot = root,
        };
        if (canTrash)
        {
            dlg.PrimaryButtonText = "Move to Recycle Bin";
        }
        // Open and Show in folder keep the dialog up so the user can still decide
        dlg.SecondaryButtonClick += (_, a) => { a.Cancel = true; Launch(path); };
        var reveal = new HyperlinkButton { Content = "Show in folder", Padding = new Thickness(0) };
        reveal.Click += (_, _) => Launch("explorer.exe", $"/select,\"{path}\"");
        body.Children.Add(reveal);
        return await dlg.ShowAsync() == ContentDialogResult.Primary;
    }

    /// <summary>Lists what a Clean item would delete; click a file to preview it.</summary>
    public static async Task Review(XamlRoot root, Item item)
    {
        while (true)
        {
            string chosen = null;
            var dlg = new ContentDialog { Title = item.Title, CloseButtonText = "Close", XamlRoot = root };
            var list = new ListView { MaxHeight = 360, SelectionMode = ListViewSelectionMode.None, IsItemClickEnabled = true };
            int shown = Math.Min(item.Paths.Count, 200);
            for (int i = 0; i < shown; i++)
            {
                string p = item.Paths[i];
                bool dir = Util.IsDir(p);
                long len = 0;
                if (!dir) { try { len = new FileInfo(p).Length; } catch (Exception) { } }
                var row = new Grid { ColumnSpacing = 12, Tag = dir ? null : p };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var name = new TextBlock { Text = Path.GetFileName(p), TextTrimming = TextTrimming.CharacterEllipsis };
                var size = new TextBlock
                {
                    Text = dir ? "folder" : Util.Fmt(len),
                    Foreground = Ui.Res("TextFillColorSecondaryBrush"),
                };
                Grid.SetColumn(size, 1);
                row.Children.Add(name);
                row.Children.Add(size);
                list.Items.Add(row);
            }
            if (item.Paths.Count > shown)
                list.Items.Add(new TextBlock { Text = $"...and {item.Paths.Count - shown} more" });
            list.ItemClick += (_, e) =>
            {
                if (e.ClickedItem is Grid { Tag: string p })
                {
                    chosen = p;
                    dlg.Hide();
                }
            };
            var body = new StackPanel { Spacing = 8 };
            body.Children.Add(new TextBlock
            {
                Text = "Click a file to preview it.",
                Foreground = Ui.Res("TextFillColorSecondaryBrush"),
            });
            body.Children.Add(list);
            dlg.Content = body;
            await dlg.ShowAsync();
            if (chosen == null) return;
            await Show(root, chosen, false);     // then come back to the list
        }
    }
}
