using System.Globalization;
using ArnoA2.Meter;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace ArnoA2.App;

/// <summary>How the session window looks, remembered between starts.</summary>
public sealed class SessionLook
{
    public static readonly string[] Sizes = { "Small", "Medium", "Large" };
    /// <summary>Currencies not shown, by name.</summary>
    public List<string> Hidden { get; set; } = new();
    public bool Rates { get; set; } = true;
    public string Size { get; set; } = "Medium";
    /// <summary>Currencies side by side before the next row starts: 1 is a list, 4 one line.</summary>
    public int Columns { get; set; } = 1;

    /// <summary>Back to how it first looks.</summary>
    public void Reset() => (Hidden, Rates, Size, Columns) = (new(), true, "Medium", 1);
}

/// <summary>What you have earned this session: Kinah, bound Kinah, Abyss Points and Rank
/// Points, in all and per hour, each in its own colour, in as many columns as you like. Drag it
/// anywhere; right-click to choose what it shows and how, or to start a new session.</summary>
public sealed class SessionWindow : Window
{
    static readonly IBrush Soft = Navy.OverlayText2, Loss = Brush.Parse("#FF5A60");
    /// <summary>Each currency's colour, after the game's own icons.</summary>
    public static readonly Dictionary<string, string> Colors = new()
    {
        ["Kinah"] = "#E8B64C", ["Bound Kinah"] = "#C9CED6", ["Abyss Points"] = "#4F9BEA", ["Rank Points"] = "#45D6E0"
    };
    readonly AppState app;
    readonly SessionLook look;
    readonly Action save;
    readonly DispatcherTimer tick = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly TextBlock time = new() { Foreground = Soft, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock title = new() { Text = "SESSION", FontWeight = FontWeight.SemiBold, LetterSpacing = 1, Foreground = Brush.Parse("#E8B64C") };
    readonly StackPanel stack = new() { Spacing = 3 };
    readonly List<(uint Item, IBrush Color, TextBlock Total, TextBlock Rate)> rows = new();

    public SessionWindow(AppState app, SessionLook look, Action save, int? x, int? y, Action<int, int> moved)
    {
        this.app = app;
        this.look = look;
        this.save = save;
        Title = KdeWindowRules.SessionTitle;
        Icon = ToolTheme.AppIcon;
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        Topmost = true;
        ShowInTaskbar = false;
        WindowDecorations = WindowDecorations.None;
        WindowStartupLocation = WindowStartupLocation.Manual;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;

        stack.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, VerticalAlignment = VerticalAlignment.Center, Children = { title, time } });
        Layout();
        var face = new Border { Padding = new Thickness(11, 6), CornerRadius = new CornerRadius(7), Background = Navy.Overlay, BorderBrush = Navy.OverlayRim, BorderThickness = new Thickness(1), Child = stack, Cursor = new Cursor(StandardCursorType.SizeAll) };
        ToolTip.SetTip(face, "Earned since the session started. Drag to move, right-click for options");
        face.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(face).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        };
        face.ContextMenu = new ContextMenu();
        face.ContextMenu.Opening += (_, _) => face.ContextMenu.ItemsSource = Options();
        Content = face;

        Opened += (_, _) =>
        {
            if (x is int left && y is int top && Screens.ScreenFromPoint(new PixelPoint(left, top)) != null)
                Position = new PixelPoint(left, top);
            else if (Screens.Primary is { } main)
                Position = new PixelPoint(main.WorkingArea.X + 24, main.WorkingArea.Y + 140);
            PositionChanged += (_, e) => moved(e.Point.X, e.Point.Y);
        };
        tick.Tick += (_, _) => Refresh();
        tick.Start();
        Closed += (_, _) => tick.Stop();
        Refresh();
    }

    /// <summary>Text size for a size choice.</summary>
    public static double Scale(string size) => size switch
    {
        "Small" => 0.85,
        "Large" => 1.3,
        _ => 1
    };

    /// <summary>The currencies shown, in order.</summary>
    public static IEnumerable<(uint Item, string Name)> Shown(SessionLook look) => SessionGains.Tracked.Where(t => !look.Hidden.Contains(t.Name)).Select(t => (t.Item, t.Name));

    /// <summary>Where the n-th shown currency goes: its row, and its column of currencies.</summary>
    public static (int Row, int Column) Cell(int index, int columns) => (index / Math.Max(1, columns), index % Math.Max(1, columns));

    /// <summary>Lays the currencies out again after the look changed; the header stays.</summary>
    public void Layout()
    {
        double scale = Scale(look.Size);
        int columns = Math.Clamp(look.Columns, 1, SessionGains.Tracked.Length);
        title.FontSize = time.FontSize = 10.5 * scale;
        while (stack.Children.Count > 1)
            stack.Children.RemoveAt(1);
        rows.Clear();
        // Each column of currencies is a name, its amount and its rate, lined up down the rows.
        var grid = new Grid { RowSpacing = 3 };
        for (int c = 0; c < columns; c++)
            grid.ColumnDefinitions.AddRange(new[] { new ColumnDefinition(c == 0 ? 0 : 22, GridUnitType.Pixel), new(GridLength.Auto), new(14, GridUnitType.Pixel), new(GridLength.Auto), new(look.Rates ? 10 : 0, GridUnitType.Pixel), new(GridLength.Auto) });
        var shown = Shown(look).ToList();
        // One row reads as a strip: the header goes in front instead of on top.
        bool strip = shown.Count <= columns;
        stack.Orientation = strip ? Orientation.Horizontal : Orientation.Vertical;
        stack.Spacing = strip ? 16 : 3;
        grid.VerticalAlignment = VerticalAlignment.Center;
        for (int r = 0; r < (shown.Count + columns - 1) / columns; r++)
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        foreach (var ((item, name), index) in shown.Select((s, i) => (s, i)))
        {
            var (row, column) = Cell(index, columns);
            var color = Brush.Parse(Colors.GetValueOrDefault(name, "#F4F2EA"));
            var label = new TextBlock { Text = name, FontSize = 12.5 * scale, Foreground = color };
            var total = new TextBlock { FontSize = 13 * scale, FontWeight = FontWeight.Bold, Foreground = color, HorizontalAlignment = HorizontalAlignment.Right };
            var rate = new TextBlock { FontSize = 11.5 * scale, Foreground = Soft, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, IsVisible = look.Rates };
            foreach (var (cell, part) in new[] { (label, 1), (total, 3), (rate, 5) })
            {
                Grid.SetRow(cell, row);
                Grid.SetColumn(cell, column * 6 + part);
                grid.Children.Add(cell);
            }

            rows.Add((item, color, total, rate));
        }

        stack.Children.Add(grid);
    }

    void Refresh()
    {
        var now = DateTimeOffset.UtcNow;
        var session = app.Meter.Session;
        time.Text = Elapsed(now - session.Since);
        foreach (var (item, color, total, rate) in rows)
        {
            total.Text = Earned(session.Gained(item));
            total.Foreground = session.Gained(item) < 0 ? Loss : color;
            rate.Text = Rate(session.PerHour(item, now));
        }
    }

    /// <summary>Opens Session Gains' settings in the toolkit.</summary>
    public Action? ShowSettings { get; set; }

    List<MenuItem> Options() => OverlayMenu.Items(ShowSettings, Close, ("Reset session", () =>
    {
        app.Meter.Session.Restart(DateTimeOffset.UtcNow);
        Refresh();
    }));

    /// <summary>Redraws after a setting changed.</summary>
    public void Restyle()
    {
        Layout();
        Refresh();
    }

    /// <summary>"+1,470", "−96" for a net loss, or "0" before anything was earned.</summary>
    public static string Earned(long amount) => amount switch
    {
        > 0 => "+" + amount.ToString("N0", CultureInfo.InvariantCulture),
        < 0 => "−" + (-amount).ToString("N0", CultureInfo.InvariantCulture),
        _ => "0"
    };

    /// <summary>"2.1k/h", "850/h", or a dash in the first minute.</summary>
    public static string Rate(double? perHour) => perHour switch
    {
        null => "—",
        >= 10_000 => (perHour.Value / 1000).ToString("0", CultureInfo.InvariantCulture) + "k/h",
        >= 1_000 => (perHour.Value / 1000).ToString("0.0", CultureInfo.InvariantCulture) + "k/h",
        _ => perHour.Value.ToString("0", CultureInfo.InvariantCulture) + "/h"
    };

    /// <summary>"0:42" or "1:05:09".</summary>
    public static string Elapsed(TimeSpan span) => span.TotalHours >= 1 ? $"{(int)span.TotalHours}:{span:mm\\:ss}" : $"{(int)span.TotalMinutes}:{span:ss}";
}
