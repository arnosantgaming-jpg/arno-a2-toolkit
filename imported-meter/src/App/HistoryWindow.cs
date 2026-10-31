using System.Globalization;
using ArnoA2.Meter;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace ArnoA2.App;

/// <summary>Past fights, newest first. A fight opens to its players, a player to their skills.</summary>
public sealed class HistoryWindow : Window
{
    const string FightColumns = "96,2*,44,52,52,16";
    const string PlayerColumns = "22,2*,54,54,44,*";
    readonly AppState app;
    readonly StackPanel rows = new();
    readonly Border bosses, everything;
    readonly TextBlock title = new() { FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = Ui.Title, VerticalAlignment = VerticalAlignment.Center };
    readonly Border back;
    IReadOnlyList<FightRecord> fights;
    bool all, listing;
    int saved;
    BreakdownWindow? breakdown;

    public HistoryWindow(AppState app)
    {
        this.app = app;
        Title = "Arno's DPS Meter · history";
        Width = 520;
        Height = 400;
        MinWidth = 420;
        MinHeight = 200;
        Topmost = true;
        Background = Ui.Window;
        fights = app.Engine.History;

        bosses = Ui.Flat("Bosses", () => Show(false));
        everything = Ui.Flat("All fights", () => Show(true));
        back = Ui.Flat("← Back", () => Show(all));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Children = { bosses, everything, back } };
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(actions, Dock.Right);
        top.Children.Add(actions);
        top.Children.Add(title);
        DockPanel.SetDock(top, Dock.Top);
        Content = new DockPanel { Margin = new Thickness(10), Children = { top, new ScrollViewer { Content = rows } } };
        app.Changed += Follow;
        Closed += (_, _) =>
        {
            app.Changed -= Follow;
            breakdown?.Close();
        };
        Show(false);
    }

    // A fight that just ended appears, unless you are reading another one.
    void Follow()
    {
        if (listing && saved != app.Engine.FightsSaved)
            Show(all);
    }

    string Ended => $"A fight appears here {app.Engine.Settings.MeterQuietSeconds} seconds after the last hit.";

    void Show(bool wholeList)
    {
        (all, listing, saved) = (wholeList, true, app.Engine.FightsSaved);
        fights = app.Engine.History;
        (bosses.IsVisible, everything.IsVisible, back.IsVisible) = (true, true, false);
        Ui.Select(bosses, !all);
        Ui.Select(everything, all);
        var shown = fights.Where(f => all || f.Boss).ToArray();
        title.Text = $"{shown.Length} fights";
        rows.Children.Clear();
        if (shown.Length == 0)
            rows.Children.Add(Ui.Note(all ? Ended : Ended + " A boss is only recognised if the meter was running when it came into view. Everything else is under All fights."));
        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions(FightColumns), Height = 18 };
        foreach (var (text, column, right) in new[] { ("WHEN", 0, false), ("TARGET", 1, false), ("TIME", 2, true), ("TEAM", 3, true), ("YOU", 4, true) })
            heading.Children.Add(Ui.Cell(text, 9, Ui.Muted, column, right));
        if (shown.Length > 0)
            rows.Children.Add(heading);
        foreach (var fight in shown)
            rows.Children.Add(Clickable(FightRow(fight), () => Open(fight)));
    }

    static Grid FightRow(FightRecord fight)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(FightColumns), Height = 24 };
        double seconds = Math.Max(1, fight.Seconds);
        grid.Children.Add(Ui.Cell(fight.Start.ToLocalTime().ToString("MMM d HH:mm", CultureInfo.InvariantCulture), 10.5, Ui.Muted, 0));
        grid.Children.Add(Ui.Cell(fight.Target.Length > 0 ? fight.Target : "Unknown", 11, fight.Boss ? Ui.Title : Ui.Rate, 1, bold: fight.Boss));
        grid.Children.Add(Ui.Cell(MeterWindow.Clock(fight.Seconds)[3..], 10.5, Ui.Title, 2, true));
        grid.Children.Add(Ui.Cell(MeterWindow.Short(fight.Damage / seconds), 10.5, Ui.Title, 3, true));
        grid.Children.Add(Ui.Cell(fight.Players.FirstOrDefault(p => p.You) is { } you ? MeterWindow.Short(you.Damage / seconds) : "–", 10.5, Ui.Accent, 4, true));
        if (fight.Killed)
            grid.Children.Add(Ui.Cell("✓", 10.5, Ui.Accent, 5));
        return grid;
    }

    void Open(FightRecord fight)
    {
        listing = false;
        (bosses.IsVisible, everything.IsVisible, back.IsVisible) = (false, false, true);
        title.Text = $"{(fight.Target.Length > 0 ? fight.Target : "Unknown")} · {MeterWindow.Clock(fight.Seconds)[3..]}{(fight.Killed ? " · killed" : "")}";
        rows.Children.Clear();
        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions(PlayerColumns), Height = 18 };
        foreach (var (text, column, right) in new[] { ("PLAYER", 1, false), ("DPS", 2, true), ("TOTAL", 3, true), ("%", 4, true) })
            heading.Children.Add(Ui.Cell(text, 9, Ui.Muted, column, right));
        rows.Children.Add(heading);
        foreach (var player in fight.Players.OrderByDescending(p => p.Damage))
        {
            var color = Ui.ClassColor(player.Class);
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(PlayerColumns), Height = 24, Background = new SolidColorBrush(color, player.You ? 0.16 : 0.07) };
            var logo = Ui.Logo(player.Class, 18);
            Grid.SetColumn(logo, 0);
            grid.Children.Add(logo);
            grid.Children.Add(Ui.Cell($"{player.Name ?? (player.You ? "You" : "Player " + player.Actor)} {Ui.PowerText(player.CombatPower)}", 11, Ui.Title, 1, bold: player.You));
            grid.Children.Add(Ui.Cell(MeterWindow.Short(player.Damage / Math.Max(1, fight.Seconds)), 11, new SolidColorBrush(color), 2, true, true));
            grid.Children.Add(Ui.Cell(MeterWindow.Short(player.Damage), 10.5, Ui.Title, 3, true));
            grid.Children.Add(Ui.Cell(player.Share.ToString("P0", CultureInfo.InvariantCulture), 10.5, Ui.Title, 4, true));
            grid.Children.Add(Ui.ShareBar(player.Share, color, 5));
            rows.Children.Add(Clickable(grid, () =>
            {
                breakdown?.Close();
                breakdown = new BreakdownWindow(player, fight.Seconds);
                breakdown.Closed += (_, _) => breakdown = null;
                breakdown.Show();
            }));
        }
    }

    static Border Clickable(Grid grid, Action action)
    {
        var row = new Border { Child = grid, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 0, 1), Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand) };
        row.PointerEntered += (_, _) => row.Background = Ui.Line;
        row.PointerExited += (_, _) => row.Background = Brushes.Transparent;
        row.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.Left)
                action();
        };
        return row;
    }
}
