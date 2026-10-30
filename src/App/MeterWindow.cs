using System.Globalization;
using ArnoA2.Meter;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace ArnoA2.App;

/// <summary>The always-on-top damage meter: this fight or the whole session, per character.</summary>
public sealed class MeterWindow : Window
{
    readonly AppState app;
    readonly StackPanel rows = new() { Spacing = 6 };
    readonly TextBlock summary = new() { FontSize = 12, Foreground = Ui.Muted, TextWrapping = TextWrapping.Wrap };
    readonly Button fight, session;
    SettingsWindow? settings;
    bool showSession;
    string rendered = "";

    public MeterWindow(AppState app)
    {
        this.app = app;
        Title = KdeWindowRules.MeterTitle;
        Width = 320;
        Height = 240;
        MinWidth = 240;
        MinHeight = 140;
        Topmost = true;
        Background = Ui.Window;

        fight = Ui.Button("Fight", () => Show(false), "This fight, or the last one until the next starts");
        session = Ui.Button("Session", () => Show(true), "Every fight since the last reset");
        var modes = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        modes.Children.Add(fight);
        modes.Children.Add(session);
        modes.Children.Add(Ui.Button("Reset", () =>
        {
            app.Engine.Damage.Reset();
            Refresh();
        }));
        modes.Children.Add(Ui.Button("⚙", OpenSettings, "Settings"));

        var header = new StackPanel { Spacing = 8 };
        header.Children.Add(modes);
        header.Children.Add(summary);
        var layout = new DockPanel { Margin = new Thickness(12) };
        DockPanel.SetDock(header, Dock.Top);
        layout.Children.Add(header);
        layout.Children.Add(new ScrollViewer { Content = rows, Margin = new Thickness(0, 8, 0, 0) });
        Content = layout;

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
        var list = tally.Rows(app.Engine.LocalEntity);
        fight.FontWeight = showSession ? FontWeight.Normal : FontWeight.Bold;
        session.FontWeight = showSession ? FontWeight.Bold : FontWeight.Normal;
        fight.BorderBrush = showSession ? null : Ui.Gold;
        session.BorderBrush = showSession ? Ui.Gold : null;

        string key = $"{showSession}|{tally.Seconds:F1}|{tally.Incomplete}|{Idle()}|" + string.Join("|", list.Select(r => $"{r.Actor}:{r.You}:{r.Damage}"));
        if (key == rendered)
            return;
        rendered = key;
        summary.Text = tally.Empty ? "" : $"{Clock(tally.Seconds)} in combat · {Short(tally.Damage)} damage{(tally.Incomplete ? " · incomplete (data lost)" : "")}";
        rows.Children.Clear();
        foreach (var row in list)
            rows.Children.Add(Row(row, list[0].Damage));
        if (list.Count == 0)
            rows.Children.Add(new TextBlock { Text = Idle(), Foreground = Ui.Muted, TextWrapping = TextWrapping.Wrap });
    }

    string Idle() => app.CaptureProblem ? app.CaptureStatus : app.Engine.Connection == Connection.Waiting ? app.CaptureStatus : "Damage appears here when a fight starts.";

    static Control Row(MeterRow row, long top)
    {
        var text = new DockPanel();
        var rate = new TextBlock { Text = $"{Short(row.Dps)}/s · {row.Share:P0}", FontSize = 12, Foreground = Ui.Muted };
        DockPanel.SetDock(rate, Dock.Right);
        text.Children.Add(rate);
        text.Children.Add(new TextBlock { Text = row.You ? "You" : "Player " + row.Actor, FontSize = 12.5, FontWeight = row.You ? FontWeight.SemiBold : FontWeight.Normal, Foreground = row.You ? Ui.Bright : Brushes.White });

        var bar = new Grid { ColumnDefinitions = new ColumnDefinitions($"{Math.Max(row.Damage, 1)}*,{Math.Max(top - row.Damage, 0)}*"), Height = 5 };
        bar.Children.Add(new Border { Background = row.You ? Ui.Gold : Ui.Muted, CornerRadius = new CornerRadius(2) });
        var panel = new StackPanel { Spacing = 3 };
        panel.Children.Add(text);
        panel.Children.Add(new Border { Background = Ui.Track, CornerRadius = new CornerRadius(2), Child = bar });
        ToolTip.SetTip(panel, $"{row.Damage:N0} damage · {row.Hits:N0} hits · biggest {row.Biggest:N0}");
        return panel;
    }

    static string Clock(double seconds) => $"{(int)seconds / 60}:{(int)seconds % 60:00}";

    static string Short(double value) => value switch
    {
        >= 1_000_000 => (value / 1_000_000).ToString("0.0", CultureInfo.InvariantCulture) + "M",
        >= 10_000 => (value / 1_000).ToString("0.0", CultureInfo.InvariantCulture) + "k",
        _ => value.ToString("N0", CultureInfo.InvariantCulture)
    };
}
