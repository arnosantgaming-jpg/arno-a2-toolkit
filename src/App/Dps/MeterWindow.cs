using System.Globalization;
using ArnoA2.Meter;
using ArnoA2.Protocol;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace ArnoA2.App.Dps;

/// <summary>The always-on-top damage meter: this fight or the whole session, as a table.</summary>
public sealed class MeterWindow : Window
{
    const string Columns = "3,18,2*,46,46,40,*";
    readonly AppState app;
    readonly StackPanel rows = new();
    readonly TextBlock clock = new() { FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = Ui.Title, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock status = new() { FontSize = 9.5, Foreground = Ui.Muted, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(2, 3, 2, 0) };
    readonly Border fight, session, boss, backdrop;
    BreakdownWindow? breakdown;
    bool showSession;
    string rendered = "";

    public MeterWindow(AppState app)
    {
        this.app = app;
        Title = ArnoA2.App.KdeWindowRules.MeterTitle;
        Icon = ToolTheme.AppIcon;
        var saved = app.Meter.Settings;
        Width = saved.MeterWidth ?? 380;
        Height = saved.MeterHeight ?? 210;
        WindowStartupLocation = WindowStartupLocation.Manual;
        MinWidth = 320;
        MinHeight = 110;
        Topmost = true;
        WindowDecorations = WindowDecorations.None;
        // The window itself is see-through; the backdrop below carries the chosen opacity.
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        ToolTheme.Tint(this, ToolTheme.Meter);

        fight = Ui.Flat("Fight", () => Show(false), "This fight, or the last one until the next starts");
        session = Ui.Flat("Session", () => Show(true), "Every fight since the last reset");
        boss = Ui.Flat("Boss", ToggleBoss, "On: only damage to the boss while one is being fought. Off: all damage");
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0 };
        actions.Children.Add(clock);
        actions.Children.Add(fight);
        actions.Children.Add(session);
        actions.Children.Add(boss);
        actions.Children.Add(Ui.Flat("Reset", () =>
        {
            app.Meter.Damage.Reset();
            Refresh();
        }));
        actions.Children.Add(Ui.Flat("✕", Close, "Close"));
        clock.Margin = new Thickness(0, 0, 4, 0);

        // No window frame: the header moves the window, the corner resizes it.
        var header = new DockPanel { Margin = new Thickness(2, 0, 0, 3), Background = Brushes.Transparent };
        header.PointerPressed += (_, e) =>
        {
            if (e.Source == header || e.Source is TextBlock { Parent: DockPanel })
                BeginMoveDrag(e);
        };
        DockPanel.SetDock(actions, Dock.Right);
        header.Children.Add(actions);
        header.Children.Add(new TextBlock { Text = "DPS", FontSize = 12, FontWeight = FontWeight.SemiBold, LetterSpacing = 1.5, Foreground = Ui.Accent, VerticalAlignment = VerticalAlignment.Center });

        var table = new DockPanel();
        var titles = Heading();
        DockPanel.SetDock(titles, Dock.Top);
        table.Children.Add(titles);
        table.Children.Add(new ScrollViewer { Content = rows });

        var layout = new DockPanel { Margin = new Thickness(5) };
        DockPanel.SetDock(header, Dock.Top);
        var grip = new TextBlock { Text = "◢", FontSize = 9, Foreground = Ui.Muted, Cursor = new Cursor(StandardCursorType.BottomRightCorner), Padding = new Thickness(4, 2, 0, 0) };
        grip.PointerPressed += (_, e) => BeginResizeDrag(WindowEdge.SouthEast, e);
        var footer = new DockPanel();
        DockPanel.SetDock(grip, Dock.Right);
        footer.Children.Add(grip);
        footer.Children.Add(status);
        DockPanel.SetDock(footer, Dock.Bottom);
        layout.Children.Add(header);
        layout.Children.Add(footer);
        layout.Children.Add(new Border { Child = table, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), ClipToBounds = true });
        Content = backdrop = new Border { Child = layout, BorderBrush = Ui.Line, BorderThickness = new Thickness(1) };
        ApplyOpacity();

        Opened += (_, _) =>
        {
            Place();
            PositionChanged += (_, e) => app.Meter.ChangeSettings(s => (s.MeterX, s.MeterY) = (e.Point.X, e.Point.Y));
            SizeChanged += (_, e) => app.Meter.ChangeSettings(s => (s.MeterWidth, s.MeterHeight) = (e.NewSize.Width, e.NewSize.Height));
        };
        app.Changed += Refresh;
        Closed += (_, _) =>
        {
            app.Changed -= Refresh;
            breakdown?.Close();
        };
        Refresh();
    }

    // Where it was left, unless that screen is gone; at first, the main screen.
    void Place()
    {
        var saved = app.Meter.Settings;
        if (saved.MeterX is int x && saved.MeterY is int y && Screens.ScreenFromPoint(new PixelPoint(x, y)) != null)
            Position = new PixelPoint(x, y);
        else if (Screens.Primary is { } main)
            Position = new PixelPoint(main.WorkingArea.X + 24, main.WorkingArea.Y + 144);
    }

    void ToggleBoss()
    {
        app.Meter.ChangeSettings(s => s.MeterBossOnly = !s.MeterBossOnly);
        Refresh();
    }

    // Damage to the boss while there is one, whatever else is hit.
    DamageMeter.Tally Shown()
    {
        var whole = showSession ? app.Meter.Damage.Session : app.Meter.Damage.Fight;
        return app.Meter.Settings.MeterBossOnly && whole.HasBoss ? whole.Bosses : whole;
    }

    void OpenBreakdown(int actor)
    {
        breakdown?.Close();
        breakdown = new BreakdownWindow(app, () => app.Meter.Snapshot(Shown(), false).Players.FirstOrDefault(p => p.Actor == actor), Shown);
        breakdown.Closed += (_, _) => breakdown = null;
        breakdown.Show();
    }

    public void ApplyOpacity() => backdrop.Background = new SolidColorBrush(Ui.Backdrop, app.Meter.Settings.MeterOpacity / 100.0);

    void Show(bool wholeSession)
    {
        showSession = wholeSession;
        Refresh();
    }

    public void Refresh()
    {
        var tally = Shown();
        var list = app.Meter.Rows(tally);
        string target = app.Meter.TargetName(tally);
        Ui.Select(fight, !showSession);
        Ui.Select(session, showSession);
        Ui.Select(boss, app.Meter.Settings.MeterBossOnly);

        string idle = Idle();
        string key = $"{showSession}|{target}|{tally.Seconds:F1}|{tally.Incomplete}|{idle}|" + string.Join("|", list.Select(r => $"{r.Actor}:{r.You}:{r.Damage}:{r.Name}:{r.Class}:{r.CombatPower}"));
        if (key == rendered)
            return;
        rendered = key;
        clock.Text = Clock(tally.Seconds);
        status.Text = tally.Empty ? idle : $"{(target.Length > 0 ? target + " · " : "")}{Short(tally.Damage)} damage{(tally.Incomplete ? " · incomplete (data lost)" : "")}";
        rows.Children.Clear();
        // Your row stays on top and keeps your real rank.
        foreach (var (row, rank) in list.Select((row, i) => (row, i + 1)).OrderByDescending(r => r.row.You))
            rows.Children.Add(Row(rank, row));
    }

    string Idle() => app.CaptureProblem || app.Meter.Connection == Connection.Waiting ? app.CaptureStatus : "Damage appears here when a fight starts.";

    static Grid Heading()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(Columns), Height = 18 };
        foreach (var (text, column, right) in new[] { ("#", 1, false), ("PLAYER", 2, false), ("DPS", 3, true), ("TOTAL", 4, true), ("%", 5, true) })
            grid.Children.Add(Place(new TextBlock { Text = text, FontSize = 9, LetterSpacing = 0.5, Foreground = Ui.Muted, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = right ? HorizontalAlignment.Right : column == 1 ? HorizontalAlignment.Center : HorizontalAlignment.Left }, column));
        return grid;
    }

    Control Row(int rank, MeterRow row)
    {
        var color = Ui.ClassColor(row.Class);
        var tint = new SolidColorBrush(color);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(Columns), Height = 22, Background = new SolidColorBrush(color, row.You ? 0.16 : 0.07) };
        grid.Children.Add(Place(new Border { Background = row.You ? Ui.Accent : tint }, 0));
        grid.Children.Add(Place(new TextBlock { Text = rank.ToString(CultureInfo.InvariantCulture), FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = tint, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }, 1));
        grid.Children.Add(Place(Cell(Ui.Logo(row.Class, 18), new TextBlock { Text = row.Name ?? (row.You ? "You" : "Player " + row.Actor), FontSize = 11, FontWeight = row.You ? FontWeight.SemiBold : FontWeight.Normal, Foreground = Ui.Title, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center }, Ui.CombatPower(row.CombatPower)), 2));
        grid.Children.Add(Place(Number(Short(row.Dps), 12, FontWeight.Bold, tint), 3));
        grid.Children.Add(Place(Number(Short(row.Damage), 10.5, FontWeight.Normal, Ui.Title), 4));
        grid.Children.Add(Place(Number(row.Share.ToString("P0", CultureInfo.InvariantCulture), 10.5, FontWeight.Normal, Ui.Title), 5));

        var fill = new Grid { ColumnDefinitions = new ColumnDefinitions($"{Math.Max(row.Share, 0.001).ToString(CultureInfo.InvariantCulture)}*,{Math.Max(1 - row.Share, 0).ToString(CultureInfo.InvariantCulture)}*") };
        fill.Children.Add(new Border { Background = Ui.Bar(color) });
        grid.Children.Add(Place(new Border { Background = Ui.Track, Height = 7, Margin = new Thickness(4, 0, 6, 0), Child = fill }, 6));
        ToolTip.SetTip(grid, $"{(row.Class is { } job ? PlayerClasses.Name(job) + " · " : "")}{row.Damage:N0} damage · {row.Hits:N0} hits · biggest {row.Biggest:N0}{(row.You ? "" : " · right-click if this is you")} · click for skills");
        var mine = new MenuItem { Header = "This is me" };
        mine.Click += (_, _) =>
        {
            app.Meter.Claim(row.Actor);
            Refresh();
        };
        grid.ContextMenu = new ContextMenu { ItemsSource = new[] { mine } };
        grid.Cursor = new Cursor(StandardCursorType.Hand);
        grid.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.Left)
                OpenBreakdown(row.Actor);
        };
        return new Border { Child = grid, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 0, 1) };
    }

    static Control Place(Control control, int column)
    {
        Grid.SetColumn(control, column);
        return control;
    }

    static StackPanel Cell(Control logo, TextBlock text, TextBlock power)
    {
        var cell = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        cell.Children.Add(logo);
        cell.Children.Add(text);
        cell.Children.Add(power);
        return cell;
    }

    static TextBlock Number(string text, double size, FontWeight weight, IBrush brush) => new() { Text = text, FontSize = size, FontWeight = weight, Foreground = brush, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };

    public static string Clock(double seconds) => $"{(int)seconds / 3600:00}:{(int)seconds / 60 % 60:00}:{(int)seconds % 60:00}";

    public static string Short(double value) => value switch
    {
        >= 1_000_000 => (value / 1_000_000).ToString("0.0", CultureInfo.InvariantCulture) + "M",
        >= 1_000 => (value / 1_000).ToString("0.0", CultureInfo.InvariantCulture) + "K",
        _ => value.ToString("N0", CultureInfo.InvariantCulture)
    };
}
