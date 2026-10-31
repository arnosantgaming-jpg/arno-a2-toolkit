using System.Globalization;
using ArnoA2.Meter;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace ArnoA2.App.Dps;

/// <summary>One player's damage skill by skill. Follows the meter while the fight runs;
/// a fight from the history stays as it was.</summary>
public sealed class BreakdownWindow : Window
{
    const string Columns = "2.4*,38,44,54,44,*";
    readonly Func<FightPlayer?> player;
    readonly Func<DamageMeter.Tally>? live;
    readonly double? seconds;
    readonly StackPanel rows = new();
    readonly TextBlock summary = new() { FontSize = 11, Foreground = Ui.Muted, Margin = new Thickness(0, 2, 0, 6) };
    readonly TextBlock title = new() { FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = Ui.Title };
    string rendered = "";

    public BreakdownWindow(AppState app, Func<FightPlayer?> player, Func<DamageMeter.Tally> live)
        : this(player)
    {
        this.live = live;
        app.Changed += Refresh;
        Closed += (_, _) => app.Changed -= Refresh;
        Refresh();
    }

    public BreakdownWindow(FightPlayer player, double seconds)
        : this(() => player)
    {
        this.seconds = seconds;
        Refresh();
    }

    BreakdownWindow(Func<FightPlayer?> player)
    {
        this.player = player;
        Title = "Arno's A2 Toolkit · skills";
        Icon = ToolTheme.AppIcon;
        Width = 440;
        Height = 360;
        MinWidth = 340;
        MinHeight = 160;
        Topmost = true;
        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions(Columns), Height = 18 };
        foreach (var (text, column, right) in new[] { ("SKILL", 0, false), ("HITS", 1, true), ("CRIT", 2, true), ("TOTAL", 3, true), ("%", 4, true) })
            heading.Children.Add(Ui.Cell(text, 9, Ui.Muted, column, right));
        var top = new StackPanel { Children = { title, summary, heading } };
        DockPanel.SetDock(top, Dock.Top);
        ToolTheme.Frame(this, "SKILLS", ToolTheme.Meter, new DockPanel { Margin = new Thickness(10, 0, 10, 10), Children = { top, new ScrollViewer { Content = rows } } }, Ui.Window, Ui.Line);
    }

    void Refresh()
    {
        if (player() is not { } shown)
            return;
        double time = Math.Max(1, seconds ?? live?.Invoke().Seconds ?? 1);
        string key = $"{shown.Name}|{shown.Damage}|{time:F0}";
        if (key == rendered)
            return;
        rendered = key;
        title.Text = $"{shown.Name ?? (shown.You ? "You" : "Player " + shown.Actor)} {Ui.PowerText(shown.CombatPower)}";
        title.Foreground = new SolidColorBrush(Ui.ClassColor(shown.Class));
        summary.Text = $"{MeterWindow.Short(shown.Damage / time)} DPS · {MeterWindow.Short(shown.Damage)} damage · {shown.Hits:N0} hits · biggest {MeterWindow.Short(shown.Biggest)} · {MeterWindow.Clock(time)}";
        rows.Children.Clear();
        var color = Ui.ClassColor(shown.Class);
        foreach (var skill in shown.Skills)
        {
            double share = skill.Damage / Math.Max(1d, shown.Damage);
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(Columns), Height = 22 };
            grid.Children.Add(Ui.Cell(skill.Name, 11, Ui.Title, 0));
            grid.Children.Add(Ui.Cell(skill.Hits.ToString("N0", CultureInfo.InvariantCulture), 10.5, Ui.Title, 1, true));
            grid.Children.Add(Ui.Cell((skill.Crits / Math.Max(1d, skill.Hits)).ToString("P0", CultureInfo.InvariantCulture), 10.5, Ui.Title, 2, true));
            grid.Children.Add(Ui.Cell(MeterWindow.Short(skill.Damage), 10.5, Ui.Title, 3, true));
            grid.Children.Add(Ui.Cell(share.ToString("P0", CultureInfo.InvariantCulture), 10.5, Ui.Title, 4, true));
            grid.Children.Add(Ui.ShareBar(share, color, 5));
            ToolTip.SetTip(grid, $"{skill.Damage:N0} damage · {skill.Damage / time:N0}/s · biggest {skill.Biggest:N0}");
            rows.Children.Add(new Border { Child = grid, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 0, 1) });
        }
    }
}
