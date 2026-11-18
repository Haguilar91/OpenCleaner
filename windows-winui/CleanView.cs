using System.Diagnostics;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OpenCleaner;

/// <summary>The Clean page: scan, tick, confirm, clean. Built in code to keep the XAML minimal.</summary>
public sealed class CleanView : UserControl
{
    private readonly bool isAdmin = Util.IsAdmin();
    private readonly Action freeChanged;
    private readonly StackPanel listPanel = new() { Spacing = 6, Margin = new Thickness(24, 4, 24, 16) };
    private readonly TextBlock summary = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Button scanBtn = new() { Content = L.T("Rescan") };
    private readonly Button adminBtn = new() { Content = L.T("Restart as administrator") };
    private readonly Button cleanBtn = new() { Content = L.T("Clean selected") };
    private readonly ProgressRing ring = new() { Width = 22, Height = 22, Visibility = Visibility.Collapsed };
    private readonly List<(Item item, CheckBox box)> rows = new();
    private List<Item> items = new();
    private bool busy;
    private bool started;

    public CleanView(Action freeChanged)
    {
        this.freeChanged = freeChanged;

        var scroll = new ScrollViewer { Content = listPanel };

        var bar = new Grid
        {
            Padding = new Thickness(24, 12, 24, 12),
            ColumnSpacing = 12,
            Background = Ui.Res("CardBackgroundFillColorDefaultBrush"),
        };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(adminBtn, 1);
        Grid.SetColumn(summary, 2);
        Grid.SetColumn(ring, 3);
        Grid.SetColumn(cleanBtn, 4);
        bar.Children.Add(scanBtn);
        bar.Children.Add(adminBtn);
        bar.Children.Add(summary);
        bar.Children.Add(ring);
        bar.Children.Add(cleanBtn);

        cleanBtn.Style = Ui.Sty("AccentButtonStyle");
        adminBtn.Visibility = isAdmin ? Visibility.Collapsed : Visibility.Visible;
        scanBtn.Click += async (_, _) => await ScanAsync();
        cleanBtn.Click += async (_, _) => await CleanAsync();
        adminBtn.Click += (_, _) => RestartAsAdmin();

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(bar, 1);
        grid.Children.Add(scroll);
        grid.Children.Add(bar);
        Content = grid;

        Loaded += async (_, _) =>
        {
            if (started) return;
            started = true;
            await ScanAsync();
        };
    }

    private bool Disabled(Item it) => it.Empty || (it.NeedsAdmin && !isAdmin);

    private void SetBusy(bool on, string text)
    {
        busy = on;
        scanBtn.IsEnabled = !on;
        cleanBtn.IsEnabled = !on && Selected().Count > 0;
        ring.IsActive = on;
        ring.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (on) summary.Text = text;
    }

    private async Task ScanAsync()
    {
        if (busy) return;
        SetBusy(true, L.T("Scanning..."));
        List<Item> found;
        try
        {
            found = await Task.Run(() => Scanners.RunAll());
        }
        catch (Exception ex)
        {
            found = new List<Item>();
            await Ui.Message(XamlRoot, L.T("Scan failed"), ex.Message);
        }
        items = found;
        Populate(found);
        SetBusy(false, "");
        UpdateSummary();
        freeChanged();
    }

    private void Populate(List<Item> items)
    {
        listPanel.Children.Clear();
        rows.Clear();
        string lastGroup = null;
        foreach (Item it in items)
        {
            if (it.Group != lastGroup)
            {
                lastGroup = it.Group;
                listPanel.Children.Add(new TextBlock
                {
                    Text = it.Group,
                    Style = Ui.Sty("BodyStrongTextBlockStyle"),
                    Margin = new Thickness(2, 14, 0, 2),
                });
            }
            listPanel.Children.Add(BuildRow(it));
        }
    }

    private UIElement BuildRow(Item it)
    {
        if (it.Kind == "hidden") return BuildHiddenRow(it);
        bool disabled = Disabled(it);
        var g = new Grid { ColumnSpacing = 12 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        UIElement lead;
        if (it.Empty)
        {
            var mark = Icons.Make(it.Failed ? Icons.X : Icons.Check, 16);
            mark.Margin = new Thickness(8, 0, 8, 0);
            mark.Foreground = it.Failed ? Ui.Red : Ui.Green;
            lead = mark;
        }
        else
        {
            var box = new CheckBox { IsChecked = it.Selected && !disabled, IsEnabled = !disabled, MinWidth = 32 };
            box.Click += (_, _) => UpdateSummary();
            rows.Add((it, box));
            lead = box;
        }

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = it.Title, TextWrapping = TextWrapping.Wrap });
        text.Children.Add(new TextBlock
        {
            Text = it.Subtitle,
            Style = Ui.Sty("CaptionTextBlockStyle"),
            Foreground = Ui.Res("TextFillColorSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap,
        });

        string sizeText = it.Empty ? (it.Failed ? L.T("Scan failed") : L.T("status|Clean"))
            : (it.NeedsAdmin && !isAdmin ? L.T("Needs admin") : Util.Fmt(it.Size));
        var size = new TextBlock { Text = sizeText, VerticalAlignment = VerticalAlignment.Center };
        if (it.Empty) size.Foreground = it.Failed ? Ui.Red : Ui.Green;
        else size.Foreground = Ui.Res("TextFillColorSecondaryBrush");

        Grid.SetColumn(text, 1);
        Grid.SetColumn(size, 2);
        g.Children.Add(lead);
        g.Children.Add(text);
        g.Children.Add(size);
        if (!it.Empty && it.Paths.Count > 0)
        {
            var review = new Button
            {
                Content = Icons.Make(Icons.Review, 14),
                Padding = new Thickness(8, 4, 8, 4),
            };
            // the eye: one file opens straight in the preview, anything else lists its files
            bool single = it.Paths.Count == 1 && !Util.IsDir(it.Paths[0]) && File.Exists(it.Paths[0]);
            ToolTipService.SetToolTip(review, single ? L.T("Preview") : L.T("Review files"));
            review.Click += async (_, _) =>
            {
                if (single) await Preview.Show(XamlRoot, it.Paths[0], false);
                else await Preview.Review(XamlRoot, it);
            };
            Grid.SetColumn(review, 3);
            g.Children.Add(review);
        }
        if (it.Kind == "big")
        {
            var hide = new Button { Content = Icons.Make(Icons.Hide, 14), Padding = new Thickness(8, 4, 8, 4) };
            ToolTipService.SetToolTip(hide, L.T("Hide this file"));
            hide.Click += (_, _) => HideFile(it);
            Grid.SetColumn(hide, 4);
            g.Children.Add(hide);
        }
        if (disabled && !it.Empty) g.Opacity = 0.6;
        return Ui.Card(g);
    }

    /// <summary>"Hidden files (N)": its Show button lists the hidden large files so they can be shown again.</summary>
    private UIElement BuildHiddenRow(Item it)
    {
        var g = new Grid { ColumnSpacing = 12 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var mark = Icons.Make(Icons.Hide, 16);
        mark.Margin = new Thickness(8, 0, 8, 0);
        mark.Foreground = Ui.Res("TextFillColorSecondaryBrush");
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = it.Title, TextWrapping = TextWrapping.Wrap });
        text.Children.Add(new TextBlock
        {
            Text = it.Subtitle,
            Style = Ui.Sty("CaptionTextBlockStyle"),
            Foreground = Ui.Res("TextFillColorSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap,
        });
        var show = new Button { Content = L.T("Show"), VerticalAlignment = VerticalAlignment.Center };
        show.Click += async (_, _) => await ShowHiddenAsync();
        Grid.SetColumn(text, 1);
        Grid.SetColumn(show, 2);
        g.Children.Add(mark);
        g.Children.Add(text);
        g.Children.Add(show);
        return Ui.Card(g);
    }

    /// <summary>Keeps a large file out of the list from now on; nothing is deleted.</summary>
    private void HideFile(Item it)
    {
        if (busy) return;
        foreach (var (item, box) in rows) item.Selected = box.IsChecked == true;   // keep the ticks through the refill
        items = Scanners.Hide(items, it);
        Populate(items);
        UpdateSummary();
        summary.Text = L.T("{0} is hidden. It stays out of Large files until you show it again.", it.Title);
    }

    /// <summary>Lists the hidden files with checkboxes; the ticked ones are shown again after a rescan.</summary>
    private async Task ShowHiddenAsync()
    {
        var checks = new List<(string path, CheckBox box)>();
        var list = new StackPanel { Spacing = 2 };
        foreach (string p in HiddenFiles.All())
        {
            string size;
            try { size = File.Exists(p) ? Util.Fmt(new FileInfo(p).Length) : L.T("no longer exists"); }
            catch (Exception) { size = "?"; }
            var label = new StackPanel();
            label.Children.Add(new TextBlock { Text = Path.GetFileName(p), TextTrimming = TextTrimming.CharacterEllipsis });
            label.Children.Add(new TextBlock
            {
                Text = Path.GetDirectoryName(p) + "  |  " + size,
                Style = Ui.Sty("CaptionTextBlockStyle"),
                Foreground = Ui.Res("TextFillColorSecondaryBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            var box = new CheckBox { Content = label };
            checks.Add((p, box));
            list.Children.Add(box);
        }
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(new TextBlock
        {
            Text = L.T("These files are left out of Large files. Tick the ones to show again."),
            Foreground = Ui.Res("TextFillColorSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap,
        });
        body.Children.Add(new ScrollViewer { Content = list, MaxHeight = 340 });
        var dlg = new ContentDialog
        {
            Title = L.T("Hidden files"),
            Content = body,
            PrimaryButtonText = L.T("Show again"),
            CloseButtonText = L.T("Close"),
            DefaultButton = ContentDialogButton.Close,
            IsPrimaryButtonEnabled = false,
            XamlRoot = XamlRoot,
        };
        foreach (var (_, box) in checks)
            box.Click += (_, _) => dlg.IsPrimaryButtonEnabled = checks.Any(c => c.box.IsChecked == true);
        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
        var back = checks.Where(c => c.box.IsChecked == true).Select(c => c.path).ToList();
        if (back.Count == 0) return;
        HiddenFiles.Remove(back);
        await ScanAsync();
    }

    private List<Item> Selected()
    {
        var sel = new List<Item>();
        foreach (var (item, box) in rows)
            if (box.IsChecked == true && box.IsEnabled) sel.Add(item);
        return sel;
    }

    private void UpdateSummary()
    {
        var sel = Selected();
        long known = 0;
        bool unknown = false;
        foreach (Item it in sel)
        {
            if (it.Size >= 0) known += it.Size; else unknown = true;
        }
        summary.Text = L.T("{0} selected  |  {1}", sel.Count, Util.Fmt(known)) + (unknown ? " " + L.T("+ more") : "");
        cleanBtn.IsEnabled = sel.Count > 0 && !busy;
    }

    private async Task CleanAsync()
    {
        var sel = Selected();
        if (sel.Count == 0 || busy) return;

        var sb = new StringBuilder(L.T("This cannot be undone.") + "\n\n");
        foreach (Item it in sel) sb.AppendLine($"• {it.Title}  ({Util.Fmt(it.Size)})");
        if (!await Ui.Confirm(XamlRoot, L.T("Delete selected items?"), sb.ToString(), L.T("Clean"))) return;

        long before = Util.FreeSpace();
        SetBusy(true, L.T("Cleaning..."));
        var results = await Task.Run(() => sel.Select(Scanners.Clean).ToList());
        SetBusy(false, "");
        freeChanged();

        long freed = Util.FreeSpace() - before;
        var msg = new StringBuilder(L.T("Cleaning finished"));
        if (freed > 0) msg.Append("  |  " + L.T("freed {0}", Util.Fmt(freed)));
        int skipped = results.Sum(r => r.Skipped);
        if (skipped > 0) msg.Append("\n\n" + L.T("{0} file(s) were in use and skipped.", skipped));
        foreach (var r in results)
            if (r.Error != null) msg.Append($"\n\n{r.Title}: {r.Error}");
        await Ui.Message(XamlRoot, "OpenCleaner", msg.ToString());
        await ScanAsync();
    }

    private void RestartAsAdmin()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath) { Verb = "runas", UseShellExecute = true });
            Application.Current.Exit();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // the user cancelled the UAC prompt
        }
    }
}
