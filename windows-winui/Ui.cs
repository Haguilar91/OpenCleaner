using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace OpenCleaner;

/// <summary>Small helpers for theme resources and dialogs.</summary>
static class Ui
{
    public static Brush Res(string key) => (Brush)Application.Current.Resources[key];

    public static Style Sty(string key) => (Style)Application.Current.Resources[key];

    public static Brush Green => new SolidColorBrush(ColorHelper.FromArgb(255, 30, 140, 80));

    public static Brush Red => new SolidColorBrush(ColorHelper.FromArgb(255, 200, 50, 50));

    public static Border Card(UIElement child) => new Border
    {
        Background = Res("CardBackgroundFillColorDefaultBrush"),
        BorderBrush = Res("CardStrokeColorDefaultBrush"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(12, 8, 14, 8),
        Child = child,
    };

    static ScrollViewer Body(string text) => new ScrollViewer
    {
        MaxHeight = 320,
        Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true },
    };

    public static async Task Message(XamlRoot root, string title, string text)
    {
        var dlg = new ContentDialog { Title = title, Content = Body(text), CloseButtonText = "OK", XamlRoot = root };
        await dlg.ShowAsync();
    }

    public static async Task<bool> Confirm(XamlRoot root, string title, string text, string yes)
    {
        var dlg = new ContentDialog
        {
            Title = title,
            Content = Body(text),
            PrimaryButtonText = yes,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = root,
        };
        return await dlg.ShowAsync() == ContentDialogResult.Primary;
    }
}
