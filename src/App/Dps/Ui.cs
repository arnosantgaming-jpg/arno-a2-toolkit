using ArnoA2.Protocol;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace ArnoA2.App.Dps;

public static class Ui
{
    public static readonly IBrush Window = Brushes.Black;
    public static readonly IBrush Panel = Brush.Parse("#0A0A0A");
    public static readonly IBrush Line = Brush.Parse("#262626");
    public static readonly IBrush Accent = Brush.Parse("#4FB6FF");
    public static readonly IBrush Title = Brush.Parse("#E1EBFF");
    public static readonly IBrush Rate = Brush.Parse("#CFE9FF");
    public static readonly IBrush Muted = Brush.Parse("#8A9AAE");
    public static readonly IBrush Track = Brush.Parse("#2B2B2B");
    /// <summary>A row whose class is not known yet.</summary>
    public static readonly Color NoClass = Color.Parse("#6B7A8C");

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
            {
                var image = new Image { Source = bitmap, Width = size, Height = size };
                // The default filter turns fine art this small into jagged noise.
                RenderOptions.SetBitmapInterpolationMode(image, BitmapInterpolationMode.HighQuality);
                return image;
            }
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
        var flat = new Border { Padding = new Thickness(5, 2), CornerRadius = new CornerRadius(3), Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = label, FontSize = 10.5, Foreground = Muted } };
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

    /// <summary>Combat power, as it follows a name: (155K). Empty until the game sends it.</summary>
    public static TextBlock CombatPower(long? power) => new() { Text = PowerText(power), FontSize = 9.5, Foreground = Muted, VerticalAlignment = VerticalAlignment.Center };

    public static string PowerText(long? power) => power switch
    {
        null => "",
        >= 1_000_000 => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"({power / 1_000_000d:0.#}M)"),
        >= 1_000 => $"({power / 1_000}K)",
        _ => $"({power})"
    };

    /// <summary>A text cell in a table.</summary>
    public static TextBlock Cell(string text, double size, IBrush brush, int column, bool right = false, bool bold = false)
    {
        var cell = new TextBlock { Text = text, FontSize = size, Foreground = brush, FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(4, 0) };
        Grid.SetColumn(cell, column);
        return cell;
    }

    /// <summary>A horizontal share bar.</summary>
    public static Control ShareBar(double share, Color color, int column)
    {
        var fill = new Grid { ColumnDefinitions = new ColumnDefinitions($"{Math.Max(share, 0.001).ToString(System.Globalization.CultureInfo.InvariantCulture)}*,{Math.Max(1 - share, 0).ToString(System.Globalization.CultureInfo.InvariantCulture)}*") };
        fill.Children.Add(new Border { Background = Bar(color) });
        var bar = new Border { Background = Track, Height = 7, Margin = new Thickness(4, 0), Child = fill };
        Grid.SetColumn(bar, column);
        return bar;
    }

    public static TextBlock Heading(string text, double size = 18) => new() { Text = text, FontSize = size, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 6, 0, 2) };
    public static TextBlock Note(string text) => new() { Text = text, FontSize = 11.5, Foreground = Muted, TextWrapping = TextWrapping.Wrap };
}
