using System.Globalization;
using ArnoA2.Meter;
using ArnoA2.Protocol;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace ArnoA2.App;

/// <summary>The always-on-top damage meter: this fight or the whole session, as a table.</summary>
public sealed class MeterWindow : Window
{
    const string Columns = "5,34,2*,86,86,62,1.4*,50";
    readonly AppState app;
    readonly StackPanel rows = new();
    readonly TextBlock clock = new() { FontSize = 15, FontWeight = FontWeight.SemiBold, Foreground = Ui.Title, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock status = new() { FontSize = 11.5, Foreground = Ui.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 6, 2, 0) };
    readonly Border fight, session;
    SettingsWindow? settings;
    bool showSession;
    string rendered = "";

    public MeterWindow(AppState app)
    {
        this.app = app;
        Title = KdeWindowRules.MeterTitle;
        Width = 760;
        Height = 420;
        MinWidth = 600;
        MinHeight = 200;
        Topmost = true;
        WindowDecorations = WindowDecorations.None;
        Background = Ui.Window;

        fight = Ui.Flat("Fight", () => Show(false), "This fight, or the last one until the next starts");
        session = Ui.Flat("Session", () => Show(true), "Every fight since the last reset");
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        actions.Children.Add(clock);
        actions.Children.Add(fight);
        actions.Children.Add(session);
        actions.Children.Add(Ui.Flat("Reset", () =>
        {
            app.Engine.Damage.Reset();
            Refresh();
        }));
        actions.Children.Add(Ui.Flat("⚙", OpenSettings, "Settings"));
        actions.Children.Add(Ui.Flat("✕", Close, "Close"));
        clock.Margin = new Thickness(0, 0, 10, 0);

        // No window frame: the header moves the window, the corner resizes it.
        var header = new DockPanel { Margin = new Thickness(4, 0, 0, 8), Background = Brushes.Transparent };
        header.PointerPressed += (_, e) =>
        {
            if (e.Source == header || e.Source is TextBlock { Parent: DockPanel })
                BeginMoveDrag(e);
        };
        DockPanel.SetDock(actions, Dock.Right);
        header.Children.Add(actions);
        header.Children.Add(new TextBlock { Text = "DPS METER", FontSize = 18, FontWeight = FontWeight.SemiBold, LetterSpacing = 3, Foreground = Ui.Accent, VerticalAlignment = VerticalAlignment.Center });

        var table = new DockPanel();
        var titles = Heading();
        DockPanel.SetDock(titles, Dock.Top);
        table.Children.Add(titles);
        table.Children.Add(new ScrollViewer { Content = rows });

        var layout = new DockPanel { Margin = new Thickness(10) };
        DockPanel.SetDock(header, Dock.Top);
        var grip = new TextBlock { Text = "◢", FontSize = 11, Foreground = Ui.Muted, Cursor = new Cursor(StandardCursorType.BottomRightCorner), Padding = new Thickness(6, 4, 0, 0) };
        grip.PointerPressed += (_, e) => BeginResizeDrag(WindowEdge.SouthEast, e);
        var footer = new DockPanel();
        DockPanel.SetDock(grip, Dock.Right);
        footer.Children.Add(grip);
        footer.Children.Add(status);
        DockPanel.SetDock(footer, Dock.Bottom);
        layout.Children.Add(header);
        layout.Children.Add(footer);
        layout.Children.Add(new Border { Child = table, Background = Ui.Panel, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), ClipToBounds = true });
        Content = new Border { Child = layout, BorderBrush = Ui.Line, BorderThickness = new Thickness(1) };

        app.Changed += Refresh;
        Closed += (_, _) =>
        {
            app.Changed -= Refresh;
            settings?.Close();
        };
        Refresh();
    }

    void OpenSettings()
    {
        if (settings == null)
        {
            settings = new SettingsWindow(app);
            settings.Closed += (_, _) => settings = null;
            settings.Show();
        }

        settings.Activate();
    }

    void Show(bool wholeSession)
    {
        showSession = wholeSession;
        Refresh();
    }

    public void Refresh()
    {
        var meter = app.Engine.Damage;
        var tally = showSession ? meter.Session : meter.Fight;
        var list = app.Engine.Rows(tally);
        Ui.Select(fight, !showSession);
        Ui.Select(session, showSession);

        string idle = Idle();
        string key = $"{showSession}|{tally.Seconds:F1}|{tally.Incomplete}|{idle}|" + string.Join("|", list.Select(r => $"{r.Actor}:{r.You}:{r.Damage}:{r.Name}:{r.Class}"));
        if (key == rendered)
            return;
        rendered = key;
        clock.Text = Clock(tally.Seconds);
        status.Text = tally.Empty ? idle : $"{Short(tally.Damage)} damage{(tally.Incomplete ? " · incomplete (data lost)" : "")}";
        rows.Children.Clear();
        for (int i = 0; i < list.Count; i++)
            rows.Children.Add(Row(i + 1, list[i]));
    }

    string Idle() => app.CaptureProblem || app.Engine.Connection == Connection.Waiting ? app.CaptureStatus : "Damage appears here when a fight starts.";

    static Grid Heading()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(Columns), Height = 30, Background = Ui.Window };
        foreach (var (text, column, right) in new[] { ("#", 1, false), ("PLAYER", 2, false), ("DPS", 3, true), ("TOTAL", 4, true), ("%", 5, true) })
            grid.Children.Add(Place(new TextBlock { Text = text, FontSize = 11, LetterSpacing = 1, Foreground = Ui.Muted, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = right ? HorizontalAlignment.Right : column == 1 ? HorizontalAlignment.Center : HorizontalAlignment.Left }, column));
        return grid;
    }

    static Control Row(int rank, MeterRow row)
    {
        var color = Ui.ClassColor(row.Class);
        var tint = new SolidColorBrush(color);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(Columns), Height = 38, Background = new SolidColorBrush(color, row.You ? 0.16 : 0.07) };
        grid.Children.Add(Place(new Border { Background = row.You ? Ui.Accent : tint }, 0));
        grid.Children.Add(Place(new TextBlock { Text = rank.ToString(CultureInfo.InvariantCulture), FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = tint, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }, 1));
        grid.Children.Add(Place(Cell(Ui.Logo(row.Class, 24), new TextBlock { Text = row.Name ?? (row.You ? "You" : "Player " + row.Actor), FontSize = 14, FontWeight = row.You ? FontWeight.SemiBold : FontWeight.Normal, Foreground = Ui.Title, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center }), 2));
        grid.Children.Add(Place(Number(Short(row.Dps), 16, FontWeight.Bold, tint), 3));
        grid.Children.Add(Place(Number(Short(row.Damage), 13, FontWeight.Normal, Ui.Title), 4));
        grid.Children.Add(Place(Number(row.Share.ToString("P1", CultureInfo.InvariantCulture), 13, FontWeight.Normal, Ui.Title), 5));

        var fill = new Grid { ColumnDefinitions = new ColumnDefinitions($"{Math.Max(row.Share, 0.001).ToString(CultureInfo.InvariantCulture)}*,{Math.Max(1 - row.Share, 0).ToString(CultureInfo.InvariantCulture)}*") };
        fill.Children.Add(new Border { Background = Ui.Bar(color) });
        grid.Children.Add(Place(new Border { Background = Ui.Track, Height = 12, Margin = new Thickness(14, 0, 8, 0), Child = fill }, 6));
        grid.Children.Add(Place(Number(row.Share.ToString("P0", CultureInfo.InvariantCulture), 11.5, FontWeight.Normal, Ui.Muted), 7));
        ToolTip.SetTip(grid, $"{(row.Class is { } job ? PlayerClasses.Name(job) + " · " : "")}{row.Damage:N0} damage · {row.Hits:N0} hits · biggest {row.Biggest:N0}");
        return new Border { Child = grid, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 0, 1) };
    }

    static Control Place(Control control, int column)
    {
        Grid.SetColumn(control, column);
        return control;
    }

    static StackPanel Cell(Control logo, TextBlock text)
    {
        var cell = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        cell.Children.Add(logo);
        cell.Children.Add(text);
        return cell;
    }

    static TextBlock Number(string text, double size, FontWeight weight, IBrush brush) => new() { Text = text, FontSize = size, FontWeight = weight, Foreground = brush, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };

    static string Clock(double seconds) => $"{(int)seconds / 3600:00}:{(int)seconds / 60 % 60:00}:{(int)seconds % 60:00}";

    static string Short(double value) => value switch
    {
        >= 1_000_000 => (value / 1_000_000).ToString("0.0", CultureInfo.InvariantCulture) + "M",
        >= 1_000 => (value / 1_000).ToString("0.0", CultureInfo.InvariantCulture) + "K",
        _ => value.ToString("N0", CultureInfo.InvariantCulture)
    };
}
