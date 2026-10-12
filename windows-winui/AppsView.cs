using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OpenCleaner;

/// <summary>Installed programs with size and last-used date; sort, Top N, and uninstall with a review of leftover folders.</summary>
public sealed class AppsView : UserControl
{
    private static readonly string[] SortNames = { "Biggest first", "Least recently used", "Recently used", "Name" };
    private static readonly int[] TopChoices = { 0, 5, 10, 50, 100 };   // 0 = all

    private readonly Action freeChanged;
    private readonly StackPanel list = new() { Spacing = 4, Margin = new Thickness(24, 4, 24, 16) };
    private readonly TextBlock status = new() { Margin = new Thickness(26, 0, 0, 6), Foreground = Ui.Res("TextFillColorSecondaryBrush") };
    private readonly ProgressBar progress = new() { Margin = new Thickness(26, 0, 26, 6), Visibility = Visibility.Collapsed };
    private readonly ComboBox sortBox = new() { MinWidth = 170 };
    private readonly ComboBox topBox = new() { MinWidth = 90 };
    private List<AppEntry> apps = new();
    private int sortMode;
    private int topN;
    private int gen;
    private int measured;
    private int toMeasure;
    private bool started;

    public AppsView(Action freeChanged)
    {
        this.freeChanged = freeChanged;

        foreach (string n in SortNames) sortBox.Items.Add(n);
        sortBox.SelectedIndex = 0;
        sortBox.SelectionChanged += (_, _) => { sortMode = Math.Max(sortBox.SelectedIndex, 0); Render(); };
        topBox.Items.Add("All");
        for (int k = 1; k < TopChoices.Length; k++) topBox.Items.Add("Top " + TopChoices[k]);
        topBox.SelectedIndex = 0;
        topBox.SelectionChanged += (_, _) => { topN = TopChoices[Math.Max(topBox.SelectedIndex, 0)]; Render(); };

        var reload = new Button { Content = "Rescan" };
        reload.Click += async (_, _) => await LoadAsync();

        var top = new Grid { Padding = new Thickness(24, 4, 24, 8), ColumnSpacing = 8 };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(sortBox, 1);
        Grid.SetColumn(topBox, 2);
        Grid.SetColumn(reload, 3);
        top.Children.Add(sortBox);
        top.Children.Add(topBox);
        top.Children.Add(reload);

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(status, 1);
        Grid.SetRow(progress, 2);
        var scroll = new ScrollViewer { Content = list };
        Grid.SetRow(scroll, 3);
        grid.Children.Add(top);
        grid.Children.Add(status);
        grid.Children.Add(progress);
        grid.Children.Add(scroll);
        Content = grid;

        Loaded += async (_, _) =>
        {
            if (started) return;
            started = true;
            await LoadAsync();
        };
    }

    private async Task LoadAsync()
    {
        int myGen = ++gen;
        status.Text = "Reading installed programs...";
        var found = await Task.Run(() =>
        {
            var l = Apps.Installed();
            Apps.FillLastUsed(l);
            return l;
        });
        if (myGen != gen) return;
        apps = found;
        Render();

        // sizes that Windows does not report are measured from the install folder
        var pending = apps.Where(a => a.Size <= 0 && a.InstallLocation.Length > 0).ToList();
        toMeasure = pending.Count;
        measured = 0;
        if (pending.Count == 0) return;
        progress.Maximum = pending.Count;
        progress.Value = 0;
        progress.Visibility = Visibility.Visible;
        var dq = DispatcherQueue;
        await Task.Run(() => Parallel.ForEach(pending, new ParallelOptions { MaxDegreeOfParallelism = 4 }, a =>
        {
            if (gen != myGen) return;
            Apps.Measure(a);
            dq.TryEnqueue(() =>
            {
                if (gen != myGen) return;
                measured++;
                progress.Value = measured;
                if (measured == toMeasure || measured % 5 == 0) Render();
                if (measured == toMeasure) progress.Visibility = Visibility.Collapsed;
            });
        }));
    }

    /// <summary>Apps in the chosen order, cut to the top N.</summary>
    private List<AppEntry> Ordered()
    {
        IEnumerable<AppEntry> q;
        switch (sortMode)
        {
            case 1: q = apps.OrderBy(a => a.LastUsed); break;
            case 2: q = apps.OrderByDescending(a => a.LastUsed); break;
            case 3: q = apps.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase); break;
            default: q = apps.OrderByDescending(a => a.Size); break;
        }
        return (topN > 0 ? q.Take(topN) : q).ToList();
    }

    private static string Ago(DateTime d)
    {
        if (d == DateTime.MinValue) return "last used: unknown";
        double days = (DateTime.Now - d).TotalDays;
        if (days < 1) return "used today";
        if (days < 2) return "used yesterday";
        if (days < 60) return $"used {(int)days} days ago";
        if (days < 730) return $"used {(int)(days / 30)} months ago";
        return $"used {(int)(days / 365)} years ago";
    }

    private void Render()
    {
        long total = apps.Sum(a => Math.Max(a.Size, 0));
        string pct = toMeasure > 0 && measured < toMeasure ? $"Measuring {measured * 100 / toMeasure}% ({measured} of {toMeasure})  |  " : "";
        status.Text = pct + $"{apps.Count} programs  |  {Util.Fmt(total)}  |  last used is approximate";
        list.Children.Clear();
        foreach (AppEntry a in Ordered()) list.Children.Add(Row(a));
    }

    private UIElement Row(AppEntry a)
    {
        var g = new Grid
        {
            ColumnSpacing = 12,
            Padding = new Thickness(12, 6, 8, 6),
            Background = Ui.Res("CardBackgroundFillColorDefaultBrush"),
            CornerRadius = new CornerRadius(6),
        };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = a.Name, TextTrimming = TextTrimming.CharacterEllipsis });
        string sub = (a.Publisher + "  " + a.Version).Trim();
        text.Children.Add(new TextBlock
        {
            Text = sub.Length > 0 ? sub : a.InstallLocation,
            Style = Ui.Sty("CaptionTextBlockStyle"),
            Foreground = Ui.Res("TextFillColorSecondaryBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        var used = new TextBlock
        {
            Text = Ago(a.LastUsed),
            Style = Ui.Sty("CaptionTextBlockStyle"),
            Foreground = Ui.Res("TextFillColorSecondaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var size = new TextBlock
        {
            Text = a.Size > 0 ? Util.Fmt(a.Size) : "...",
            Foreground = Ui.Res("TextFillColorSecondaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var del = new Button { Content = new FontIcon { Glyph = "", FontSize = 14 }, Padding = new Thickness(8, 4, 8, 4) };
        ToolTipService.SetToolTip(del, "Uninstall...");
        del.Click += async (_, _) => await UninstallAsync(a);

        Grid.SetColumn(used, 1);
        Grid.SetColumn(size, 2);
        Grid.SetColumn(del, 3);
        g.Children.Add(text);
        g.Children.Add(used);
        g.Children.Add(size);
        g.Children.Add(del);
        return g;
    }

    /// <summary>Review first: the program's own uninstaller runs, then the ticked leftover folders are deleted permanently.</summary>
    private async Task UninstallAsync(AppEntry a)
    {
        var left = await Task.Run(() => Apps.Leftovers(a));
        var body = new StackPanel { Spacing = 8, MinWidth = 520 };
        body.Children.Add(new TextBlock
        {
            Text = "WARNING: this deletes permanently. Nothing goes to the Recycle Bin and it cannot be undone.",
            TextWrapping = TextWrapping.Wrap,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = Ui.Red,
        });
        body.Children.Add(new TextBlock
        {
            Text = "Windows will run the program's own uninstaller, then the ticked folders are deleted. Programs installed for all users "
                 + "(like Microsoft Office) ask for administrator permission. "
                 + "Roaming and ProgramData folders may hold settings, so they start unticked.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Ui.Res("TextFillColorSecondaryBrush"),
        });
        var boxes = new List<(LeftoverItem item, CheckBox box)>();
        foreach (LeftoverItem it in left)
        {
            var cb = new CheckBox { Content = $"{it.Path}   ({Util.Fmt(it.Size)}, {it.Detail})", IsChecked = it.Selected };
            boxes.Add((it, cb));
            body.Children.Add(cb);
        }
        if (left.Count == 0) body.Children.Add(new TextBlock { Text = "No leftover folders found." });

        var dlg = new ContentDialog
        {
            Title = $"Uninstall {a.Name}?",
            Content = new ScrollViewer { Content = body, MaxHeight = 360 },
            PrimaryButtonText = "Delete permanently",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;

        var picked = boxes.Where(b => b.box.IsChecked == true).Select(b => b.item.Path).ToList();
        string err = await Task.Run(() => Apps.RunUninstaller(a));
        if (err != null)
        {
            await Ui.Message(XamlRoot, "Uninstall", err);
            return;
        }
        // Some uninstallers (Office) return at once and keep working in another process: wait until the
        // program is really gone before touching any leftovers.
        for (int i = 0; i < 200 && await Task.Run(() => Apps.StillInstalled(a)); i++)
        {
            status.Text = $"Waiting for the {a.Name} uninstaller to finish...";
            await Task.Delay(3000);
        }
        if (await Task.Run(() => Apps.StillInstalled(a)))
        {
            await Ui.Message(XamlRoot, "Uninstall", $"{a.Name} is still installed (the uninstaller was cancelled or is still running). Its leftover folders were left alone.");
            await LoadAsync();
            return;
        }
        // the install folder, if the uninstaller left it behind
        if (a.InstallLocation.Length > 0 && Directory.Exists(a.InstallLocation)) picked.Add(a.InstallLocation);
        var failed = new List<string>();
        var needAdmin = new List<string>();
        int moved = 0;
        foreach (string p in picked)
        {
            string e = await Task.Run(() => Apps.RemoveLeftover(a, p));
            if (e == null) moved++;
            else if (e == Apps.NeedsAdmin) needAdmin.Add(p);
            else failed.Add(p + ": " + e);
        }
        if (needAdmin.Count > 0)
        {
            string e = await Task.Run(() => Apps.RemoveLeftoversAsAdmin(a, needAdmin));
            if (e == null) moved += needAdmin.Count; else failed.Add(e);
        }
        string msg = $"{a.Name} was uninstalled." + (moved > 0 ? $" {moved} folder(s) deleted." : "");
        if (failed.Count > 0) msg += "\n\nCould not remove:\n" + string.Join("\n", failed);
        await Ui.Message(XamlRoot, "Uninstall finished", msg);
        freeChanged();
        await LoadAsync();
    }
}
