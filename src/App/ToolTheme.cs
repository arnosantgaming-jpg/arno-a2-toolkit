using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace ArnoA2.App;

/// <summary>The colours of a tool's maps. Spot marks the ranked farming spots.</summary>
public sealed record ToolColors(Color Accent, Color Bright, Color Window, Color Panel, Color Line, Color Muted, Color Spot);

/// <summary>Each tool's colour, the one its launcher card wears, and the frame its windows share.</summary>
public static class ToolTheme
{
    public static readonly Color Pets = Color.Parse("#2E8CFF"), Meter = Color.Parse("#FF4048"), Resources = Color.Parse("#22C878"), Cubes = Color.Parse("#9A4DFF"), Toolkit = Color.Parse("#6CC0FF");
    public static readonly ToolColors PetColors = new(Pets, Color.Parse("#6CC0FF"), Color.Parse("#060E1C"), Color.Parse("#0A1424"), Color.Parse("#1F3A5C"), Color.Parse("#8FA6C2"), Color.Parse("#FFD54F"));
    public static readonly ToolColors CubeColors = new(Cubes, Color.Parse("#C79BFF"), Color.Parse("#0E0818"), Color.Parse("#160D26"), Color.Parse("#3A2560"), Color.Parse("#A99BC4"), Color.Parse("#C79BFF"));
    public static readonly ToolColors ResourceColors = new(Resources, Color.Parse("#6FE8AE"), Color.Parse("#06140F"), Color.Parse("#0A1E17"), Color.Parse("#1B4A36"), Color.Parse("#8FB5A4"), Color.Parse("#6FE8AE"));

    static readonly Lazy<WindowIcon?> Logo = new(() => File.Exists(Path.Combine(AppContext.BaseDirectory, "icon.png")) ? new WindowIcon(Path.Combine(AppContext.BaseDirectory, "icon.png")) : null);

    /// <summary>The logo, for every window's taskbar entry.</summary>
    public static WindowIcon? AppIcon => Logo.Value;

    /// <summary>Makes the standard controls under <paramref name="element"/> use the tool's colour.</summary>
    public static void Tint(StyledElement element, Color accent)
    {
        var fill = new SolidColorBrush(accent);
        foreach (string key in new[] { "SystemAccentColor", "SystemAccentColorDark1", "SystemAccentColorDark2", "SystemAccentColorDark3", "SystemAccentColorLight1", "SystemAccentColorLight2", "SystemAccentColorLight3" })
            element.Resources[key] = accent;
        foreach (string key in new[] { "SystemControlHighlightAccentBrush", "CheckBoxCheckBackgroundFillChecked", "CheckBoxCheckBackgroundFillCheckedPointerOver", "CheckBoxCheckBackgroundFillCheckedPressed", "CheckBoxCheckBackgroundStrokeChecked", "SliderTrackValueFill", "SliderTrackValueFillPointerOver", "SliderTrackValueFillPressed", "SliderThumbBackground", "SliderThumbBackgroundPointerOver", "SliderThumbBackgroundPressed", "TextControlBorderBrushFocused", "TabItemHeaderSelectedPipeFill", "ComboBoxItemBackgroundSelected" })
            element.Resources[key] = fill;
    }

    /// <summary>A borderless window: a title strip to move it, ✕ to close it, a corner to resize it.</summary>
    public static void Frame(Window window, string label, Color accent, Control content, IBrush background, IBrush line)
    {
        window.WindowDecorations = WindowDecorations.None;
        window.Background = background;
        Tint(window, accent);
        var tint = new SolidColorBrush(accent);
        var close = new Border { Padding = new Thickness(8, 2), Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Child = new TextBlock { Text = "✕", FontSize = 12, Foreground = tint } };
        close.PointerPressed += (_, e) => e.Handled = true;
        close.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.Left)
                window.Close();
        };
        var strip = new DockPanel { Height = 26, Background = Brushes.Transparent };
        strip.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(window).Properties.IsLeftButtonPressed)
                window.BeginMoveDrag(e);
        };
        DockPanel.SetDock(close, Dock.Right);
        strip.Children.Add(close);
        strip.Children.Add(new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeight.SemiBold, LetterSpacing = 1.5, Foreground = tint, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) });

        var grip = new TextBlock { Text = "◢", FontSize = 10, Foreground = tint, Cursor = new Cursor(StandardCursorType.BottomRightCorner), Padding = new Thickness(6, 4, 2, 0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
        grip.PointerPressed += (_, e) =>
        {
            window.BeginResizeDrag(WindowEdge.SouthEast, e);
            e.Handled = true;
        };

        DockPanel.SetDock(strip, Dock.Top);
        var layout = new DockPanel { Children = { strip, content } };
        window.Content = new Border { BorderBrush = line, BorderThickness = new Thickness(1), Child = new Panel { Children = { layout, grip } } };
    }
}
