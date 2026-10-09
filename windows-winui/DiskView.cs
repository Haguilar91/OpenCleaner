using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OpenCleaner;

/// <summary>Disk usage page: browse folders by size; deleting only sends items to the Recycle Bin.</summary>
public sealed class DiskView : UserControl
{
    private sealed class Row
    {
        public string FullPath;
        public long Size = -1;
        public Grid View;
        public TextBlock SizeText;
        public ProgressBar Bar;
    }

    private readonly Action freeChanged;
    private readonly StackPanel list = new() { Spacing = 4, Margin = new Thickness(24, 4, 24, 16) };
    private readonly TextBlock pathText = new()
    {
        VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };
    private readonly TextBlock status = new()
    {
        Margin = new Thickness(26, 0, 0, 6),
        Foreground = Ui.Res("TextFillColorSecondaryBrush"),
    };
    private readonly Button upBtn = new() { Content = new FontIcon { Glyph = "" } };
    private readonly ComboBox driveBox = new() { MinWidth = 90, PlaceholderText = "Drive" };
    private readonly Dictionary<string, Row> byPath = new();
    private readonly List<Row> rows = new();
    private string root = Environment.GetEnvironmentVariable("USERPROFILE");
    private string current;
    private int gen;
    private int pending;
    private long total;
    private long maxSize = 1;
    private DateTime lastSort = DateTime.MinValue;
    private bool started;

    public DiskView(Action freeChanged)
    {
        this.freeChanged = freeChanged;

        var homeBtn = new Button { Content = "My folder" };
        homeBtn.Click += (_, _) =>
        {
            driveBox.SelectedIndex = -1;
            root = Environment.GetEnvironmentVariable("USERPROFILE");
            Load(root);
        };
        upBtn.Click += (_, _) =>
        {
            var parent = current == null ? null : Directory.GetParent(current)?.FullName;
            if (parent != null) Load(parent);
        };
        foreach (DriveInfo d in DriveInfo.GetDrives())
            if (d.DriveType == DriveType.Fixed && d.IsReady) driveBox.Items.Add(d.Name);
        driveBox.SelectionChanged += (_, _) =>
        {
            if (driveBox.SelectedItem is string drive)
            {
                root = drive;
                Load(drive);
            }
        };

        var top = new Grid { Padding = new Thickness(24, 4, 24, 8), ColumnSpacing = 8 };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(homeBtn, 1);
        Grid.SetColumn(driveBox, 2);
        Grid.SetColumn(pathText, 3);
        top.Children.Add(upBtn);
        top.Children.Add(homeBtn);
        top.Children.Add(driveBox);
        top.Children.Add(pathText);

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(status, 1);
        var scroll = new ScrollViewer { Content = list };
        Grid.SetRow(scroll, 2);
        grid.Children.Add(top);
        grid.Children.Add(status);
        grid.Children.Add(scroll);
        Content = grid;

        Loaded += (_, _) =>
        {
            if (started) return;
            started = true;
            Load(root);
        };
    }

    private void Load(string path)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;
        current = path;
        pathText.Text = path;
        upBtn.IsEnabled = Directory.GetParent(path) != null;

        int myGen = ++gen;
        rows.Clear();
        byPath.Clear();
        list.Children.Clear();
        total = 0;
        maxSize = 1;

        var entries = Util.Children(path);
        pending = entries.Count;
        foreach (string e in entries)
        {
            var r = MakeRow(e);
            rows.Add(r);
            byPath[e] = r;
            list.Children.Add(r.View);
        }
        status.Text = entries.Count == 0 ? "Empty folder" : "Measuring...";
        if (entries.Count == 0) return;

        var dq = DispatcherQueue;
        _ = Task.Run(() => Parallel.ForEach(entries, new ParallelOptions { MaxDegreeOfParallelism = 4 }, e =>
        {
            if (gen != myGen) return;
            long s = Util.Size(e);
            dq.TryEnqueue(() => SetSize(myGen, e, s));
        }));
    }

    private Row MakeRow(string fullPath)
    {
        bool dir = Util.IsDir(fullPath);
        var r = new Row { FullPath = fullPath };
        string name = Path.GetFileName(fullPath);
        if (string.IsNullOrEmpty(name)) name = fullPath;

        var g = new Grid
        {
            ColumnSpacing = 12,
            Padding = new Thickness(12, 6, 8, 6),
            Background = Ui.Res("CardBackgroundFillColorDefaultBrush"),
            CornerRadius = new CornerRadius(6),
        };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new FontIcon { Glyph = dir ? "" : "", FontSize = 16 };

        FrameworkElement label;
        if (dir)
        {
            var link = new HyperlinkButton { Content = name, Padding = new Thickness(0) };
            link.Click += (_, _) => Load(fullPath);
            label = link;
        }
        else
        {
            label = new TextBlock
            {
                Text = name,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }
        label.VerticalAlignment = VerticalAlignment.Center;

        r.Bar = new ProgressBar { Minimum = 0, Maximum = 1, Value = 0, VerticalAlignment = VerticalAlignment.Center };
        r.SizeText = new TextBlock
        {
            Text = "...",
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Ui.Res("TextFillColorSecondaryBrush"),
        };
        var del = new Button
        {
            Content = new FontIcon { Glyph = "", FontSize = 14 },
            Padding = new Thickness(8, 4, 8, 4),
        };
        ToolTipService.SetToolTip(del, "Move to Recycle Bin");
        del.Click += async (_, _) => await RecycleAsync(fullPath);

        Grid.SetColumn(label, 1);
        Grid.SetColumn(r.Bar, 2);
        Grid.SetColumn(r.SizeText, 3);
        Grid.SetColumn(del, 4);
        g.Children.Add(icon);
        g.Children.Add(label);
        g.Children.Add(r.Bar);
        g.Children.Add(r.SizeText);
        g.Children.Add(del);
        r.View = g;
        return r;
    }

    private void SetSize(int myGen, string path, long size)
    {
        if (myGen != gen || !byPath.TryGetValue(path, out Row r)) return;
        r.Size = size;
        r.SizeText.Text = Util.Fmt(size);
        total += size;
        maxSize = Math.Max(maxSize, size);
        pending--;
        if (pending == 0 || (DateTime.Now - lastSort).TotalMilliseconds > 400) Resort();
        status.Text = (pending > 0 ? "Measuring...  " : "") + "Total " + Util.Fmt(total);
    }

    private void Resort()
    {
        rows.Sort((a, b) => b.Size.CompareTo(a.Size));
        list.Children.Clear();
        foreach (Row r in rows)
        {
            list.Children.Add(r.View);
            r.Bar.Maximum = Math.Max(maxSize, 1);
            r.Bar.Value = Math.Max(r.Size, 0);
        }
        lastSort = DateTime.Now;
    }

    private async Task RecycleAsync(string path)
    {
        if (!await Ui.Confirm(XamlRoot, "Move to Recycle Bin?", path, "Move to Recycle Bin")) return;
        try
        {
            await Task.Run(() => Util.SendToRecycleBin(path));
        }
        catch (Exception ex)
        {
            await Ui.Message(XamlRoot, "Could not move to Recycle Bin", ex.Message);
        }
        freeChanged();
        Load(current);
    }
}
