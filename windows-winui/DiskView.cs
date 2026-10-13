using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Text;

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
        public bool IsDir;
        public DateTime Modified;
        public CheckBox Check;
    }

    private static readonly string[] SortNames = { "Biggest first", "Oldest first", "Newest first", "File type" };
    private static readonly int[] TopChoices = { 0, 5, 10, 50, 100 };   // 0 = all

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
    private int measured;
    private int itemCount;
    private int sortMode;
    private int topN;
    private bool mapMode;
    private readonly ComboBox sortBox = new() { MinWidth = 140 };
    private readonly ComboBox topBox = new() { MinWidth = 90 };
    private readonly ToggleButton mapBtn = new() { Content = new FontIcon { Glyph = "\uE80A" } };
    private readonly ProgressBar progress = new() { Margin = new Thickness(26, 0, 26, 6), Visibility = Visibility.Collapsed };
    private readonly Canvas mapCanvas = new() { Margin = new Thickness(24, 0, 24, 4) };
    private readonly ScrollViewer scroll = new();
    private readonly Grid mapPanel = new() { Visibility = Visibility.Collapsed };
    private static readonly string[] DeleteModes = { "Move to Recycle Bin", "Delete permanently" };
    private readonly ComboBox deleteBox = new() { MinWidth = 170 };
    private readonly HashSet<string> selected = new(StringComparer.OrdinalIgnoreCase);
    private readonly Grid selBar = new() { Padding = new Thickness(24, 8, 24, 8), ColumnSpacing = 12, Visibility = Visibility.Collapsed };
    private readonly TextBlock selText = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Button delSelBtn = new();

    private bool Permanent => deleteBox.SelectedIndex == 1;

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
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        foreach (string n in SortNames) sortBox.Items.Add(n);
        sortBox.SelectedIndex = 0;
        sortBox.SelectionChanged += (_, _) => { sortMode = Math.Max(sortBox.SelectedIndex, 0); Resort(); };
        topBox.Items.Add("All");
        for (int k = 1; k < TopChoices.Length; k++) topBox.Items.Add("Top " + TopChoices[k]);
        topBox.SelectedIndex = 0;
        topBox.SelectionChanged += (_, _) => { topN = TopChoices[Math.Max(topBox.SelectedIndex, 0)]; Resort(); };
        ToolTipService.SetToolTip(sortBox, "Sort by size, age or type");
        ToolTipService.SetToolTip(topBox, "Show only the first items of the sort");
        ToolTipService.SetToolTip(mapBtn, "Disk map");
        mapBtn.Click += (_, _) =>
        {
            mapMode = mapBtn.IsChecked == true;
            scroll.Visibility = mapMode ? Visibility.Collapsed : Visibility.Visible;
            mapPanel.Visibility = mapMode ? Visibility.Visible : Visibility.Collapsed;
            DrawMap();
        };
        Grid.SetColumn(sortBox, 4);
        Grid.SetColumn(topBox, 5);
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        foreach (string m in DeleteModes) deleteBox.Items.Add(m);
        deleteBox.SelectedIndex = Settings.Get("deleteMode", "trash") == "permanent" ? 1 : 0;
        deleteBox.SelectionChanged += (_, _) =>
        {
            Settings.Set("deleteMode", Permanent ? "permanent" : "trash");
            UpdateSelBar();
        };
        ToolTipService.SetToolTip(deleteBox, "What Delete does here (uninstalling is always permanent)");
        Grid.SetColumn(deleteBox, 7);
        top.Children.Add(deleteBox);
        Grid.SetColumn(mapBtn, 6);
        top.Children.Add(sortBox);
        top.Children.Add(topBox);
        top.Children.Add(mapBtn);
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
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(status, 1);
        Grid.SetRow(progress, 2);
        scroll.Content = list;
        Grid.SetRow(scroll, 3);
        Grid.SetRow(mapPanel, 3);

        // disk map: colored blocks sized by space; click a folder to open it, a file to preview it
        var legend = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Margin = new Thickness(26, 4, 0, 10) };
        foreach (var (label, color) in Treemap.Kinds)
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
            item.Children.Add(new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(color), VerticalAlignment = VerticalAlignment.Center });
            item.Children.Add(new TextBlock { Text = label, Style = Ui.Sty("CaptionTextBlockStyle"), Foreground = Ui.Res("TextFillColorSecondaryBrush") });
            legend.Children.Add(item);
        }
        mapPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        mapPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(legend, 1);
        mapPanel.Children.Add(mapCanvas);
        mapPanel.Children.Add(legend);
        mapCanvas.SizeChanged += (_, _) => DrawMap();

        grid.Children.Add(top);
        grid.Children.Add(status);
        grid.Children.Add(progress);
        grid.Children.Add(scroll);
        grid.Children.Add(mapPanel);

        // selection bar: list checkboxes and right-click in the map share one selection
        selBar.Background = Ui.Res("CardBackgroundFillColorDefaultBrush");
        selBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        selBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        selBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var clearBtn = new HyperlinkButton { Content = "Clear" };
        clearBtn.Click += (_, _) => { selected.Clear(); SyncChecks(); UpdateSelBar(); DrawMap(); };
        delSelBtn.Style = Ui.Sty("AccentButtonStyle");
        delSelBtn.Click += async (_, _) => await DeleteAsync(Ordered().Select(r => r.FullPath).Where(selected.Contains).ToList());
        Grid.SetColumn(clearBtn, 1);
        Grid.SetColumn(delSelBtn, 2);
        selBar.Children.Add(selText);
        selBar.Children.Add(clearBtn);
        selBar.Children.Add(delSelBtn);
        Grid.SetRow(selBar, 4);
        grid.Children.Add(selBar);
        Content = grid;

        Loaded += (_, _) =>
        {
            if (started) return;
            started = true;
            Load(root);
        };
    }

    /// <summary>Launch options (--root, --map): the folder to open first and whether to show the map.</summary>
    public void StartWith(string folder, bool map)
    {
        if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder)) root = folder;
        if (!map) return;
        mapBtn.IsChecked = true;
        mapMode = true;
        scroll.Visibility = Visibility.Collapsed;
        mapPanel.Visibility = Visibility.Visible;
    }

    private void Load(string path)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;
        current = path;
        pathText.Text = path;
        upBtn.IsEnabled = Directory.GetParent(path) != null;

        int myGen = ++gen;
        rows.Clear();
        selected.Clear();
        UpdateSelBar();
        byPath.Clear();
        list.Children.Clear();
        total = 0;
        maxSize = 1;
        measured = 0;

        var entries = Util.Children(path);
        pending = entries.Count;
        itemCount = entries.Count;
        progress.Value = 0;
        progress.Visibility = entries.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        mapCanvas.Children.Clear();
        foreach (string e in entries)
        {
            var r = MakeRow(e);
            rows.Add(r);
            byPath[e] = r;
            list.Children.Add(r.View);
        }
        Resort();
        status.Text = entries.Count == 0 ? "Empty folder" : "Measuring 0% (0 of " + entries.Count + " items)";
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
        var r = new Row { FullPath = fullPath, IsDir = dir };
        try { r.Modified = dir ? Directory.GetLastWriteTime(fullPath) : File.GetLastWriteTime(fullPath); }
        catch (Exception) { r.Modified = DateTime.MinValue; }
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

        var icon = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        r.Check = new CheckBox { MinWidth = 32 };
        ToolTipService.SetToolTip(r.Check, "Select");
        r.Check.Click += (_, _) => Toggle(fullPath);
        icon.Children.Add(r.Check);
        icon.Children.Add(new FontIcon { Glyph = dir ? "" : "", FontSize = 16 });

        FrameworkElement label;
        if (dir)
        {
            var link = new HyperlinkButton { Content = name, Padding = new Thickness(0) };
            link.Click += (_, _) => Load(fullPath);
            label = link;
        }
        else
        {
            var link = new HyperlinkButton { Content = name, Padding = new Thickness(0) };
            ToolTipService.SetToolTip(link, "Preview");
            link.Click += async (_, _) =>
            {
                if (await Preview.Show(XamlRoot, fullPath, true)) await DeleteAsync(new List<string> { fullPath });
            };
            label = link;
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
        ToolTipService.SetToolTip(del, "Delete");
        del.Click += async (_, _) => await DeleteAsync(new List<string> { fullPath });

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
        measured++;
        if (pending == 0 || (DateTime.Now - lastSort).TotalMilliseconds > 400) Resort();
        int pct = itemCount == 0 ? 100 : measured * 100 / itemCount;
        status.Text = (pending > 0 ? $"Measuring {pct}% ({measured} of {itemCount} items)  |  " : "") + "Total " + Util.Fmt(total);
        progress.Maximum = Math.Max(itemCount, 1);
        progress.Value = measured;
        progress.Visibility = pending > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Rows in the chosen order, cut to the top N (list and map both use this).</summary>
    private List<Row> Ordered()
    {
        IEnumerable<Row> q;
        switch (sortMode)
        {
            case 1: q = rows.OrderBy(r => r.Modified); break;
            case 2: q = rows.OrderByDescending(r => r.Modified); break;
            case 3: q = rows.OrderBy(r => Treemap.KindOf(r.FullPath, r.IsDir)).ThenByDescending(r => r.Size); break;
            default: q = rows.OrderByDescending(r => r.Size); break;
        }
        return (topN > 0 ? q.Take(topN) : q).ToList();
    }

    private void DrawMap()
    {
        mapCanvas.Children.Clear();
        if (!mapMode || mapCanvas.ActualWidth <= 0 || mapCanvas.ActualHeight <= 0) return;
        var shown = Ordered().Where(r => r.Size > 0).OrderByDescending(r => r.Size).Take(150).ToList();
        var rects = Treemap.Layout(shown.Select(r => (double)r.Size).ToList(), mapCanvas.ActualWidth, mapCanvas.ActualHeight);
        for (int i = 0; i < shown.Count && i < rects.Count; i++)
        {
            var r = shown[i];
            var (x, y, w, h) = rects[i];
            var color = Treemap.Kinds[Treemap.KindOf(r.FullPath, r.IsDir)].Color;
            var block = new Border
            {
                Width = Math.Max(w - 2, 0),
                Height = Math.Max(h - 2, 0),
                CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(color),
                Padding = new Thickness(5, 3, 3, 3),
                BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.White),
                BorderThickness = new Thickness(selected.Contains(r.FullPath) ? 3 : 0),
            };
            if (w > 64 && h > 34)
            {
                var text = new StackPanel();
                text.Children.Add(new TextBlock
                {
                    Text = Path.GetFileName(r.FullPath),
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                });
                text.Children.Add(new TextBlock { Text = Util.Fmt(r.Size), FontSize = 11, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White), Opacity = 0.85 });
                block.Child = text;
            }
            ToolTipService.SetToolTip(block, Path.GetFileName(r.FullPath) + "  |  " + Util.Fmt(r.Size) + "  |  right-click to select");
            string toggled = r.FullPath;
            block.RightTapped += (_, e) => { Toggle(toggled); e.Handled = true; };
            string path = r.FullPath;
            bool dir = r.IsDir;
            block.Tapped += async (_, _) =>
            {
                if (dir) Load(path);
                else if (await Preview.Show(XamlRoot, path, true)) await DeleteAsync(new List<string> { path });
            };
            Canvas.SetLeft(block, x + 1);
            Canvas.SetTop(block, y + 1);
            mapCanvas.Children.Add(block);
        }
    }

    private void Resort()
    {
        var ordered = Ordered();
        long visibleMax = Math.Max(ordered.Count == 0 ? 1 : ordered.Max(r => r.Size), 1);
        list.Children.Clear();
        foreach (Row r in ordered)
        {
            list.Children.Add(r.View);
            r.Bar.Maximum = visibleMax;
            r.Bar.Value = Math.Max(r.Size, 0);
        }
        lastSort = DateTime.Now;
        DrawMap();
    }

    private void Toggle(string path)
    {
        if (!selected.Remove(path)) selected.Add(path);
        SyncChecks();
        UpdateSelBar();
        DrawMap();
    }

    private void SyncChecks()
    {
        foreach (Row r in rows) r.Check.IsChecked = selected.Contains(r.FullPath);
    }

    private void UpdateSelBar()
    {
        long size = rows.Where(r => selected.Contains(r.FullPath)).Sum(r => Math.Max(r.Size, 0));
        selText.Text = $"{selected.Count} selected  |  {Util.Fmt(size)}";
        delSelBtn.Content = Permanent ? "Delete selected permanently" : "Move selected to Recycle Bin";
        selBar.Visibility = selected.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Why a path may not be deleted permanently (drive roots and system folders), or null.</summary>
    private static string PermanentBlock(string path)
    {
        string full = Path.GetFullPath(path).TrimEnd('\\');
        if (string.Equals(Path.GetPathRoot(path)?.TrimEnd('\\'), full, StringComparison.OrdinalIgnoreCase))
            return "a whole drive";
        foreach (var folder in new[] { Environment.SpecialFolder.Windows, Environment.SpecialFolder.ProgramFiles,
                                       Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.CommonApplicationData })
        {
            string root = Environment.GetFolderPath(folder).TrimEnd('\\');
            if (root.Length > 3 && (full.Equals(root, StringComparison.OrdinalIgnoreCase)
                || full.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase)))
                return "a system folder (use Move to Recycle Bin, or uninstall the program)";
        }
        return null;
    }

    /// <summary>Asks first, then moves to the Recycle Bin or deletes permanently, as the setting says.</summary>
    private async Task DeleteAsync(List<string> paths)
    {
        if (paths.Count == 0) return;
        bool permanent = Permanent;
        string what = paths.Count == 1 ? $"\"{Path.GetFileName(paths[0])}\"" : $"{paths.Count} items";
        string body = (permanent ? "This cannot be undone: nothing goes to the Recycle Bin." : "You can restore them from the Recycle Bin.")
                      + "\n\n" + string.Join("\n", paths.Take(12)) + (paths.Count > 12 ? $"\n...and {paths.Count - 12} more" : "");
        if (!await Ui.Confirm(XamlRoot, permanent ? $"Delete {what} permanently?" : $"Move {what} to the Recycle Bin?",
                              body, DeleteModes[permanent ? 1 : 0])) return;
        var failed = new List<string>();
        await Task.Run(() =>
        {
            foreach (string p in paths)
            {
                try
                {
                    if (!permanent) { Util.SendToRecycleBin(p); continue; }
                    string block = PermanentBlock(p);
                    if (block != null) throw new InvalidOperationException("refused: " + block);
                    if (Directory.Exists(p)) Directory.Delete(p, true); else File.Delete(p);
                }
                catch (Exception ex)
                {
                    lock (failed) failed.Add(Path.GetFileName(p) + ": " + ex.Message);
                }
            }
        });
        if (failed.Count > 0) await Ui.Message(XamlRoot, "Some items could not be deleted", string.Join("\n", failed.Take(12)));
        freeChanged();
        Load(current);
    }
}
