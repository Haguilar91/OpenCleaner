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

        var title = new TextBlock
        {
            Text = "OpenCleaner",
            Style = Ui.Sty("SubtitleTextBlockStyle"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var header = new Grid { Padding = new Thickness(20, 12, 20, 8) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(freeText, 1);
        header.Children.Add(title);
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
        nav.MenuItems.Add(diskItem);
        nav.SelectionChanged += (s, e) =>
        {
            var tag = (e.SelectedItem as NavigationViewItem)?.Tag as string;
            host.Content = tag == "disk" ? disk : clean;
        };
        nav.SelectedItem = cleanItem;

        Root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(nav, 1);
        Root.Children.Add(header);
        Root.Children.Add(nav);

        UpdateFree();
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
