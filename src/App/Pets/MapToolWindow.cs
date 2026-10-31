using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace ArnoA2.App;

/// <summary>A map tool's map look, and where its overlay window sits.</summary>
public class ToolWindowLook : MapLook
{
    public int Opacity { get; set; } = 70;
    public int? X { get; set; }
    public int? Y { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }
}

/// <summary>
/// A map tool's always-on-top window, drawn like the meter: a table with a status line. While
/// it's open the tool follows your position and has its ring on the shared mini-map.
/// </summary>
public abstract class MapToolWindow : Window
{
    readonly MapLayer layer;
    readonly ToolWindowLook look;
    readonly int startY;
    readonly Action save;
    readonly StackPanel actions = new() { Orientation = Orientation.Horizontal };
    readonly Border backdrop = new() { BorderThickness = new Thickness(1) };
    FullMapWindow? fullMap;

    protected readonly IBrush Line, Muted, Bright;
    protected readonly StackPanel Rows = new();
    protected readonly TextBlock Status = new() { FontSize = 9.5, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(2, 3, 2, 0) };

    /// <param name="startY">Pixels below the screen's top on first start, clear of the other tools.</param>
    protected MapToolWindow(MapLayer layer, ToolWindowLook look, Action save, string title, string label, string columns, (string Text, int Column, bool Right)[] titles, double width, double height, int startY)
    {
        (this.layer, this.look, this.save, this.startY) = (layer, look, save, startY);
        var colors = layer.Colors;
        (Line, Muted, Bright) = (new SolidColorBrush(colors.Line), new SolidColorBrush(colors.Muted), new SolidColorBrush(colors.Bright));
        Title = title;
        Icon = ToolTheme.AppIcon;
        Width = look.Width ?? width;
        Height = look.Height ?? height;
        WindowStartupLocation = WindowStartupLocation.Manual;
        MinWidth = 260;
        MinHeight = 120;
        Topmost = true;
        WindowDecorations = WindowDecorations.None;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        ToolTheme.Tint(this, colors.Accent);

        Status.Foreground = Muted;
        backdrop.BorderBrush = Line;
        actions.Children.Add(Dps.Ui.Flat("✕", Close, "Close", Line, Muted));
        var header = new DockPanel { Margin = new Thickness(2, 0, 0, 3), Background = Brushes.Transparent };
        header.PointerPressed += (_, e) =>
        {
            if (e.Source == header || e.Source is TextBlock { Parent: DockPanel })
                BeginMoveDrag(e);
        };
        DockPanel.SetDock(actions, Dock.Right);
        header.Children.Add(actions);
        header.Children.Add(new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeight.SemiBold, LetterSpacing = 1.5, Foreground = Bright, VerticalAlignment = VerticalAlignment.Center });

        var headings = new Grid { ColumnDefinitions = new ColumnDefinitions(columns), Height = 18 };
        foreach (var (text, column, right) in titles)
            headings.Children.Add(Column(new TextBlock { Text = text, FontSize = 9, LetterSpacing = 0.5, Foreground = Muted, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left, Margin = new Thickness(right ? 0 : 4, 0, 4, 0) }, column));
        var table = new DockPanel();
        DockPanel.SetDock(headings, Dock.Top);
        table.Children.Add(headings);
        table.Children.Add(new ScrollViewer { Content = Rows });

        var grip = new TextBlock { Text = "◢", FontSize = 9, Foreground = Muted, Cursor = new Cursor(StandardCursorType.BottomRightCorner), Padding = new Thickness(4, 2, 0, 0) };
        grip.PointerPressed += (_, e) => BeginResizeDrag(WindowEdge.SouthEast, e);
        var footer = new DockPanel();
        DockPanel.SetDock(grip, Dock.Right);
        footer.Children.Add(grip);
        footer.Children.Add(Status);

        var layout = new DockPanel { Margin = new Thickness(5) };
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        layout.Children.Add(header);
        layout.Children.Add(footer);
        layout.Children.Add(new Border { Child = table, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), ClipToBounds = true });
        backdrop.ClipToBounds = true;
        backdrop.Child = layout;
        Content = backdrop;
        ApplyOpacity();

        layer.Changed += Update;
        Opened += (_, _) =>
        {
            Place();
            PositionChanged += (_, e) => (look.X, look.Y) = (e.Point.X, e.Point.Y);
            SizeChanged += (_, e) => (look.Width, look.Height) = (e.NewSize.Width, e.NewSize.Height);
            layer.Hub.Attach();
            Update();
        };
        Closed += (_, _) =>
        {
            layer.Changed -= Update;
            layer.Hub.Detach();
            save();
            layer.Hub.Mini.Leave(layer);
            fullMap?.Close();
        };
    }

    public MiniMapWindow? MiniMap => layer.Hub.Mini.Window;
    public FullMapWindow? FullMap => fullMap;

    /// <summary>Redraws the table from the tool's current state.</summary>
    protected abstract void Refresh();

    /// <summary>Adds a button to the title strip, left of ✕.</summary>
    protected void AddAction(Control action) => actions.Children.Insert(actions.Children.Count - 1, action);

    void Update()
    {
        Status.Text = layer.Hub.Error ?? layer.Guidance();
        ToolTip.SetTip(Status, Status.Text);
        Refresh();
    }

    // Where it was left, unless that screen is gone.
    void Place()
    {
        if (look.X is int x && look.Y is int y && Screens.ScreenFromPoint(new PixelPoint(x, y)) != null)
            Position = new PixelPoint(x, y);
        else if (Screens.Primary is { } main)
            Position = new PixelPoint(main.WorkingArea.X + 24, main.WorkingArea.Y + startY);
    }

    public void ApplyOpacity() => backdrop.Background = new SolidColorBrush(layer.Colors.Window, look.Opacity / 100.0);

    public void ToggleMiniMap() => layer.Hub.Mini.Toggle(layer);

    public void OpenFullMap()
    {
        if (fullMap == null)
        {
            fullMap = new FullMapWindow(layer);
            fullMap.Closed += (_, _) => fullMap = null;
            fullMap.Show();
        }

        fullMap.Activate();
    }

    protected static Control Column(Control control, int column)
    {
        Grid.SetColumn(control, column);
        return control;
    }

    protected static TextBlock Number(string text, double size, FontWeight weight, IBrush brush) => new() { Text = text, FontSize = size, FontWeight = weight, Foreground = brush, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };

    /// <summary>The "Look" section of a tool's settings drawer.</summary>
    public static void LookSettings(StackPanel panel, ToolWindowLook look, Action save, Func<MapToolWindow?> window)
    {
        panel.Children.Add(Ui.Heading("Look"));
        var label = new TextBlock { Text = $"{look.Opacity} %", Width = 50, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var opacity = new Slider { Minimum = 20, Maximum = 100, Value = look.Opacity };
        opacity.ValueChanged += (_, e) =>
        {
            look.Opacity = (int)Math.Round(e.NewValue);
            label.Text = $"{look.Opacity} %";
            save();
            window()?.ApplyOpacity();
        };
        var row = new DockPanel();
        DockPanel.SetDock(label, Dock.Right);
        row.Children.Add(label);
        row.Children.Add(opacity);
        panel.Children.Add(new TextBlock { Text = "Background opacity" });
        panel.Children.Add(row);
    }
}
