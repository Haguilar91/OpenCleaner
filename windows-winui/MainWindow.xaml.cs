using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OpenCleaner;

public sealed partial class MainWindow : Window
{
    private readonly TextBlock freeText = new() { VerticalAlignment = VerticalAlignment.Center };

    public MainWindow()
    {
        InitializeComponent();
        Title = "OpenCleaner";
        try { AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "icon.ico")); } catch { }
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1080, 780));

        var clean = new CleanView(UpdateFree);
        var disk = new DiskView(UpdateFree);
        var appsPage = new AppsView(UpdateFree);

        var title = new TextBlock
        {
            Text = "OpenCleaner",
            Style = Ui.Sty("SubtitleTextBlockStyle"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var header = new Grid { Padding = new Thickness(20, 12, 20, 8), ColumnSpacing = 12 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        updateBtn.Content = "Check for updates";
        updateBtn.Click += async (_, _) => { if (pendingUpdate == null) await CheckUpdatesAsync(true); else await OfferUpdateAsync(); };
        Grid.SetColumn(updateBtn, 1);
        Grid.SetColumn(freeText, 2);
        header.Children.Add(title);
        header.Children.Add(updateBtn);
        header.Children.Add(freeText);

        var host = new ContentControl
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
        };
        var nav = new NavigationView
        {
            PaneDisplayMode = NavigationViewPaneDisplayMode.Left,
            IsSettingsVisible = false,
            IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed,
            OpenPaneLength = 190,
            Content = host,
        };
        var cleanItem = new NavigationViewItem { Content = "Clean", Icon = new SymbolIcon(Symbol.Delete), Tag = "clean" };
        var diskItem = new NavigationViewItem { Content = "Disk usage", Icon = new FontIcon { Glyph = "" }, Tag = "disk" };
        nav.MenuItems.Add(cleanItem);
        var appsItem = new NavigationViewItem { Content = "Apps", Icon = new FontIcon { Glyph = "\uE71D" }, Tag = "apps" };
        nav.MenuItems.Add(diskItem);
        nav.MenuItems.Add(appsItem);
        nav.SelectionChanged += (s, e) =>
        {
            var tag = (e.SelectedItem as NavigationViewItem)?.Tag as string;
            host.Content = tag == "disk" ? disk : tag == "apps" ? appsPage : clean;
        };
        // launch options for screenshots: --tab clean|disk|apps (or 0|1|2), --root <folder>, --map
        string[] argv = Environment.GetCommandLineArgs();
        string Arg(string name)
        {
            int i = Array.IndexOf(argv, name);
            return i >= 0 && i + 1 < argv.Length ? argv[i + 1] : null;
        }
        disk.StartWith(Arg("--root"), argv.Contains("--map"));
        string tab = Arg("--tab");
        nav.SelectedItem = tab is "disk" or "1" ? diskItem : tab is "apps" or "2" ? appsItem : cleanItem;

        Root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(nav, 1);
        Root.Children.Add(header);
        Root.Children.Add(nav);

        UpdateFree();
        Root.Loaded += async (_, _) => await CheckUpdatesAsync(false);
    }

    private readonly Button updateBtn = new() { VerticalAlignment = VerticalAlignment.Center };
    private ReleaseInfo pendingUpdate;

    private async Task CheckUpdatesAsync(bool manual)
    {
        ReleaseInfo r = null;
        string error = null;
        try { r = await Task.Run(Updater.Latest); }
        catch (Exception ex) { error = ex.Message; }
        if (r != null && Updater.IsNewer(r.Version, Updater.CurrentVersion))
        {
            pendingUpdate = r;
            updateBtn.Content = "Update to " + r.Version;
            updateBtn.Style = Ui.Sty("AccentButtonStyle");
            if (manual) await OfferUpdateAsync();
        }
        else if (manual)
        {
            await Ui.Message(Root.XamlRoot, "OpenCleaner", error != null ? "Could not check for updates: " + error
                : $"OpenCleaner {Updater.CurrentVersion} is up to date.");
        }
    }

    /// <summary>Downloads this machine's installer, checks it, runs it silently (it upgrades in place) and exits;
    /// the installer starts the new version.</summary>
    private async Task OfferUpdateAsync()
    {
        var r = pendingUpdate;
        string notes = r.Notes.Length > 600 ? r.Notes[..600] + "..." : r.Notes;
        if (!await Ui.Confirm(Root.XamlRoot, $"Update to OpenCleaner {r.Version}?",
                $"You have {Updater.CurrentVersion}. The installer is downloaded from GitHub, checked against the release "
                + "checksums, and run; OpenCleaner restarts when it is done.\n\n" + notes + "\n\nRelease page: " + r.Page,
                "Update and restart")) return;
        string arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
            System.Runtime.InteropServices.Architecture.X86 => "x86",
            _ => "x64",
        };
        updateBtn.Content = "Updating...";
        updateBtn.IsEnabled = false;
        try
        {
            string file = await Task.Run(() => Updater.Download(r, "-Setup-" + arch + ".exe"));
            Process.Start(new ProcessStartInfo(file, "/SILENT /SP- /NOCANCEL /NORESTART") { UseShellExecute = true });
            Application.Current.Exit();
        }
        catch (Exception ex)
        {
            updateBtn.Content = "Update to " + r.Version;
            updateBtn.IsEnabled = true;
            await Ui.Message(Root.XamlRoot, "Update failed", ex.Message + "\n\n" + r.Page);
        }
    }

    private void UpdateFree()
    {
        try
        {
            var d = new DriveInfo(Util.SystemDrive());
            double used = 100.0 * (d.TotalSize - d.AvailableFreeSpace) / d.TotalSize;
            freeText.Text = $"{Util.SystemDrive()}  {Util.Fmt(d.AvailableFreeSpace)} free  |  {used:0}% used";
        }
        catch { }
    }
}
