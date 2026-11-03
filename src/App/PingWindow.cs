using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace ArnoA2.App;

/// <summary>The always-on-top ping readout. Drag it anywhere; the hub's switch closes it.</summary>
public sealed class PingWindow : Window
{
    static readonly IBrush Good = Brush.Parse("#35D986"), Fair = Brush.Parse("#F2B632"), Bad = Brush.Parse("#FF5A60"), Unknown = Brush.Parse("#AEBBCF");
    readonly AppState app;
    readonly TextBlock value = new() { FontSize = 15, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center };

    public PingWindow(AppState app, int? x, int? y, Action<int, int> moved)
    {
        this.app = app;
        Title = KdeWindowRules.PingTitle;
        Icon = ToolTheme.AppIcon;
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        Topmost = true;
        ShowInTaskbar = false;
        WindowDecorations = WindowDecorations.None;
        WindowStartupLocation = WindowStartupLocation.Manual;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        row.Children.Add(new TextBlock { Text = "PING", FontSize = 10.5, FontWeight = FontWeight.SemiBold, LetterSpacing = 1, Foreground = Unknown, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(value);
        var face = new Border { Padding = new Thickness(10, 4), MinWidth = 104, CornerRadius = new CornerRadius(5), Background = new SolidColorBrush(Color.Parse("#05070D"), 0.72), Child = row, Cursor = new Cursor(StandardCursorType.SizeAll) };
        ToolTip.SetTip(face, "The game's own ping, updated every 10 seconds. Drag to move");
        face.PointerPressed += (_, e) => BeginMoveDrag(e);
        Content = face;

        Opened += (_, _) =>
        {
            if (x is int left && y is int top && Screens.ScreenFromPoint(new PixelPoint(left, top)) != null)
                Position = new PixelPoint(left, top);
            else if (Screens.Primary is { } main)
                Position = new PixelPoint(main.WorkingArea.X + 24, main.WorkingArea.Y + 96);
            PositionChanged += (_, e) => moved(e.Point.X, e.Point.Y);
        };
        app.Changed += Refresh;
        Closed += (_, _) => app.Changed -= Refresh;
        Refresh();
    }

    void Refresh()
    {
        value.Text = Reading(app.Ping);
        value.Foreground = Tint(app.Ping);
    }

    public static string Reading(int? milliseconds) => milliseconds is int ms ? ms.ToString(CultureInfo.InvariantCulture) + " ms" : "—";

    public static IBrush Tint(int? milliseconds) => milliseconds switch
    {
        null => Unknown,
        < 80 => Good,
        < 150 => Fair,
        _ => Bad
    };
}
