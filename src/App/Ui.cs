using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ArnoA2.App;

public static class Ui
{
    public static readonly IBrush Window = Brush.Parse("#0B1118");
    public static readonly IBrush Gold = Brush.Parse("#C9A227");
    public static readonly IBrush Bright = Brush.Parse("#FFD54F");
    public static readonly IBrush Muted = Brush.Parse("#8A9AAE");
    public static readonly IBrush Track = Brush.Parse("#2E3A48");

    public static Button Button(string label, Action action, string? tip = null)
    {
        var button = new Button { Content = label, Padding = new Thickness(12, 5), CornerRadius = new CornerRadius(6) };
        if (tip != null)
            ToolTip.SetTip(button, tip);
        button.Click += (_, _) => action();
        return button;
    }

    public static TextBlock Heading(string text, double size = 18) => new() { Text = text, FontSize = size, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 6, 0, 2) };
    public static TextBlock Note(string text) => new() { Text = text, FontSize = 11.5, Foreground = Muted, TextWrapping = TextWrapping.Wrap };
}
