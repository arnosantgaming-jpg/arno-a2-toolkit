using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace ArnoA2.App;

/// <summary>The colours of a tool's maps. Spot marks the ranked farming spots.</summary>
public sealed record ToolColors(Color Accent, Color Bright, Color Window, Color Panel, Color Line, Color Muted, Color Spot);

/// <summary>The hub's dark navy palette, shared with the overlays.</summary>
public static class Navy
{
    public static readonly IBrush Background = Brush.Parse("#080D16"), Sidebar = Brush.Parse("#0B111D"), Surface1 = Brush.Parse("#101A29"), Surface2 = Brush.Parse("#152236"), Surface3 = Brush.Parse("#1C2B40"),
        Line = Brush.Parse("#26364C"), LineStrong = Brush.Parse("#3A5270"), Text = Brush.Parse("#E8EEF7"), Text2 = Brush.Parse("#A8B7CA"), Muted = Brush.Parse("#71839A"),
        Accent = Brush.Parse("#339DFF"), Success = Brush.Parse("#29D391"), Warning = Brush.Parse("#F2B94B"), Danger = Brush.Parse("#F05B65"), Track = Brush.Parse("#25364B");
    public static readonly Color AccentColor = Color.Parse("#339DFF");
    /// <summary>The overlays' face: near-black at 72 %, a faint rim.</summary>
    public static readonly IBrush Overlay = Brush.Parse("#B8080D16"), OverlayRim = Brush.Parse("#663A5270"), OverlayText = Brush.Parse("#F0F4FA"), OverlayText2 = Brush.Parse("#B6C3D3");
}

/// <summary>Each tool's colour, the one its launcher card wears, and the frame its windows share.</summary>
public static class ToolTheme
{
    public static readonly Color Pets = Color.Parse("#339DFF"), Meter = Color.Parse("#FF5968"), Resources = Color.Parse("#29D391"), Cubes = Color.Parse("#B879FF"), Toolkit = Color.Parse("#65BFFF");
    public static readonly ToolColors PetColors = new(Pets, Color.Parse("#65BFFF"), Color.Parse("#080D16"), Color.Parse("#101A29"), Color.Parse("#26364C"), Color.Parse("#8A9BB0"), Color.Parse("#FFD54F"));
    public static readonly ToolColors CubeColors = new(Cubes, Color.Parse("#D2A8FF"), Color.Parse("#080D16"), Color.Parse("#101A29"), Color.Parse("#26364C"), Color.Parse("#8A9BB0"), Color.Parse("#C79BFF"));
    public static readonly ToolColors ResourceColors = new(Resources, Color.Parse("#6FE8AE"), Color.Parse("#080D16"), Color.Parse("#101A29"), Color.Parse("#26364C"), Color.Parse("#8A9BB0"), Color.Parse("#6FE8AE"));

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
        // See-through window, rounded face: the same look as the small overlays.
        window.TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        window.Background = Brushes.Transparent;
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
        window.Content = new Border { Background = background, CornerRadius = new CornerRadius(8), BorderBrush = Navy.OverlayRim, BorderThickness = new Thickness(1), ClipToBounds = true, Child = new Panel { Children = { layout, grip } } };
    }
}
