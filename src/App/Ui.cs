using ArnoA2.Protocol;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ArnoA2.App;

public static class Ui
{
    public static readonly IBrush Window = Brush.Parse("#0B1118");
    public static readonly IBrush Accent = Brush.Parse("#4FB6FF");
    public static readonly IBrush Title = Brush.Parse("#E1EBFF");
    public static readonly IBrush Rate = Brush.Parse("#CFE9FF");
    public static readonly IBrush Muted = Brush.Parse("#8A9AAE");
    public static readonly IBrush Track = Brush.Parse("#2E3A48");
    /// <summary>A row whose class is not known yet.</summary>
    public static readonly Color NoClass = Color.Parse("#5A47B0");

    /// <summary>The class colours of A2Tools' AION2 themes.</summary>
    public static Color ClassColor(PlayerClass? job) => job switch
    {
        PlayerClass.Gladiator => Color.Parse("#4FD1C5"),
        PlayerClass.Templar => Color.Parse("#5F8CFF"),
        PlayerClass.Assassin => Color.Parse("#7BE35A"),
        PlayerClass.Ranger => Color.Parse("#41D98A"),
        PlayerClass.Sorcerer => Color.Parse("#9A6BFF"),
        PlayerClass.Spiritmaster => Color.Parse("#E06BFF"),
        PlayerClass.Cleric => Color.Parse("#F2C15A"),
        PlayerClass.Chanter => Color.Parse("#FF9A3D"),
        PlayerClass.Fighter => Color.Parse("#E85D5D"),
        _ => NoClass
    };

    /// <summary>A bar drawn stronger towards its tip, as those themes do.</summary>
    public static IBrush Bar(Color color) => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Color.FromArgb(140, color.R, color.G, color.B), 0), new GradientStop(Color.FromArgb(224, color.R, color.G, color.B), 1) }
    };

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
