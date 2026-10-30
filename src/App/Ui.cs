using ArnoA2.Protocol;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace ArnoA2.App;

public static class Ui
{
    public static readonly IBrush Window = Brush.Parse("#070B12");
    public static readonly IBrush Panel = Brush.Parse("#0C1420");
    public static readonly IBrush Line = Brush.Parse("#1E3A5F");
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

    static readonly Dictionary<PlayerClass, Bitmap?> logos = new();

    /// <summary>The class logo from classes/&lt;class&gt;.png, or a coloured disc with the
    /// class's initial when there is no such file.</summary>
    public static Control Logo(PlayerClass? job, double size)
    {
        if (job is { } known)
        {
            if (!logos.TryGetValue(known, out var bitmap))
            {
                string path = Path.Combine(AppContext.BaseDirectory, "classes", known.ToString().ToLowerInvariant() + ".png");
                logos[known] = bitmap = File.Exists(path) ? new Bitmap(path) : null;
            }

            if (bitmap != null)
                return new Image { Source = bitmap, Width = size, Height = size };
        }

        var color = ClassColor(job);
        return new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size / 2),
            BorderThickness = new Thickness(1.5),
            BorderBrush = new SolidColorBrush(color),
            Background = new SolidColorBrush(color, 0.15),
            Child = new TextBlock { Text = job is { } named ? named.ToString()[..1] : "?", FontSize = size * 0.5, FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(color), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        };
    }

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

    /// <summary>A button that is only its label until the pointer is over it.</summary>
    public static Border Flat(string label, Action action, string? tip = null)
    {
        var flat = new Border { Padding = new Thickness(10, 4), CornerRadius = new CornerRadius(4), Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = label, FontSize = 13, Foreground = Muted } };
        if (tip != null)
            ToolTip.SetTip(flat, tip);
        flat.PointerEntered += (_, _) => flat.Background = Line;
        flat.PointerExited += (_, _) => flat.Background = Brushes.Transparent;
        flat.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.Left)
                action();
        };
        return flat;
    }

    /// <summary>Marks a flat button as the current choice.</summary>
    public static void Select(Border flat, bool selected)
    {
        var label = (TextBlock)flat.Child!;
        label.Foreground = selected ? Accent : Muted;
        label.FontWeight = selected ? FontWeight.SemiBold : FontWeight.Normal;
    }

    public static TextBlock Heading(string text, double size = 18) => new() { Text = text, FontSize = size, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 6, 0, 2) };
    public static TextBlock Note(string text) => new() { Text = text, FontSize = 11.5, Foreground = Muted, TextWrapping = TextWrapping.Wrap };
}
