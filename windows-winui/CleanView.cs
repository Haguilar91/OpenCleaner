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
    private readonly Button scanBtn = new() { Content = "Rescan" };
    private readonly Button adminBtn = new() { Content = "Restart as administrator" };
    private readonly Button cleanBtn = new() { Content = "Clean selected" };
    private readonly ProgressRing ring = new() { Width = 22, Height = 22, Visibility = Visibility.Collapsed };
    private readonly List<(Item item, CheckBox box)> rows = new();
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
        SetBusy(true, "Scanning...");
        List<Item> found;
        try
        {
            found = await Task.Run(() => Scanners.RunAll());
        }
        catch (Exception ex)
        {
            found = new List<Item>();
            await Ui.Message(XamlRoot, "Scan failed", ex.Message);
        }
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
        bool disabled = Disabled(it);
        var g = new Grid { ColumnSpacing = 12 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        UIElement lead;
        if (it.Empty)
        {
            lead = new FontIcon
            {
                Glyph = it.Failed ? "" : "",
                FontSize = 16,
                Margin = new Thickness(8, 0, 8, 0),
                Foreground = it.Failed ? Ui.Red : Ui.Green,
            };
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

        string sizeText = it.Empty ? (it.Failed ? "Scan failed" : "Clean")
            : (it.NeedsAdmin && !isAdmin ? "Needs admin" : Util.Fmt(it.Size));
        var size = new TextBlock { Text = sizeText, VerticalAlignment = VerticalAlignment.Center };
        if (it.Empty) size.Foreground = it.Failed ? Ui.Red : Ui.Green;
        else size.Foreground = Ui.Res("TextFillColorSecondaryBrush");

        Grid.SetColumn(text, 1);
        Grid.SetColumn(size, 2);
        g.Children.Add(lead);
        g.Children.Add(text);
        g.Children.Add(size);
        if (disabled && !it.Empty) g.Opacity = 0.6;
        return Ui.Card(g);
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
        summary.Text = $"{sel.Count} selected  |  {Util.Fmt(known)}" + (unknown ? " + more" : "");
        cleanBtn.IsEnabled = sel.Count > 0 && !busy;
    }

    private async Task CleanAsync()
    {
        var sel = Selected();
        if (sel.Count == 0 || busy) return;

        var sb = new StringBuilder("This cannot be undone.\n\n");
        foreach (Item it in sel) sb.AppendLine($"• {it.Title}  ({Util.Fmt(it.Size)})");
        if (!await Ui.Confirm(XamlRoot, "Delete selected items?", sb.ToString(), "Clean")) return;

        long before = Util.FreeSpace();
        SetBusy(true, "Cleaning...");
        var results = await Task.Run(() => sel.Select(Scanners.Clean).ToList());
        SetBusy(false, "");
        freeChanged();

        long freed = Util.FreeSpace() - before;
        var msg = new StringBuilder("Cleaning finished");
        if (freed > 0) msg.Append($"  |  freed {Util.Fmt(freed)}");
        int skipped = results.Sum(r => r.Skipped);
        if (skipped > 0) msg.Append($"\n\n{skipped} file(s) were in use and skipped.");
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
